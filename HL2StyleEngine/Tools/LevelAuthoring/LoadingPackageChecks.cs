using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine.Core.Diagnostics;
using Engine.Editor.Level;
using Engine.Render;
using Game;

internal static class LoadingPackageChecks
{
    public static string Build(string root, string levelFile = "atmosphereTest.json")
    {
        var clock = Stopwatch.StartNew();
        if (Path.GetFileName(levelFile) != levelFile || !levelFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a level JSON filename, not an arbitrary path.");
        string game = Path.Combine(root, "Game"), level = Path.Combine(game, "Content/Levels", levelFile);
        string levelHash = Hash(level);
        var dependencies = HL2GameModule.PackageModelDependencies(LevelIO.Load(level)).ToArray();
        var hashes = dependencies.ToDictionary(p => p, p => Hash(AssetPackage.Resolve(game, p)), StringComparer.OrdinalIgnoreCase);
        string[] sidecars = Directory.GetFiles(Path.GetDirectoryName(level)!, Path.GetFileNameWithoutExtension(levelFile) + ".*")
            .Where(p => Path.GetExtension(p) is ".hs2reflection" or ".hs2lightmap").Order().ToArray();
        string identity = $"{AssetPackage.Version}/{CookedModelCache.FormatVersion}/{levelFile}/{levelHash}\n" +
            string.Join('\n', hashes.Select(p => p.Key + ":" + p.Value)) + string.Join('\n', sidecars.Select(p => Path.GetFileName(p) + ":" + Hash(p)));
        string output = Path.Combine(game, "bin/Release/net10.0/LoadingTestPackages", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]);
        Directory.CreateDirectory(output);
        string manifest = Path.Combine(output, "assets.json");
        string levelRelative = "Content/Levels/" + levelFile;
        Copy(level, AssetPackage.Resolve(output, levelRelative));
        foreach (string file in sidecars) Copy(file, AssetPackage.Resolve(output, "Content/Levels/" + Path.GetFileName(file)));
        var entries = new List<PackagedModel>();
        foreach (string relative in dependencies)
        {
            string source = AssetPackage.Resolve(game, relative), destination = AssetPackage.Resolve(output, relative);
            CookedModelCache.Cook(source);
            var model = CookedModelCache.Load(source);
            Copy(source, destination);
            File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
            string hash = hashes[relative];
            // Detect a source edit during packaging instead of publishing inconsistent metadata.
            if (Hash(destination) != hash || Hash(source) != hash) throw new IOException("Source changed during packaging: " + relative);
            Copy(CookedModelCache.PathForHash(source, Convert.FromHexString(hash)), CookedModelCache.PathForHash(destination, Convert.FromHexString(hash)));
            string[] textures = model.Parts.SelectMany(p => new[] { p.BaseColorTexture, p.MaterialTexture, p.NormalTexture, p.EmissiveTexture })
                .Where(t => t != null).Select(t => t!.Key).Distinct().Order().ToArray();
            foreach (string key in textures)
                Copy(Path.Combine(TextureCooker.CacheDirectory(source), key + ".hs2tex"), Path.Combine(TextureCooker.CacheDirectory(destination), key + ".hs2tex"));
            var info = new FileInfo(destination);
            entries.Add(new(relative, hash, info.Length, info.LastWriteTimeUtc.Ticks, textures));
        }
        if (Hash(level) != levelHash || Hash(AssetPackage.Resolve(output, levelRelative)) != levelHash)
            throw new IOException("Level changed during packaging; retry after saving.");
        var data = new AssetPackageManifest(AssetPackage.Version, CookedModelCache.FormatVersion, levelRelative, levelHash, entries.ToArray());
        string temporary = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, manifest, true);
        _ = new AssetPackage(manifest);
        Console.WriteLine($"[Package] {entries.Count} models, {entries.SelectMany(e => e.Textures).Distinct().Count()} shared textures; {entries.Sum(e => e.Length) / 1048576.0:F1} MiB GLB fallback sources.");
        Console.WriteLine($"[Package] Preparation {clock.Elapsed.TotalSeconds:F2}s (not runtime loading): {manifest}");
        return manifest;
    }

    public static void Launch(string root, bool source, bool sourceLighting = false)
    {
        string manifest = Build(root), folder = Path.GetDirectoryName(manifest)!;
        string report = Path.Combine(folder, source ? "last-source-load.json" : sourceLighting ? "last-source-lighting-load.json" : "last-package-load.json");
        int code = RunGame(root, manifest, report, source, probe: false, sourceLighting);
        if (code != 0) throw new InvalidOperationException("Game exited with code " + code);
    }

    public static void Benchmark(string root, bool lightingOnly = false)
    {
        Console.WriteLine(lightingOnly ? "[Probe] Comparing package models + strict lighting against package models + package lighting identities." : "[Probe] Comparing all strict source loading against package models + package lighting identities.");
        string manifest = Build(root);
        string output = Path.Combine(Path.GetDirectoryName(manifest)!, "qa", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        // Discard one run per mode to warm generated collision/structured-data caches equally.
        for (int i = 0; i < 8; i++)
        {
            bool source = i % 4 is 0 or 3;
            string path = Path.Combine(output, $"{i:00}-{(source ? "source" : "package")}.json");
            if (RunGame(root, manifest, path, source && !lightingOnly, probe: true, sourceLighting: lightingOnly && source) != 0) throw new InvalidOperationException("Load probe failed: " + path);
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.GetProperty("Outcome").GetString() != "first-playable-presented") throw new InvalidOperationException("Level was not ready: " + path);
            Console.WriteLine($"[Probe] {(i < 2 ? "warmup" : "sample")} {Path.GetFileName(path)}: {json.RootElement.GetProperty("ProcessMilliseconds").GetDouble():F1} ms");
        }
        Console.WriteLine("[Probe] Fresh processes, warmed OS file cache; NOT cold disk-cache measurements. Reports: " + output);
    }

    public static void PrepareProbe(string root)
    {
        string manifest = Build(root);
        string app = Path.Combine(root, "Game/bin/Release/net10.0");
        string pointer = Path.Combine(app, "loading-probe.json");
        File.WriteAllText(pointer, JsonSerializer.Serialize(new
        {
            Version = 1, PreparedUtc = DateTime.UtcNow,
            Manifest = Path.GetRelativePath(app, manifest),
            Level = Path.GetRelativePath(app, Path.Combine(Path.GetDirectoryName(manifest)!, "Content/Levels/atmosphereTest.json"))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[Probe] Prepared without launching the game: " + pointer);
        Console.WriteLine("[Probe] RunLoadingProbe.bat performs no build/cook/copy. After a manual restart, add -ColdCandidate; this label does not verify Windows cache state.");
    }

    private static int RunGame(string root, string manifest, string report, bool source, bool probe, bool sourceLighting = false)
    {
        var start = new ProcessStartInfo(Path.Combine(root, ".dotnet/dotnet.exe")) { UseShellExecute = false, WorkingDirectory = Path.Combine(root, "Game/bin/Release/net10.0") };
        foreach (string arg in new[] { Path.Combine(start.WorkingDirectory, "Game.dll"), "--level", Path.Combine(Path.GetDirectoryName(manifest)!, "Content/Levels/atmosphereTest.json"),
            "--asset-package", manifest, "--load-report", report }) start.ArgumentList.Add(arg);
        if (source) start.ArgumentList.Add("--source-assets");
        if (sourceLighting) start.ArgumentList.Add("--source-lighting");
        if (probe) start.ArgumentList.Add("--load-probe");
        start.Environment["HS2_LOAD_CACHE_LABEL"] = "Fresh process; package creation/previous runs warmed OS file cache";
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Game did not start.");
        if (probe && !child.WaitForExit(90000))
        { child.Kill(true); child.WaitForExit(); throw new TimeoutException("Load probe exceeded 90 seconds."); }
        child.WaitForExit();
        return child.ExitCode;
    }

    public static void FailureProbes(string root)
    {
        string manifest = Build(root);
        var package = new AssetPackage(manifest);
        string invalid = Path.Combine(package.Root, "invalid-manifest-test.json");
        File.WriteAllText(invalid, "{");
        string report = Path.Combine(package.Root, "invalid-manifest-report.json");
        if (RunGame(root, invalid, report, source: false, probe: true) != 0) throw new InvalidOperationException("Invalid manifest did not fall back to source mode.");
        using (var json = JsonDocument.Parse(File.ReadAllBytes(report)))
            if (json.RootElement.GetProperty("Detail").GetProperty("Diagnostics").GetProperty("Mode").GetString() != "strict-source")
                throw new InvalidOperationException("Invalid manifest used package mode.");
        string missing = AssetPackage.Resolve(package.Root, package.Manifest.Models.First(m => m.Path.EndsWith("Future_Soldier_02.glb")).Path);
        string held = missing + ".failure-probe";
        // The checked, generated snapshot only; restore even when the probe fails.
        File.Move(missing, held);
        try
        {
            report = Path.Combine(package.Root, "missing-required-report.json");
            if (RunGame(root, manifest, report, source: false, probe: true) == 0) throw new InvalidOperationException("Missing required model incorrectly reported success.");
            using var json = JsonDocument.Parse(File.ReadAllBytes(report));
            if (json.RootElement.GetProperty("Outcome").GetString() != "required-assets-failed" ||
                json.RootElement.GetProperty("Detail").GetProperty("FailedModels").GetInt32() == 0)
                throw new InvalidOperationException("Missing asset was not diagnosed.");
        }
        finally { File.Move(held, missing); }
        Console.WriteLine("[Loading] PASS: malformed manifest uses strict source loading; missing required model exits failed with an explicit report.");
    }

    public static void Validate(string root)
    {
        string manifest = Build(root);
        var package = new AssetPackage(manifest);
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); checks++; }
        foreach (var entry in package.Manifest.Models)
        {
            string path = AssetPackage.Resolve(package.Root, entry.Path);
            ModelCookingChecks.Equivalent(CookedModelCache.Load(path), package.Load(path));
            checks++;
        }
        Check(package.Hits == package.Manifest.Models.Length && package.Fallbacks == 0, "Healthy package did not exclusively hit prepared data.");
        // Corruption tests only modify a disposable generated fixture, never source/project assets.
        string test = Path.Combine(root, "Tools/LevelAuthoring/bin/Release/net10.0/qa", "package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(test);
        var original = package.Manifest.Models.First(e => e.Path.EndsWith("WoodenCratePbr.glb", StringComparison.OrdinalIgnoreCase));
        string modelPath = AssetPackage.Resolve(test, original.Path);
        Copy(AssetPackage.Resolve(package.Root, original.Path), modelPath);
        File.SetAttributes(modelPath, FileAttributes.Normal);
        string cached = CookedModelCache.PathForHash(modelPath, Convert.FromHexString(original.SourceHash));
        string originalCache = CookedModelCache.PathForHash(AssetPackage.Resolve(package.Root, original.Path), Convert.FromHexString(original.SourceHash));
        Copy(originalCache, cached);
        foreach (string key in original.Textures)
            Copy(Path.Combine(TextureCooker.CacheDirectory(AssetPackage.Resolve(package.Root, original.Path)), key + ".hs2tex"),
                Path.Combine(TextureCooker.CacheDirectory(modelPath), key + ".hs2tex"));
        string testLevel = AssetPackage.Resolve(test, "Content/Levels/test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(testLevel)!);
        File.WriteAllText(testLevel, "{}");
        var info = new FileInfo(modelPath);
        var entryCopy = original with { LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
        var data = new AssetPackageManifest(AssetPackage.Version, CookedModelCache.FormatVersion,
            "Content/Levels/test.json", Hash(testLevel), [entryCopy]);
        string testManifest = Path.Combine(test, "assets.json");
        void Write(AssetPackageManifest value) => File.WriteAllText(testManifest, JsonSerializer.Serialize(value));
        Write(data);
        var moved = new AssetPackage(testManifest);
        var reference = CookedModelCache.Load(modelPath);
        string report = Path.Combine(test, "profile.json");
        var profile = new LoadProfile(report);
        ModelCookingChecks.Equivalent(reference, moved.Load(modelPath, profile));
        profile.Finish("first-playable-presented", "CPU telemetry serialization test only; not a game timing");
        using (var json = JsonDocument.Parse(File.ReadAllBytes(report)))
            Check(!json.RootElement.GetProperty("Stages").TryGetProperty("model-source-read", out _), "Healthy prepared load read GLB source bytes.");
        Check(moved.Hits == 1, "Relative package failed after relocation.");
        void Fallback(string reason, bool equivalent = true)
        {
            var broken = new AssetPackage(testManifest);
            var result = broken.Load(modelPath);
            Check(broken.Fallbacks == 1 && broken.Hits == 0, reason);
            if (equivalent) ModelCookingChecks.Equivalent(reference, result);
        }
        byte[] bytes = File.ReadAllBytes(cached);
        try
        {
            File.WriteAllBytes(cached, bytes[..20]); Fallback("Truncated model did not fall back.");
            var corrupt = (byte[])bytes.Clone(); corrupt[^1] ^= 0x5A;
            File.WriteAllBytes(cached, corrupt); Fallback("Corrupt model did not fall back.");
            corrupt = (byte[])bytes.Clone(); corrupt[4] ^= 0x7F;
            File.WriteAllBytes(cached, corrupt); Fallback("Unknown model version did not fall back.");
            File.Move(cached, cached + ".held"); Fallback("Missing model did not fall back.");
            File.Move(cached + ".held", cached);
        }
        finally { File.WriteAllBytes(cached, bytes); }
        File.SetLastWriteTimeUtc(modelPath, info.LastWriteTimeUtc.AddSeconds(2));
        Fallback("Changed source metadata did not force strict validation.");
        File.SetLastWriteTimeUtc(modelPath, info.LastWriteTimeUtc);
        Write(data with { Models = [entryCopy with { SourceHash = new string('0', 64) }] });
        Fallback("Wrong source identity did not fall back."); Write(data);
        Write(data with { Models = [entryCopy with { Textures = [] }] });
        Fallback("Undeclared texture was accepted."); Write(data);
        string texture = Path.Combine(TextureCooker.CacheDirectory(modelPath), original.Textures[0] + ".hs2tex");
        byte[] textureBytes = File.ReadAllBytes(texture);
        try
        {
            File.WriteAllBytes(texture, [0, 1, 2]); Fallback("Corrupt live-memory texture dependency was not checked.", false);
            File.Move(texture, texture + ".held"); Fallback("Missing live-memory texture dependency was not checked.", false);
            var start = new ProcessStartInfo(Path.Combine(root, ".dotnet/dotnet.exe")) { UseShellExecute = false };
            foreach (string arg in new[] { typeof(LoadingPackageChecks).Assembly.Location, "check-package-fallback-child", testManifest, modelPath }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(30000)) { child.Kill(true); child.WaitForExit(); throw new TimeoutException("Fallback child timed out."); }
            Check(child.ExitCode == 0, "Fresh-process missing texture fallback failed.");
            File.Move(texture + ".held", texture);
        }
        finally { File.WriteAllBytes(texture, textureBytes); }
        CookedModelCache.Enabled = false;
        try { Fallback("Source reference switch did not bypass package data."); }
        finally { CookedModelCache.Enabled = true; }
        void Rejected(AssetPackageManifest value)
        {
            Write(value); bool rejected = false;
            try { _ = new AssetPackage(testManifest); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid manifest accepted."); Write(data);
        }
        Rejected(data with { Version = 99 });
        Rejected(data with { ModelVersion = 99 });
        Rejected(data with { LevelHash = new string('0', 64) });
        Rejected(data with { Models = [entryCopy, entryCopy] });
        Rejected(data with { Models = [entryCopy with { Path = "../outside.glb" }] });
        Rejected(data with { Models = [entryCopy with { Path = "C:/outside.glb" }] });
        Rejected(data with { Models = [entryCopy with { Textures = ["../outside"] }] });
        File.WriteAllText(testManifest, "{");
        bool malformed = false;
        try { _ = new AssetPackage(testManifest); } catch (JsonException) { malformed = true; }
        Check(malformed, "Malformed manifest accepted."); Write(data);
        Check(Hash(modelPath) == original.SourceHash, "Failure tests modified source geometry.");
        Console.WriteLine($"[Package] PASS {checks} checks including all-model equivalence, relocation, telemetry, fresh-process fallback and invalid metadata.");
        Console.WriteLine("[Package] Generated failure fixtures retained for inspection: " + test);
    }

    public static void FallbackChild(string manifest, string model)
    {
        var package = new AssetPackage(manifest);
        var loaded = package.Load(model);
        if (package.Fallbacks != 1 || loaded.Parts.Count == 0 || TextureCooker.FallbackDecodes == 0)
            throw new InvalidOperationException("Missing dependency did not decode source in fresh process.");
    }

    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && Hash(source) == Hash(destination)) return;
        if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal);
        File.Copy(source, destination, true);
    }
}

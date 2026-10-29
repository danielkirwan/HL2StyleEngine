using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine.Core.Assets;
using Engine.Core.Diagnostics;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;

internal static class LightingDependencyChecks
{
    public static void Run(string root)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new InvalidDataException(reason); checks++; }
        string manifest = LoadingPackageChecks.Build(root);
        var package = new AssetPackage(manifest);
        string output = Path.Combine(root, "Tools/LevelAuthoring/bin/Release/net10.0/qa", "lighting-dependencies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var profile = new LoadProfile(Path.Combine(output, "cpu-validation.json"));
        var fast = new AssetFingerprintReader(profile, package.TryGetModelIdentity);
        var strict = new AssetFingerprintReader(profile);
        string Resolve(string p) => AssetPackage.Resolve(package.Root, p);
        var level = LevelIO.Load(package.LevelPath);
        ReflectionEnvironmentData? reference = null;
        foreach (var probe in level.Entities.Where(e => e.Type == EntityTypes.ReflectionProbe))
        {
            var snapshot = ReflectionSnapshot.Create(level, package.LevelPath, probe.Id, Resolve);
            string key = snapshot.Key(fingerprints: strict);
            Check(key == snapshot.Key(fingerprints: fast), "Packaged reflection key changed: " + probe.Name);
            reference = ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(package.LevelPath, probe.Id), key);
            Check(reference != null, "Existing reflection no longer validates: " + probe.Name);
        }
        Check(fast.IdentityHits > 0 && fast.SourceBytes < strict.SourceBytes, "No lighting dependency reads were avoided.");
        var diffuse = BakeSnapshot.Capture(level, Resolve);
        Check(diffuse.Key() == diffuse.Key(fingerprints: fast), "Diffuse key changed.");

        // Exercise a real diffuse/probe bake as well as the reflection-only playtest.
        var bakedPackage = new AssetPackage(LoadingPackageChecks.Build(root, "lightProbeTest.json"));
        var baked = BakeSnapshot.Capture(LevelIO.Load(bakedPackage.LevelPath), p => AssetPackage.Resolve(bakedPackage.Root, p));
        string bakedKey = baked.Key();
        Check(bakedKey == baked.Key(fingerprints: new AssetFingerprintReader(null, bakedPackage.TryGetModelIdentity)), "Real probe bake fingerprint changed.");
        Check(LightmapFile.Load(LightmapFile.PathFor(bakedPackage.LevelPath), bakedKey)?.Probes != null, "Real probe bake did not load.");

        // Failures use tiny disposable fixture files; project GLBs/shaders/caches are untouched.
        string source = Path.Combine(output, "Content/Models/probe.glb"), shader = Path.Combine(output, "shader.cso");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        byte[] original = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();
        File.WriteAllBytes(source, original); File.WriteAllBytes(shader, [4, 5, 6]);
        string testLevel = Path.Combine(output, "level.json"); File.WriteAllText(testLevel, "{}");
        var info = new FileInfo(source);
        var data = new AssetPackageManifest(AssetPackage.Version, CookedModelCache.FormatVersion, "level.json", Hash(testLevel),
            [new("Content/Models/probe.glb", Hash(source), info.Length, info.LastWriteTimeUtc.Ticks, [])]);
        string testManifest = Path.Combine(output, "assets.json");
        File.WriteAllText(testManifest, JsonSerializer.Serialize(data));
        var fixture = new AssetPackage(testManifest);
        var optimized = new AssetFingerprintReader(profile, fixture.TryGetModelIdentity);
        var paths = new Dictionary<string, string> { ["mesh"] = source };
        var reflection = new ReflectionSnapshot
        {
            Level = new(), LevelPath = testLevel, Probe = new(), Position = Vector3.Zero, Json = Encoding.UTF8.GetBytes("{}"),
            ModelPaths = paths, Dependencies = new(paths) { ["shader"] = shader }, FixedLightIds = []
        };
        var bake = new BakeSnapshot(Encoding.UTF8.GetBytes("{}"), paths);
        string baseline = reflection.Key(), bakeKey = bake.Key();
        Check(baseline == reflection.Key(fingerprints: optimized) && bakeKey == bake.Key(fingerprints: optimized), "Healthy metadata keys differ.");
        string cache = Path.Combine(output, "test.hs2reflection");
        ReflectionProbeCache.Save(cache, baseline, reference!);
        string lightmap = Path.Combine(output, "test.hs2lightmap");
        LightmapFile.Save(lightmap, new LightmapData
        {
            Key = bakeKey, Size = 32, Texels = 4, Pixels = new byte[32 * 32 * 8], LightIds = ["fixture-light"],
            Records = [new() { EntityId = "fixture-model", PartTriangles = [1], Transform = Matrix4x4.Identity }]
        });
        Check(ReflectionProbeCache.Load(cache, reflection.Key(fingerprints: optimized)) != null, "Healthy reflection rejected.");
        Check(LightmapFile.Load(lightmap, bake.Key(fingerprints: optimized)) != null, "Healthy diffuse rejected.");
        File.WriteAllBytes(source, [7, 8, 9]);
        Check(fixture.TryGetModelIdentity(source) == null, "Changed model metadata still trusted.");
        Check(reflection.Key() == reflection.Key(fingerprints: optimized), "Changed model did not use source hash.");
        Check(ReflectionProbeCache.Load(cache, reflection.Key(fingerprints: optimized)) == null, "Stale reflection accepted.");
        Check(LightmapFile.Load(lightmap, bake.Key(fingerprints: optimized)) == null, "Stale diffuse accepted.");
        File.WriteAllBytes(source, original); File.SetLastWriteTimeUtc(source, info.LastWriteTimeUtc);
        DateTime shaderTime = File.GetLastWriteTimeUtc(shader);
        File.WriteAllBytes(shader, [4, 5, 9]); File.SetLastWriteTimeUtc(shader, shaderTime);
        Check(reflection.Key(fingerprints: optimized) != baseline, "Same-size/time shader edit escaped validation.");
        Check(reflection.Key() == reflection.Key(fingerprints: optimized), "Shader fingerprint differs from strict mode.");
        File.WriteAllBytes(shader, [4, 5, 6]);
        byte[] edited = (byte[])original.Clone(); edited[0] ^= 1;
        File.WriteAllBytes(source, edited); File.SetLastWriteTimeUtc(source, info.LastWriteTimeUtc);
        Check(bake.Key() != bakeKey && bake.Key(fingerprints: strict) != bakeKey, "Authoring missed same-size/time source edit.");
        File.WriteAllBytes(source, original); File.SetLastWriteTimeUtc(source, info.LastWriteTimeUtc);
        File.Move(source, source + ".held");
        try
        {
            Check(fixture.TryGetModelIdentity(source) == null, "Missing model still trusted.");
            bool failed = false;
            try { reflection.Key(fingerprints: optimized); } catch (FileNotFoundException) { failed = true; }
            Check(failed, "Missing dependency did not fail validation.");
        }
        finally { File.Move(source + ".held", source); }
        bool cancelled = false;
        try { reflection.Key(new CancellationToken(true), optimized); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Cancelled validation did not stop.");
        // JSON/state/placement remains part of the key even when model identities are shared.
        reflection.Json[0] = (byte)' ';
        Check(reflection.Key(fingerprints: optimized) != baseline, "Changed scene metadata did not invalidate lighting.");
        reflection.Json[0] = (byte)'{';
        foreach (string payload in new[] { cache, lightmap })
        {
            byte[] valid = File.ReadAllBytes(payload);
            bool Invalid() => payload == cache ? ReflectionProbeCache.Load(payload, baseline) == null : LightmapFile.Load(payload, bakeKey) == null;
            File.WriteAllBytes(payload, valid[..12]); Check(Invalid(), "Truncated lighting payload accepted.");
            var corrupt = (byte[])valid.Clone(); corrupt[^1] ^= 127;
            File.WriteAllBytes(payload, corrupt); Check(Invalid(), "Corrupt lighting payload accepted.");
            corrupt = (byte[])valid.Clone(); corrupt[4] = 99;
            File.WriteAllBytes(payload, corrupt); Check(Invalid(), "Wrong lighting cache version accepted.");
            File.Move(payload, payload + ".held"); Check(Invalid(), "Missing lighting payload accepted.");
            File.Move(payload + ".held", payload); File.WriteAllBytes(payload, valid);
        }
        AssetFingerprintReader.ReadHash(source, "collision-dependency", strict);
        profile.Finish("cpu-validation-complete", new { Checks = checks, fast.SourceBytes, strictReads = strict.SourceBytes, fast.IdentityHits });
        Console.WriteLine($"PASS: {checks} lighting dependency checks; exact real reflection/diffuse keys, stale/missing/corrupt fallbacks, strict authoring/shader validation and cancellation. {output}");
    }

    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}

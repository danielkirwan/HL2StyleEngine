using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Engine.Editor.Level;
using Engine.Render;
using K4os.Compression.LZ4;

internal static class ModelCookingChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }

    public static void Run(string root)
    {
        string[] paths = Directory.GetFiles(Path.Combine(root, "Game/Content/Models"), "*.glb", SearchOption.AllDirectories).Order().ToArray();
        int parts = 0;
        foreach (string path in paths)
        {
            LoadedModel reference = GlbModelLoader.Load(path);
            TextureCooker.Prepare(reference, TextureCooker.CacheDirectory(path), false);
            long before = CookedModelCache.CacheHits;
            LoadedModel cooked = TextureCooker.LoadForRendering(path);
            Require(CookedModelCache.CacheHits == before + 1, "Missing model cache: " + path);
            Equivalent(reference, cooked);
            parts += cooked.Parts.Count;
        }
        Console.WriteLine($"PASS: {paths.Length} GLBs / {parts} parts: bit-exact positions, normals, UVs, indices, part/node identities, material factors and prepared texture mips.");
        CheckFailures(root);
    }

    public static void CheckChild(string path, bool expectHit)
    {
        long before = CookedModelCache.CacheHits;
        var loaded = TextureCooker.LoadForRendering(path);
        Require((CookedModelCache.CacheHits > before) == expectHit, "Wrong fresh-process cache/fallback path.");
        Require(loaded.Parts.Count > 0 && loaded.Parts.Any(p => p.BaseColorTexture != null), "Model/texture was lost on load.");
        Console.WriteLine(expectHit ? "PASS: cooked load" : "PASS: source fallback");
    }

    private static void CheckFailures(string root)
    {
        string source = Path.Combine(root, "Game/Content/Models/ViewModels/Breakable_Wooden_Crate.glb");
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(source));
        string directory = Path.Combine(Path.GetTempPath(), "hs2-model-cache-" + Guid.NewGuid().ToString("N"));
        string modelPath = Path.Combine(directory, "Content/Models/crate.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        try
        {
            File.Copy(source, modelPath);
            string textureDir = TextureCooker.CacheDirectory(modelPath);
            Directory.CreateDirectory(textureDir);
            LoadedModel original = TextureCooker.LoadForRendering(source);
            var textures = original.Parts.SelectMany(p => new[] { p.BaseColorTexture, p.MaterialTexture, p.NormalTexture })
                .OfType<PreparedTexture>().DistinctBy(t => t.Key).ToArray();
            foreach (var texture in textures)
                File.Copy(Path.Combine(TextureCooker.CacheDirectory(source), texture.Key + ".hs2tex"), Path.Combine(textureDir, texture.Key + ".hs2tex"));
            string cache = CookedModelCache.CachePath(modelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.Copy(CookedModelCache.CachePath(source), cache);
            byte[] valid = File.ReadAllBytes(cache);
            LoadChild(modelPath, true);
            File.Delete(cache);
            LoadChild(modelPath, false);
            File.WriteAllBytes(cache, valid[..12]);
            LoadChild(modelPath, false);
            byte[] changed = (byte[])valid.Clone(); changed[^1] ^= 127;
            File.WriteAllBytes(cache, changed);
            LoadChild(modelPath, false);
            changed = (byte[])valid.Clone(); changed[4] = 99;
            File.WriteAllBytes(cache, changed);
            LoadChild(modelPath, false);
            changed = (byte[])valid.Clone(); changed[8] ^= 1;
            File.WriteAllBytes(cache, changed);
            LoadChild(modelPath, false);

            // Valid checksum/compression with a malformed decoded part count must still be rejected.
            int rawLength = BitConverter.ToInt32(valid, 40);
            byte[] raw = new byte[rawLength];
            Require(LZ4Codec.Decode(valid.AsSpan(80), raw) == raw.Length, "Invalid test source cache.");
            BitConverter.GetBytes(int.MaxValue).CopyTo(raw, 0);
            byte[] packed = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
            int packedSize = LZ4Codec.Encode(raw, packed);
            using (var file = File.Create(cache))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(valid, 0, 40); writer.Write(raw.Length); writer.Write(packedSize);
                writer.Write(SHA256.HashData(raw)); writer.Write(packed, 0, packedSize);
            }
            LoadChild(modelPath, false);
            TextureCooker.CookModel(modelPath);
            LoadChild(modelPath, true);
            byte[] sourceBytes = File.ReadAllBytes(modelPath);
            DateTime sourceTime = File.GetLastWriteTimeUtc(modelPath);
            int nameOffset = sourceBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(original.Parts[0].NodeName));
            Require(nameOffset >= 20, "Fixture node name not found in GLB JSON.");
            sourceBytes[nameOffset] = sourceBytes[nameOffset] == (byte)'X' ? (byte)'Y' : (byte)'X';
            File.WriteAllBytes(modelPath, sourceBytes); File.SetLastWriteTimeUtc(modelPath, sourceTime);
            LoadChild(modelPath, false);
            TextureCooker.CookModel(modelPath);
            LoadChild(modelPath, true);

            string dependency = Path.Combine(textureDir, textures.First(t => t.Semantic == TextureSemantic.Color).Key + ".hs2tex");
            File.Delete(dependency);
            LoadChild(modelPath, false);
            File.WriteAllBytes(dependency, [1, 2, 3]);
            LoadChild(modelPath, false);
            TextureCooker.CookModel(modelPath);
            LoadChild(modelPath, true);
            Require(originalHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Source asset changed.");
            Require(!Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any(), "Atomic cache write leaked temporary files.");
            Console.WriteLine("PASS: cold-process missing/truncated/corrupt/stale/version-mismatched model and texture caches fall back; same-size/same-time source changes invalidate; recooking repairs dependencies.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void LoadChild(string path, bool hit)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("validate-model-cache-load"); start.ArgumentList.Add(path); start.ArgumentList.Add(hit ? "cooked" : "fallback");
        start.Environment.Remove("HS2_MODEL_CACHE");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new TimeoutException("Model check timed out."); }
        Require(process.ExitCode == 0, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static void Equivalent(LoadedModel reference, LoadedModel cooked)
    {
        Require(reference.Parts.Count == cooked.Parts.Count, "Part count changed.");
        foreach (var (a, b) in reference.Parts.Zip(cooked.Parts))
        {
            Require(Same(a.Positions, b.Positions) && Same(a.Normals, b.Normals) && Same(a.TexCoords, b.TexCoords) && Same(a.Indices, b.Indices), "Geometry changed: " + a.PartKey);
            Require(a.PartKey == b.PartKey && a.NodeName == b.NodeName && a.MeshName == b.MeshName &&
                a.NodeIndex == b.NodeIndex && a.MeshIndex == b.MeshIndex && a.PrimitiveIndex == b.PrimitiveIndex, "Part identity changed.");
            Require(a.Color == b.Color && a.MetallicFactor == b.MetallicFactor && a.RoughnessFactor == b.RoughnessFactor, "Material changed.");
            foreach (var pair in new[] { (a.BaseColorTexture, b.BaseColorTexture), (a.MaterialTexture, b.MaterialTexture), (a.NormalTexture, b.NormalTexture) })
            {
                if (pair.Item1 == null || pair.Item2 == null) { Require(pair.Item1 == pair.Item2, "Texture presence changed."); continue; }
                var (x, y) = (pair.Item1, pair.Item2);
                Require(x.Key == y.Key && x.Semantic == y.Semantic && x.Compressed == y.Compressed && x.Width == y.Width && x.Height == y.Height &&
                    x.Mips.Length == y.Mips.Length && x.Mips.Zip(y.Mips).All(p => p.First.AsSpan().SequenceEqual(p.Second)), "Texture mip data changed.");
            }
        }
    }

    private static bool Same<T>(T[]? a, T[]? b) where T : unmanaged
        => a == null || b == null ? a == b : MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan()));

    public static void Benchmark(string root, bool cooked)
    {
        CookedModelCache.Enabled = cooked;
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
        string[] paths = level.Entities.Select(e => e.MeshPath).Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order().Select(p => Path.Combine(root, "Game", p)).ToArray();
        var retained = new List<LoadedModel>();
        long startBytes = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        foreach (string path in paths) retained.Add(TextureCooker.LoadForRendering(path));
        watch.Stop();
        long allocated = GC.GetTotalAllocatedBytes(true) - startBytes;
        Console.WriteLine($"{(cooked ? "MODEL CACHE" : "GLB PARSE")}: {paths.Length} models + same cooked textures: {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated / 1048576.0:F2} MiB allocated; model hits {CookedModelCache.CacheHits}, source parses {CookedModelCache.FallbackLoads}, texture hits {TextureCooker.CacheHits}, image decodes {TextureCooker.FallbackDecodes}.");
        Console.WriteLine("Fresh process, warm filesystem, serial CPU preparation; source hashing included, GPU/collider construction and startup excluded.");
        GC.KeepAlive(retained);
    }
}

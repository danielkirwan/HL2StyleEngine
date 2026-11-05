using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;

internal static class LightingAllocationChecks
{
    public static void Run(string root)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "lighting-allocations-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(root, "Game/Content/Levels/sixRoomShadowTest.json");
        string Resolve(string p) => Path.GetFullPath(Path.Combine(root, "Game", p));
        var level = StructuredData.Load<LevelFile>(path);
        byte[] original = StructuredData.WriteJson(level);
        var view = LightingScenarioResolver.CreateView(level, level.LightingScenarios![0]);
        string viewPath = LightingScenarioResolver.PathFor(path, level.LightingScenarios[0].Id);
        var bake = BakeSnapshot.Capture(view, Resolve);
        string bakeKey = bake.Key(), bakePath = LightmapFile.PathFor(viewPath);
        var probe = view.Entities.First(e => e.Type == EntityTypes.ReflectionProbe);
        var reflection = ReflectionSnapshot.Create(view, viewPath, probe.Id, Resolve);
        string reflectionKey = reflection.Key(), reflectionPath = ReflectionProbeCache.PathFor(viewPath, probe.Id);
        var lightmap = LightmapFile.Load(bakePath, bakeKey) ?? throw new InvalidDataException("Six-room bake is missing or stale.");
        var environment = ReflectionProbeCache.Load(reflectionPath, reflectionKey) ?? throw new InvalidDataException("Six-room reflection is missing or stale.");
        var rows = new List<object>();
        void Measure(string name, Func<object> operation)
        {
            // Warm JIT and caches, but never force a collection to improve the result.
            GC.KeepAlive(operation());
            const int iterations = 6;
            long startBytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) GC.KeepAlive(operation());
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
            rows.Add(new { Name = name, Iterations = iterations, BytesPerOperation = bytes / iterations, MillisecondsPerOperation = elapsed / iterations });
            Console.WriteLine($"[LightingAllocations] {name}: {bytes / iterations:N0} bytes, {elapsed / iterations:F2} ms");
        }
        Measure("write-scene-json", () => StructuredData.WriteJson(level));
        Measure("read-scene-json", () => StructuredData.ReadJson<LevelFile>(original));
        Measure("scenario-view", () => LightingScenarioResolver.CreateView(level, level.LightingScenarios[0]));
        Measure("diffuse-snapshot", () => BakeSnapshot.Capture(view, Resolve));
        Measure("reflection-snapshot", () => ReflectionSnapshot.Create(view, viewPath, probe.Id, Resolve));
        Measure("lightmap-read", () => LightmapFile.Load(bakePath, bakeKey) ?? throw new InvalidDataException("Bake read failed."));
        Measure("reflection-read", () => ReflectionProbeCache.Load(reflectionPath, reflectionKey) ?? throw new InvalidDataException("Reflection read failed."));
        if (!StructuredData.WriteJson(level).AsSpan().SequenceEqual(original)) throw new InvalidDataException("Snapshot preparation mutated the source level.");
        int checks = ValidateReaders(folder);
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
        {
            Rows = rows, Checks = checks, BakeKey = bakeKey, ReflectionKey = reflectionKey,
            SceneJsonHash = Hash(original), BakeJsonHash = Hash(bake.Json), ReflectionJsonHash = Hash(reflection.Json),
            LightmapHash = Hash(lightmap.Pixels), ReflectionHashes = environment.Faces.Select(Hash).ToArray(), LutHash = Hash(environment.Lut),
            Note = "Warm single-thread allocations, not a cold-load or GPU benchmark. No forced GC. Source levels/caches are read-only."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[LightingAllocations] {checks} reader/ownership checks passed: " + folder);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static int ValidateReaders(string folder)
    {
        int checks = 0;
        void Check(bool ok, string reason)
        {
            if (!ok) throw new InvalidDataException(reason);
            Interlocked.Increment(ref checks);
        }
        var data = new LightmapData
        {
            Key = new string('A', 64), Size = 32, Texels = 4, Pixels = new byte[32 * 32 * 8], LightIds = ["light"],
            Records = [new() { EntityId = "model", PartTriangles = [1], Transform = Matrix4x4.Identity }]
        };
        string path = Path.Combine(folder, "fixture.hs2lightmap");
        LightmapFile.Save(path, data);
        byte[] valid = File.ReadAllBytes(path);
        var retained = LightmapFile.Load(path, data.Key)!;
        Check(retained.Pixels.AsSpan().SequenceEqual(data.Pixels), "Lightmap round trip changed pixels.");
        Check(LightmapFile.Load(path, "wrong-key") == null, "Stale lightmap accepted.");
        foreach (int length in new[] { 0, 4, 16, 47, valid.Length - 1 })
        {
            File.WriteAllBytes(path, valid[..length]);
            Check(LightmapFile.Load(path, data.Key) == null, "Truncated lightmap accepted.");
        }
        byte[] bad = (byte[])valid.Clone(); bad[16] ^= 1;
        File.WriteAllBytes(path, bad);
        Check(LightmapFile.Load(path, data.Key) == null, "Corrupt lightmap checksum accepted.");
        bad = (byte[])valid.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8), int.MaxValue);
        File.WriteAllBytes(path, bad);
        Check(LightmapFile.Load(path, data.Key) == null, "Unbounded lightmap accepted.");
        bad = (byte[])valid.Clone(); bad.AsSpan(48).Fill(255);
        File.WriteAllBytes(path, bad);
        Check(LightmapFile.Load(path, data.Key) == null, "Invalid LZ4 block accepted.");
        // Reload differently sized/coloured payloads concurrently, then verify the retained result.
        string otherPath = Path.Combine(folder, "other.hs2lightmap");
        data.Size = 64; data.Pixels = Enumerable.Repeat((byte)0x3c, 64 * 64 * 8).ToArray();
        LightmapFile.Save(otherPath, data); File.WriteAllBytes(path, valid);
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i =>
        {
            bool other = i % 2 == 0;
            var loaded = LightmapFile.Load(other ? otherPath : path, data.Key);
            Check(loaded != null && loaded.Size == (other ? 64 : 32) && loaded.Pixels.All(b => b == (other ? 0x3c : 0)),
                "Concurrent lightmap load reused another result's memory.");
        });
        Check(retained.Pixels.All(b => b == 0), "Pooled lightmap scratch escaped into a published result.");
        var faces = Enumerable.Range(0, 12).Select(i => Enumerable.Repeat((byte)0x3c, i < 6 ? 32 : 8).ToArray()).ToArray();
        var reflection = new ReflectionEnvironmentData(2, 2, 1, faces, new byte[4]);
        using var memory = new MemoryStream(); reflection.Write(memory);
        byte[] bytes = memory.ToArray();
        using (var chunks = new ChunkedStream(bytes))
        {
            var read = ReflectionEnvironmentData.Read(chunks);
            Check(read.Faces.Zip(faces).All(p => p.First.AsSpan().SequenceEqual(p.Second)) && read.Lut.AsSpan().SequenceEqual(reflection.Lut),
                "Non-seekable partial-read reflection changed pixels.");
            Check(chunks.CanRead, "Reflection reader closed its caller's stream.");
        }
        void Reject(byte[] broken)
        {
            bool rejected = false;
            try { using var input = new ChunkedStream(broken); ReflectionEnvironmentData.Read(input); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid reflection accepted.");
        }
        foreach (int length in new[] { 0, 8, 43, 55, bytes.Length - 1 }) Reject(bytes[..length]);
        Reject([.. bytes, 0]);
        bad = (byte[])bytes.Clone(); bad[^1] ^= 1; Reject(bad);
        bad = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8), 12); Reject(bad);
        bad = (byte[])bytes.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(44), 256); Reject(bad);
        // A valid checksum must not bypass finite-radiance validation.
        bad = (byte[])bytes.Clone(); bad[56] = 0; bad[57] = 0x7c;
        SHA256.HashData(bad.AsSpan(44)).CopyTo(bad, 12); Reject(bad);
        return checks;
    }

    private sealed class ChunkedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _source = new(bytes);
        public override bool CanRead => _source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _source.Read(buffer, offset, Math.Min(7, count));
        public override int Read(Span<byte> buffer) => _source.Read(buffer[..Math.Min(7, buffer.Length)]);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }
}

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using K4os.Compression.LZ4;

namespace Engine.Editor.Editor;

public sealed class LightmapRecord
{
    public string EntityId { get; set; } = "";
    public int FirstTriangle { get; set; }
    public int[] PartTriangles { get; set; } = [];
    public Matrix4x4 Transform { get; set; }
}

public sealed class LightmapData
{
    public string Key { get; set; } = "";
    public int Size { get; set; }
    public int Texels { get; set; }
    public List<LightmapRecord> Records { get; set; } = [];
    public List<string> LightIds { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LightProbeVolume? Probes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public LightProbeSampler? PreparedProbes { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public byte[] Pixels { get; set; } = [];
}

public static class LightmapFile
{
    // Session-owned receipt, not a global cache. Data must remain immutable while retained.
    public sealed class ValidatedRead
    {
        public LightmapData Data { get; }
        internal byte[]? ContentHash { get; }
        internal ValidatedRead(LightmapData data, byte[]? contentHash)
        { Data = data; ContentHash = contentHash; }
    }

    public const int Version = 2;
    private const int MaxBytes = 64 * 1024 * 1024;
    // One exact-sized idle buffer per role avoids rounding a 36 MiB bake up to 64 MiB.
    // Concurrent readers own separate buffers; published data never references scratch.
    private static byte[]? _compressedScratch, _decodedScratch;
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    public static string PathFor(string levelPath) => Path.ChangeExtension(Path.GetFullPath(levelPath), ".hs2lightmap");

    public static LightmapData? Load(string path, string key)
        => LoadValidated(path, key)?.Data;

    public static ValidatedRead? LoadValidated(string path, string key, ValidatedRead? previous = null,
        CancellationToken cancel = default)
    {
        byte[]? compressed = null, decoded = null;
        try
        {
            cancel.ThrowIfCancellationRequested();
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (file.Length < 48 || file.Length > MaxBytes) return null;
            // Hash the complete file, including header and compressed bytes. A timestamp,
            // embedded checksum or matching source key alone cannot certify unchanged content.
            byte[] contentHash = SHA256.HashData(file);
            cancel.ThrowIfCancellationRequested();
            if (previous?.Data.Key == key && previous.ContentHash != null &&
                contentHash.AsSpan().SequenceEqual(previous.ContentHash)) return previous;
            file.Position = 0;
            if (reader.ReadUInt32() != 0x4C325348) return null;
            int version = reader.ReadInt32();
            if (version is < 1 or > Version) return null;
            int length = reader.ReadInt32(), packed = reader.ReadInt32();
            if (length < 16 || length > MaxBytes || packed < 1 || packed != file.Length - 48) return null;
            Span<byte> hash = stackalloc byte[32], actualHash = stackalloc byte[32];
            file.ReadExactly(hash);
            compressed = RentScratch(ref _compressedScratch, packed);
            decoded = RentScratch(ref _decodedScratch, length);
            file.ReadExactly(compressed.AsSpan(0, packed));
            var data = decoded.AsSpan(0, length);
            if (LZ4Codec.Decode(compressed.AsSpan(0, packed), data) != length) return null;
            cancel.ThrowIfCancellationRequested();
            SHA256.HashData(data, actualHash);
            if (!actualHash.SequenceEqual(hash)) return null;
            int metadataLength = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (metadataLength < 1 || metadataLength > 16 * 1024 * 1024 || metadataLength > length - 4) return null;
            var result = JsonSerializer.Deserialize<LightmapData>(data.Slice(4, metadataLength), Options);
            if (result == null || result.Key != key || result.Size < 32 || result.Size > 2048 ||
                (result.Size & (result.Size - 1)) != 0 || result.Texels is < 4 or > 32 || result.Records == null ||
                result.LightIds == null || result.LightIds.Count is < 1 or > 32 || result.Records.Count is < 1 or > 4096) return null;
            if (result.LightIds.Any(string.IsNullOrWhiteSpace) ||
                result.LightIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.LightIds.Count) return null;
            int bytes = checked(result.Size * result.Size * 8);
            if (length - 4 - metadataLength != bytes) return null;
            int next = 0, columns = result.Size / (result.Texels + 4);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in result.Records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.EntityId) || !ids.Add(record.EntityId) ||
                    record.FirstTriangle != next || record.PartTriangles == null || record.PartTriangles.Length is < 1 or > 4096 ||
                    record.PartTriangles.Any(n => n is < 1 or > 65536)) return null;
                next = checked(next + record.PartTriangles.Sum());
                var matrix = record.Transform;
                if (MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref matrix, 1)).ContainsAnyExceptFinite()) return null;
            }
            if (next > columns * columns) return null;
            result.Pixels = data.Slice(4 + metadataLength, bytes).ToArray();
            foreach (Half value in MemoryMarshal.Cast<byte, Half>(result.Pixels))
                if (!Half.IsFinite(value) || value < (Half)0 || value > (Half)60000) return null;
            if (result.Probes != null)
            {
                if (!result.Probes.IsValid()) return null;
                result.PreparedProbes = new LightProbeSampler(result.Probes);
            }
            cancel.ThrowIfCancellationRequested();
            return new ValidatedRead(result, contentHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or OverflowException)
        { return null; }
        finally
        {
            if (decoded != null) Interlocked.CompareExchange(ref _decodedScratch, decoded, null);
            if (compressed != null) Interlocked.CompareExchange(ref _compressedScratch, compressed, null);
        }
    }

    private static byte[] RentScratch(ref byte[]? idle, int length)
    {
        var buffer = Interlocked.Exchange(ref idle, null);
        return buffer != null && buffer.Length >= length ? buffer : new byte[length];
    }

    public static void Save(string path, LightmapData data, CancellationToken cancel = default)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(data, Options);
        if (metadata.Length > 16 * 1024 * 1024) throw new InvalidDataException("Bake metadata exceeds the 16 MiB budget.");
        writer.Write(metadata.Length); writer.Write(metadata); writer.Write(data.Pixels);
        if (memory.Length > MaxBytes) throw new InvalidDataException("Lightmap exceeds the 64 MiB file budget.");
        byte[] raw = memory.ToArray(), packed = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int count = LZ4Codec.Encode(raw, packed);
        if (count <= 0) throw new InvalidDataException("Lightmap compression failed.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using (var file = File.Create(temporary))
            using (var output = new BinaryWriter(file))
            {
                output.Write(0x4C325348u); output.Write(Version); output.Write(raw.Length); output.Write(count);
                output.Write(SHA256.HashData(raw)); output.Write(packed, 0, count); file.Flush(true);
            }
            cancel.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class BakeFiniteValues
{
    public static bool ContainsAnyExceptFinite(this ReadOnlySpan<float> values)
    { foreach (float value in values) if (!float.IsFinite(value)) return true; return false; }
}

using System.Security.Cryptography;
using K4os.Compression.LZ4;

namespace Engine.Physics.Collision;

// Version covers both the triangle/BVH layout and collision construction semantics.
public static class MeshCollisionCache
{
    private const uint Magic = 0x43325348;
    public const int Version = 1;
    private const int MaxPayload = 256 * 1024 * 1024;
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HS2_COLLISION_CACHE") != "0";
    public static long Hits, Misses, Writes, WriteFailures;
    public static string? LastWriteError { get; private set; }

    public static string CachePath(string directory, byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Collision cache keys must be SHA256 hashes.", nameof(key));
        return Path.Combine(directory, $"v{Version}-{Convert.ToHexString(key)}.hs2collision");
    }

    public static bool TryLoad(string directory, byte[] key, out MeshCollisionMesh mesh)
    {
        mesh = null!;
        if (!Enabled) return false;
        try
        {
            using var file = File.OpenRead(CachePath(directory, key));
            using var header = new BinaryReader(file);
            if (header.ReadUInt32() != Magic || header.ReadInt32() != Version || !header.ReadBytes(32).AsSpan().SequenceEqual(key))
                throw new InvalidDataException("Stale collision cache.");
            int length = header.ReadInt32(), packedLength = header.ReadInt32();
            if (length < 88 || length > MaxPayload || packedLength < 1 || packedLength > LZ4Codec.MaximumOutputSize(length) ||
                file.Length - file.Position != 32L + packedLength) throw new InvalidDataException("Invalid collision payload size.");
            byte[] checksum = header.ReadBytes(32), packed = header.ReadBytes(packedLength), raw = new byte[length];
            if (LZ4Codec.Decode(packed, raw) != length || !SHA256.HashData(raw).AsSpan().SequenceEqual(checksum))
                throw new InvalidDataException("Invalid collision payload checksum.");
            using var reader = new BinaryReader(new MemoryStream(raw, writable: false));
            mesh = MeshCollisionMesh.ReadCooked(reader);
            Interlocked.Increment(ref Hits);
            return true;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        { Interlocked.Increment(ref Misses); return false; }
    }

    public static bool TryStore(string directory, byte[] key, MeshCollisionMesh mesh)
    {
        if (!Enabled || !mesh.IsValid || mesh.Triangles.Count > 2_000_000) return false;
        string? temporary = null;
        try
        {
            string path = CachePath(directory, key);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) mesh.WriteCooked(writer);
            if (stream.Length > MaxPayload) return false;
            byte[] raw = stream.ToArray(), packed = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
            int packedLength = LZ4Codec.Encode(raw, packed, LZ4Level.L00_FAST);
            if (packedLength <= 0) throw new InvalidDataException("Collision compression failed.");
            Directory.CreateDirectory(directory);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(key);
                writer.Write(raw.Length); writer.Write(packedLength); writer.Write(SHA256.HashData(raw));
                writer.Write(packed, 0, packedLength); writer.Flush(); file.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
            LastWriteError = null;
            Interlocked.Increment(ref Writes);
            return true;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        { LastWriteError = ex.Message; Interlocked.Increment(ref WriteFailures); return false; }
        finally
        {
            if (temporary != null)
                try { File.Delete(temporary); } catch (Exception ex) when (IsCacheFailure(ex)) { }
        }
    }

    private static bool IsCacheFailure(Exception ex)
        => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException;
}

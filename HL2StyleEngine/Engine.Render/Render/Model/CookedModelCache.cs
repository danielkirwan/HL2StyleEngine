using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using K4os.Compression.LZ4;

namespace Engine.Render;

// Lossless snapshot of the GLB loader's output. Bump Version when import semantics change.
public static class CookedModelCache
{
    private const uint Magic = 0x4D324853;
    private const int Version = 1, MaxPayload = 512 * 1024 * 1024, MaxParts = 65536, MaxStringBytes = 4096;
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HS2_MODEL_CACHE") != "0";
    public static long CacheHits, FallbackLoads, CookedModels;

    public static string CacheDirectory(string source)
        => Path.Combine(Path.GetDirectoryName(TextureCooker.CacheDirectory(source))!, "models");

    public static string CachePath(string source)
        => PathForHash(source, SHA256.HashData(File.ReadAllBytes(source)));

    private static string PathForHash(string source, byte[] hash)
        => Path.Combine(CacheDirectory(source), $"v{Version}-{Convert.ToHexString(hash)}.hs2model");

    public static LoadedModel Load(string source)
    {
        byte[] bytes = File.ReadAllBytes(source);
        if (Enabled)
        {
            byte[] hash = SHA256.HashData(bytes);
            if (TryRead(PathForHash(source, hash), hash, TextureCooker.CacheDirectory(source), out var cached))
            {
                Interlocked.Increment(ref CacheHits);
                return cached!;
            }
        }
        Interlocked.Increment(ref FallbackLoads);
        LoadedModel model = GlbModelLoader.Load(bytes);
        TextureCooker.Prepare(model, TextureCooker.CacheDirectory(source), cook: false);
        return model;
    }

    public static void Cook(string source)
    {
        byte[] bytes = File.ReadAllBytes(source), hash = SHA256.HashData(bytes);
        string path = PathForHash(source, hash);
        // Offline validation bypasses memory so deleted/corrupt texture dependencies are repaired.
        if (TryRead(path, hash, TextureCooker.CacheDirectory(source), out _, validateTextureFiles: true)) return;
        LoadedModel model = GlbModelLoader.Load(bytes);
        TextureCooker.Prepare(model, TextureCooker.CacheDirectory(source), cook: true);
        Write(path, hash, model);
        Interlocked.Increment(ref CookedModels);
    }

    private static bool TryRead(string path, byte[] sourceHash, string textureDirectory, out LoadedModel? model,
        bool validateTextureFiles = false)
    {
        model = null;
        try
        {
            using var file = File.OpenRead(path);
            using var header = new BinaryReader(file);
            if (header.ReadUInt32() != Magic || header.ReadInt32() != Version ||
                !header.ReadBytes(32).AsSpan().SequenceEqual(sourceHash)) return false;
            int length = header.ReadInt32(), packedLength = header.ReadInt32();
            if (length < 4 || length > MaxPayload || packedLength < 1 || packedLength > LZ4Codec.MaximumOutputSize(length) ||
                file.Length - file.Position != 32L + packedLength) return false;
            byte[] digest = header.ReadBytes(32), packed = header.ReadBytes(packedLength), payload = new byte[length];
            if (LZ4Codec.Decode(packed, payload) != length || !SHA256.HashData(payload).AsSpan().SequenceEqual(digest)) return false;
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            int count = reader.ReadInt32();
            if (count < 1 || count > MaxParts || count > length / 64) return false;
            var parts = new List<LoadedModelPart>(count);
            for (int i = 0; i < count; i++)
            {
                string node = ReadString(reader), mesh = ReadString(reader);
                int nodeIndex = reader.ReadInt32(), meshIndex = reader.ReadInt32(), primitiveIndex = reader.ReadInt32();
                var color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                float metallic = reader.ReadSingle(), roughness = reader.ReadSingle();
                var positions = ReadArray<Vector3>(reader);
                var normals = ReadOptionalArray<Vector3>(reader);
                var uv = ReadOptionalArray<Vector2>(reader);
                var indices = ReadArray<uint>(reader);
                if (positions.Length == 0 || indices.Length % 3 != 0 || indices.Any(v => v >= positions.Length) ||
                    normals != null && normals.Length != positions.Length || uv != null && uv.Length != positions.Length) return false;
                var part = new LoadedModelPart(positions, normals, uv, indices, color, null, null, metallic, roughness,
                    node, mesh, nodeIndex, meshIndex, primitiveIndex)
                {
                    BaseColorTexture = ReadTexture(reader, TextureSemantic.Color),
                    MaterialTexture = ReadTexture(reader, TextureSemantic.Material),
                    NormalTexture = ReadTexture(reader, TextureSemantic.Normal)
                };
                parts.Add(part);
            }
            if (stream.Position != stream.Length) return false;
            model = new LoadedModel(parts);
            return true;

            PreparedTexture? ReadTexture(BinaryReader data, TextureSemantic semantic)
            {
                string key = ReadString(data);
                if (key.Length == 0) return null;
                if (!TextureCooker.TryLoadPrepared(textureDirectory, key, semantic, out var texture))
                    throw new InvalidDataException("Cooked texture dependency is missing or invalid.");
                if (validateTextureFiles && !TextureCooker.TryRead(Path.Combine(textureDirectory, key + ".hs2tex"), key, semantic, out _))
                    throw new InvalidDataException("Cooked texture dependency needs repair.");
                return texture;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException or FormatException)
        { return false; }
    }

    private static T[] ReadArray<T>(BinaryReader reader) where T : unmanaged
    {
        int count = reader.ReadInt32();
        long bytes = (long)count * Marshal.SizeOf<T>();
        if (count < 0 || bytes > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Cooked array length exceeds payload.");
        T[] result = new T[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(result.AsSpan()));
        return result;
    }

    private static T[]? ReadOptionalArray<T>(BinaryReader reader) where T : unmanaged
        => reader.ReadBoolean() ? ReadArray<T>(reader) : null;

    private static string ReadString(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MaxStringBytes || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Cooked string length exceeds payload.");
        return Encoding.UTF8.GetString(reader.ReadBytes(count));
    }

    private static void Write(string path, byte[] hash, LoadedModel model)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            if (model.Parts.Count < 1 || model.Parts.Count > MaxParts) throw new InvalidDataException("Unsupported cooked part count.");
            writer.Write(model.Parts.Count);
            foreach (var part in model.Parts)
            {
                WriteString(writer, part.NodeName); WriteString(writer, part.MeshName);
                writer.Write(part.NodeIndex); writer.Write(part.MeshIndex); writer.Write(part.PrimitiveIndex);
                writer.Write(part.Color.X); writer.Write(part.Color.Y); writer.Write(part.Color.Z); writer.Write(part.Color.W);
                writer.Write(part.MetallicFactor); writer.Write(part.RoughnessFactor);
                WriteArray(writer, part.Positions);
                WriteOptionalArray(writer, part.Normals); WriteOptionalArray(writer, part.TexCoords);
                WriteArray(writer, part.Indices);
                WriteString(writer, part.BaseColorTexture?.Key ?? "");
                WriteString(writer, part.MaterialTexture?.Key ?? "");
                WriteString(writer, part.NormalTexture?.Key ?? "");
                if (stream.Length > MaxPayload) throw new InvalidDataException("Cooked model exceeds payload budget.");
            }
        }
        byte[] raw = stream.ToArray(), packed = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int packedLength = LZ4Codec.Encode(raw, packed, LZ4Level.L09_HC);
        if (packedLength <= 0) throw new InvalidDataException("Model compression failed.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(hash);
                writer.Write(raw.Length); writer.Write(packedLength); writer.Write(SHA256.HashData(raw));
                writer.Write(packed, 0, packedLength); writer.Flush(); file.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteArray<T>(BinaryWriter writer, T[] values) where T : unmanaged
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static void WriteOptionalArray<T>(BinaryWriter writer, T[]? values) where T : unmanaged
    {
        writer.Write(values != null);
        if (values != null) WriteArray(writer, values);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringBytes) throw new InvalidDataException("Cooked string exceeds budget.");
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}

using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using K4os.Compression.LZ4;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Engine.Render;

public enum TextureSemantic { Color, Material, Normal }

public sealed record PreparedTexture(string Key, int Width, int Height, TextureSemantic Semantic,
    bool Compressed, byte[][] Mips)
{
    public long Bytes => Mips.Sum(m => (long)m.Length);
}

// Source GLBs remain authoritative. This cache is disposable and content-addressed.
public static class TextureCooker
{
    private const int Version = 1;
    private const int MaxPayload = 512 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, WeakReference<PreparedTexture>> Memory = new();
    private static readonly object[] Locks = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    public static long CacheHits, FallbackDecodes, CookedTextures, SourceBytes, PreparedBytes;

    public static string CacheDirectory(string modelPath)
    {
        DirectoryInfo? dir = new(Path.GetDirectoryName(Path.GetFullPath(modelPath))!);
        while (dir != null && !dir.Name.Equals("Content", StringComparison.OrdinalIgnoreCase)) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? Path.GetDirectoryName(Path.GetFullPath(modelPath))!, ".hs2cache", "textures");
    }

    public static LoadedModel LoadForRendering(string path)
        => CookedModelCache.Load(path);

    public static void CookModel(string path)
        => CookedModelCache.Cook(path);

    internal static bool TryLoadPrepared(string directory, string key, TextureSemantic semantic, out PreparedTexture? texture)
    {
        texture = null;
        string prefix = $"v{Version}-{semantic}-";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length != prefix.Length + 64 ||
            key.AsSpan(prefix.Length).ContainsAnyExcept("0123456789ABCDEF")) return false;
        string path = Path.Combine(directory, key + ".hs2tex");
        lock (Locks[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)Locks.Length])
        {
            if (Memory.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached) && cached.Compressed)
            { texture = cached; return true; }
            if (!TryRead(path, key, semantic, out texture)) return false;
            Interlocked.Increment(ref CacheHits);
            Memory[key] = new(texture!);
            return true;
        }
    }

    public static void Prepare(LoadedModel model, string? cacheDirectory, bool cook)
    {
        foreach (var part in model.Parts)
        {
            if (part.BaseColorPng is { Length: > 0 } color)
                part.BaseColorTexture = PrepareImage(color, TextureSemantic.Color, cacheDirectory, cook);
            if (part.MetallicRoughnessPng is { Length: > 0 } material)
                part.MaterialTexture = PrepareImage(material, TextureSemantic.Material, cacheDirectory, cook);
            if (part.NormalPng is { Length: > 0 } normal)
                part.NormalTexture = PrepareImage(normal, TextureSemantic.Normal, cacheDirectory, cook);
        }
    }

    public static PreparedTexture PrepareImage(byte[] image, TextureSemantic semantic, string? cacheDirectory, bool cook)
    {
        string key = $"v{Version}-{semantic}-{Convert.ToHexString(SHA256.HashData(image))}";
        string? path = cacheDirectory == null ? null : Path.Combine(cacheDirectory, key + ".hs2tex");
        // Serialize a shared image's preparation while allowing unrelated workers to progress.
        lock (Locks[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)Locks.Length])
        {
            if (!cook && Memory.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached)) return cached;
            if (path != null && TryRead(path, key, semantic, out var prepared))
            {
                Interlocked.Increment(ref CacheHits);
                Memory[key] = new(prepared!);
                return prepared!;
            }
            (int width, int height, byte[] rgba) = Decode(image);
            var mips = BuildMips(rgba, width, height, semantic);
            if (cook)
            {
                var encoder = new BcEncoder();
                encoder.OutputOptions.GenerateMipMaps = false;
                encoder.OutputOptions.Format = semantic == TextureSemantic.Normal ? CompressionFormat.Bc5 : CompressionFormat.Bc7;
                encoder.OutputOptions.Quality = CompressionQuality.Balanced;
                encoder.Options.TaskCount = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
                int w = width, h = height;
                for (int mip = 0; mip < mips.Length; mip++)
                {
                    mips[mip] = encoder.EncodeToRawBytes(mips[mip], w, h, BCnEncoder.Encoder.PixelFormat.Rgba32)[0];
                    w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                }
                Interlocked.Increment(ref CookedTextures);
            }
            else Interlocked.Increment(ref FallbackDecodes);
            prepared = new PreparedTexture(key, width, height, semantic, cook, mips);
            Interlocked.Add(ref SourceBytes, image.LongLength);
            Interlocked.Add(ref PreparedBytes, prepared.Bytes);
            if (cook && path != null) Write(path, prepared);
            Memory[key] = new(prepared);
            return prepared;
        }
    }

    public static bool TryRead(string path, string key, TextureSemantic semantic, out PreparedTexture? texture)
    {
        texture = null;
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (reader.ReadUInt32() != 0x58543248 || reader.ReadInt32() != Version || reader.ReadString() != key) return false;
            int w = reader.ReadInt32(), h = reader.ReadInt32(), count = reader.ReadInt32();
            if (w < 1 || h < 1 || w > 16384 || h > 16384 || count != 1 + (int)Math.Floor(Math.Log2(Math.Max(w, h)))) return false;
            var mips = new byte[count][];
            int mw = w, mh = h;
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32(), packed = reader.ReadInt32();
                int expected = checked(((mw + 3) / 4) * ((mh + 3) / 4) * 16);
                if (length != expected || length > MaxPayload || packed < 1 || packed > LZ4Codec.MaximumOutputSize(length) || packed > file.Length - file.Position - 32) return false;
                byte[] hash = reader.ReadBytes(32), bytes = reader.ReadBytes(packed);
                mips[i] = new byte[length];
                if (LZ4Codec.Decode(bytes, mips[i]) != length || !SHA256.HashData(mips[i]).AsSpan().SequenceEqual(hash)) return false;
                mw = Math.Max(1, mw / 2); mh = Math.Max(1, mh / 2);
            }
            if (file.Position != file.Length) return false;
            texture = new PreparedTexture(key, w, h, semantic, true, mips);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException or FormatException) { return false; }
    }

    private static void Write(string path, PreparedTexture texture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(0x58543248u); writer.Write(Version); writer.Write(texture.Key);
                writer.Write(texture.Width); writer.Write(texture.Height); writer.Write(texture.Mips.Length);
                foreach (byte[] mip in texture.Mips)
                {
                    byte[] packed = new byte[LZ4Codec.MaximumOutputSize(mip.Length)];
                    int count = LZ4Codec.Encode(mip, packed, LZ4Level.L09_HC);
                    writer.Write(mip.Length); writer.Write(count); writer.Write(SHA256.HashData(mip));
                    writer.Write(packed, 0, count);
                }
                writer.Flush(); file.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (int, int, byte[]) DecodeWindows(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var source = new Bitmap(stream);
        if (source.Width > 16384 || source.Height > 16384) throw new InvalidDataException("Texture exceeds 16384 pixels.");
        using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[Math.Abs(data.Stride)], rgba = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int s = x * 4, d = (y * bitmap.Width + x) * 4;
                    rgba[d] = row[s + 2]; rgba[d + 1] = row[s + 1]; rgba[d + 2] = row[s]; rgba[d + 3] = row[s + 3];
                }
            }
            return (bitmap.Width, bitmap.Height, rgba);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static (int, int, byte[]) Decode(byte[] image)
        => OperatingSystem.IsWindows() ? DecodeWindows(image) : throw new PlatformNotSupportedException("Image import currently requires Windows.");

    public static byte[][] BuildMips(byte[] pixels, int width, int height, TextureSemantic semantic)
    {
        var result = new List<byte[]> { pixels };
        Span<float> sum = stackalloc float[4];
        while (width > 1 || height > 1)
        {
            int nw = Math.Max(1, width / 2), nh = Math.Max(1, height / 2);
            byte[] next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++)
            {
                sum.Clear(); int samples = 0;
                for (int sy = y * height / nh; sy < (y + 1) * height / nh; sy++)
                for (int sx = x * width / nw; sx < (x + 1) * width / nw; sx++)
                {
                    int index = (sy * width + sx) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        float v = pixels[index + c] / 255f;
                        sum[c] += semantic == TextureSemantic.Color && c < 3 ? SrgbToLinear(v) : v;
                    }
                    samples++;
                }
                for (int c = 0; c < 4; c++) sum[c] /= samples;
                if (semantic == TextureSemantic.Normal)
                {
                    var normal = new System.Numerics.Vector3(sum[0] * 2 - 1, sum[1] * 2 - 1, sum[2] * 2 - 1);
                    normal = normal.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(normal) : System.Numerics.Vector3.UnitZ;
                    sum[0] = normal.X * .5f + .5f; sum[1] = normal.Y * .5f + .5f; sum[2] = normal.Z * .5f + .5f;
                }
                for (int c = 0; c < 4; c++)
                {
                    float v = semantic == TextureSemantic.Color && c < 3 ? LinearToSrgb(sum[c]) : sum[c];
                    next[(y * nw + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
                }
            }
            result.Add(next); pixels = next; width = nw; height = nh;
        }
        return result.ToArray();
    }

    private static float SrgbToLinear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
    private static float LinearToSrgb(float v) => v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f;
}

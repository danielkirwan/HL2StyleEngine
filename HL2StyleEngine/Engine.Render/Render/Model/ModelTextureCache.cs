using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Veldrid;

namespace Engine.Render;

internal sealed class ModelTextureCache : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, SharedTexture> _textures = new(StringComparer.Ordinal);
    public long ResidentBytes { get; private set; }
    public int Count => _textures.Count;
    public long Uploads { get; private set; }
    public ModelTextureCache(GraphicsDevice device) => _device = device;

    public SharedTexture Acquire(PreparedTexture source)
    {
        string key = source.Key + (source.Compressed ? "-bc" : "-rgba");
        if (_textures.TryGetValue(key, out var shared)) { shared.References++; return shared; }
        bool srgb = source.Semantic == TextureSemantic.Color;
        PixelFormat format = source.Compressed
            ? source.Semantic == TextureSemantic.Normal ? PixelFormat.BC5_UNorm : srgb ? PixelFormat.BC7_UNorm_SRgb : PixelFormat.BC7_UNorm
            : srgb ? PixelFormat.R8_G8_B8_A8_UNorm_SRgb : PixelFormat.R8_G8_B8_A8_UNorm;
        byte[][] mips = source.Mips;
        if (source.Compressed && !_device.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.Sampled))
        {
            var decoder = new BcDecoder();
            var decoded = new byte[mips.Length][];
            for (int i = 0; i < decoded.Length; i++)
            {
                var pixels = decoder.DecodeRaw(mips[i], Math.Max(1, source.Width >> i), Math.Max(1, source.Height >> i),
                    source.Semantic == TextureSemantic.Normal ? CompressionFormat.Bc5 : CompressionFormat.Bc7);
                decoded[i] = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
            }
            mips = decoded;
            format = srgb ? PixelFormat.R8_G8_B8_A8_UNorm_SRgb : PixelFormat.R8_G8_B8_A8_UNorm;
        }
        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)source.Width, (uint)source.Height, (uint)mips.Length, 1, format, TextureUsage.Sampled));
        try
        {
            for (uint mip = 0; mip < mips.Length; mip++)
                _device.UpdateTexture(texture, mips[mip], 0, 0, 0,
                    (uint)Math.Max(1, source.Width >> (int)mip), (uint)Math.Max(1, source.Height >> (int)mip), 1, mip, 0);
            shared = new SharedTexture(key, texture, _device.ResourceFactory.CreateTextureView(texture), mips.Sum(m => (long)m.Length));
            _textures.Add(key, shared); ResidentBytes += shared.Bytes; Uploads++;
            return shared;
        }
        catch { texture.Dispose(); throw; }
    }

    public void Release(SharedTexture? texture)
    {
        if (texture == null || --texture.References != 0) return;
        if (!_textures.Remove(texture.Key)) return;
        ResidentBytes -= texture.Bytes; texture.View.Dispose(); texture.Texture.Dispose();
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values) { texture.View.Dispose(); texture.Texture.Dispose(); }
        _textures.Clear(); ResidentBytes = 0;
    }

    internal sealed class SharedTexture(string key, Texture texture, TextureView view, long bytes)
    {
        public string Key { get; } = key;
        public Texture Texture { get; } = texture;
        public TextureView View { get; } = view;
        public long Bytes { get; } = bytes;
        public int References = 1;
    }
}

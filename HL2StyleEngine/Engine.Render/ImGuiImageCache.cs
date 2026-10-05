using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public readonly record struct UiImage(IntPtr Id, int Width, int Height);

internal sealed class ImGuiImageCache : IDisposable
{
    private readonly Dictionary<string, (UiImage Image, Texture? Texture, TextureView? View)> _images = new(StringComparer.OrdinalIgnoreCase);

    public UiImage Get(string path, GraphicsDevice device, ImGuiRenderer renderer)
    {
        if (_images.TryGetValue(path, out var cached)) return cached.Image;
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return default;
        Texture? texture = null;
        TextureView? view = null;
        try
        {
            using var source = new Bitmap(path);
            using var bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            var bits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
            try
            {
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            }
            finally { bitmap.UnlockBits(bits); }
            texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)bitmap.Width,
                (uint)bitmap.Height, 1, 1, Veldrid.PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.Sampled));
            device.UpdateTexture(texture, pixels, 0, 0, 0, (uint)bitmap.Width, (uint)bitmap.Height, 1, 0, 0);
            view = device.ResourceFactory.CreateTextureView(texture);
            var image = new UiImage(renderer.GetOrCreateImGuiBinding(device.ResourceFactory, view), bitmap.Width, bitmap.Height);
            _images[path] = (image, texture, view);
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException)
        {
            view?.Dispose(); texture?.Dispose();
            _images[path] = (default, null, null);
            return default;
        }
    }

    public void Dispose()
    {
        foreach (var item in _images.Values) { item.View?.Dispose(); item.Texture?.Dispose(); }
        _images.Clear();
    }
}

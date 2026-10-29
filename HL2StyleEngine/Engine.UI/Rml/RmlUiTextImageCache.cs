using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security.Cryptography;
using System.Text;

namespace Engine.UI.Rml;

#pragma warning disable CA1416

internal sealed class RmlUiTextImageCache : IDisposable
{
    private const string FontFileName = "AurelDeco-Regular.ttf";

    private readonly string _fontPath;
    private readonly string _outputDirectory;
    private readonly PrivateFontCollection _fonts = new();
    private readonly bool _available;
    private readonly Dictionary<(string Text, string Role, int Height, long FontStamp), CachedImage> _metadata = new();
    private readonly record struct CachedImage(RmlUiTextImage Image, string Path, long Length, long Stamp);
    private const int MaxMetadataEntries = 1024;
    public UiWorkProfile? Profile { get; set; }

    public RmlUiTextImageCache(string contentRoot)
    {
        _fontPath = Path.Combine(contentRoot, "Fonts", FontFileName);
        _outputDirectory = Path.Combine(contentRoot, "Runtime", "Text");

        if (!OperatingSystem.IsWindows() || !File.Exists(_fontPath))
            return;

        try
        {
            _fonts.AddFontFile(_fontPath);
            _available = _fonts.Families.Length > 0;
        }
        catch
        {
            _available = false;
        }
    }

    public bool TryGetTextImage(string text, string role, int pixelHeight, out RmlUiTextImage image)
    {
        using var measure = Profile?.Measure("text-image");
        image = default;
        if (!_available || string.IsNullOrWhiteSpace(text) || pixelHeight <= 0 || !OperatingSystem.IsWindows())
            return false;

        try
        {
            long fontStamp = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0;
            var metadataKey = (text, role, pixelHeight, fontStamp);
            if (_metadata.TryGetValue(metadataKey, out var cached))
            {
                var file = new FileInfo(cached.Path);
                if (file.Exists && file.Length == cached.Length && file.LastWriteTimeUtc.Ticks == cached.Stamp)
                {
                    image = cached.Image;
                    return true;
                }
                _metadata.Remove(metadataKey);
            }
            Directory.CreateDirectory(_outputDirectory);

            string key = BuildKey(text, role, pixelHeight, fontStamp);
            string fileName = $"{key}.png";
            string path = Path.Combine(_outputDirectory, fileName);

            if (!File.Exists(path))
                RenderText(text, role, pixelHeight, path);

            using Bitmap bitmap = new(path);
            image = new RmlUiTextImage($"Text/{fileName}", bitmap.Width, bitmap.Height);
            var info = new FileInfo(path);
            if (_metadata.Count >= MaxMetadataEntries) _metadata.Clear();
            _metadata[metadataKey] = new(image, path, info.Length, info.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch
        {
            image = default;
            return false;
        }
    }

    public void Dispose()
        => _fonts.Dispose();

    private static string BuildKey(string text, string role, int pixelHeight, long fontStamp)
    {
        string raw = $"{role}\n{pixelHeight}\n{fontStamp}\n{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    private void RenderText(string text, string role, int pixelHeight, string path)
    {
        using Font font = new(_fonts.Families[0], pixelHeight, FontStyle.Regular, GraphicsUnit.Pixel);
        using StringFormat format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        SizeF measured;
        using (Bitmap scratch = new(8, 8))
        using (Graphics scratchGraphics = Graphics.FromImage(scratch))
        {
            scratchGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            measured = scratchGraphics.MeasureString(text, font, int.MaxValue, format);
        }

        int paddingX = Math.Max(4, pixelHeight / 5);
        int paddingY = Math.Max(2, pixelHeight / 8);
        int width = Math.Max(1, (int)Math.Ceiling(measured.Width) + paddingX * 2);
        int height = Math.Max(1, (int)Math.Ceiling(measured.Height) + paddingY * 2);

        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using SolidBrush shadow = new(Color.FromArgb(120, 0, 0, 0));
        using SolidBrush fill = new(TextColor(role));
        PointF origin = new(paddingX, paddingY);
        graphics.DrawString(text, font, shadow, new PointF(origin.X + 1, origin.Y + 1), format);
        graphics.DrawString(text, font, fill, origin, format);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static Color TextColor(string role)
        => role switch
        {
            "eyebrow" or "subtitle" or "footer" => Color.FromArgb(178, 184, 174),
            "warning" => Color.FromArgb(241, 216, 160),
            "muted" => Color.FromArgb(174, 182, 174),
            _ => Color.FromArgb(241, 237, 221)
        };
}

#pragma warning restore CA1416

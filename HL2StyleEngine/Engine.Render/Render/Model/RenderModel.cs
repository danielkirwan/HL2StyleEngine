using System.Numerics;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using Veldrid;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Runtime.Versioning;

namespace Engine.Render;

public sealed class RenderModel : IDisposable
{
    private readonly List<RenderModelPart> _parts = new();

    internal RenderModel(GraphicsDevice graphicsDevice, LoadedModel source, ResourceLayout textureLayout, Sampler sampler)
    {
        foreach (LoadedModelPart part in source.Parts)
            _parts.Add(new RenderModelPart(graphicsDevice, part, textureLayout, sampler));
    }

    internal IReadOnlyList<RenderModelPart> Parts => _parts;

    public void Dispose()
    {
        for (int i = 0; i < _parts.Count; i++)
            _parts[i].Dispose();

        _parts.Clear();
    }
}

internal sealed class RenderModelPart : IDisposable
{
    public RenderModelPart(GraphicsDevice graphicsDevice, LoadedModelPart source, ResourceLayout textureLayout, Sampler sampler)
    {
        VertexBuffer = graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
            (uint)(source.Positions.Length * Marshal.SizeOf<Vector3>()),
            BufferUsage.VertexBuffer));
        graphicsDevice.UpdateBuffer(VertexBuffer, 0, source.Positions);

        if (source.TexCoords is { Length: > 0 } texCoords && texCoords.Length == source.Positions.Length)
        {
            TexturedVertex[] texturedVertices = new TexturedVertex[source.Positions.Length];
            Vector3[]? normals = source.Normals != null && source.Normals.Length == source.Positions.Length
                ? source.Normals
                : null;
            for (int i = 0; i < texturedVertices.Length; i++)
            {
                Vector3 normal = normals != null
                    ? normals[i]
                    : Vector3.UnitY;
                texturedVertices[i] = new TexturedVertex(source.Positions[i], normal, texCoords[i]);
            }

            TexturedVertexBuffer = graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(texturedVertices.Length * TexturedVertex.SizeInBytes),
                BufferUsage.VertexBuffer));
            graphicsDevice.UpdateBuffer(TexturedVertexBuffer, 0, texturedVertices);
        }

        IndexFormat = source.Positions.Length <= ushort.MaxValue && source.Indices.All(static i => i <= ushort.MaxValue)
            ? IndexFormat.UInt16
            : IndexFormat.UInt32;

        if (IndexFormat == IndexFormat.UInt16)
        {
            ushort[] indices = source.Indices.Select(static i => (ushort)i).ToArray();
            IndexBuffer = graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));
            graphicsDevice.UpdateBuffer(IndexBuffer, 0, indices);
        }
        else
        {
            IndexBuffer = graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(source.Indices.Length * sizeof(uint)),
                BufferUsage.IndexBuffer));
            graphicsDevice.UpdateBuffer(IndexBuffer, 0, source.Indices);
        }

        IndexCount = (uint)source.Indices.Length;
        Color = source.Color;
        MaterialFactors = new Vector4(
            Math.Clamp(source.MetallicFactor, 0f, 1f),
            Math.Clamp(source.RoughnessFactor, 0.04f, 1f),
            0f,
            0f);

        if (TexturedVertexBuffer != null &&
            source.BaseColorPng is { Length: > 0 } pngBytes &&
            OperatingSystem.IsWindows() &&
            TryDecodePngRgba(pngBytes, out int width, out int height, out byte[] rgba))
        {
            Texture = CreateTexture(graphicsDevice, width, height, rgba);
            TextureView = graphicsDevice.ResourceFactory.CreateTextureView(Texture);

            byte[] metallicRoughnessRgba = [0, 255, 0, 255];
            int metallicRoughnessWidth = 1;
            int metallicRoughnessHeight = 1;
            if (source.MetallicRoughnessPng is { Length: > 0 } metallicRoughnessPng &&
                TryDecodePngRgba(metallicRoughnessPng, out int mrWidth, out int mrHeight, out byte[] mrRgba))
            {
                metallicRoughnessWidth = mrWidth;
                metallicRoughnessHeight = mrHeight;
                metallicRoughnessRgba = mrRgba;
            }

            MetallicRoughnessTexture = CreateTexture(graphicsDevice, metallicRoughnessWidth, metallicRoughnessHeight, metallicRoughnessRgba);
            MetallicRoughnessTextureView = graphicsDevice.ResourceFactory.CreateTextureView(MetallicRoughnessTexture);
            TextureSet = graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                textureLayout,
                TextureView,
                sampler,
                MetallicRoughnessTextureView,
                sampler));
        }
    }

    public DeviceBuffer VertexBuffer { get; }
    public DeviceBuffer? TexturedVertexBuffer { get; }
    public DeviceBuffer IndexBuffer { get; }
    public IndexFormat IndexFormat { get; }
    public uint IndexCount { get; }
    public Vector4 Color { get; }
    public Vector4 MaterialFactors { get; }
    public Texture? Texture { get; }
    public TextureView? TextureView { get; }
    public Texture? MetallicRoughnessTexture { get; }
    public TextureView? MetallicRoughnessTextureView { get; }
    public ResourceSet? TextureSet { get; }
    public bool IsTextured => TexturedVertexBuffer != null && TextureSet != null;

    public void Dispose()
    {
        TextureSet?.Dispose();
        MetallicRoughnessTextureView?.Dispose();
        MetallicRoughnessTexture?.Dispose();
        TextureView?.Dispose();
        Texture?.Dispose();
        IndexBuffer.Dispose();
        TexturedVertexBuffer?.Dispose();
        VertexBuffer.Dispose();
    }

    private static Texture CreateTexture(GraphicsDevice graphicsDevice, int width, int height, byte[] rgba)
    {
        Texture texture = graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            1,
            1,
            Veldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));
        graphicsDevice.UpdateTexture(
            texture,
            rgba,
            0,
            0,
            0,
            (uint)width,
            (uint)height,
            1,
            0,
            0);
        return texture;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryDecodePngRgba(byte[] pngBytes, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];

        try
        {
            using MemoryStream stream = new(pngBytes);
            using Bitmap source = new(stream);
            using Bitmap bitmap = new(source.Width, source.Height, DrawingPixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            System.Drawing.Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
            try
            {
                int byteCount = Math.Abs(data.Stride) * bitmap.Height;
                byte[] bgra = new byte[byteCount];
                Marshal.Copy(data.Scan0, bgra, 0, byteCount);

                rgba = new byte[bitmap.Width * bitmap.Height * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int srcRow = data.Stride > 0
                        ? y * data.Stride
                        : (bitmap.Height - 1 - y) * -data.Stride;
                    int dstRow = y * bitmap.Width * 4;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int src = srcRow + x * 4;
                        int dst = dstRow + x * 4;
                        rgba[dst + 0] = bgra[src + 2];
                        rgba[dst + 1] = bgra[src + 1];
                        rgba[dst + 2] = bgra[src + 0];
                        rgba[dst + 3] = bgra[src + 3];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            width = bitmap.Width;
            height = bitmap.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct TexturedVertex
{
    public TexturedVertex(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
    }

    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector2 TexCoord;

    public static uint SizeInBytes => (uint)((3 + 3 + 2) * sizeof(float));
}

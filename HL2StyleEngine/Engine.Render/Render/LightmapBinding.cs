using System.Numerics;
using Veldrid;

namespace Engine.Render;

// Each triangle has its own padded chart. Source UVs/indices and shared model resources stay immutable.
public static class LightmapCharts
{
    public const int Padding = 2;
    public static Vector2 Corner(int triangle, int corner, int texels, int atlasSize)
    {
        int tile = texels + Padding * 2, columns = atlasSize / tile;
        var pixel = new Vector2(triangle % columns * tile + Padding + .5f, triangle / columns * tile + Padding + .5f);
        if (corner == 1) pixel.X += texels - 1;
        if (corner == 2) pixel.Y += texels - 1;
        return pixel / atlasSize;
    }
}

public sealed class LightmapAtlas : IDisposable
{
    private readonly Texture _texture;
    private readonly TextureView _view;
    internal ResourceSet Set { get; }
    public long Bytes { get; }
    internal LightmapAtlas(GraphicsDevice device, ResourceLayout layout, Sampler sampler, int size, byte[] halfRgba)
    {
        if (size < 1 || size > 2048 || halfRgba.Length != size * size * 8) throw new ArgumentException("Invalid lightmap dimensions.");
        _texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)size, (uint)size, 1, 1,
            PixelFormat.R16_G16_B16_A16_Float, TextureUsage.Sampled));
        try
        {
            device.UpdateTexture(_texture, halfRgba, 0, 0, 0, (uint)size, (uint)size, 1, 0, 0);
            _view = device.ResourceFactory.CreateTextureView(_texture);
            try { Set = device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, _view, sampler)); }
            catch { _view.Dispose(); throw; }
            Bytes = halfRgba.Length;
        }
        catch { _texture.Dispose(); throw; }
    }
    public void Dispose() { Set.Dispose(); _view.Dispose(); _texture.Dispose(); }
}

public sealed class LightmapBinding : IDisposable
{
    internal sealed record Part(DeviceBuffer Vertices, DeviceBuffer Indices, uint Count) : IDisposable
    { public void Dispose() { Vertices.Dispose(); Indices.Dispose(); } }
    internal readonly List<Part> Parts = new();
    internal LightmapAtlas Atlas { get; }
    private readonly LoadedModel _source;
    private readonly int _texels, _size;
    private int _triangle;
    private readonly bool _pbr;
    public bool IsReady => Parts.Count == _source.Parts.Count;
    public long Bytes => Parts.Sum(p => (long)p.Vertices.SizeInBytes + p.Indices.SizeInBytes);
    public LightmapBinding(LoadedModel source, LightmapAtlas atlas, int firstTriangle, int texels, int size, bool pbr = false)
    { _source = source; Atlas = atlas; _triangle = firstTriangle; _texels = texels; _size = size; _pbr = pbr; }
    public void UploadNext(GraphicsDevice device)
    {
        if (IsReady) return;
        var source = _source.Parts[Parts.Count];
        var vertices = new TexturedVertex[source.Indices.Length];
        var indices = new uint[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            int index = (int)source.Indices[i];
            int first = i - i % 3;
            Vector3 normal = (_pbr ? source.PbrNormals ?? source.Normals : source.Normals)?[index] ?? Vector3.Normalize(Vector3.Cross(
                source.Positions[source.Indices[first + 1]] - source.Positions[source.Indices[first]],
                source.Positions[source.Indices[first + 2]] - source.Positions[source.Indices[first]]));
            if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-12f) normal = Vector3.UnitY;
            Vector2 uv = source.TexCoords?[index] ?? Vector2.Zero;
            vertices[i] = new(source.Positions[index], normal, uv, source.EmissiveTexCoords?[index] ?? uv,
                LightmapCharts.Corner(_triangle + i / 3, i % 3, _texels, _size));
            indices[i] = (uint)i;
        }
        var vb = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(vertices.Length * TexturedVertex.SizeInBytes), BufferUsage.VertexBuffer));
        DeviceBuffer? ib = null;
        try
        {
            ib = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(indices.Length * 4), BufferUsage.IndexBuffer));
            device.UpdateBuffer(vb, 0, vertices); device.UpdateBuffer(ib, 0, indices);
            Parts.Add(new(vb, ib, (uint)indices.Length)); _triangle += indices.Length / 3;
        }
        catch { vb.Dispose(); ib?.Dispose(); throw; }
    }
    public void Dispose() { foreach (var part in Parts) part.Dispose(); Parts.Clear(); }
}

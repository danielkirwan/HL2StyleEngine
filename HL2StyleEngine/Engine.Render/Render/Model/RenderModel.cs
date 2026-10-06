using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed class RenderModel : IDisposable
{
    private readonly List<RenderModelPart> _parts = new();
    private readonly LoadedModel _source;
    public bool IsReady => _parts.Count == _source.Parts.Count;
    public long GeometryBytes => _parts.Sum(p => (long)p.VertexBuffer.SizeInBytes + p.IndexBuffer.SizeInBytes + (p.TexturedVertexBuffer?.SizeInBytes ?? 0));

    internal RenderModel(GraphicsDevice graphicsDevice, LoadedModel source, ResourceLayout textureLayout, Sampler sampler,
        ModelTextureCache textures, bool loadTextures = true, bool deferred = false)
    {
        _source = source;
        if (deferred) return;
        try { while (!IsReady) UploadNext(graphicsDevice, textureLayout, sampler, textures, loadTextures); }
        catch { Dispose(); throw; }
    }

    internal void UploadNext(GraphicsDevice graphicsDevice, ResourceLayout textureLayout, Sampler sampler,
        ModelTextureCache textures, bool loadTextures = true)
        => _parts.Add(new RenderModelPart(graphicsDevice, _source.Parts[_parts.Count], textureLayout, sampler, textures, loadTextures));

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
    private readonly ModelTextureCache _textures;
    private ModelTextureCache.SharedTexture? _color, _material, _normal;
    private static readonly PreparedTexture DefaultMaterial = new("default-material", 1, 1, TextureSemantic.Material, false, [[0, 255, 0, 255]]);
    private static readonly PreparedTexture DefaultNormal = new("default-normal", 1, 1, TextureSemantic.Normal, false, [[128, 128, 255, 255]]);

    public RenderModelPart(GraphicsDevice graphicsDevice, LoadedModelPart source, ResourceLayout textureLayout, Sampler sampler,
        ModelTextureCache textures, bool loadTextures = true)
    {
        _textures = textures;
        BoundsMin = new Vector3(float.PositiveInfinity);
        BoundsMax = new Vector3(float.NegativeInfinity);
        foreach (Vector3 position in source.Positions)
        { BoundsMin = Vector3.Min(BoundsMin, position); BoundsMax = Vector3.Max(BoundsMax, position); }
        try
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
            PartKey = source.PartKey;
            NodeName = source.NodeName;
            MeshName = source.MeshName;
            Color = source.Color;
            MaterialFactors = new Vector4(
                Math.Clamp(source.MetallicFactor, 0f, 1f),
                Math.Clamp(source.RoughnessFactor, 0.04f, 1f),
                source.NormalTexture != null || source.NormalPng != null ? 1f : 0f,
                0f);

            if (loadTextures &&
                TexturedVertexBuffer != null &&
                (source.BaseColorTexture != null || source.BaseColorPng is { Length: > 0 }))
            {
                _color = textures.Acquire(source.BaseColorTexture ?? TextureCooker.PrepareImage(source.BaseColorPng!, TextureSemantic.Color, null, false));
                _material = textures.Acquire(source.MaterialTexture ?? (source.MetallicRoughnessPng is { Length: > 0 } mr
                    ? TextureCooker.PrepareImage(mr, TextureSemantic.Material, null, false) : DefaultMaterial));
                _normal = textures.Acquire(source.NormalTexture ?? (source.NormalPng is { Length: > 0 } normal
                    ? TextureCooker.PrepareImage(normal, TextureSemantic.Normal, null, false) : DefaultNormal));
                TextureSet = graphicsDevice.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                    textureLayout,
                    _color.View,
                    sampler,
                    _material.View,
                    sampler,
                    _normal.View));
            }
        }
        catch { Dispose(); throw; }
    }

    public DeviceBuffer VertexBuffer { get; } = null!;
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public DeviceBuffer? TexturedVertexBuffer { get; }
    public DeviceBuffer IndexBuffer { get; } = null!;
    public IndexFormat IndexFormat { get; }
    public uint IndexCount { get; }
    public string PartKey { get; }
    public string NodeName { get; }
    public string MeshName { get; }
    public Vector4 Color { get; }
    public Vector4 MaterialFactors { get; }
    public ResourceSet? TextureSet { get; }
    public bool IsTextured => TexturedVertexBuffer != null && TextureSet != null;

    public void Dispose()
    {
        TextureSet?.Dispose();
        _textures.Release(_normal); _normal = null;
        _textures.Release(_material); _material = null;
        _textures.Release(_color); _color = null;
        IndexBuffer?.Dispose();
        TexturedVertexBuffer?.Dispose();
        VertexBuffer?.Dispose();
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

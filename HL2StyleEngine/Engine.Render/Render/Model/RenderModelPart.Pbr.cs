using System.Numerics;
using Veldrid;

namespace Engine.Render;

internal sealed partial class RenderModelPart
{
    private static readonly PreparedTexture NeutralPbrMaterial = new("pbr-neutral-metal-rough-v1", 1, 1, TextureSemantic.Material, false, [[255, 255, 255, 255]]);
    private readonly List<ModelTextureCache.SharedTexture> _pbrTextures = new();
    public DeviceBuffer? PbrVertexBuffer { get; private set; }
    public DeviceBuffer? PbrIndexBuffer { get; private set; }
    public uint PbrIndexCount { get; private set; }
    public ResourceSet? PbrTextureSet { get; private set; }
    public Vector4 PbrMaterialFactors { get; private set; }

    private void CreatePbrResources(GraphicsDevice device, LoadedModelPart source, ResourceLayout layout, Sampler sampler, ModelTextureCache textures)
    {
        bool uv = source.TexCoords?.Length == source.Positions.Length;
        bool emissionUv = source.EmissiveTexCoords?.Length == source.Positions.Length || uv;
        var normals = source.PbrNormals ?? source.Normals;
        bool hasNormals = normals?.Length == source.Positions.Length;
        PbrIndexCount = IndexCount;
        if (source.PbrNormals == null && hasNormals && TexturedVertexBuffer != null)
            PbrVertexBuffer = TexturedVertexBuffer;
        else
        {
            var vertices = new TexturedVertex[hasNormals ? source.Positions.Length : source.Indices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int index = hasNormals ? i : (int)source.Indices[i];
                Vector3 normal;
                if (hasNormals) normal = normals![index];
                else
                {
                    int triangle = i / 3 * 3;
                    var a = source.Positions[source.Indices[triangle]];
                    var b = source.Positions[source.Indices[triangle + 1]];
                    var c = source.Positions[source.Indices[triangle + 2]];
                    normal = Vector3.Cross(b - a, c - a);
                }
                normal = float.IsFinite(normal.LengthSquared()) && normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                Vector2 coordinates = uv ? source.TexCoords![index] : Vector2.Zero;
                vertices[i] = new(source.Positions[index], normal, coordinates, source.EmissiveTexCoords is { } euv ? euv[index] : coordinates);
            }
            PbrVertexBuffer = device.ResourceFactory.CreateBuffer(new((uint)vertices.Length * TexturedVertex.SizeInBytes, BufferUsage.VertexBuffer));
            device.UpdateBuffer(PbrVertexBuffer, 0, vertices);
            if (!hasNormals)
            {
                uint[] indices = Enumerable.Range(0, vertices.Length).Select(i => (uint)i).ToArray();
                PbrIndexBuffer = device.ResourceFactory.CreateBuffer(new((uint)indices.Length * sizeof(uint), BufferUsage.IndexBuffer));
                device.UpdateBuffer(PbrIndexBuffer, 0, indices);
            }
        }
        TextureView Acquire(PreparedTexture texture)
        { var shared = textures.Acquire(texture); _pbrTextures.Add(shared); return shared.View; }
        PreparedTexture Prepare(PreparedTexture? prepared, byte[]? encoded, TextureSemantic semantic, PreparedTexture fallback, bool coordinates)
            => coordinates ? prepared ?? (encoded is { Length: > 0 } ? TextureCooker.PrepareImage(encoded, semantic, null, false) : fallback) : fallback;
        PbrTextureSet = device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout,
            Acquire(Prepare(source.BaseColorTexture, source.BaseColorPng, TextureSemantic.Color, DefaultWhite, uv)), sampler,
            Acquire(Prepare(source.MaterialTexture, source.MetallicRoughnessPng, TextureSemantic.Material, NeutralPbrMaterial, uv)), sampler,
            Acquire(Prepare(source.NormalTexture, source.NormalPng, TextureSemantic.Normal, DefaultNormal, uv)),
            Acquire(Prepare(source.EmissiveTexture, source.EmissivePng, TextureSemantic.Color, DefaultWhite, emissionUv))));
        PbrMaterialFactors = new(FiniteFactor(source.MetallicFactor, 1), Math.Max(.04f, FiniteFactor(source.RoughnessFactor, 1)),
            uv && (source.NormalTexture != null || source.NormalPng is { Length: > 0 }) ? 1 : 0, 0);
    }

    private static float FiniteFactor(float value, float fallback) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    private void DisposePbrResources()
    {
        PbrTextureSet?.Dispose();
        foreach (var texture in _pbrTextures) _textures.Release(texture);
        _pbrTextures.Clear();
        if (!ReferenceEquals(PbrVertexBuffer, TexturedVertexBuffer)) PbrVertexBuffer?.Dispose();
        PbrIndexBuffer?.Dispose();
    }
}

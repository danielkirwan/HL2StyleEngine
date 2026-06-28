using System.Numerics;

namespace Engine.Render;

public sealed class LoadedModel
{
    public LoadedModel(IReadOnlyList<LoadedModelPart> parts)
    {
        Parts = parts;
    }

    public IReadOnlyList<LoadedModelPart> Parts { get; }
}

public sealed class LoadedModelPart
{
    public LoadedModelPart(
        Vector3[] positions,
        Vector3[]? normals,
        Vector2[]? texCoords,
        uint[] indices,
        Vector4 color,
        byte[]? baseColorPng,
        byte[]? metallicRoughnessPng,
        float metallicFactor,
        float roughnessFactor)
    {
        Positions = positions;
        Normals = normals;
        TexCoords = texCoords;
        Indices = indices;
        Color = color;
        BaseColorPng = baseColorPng;
        MetallicRoughnessPng = metallicRoughnessPng;
        MetallicFactor = metallicFactor;
        RoughnessFactor = roughnessFactor;
    }

    public Vector3[] Positions { get; }
    public Vector3[]? Normals { get; }
    public Vector2[]? TexCoords { get; }
    public uint[] Indices { get; }
    public Vector4 Color { get; }
    public byte[]? BaseColorPng { get; }
    public byte[]? MetallicRoughnessPng { get; }
    public float MetallicFactor { get; }
    public float RoughnessFactor { get; }
}

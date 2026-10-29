using System.Numerics;

namespace Engine.Render;

public sealed class LoadedModel
{
    public LoadedModel(IReadOnlyList<LoadedModelPart> parts)
    {
        Parts = parts;
    }

    public IReadOnlyList<LoadedModelPart> Parts { get; }

    public LoadedModel MipTail(int firstMip)
    {
        if (firstMip < 0 || firstMip > 14) throw new ArgumentOutOfRangeException(nameof(firstMip));
        if (firstMip == 0) return this;
        return new(Parts.Select(p => new LoadedModelPart(p.Positions, p.Normals, p.TexCoords, p.Indices, p.Color,
            p.BaseColorPng, p.MetallicRoughnessPng, p.MetallicFactor, p.RoughnessFactor, p.NodeName, p.MeshName,
            p.NodeIndex, p.MeshIndex, p.PrimitiveIndex)
        {
            PbrNormals = p.PbrNormals, NormalPng = p.NormalPng, EmissivePng = p.EmissivePng,
            EmissiveFactor = p.EmissiveFactor, EmissiveTexCoords = p.EmissiveTexCoords,
            BaseColorTexture = p.BaseColorTexture?.MipTail(firstMip), MaterialTexture = p.MaterialTexture?.MipTail(firstMip),
            NormalTexture = p.NormalTexture?.MipTail(firstMip), EmissiveTexture = p.EmissiveTexture?.MipTail(firstMip)
        }).ToArray());
    }
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
        float roughnessFactor,
        string nodeName = "",
        string meshName = "",
        int nodeIndex = -1,
        int meshIndex = -1,
        int primitiveIndex = -1)
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
        NodeName = nodeName ?? "";
        MeshName = meshName ?? "";
        NodeIndex = nodeIndex;
        MeshIndex = meshIndex;
        PrimitiveIndex = primitiveIndex;
        PartKey = !string.IsNullOrWhiteSpace(NodeName)
            ? NodeName
            : !string.IsNullOrWhiteSpace(MeshName)
                ? $"{MeshName}#{PrimitiveIndex}"
                : $"mesh{MeshIndex}:primitive{PrimitiveIndex}";
    }

    public Vector3[] Positions { get; }
    public Vector3[]? Normals { get; }
    // Corrected node-space inverse-transpose normals for opt-in PBR. Null shares the legacy array.
    public Vector3[]? PbrNormals { get; init; }
    public Vector2[]? TexCoords { get; }
    public uint[] Indices { get; }
    public Vector4 Color { get; }
    public byte[]? BaseColorPng { get; }
    public byte[]? MetallicRoughnessPng { get; }
    public byte[]? NormalPng { get; init; }
    public byte[]? EmissivePng { get; init; }
    public Vector3 EmissiveFactor { get; init; }
    public Vector2[]? EmissiveTexCoords { get; init; }
    public PreparedTexture? BaseColorTexture { get; set; }
    public PreparedTexture? MaterialTexture { get; set; }
    public PreparedTexture? NormalTexture { get; set; }
    public PreparedTexture? EmissiveTexture { get; set; }
    public float MetallicFactor { get; }
    public float RoughnessFactor { get; }
    public string NodeName { get; }
    public string MeshName { get; }
    public int NodeIndex { get; }
    public int MeshIndex { get; }
    public int PrimitiveIndex { get; }
    public string PartKey { get; }
}

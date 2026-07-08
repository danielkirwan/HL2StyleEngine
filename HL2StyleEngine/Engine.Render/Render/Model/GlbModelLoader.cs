using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Engine.Render;

public static class GlbModelLoader
{
    private const uint Magic = 0x46546C67; // glTF
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static LoadedModel Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 20)
            throw new InvalidDataException("GLB file is too small.");

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        if (magic != Magic)
            throw new InvalidDataException("File is not a binary GLB.");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        if (version != 2)
            throw new InvalidDataException($"Unsupported GLB version {version}.");

        int declaredLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)));
        if (declaredLength > bytes.Length)
            throw new InvalidDataException("GLB length header is larger than the file.");

        ReadOnlyMemory<byte> jsonBytes = default;
        ReadOnlyMemory<byte> binBytes = default;
        int offset = 12;
        while (offset + 8 <= declaredLength)
        {
            int chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;

            if (chunkLength < 0 || offset + chunkLength > bytes.Length)
                throw new InvalidDataException("GLB chunk extends past the end of the file.");

            if (chunkType == JsonChunkType)
                jsonBytes = bytes.AsMemory(offset, chunkLength);
            else if (chunkType == BinChunkType)
                binBytes = bytes.AsMemory(offset, chunkLength);

            offset += chunkLength;
        }

        if (jsonBytes.IsEmpty || binBytes.IsEmpty)
            throw new InvalidDataException("GLB must contain JSON and BIN chunks.");

        string json = Encoding.UTF8.GetString(jsonBytes.Span).TrimEnd('\0', ' ', '\r', '\n', '\t');
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<LoadedModelPart> parts = new();

        if (!root.TryGetProperty("meshes", out JsonElement meshes))
            throw new InvalidDataException("GLB contains no meshes.");

        if (root.TryGetProperty("nodes", out JsonElement nodes))
        {
            bool[] visited = new bool[nodes.GetArrayLength()];
            if (root.TryGetProperty("scenes", out JsonElement scenes))
            {
                int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneElement)
                    ? sceneElement.GetInt32()
                    : 0;

                if (sceneIndex >= 0 && sceneIndex < scenes.GetArrayLength() &&
                    scenes[sceneIndex].TryGetProperty("nodes", out JsonElement sceneNodes))
                {
                    for (int i = 0; i < sceneNodes.GetArrayLength(); i++)
                        AppendNodeParts(root, nodes, meshes, sceneNodes[i].GetInt32(), binBytes, Matrix4x4.Identity, visited, parts);
                }
            }

            for (int i = 0; i < nodes.GetArrayLength(); i++)
            {
                if (!visited[i])
                    AppendNodeParts(root, nodes, meshes, i, binBytes, Matrix4x4.Identity, visited, parts);
            }
        }
        else
        {
            for (int i = 0; i < meshes.GetArrayLength(); i++)
                AppendMeshParts(root, meshes[i], binBytes, Matrix4x4.Identity, parts, "", -1, i);
        }

        if (parts.Count == 0)
            throw new InvalidDataException("GLB contained no supported mesh primitives.");

        return new LoadedModel(parts);
    }

    private static void AppendNodeParts(
        JsonElement root,
        JsonElement nodes,
        JsonElement meshes,
        int nodeIndex,
        ReadOnlyMemory<byte> binBytes,
        Matrix4x4 parentTransform,
        bool[] visited,
        List<LoadedModelPart> parts)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
            return;

        visited[nodeIndex] = true;
        JsonElement node = nodes[nodeIndex];
        Matrix4x4 nodeTransform = ReadNodeTransform(node) * parentTransform;

        if (node.TryGetProperty("mesh", out JsonElement meshElement))
        {
            int meshIndex = meshElement.GetInt32();
            AppendMeshParts(root, meshes[meshIndex], binBytes, nodeTransform, parts, ReadName(node, $"Node{nodeIndex}"), nodeIndex, meshIndex);
        }

        if (!node.TryGetProperty("children", out JsonElement children))
            return;

        for (int i = 0; i < children.GetArrayLength(); i++)
            AppendNodeParts(root, nodes, meshes, children[i].GetInt32(), binBytes, nodeTransform, visited, parts);
    }

    private static void AppendMeshParts(
        JsonElement root,
        JsonElement mesh,
        ReadOnlyMemory<byte> binBytes,
        Matrix4x4 transform,
        List<LoadedModelPart> parts,
        string nodeName,
        int nodeIndex,
        int meshIndex)
    {
        if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
            return;

        string meshName = ReadName(mesh, meshIndex >= 0 ? $"Mesh{meshIndex}" : "Mesh");

        for (int i = 0; i < primitives.GetArrayLength(); i++)
        {
            JsonElement primitive = primitives[i];
            if (primitive.TryGetProperty("mode", out JsonElement modeElement) && modeElement.GetInt32() != 4)
                continue;

            if (!primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                !attributes.TryGetProperty("POSITION", out JsonElement positionAccessorElement))
            {
                continue;
            }

            Vector3[] positions = ReadVec3Accessor(root, positionAccessorElement.GetInt32(), binBytes);
            for (int v = 0; v < positions.Length; v++)
                positions[v] = Vector3.Transform(positions[v], transform);

            Vector3[]? normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessorElement)
                ? ReadVec3Accessor(root, normalAccessorElement.GetInt32(), binBytes)
                : null;
            if (normals != null)
            {
                for (int n = 0; n < normals.Length; n++)
                    normals[n] = Vector3.Normalize(Vector3.TransformNormal(normals[n], transform));
            }

            Vector2[]? texCoords = attributes.TryGetProperty("TEXCOORD_0", out JsonElement texCoordAccessorElement)
                ? ReadVec2Accessor(root, texCoordAccessorElement.GetInt32(), binBytes)
                : null;

            uint[] indices = primitive.TryGetProperty("indices", out JsonElement indicesAccessorElement)
                ? ReadIndexAccessor(root, indicesAccessorElement.GetInt32(), binBytes)
                : CreateSequentialIndices(positions.Length);

            Vector4 color = ReadMaterialColor(root, primitive);
            byte[]? baseColorPng = ReadBaseColorImageBytes(root, primitive, binBytes);
            byte[]? metallicRoughnessPng = ReadMaterialImageBytes(root, primitive, binBytes, "metallicRoughnessTexture");
            float metallicFactor = ReadMaterialScalar(root, primitive, "metallicFactor", defaultValue: 1f);
            float roughnessFactor = ReadMaterialScalar(root, primitive, "roughnessFactor", defaultValue: 1f);
            parts.Add(new LoadedModelPart(
                positions,
                normals,
                texCoords,
                indices,
                color,
                baseColorPng,
                metallicRoughnessPng,
                metallicFactor,
                roughnessFactor,
                nodeName,
                meshName,
                nodeIndex,
                meshIndex,
                i));
        }
    }


    private static string ReadName(JsonElement element, string fallback)
        => element.TryGetProperty("name", out JsonElement nameElement) && !string.IsNullOrWhiteSpace(nameElement.GetString())
            ? nameElement.GetString()!
            : fallback;
    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixElement) && matrixElement.GetArrayLength() == 16)
        {
            float[] m = ReadFloatArray(matrixElement, 16);
            return new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);
        }

        Vector3 translation = node.TryGetProperty("translation", out JsonElement t) && t.GetArrayLength() == 3
            ? new Vector3(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle())
            : Vector3.Zero;

        Vector3 scale = node.TryGetProperty("scale", out JsonElement s) && s.GetArrayLength() == 3
            ? new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle())
            : Vector3.One;

        Quaternion rotation = node.TryGetProperty("rotation", out JsonElement r) && r.GetArrayLength() == 4
            ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle())
            : Quaternion.Identity;

        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static Vector3[] ReadVec3Accessor(JsonElement root, int accessorIndex, ReadOnlyMemory<byte> binBytes)
    {
        JsonElement accessor = root.GetProperty("accessors")[accessorIndex];
        if (!string.Equals(accessor.GetProperty("type").GetString(), "VEC3", StringComparison.OrdinalIgnoreCase) ||
            accessor.GetProperty("componentType").GetInt32() != 5126)
        {
            throw new InvalidDataException("Only float VEC3 POSITION accessors are supported.");
        }

        AccessorSpan span = GetAccessorSpan(root, accessor, binBytes, 12);
        Vector3[] values = new Vector3[span.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = span.Bytes.Span.Slice(i * span.Stride, 12);
            values[i] = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(item.Slice(0, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(item.Slice(4, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(item.Slice(8, 4)));
        }

        return values;
    }

    private static uint[] ReadIndexAccessor(JsonElement root, int accessorIndex, ReadOnlyMemory<byte> binBytes)
    {
        JsonElement accessor = root.GetProperty("accessors")[accessorIndex];
        int componentType = accessor.GetProperty("componentType").GetInt32();
        int itemSize = componentType switch
        {
            5121 => 1,
            5123 => 2,
            5125 => 4,
            _ => throw new InvalidDataException($"Unsupported index component type {componentType}.")
        };

        AccessorSpan span = GetAccessorSpan(root, accessor, binBytes, itemSize);
        uint[] values = new uint[span.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = span.Bytes.Span.Slice(i * span.Stride, itemSize);
            values[i] = componentType switch
            {
                5121 => item[0],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(item),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(item),
                _ => 0
            };
        }

        return values;
    }

    private static Vector2[] ReadVec2Accessor(JsonElement root, int accessorIndex, ReadOnlyMemory<byte> binBytes)
    {
        JsonElement accessor = root.GetProperty("accessors")[accessorIndex];
        if (!string.Equals(accessor.GetProperty("type").GetString(), "VEC2", StringComparison.OrdinalIgnoreCase) ||
            accessor.GetProperty("componentType").GetInt32() != 5126)
        {
            throw new InvalidDataException("Only float VEC2 TEXCOORD_0 accessors are supported.");
        }

        AccessorSpan span = GetAccessorSpan(root, accessor, binBytes, 8);
        Vector2[] values = new Vector2[span.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = span.Bytes.Span.Slice(i * span.Stride, 8);
            values[i] = new Vector2(
                BinaryPrimitives.ReadSingleLittleEndian(item.Slice(0, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(item.Slice(4, 4)));
        }

        return values;
    }

    private static AccessorSpan GetAccessorSpan(JsonElement root, JsonElement accessor, ReadOnlyMemory<byte> binBytes, int naturalStride)
    {
        int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
        int bufferViewIndex = accessor.GetProperty("bufferView").GetInt32();
        int count = accessor.GetProperty("count").GetInt32();
        JsonElement bufferView = root.GetProperty("bufferViews")[bufferViewIndex];
        int viewOffset = bufferView.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int viewLength = bufferView.GetProperty("byteLength").GetInt32();
        int stride = bufferView.TryGetProperty("byteStride", out JsonElement strideElement)
            ? strideElement.GetInt32()
            : naturalStride;

        int start = viewOffset + accessorOffset;
        int required = count <= 0 ? 0 : (count - 1) * stride + naturalStride;
        if (start < 0 || required < 0 || start + required > binBytes.Length || accessorOffset > viewLength)
            throw new InvalidDataException("Accessor points outside the GLB BIN chunk.");

        return new AccessorSpan(binBytes.Slice(start, required), count, stride);
    }

    private static Vector4 ReadMaterialColor(JsonElement root, JsonElement primitive)
    {
        if (!primitive.TryGetProperty("material", out JsonElement materialElement) ||
            !root.TryGetProperty("materials", out JsonElement materials))
        {
            return Vector4.One;
        }

        JsonElement material = materials[materialElement.GetInt32()];
        if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) ||
            !pbr.TryGetProperty("baseColorFactor", out JsonElement colorElement) ||
            colorElement.GetArrayLength() != 4)
        {
            return Vector4.One;
        }

        return new Vector4(
            colorElement[0].GetSingle(),
            colorElement[1].GetSingle(),
            colorElement[2].GetSingle(),
            colorElement[3].GetSingle());
    }

    private static byte[]? ReadBaseColorImageBytes(JsonElement root, JsonElement primitive, ReadOnlyMemory<byte> binBytes)
        => ReadMaterialImageBytes(root, primitive, binBytes, "baseColorTexture");

    private static byte[]? ReadMaterialImageBytes(JsonElement root, JsonElement primitive, ReadOnlyMemory<byte> binBytes, string texturePropertyName)
    {
        if (!primitive.TryGetProperty("material", out JsonElement materialElement) ||
            !root.TryGetProperty("materials", out JsonElement materials))
        {
            return null;
        }

        JsonElement material = materials[materialElement.GetInt32()];
        if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) ||
            !pbr.TryGetProperty(texturePropertyName, out JsonElement textureElement) ||
            !textureElement.TryGetProperty("index", out JsonElement textureIndexElement) ||
            !root.TryGetProperty("textures", out JsonElement textures) ||
            !root.TryGetProperty("images", out JsonElement images))
        {
            return null;
        }

        JsonElement texture = textures[textureIndexElement.GetInt32()];
        if (!texture.TryGetProperty("source", out JsonElement sourceElement))
            return null;

        JsonElement image = images[sourceElement.GetInt32()];
        if (!image.TryGetProperty("bufferView", out JsonElement bufferViewElement))
            return null;

        JsonElement bufferView = root.GetProperty("bufferViews")[bufferViewElement.GetInt32()];
        int viewOffset = bufferView.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
        int viewLength = bufferView.GetProperty("byteLength").GetInt32();
        if (viewOffset < 0 || viewLength <= 0 || viewOffset + viewLength > binBytes.Length)
            return null;

        return binBytes.Slice(viewOffset, viewLength).ToArray();
    }

    private static float ReadMaterialScalar(JsonElement root, JsonElement primitive, string propertyName, float defaultValue)
    {
        if (!primitive.TryGetProperty("material", out JsonElement materialElement) ||
            !root.TryGetProperty("materials", out JsonElement materials))
        {
            return defaultValue;
        }

        JsonElement material = materials[materialElement.GetInt32()];
        if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) ||
            !pbr.TryGetProperty(propertyName, out JsonElement valueElement))
        {
            return defaultValue;
        }

        return valueElement.GetSingle();
    }

    private static uint[] CreateSequentialIndices(int count)
    {
        uint[] indices = new uint[count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = (uint)i;

        return indices;
    }

    private static float[] ReadFloatArray(JsonElement element, int expectedCount)
    {
        float[] values = new float[expectedCount];
        for (int i = 0; i < expectedCount; i++)
            values[i] = element[i].GetSingle();

        return values;
    }

    private readonly struct AccessorSpan
    {
        public AccessorSpan(ReadOnlyMemory<byte> bytes, int count, int stride)
        {
            Bytes = bytes;
            Count = count;
            Stride = stride;
        }

        public ReadOnlyMemory<byte> Bytes { get; }
        public int Count { get; }
        public int Stride { get; }
    }
}

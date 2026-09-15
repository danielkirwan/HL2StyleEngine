using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Engine.Render;

internal static class RollupDoorAssets
{
    internal const string LeafPath = "Content/Models/Puzzles/RollupDoor1_Leaf.glb";
    internal const string FramePath = "Content/Models/Puzzles/RollupDoor1_Frame.glb";

    internal static void Prepare(string root)
    {
        string source = Path.Combine(root, "Game/Content/Models/ViewModels/Rollup Door 1.glb");
        ExtractNode(source, Path.Combine(root, "Game", LeafPath), "Rollup_Door_1_Door");
        ExtractNode(source, Path.Combine(root, "Game", FramePath), "Rollup_Door_1_Door_Frame");
    }

    private static void ExtractNode(string source, string output, string nodeName)
    {
        byte[] original = File.ReadAllBytes(source);
        using var input = new BinaryReader(new MemoryStream(original));
        if (input.ReadUInt32() != 0x46546C67 || input.ReadUInt32() != 2 || input.ReadUInt32() != original.Length)
            throw new InvalidDataException("Expected a GLB 2.0 file.");
        int length = input.ReadInt32();
        if (input.ReadUInt32() != 0x4E4F534A) throw new InvalidDataException("Missing GLB JSON.");
        var json = JsonNode.Parse(input.ReadBytes(length))!;
        bool found = false;
        foreach (JsonObject node in json["nodes"]!.AsArray().Cast<JsonObject>())
        {
            if (node["name"]?.GetValue<string>() == nodeName) found = true;
            else node.Remove("mesh");
        }
        if (!found) throw new InvalidDataException("Missing rollup node: " + nodeName);

        // Retain original hierarchy, vertex buffers, embedded images and materials byte-for-byte.
        byte[] tail = input.ReadBytes((int)(input.BaseStream.Length - input.BaseStream.Position));
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        int padded = (jsonBytes.Length + 3) & ~3;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x46546C67u); writer.Write(2u); writer.Write((uint)(20 + padded + tail.Length));
            writer.Write(padded); writer.Write(0x4E4F534Au); writer.Write(jsonBytes);
            for (int i = jsonBytes.Length; i < padded; i++) writer.Write((byte)' ');
            writer.Write(tail);
        }
        byte[] result = stream.ToArray();
        if (File.Exists(output))
        {
            if (!File.ReadAllBytes(output).SequenceEqual(result))
                throw new IOException("Refusing to overwrite edited derived asset: " + output);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, result);
    }

    internal static (Vector3 Min, Vector3 Max) Bounds(LoadedModel model)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var part in model.Parts)
        foreach (Vector3 point in part.Positions)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return (min, max);
    }
}

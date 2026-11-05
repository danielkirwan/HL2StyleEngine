using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Render;

internal static class FloorMaterial
{
    internal const string Original = "Content/Models/ViewModels/Basement_Corridor_B_Floor_4x4m.glb";
    internal const string Corrected = PbrTestRoom.ModelFolder + "BasementFloorPbr.glb";
    internal const float Strength = .5f;

    internal static void Prepare(string root)
    {
        string source = Path.Combine(root, "Game", Original), path = Path.Combine(root, "Game", Corrected);
        byte[] result = Correct(File.ReadAllBytes(source));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).SequenceEqual(result))
                throw new IOException("The floor variant has author edits; refusing to overwrite: " + path);
        }
        else File.WriteAllBytes(path, result);
        TextureCooker.CookModel(path);

        string levelPath = Path.Combine(root, "Game/Content/Levels", ShadowStabilityChecks.FileName);
        var level = JsonNode.Parse(File.ReadAllText(levelPath))!;
        var before = level.DeepClone();
        var changed = new List<int>();
        var entities = level["Entities"]!.AsArray();
        for (int i = 0; i < entities.Count; i++)
            if (entities[i]?["MeshPath"]?.GetValue<string>() == Original)
            {
                entities[i]!["MeshPath"] = Corrected;
                changed.Add(i);
            }
        var restored = level.DeepClone();
        foreach (int i in changed) restored["Entities"]![i]!["MeshPath"] = Original;
        Require(JsonNode.DeepEquals(before, restored), "Floor assignment changed unrelated level data.");
        if (changed.Count > 0)
        {
            string temp = levelPath + ".floor-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, level.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                File.Move(temp, levelPath, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        Console.WriteLine($"Floor material prepared: {changed.Count} assignments updated; all other scene fields preserved. Re-cook both lighting states before launching.");
    }

    private static byte[] Correct(byte[] source)
    {
        Require(source.Length >= 28 && BinaryPrimitives.ReadUInt32LittleEndian(source) == 0x46546c67 &&
            BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)) == 2 &&
            BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8)) == source.Length &&
            BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(16)) == 0x4e4f534a, "Expected GLB v2.");
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)));
        Require(length >= 0 && length <= source.Length - 28 && length % 4 == 0, "Invalid JSON chunk.");
        int binHeader = 20 + length;
        int binLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binHeader)));
        Require(binLength == source.Length - binHeader - 8 && binLength % 4 == 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binHeader + 4)) == 0x004e4942, "Expected one BIN chunk.");
        var json = JsonNode.Parse(source.AsSpan(20, length))!;
        Require(json["buffers"]!.AsArray().Count == 1 && json["materials"]!.AsArray().Count == 1, "Reassess changed floor asset layout.");
        int texture = json["materials"]![0]!["normalTexture"]!["index"]!.GetValue<int>();
        int image = json["textures"]![texture]!["source"]!.GetValue<int>();
        var imageNode = json["images"]![image]!;
        Require(imageNode["mimeType"]!.GetValue<string>() == "image/png", "Expected an embedded normal PNG.");
        var views = json["bufferViews"]!.AsArray();
        var view = views[imageNode["bufferView"]!.GetValue<int>()]!;
        int offset = view["byteOffset"]?.GetValue<int>() ?? 0, size = view["byteLength"]!.GetValue<int>();
        Require(view["buffer"]!.GetValue<int>() == 0 && offset >= 0 && size > 0 && offset <= binLength - size, "Invalid normal image view.");
        byte[] normal = ReduceNormalStrength(source.AsSpan(binHeader + 8 + offset, size).ToArray(), Strength);
        imageNode["bufferView"] = views.Count;
        views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = binLength, ["byteLength"] = normal.Length });
        int newBinLength = (checked(binLength + normal.Length) + 3) & ~3;
        json["buffers"]![0]!["byteLength"] = binLength + normal.Length;
        byte[] text = JsonSerializer.SerializeToUtf8Bytes(json);
        int jsonPadded = (text.Length + 3) & ~3;
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result);
        writer.Write(0x46546c67u); writer.Write(2u); writer.Write(28 + jsonPadded + newBinLength);
        writer.Write(jsonPadded); writer.Write(0x4e4f534au); writer.Write(text);
        for (int i = text.Length; i < jsonPadded; i++) writer.Write((byte)' ');
        writer.Write(newBinLength); writer.Write(0x004e4942u);
        // Keep original geometry, colour and material images byte-for-byte; only redirect the normal image.
        writer.Write(source.AsSpan(binHeader + 8)); writer.Write(normal);
        for (int i = binLength + normal.Length; i < newBinLength; i++) writer.Write((byte)0);
        return result.ToArray();
    }

    internal static void Validate(string root)
    {
        string source = Path.Combine(root, "Game", Original), path = Path.Combine(root, "Game", Corrected);
        Require(Correct(File.ReadAllBytes(source)).SequenceEqual(File.ReadAllBytes(path)), "Floor variant is not reproducible.");
        var a = GlbModelLoader.Load(source); var b = GlbModelLoader.Load(path); var c = TextureCooker.LoadForRendering(path);
        Require(a.Parts.Count > 0 && a.Parts.Count == b.Parts.Count && b.Parts.Count == c.Parts.Count, "Lost floor parts.");
        for (int i = 0; i < a.Parts.Count; i++)
        {
            var p = a.Parts[i]; var q = b.Parts[i];
            foreach (var r in new[] { q, c.Parts[i] })
                Require(p.Positions.SequenceEqual(r.Positions) && p.Indices.SequenceEqual(r.Indices) &&
                    Equal(p.Normals, r.Normals) && Equal(p.PbrNormals, r.PbrNormals) && Equal(p.TexCoords, r.TexCoords) &&
                    p.PartKey == r.PartKey && p.Color == r.Color && p.MetallicFactor == r.MetallicFactor && p.RoughnessFactor == r.RoughnessFactor,
                    "Floor geometry, UVs, keys or other material factors changed.");
            Require(Equal(p.BaseColorPng, q.BaseColorPng) && Equal(p.MetallicRoughnessPng, q.MetallicRoughnessPng) &&
                Equal(p.EmissivePng, q.EmissivePng) && !Equal(p.NormalPng, q.NormalPng), "Unexpected image changes.");
            Require(c.Parts[i].NormalTexture is { Compressed: true }, "Normal variant was not BC5 cooked.");
        }
        Console.WriteLine("PASS: deterministic normal-only floor variant; geometry/UV/colour/material parity, BC5 cook.");
    }

    private static bool Equal<T>(T[]? a, T[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }

    internal static byte[] ReduceNormalStrength(byte[] png, float strength)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The texture cooker uses Windows image decoding.");
        if (!float.IsFinite(strength) || strength <= 0 || strength > 1)
            throw new ArgumentOutOfRangeException(nameof(strength));
        using var input = new MemoryStream(png);
        using var image = new Bitmap(input);
        using var output = image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
        var data = output.LockBits(new Rectangle(0, 0, output.Width, output.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[output.Width * 4];
            for (int y = 0; y < output.Height; y++)
            {
                IntPtr address = data.Scan0 + y * data.Stride;
                Marshal.Copy(address, row, 0, row.Length);
                for (int x = 0; x < row.Length; x += 4)
                {
                    // Match the renderer's BC5 normal reconstruction, then attenuate tangent slopes.
                    float nx = row[x + 2] / 127.5f - 1, ny = row[x + 1] / 127.5f - 1;
                    var n = Vector3.Normalize(new Vector3(nx * strength, ny * strength, MathF.Sqrt(MathF.Max(.0001f, 1 - nx * nx - ny * ny))));
                    row[x + 2] = Encode(n.X); row[x + 1] = Encode(n.Y); row[x] = Encode(n.Z);
                }
                Marshal.Copy(row, 0, address, row.Length);
            }
        }
        finally { output.UnlockBits(data); }
        using var result = new MemoryStream();
        output.Save(result, ImageFormat.Png);
        return result.ToArray();
    }

    private static byte Encode(float n) => (byte)Math.Clamp((int)MathF.Round((n * .5f + .5f) * 255), 0, 255);
}

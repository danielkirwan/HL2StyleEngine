using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;

internal static class PbrTestRoom
{
    public const string FileName = "pbrMaterialTest.json";
    public const string ModelFolder = "Content/Models/RenderingTests/";

    public static void Build(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        if (File.Exists(path)) throw new IOException("The PBR test already exists; existing edits will not be overwritten.");
        PbrCrateChecks.Prepare(root);
        Directory.CreateDirectory(Path.Combine(root, "Game", ModelFolder));
        var sphere = Sphere();
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 5; column++)
            WriteGlb(Path.Combine(root, "Game", ModelFolder, $"Sphere_{row}_{column}.glb"), sphere,
                row, new[] { 0f, .15f, .35f, .65f, 1f }[column], new(.65f, .34f, .12f, 1));
        WriteGlb(Path.Combine(root, "Game", ModelFolder, "ScalarNoNormals.glb"),
            new LoadedModelPart(sphere.Positions, null, null, sphere.Indices, Vector4.One, null, null, .5f, .5f), .5f, .5f, new(.5f, .6f, .7f, 1));

        var level = new LevelFile { LightingModel = SurfaceLightingModel.Pbr, ReflectionEnvironment = ReflectionPreset.SyntheticStudio, UsePointLights = true, AmbientLight = .025f, DirectionalLight = .1f, Exposure = 1 };
        string Id(string name) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("pbr-fixture/" + name))).ToLowerInvariant();
        LevelEntityDef Add(string name, string model, Vector3 position, Vector3 size, float yaw = 0)
        {
            var e = new LevelEntityDef { Id = Id(name), Name = name, Type = EntityTypes.RigidBody, MeshPath = model,
                LocalPosition = position, LocalRotationEulerDeg = new(0, yaw, 0), Size = size, Color = Vector4.One,
                MotionType = MotionType.Static, Mass = 0, Shape = "Box", Restitution = 0,
                StaticLighting = model.Contains("Basement_Corridor", StringComparison.Ordinal) };
            level.Entities.Add(e); return e;
        }
        const string assets = "Content/Models/ViewModels/";
        const string wall = assets + "Basement_Corridor_A_Wall_Double_Sided_4m.glb";
        for (int x = -2; x <= 2; x += 4)
        for (int z = -2; z <= 10; z += 4)
        {
            Add($"Floor_{x}_{z}", assets + "Basement_Corridor_B_Floor_4x4m.glb", new(x, -.1f, z), new(4, .2f, 4));
            Add($"Ceiling_{x}_{z}", assets + "Basement_Corridor_A_Ceiling_4x4m.glb", new(x, 4.1f, z), new(4, .2f, 4));
        }
        for (int z = -2; z <= 10; z += 4)
        {
            Add($"East_{z}", wall, new(4.1f, 2, z), new(.2f, 4, 4));
            Add($"West_{z}", wall, new(-4.1f, 2, z), new(.2f, 4, 4));
        }
        for (int x = -2; x <= 2; x += 4)
        {
            Add($"South_{x}", wall, new(x, 2, -4.1f), new(.2f, 4, 4), 90);
            Add($"North_{x}", wall, new(x, 2, 12.1f), new(.2f, 4, 4), 90);
        }
        // A real opening, not a solid collider hidden behind a doorway visual.
        Add("DoorwayLeft", wall, new(-2.65f, 2, 4), new(.2f, 4, 2.7f), 90);
        Add("DoorwayRight", wall, new(2.65f, 2, 4), new(.2f, 4, 2.7f), 90);
        Add("DoorwayTop", wall, new(0, 3.5f, 4), new(.2f, 1, 2.6f), 90);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 5; column++)
        {
            var e = Add($"{(row == 0 ? "Dielectric" : "Metal")}_Roughness_{column}",
                ModelFolder + $"Sphere_{row}_{column}.glb", new(-2.8f + column * 1.4f, .85f + row * 1.45f, 1.8f), new(.9f));
            e.Type = EntityTypes.Prop;
        }
        var noNormal = Add("ScalarNoNormals_NonUniform", ModelFolder + "ScalarNoNormals.glb", new(2.8f, .8f, -1), new(.7f, 1.2f, .9f));
        noNormal.Type = EntityTypes.Prop;
        var crate = Add("MovableComparisonCrate", PbrCrateChecks.Corrected, new(-2.5f, .55f, -.5f), Vector3.One);
        crate.MotionType = MotionType.Dynamic; crate.CanPickUp = true; crate.Mass = 12;
        var warm = new LevelEntityDef { Id = Id("warm"), Name = "MaterialPointLight", Type = EntityTypes.PointLight,
            LocalPosition = new(-1.5f, 2.8f, -1.3f), LightColor = new(1, .85f, .65f, 1), Intensity = 6, Range = 10, CastShadows = true };
        var cool = new LevelEntityDef { Id = Id("cool"), Name = "SecondRoomSpot", Type = EntityTypes.PointLight,
            LocalPosition = new(0, 3.4f, 8), LocalRotationEulerDeg = new(-90, 0, 0), IsSpotLight = true,
            SpotAngleDeg = 110, LightColor = new(.25f, .5f, 1, 1), Intensity = 7, Range = 9, CastShadows = true };
        level.Entities.Add(warm); level.Entities.Add(cool);
        var screen = Add("SwitchedScreen", assets + "Console 1.glb", new(-2.7f, 1.25f, 7), new(1.2f, .8f, .8f));
        screen.EmissionLightId = cool.Id; screen.EmissionIntensity = 2;
        var lever = Add("ScreenLightSwitch", assets + "Lever04.glb", new(-2.5f, 1.1f, 5.5f), new(.25f, .5f, .25f));
        lever.Type = EntityTypes.Prop; lever.Interaction = new() { Kind = "LightSwitch", Targets = [cool.Id] };
        level.Entities.Add(new() { Id = Id("spawn"), Name = "Spawn", Type = EntityTypes.PlayerSpawn, LocalPosition = new(0, 0, -2.7f) });
        LevelIO.Save(path, level);
        Console.WriteLine($"Created {path}: {level.Entities.Count} entities, two connected rooms, scalar/no-UV ramps and existing GLBs.");
    }

    internal static LoadedModelPart Sphere()
    {
        // Reuse the renderer's sphere topology; this fixture does not introduce a separate mesh implementation.
        var mesh = ((Vector3[] vertices, ushort[] indices))typeof(BasicWorldRenderer)
            .GetMethod("CreateSphereMesh", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [32, 48])!;
        var indices = mesh.indices.Select(i => (uint)i).ToArray();
        for (int i = 0; i < indices.Length; i += 3) (indices[i+1],indices[i+2]) = (indices[i+2],indices[i+1]);
        return new(mesh.vertices, mesh.vertices.Select(Vector3.Normalize).ToArray(), null,
            indices, Vector4.One, null, null, 0, .5f);
    }

    internal static void RefreshNormalFixture(string root)
    {
        var sphere = Sphere();
        string path = Path.Combine(root,"Game",ModelFolder,"ScalarNoNormals.glb");
        WriteGlb(path,new(sphere.Positions,null,null,sphere.Indices,Vector4.One,null,null,.5f,.5f),.5f,.5f,new(.5f,.6f,.7f,1),overwrite:true);
        TextureCooker.CookModel(path);
    }

    internal static void WriteGlb(string path, LoadedModelPart part, float metallic, float roughness, Vector4 color, Vector3? nodeScale = null, bool overwrite = false)
    {
        using var binary = new MemoryStream(); using var writer = new BinaryWriter(binary);
        var views = new List<object>(); var accessors = new List<object>();
        int Floats(float[] values, string type, int stride)
        {
            int start = (int)binary.Position;
            foreach (float v in values) writer.Write(v);
            views.Add(new { buffer = 0, byteOffset = start, byteLength = values.Length * 4 });
            accessors.Add(new { bufferView = views.Count - 1, componentType = 5126, count = values.Length / stride, type });
            return accessors.Count - 1;
        }
        var attributes = new Dictionary<string, int> { ["POSITION"] = Floats(part.Positions.SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray(), "VEC3", 3) };
        if (part.Normals != null) attributes["NORMAL"] = Floats(part.Normals.SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray(), "VEC3", 3);
        if (part.TexCoords != null) attributes["TEXCOORD_0"] = Floats(part.TexCoords.SelectMany(v => new[] { v.X, v.Y }).ToArray(), "VEC2", 2);
        views.Add(new { buffer = 0, byteOffset = (int)binary.Position, byteLength = part.Indices.Length * 4 });
        foreach (uint i in part.Indices) writer.Write(i);
        int indices = accessors.Count;
        accessors.Add(new { bufferView = views.Count - 1, componentType = 5125, count = part.Indices.Length, type = "SCALAR" });
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { asset = new { version = "2.0" }, buffers = new[] { new { byteLength = (int)binary.Length } }, bufferViews = views, accessors,
            materials = new[] { new { pbrMetallicRoughness = new { baseColorFactor = new[] { color.X, color.Y, color.Z, color.W }, metallicFactor = metallic, roughnessFactor = roughness } } },
            meshes = new[] { new { primitives = new[] { new { attributes, indices, material = 0 } } } }, nodes = new[] { new { mesh = 0, scale = new[] { nodeScale?.X ?? 1, nodeScale?.Y ?? 1, nodeScale?.Z ?? 1 } } }, scenes = new[] { new { nodes = new[] { 0 } } }, scene = 0 });
        int pad = (4 - json.Length % 4) % 4;
        using var file = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew); using var output = new BinaryWriter(file);
        output.Write(0x46546C67u); output.Write(2u); output.Write(28 + json.Length + pad + (int)binary.Length);
        output.Write(json.Length + pad); output.Write(0x4E4F534Au); output.Write(json);
        for (int i = 0; i < pad; i++) output.Write((byte)' ');
        output.Write((int)binary.Length); output.Write(0x004E4942u); output.Write(binary.ToArray());
    }
}

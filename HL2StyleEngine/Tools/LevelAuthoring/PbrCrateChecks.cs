using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class PbrCrateChecks
{
    internal const string Original = "Content/Models/ViewModels/Breakable_Wooden_Crate.glb";
    internal const string Corrected = PbrTestRoom.ModelFolder + "WoodenCratePbr.glb";

    internal static void Prepare(string root)
    {
        byte[] source = File.ReadAllBytes(Path.Combine(root, "Game", Original));
        byte[] result = CorrectMaterial(source);
        string path = Path.Combine(root, "Game", Corrected);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Never silently overwrite an edited fixture asset.
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).SequenceEqual(result))
                throw new IOException("The corrected crate already exists with different content: " + path);
        }
        else File.WriteAllBytes(path, result);
        TextureCooker.CookModel(path);
        Console.WriteLine("Prepared dielectric wood fixture (metallic 0, roughness 0.85): " + path);
    }

    private static int JsonLength(byte[] glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546c67 ||
            BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)) != glb.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)) != 0x4e4f534a)
            throw new InvalidDataException("Expected a GLB v2 with a leading JSON chunk.");
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        if (length > glb.Length - 20 || length % 4 != 0) throw new InvalidDataException("Invalid GLB JSON length.");
        return (int)length;
    }

    private static byte[] CorrectMaterial(byte[] source)
    {
        int length = JsonLength(source);
        var document = JsonNode.Parse(source.AsSpan(20, length))!;
        var materials = document["materials"]!.AsArray();
        if (materials.Count != 1) throw new InvalidDataException("Reassess the wood fixture: source material count changed.");
        var material = materials[0]!["pbrMetallicRoughness"]!.AsObject();
        material["metallicFactor"] = 0f;
        material["roughnessFactor"] = .85f;
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(document);
        int padded = (json.Length + 3) & ~3;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546c67u); writer.Write(2u); writer.Write(source.Length - length + padded);
        writer.Write(padded); writer.Write(0x4e4f534au); writer.Write(json);
        for (int i = json.Length; i < padded; i++) writer.Write((byte)' ');
        // Preserve every geometry/image byte and any subsequent chunks, including alignment.
        writer.Write(source.AsSpan(20 + length));
        return stream.ToArray();
    }

    public static void Run(string root)
    {
        string originalPath = Path.Combine(root, "Game", Original), path = Path.Combine(root, "Game", Corrected);
        byte[] source = File.ReadAllBytes(originalPath), corrected = File.ReadAllBytes(path);
        Require(CorrectMaterial(source).SequenceEqual(corrected), "Corrected crate is not reproducible from its source.");
        Require(source.AsSpan(20 + JsonLength(source)).SequenceEqual(corrected.AsSpan(20 + JsonLength(corrected))),
            "Geometry/embedded texture chunks changed.");
        var original = GlbModelLoader.Load(originalPath);
        var model = GlbModelLoader.Load(path);
        var cooked = TextureCooker.LoadForRendering(path);
        Require(original.Parts.Count > 0 && original.Parts.Count == model.Parts.Count && model.Parts.Count == cooked.Parts.Count,
            "Crate parts lost during material correction/cooking.");
        for (int i = 0; i < model.Parts.Count; i++)
        {
            var a = original.Parts[i]; var b = model.Parts[i]; var c = cooked.Parts[i];
            Require(a.MetallicFactor == 1 && a.RoughnessFactor == 0, "Original material diagnosis changed.");
            foreach (var part in new[] { b, c })
            {
                Require(part.MetallicFactor == 0 && part.RoughnessFactor == .85f, "Wood factors lost during import/cooking.");
                Require(a.PartKey == part.PartKey && a.Positions.SequenceEqual(part.Positions) && a.Indices.SequenceEqual(part.Indices) &&
                    Equal(a.Normals, part.Normals) && Equal(a.PbrNormals, part.PbrNormals) && Equal(a.TexCoords, part.TexCoords),
                    "Material fix changed geometry, UVs, normals or fracture keys.");
            }
            Require(Equal(a.BaseColorPng, b.BaseColorPng), "Source wood texture changed.");
        }
        Gpu(root, TextureCooker.LoadForRendering(originalPath), cooked);
        Console.WriteLine($"PASS: {model.Parts.Count} crate parts; deterministic material-only variant, binary/geometry/texture/key parity, cooked factors and flashlight GPU checks.");
    }

    private static bool Equal<T>(T[]? a, T[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }

    private static void Gpu(string root, LoadedModel original, LoadedModel corrected)
    {
        using var host = new EngineHost(640, 480, "Crate flashlight validation");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders");
        world.LightingModel = SurfaceLightingModel.Pbr;
        world.AmbientStrength = world.DirectionalStrength = 0;
        using var oldModel = world.CreateRenderModel(original);
        using var newModel = world.CreateRenderModel(corrected);
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var vertex in corrected.Parts.SelectMany(p => p.Positions))
        { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
        Vector3 center = (min + max) * .5f;
        Vector3 size = max - min;
        var fit = Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(Vector3.One / size);
        string output = Path.Combine(AppContext.BaseDirectory, "qa", "crate-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        int face = 0;
        foreach (Vector3 axis in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var up = MathF.Abs(axis.Y) > .9f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 eye = axis * 2 + Vector3.Cross(up, axis) * .3f;
            var vp = Matrix4x4.CreateLookAt(eye, Vector3.Zero, up) *
                Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, target.Width / (float)target.Height, .05f, 100);
            uint[] Frame(RenderModel model, bool on, bool shadows, Matrix4x4 transform)
                => target.Frame(() =>
                {
                    world.BeginFrame(); world.UpdateCamera(vp, eye);
                    world.UpdatePointLights(on ? [new WorldPointLight(eye, Vector3.One, 7, 22, Vector3.Normalize(-eye), 60, shadows)] : [], eye);
                    world.RenderShadows(renderer, [new(model, transform, Vector3.Zero, 1)], new Viewport(0, 0, target.Width, target.Height, 0, 1));
                    using (world.BatchObjectUploads()) world.DrawModel(renderer.CommandList, model, transform, Vector4.One);
                });
            uint[] off = Frame(newModel, false, true, fit);
            uint[] old = Frame(oldModel, true, true, fit);
            uint[] lit = Frame(newModel, true, true, fit);
            int oldLit = Brighter(old, off), newLit = Brighter(lit, off);
            Console.WriteLine($"Crate face {face}: flashlight brightened {oldLit} original / {newLit} corrected pixels.");
            Require(newLit > 8000 && newLit > oldLit * 1.5, "Wood does not receive broad flashlight illumination.");
            Require(Brighter(Frame(newModel, true, false, fit), off) > 8000, "Unshadowed flashlight missing.");
            Require(Brighter(Frame(newModel, true, true, fit * Matrix4x4.CreateRotationY(.7f)), off) > 5000,
                "Rotated crate lost flashlight illumination.");
            Require(Frame(newModel, false, true, fit).SequenceEqual(off), "Flashlight contribution persists after switch-off.");
            Require(world.ObjectUploadCount == 1, "Crate rendering broke object batching.");
            if (face is 0 or 4)
                foreach (var capture in new[] { ("original", old), ("corrected", lit), ("off", off) })
                    RenderPipelineChecks.SavePng(capture.Item2, target.Width, target.Height, Path.Combine(output, $"face-{face}-{capture.Item1}.png"));
            face++;
        }
        Console.WriteLine("Crate captures: " + output);
        GameFlashlight(root, host, target, output);
        renderer.GraphicsDevice.WaitForIdle();
    }

    private static void GameFlashlight(string root, EngineHost host, PbrBaselineChecks.ImageTarget target, string output)
    {
        using var game = new Game.HL2GameModule(Path.Combine(root, "Game/Content/Levels", PbrTestRoom.FileName));
        game.AssetStreamingEnabled = false;
        game.Initialize(host.Context);
        var entities = (List<Entity>)PbrBaselineChecks.Field(game, "_runtimeEntities");
        var crate = entities.Single(e => e.Name == "MovableComparisonCrate");
        Require(crate.Render.ModelAssetPath == Corrected, "Actual game did not load the corrected crate assignment.");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        var editor = (Engine.Editor.Editor.LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        Vector3 position = editor.LevelFile.Entities.Single(e => e.Name == crate.Name).LocalPosition;
        camera.Position = position + new Vector3(0, 1, -2.4f);
        camera.Yaw = 0; camera.Pitch = -MathF.Atan2(1, 2.4f);
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game, "_world");
        var cache = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            target.Frame(() => game.RenderWorld(host.Context.Renderer), read: false);
            bool ready = cache.Count > 0 && world.ReflectionReady;
            foreach (object entry in cache.Values)
            {
                Require(!(bool)PbrBaselineChecks.Field(entry, "Failed"), "Game asset failed to load.");
                ready &= PbrBaselineChecks.Field(entry, "Model") != null;
            }
            if (ready) break;
            if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Crate game readiness.");
            Thread.Sleep(5);
        }
        uint[] Frame(bool on)
        {
            PbrBaselineChecks.Set(game, "_flashlightOn", on);
            return target.Frame(() => game.RenderWorld(host.Context.Renderer));
        }
        var off = Frame(false); var on = Frame(true);
        // Only compare the crate's central face, not the surrounding walls or weapon.
        uint[] Crop(uint[] pixels) => Enumerable.Range(target.Height / 2 - 30, 60)
            .SelectMany(y => pixels.Skip(y * target.Width + target.Width / 2 - 30).Take(60)).ToArray();
        int brighter = Brighter(Crop(on), Crop(off));
        Require(brighter > 2500, $"Actual flashlight did not illuminate the crate face: {brighter}/3600 pixels.");
        var restored = Frame(false);
        RenderPipelineChecks.SavePng(off, target.Width, target.Height, Path.Combine(output, "game-off.png"));
        RenderPipelineChecks.SavePng(on, target.Width, target.Height, Path.Combine(output, "game-on.png"));
        RenderPipelineChecks.SavePng(restored, target.Width, target.Height, Path.Combine(output, "game-restored.png"));
        int maxDifference = Crop(restored).Zip(Crop(off)).Max(p => Enumerable.Range(0, 3)
            .Max(c => Math.Abs((int)((p.First >> (c * 8)) & 255) - (int)((p.Second >> (c * 8)) & 255))));
        Console.WriteLine($"Game switch-off: maximum crate RGB difference {maxDifference}/255.");
        Require(maxDifference <= 1,
            "Actual flashlight did not return the crate to its off state.");
        Console.WriteLine($"PASS: real game crate, flashlight, shadow and reflection path: {brighter}/3600 face pixels brighten; switch-off restored.");
    }

    private static int Brighter(uint[] on, uint[] off)
    {
        int result = 0;
        for (int i = 0; i < on.Length; i++)
        {
            int sum = 0;
            for (int shift = 0; shift < 24; shift += 8) sum += (int)((on[i] >> shift) & 255) - (int)((off[i] >> shift) & 255);
            if (sum > 30) result++;
        }
        return result;
    }
}

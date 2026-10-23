using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class EmissiveChecks
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); _checks++; }

    internal static void Run(string root)
    {
        string folder = Path.Combine(Path.GetTempPath(), "hs2-emission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "fixture.glb");
            WriteFixture(path);
            var imported = GlbModelLoader.Load(path);
            var part = imported.Parts.Single();
            Check(part.EmissiveFactor == new Vector3(2, 1, .5f), "glTF emissive strength/factor lost.");
            Check(part.BaseColorPng == null && part.EmissivePng?.Length > 0, "Emission without base colour image lost.");
            Check(part.EmissiveTexCoords!.All(uv => Vector2.Distance(uv, new(.25f, .5f)) < .00001f), "Emission UV channel/transform lost.");
            TextureCooker.CookModel(path);
            long hits = CookedModelCache.CacheHits;
            var cached = TextureCooker.LoadForRendering(path);
            Check(CookedModelCache.CacheHits == hits + 1, "Emission model did not use the cooked cache.");
            Check(cached.Parts[0].EmissiveFactor == part.EmissiveFactor &&
                cached.Parts[0].EmissiveTexCoords!.SequenceEqual(part.EmissiveTexCoords), "Cooked emission metadata changed.");
            Check(cached.Parts[0].EmissiveTexture is { Compressed: true, Semantic: TextureSemantic.Color }, "Emission was not BC7/sRGB.");
            string cachePath = CookedModelCache.CachePath(path);
            byte[] valid = File.ReadAllBytes(cachePath);
            var bad = (byte[])valid.Clone(); bad[4] = 1;
            File.WriteAllBytes(cachePath, bad);
            hits = CookedModelCache.CacheHits;
            Check(TextureCooker.LoadForRendering(path).Parts[0].EmissiveFactor == part.EmissiveFactor && CookedModelCache.CacheHits == hits,
                "Old model version did not fall back with emission intact.");
            File.WriteAllBytes(cachePath, valid[..12]);
            Check(TextureCooker.LoadForRendering(path).Parts[0].EmissiveTexture != null, "Corrupt model cache lost emission.");
            TextureCooker.CookModel(path);
            string texturePath = Path.Combine(TextureCooker.CacheDirectory(path), cached.Parts[0].EmissiveTexture!.Key + ".hs2tex");
            File.WriteAllBytes(texturePath, [1, 2, 3]);
            TextureCooker.CookModel(path);
            Check(new FileInfo(texturePath).Length > 3, "Offline cook failed to repair corrupt emissive dependency.");
            TextureCooker.Prepare(imported, null, false);
            CheckStateAndPrefabs(folder);
            CheckGpu(imported, cached);
            Check(GlbModelLoader.Load(Path.Combine(root, "Game/Content/Models/ViewModels/Console 1.glb")).Parts.Any(p => p.EmissivePng != null),
                "Real console fixture has no imported emission.");
            Console.WriteLine($"PASS: {_checks} emission checks: glTF/UV transform, BC7/caches, links/flicker, persistence/prefabs, HDR GPU pixels and instance isolation.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void CheckStateAndPrefabs(string folder)
    {
        var light = new LevelEntityDef { Id = "light", Name = "Test Light", Type = EntityTypes.PointLight, ParentId = "fixture", FlickerAmount = .7f };
        var fixture = new LevelEntityDef { Id = "fixture", Name = "Fixture", Type = EntityTypes.Prop,
            EmissionOverride = true, EmissionColor = new(.2f, .5f, 1), EmissionIntensity = 3, EmissionPart = "bulb", EmissionLightId = light.Id };
        var level = new LevelFile { UsePointLights = true, Entities = [fixture, light] };
        var state = new LevelLightState();
        Vector3 Glow(LevelLightState? live = null) => LevelLighting.GetEmission(level, fixture, live, 1).Resolve(Vector3.Zero, "bulb", "", "");
        Vector3 on = Glow();
        Check(on != Vector3.Zero && Vector3.Distance(on, new Vector3(.2f, .5f, 1) * 3 * LevelLightState.Flicker(light, 1)) < .0001f, "Fixture flicker differs from actual light.");
        Check(LevelLighting.GetEmission(level, fixture).Resolve(Vector3.Zero, "housing", "", "") == Vector3.Zero, "Bulb override affected housing.");
        var control = new LevelInteractionDef { Kind = "LightSwitch", Targets = [light.Id] };
        Check(state.Toggle(level, control, out _) && Glow(state) == Vector3.Zero && Glow() == on, "Runtime switch modified editor defaults or left emission on.");
        state.Restore(StructuredData.ReadJson<List<LightStateOverride>>(StructuredData.WriteJson(state.Capture())));
        Check(Glow(state) == Vector3.Zero, "Save/restore lost linked fixture state.");
        state.Toggle(level, control, out _);
        Check(Glow(state) == on, "Repeated switch did not restore emission.");
        light.Name = "Renamed";
        Check(Glow(state) == on, "Renaming broke a fixture's stable-ID link.");
        light.Intensity = 0; Check(Glow() == Vector3.Zero, "Zero intensity light still glows."); light.Intensity = 3;
        light.LightEnabled = false; Check(Glow() == Vector3.Zero, "Editor ignored disabled light."); light.LightEnabled = true;
        level.UsePointLights = false; Check(Glow() == Vector3.Zero, "Scene light toggle ignored."); level.UsePointLights = true;
        level.Entities.Remove(light); Check(Glow() == Vector3.Zero, "Missing link failed bright."); level.Entities.Add(light);
        string path = Path.Combine(folder, "level.json");
        LevelIO.Save(path, level);
        var loaded = LevelIO.Load(path);
        Check(loaded.Entities[0].EmissionPart == "bulb" && LevelLighting.GetEmission(loaded, loaded.Entities[0], seconds: 1).Equals(LevelLighting.GetEmission(level, fixture, seconds: 1)),
            "Level persistence lost emission settings.");
        Check(StructuredData.ReadJson<LevelEntityDef>(Encoding.UTF8.GetBytes("{}")) is { EmissionIntensity: 1, EmissionOverride: false, EmissionLightId: "" }, "Legacy level defaults changed.");
        var prefab = new PrefabFile { Name = "Fixture", RootEntityId = fixture.Id, Entities = [fixture, light] };
        string prefabPath = Path.Combine(folder, "fixture.prefab.json");
        PrefabIO.Save(prefabPath, prefab); prefab = PrefabIO.Load(prefabPath);
        var editor = new LevelEditorController(); editor.LoadFromMemory(path, new());
        Check(editor.AddPrefabInstance(prefab, prefabPath), "Could not place fixture prefab.");
        Check(editor.LevelFile.Entities[0].EmissionLightId == editor.LevelFile.Entities[1].Id, "Prefab emission link not remapped.");
        Check(editor.TryBuildPrefabFromSelectedInstanceForApply("Fixture", out var applied, out _), "Could not apply fixture prefab.");
        Check(applied.Entities[0].EmissionLightId == "light", "Apply retained scene light ID.");
        Check(editor.AddPrefabInstance(applied, prefabPath), "Could not place second fixture prefab.");
        Check(editor.LevelFile.Entities[2].EmissionLightId == editor.LevelFile.Entities[3].Id &&
            editor.LevelFile.Entities[0].EmissionLightId != editor.LevelFile.Entities[2].EmissionLightId, "Two prefab fixtures share the wrong light.");
        Check(editor.RevertSelectedPrefabInstance(applied, prefabPath), "Could not revert fixture prefab.");
        Check(editor.LevelFile.Entities[2].EmissionLightId == editor.LevelFile.Entities[3].Id, "Revert broke emission link.");
        Check(editor.Undo() && editor.Redo() && editor.LevelFile.Entities[2].EmissionLightId == editor.LevelFile.Entities[3].Id, "Undo/redo lost fixture link.");
        fixture.EmissionIntensity = float.NaN;
        Check(Glow() == Vector3.Zero, "Invalid authored intensity reached renderer.");
    }

    private static void CheckGpu(LoadedModel source, LoadedModel cooked)
    {
        using var host = new EngineHost(640, 480, "Emission GPU QA");
        var renderer = host.Context.Renderer; var device = renderer.GraphicsDevice; var factory = device.ResourceFactory;
        using var world = new BasicWorldRenderer(device, renderer.WorldOutputDescription, "Shaders");
        world.AmbientStrength = 0; world.DirectionalStrength = 0;
        using var rawModel = world.CreateRenderModel(source);
        using var cachedModel = world.CreateRenderModel(cooked);
        var original = source.Parts[0];
        var noUv = new LoadedModel([new(original.Positions, original.Normals, null, original.Indices, Vector4.Zero, null, null, 0, 1) { EmissiveFactor = new(2, 1, .5f) }]);
        using var untextured = world.CreateRenderModel(noUv);
        int width = (int)renderer.WorldFramebuffer.Width, height = (int)renderer.WorldFramebuffer.Height;
        var format = device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
        using var color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, format, TextureUsage.RenderTarget));
        using var depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R32_Float, TextureUsage.DepthStencil));
        using var staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, format, TextureUsage.Staging));
        using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color));
        var eye = new Vector3(0, 0, 3);
        var vp = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, width / (float)height, .05f, 100);
        uint[] Frame(RenderModel model, ModelEmission emission, bool split = false)
        {
            renderer.BeginFrame(); world.BeginFrame(); world.UpdateCamera(vp, eye); world.UpdatePointLights([], eye);
            world.RenderShadows(renderer, [], new Viewport(0, 0, width, height, 0, 1));
            using (world.BatchObjectUploads())
            {
                var transform = split ? Matrix4x4.CreateScale(.45f) * Matrix4x4.CreateTranslation(-.7f, 0, 0) : Matrix4x4.Identity;
                world.DrawModel(renderer.CommandList, model, transform, Vector4.One, null, emission);
                if (split) world.DrawModel(renderer.CommandList, model, Matrix4x4.CreateScale(.45f) * Matrix4x4.CreateTranslation(.7f, 0, 0), Vector4.One, null, new ModelEmission(0));
            }
            renderer.ResolveWorldToSwapchain(framebuffer); renderer.CommandList.CopyTexture(color, staging);
            renderer.EndFrame(); device.WaitForIdle();
            var mapped = device.Map<uint>(staging, MapMode.Read);
            try
            {
                var pixels = new uint[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = mapped[x, y];
                return pixels;
            }
            finally { device.Unmap(staging); }
        }
        uint[] off = Frame(rawModel, new(0)), on = Frame(rawModel, new(1)), cached = Frame(cachedModel, new(1));
        int brightness(uint pixel) => (int)((pixel & 255) + ((pixel >> 8) & 255) + ((pixel >> 16) & 255));
        Check(on.Zip(off).Count(p => brightness(p.First) > brightness(p.Second) + 50) > 20000, "Emission was not visible without lights.");
        float grey = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
        int Expected(float strength)
        {
            float radiance = grey * strength, mapped = radiance / (1 + radiance);
            return (int)MathF.Round((1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f) * 255);
        }
        uint centre = on[(height / 2) * width + width / 2];
        bool bgra = format is Veldrid.PixelFormat.B8_G8_R8_A8_UNorm or Veldrid.PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        Check(Math.Abs((int)((centre >> (bgra ? 16 : 0)) & 255) - Expected(2)) <= 2 &&
            Math.Abs((int)((centre >> 8) & 255) - Expected(1)) <= 2 &&
            Math.Abs((int)((centre >> (bgra ? 0 : 16)) & 255) - Expected(.5f)) <= 2,
            "Emission did not decode sRGB/add linear radiance/tone map exactly once.");
        Check(on.Zip(cached).All(p => Math.Abs(brightness(p.First) - brightness(p.Second)) <= 6), "Cooked emission GPU output differs from source.");
        Check(Frame(rawModel, new(0)).SequenceEqual(off), "Turning emission off did not restore the original material.");
        uint[] targeted = Frame(rawModel, new(1, Vector3.Zero, "bulb"));
        Check(targeted.SequenceEqual(off), "Part override failed.");
        Check(Frame(rawModel, new(1, Vector3.Zero, "housing")).SequenceEqual(on), "Part override leaked onto another part.");
        renderer.Exposure = 2;
        Check(Frame(rawModel, new(1)).Zip(on).Count(p => brightness(p.First) > brightness(p.Second) + 5) > 20000, "Emission bypassed HDR exposure.");
        renderer.Exposure = 1;
        var split = Frame(rawModel, new(1), true);
        Check(Enumerable.Range(0, split.Length).Count(i => i % width < width / 2 && brightness(split[i]) > 100) > 1000 &&
            Enumerable.Range(0, split.Length).Count(i => i % width >= width / 2 && brightness(split[i]) > 100) == 0, "Emission state leaked across shared model instances.");
        Check(Frame(untextured, new(1)).Zip(Frame(untextured, new(0))).Count(p => brightness(p.First) > brightness(p.Second) + 50) > 20000, "Factor-only/no-UV emission failed.");
        string output = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(output);
        RenderPipelineChecks.SavePng(on, width, height, Path.Combine(output, "emission-on.png"));
        RenderPipelineChecks.SavePng(off, width, height, Path.Combine(output, "emission-off.png"));
        RenderPipelineChecks.SavePng(split, width, height, Path.Combine(output, "emission-instances.png"));
        device.WaitForIdle();
    }

    private static void WriteFixture(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("GPU/PNG checks require Windows.");
        using var binary = new MemoryStream();
        using var writer = new BinaryWriter(binary, Encoding.UTF8, true);
        var views = new List<object>(); var accessors = new List<object>();
        int Floats(float[] values, string type, int components)
        {
            int start = (int)binary.Position;
            foreach (float value in values) writer.Write(value);
            int index = views.Count; views.Add(new { buffer = 0, byteOffset = start, byteLength = values.Length * 4 });
            accessors.Add(new { bufferView = index, componentType = 5126, count = values.Length / components, type });
            return accessors.Count - 1;
        }
        int pos = Floats([-1,-1,0, 1,-1,0, 1,1,0, -1,1,0], "VEC3", 3);
        int norm = Floats([0,0,1, 0,0,1, 0,0,1, 0,0,1], "VEC3", 3);
        int uv0 = Floats([0,0, 1,0, 1,1, 0,1], "VEC2", 2);
        int uv1 = Floats([.5f,.5f, .5f,.5f, .5f,.5f, .5f,.5f], "VEC2", 2);
        int indexOffset = (int)binary.Position;
        foreach (ushort index in new ushort[] { 0,1,2,0,2,3 }) writer.Write(index);
        views.Add(new { buffer = 0, byteOffset = indexOffset, byteLength = 12 });
        int indices = accessors.Count;
        accessors.Add(new { bufferView = views.Count - 1, componentType = 5123, count = 6, type = "SCALAR" });
        using var image = new Bitmap(8, 8);
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) image.SetPixel(x, y, Color.FromArgb(128, 128, 128));
        using var imageBytes = new MemoryStream(); image.Save(imageBytes, ImageFormat.Png);
        views.Add(new { buffer = 0, byteOffset = (int)binary.Position, byteLength = (int)imageBytes.Length });
        writer.Write(imageBytes.ToArray());
        while (binary.Length % 4 != 0) writer.Write((byte)0);
        object gltf = new
        {
            asset = new { version = "2.0" }, buffers = new[] { new { byteLength = (int)binary.Length } }, bufferViews = views, accessors,
            images = new[] { new { bufferView = views.Count - 1, mimeType = "image/png" } }, textures = new[] { new { source = 0 } },
            materials = new[] { new { emissiveFactor = new[] { 1f, .5f, .25f },
                extensions = new { KHR_materials_emissive_strength = new { emissiveStrength = 2 } },
                emissiveTexture = new { index = 0, texCoord = 1, extensions = new { KHR_texture_transform = new { offset = new[] { 0f, .25f }, scale = new[] { .5f, .5f } } } } } },
            meshes = new[] { new { name = "bulb", primitives = new[] { new { attributes = new { POSITION = pos, NORMAL = norm, TEXCOORD_0 = uv0, TEXCOORD_1 = uv1 }, indices, material = 0 } } } },
            nodes = new[] { new { name = "bulb", mesh = 0 } }, scenes = new[] { new { nodes = new[] { 0 } } }, scene = 0
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(gltf);
        int padding = (4 - json.Length % 4) % 4;
        using var file = File.Create(path); using var output = new BinaryWriter(file);
        output.Write(0x46546C67u); output.Write(2u); output.Write(28 + json.Length + padding + (int)binary.Length);
        output.Write(json.Length + padding); output.Write(0x4E4F534Au); output.Write(json);
        for (int i = 0; i < padding; i++) output.Write((byte)' ');
        output.Write((int)binary.Length); output.Write(0x004E4942u); output.Write(binary.ToArray());
    }
}

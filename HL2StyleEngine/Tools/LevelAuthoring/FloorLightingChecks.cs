using System.Numerics;
using System.Text.Json;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class FloorLightingChecks
{
    internal const string FloorPath = FloorMaterial.Original;

    internal static void Run(string root, bool validate = false)
    {
        if (validate) FloorMaterial.Validate(root);
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "floor-lighting-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        var source = TextureCooker.LoadForRendering(Path.Combine(root, "Game", FloorPath));
        var flat = new LoadedModel(source.Parts.Select(p => Copy(p, false)).ToArray());
        using var host = new EngineHost(960, 720, "Floor lighting isolation");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders");
        world.LightingModel = SurfaceLightingModel.Pbr; world.PointShadowLimit = 16;
        world.AmbientStrength = world.DirectionalStrength = 0;
        using var normalModel = world.CreateRenderModel(source);
        using var flatModel = world.CreateRenderModel(flat);
        var raw = GlbModelLoader.Load(Path.Combine(root, "Game", FloorPath));
        float worstAlignment = 1; int floorTriangles = 0;
        foreach (var part in raw.Parts)
            for (int i = 0; i < part.Indices.Length; i += 3)
            {
                int a = (int)part.Indices[i], b = (int)part.Indices[i + 1], c = (int)part.Indices[i + 2];
                var geometric = Vector3.Normalize(Vector3.Cross(part.Positions[b] - part.Positions[a], part.Positions[c] - part.Positions[a]));
                if (!float.IsFinite(geometric.Y) || MathF.Abs(geometric.Y) < .8f) continue;
                var normals = part.PbrNormals ?? part.Normals;
                if (normals != null) worstAlignment = MathF.Min(worstAlignment, MathF.Abs(Vector3.Dot(geometric, Vector3.Normalize(normals[a] + normals[b] + normals[c]))));
                floorTriangles++;
            }
        Console.WriteLine($"[FloorLighting] {floorTriangles} floor triangles; minimum shading/face-normal alignment {worstAlignment}.");
        var candidates = new List<(float Strength, RenderModel Model)>();
        foreach (float strength in validate ? new[] { FloorMaterial.Strength } : new[] { .5f, .35f, .25f })
        {
            if (validate)
            {
                candidates.Add((strength, world.CreateRenderModel(TextureCooker.LoadForRendering(Path.Combine(root, "Game", FloorMaterial.Corrected)))));
                continue;
            }
            var parts = source.Parts.Select(p => Copy(p, true)).ToArray();
            for (int i = 0; i < parts.Length; i++)
                parts[i].NormalTexture = TextureCooker.PrepareImage(FloorMaterial.ReduceNormalStrength(raw.Parts[i].NormalPng!, strength),
                    TextureSemantic.Normal, TextureCooker.CacheDirectory(Path.Combine(root, "Game", FloorPath)), cook: true);
            candidates.Add((strength, world.CreateRenderModel(new LoadedModel(parts))));
        }
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var p in source.Parts.SelectMany(p => p.Positions)) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        Console.WriteLine($"[FloorLighting] Source bounds {min} -> {max}");
        var center = (min + max) * .5f;
        var fit = Matrix4x4.CreateTranslation(-center.X, -max.Y, -center.Z) * Matrix4x4.CreateScale(4 / (max.X - min.X), 1, 4 / (max.Z - min.Z));
        var transforms = new List<Matrix4x4>();
        for (int x = -3; x <= 3; x++) for (int z = 0; z < 6; z++)
            transforms.Add(fit * Matrix4x4.CreateTranslation(x * 4, 0, -2 - z * 4));
        var results = new List<object>();
        foreach (float aim in new[] { 4f, 8f, 12f })
        {
            var eye = new Vector3(.3f, 1.7f, 1);
            var forward = Vector3.Normalize(new Vector3(0, 0, -aim) - eye);
            var vp = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitY) *
                Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, target.Width / (float)target.Height, .05f, 100);
            uint[] Frame(RenderModel model, bool shadows, bool light = true) => target.Frame(() =>
            {
                world.BeginFrame(); world.UpdateCamera(vp, eye);
                world.UpdatePointLights(light ? [new(eye + new Vector3(.04f, -.02f, 0), new(1, .95f, .85f), 7, 22, forward, 55, shadows, 100, "torch")] : [], eye);
                world.RenderShadows(renderer, transforms.Select(m => new ShadowCaster(model, m,
                    Vector3.Transform(center, m), 3)).ToArray(), new Viewport(0, 0, target.Width, target.Height, 0, 1));
                using (world.BatchObjectUploads()) foreach (var transform in transforms)
                    world.DrawModel(renderer.CommandList, model, transform, Vector4.One);
            });
            var normalLit = Frame(normalModel, false);
            var normalShadow = Frame(normalModel, true);
            var flatLit = Frame(flatModel, false);
            var flatShadow = Frame(flatModel, true);
            int samples = 0, normalBlack = 0, shadowBlack = 0, flatShadowBlack = 0;
            for (int i = 0; i < flatLit.Length; i++)
            {
                if (Energy(flatLit[i]) < 60) continue;
                samples++;
                if (Energy(normalLit[i]) <= 3) normalBlack++;
                if (Energy(normalLit[i]) >= 60 && Energy(normalShadow[i]) <= 3) shadowBlack++;
                if (Energy(flatShadow[i]) <= 3) flatShadowBlack++;
            }
            results.Add(new { Aim = aim, Samples = samples, NormalBlack = normalBlack, ShadowBlack = shadowBlack, FlatShadowBlack = flatShadowBlack });
            Console.WriteLine($"[FloorLighting] Aim {aim}: {samples} flat-lit pixels; normal-map black {normalBlack}; additional shadow black {shadowBlack}; flat self-shadow black {flatShadowBlack}.");
            foreach (var pair in new[] { ("normal-no-shadow", normalLit), ("normal-shadow", normalShadow), ("flat-no-shadow", flatLit), ("flat-shadow", flatShadow) })
                RenderPipelineChecks.SavePng(pair.Item2, target.Width, target.Height, Path.Combine(folder, $"aim-{aim}-{pair.Item1}.png"));
            foreach (var candidate in candidates)
            {
                var pixels = Frame(candidate.Model, true);
                var lit = Frame(candidate.Model, false);
                int black = pixels.Where((p, i) => Energy(flatLit[i]) >= 60 && Energy(p) <= 3).Count();
                int litBlack = lit.Where((p, i) => Energy(flatLit[i]) >= 60 && Energy(p) <= 3).Count();
                int detail = lit.Where((p, i) => Energy(flatLit[i]) >= 60 && Math.Abs(Energy(p) - Energy(flatLit[i])) > 9).Count();
                results.Add(new { Aim = aim, candidate.Strength, Samples = samples, Black = black, LitBlack = litBlack, Detail = detail });
                Console.WriteLine($"[FloorLighting] Aim {aim}, strength {candidate.Strength}: black shadow-on/off {black}/{litBlack}; {detail} detail pixels.");
                RenderPipelineChecks.SavePng(pixels, target.Width, target.Height, Path.Combine(folder, $"aim-{aim}-strength-{candidate.Strength}.png"));
                if (validate)
                {
                    Require(samples > 100000 && normalBlack > 300, "Reference no longer exercises the grazing-angle artifact.");
                    Require(litBlack < normalBlack * .1f, "Floor normal correction did not remove at least 90% of black normal-map pixels.");
                    Require(flatShadowBlack == 0, "Smoothed floor normals caused false receiver-plane self-shadowing.");
                    Require(black <= flatShadowBlack + normalBlack * .1f, "Shadow-on floor retained excessive black speckles.");
                    Require(detail > samples * .05f, "Floor lost its normal-mapped surface detail.");
                    var dark = Frame(candidate.Model, true, false);
                    Require(dark.All(p => Energy(p) <= 3), "Floor fix added ambient/emissive light with the torch off.");
                }
            }
        }
        foreach (var candidate in candidates) candidate.Model.Dispose();
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[FloorLighting] " + folder);
        if (validate) Console.WriteLine("PASS: three grazing views, BC5 material, 90% normal-artifact reduction, detail retained, shadows on/off and no added ambient light.");
    }

    private static LoadedModelPart Copy(LoadedModelPart p, bool normal) => new(p.Positions, p.Normals, p.TexCoords, p.Indices,
        p.Color, p.BaseColorPng, p.MetallicRoughnessPng, p.MetallicFactor, p.RoughnessFactor)
    {
        PbrNormals = p.PbrNormals, BaseColorTexture = p.BaseColorTexture, MaterialTexture = p.MaterialTexture,
        NormalPng = normal ? p.NormalPng : null, NormalTexture = normal ? p.NormalTexture : null,
        EmissivePng = p.EmissivePng, EmissiveTexture = p.EmissiveTexture, EmissiveFactor = p.EmissiveFactor,
        EmissiveTexCoords = p.EmissiveTexCoords
    };

    private static int Energy(uint p) => (int)((p & 255) + ((p >> 8) & 255) + ((p >> 16) & 255));
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}

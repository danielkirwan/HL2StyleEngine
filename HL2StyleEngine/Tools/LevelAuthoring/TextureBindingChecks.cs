using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class TextureBindingChecks
{
    internal static void Run(string root)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks++; }
        using (var plan = new TextureMipResidency { DemotionDelay = TimeSpan.Zero })
        {
            plan.Register(new("shared", TextureSemantic.Color, false, 8, 8, [336, 80, 16], _ => throw new Exception()));
            plan.Plan([new("shared", 2, 1, false), new("shared", 0, 10, true)], 1);
            Check(plan.RequestedMip("shared") == 0 && plan.OverBudget && plan.EstimatedBytes == 336, "Shared pin/accounting failed.");
            plan.Plan([new("shared", 2, 1, false), new("shared", 1, 5, false)], 1000);
            Check(plan.RequestedMip("shared") == 1 && plan.EstimatedBytes == 80, "Most detailed shared demand lost.");
            plan.Plan([new("shared", 1, 5, false, 1)], 1);
            Check(plan.RequestedMip("shared") == 1 && plan.OverBudget, "Visible quality ceiling was degraded by budget.");
            plan.Plan([], 1);
            Check(plan.RequestedMip("shared") == 0, "Unclassified texture was demoted.");
            Check(TextureMipResidency.ProjectedMip(1024, 1, 2, 1, 1, 1080) == 0, "Near object demoted.");
            Check(TextureMipResidency.ProjectedMip(1024, 1, 100, 1, 1, 1080) == 3, "Distant object never demoted.");
            Check(TextureMipResidency.ProjectedMip(1024, 1, 100, .001f, 1, 1080) == 0, "Small UV island lost detail.");
            Check(TextureMipResidency.ProjectedMip(1024, 1, 100, float.NaN, 1, 1080) == 0, "Invalid UV demand unsafe.");
        }
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string path = AssetPackage.Resolve(package.Root, package.Manifest.Models.First(m => m.Path.EndsWith("WoodenCratePbr.glb", StringComparison.OrdinalIgnoreCase)).Path);
        var source = package.Load(path);
        var textures = source.Parts.SelectMany(p => new[] { p.BaseColorTexture, p.MaterialTexture, p.NormalTexture, p.EmissiveTexture })
            .OfType<PreparedTexture>().DistinctBy(t => t.Key).ToArray();
        Check(textures.Length > 0, "No texture fixture.");
        foreach (var t in textures)
        {
            var tail = package.LoadTextureMipTail(path, t.Key, t.Semantic, 3);
            Check(tail.Mips.Zip(t.MipTail(3).Mips).All(p => p.First.SequenceEqual(p.Second)), "Texture-only package bytes differ.");
        }
        using var host = new EngineHost(640, 480, "Texture binding regression");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders")
        { AmbientStrength = .5f, DirectionalStrength = 1 };
        using var a = world.CreateRenderModel(source);
        using var b = world.CreateRenderModel(source);
        var geometry = Buffers(a).Concat(Buffers(b)).ToArray();
        long geometryBytes = a.GeometryBytes + b.GeometryBytes, full = world.TextureResidentBytes;
        var positions = source.Parts.SelectMany(p => p.Positions).ToArray();
        Vector3 min = positions.Aggregate(Vector3.Min), max = positions.Aggregate(Vector3.Max);
        var transform = Matrix4x4.CreateTranslation(-(min + max) / 2) * Matrix4x4.CreateScale(2 / (max - min).Length());
        var eye = new Vector3(0, .5f, -2);
        var vp = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1, target.Width / (float)target.Height, .1f, 100);
        uint[] Draw(RenderModel model, SurfaceLightingModel lighting)
        {
            world.LightingModel = lighting;
            return target.Frame(() => { world.BeginFrame(); world.UpdateCamera(vp, eye); world.UpdatePointLights([], eye); world.DrawModel(renderer.CommandList, model, transform, Vector4.One); });
        }
        var legacy = Draw(a, SurfaceLightingModel.Legacy);
        var pbr = Draw(a, SurfaceLightingModel.Pbr);
        void Step(TextureMipResidency stream) => target.Frame(() => { world.BeginFrame(); stream.Pump(world, renderer); }, false);
        void Plan(TextureMipResidency stream, int mip, PreparedTexture[]? selected = null) => stream.Plan((selected ?? textures).Select(t => new TextureMipDemand(t.Key, mip, 1, false)), long.MaxValue);
        void Ready(TextureMipResidency stream, int mip, PreparedTexture[]? selected = null)
        {
            var items = selected ?? textures;
            Plan(stream, mip, items); var timer = Stopwatch.StartNew();
            do
            {
                Step(stream); Thread.Sleep(1);
                if (stream.Errors.Any() || timer.Elapsed.TotalSeconds > 20) throw new InvalidDataException("Residency readiness: " + string.Join(",", stream.Errors));
            } while (stream.Busy || items.Any(t => world.GetTextureResidency().Any(i => i.Key == t.Key && i.FirstMip != Math.Min(mip, t.Mips.Length - 1))));
            renderer.DrainRetiredResources();
        }
        bool fail = false;
        using var stream = new TextureMipResidency { DemotionDelay = TimeSpan.Zero };
        foreach (var t in textures) stream.Register(TextureMipSource.From(t, m => fail ? throw new IOException("Injected read failure") : package.LoadTextureMipTail(path, t.Key, t.Semantic, m)));
        Ready(stream, 3); long reduced = world.TextureResidentBytes;
        Check(reduced < full / 4 && stream.ChangedParts >= 2, "Shared texture-only replacement did not reduce memory.");
        Check(Draw(a, SurfaceLightingModel.Pbr).SequenceEqual(Draw(b, SurfaceLightingModel.Pbr)), "Shared models diverged.");
        using (var later = world.CreateRenderModel(source))
        {
            Ready(stream, 3);
            Check(Draw(a, SurfaceLightingModel.Pbr).SequenceEqual(Draw(later, SurfaceLightingModel.Pbr)), "Late shared owner missed residency.");
        }
        using (var priority = new TextureMipResidency { DemotionDelay = TimeSpan.Zero })
        using (var started = new ManualResetEventSlim())
        {
            var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var p = source.Parts.First(p => p.BaseColorTexture != null);
            var duplicate = p.BaseColorTexture! with { Key = "qa-priority-" + p.BaseColorTexture.Key };
            using var extra = world.CreateRenderModel(new LoadedModel([new LoadedModelPart(p.Positions, p.Normals, p.TexCoords,
                p.Indices, p.Color, null, null, p.MetallicFactor, p.RoughnessFactor)
                { PbrNormals = p.PbrNormals, BaseColorTexture = duplicate }]));
            PreparedTexture[] priorities = [p.BaseColorTexture, duplicate];
            foreach (var t in priorities)
                priority.Register(TextureMipSource.From(t, mip =>
                {
                    requests.Enqueue(t.Key); started.Set();
                    return t.MipTail(mip);
                }));
            Ready(priority, 3, priorities); requests.Clear(); started.Reset();
            priority.Plan(priorities.Select((t, i) => new TextureMipDemand(t.Key, 0, i == priorities.Length - 1 ? 100 : 1, false)), long.MaxValue);
            Step(priority);
            Check(started.Wait(10000) && requests.TryPeek(out string? first) && first == duplicate.Key,
                "Visible promotion was delayed behind a lower-priority texture.");
            Ready(priority, 0, priorities);
        }
        Ready(stream, 3);
        for (int i = 0; i < 8; i++) { Ready(stream, 0); Ready(stream, 3); }
        Check(world.TextureResidentBytes == reduced, "Repeated texture replacement leaked payload.");
        Check(geometry.SequenceEqual(Buffers(a).Concat(Buffers(b))) && geometryBytes == a.GeometryBytes + b.GeometryBytes, "Geometry buffers changed.");
        Ready(stream, 0);
        Check(legacy.SequenceEqual(Draw(a, SurfaceLightingModel.Legacy)) && pbr.SequenceEqual(Draw(a, SurfaceLightingModel.Pbr)), "Restored materials differ from full reference.");
        Check(pbr.Distinct().Take(100).Count() == 100, "Blank image comparison.");
        fail = true; Plan(stream, 2); var timer = Stopwatch.StartNew();
        while (!stream.Errors.Any() && timer.Elapsed.TotalSeconds < 10) { Step(stream); Thread.Sleep(1); }
        Check(stream.Errors.Any() && pbr.SequenceEqual(Draw(a, SurfaceLightingModel.Pbr)), "Failed read lost visible texture.");
        fail = false; stream.Retry(); Ready(stream, 2);
        Ready(stream, 0);
        bool rejected = false;
        try { world.BeginFrame(); world.TryReplaceTexture(textures[0] with { Mips = [new byte[1]] }, renderer, out _); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && pbr.SequenceEqual(Draw(a, SurfaceLightingModel.Pbr)), "Invalid payload reached GPU/changed bindings.");
        foreach (bool throwOld in new[] { false, true })
        {
            using var release = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();
            using var superseded = new TextureMipResidency { DemotionDelay = TimeSpan.Zero };
            var texture = textures[0];
            superseded.Register(TextureMipSource.From(texture, m =>
            {
                if (m == 3) { started.Set(); release.Wait(); if (throwOld) throw new IOException("Superseded failure"); }
                return package.LoadTextureMipTail(path, texture.Key, texture.Semantic, m);
            }));
            superseded.Plan([new(texture.Key, 3, 1, false)], long.MaxValue); Step(superseded);
            Check(started.Wait(10000), "Worker did not start.");
            superseded.Plan([new(texture.Key, 0, 1, true)], long.MaxValue); release.Set(); timer.Restart();
            do { Step(superseded); Thread.Sleep(1); } while (superseded.Busy && timer.Elapsed.TotalSeconds < 10);
            Check(!superseded.Busy && !superseded.Errors.Any() && pbr.SequenceEqual(Draw(a, SurfaceLightingModel.Pbr)), "Superseded result overrode current full texture.");
        }
        renderer.RetireAfterFrame(a); renderer.RetireAfterFrame(b); renderer.DrainRetiredResources();
        Check(world.TextureResidentBytes == 0 && world.GetTextureResidency().Length == 0, "Owner/binding retirement leaked.");
        string folder = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(folder);
        string report = Path.Combine(folder, "texture-bindings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { checks, full, reduced, geometryBytes, stream.Replacements, stream.ChangedParts, package.Fallbacks }));
        Console.WriteLine($"PASS {checks} texture binding checks; {full} -> {reduced} bytes, geometry stable, legacy/PBR pixels exact. {report}");
    }

    private static object[] Buffers(RenderModel model)
        => ((IEnumerable)typeof(RenderModel).GetProperty("Parts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!).Cast<object>()
            .SelectMany(p => new[] { "VertexBuffer", "IndexBuffer", "TexturedVertexBuffer", "PbrVertexBuffer", "PbrIndexBuffer" }.Select(n => p.GetType().GetProperty(n)!.GetValue(p))).OfType<object>().ToArray();
}

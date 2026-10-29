using System.Collections;
using System.Diagnostics;
using System.Numerics;
using Engine.Core.Assets;
using Engine.Editor.Editor;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class LightingDependencyImageChecks
{
    // One host per invocation: SDL/native UI lifetime is process-wide.
    internal static void Run(string root, bool baked)
    {
        string file = baked ? "lightProbeTest.json" : AtmosphereChecks.FileName;
        var package = new AssetPackage(LoadingPackageChecks.Build(root, file));
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "lighting-images-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        using var host = new EngineHost(1280, 720, "Lighting dependency image checks");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(package.LevelPath, package.Root, package);
        game.AssetStreamingEnabled = false;
        game.Initialize(host.Context);
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game, "_world");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        var rooms = (ReflectionProbeSession)PbrBaselineChecks.Field(game, "_roomReflections");
        var bake = (LevelBakeSession)PbrBaselineChecks.Field(game, "_bakedLighting");
        var cache = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        var identities = (AssetFingerprintReader)PbrBaselineChecks.Field(game, "_packageFingerprints");
        editor.ShowColliders = editor.ShowPhysicsAabbs = editor.GizmoEnabled = false;
        Vector3 start = camera.Position;
        float yaw = camera.Yaw, pitch = camera.Pitch;
        var references = new Dictionary<string, uint[]>();
        int checks = 0;
        foreach (bool optimized in new[] { false, true })
        {
            game.PackageLightingEnabled = optimized;
            PbrBaselineChecks.Set(game, "_editorEnabled", false);
            rooms.Reload();
            PbrBaselineChecks.Set(bake, "_revision", -1L);
            Ready();
            foreach (int room in baked ? new[] { 0 } : new[] { 0, 2, 4, 6, 8, 10 })
            {
                camera.Position = baked ? start : RenderStressTest.Center(room) + new Vector3(0, 1.65f, -5);
                camera.Yaw = baked ? yaw : 0;
                camera.Pitch = baked ? pitch : -.1f;
                foreach (bool torch in new[] { false, true })
                {
                    PbrBaselineChecks.Set(game, "_flashlightOn", torch);
                    Ready();
                    Capture($"room-{room}-torch-{torch}", optimized);
                }
            }
            long beforeEditor = identities.IdentityHits;
            PbrBaselineChecks.Set(game, "_editorEnabled", true);
            rooms.Reload();
            PbrBaselineChecks.Set(bake, "_revision", -1L);
            Ready();
            Capture("f2", optimized);
            if (identities.IdentityHits != beforeEditor) throw new InvalidDataException("F2 used immutable lighting identities.");
        }
        if (identities.IdentityHits == 0) throw new InvalidDataException("Optimized path was not exercised.");
        if (baked && (bake.Probes == null || bake.ReadyModels == 0)) throw new InvalidDataException("No baked lighting bindings/probes.");
        Console.WriteLine($"PASS: {checks} strict/package lighting image pairs for {file}; F2 strict validation; {identities.IdentityHits} model identity hits. {folder}");

        void Ready()
        {
            var timer = Stopwatch.StartNew();
            world.ViewCullingEnabled = false;
            while (true)
            {
                target.Frame(() => game.RenderWorld(renderer), read: false);
                bool ready = cache.Count > 0;
                foreach (object entry in cache.Values)
                {
                    if ((bool)PbrBaselineChecks.Field(entry, "Failed")) throw new InvalidDataException("Model load failed.");
                    ready &= PbrBaselineChecks.Field(entry, "Model") != null;
                }
                if (!baked) ready &= rooms.Ready && !rooms.Busy;
                if (editor.LevelFile.Bake.Enabled) ready &= bake.Ready && !bake.Busy && bake.BindingsReady && bake.ReadyModels > 0;
                if (ready) break;
                if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException($"{bake.Status}; {rooms.Status}");
                Thread.Sleep(5);
            }
            world.ViewCullingEnabled = true;
            target.Frame(() => game.RenderWorld(renderer), read: false);
        }

        void Capture(string name, bool optimized)
        {
            uint[] pixels = target.Frame(() => game.RenderWorld(renderer));
            if (pixels.Distinct().Take(129).Count() < 129) throw new InvalidDataException("Blank/flat view: " + name);
            RenderPipelineChecks.SavePng(pixels, target.Width, target.Height, Path.Combine(folder, name + (optimized ? "-package.png" : "-strict.png")));
            if (!optimized) { references[name] = pixels; return; }
            uint[] reference = references[name];
            int maximum = 0;
            for (int i = 0; i < pixels.Length; i++)
                for (int shift = 0; shift < 24; shift += 8)
                    maximum = Math.Max(maximum, Math.Abs((int)(pixels[i] >> shift & 255) - (int)(reference[i] >> shift & 255)));
            if (maximum > 1) throw new InvalidDataException($"Lighting pixels changed: {name}, {maximum}/255.");
            Console.WriteLine($"PASS: {name}, maximum {maximum}/255.");
            checks++;
        }
    }
}

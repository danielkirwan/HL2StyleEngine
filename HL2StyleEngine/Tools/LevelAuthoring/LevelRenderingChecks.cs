using System.Collections;
using System.Numerics;
using System.Reflection;
using Engine.Render;
using Engine.Runtime.Hosting;
using Engine.Runtime.Entities;
using Veldrid;

internal static class LevelRenderingChecks
{
    public static void Run(string root, bool benchmarkOnly = false, bool flashlightOnly = false, bool profileFlashlight = false)
    {
        var readyTimer = System.Diagnostics.Stopwatch.StartNew();
        using var host = new EngineHost(profileFlashlight ? 1920 : 1280, profileFlashlight ? 1080 : 720, "Level Rendering QA");
        using var game = new Game.HL2GameModule(Path.Combine(root, "Game/Content/Levels",
            flashlightOnly ? SixRoomFlashlightTest.FileName : "sixRoomTest.json"));
        game.AssetStreamingEnabled = false;
        game.Initialize(host.Context);
        double initializedMs = readyTimer.Elapsed.TotalMilliseconds;
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        var factory = device.ResourceFactory;
        int width = (int)renderer.WorldFramebuffer.Width, height = (int)renderer.WorldFramebuffer.Height;
        var format = device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
        using var color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, format, TextureUsage.RenderTarget));
        using var depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, PixelFormat.R32_Float, TextureUsage.DepthStencil));
        using var staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, format, TextureUsage.Staging));
        using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var cache = (IDictionary)typeof(Game.HL2GameModule).GetField("_weaponModelCache", flags)!.GetValue(game)!;
        var world = (BasicWorldRenderer)typeof(Game.HL2GameModule).GetField("_world", flags)!.GetValue(game)!;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int pending;
        do
        {
            Frame(false);
            pending = 0;
            foreach (object entry in cache.Values)
            {
                Type type = entry.GetType();
                if ((bool)type.GetField("Failed", flags)!.GetValue(entry)!)
                    throw new InvalidDataException($"Model load failed: {type.GetField("Error", flags)!.GetValue(entry)}");
                if (type.GetField("Model", flags)!.GetValue(entry) == null) pending++;
            }
            if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"{pending} models still pending.");
            if (pending > 0) Thread.Sleep(10);
        } while (pending > 0);

        string output = Path.Combine(AppContext.BaseDirectory, "qa");
        Directory.CreateDirectory(output);
        if (profileFlashlight)
        {
            FlashlightPerformanceChecks.Run(game, host, framebuffer);
            return;
        }
        uint[] normal = Frame(true)!;
        world.ViewCullingEnabled = false;
        if (!EquivalentPixels(normal, Frame(true)!)) throw new InvalidDataException("Main-view culling changed visible level geometry.");
        uint referenceDraws = world.ObjectDrawCount;
        world.ViewCullingEnabled = true;
        Frame(false);
        if (world.ObjectDrawCount >= referenceDraws) throw new InvalidDataException("Main-view culling did not reduce level draws.");
        Console.WriteLine($"[ViewCulling] {referenceDraws} -> {world.ObjectDrawCount} main-view draws.");
        double readyMs = readyTimer.Elapsed.TotalMilliseconds;
        if (flashlightOnly)
        {
            CheckDarkRoom(normal);
            device.WaitForIdle();
            return;
        }
        Check(normal, "level");
        Console.WriteLine($"[LevelReady] {(CookedModelCache.Enabled ? "cooked models" : "source GLB")}, 1280x720: initialize {initializedMs:F2} ms; harness start to fully populated GPU readback {readyMs:F2} ms; {CookedModelCache.CacheHits} model hits, {CookedModelCache.FallbackLoads} source parses.");
        if (benchmarkOnly) return;
        RenderPipelineChecks.SavePng(normal, width, height, Path.Combine(output, "six-room-cooked.png"));
        typeof(Game.HL2GameModule).GetField("_flashlightOn", flags)!.SetValue(game, true);
        uint[] flashlight = Frame(true)!;
        Check(flashlight, "flashlight");
        if (world.ShadowPassCount < 1) throw new InvalidDataException("Flashlight did not render a shadow pass.");
        if (normal.Zip(flashlight).Count(p => p.First != p.Second) < 1000)
            throw new InvalidDataException("Flashlight did not change the level image.");
        RenderPipelineChecks.SavePng(flashlight, width, height, Path.Combine(output, "six-room-flashlight.png"));
        Console.WriteLine($"PASS: {cache.Count} level/weapon models GPU-ready; {world.ResidentTextureCount} shared textures, {world.TextureResidentBytes / 1048576.0:F1} MiB; {TextureCooker.CacheHits} cooked hits, {TextureCooker.FallbackDecodes} fallback decodes.");
        Console.WriteLine($"Flashlight: {world.ShadowPassCount} shadow passes, {world.ShadowDrawCount} draws. Captures: {output}");
        CheckRoomShadows();
        typeof(Game.HL2GameModule).GetField("_editorEnabled", flags)!.SetValue(game, true);
        uint[] editor = Frame(true)!;
        Check(editor, "F2 editor");
        CheckObjectUploads(editor, "F2 editor");
        RenderPipelineChecks.SavePng(editor, width, height, Path.Combine(output, "six-room-editor-cooked.png"));
        CheckReplacementReadiness();
        device.WaitForIdle();

        void CheckDarkRoom(uint[] off)
        {
            var editor = (Engine.Editor.Editor.LevelEditorController)typeof(Game.HL2GameModule).GetField("_editor", flags)!.GetValue(game)!;
            float ambient = editor.LevelFile.AmbientLight;
            editor.LevelFile.AmbientLight = 0;
            uint[] black = Frame(true)!;
            editor.LevelFile.AmbientLight = ambient;
            if (world.ShadowPassCount != 0 || black.Count(p => (p & 0x00ffffff) == 0) < black.Length * .99)
                throw new InvalidDataException("Zero-ambient reference contains unexpected illumination.");
            double brightness = off.Average(p => (double)((p & 255) + ((p >> 8) & 255) + ((p >> 16) & 255)) / 3);
            if (brightness < 1 || brightness > 30 || off.Zip(black).Count(p => p.First != p.Second) < off.Length / 3)
                throw new InvalidDataException($"Low ambient fill is missing or too bright: mean RGB {brightness:F2}/255.");
            if (CountIndicatorPixels(off, green: false) < 5)
                throw new InvalidDataException("Red puzzle indicators disappeared in the dark.");
            RenderPipelineChecks.SavePng(off, width, height, Path.Combine(output, "six-room-dark-off.png"));
            var flashlightField = typeof(Game.HL2GameModule).GetField("_flashlightOn", flags)!;
            flashlightField.SetValue(game, true);
            uint[] on = Frame(true)!;
            if (world.ShadowPassCount != 1 || off.Zip(on).Count(p => p.First != p.Second) < 10000)
                throw new InvalidDataException("Flashlight failed to illuminate the dark room with shadows.");
            CheckObjectUploads(on, "flashlight");
            RenderPipelineChecks.SavePng(on, width, height, Path.Combine(output, "six-room-dark-on.png"));
            flashlightField.SetValue(game, false);
            if (!EquivalentPixels(off, Frame(true)!))
                throw new InvalidDataException("Turning off the flashlight did not restore the dim ambient image.");

            var solved = (HashSet<string>)typeof(Game.HL2GameModule).GetField("_solvedPuzzles", flags)!.GetValue(game)!;
            var indicators = Engine.Editor.Level.LevelIO.Load(Path.Combine(root, "Game/Content/Levels", SixRoomFlashlightTest.FileName))
                .Entities.Where(e => e.Interaction?.Kind == "PuzzleIndicator").ToArray();
            solved.UnionWith(indicators.SelectMany(e => e.Interaction!.RequiredStates));
            typeof(Game.HL2GameModule).GetMethod("UpdatePuzzleIndicators", flags)!.Invoke(game, null);
            uint[] green = Frame(true)!;
            if (CountIndicatorPixels(green, green: true) < 5 || CountIndicatorPixels(green, green: false) != 0)
                throw new InvalidDataException("Completed puzzle indicators did not turn visible green in darkness.");
            RenderPipelineChecks.SavePng(green, width, height, Path.Combine(output, "six-room-dark-green.png"));
            Console.WriteLine($"PASS: flashlight GPU capture: low ambient mean RGB {brightness:F2}/255 with red indicators; zero-ambient reference >99% black; on lights textured geometry with one shadow pass; off restores ambient image; solved indicators glow green. {cache.Count} models ready. Captures: {output}");

            int CountIndicatorPixels(uint[] pixels, bool green)
                => pixels.Count(p =>
                {
                    int r = format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                        ? (int)((p >> 16) & 255) : (int)(p & 255);
                    int g = (int)((p >> 8) & 255);
                    return green ? g > 80 && g > r * 2 : r > 80 && r > g * 2;
                });
        }

        void CheckRoomShadows()
        {
            var camera = (Game.FpsCamera)typeof(Game.HL2GameModule).GetField("_camera", flags)!.GetValue(game)!;
            Vector3 originalPosition = camera.Position;
            float originalYaw = camera.Yaw, originalPitch = camera.Pitch;
            foreach (var room in SixRoomTest.Rooms)
            {
                camera.Position = new(room.Center.X, 1.65f, room.Center.Y - room.Depth * .25f);
                camera.Yaw = .4f; camera.Pitch = -.1f;
                world.ShadowCachingEnabled = world.ShadowCullingEnabled = true;
                uint[] optimized = Frame(true)!;
                uint[] cached = Frame(true)!;
                if (world.ShadowCacheHits != 1 || world.ShadowDrawCount != 0 || !EquivalentPixels(cached, optimized))
                    throw new InvalidDataException($"{room.Name}: stationary flashlight cache mismatch: hits={world.ShadowCacheHits}, draws={world.ShadowDrawCount}, pixels={cached.Zip(optimized).Count(p => p.First != p.Second)}.");
                world.ShadowCachingEnabled = world.ShadowCullingEnabled = false;
                world.ShadowBatchUploadsEnabled = false;
                world.ObjectBatchUploadsEnabled = false;
                world.ViewCullingEnabled = false;
                if (!EquivalentPixels(Frame(true)!, optimized))
                    throw new InvalidDataException($"{room.Name}: flashlight culling differs from uncached/unculled reference.");
                world.ShadowBatchUploadsEnabled = true;
                world.ObjectBatchUploadsEnabled = true;
                world.ViewCullingEnabled = true;
            }
            camera.Position = originalPosition; camera.Yaw = originalYaw; camera.Pitch = originalPitch;
            world.ShadowCachingEnabled = world.ShadowCullingEnabled = true;
            Console.WriteLine("PASS: six-room camera samples match the uncached/unculled flashlight reference; each stationary view reuses its shadow map with zero depth draws.");
        }

        static bool EquivalentPixels(uint[] first, uint[] second)
        {
            int max = 0, different = 0;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i]) different++;
                for (int shift = 0; shift < 24; shift += 8)
                    max = Math.Max(max, Math.Abs((int)((first[i] >> shift) & 255) - (int)((second[i] >> shift) & 255)));
            }
            Console.WriteLine($"[ShadowImage] {different} changed pixels, maximum RGB channel difference {max}/255.");
            return max <= 1;
        }

        void CheckObjectUploads(uint[] batched, string label)
        {
            if (world.ObjectUploadCount != 1)
                throw new InvalidDataException($"{label}: expected one main-scene upload.");
            world.ObjectBatchUploadsEnabled = false;
            world.ViewCullingEnabled = false;
            try
            {
                if (!EquivalentPixels(batched, Frame(true)!) || world.ObjectUploadCount < 100)
                    throw new InvalidDataException($"{label}: batched objects differ from the individual-upload reference.");
                Console.WriteLine($"PASS: {label}: {world.ObjectUploadCount} individual object uploads replaced by one; matching pixels.");
            }
            finally { world.ObjectBatchUploadsEnabled = true; world.ViewCullingEnabled = true; }
        }

        void CheckReplacementReadiness()
        {
            var entities = (List<Entity>)typeof(Game.HL2GameModule).GetField("_runtimeEntities", flags)!.GetValue(game)!;
            var entity = new Entity("qa-replacement", "RigidBody", "QA replacement") { Damageable = true };
            entity.Render.Shape = RuntimeShapeKind.Box;
            entity.Collider.Shape = RuntimeShapeKind.Box;
            entity.Render.ModelAssetPath = "original.glb";
            entities.Add(entity);
            var replace = typeof(Game.HL2GameModule).GetMethod("BreakDamageableEntity", flags)!;
            var update = typeof(Game.HL2GameModule).GetMethod("UpdatePendingReplacements", flags)!;
            var queue = (IDictionary)typeof(Game.HL2GameModule).GetField("_pendingReplacements", flags)!.GetValue(game)!;
            replace.Invoke(game, [entity, Path.Combine(root, "missing-replacement-qa.glb"), false, false]);
            update.Invoke(game, null);
            AssertOriginal();
            if (queue.Count != 0) throw new InvalidDataException("Failed replacement remained queued.");
            double budget = world.UploadBudgetMilliseconds;
            world.UploadBudgetMilliseconds = 0;
            string replacement = Path.GetFullPath(Path.Combine(root, "Game/Content/Models/ViewModels/DamagedCrate02.glb"));
            replace.Invoke(game, [entity, replacement, false, false]);
            AssertOriginal();
            if (queue.Count != 1) throw new InvalidDataException("Replacement was not queued while GPU uploads were blocked.");
            object entry = cache[replacement]!;
            var task = (Task<LoadedModel>?)entry.GetType().GetField("LoadTask", flags)!.GetValue(entry);
            if (task != null && !task.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Replacement CPU preparation timed out.");
            if (task == null && entry.GetType().GetField("LoadedModel", flags)!.GetValue(entry) == null)
                throw new InvalidDataException("Replacement CPU preparation was not started.");
            update.Invoke(game, null);
            AssertOriginal();
            world.UploadBudgetMilliseconds = budget;
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!entity.IsBroken)
            {
                Frame(false);
                update.Invoke(game, null);
                if (timeout.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Replacement GPU upload timed out.");
            }
            if (entity.Render.ModelAssetPath != replacement || !entity.Render.Enabled || !entity.Collider.Enabled || queue.Count != 0)
                throw new InvalidDataException("Ready replacement did not preserve visibility/collision.");
            entities.Remove(entity);
            Console.WriteLine("PASS: failed and CPU-only replacements retain original visuals/collider; swap occurs only after GPU readiness.");

            void AssertOriginal()
            {
                if (entity.IsBroken || !entity.Render.Enabled || !entity.Collider.Enabled || entity.Render.ModelAssetPath != "original.glb")
                    throw new InvalidDataException("Original object changed before replacement was ready.");
            }
        }

        uint[]? Frame(bool capture)
        {
            renderer.BeginFrame();
            game.RenderWorld(renderer);
            renderer.ResolveWorldToSwapchain(framebuffer);
            if (capture) renderer.CommandList.CopyTexture(color, staging);
            renderer.EndFrame();
            if (!capture) return null;
            device.WaitForIdle();
            var mapped = device.Map<uint>(staging, MapMode.Read);
            try
            {
                var pixels = new uint[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = mapped[x, y];
                return pixels;
            }
            finally { device.Unmap(staging); }
        }

        static void Check(uint[] pixels, string label)
        {
            int detailed = pixels.Count(p =>
            {
                int sum = (int)((p & 255) + ((p >> 8) & 255) + ((p >> 16) & 255));
                return sum > 30 && sum < 700;
            });
            if (detailed < pixels.Length / 3 || pixels.Distinct().Count() < 10000)
                throw new InvalidDataException($"{label} capture is blank, white or lacks textured detail.");
        }
    }
}

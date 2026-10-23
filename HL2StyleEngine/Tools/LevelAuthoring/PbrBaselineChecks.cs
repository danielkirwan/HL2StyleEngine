using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Engine.Runtime.Entities;
using Veldrid;

internal static class PbrBaselineChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static object Field(object instance, string name) => instance.GetType().GetField(name, Flags)!.GetValue(instance)!;
    internal static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Flags)!.SetValue(instance, value);

    public static void Run(string root, bool compare)
    {
        string references = Path.Combine(root, "Tools/LevelAuthoring/References/PbrStage0");
        if (!compare && File.Exists(Path.Combine(references, "complete.txt"))) throw new IOException("Baseline already exists; it must not be overwritten after changing rendering.");
        if (compare && !Directory.Exists(references)) throw new IOException("Capture the legacy baseline first.");
        Directory.CreateDirectory(references);
        foreach (string file in new[] { "sixRoomFlashlightTest.json", "sixRoomEmissionTest.json", "indirectLightingRoom.json", "lightProbeTest.json", PbrTestRoom.FileName })
        {
            // SDL/native UI lifetime is process-wide; isolate each host rather than reinitializing it in one process.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(PbrBaselineChecks).Assembly.Location);
            start.ArgumentList.Add("pbr-baseline-one"); start.ArgumentList.Add(file); start.ArgumentList.Add(compare ? "compare" : "capture");
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(60000)) { child.Kill(entireProcessTree: true); throw new TimeoutException(file); }
            Console.Write(stdout.GetAwaiter().GetResult()); Console.Error.Write(stderr.GetAwaiter().GetResult());
            if (child.ExitCode != 0) throw new InvalidDataException($"Baseline child failed: {file}, exit {child.ExitCode}.");
        }
        if (!compare) File.WriteAllText(Path.Combine(references, "complete.txt"), "Pre-PBR reference captures. Never overwrite to make a regression pass.\n");
    }

    public static void Capture(string root, string file, string references, bool compare, bool forceLegacy = true, bool reflections = false)
    {
        bool historicalCrate = file == PbrTestRoom.FileName &&
            Path.GetFileName(Path.TrimEndingDirectorySeparator(references)) is "PbrStage0" or "PbrDirect" or "PbrReflections";
        if (historicalCrate) Console.WriteLine("[HistoricalFixture] Original crate material retained for pre-correction reference; validate-pbr-crate tests the corrected asset.");
        var timer = Stopwatch.StartNew();
        using var host = new EngineHost(1280, 720, "Legacy rendering reference");
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        using var output = new ImageTarget(renderer);
        string path = Path.Combine(root, "Game/Content/Levels", file);
        Vector3 position; float yaw, pitch;
        using (var game = new Game.HL2GameModule(path))
        {
            game.AssetStreamingEnabled = false;
            game.Initialize(host.Context);
            // Historical references retain the original material. New captures exercise the corrected fixture.
            if (historicalCrate)
            {
                foreach (var entity in (List<Entity>)Field(game, "_runtimeEntities"))
                    if (entity.Render.ModelAssetPath == PbrCrateChecks.Corrected) entity.Render.ModelAssetPath = PbrCrateChecks.Original;
                RestoreReferenceCrate(((LevelEditorController)Field(game, "_editor")).LevelFile);
            }
            var camera = (Game.FpsCamera)Field(game, "_camera");
            position = camera.Position; yaw = camera.Yaw; pitch = camera.Pitch;
            if (file == PbrTestRoom.FileName) camera.Pitch = pitch = .04f;
            Set(game, "_flashlightOn", file == "sixRoomFlashlightTest.json");
            var editor = (LevelEditorController)Field(game, "_editor");
            editor.LevelFile.ReflectionEnvironment = reflections ? ReflectionPreset.SyntheticStudio : ReflectionPreset.None;
            var world = (BasicWorldRenderer)Field(game, "_world");
            var bake = (LevelBakeSession)Field(game, "_bakedLighting");
            // Legacy remains the reference even when the new fixture is later explicitly opted into PBR.
            var mode = typeof(LevelFile).GetProperty("LightingModel");
            if (forceLegacy && mode != null) mode.SetValue(editor.LevelFile, Enum.ToObject(mode.PropertyType, 0));
            Ready(game, (IDictionary)Field(game, "_weaponModelCache"), editor, world, bake);
            CompareOrSave(output.Frame(() => game.RenderWorld(renderer)), "game");
            Set(game, "_editorEnabled", true);
            editor.ShowColliders = editor.ShowPhysicsAabbs = editor.GizmoEnabled = false;
            Ready(game, (IDictionary)Field(game, "_weaponModelCache"), editor, world, bake);
            CompareOrSave(output.Frame(() => game.RenderWorld(renderer)), "f2");
            Console.WriteLine($"[BaselineReady] {file}: {timer.Elapsed.TotalMilliseconds:F1}ms through game/F2 readbacks; not cold-start or first-playable timing.");
        }
        // Exercise the actual standalone scene renderer without Initialize's startup/layout writes.
        Type type = Assembly.Load("HS2Editor").GetType("HS2Editor.HS2EditorModule", true)!;
        using var standalone = (IGameModule)Activator.CreateInstance(type, nonPublic: true)!;
        Set(standalone, "_context", host.Context);
        Set(standalone, "_projectRoot", root);
        Set(standalone, "_contentRoot", Path.Combine(root, "Game/Content"));
        Set(standalone, "_cameraPosition", position); Set(standalone, "_cameraYaw", yaw); Set(standalone, "_cameraPitch", pitch);
        var standaloneWorld = new BasicWorldRenderer(device, renderer.WorldOutputDescription, "Shaders");
        Set(standalone, "_world", standaloneWorld);
        var standaloneEditor = (LevelEditorController)Field(standalone, "_editor");
        standaloneEditor.LoadOrCreate(path, () => throw new IOException("Missing reference level."));
        if (historicalCrate)
            RestoreReferenceCrate(standaloneEditor.LevelFile);
        standaloneEditor.LevelFile.ReflectionEnvironment = reflections ? ReflectionPreset.SyntheticStudio : ReflectionPreset.None;
        var property = typeof(LevelFile).GetProperty("LightingModel");
        if (forceLegacy && property != null) property.SetValue(standaloneEditor.LevelFile, Enum.ToObject(property.PropertyType, 0));
        standaloneEditor.ShowColliders = standaloneEditor.ShowPhysicsAabbs = standaloneEditor.GizmoEnabled = false;
        var standaloneBake = (LevelBakeSession)Field(standalone, "_bakedLighting");
        Ready(standalone, (IDictionary)Field(standalone, "_sceneModelCache"), standaloneEditor, standaloneWorld, standaloneBake);
        CompareOrSave(output.Frame(() => ((IWorldRenderer)standalone).RenderWorld(renderer)), "hs2editor");
        device.WaitForIdle();

        void Ready(IGameModule module, IDictionary cache, LevelEditorController editor, BasicWorldRenderer world, LevelBakeSession bake)
        {
            var wait = Stopwatch.StartNew();
            world.ViewCullingEnabled = false;
            do
            {
                output.Frame(() => ((IWorldRenderer)module).RenderWorld(renderer), read: false);
                bool ready = cache.Count > 0;
                if (file == RoomReflectionChecks.FileName)
                {
                    var room = (ReflectionProbeSession)Field(module, "_roomReflections");
                    ready &= room.Ready && !room.Busy;
                }
                foreach (object entry in cache.Values)
                {
                    if ((bool)Field(entry, "Failed")) throw new InvalidDataException($"Failed reference asset: {Field(entry, "Error")}");
                    string field = entry.GetType().GetField("Model", Flags) != null ? "Model" : "RenderModel";
                    ready &= Field(entry, field) != null;
                }
                if (ready && (!reflections || world.ReflectionReady) && (!editor.LevelFile.Bake.Enabled || (bake.Ready && bake.ReadyModels == editor.LevelFile.Entities.Count(e => e.StaticLighting)))) break;
                if (wait.Elapsed.TotalSeconds > 30) throw new TimeoutException($"Reference readiness: {file}; {bake.Status}");
                Thread.Sleep(5);
            } while (true);
            world.ViewCullingEnabled = true;
        }

        void CompareOrSave(uint[] pixels, string view)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Reference images require Windows/D3D11.");
            string name = Path.GetFileNameWithoutExtension(file) + "-" + view + ".png";
            string reference = Path.Combine(references, name);
            if (!compare && !File.Exists(reference)) RenderPipelineChecks.SavePng(pixels, output.Width, output.Height, reference);
            else
            {
                using var bitmap = new Bitmap(reference);
                if (bitmap.Width != output.Width || bitmap.Height != output.Height) throw new InvalidDataException("Reference resolution changed.");
                int maximum = 0, different = 0;
                for (int y = 0; y < output.Height; y++) for (int x = 0; x < output.Width; x++)
                {
                    uint prior = (uint)bitmap.GetPixel(x, y).ToArgb(), current = pixels[y * output.Width + x];
                    if ((prior & 0xffffff) != (current & 0xffffff)) different++;
                    for (int shift = 0; shift < 24; shift += 8)
                        maximum = Math.Max(maximum, Math.Abs((int)((prior >> shift) & 255) - (int)((current >> shift) & 255)));
                }
                if (maximum > 1) throw new InvalidDataException($"Reference image changed: {name}, {different} pixels, max {maximum}/255.");
                Console.WriteLine($"PASS: {name}; {different} changed pixels, max {maximum}/255.");
            }
        }
    }

    private static void RestoreReferenceCrate(LevelFile level)
    {
        foreach (var entity in level.Entities)
            if (entity.MeshPath == PbrCrateChecks.Corrected) entity.MeshPath = PbrCrateChecks.Original;
    }

    internal sealed class ImageTarget : IDisposable
    {
        private readonly Renderer _renderer;
        private readonly Texture _color, _depth, _staging;
        public Framebuffer Framebuffer { get; }
        public int Width => (int)_color.Width;
        public int Height => (int)_color.Height;
        public ImageTarget(Renderer renderer)
        {
            _renderer = renderer;
            var factory = renderer.Factory;
            uint width = renderer.WorldFramebuffer.Width, height = renderer.WorldFramebuffer.Height;
            var format = renderer.GraphicsDevice.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
            _color = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.RenderTarget));
            _depth = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, Veldrid.PixelFormat.R32_Float, TextureUsage.DepthStencil));
            _staging = factory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.Staging));
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depth, _color));
        }
        public uint[] Frame(Action draw, bool read = true)
        {
            _renderer.BeginFrame(); draw(); _renderer.ResolveWorldToSwapchain(Framebuffer);
            if (read) _renderer.CommandList.CopyTexture(_color, _staging);
            _renderer.EndFrame();
            if (!read) return [];
            var device = _renderer.GraphicsDevice;
            device.WaitForIdle();
            var data = device.Map<uint>(_staging, MapMode.Read);
            try
            {
                var pixels = new uint[Width * Height];
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) pixels[y * Width + x] = data[x, y];
                return pixels;
            }
            finally { device.Unmap(_staging); }
        }
        public void Dispose()
        { _renderer.GraphicsDevice.WaitForIdle(); Framebuffer.Dispose(); _staging.Dispose(); _depth.Dispose(); _color.Dispose(); }
    }
}

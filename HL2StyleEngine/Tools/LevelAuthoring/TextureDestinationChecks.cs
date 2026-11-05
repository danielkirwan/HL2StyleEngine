using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Editor.Editor;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class TextureDestinationChecks
{
    internal static void Run(string root)
    {
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "texture-destination-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        using var host = new EngineHost(1280, 720, "Texture destination readiness");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(package.LevelPath, package.Root, package, Path.Combine(folder, "Saves"))
        { TextureStreamingTrialEnabled = true, TextureStreamingFullResolution = true, AssetStreamingEnabled = false };
        game.Initialize(host.Context);
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        editor.LevelFile.ReflectionStreaming = null;
        var reflections = (ReflectionProbeSession)PbrBaselineChecks.Field(game, "_roomReflections");
        var models = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        PbrBaselineChecks.Set(game, "_flashlightOn", true);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks++; }
        void Step() => target.Frame(() => game.RenderWorld(renderer), read: false);
        void Wait(Func<bool> condition, double minimumSeconds = 0)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Step(); Thread.Sleep(4);
                Check(!game.TextureTrialErrors.Any(), "Texture worker failed.");
                if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Destination did not become ready: " + game.TextureDestinationError);
            } while (timer.Elapsed.TotalSeconds < minimumSeconds || !condition());
        }
        uint[] Capture(string name)
        {
            var pixels = target.Frame(() => game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels, target.Width, target.Height, Path.Combine(folder, name + ".png"));
            return pixels;
        }
        Wait(() => reflections.Ready && !reflections.Busy && game.TextureTrialFullDetail && models.Count > 0 &&
            models.Values.Cast<object>().All(e => PbrBaselineChecks.Field(e, "Model") != null));
        long full = game.TextureTrialResidentBytes;
        var origin = RenderStressTest.Center(0) + new Vector3(0, 1.7f, -5);
        var results = new List<object>();
        foreach (int room in new[] { 3, 11, 8 })
        {
            camera.Position = origin; camera.Yaw = 0; camera.Pitch = -.18f;
            game.TextureStreamingFullResolution = false;
            Wait(() => !game.TextureTrialBusy, 2.5);
            Check(game.TextureTrialResidentBytes < full, "No reduced detail before preparation.");
            Vector3 destination = RenderStressTest.Center(room) + new Vector3(0, 1.7f, -5);
            game.PrepareTextureDestination(destination);
            Check(!game.TextureDestinationReady && game.TextureDestinationModelCount > 0, "Request reused stale readiness or found no models.");
            var timer = Stopwatch.StartNew();
            Wait(() => game.TextureDestinationReady);
            timer.Stop();
            Check(camera.Position == origin, "Preparation moved the current view.");
            Check(game.TextureDestinationError.Length == 0, "Destination reported an error.");
            camera.Position = destination;
            var first = Capture($"room-{room}-first");
            game.CancelTextureDestination();
            Check(!game.TextureDestinationPending && !game.TextureDestinationReady && game.TextureDestinationModelCount == 0, "Cancel retained request state.");
            game.TextureStreamingFullResolution = true;
            Wait(() => game.TextureTrialFullDetail);
            var reference = Capture($"room-{room}-full");
            Check(reference.Distinct().Take(1000).Count() == 1000, "Blank reference.");
            double total = 0; int significant = 0, maximum = 0;
            for (int i = 0; i < first.Length; i++)
            {
                int delta = 0;
                for (int c = 0; c < 3; c++)
                {
                    int d = Math.Abs((int)((first[i] >> (8 * c)) & 255) - (int)((reference[i] >> (8 * c)) & 255));
                    total += d; delta = Math.Max(delta, d);
                }
                maximum = Math.Max(maximum, delta); if (delta > 12) significant++;
            }
            double mean = total / (first.Length * 3), fraction = significant / (double)first.Length;
            results.Add(new { Room = room, Mean = mean, SignificantFraction = fraction, Max = maximum, PreparationMilliseconds = timer.Elapsed.TotalMilliseconds });
            File.WriteAllText(Path.Combine(folder, "images.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Check(mean <= 1 && fraction <= .01, $"Room {room} first reveal differs: {mean:F3}/255, {fraction:P3} significant.");
        }
        game.PrepareTextureDestination(origin);
        Wait(() => game.TextureDestinationReady);
        var requested = (HashSet<string>)PbrBaselineChecks.Field(game, "_textureDestinationModels");
        object failed = models[requested.First()]!;
        PbrBaselineChecks.Set(failed, "Failed", true);
        try
        {
            Step();
            Check(!game.TextureDestinationReady && game.TextureDestinationError.Length != 0, "Failed destination was reported ready.");
        }
        finally { PbrBaselineChecks.Set(failed, "Failed", false); }
        Wait(() => game.TextureDestinationReady);
        game.PrepareTextureDestination(RenderStressTest.Center(11));
        Check(!game.TextureDestinationReady, "Superseding request retained readiness.");
        game.CancelTextureDestination();
        Step();
        Check(!game.TextureDestinationReady, "Cancelled request became ready later.");
        bool invalid = false;
        try { game.PrepareTextureDestination(new(float.NaN, 0, 0)); }
        catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid && !game.TextureDestinationPending, "Invalid destination was accepted.");
        game.TextureStreamingTrialEnabled = false;
        bool disabled = false;
        try { game.PrepareTextureDestination(origin); }
        catch (InvalidOperationException) { disabled = true; }
        Check(disabled, "Destination request escaped the opt-in trial.");
        game.TextureStreamingTrialEnabled = true;
        game.PrepareTextureDestination(origin);
        PbrBaselineChecks.Set(game, "_editorEnabled", true);
        Step();
        Check(!game.TextureDestinationPending && !game.TextureDestinationReady && game.TextureDestinationError.Length != 0, "Editor entry retained stale request.");
        PbrBaselineChecks.Set(game, "_editorEnabled", false);
        game.PrepareTextureDestination(origin);
        editor.LoadFromMemory(editor.LevelPath, editor.LevelFile);
        Step();
        Check(!game.TextureDestinationPending && !game.TextureDestinationReady, "Level revision retained stale request.");
        Check(package.Fallbacks == 0, "Package fell back to source.");
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new { Checks = checks, package.Fallbacks, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: three prepared distant-room first reveals, cancel/supersede/editor/revision validation. {folder}");
    }
}

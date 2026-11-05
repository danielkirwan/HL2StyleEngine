using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Editor.Editor;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class TextureTransitionChecks
{
    internal static void Demand()
    {
        int checks = 0;
        void Check(bool value, string message) { Require(value, message); checks++; }
        var clock = new ManualClock();
        using var plan = new TextureMipResidency(clock);
        plan.Register(new("surface", TextureSemantic.Color, false, 8, 8, [336, 80, 16], _ => throw new Exception()));
        void Set(int mip, bool pin = false, long budget = 1000) => plan.Plan([new("surface", mip, 10, pin, mip)], budget);
        Set(2, budget: 20);
        Check(plan.RequestedMip("surface") == 0 && plan.EstimatedBytes == 336 && plan.OverBudget, "Retention hid actual planned cost.");
        clock.Advance(1); Set(2);
        Check(plan.RequestedMip("surface") == 0, "A short look-away reduced detail.");
        Set(0); clock.Advance(1); Set(2);
        Check(plan.RequestedMip("surface") == 0, "Returning to detail did not reset retention.");
        clock.Advance(1.6); Set(2);
        Check(plan.RequestedMip("surface") == 2 && plan.EstimatedBytes == 16, "Stable distant texture never demoted.");
        Set(0, true, 1);
        Check(plan.RequestedMip("surface") == 0 && plan.OverBudget, "Near/held quality pin was delayed or degraded.");
        Set(2); clock.Advance(1); Set(1); clock.Advance(1); Set(1);
        Check(plan.RequestedMip("surface") == 0, "Changed coarse demand reused a stale timer.");
        clock.Advance(.6); Set(1);
        Check(plan.RequestedMip("surface") == 1, "Stable revised demand never demoted.");
        plan.Plan([], 1);
        Check(plan.RequestedMip("surface") == 0, "Unknown owner was not immediately protected.");
        for (int i = 0; i < 40; i++)
        {
            Set(2); clock.Advance(.1); Set(0); clock.Advance(.1);
            Check(plan.RequestedMip("surface") == 0, "Rapid door/turn oscillation changed quality.");
        }
        Console.WriteLine($"PASS: {checks} deterministic temporal texture-demand checks.");
    }

    internal static void Images(string root)
    {
        Demand();
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "texture-transitions-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        using var host = new EngineHost(1280, 720, "Texture transition image checks");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(package.LevelPath, package.Root, package, Path.Combine(folder, "Saves"))
        { TextureStreamingTrialEnabled = true, TextureStreamingFullResolution = true, AssetStreamingEnabled = false };
        game.Initialize(host.Context);
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        // Isolate mip quality from independent model/reflection arrival; this is not a performance run.
        editor.LevelFile.ReflectionStreaming = null;
        var room = (ReflectionProbeSession)PbrBaselineChecks.Field(game, "_roomReflections");
        var models = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        PbrBaselineChecks.Set(game, "_flashlightOn", true);
        var results = new List<object>();
        var failures = new List<string>();
        long minimum = long.MaxValue, full = 0;
        void Wait(bool restore, double minimumSeconds = 0)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                target.Frame(() => game.RenderWorld(renderer), read: false);
                Require(!game.TextureTrialErrors.Any(), "Texture worker failed.");
                bool ready = room.Ready && !room.Busy && models.Count > 0 && models.Values.Cast<object>().All(e =>
                    !(bool)PbrBaselineChecks.Field(e, "Failed") && PbrBaselineChecks.Field(e, "Model") != null);
                if (ready && !game.TextureTrialBusy && (!restore || game.TextureTrialFullDetail) && watch.Elapsed.TotalSeconds >= minimumSeconds) return;
                if (watch.Elapsed.TotalSeconds > 30) throw new TimeoutException("Texture image readiness: " + room.Status);
                Thread.Sleep(4);
            }
        }
        uint[] Frame(string name)
        {
            var pixels = target.Frame(() => game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels, target.Width, target.Height, Path.Combine(folder, name + ".png"));
            return pixels;
        }
        Wait(true);
        full = game.TextureTrialResidentBytes;
        camera.Position = RenderStressTest.Center(0) + new Vector3(0, 1.7f, -5);
        game.TextureStreamingFullResolution = false; Wait(false, 2.5);
        minimum = Math.Min(minimum, game.TextureTrialResidentBytes);
        int previous = 0;
        foreach (int index in new[] { 1, 2, 3, 7 })
        {
            var from = RenderStressTest.Center(previous); var to = RenderStressTest.Center(index);
            var direction = Vector3.Normalize(to - from);
            camera.Position = (from + to) * .5f - direction * 2 + Vector3.UnitY * 1.7f;
            camera.Yaw = MathF.Atan2(direction.X, direction.Z); camera.Pitch = -.18f;
            Wait(false, 2.5);
            Frame($"room-{index}-approach");
            camera.Position = (from + to) * .5f + direction * 2 + Vector3.UnitY * 1.7f;
            var first = Frame($"room-{index}-first");
            for (int i = 0; i < 15; i++) { target.Frame(() => game.RenderWorld(renderer), read: false); Thread.Sleep(16); }
            var early = Frame($"room-{index}-early");
            Wait(false, 2.5);
            var settled = Frame($"room-{index}-settled");
            minimum = Math.Min(minimum, game.TextureTrialResidentBytes);
            game.TextureStreamingFullResolution = true; Wait(true);
            var reference = Frame($"room-{index}-full");
            Require(reference.Distinct().Take(1000).Count() == 1000, "Blank quality reference.");
            foreach (var (name, pixels) in new[] { ("first", first), ("early", early), ("settled", settled) })
            {
                double total = 0; int changed = 0, max = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    int delta = 0;
                    for (int c = 0; c < 3; c++)
                    {
                        int d = Math.Abs((int)((pixels[i] >> (c * 8)) & 255) - (int)((reference[i] >> (c * 8)) & 255));
                        total += d; delta = Math.Max(delta, d);
                    }
                    max = Math.Max(max, delta); if (delta > 12) changed++;
                }
                double mean = total / (pixels.Length * 3), fraction = changed / (double)pixels.Length;
                results.Add(new { Room = index, Phase = name, Mean = mean, SignificantFraction = fraction, Max = max });
                if (mean > 1 || fraction > .01) failures.Add($"Room {index} {name}: mean {mean:F3}, fraction {fraction:P3}");
                Console.WriteLine($"[TextureQuality] room {index} {name}: mean {mean:F4}/255, >12 delta {fraction:P3}, max {max}.");
            }
            game.TextureStreamingFullResolution = false; Wait(false, 2.5);
            previous = index;
        }
        game.TextureStreamingFullResolution = true; Wait(true);
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
        { FullBytes = full, MinimumBytes = minimum, game.TextureTrialReplacements, package.Fallbacks, Results = results, Failures = failures }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("[TextureQuality] " + folder);
        Require(minimum < full && game.TextureTrialReplacements > 4, "No reduced residency exercised.");
        Require(package.Fallbacks == 0 && failures.Count == 0, "Transition quality failed: " + string.Join("; ", failures));
        Console.WriteLine("PASS: twelve real-level first/early/settled comparisons, full-detail restoration and unchanged quality limits.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds) => _ticks += (long)(seconds * 1000);
    }
}

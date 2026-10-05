using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Engine.Editor.Level;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game;
using Veldrid;

internal sealed class StartupCheck : IGameModule, IWorldRenderer, IOverlayRenderer, IInputConsumer
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly HL2GameModule _game;
    private readonly Stopwatch _clock;
    private double _initializeMs;
    private double? _firstPresentedMs;
    private double? _assetsReadyMs;
    private int _frames;
    private int _models;
    private int _failedModels;
    private uint _width, _height;
    public InputState InputState => _game.InputState;
    private StartupCheck(string level, Stopwatch clock) { _game = new(level); _clock = clock; }

    internal static void Run(string root, string output, string codec)
    {
        string level = Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
        Environment.SetEnvironmentVariable("HS2_BENCH_LEVEL", level);
        Environment.SetEnvironmentVariable("HS2_BENCH_CODEC", codec);
        Environment.SetEnvironmentVariable("HS2_BENCH_CACHE", Path.Combine(root, "Tools/REDoxBenchmark/.work/cache"));
        Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "1");
        Environment.SetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION", "0");
        var clock = Stopwatch.StartNew();
        using var host = new EngineHost(1280, 720, "HS2 isolated loading benchmark");
        double hostMs = clock.Elapsed.TotalMilliseconds;
        using var probe = new StartupCheck(level, clock);
        host.Run(probe);
        var report = new { labelDate = "2026-10-02", codec, runtime = RuntimeInformation.FrameworkDescription,
            hostMs, initializeMs = probe._initializeMs, firstPresentedMs = probe._firstPresentedMs,
            assetsReadyPresentedMs = probe._assetsReadyMs, models = probe._models, failedModels = probe._failedModels,
            width = probe._width, height = probe._height,
            levelReads = LevelIO.Measurements.Select(m => new { path = m.Path, milliseconds = m.Milliseconds }),
            note = "Fresh process; Release; requested 1280x720, actual framebuffer size recorded separately; VSync on; OS disk cache not flushed. Clock starts before window/device creation, excludes process/CLR entry. Ready means a presented world frame with every requested model uploaded and no failed/pending cache entry. Includes production loading-overlay delay before ready is recorded. No saves loaded (explicit --level)." };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (probe._assetsReadyMs == null || probe._failedModels != 0) throw new InvalidDataException("Scene did not reach ready state.");
        Console.WriteLine("Saved " + output);
    }

    public void Initialize(EngineContext context)
    {
        long start = Stopwatch.GetTimestamp();
        _game.Initialize(context);
        _initializeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public void Update(float dt, InputSnapshot input)
    {
        if (_frames > 0)
        {
            _firstPresentedMs ??= _clock.Elapsed.TotalMilliseconds;
            var cache = (IDictionary)typeof(HL2GameModule).GetField("_weaponModelCache", Flags)!.GetValue(_game)!;
            _models = cache.Count;
            _failedModels = 0;
            bool ready = cache.Count > 0;
            foreach (object entry in cache.Values)
            {
                Type type = entry.GetType();
                if ((bool)type.GetField("Failed", Flags)!.GetValue(entry)!) _failedModels++;
                if (type.GetField("Model", Flags)!.GetValue(entry) == null) ready = false;
            }
            float loading = (float)typeof(HL2GameModule).GetField("_loadingOverlayTimer", Flags)!.GetValue(_game)!;
            if (ready && loading <= 0) _assetsReadyMs ??= _clock.Elapsed.TotalMilliseconds;
        }
        _game.Update(dt, input);
    }
    public void FixedUpdate(float dt) => _game.FixedUpdate(dt);
    public void DrawImGui() => _game.DrawImGui();
    public void RenderWorld(Renderer renderer)
    {
        _width = renderer.GraphicsDevice.MainSwapchain.Framebuffer.Width;
        _height = renderer.GraphicsDevice.MainSwapchain.Framebuffer.Height;
        _game.RenderWorld(renderer);
        _frames++;
    }
    public void RenderOverlay(Renderer renderer) => _game.RenderOverlay(renderer);
    public void Dispose() => _game.Dispose();
}

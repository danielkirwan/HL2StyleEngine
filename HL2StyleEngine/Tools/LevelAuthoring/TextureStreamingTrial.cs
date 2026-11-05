using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Game;
using ImGuiNET;
using Veldrid;

// Real gameplay, isolated package/saves; no level JSON or normal launcher is modified.
internal sealed class TextureStreamingTrial : IGameModule, IWorldRenderer, IOverlayRenderer, IInputConsumer
{
    private readonly HL2GameModule _game;
    private readonly bool _automatic, _reference, _editorCheck;
    private readonly List<double> _intervals = new();
    private readonly List<object> _memory = new();
    private SourcePlayerMotor _motor = null!;
    private int _frames, _waits, _heldActions;
    private int _editorAssertions, _referenceToggles;
    private Key? _heldKey;
    private long _lastStamp, _fullBytes, _peakBytes, _minimumBytes = long.MaxValue;
    private float _distance;
    private bool _disposed;
    private Vector3? _destination;
    private int _destinationIndex, _releaseDestinationFrame = -1;
    private readonly int[] _destinationRooms = [3, 11, 8, 0];
    public InputState InputState => _game.InputState;
    private TextureStreamingTrial(AssetPackage package, string saves, bool automatic, bool reference, bool editorCheck)
    {
        _automatic = automatic; _reference = reference; _editorCheck = editorCheck;
        _game = new(package.LevelPath, package.Root, package, saves)
        { TextureStreamingTrialEnabled = !reference, TextureStreamingFullResolution = automatic && !editorCheck, TextureStreamingBudgetMiB = 48 };
    }
    internal static void Run(string root, bool automatic, bool reference, bool editorCheck = false)
    {
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "texture-level-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        string? oldFrames = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES"), oldReport = Environment.GetEnvironmentVariable("HS2_FRAME_REPORT");
        if (automatic)
        {
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", editorCheck ? "900" : "3600");
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", Path.Combine(folder, "frames.json"));
        }
        using var trial = new TextureStreamingTrial(package, Path.Combine(folder, "Saves"), automatic, reference, editorCheck);
        Exception? failure = null;
        try
        {
            using var host = new EngineHost(1920, 1080, "HS2 Texture Streaming Trial");
            try
            {
                host.Run(trial);
                if (editorCheck)
                {
                    if (trial._editorAssertions != 3 || trial._referenceToggles != 2 || !trial._game.TextureTrialFullDetail || trial._game.TextureTrialErrors.Any())
                        throw new InvalidDataException("Editor/toggle restoration workflow failed.");
                    Console.WriteLine("PASS: coarse F2 deferred, full-detail editor entry/exit, two F6 snapshot toggles and restoration. Not a performance sample.");
                }
                else if (automatic)
                {
                    var samples = trial._intervals.Order().ToArray();
                    if (samples.Length != 3600 || trial._distance < .1f || trial._heldActions != 2)
                        throw new InvalidDataException("Incomplete real-level route.");
                    if (trial._game.TextureTrialErrors.Any() || package.Fallbacks != 0 || trial._waits != 0 || !trial._game.TextureTrialFullDetail)
                        throw new InvalidDataException("Texture/streaming error or full-detail restoration incomplete.");
                    if (!reference && (trial._game.TextureTrialReplacements < 4 || trial._minimumBytes >= trial._fullBytes))
                        throw new InvalidDataException("Route did not exercise reduced texture residency.");
                    if (samples.Average() > 17 || samples[(int)(samples.Length * .95)] > 21 || samples.Count(x => x > 33.3) > 1 || samples.Any(x => x > 100))
                        throw new InvalidDataException("Real-level trial exceeded unchanged frame-time gates.");
                }
            }
            finally { trial.Dispose(); } // Retire all resources before the host/device is disposed.
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            var samples = trial._intervals.Order().ToArray();
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
            {
                Reference = reference, Automatic = automatic, EditorWorkflow = editorCheck, Samples = samples.Length, Failure = failure?.ToString(),
                Mean = samples.DefaultIfEmpty().Average(), P95 = samples.Length == 0 ? 0 : samples[(int)(samples.Length * .95)],
                Over33ms = samples.Count(x => x > 33.3), Over100ms = samples.Count(x => x > 100),
                FullTextureBytes = trial._fullBytes, MinimumTextureBytes = trial._minimumBytes, PeakTextureBytes = trial._peakBytes,
                Replacements = trial._game.TextureTrialReplacements, trial._waits, trial._distance, trial._heldActions,
                package.Hits, package.Fallbacks, Memory = trial._memory,
                Notes = "Warm/uncontrolled OS cache. Real gameplay plus scripted room teleports, look turns and gravity hold/release. GPU payload accounting, not total VRAM/RAM; no forced GC."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", oldFrames);
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", oldReport);
            Console.WriteLine("[TextureTrial] " + folder);
        }
        if (failure != null) throw new InvalidDataException("Texture trial failed; evidence retained.", failure);
    }
    public void Initialize(EngineContext context)
    {
        _game.Initialize(context);
        _motor = (SourcePlayerMotor)Field("_motor");
    }
    public void Update(float dt, InputSnapshot input)
    {
        long stamp = Stopwatch.GetTimestamp();
        if (_frames >= 120 && _lastStamp != 0) _intervals.Add(Stopwatch.GetElapsedTime(_lastStamp, stamp).TotalMilliseconds);
        _lastStamp = stamp;
        if (_automatic)
        {
            if (!_editorCheck) _game.TextureStreamingFullResolution = _reference || _frames < 420 || _frames >= 3120;
            if (!_editorCheck && _frames >= 420 && (_frames - 420) % 300 == 0)
            {
                int[] rooms = [3, 11, 8, 6, 0, 3, 11, 8, 0, 0, 0];
                _motor.Position = RenderStressTest.Center(rooms[Math.Min(rooms.Length - 1, (_frames - 420) / 300)]) + new Vector3(0, .1f, -5);
                _motor.Velocity = Vector3.Zero;
            }
            if (_frames == 480)
            {
                var entity = ((List<Entity>)Field("_runtimeEntities")).Where(e => e.CanPickUp && !e.IsBroken && e.Physics.BoxBody != null && e.Render.Enabled)
                    .OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, _motor.Position)).First();
                Call("PickUp", entity, true);
                if (!entity.IsHeld) throw new InvalidDataException("Trial gravity hold failed.");
                _heldActions++;
            }
            if (_frames == 600) { Call("DropHeld"); _heldActions++; }
            Key? next = _frames < 120 ? null : (_frames / 90) % 2 == 0 ? Key.W : Key.S;
            var keys = new List<KeyEvent>();
            if (_heldKey != next)
            {
                if (_heldKey.HasValue) keys.Add(new(_heldKey.Value, false, ModifierKeys.None));
                if (next.HasValue) keys.Add(new(next.Value, true, ModifierKeys.None));
                _heldKey = next;
            }
            if (_frames % 300 == 150) keys.Add(new(Key.F, true, ModifierKeys.None));
            if (_frames % 300 == 151) keys.Add(new(Key.F, false, ModifierKeys.None));
            if (_editorCheck)
            {
                if (_frames == 420 && _game.TextureTrialFullDetail) throw new InvalidDataException("F2 test never reached coarse residency.");
                if (_frames is 420 or 600 or 700) keys.Add(new(Key.F2, true, ModifierKeys.None));
                if (_frames is 421 or 601 or 701) keys.Add(new(Key.F2, false, ModifierKeys.None));
                if (_frames is 750 or 850) keys.Add(new(Key.F6, true, ModifierKeys.None));
                if (_frames is 751 or 851) keys.Add(new(Key.F6, false, ModifierKeys.None));
                if (_frames is 425 or 620 or 720)
                {
                    if ((bool)Field("_editorEnabled") != (_frames == 620) || (_frames != 425 && !_game.TextureTrialFullDetail))
                        throw new InvalidDataException("Editor entered early or lost full-detail residency.");
                    _editorAssertions++;
                }
            }
            InputState.SetRelativeMouseDelta(new(MathF.Cos(_frames * .02f) * dt * 100, 0));
            input = new Snapshot(keys);
        }
        if (input.KeyEvents.Any(k => k.Key == Key.F6 && k.Down))
        { _game.TextureStreamingFullResolution = !_game.TextureStreamingFullResolution; _referenceToggles++; }
        if (!_automatic)
        {
            if (input.KeyEvents.Any(k => k.Key == Key.F2 && k.Down))
            { _game.CancelTextureDestination(); _destination = null; _releaseDestinationFrame = -1; }
            if (input.KeyEvents.Any(k => k.Key == Key.F7 && k.Down)) PrepareDestination();
            if (_destination.HasValue && _game.TextureDestinationReady)
            {
                _motor.Position = _destination.Value;
                _motor.Velocity = Vector3.Zero;
                _destination = null;
                _releaseDestinationFrame = _frames + 2;
            }
            else if (_destination.HasValue && !_game.TextureDestinationPending) _destination = null;
            if (_frames == _releaseDestinationFrame) _game.CancelTextureDestination();
        }
        _game.Update(dt, input);
        if (!float.IsFinite(_motor.Position.Y) || _motor.Position.Y < -5) throw new InvalidDataException("Player fell through trial level.");
    }
    public void FixedUpdate(float dt)
    {
        var before = _motor.Position; _game.FixedUpdate(dt);
        if (_frames >= 120) _distance += Vector2.Distance(new(before.X, before.Z), new(_motor.Position.X, _motor.Position.Z));
    }
    public void DrawImGui()
    {
        _game.DrawImGui();
        if (_automatic) return;
        ImGui.SetNextWindowPos(new(12, 12), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new(330, 220), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(330, 220), new(float.MaxValue, float.MaxValue));
        ImGui.Begin("Texture Streaming Trial", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoNavFocus);
        bool full = _game.TextureStreamingFullResolution;
        if (ImGui.Checkbox("Full-detail reference", ref full)) _game.TextureStreamingFullResolution = full;
        ImGui.Text($"Textures: {_game.TextureTrialResidentBytes / 1048576.0:F2} MiB");
        ImGui.Text($"Planned: {_game.TextureTrialEstimatedBytes / 1048576.0:F2} MiB");
        ImGui.Text($"Replacements: {_game.TextureTrialReplacements}");
        if (_game.TextureTrialOverBudget) ImGui.Text("Quality pins exceed target");
        if (_game.TextureTrialBusy) ImGui.Text("Preparing textures...");
        if (ImGui.Button(_destination.HasValue ? "Cancel destination" : "Prepare distant room"))
        {
            if (_destination.HasValue) { _game.CancelTextureDestination(); _destination = null; }
            else PrepareDestination();
        }
        if (_destination.HasValue) ImGui.Text($"Destination: {_game.TextureDestinationModelCount} models preparing");
        if (_game.TextureDestinationError.Length != 0) ImGui.TextWrapped(_game.TextureDestinationError);
        foreach (string error in _game.TextureTrialErrors.Take(1)) ImGui.TextWrapped(error);
        ImGui.End();
    }
    private void PrepareDestination()
    {
        if (!_game.TextureStreamingTrialEnabled) return;
        _releaseDestinationFrame = -1;
        var position = RenderStressTest.Center(_destinationRooms[_destinationIndex++ % _destinationRooms.Length]) + new Vector3(0, .1f, -5);
        _game.PrepareTextureDestination(position);
        _destination = position;
    }
    public void RenderWorld(Renderer renderer)
    {
        _game.RenderWorld(renderer);
        _peakBytes = Math.Max(_peakBytes, _game.TextureTrialResidentBytes);
        if (_frames >= 120 && _frames < 420) _fullBytes = Math.Max(_fullBytes, _game.TextureTrialResidentBytes);
        if (_frames >= 420 && _frames < 3120) _minimumBytes = Math.Min(_minimumBytes, _game.TextureTrialResidentBytes);
        if (_frames >= 120 && _game.StreamingWaiting) _waits++;
        if (_frames % 300 == 119)
        {
            using var process = Process.GetCurrentProcess();
            _memory.Add(new { Frame = _frames, TextureBytes = _game.TextureTrialResidentBytes, _game.StreamingResidentModels,
                PrivateBytes = process.PrivateMemorySize64, ManagedBytes = GC.GetTotalMemory(false), _game.TextureTrialReplacements });
        }
    }
    public void RenderOverlay(Renderer renderer) { _game.RenderOverlay(renderer); _frames++; }
    public void Dispose() { if (_disposed) return; _disposed = true; _game.Dispose(); }
    private object Field(string name) => typeof(HL2GameModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_game)!;
    private object? Call(string name, params object?[] args) => typeof(HL2GameModule).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_game, args);
    private sealed class Snapshot(List<KeyEvent> keys) : InputSnapshot
    {
        public IReadOnlyList<KeyEvent> KeyEvents => keys;
        public IReadOnlyList<MouseEvent> MouseEvents => [];
        public IReadOnlyList<char> KeyCharPresses => [];
        public Vector2 MousePosition => Vector2.Zero;
        public float WheelDelta => 0;
        public bool IsMouseDown(MouseButton button) => false;
    }
}

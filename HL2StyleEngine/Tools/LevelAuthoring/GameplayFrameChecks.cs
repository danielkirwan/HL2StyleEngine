using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game;
using Engine.Runtime.Entities;
using Engine.Editor.Editor;
using Game.Inventory;
using Veldrid;

internal sealed class GameplayFrameChecks : IGameModule, IWorldRenderer, IOverlayRenderer, IInputConsumer
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly HL2GameModule _game;
    private readonly bool _torch;
    private readonly bool _individualUploads;
    private readonly bool _referenceVisibility, _stress;
    private readonly List<double> _intervals = new();
    private SourcePlayerMotor _motor = null!;
    private BasicWorldRenderer _world = null!;
    private int _frames, _ticks;
    private long _objectUploads;
    private long _draws;
    private int _streamWaitFrames, _stressActions, _peakDebris;
    private double _time, _simulationTime, _distance;
    private Key? _held;
    private long _previousStamp;
    public InputState InputState => _game.InputState;
    private GameplayFrameChecks(string level, bool torch, bool individualUploads, bool referenceVisibility, bool stress)
    { _game = new(level); _torch = torch; _individualUploads = individualUploads; _referenceVisibility = referenceVisibility; _stress = stress; }

    internal static void Run(string root, bool torch, bool individualUploads = false, bool referenceVisibility = false, bool stress = false)
    {
        string? prior = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES");
        string? priorNative = Environment.GetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION");
        string? priorPreview = Environment.GetEnvironmentVariable("HS2_RMLUI_FORCE_PREVIEW_MODALS");
        Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "300");
        Environment.SetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION", "1");
        Environment.SetEnvironmentVariable("HS2_RMLUI_FORCE_PREVIEW_MODALS", "0");
        try
        {
            using var host = new EngineHost(1920, 1080, "Full gameplay flashlight profile");
            using var probe = new GameplayFrameChecks(Path.Combine(root, "Game/Content/Levels", SixRoomFlashlightTest.FileName), torch, individualUploads, referenceVisibility, stress);
            Console.WriteLine($"[GameplayProfile] torch={(torch ? "on" : "off")}; uploads={(individualUploads ? "individual reference" : "batched")}; actual host loop, scripted W/S and relative mouse, production HUD and launcher UI settings, physics and VSync/presentation; 120 warmup + 300 measured frames.");
            host.Run(probe);
            if (probe._intervals.Count != 300 || probe._distance < .1 || probe._ticks == 0)
                throw new InvalidDataException("Gameplay profile did not complete with real player movement.");
            probe._intervals.Sort();
            Console.WriteLine($"[GameplayMovement] distance {probe._distance:F3}m / simulated {probe._simulationTime:F3}s = {probe._distance / probe._simulationTime:F3}m/s including acceleration/reversals; {probe._ticks} fixed ticks; wall frame mean {probe._intervals.Average():F3}ms p95 {probe._intervals[284]:F3}ms; >16.7ms {probe._intervals.Count(x => x > 16.7)}, >33.3ms {probe._intervals.Count(x => x > 33.3)} / 300.");
            Console.WriteLine($"[GameplayUploads] {probe._objectUploads / 300.0:F1} main-scene uploads/frame; {probe._intervals.Count(x => x > 100)} frames over the 100ms simulation clamp.");
            Console.WriteLine($"[VisibilityStreaming] reference={referenceVisibility}; draws/frame {probe._draws / 300.0:F1}; streaming wait frames {probe._streamWaitFrames}; resident {probe._game.StreamingResidentModels} models / {probe._game.StreamingResidentBytes / 1048576.0:F1} MiB; {probe._game.StreamingZoneCount} dependency zones; {probe._game.StreamingEvictions} evictions.");
            if (stress)
            {
                if (probe._stressActions != 5 || probe._peakDebris == 0) throw new InvalidDataException("Stress actions did not exercise debris/doors/gravity holding/inventory.");
                Console.WriteLine($"[GameplayStress] PASS: opened puzzle shutters, broke six crates, gravity-held/released a prop, opened/closed inventory; {probe._peakDebris} peak fragment bodies; {probe._stressActions} event groups.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", prior);
            Environment.SetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION", priorNative);
            Environment.SetEnvironmentVariable("HS2_RMLUI_FORCE_PREVIEW_MODALS", priorPreview);
        }
    }

    public void Initialize(EngineContext context)
    {
        _game.Initialize(context);
        _motor = (SourcePlayerMotor)typeof(HL2GameModule).GetField("_motor", Flags)!.GetValue(_game)!;
        _world = (BasicWorldRenderer)typeof(HL2GameModule).GetField("_world", Flags)!.GetValue(_game)!;
        _world.ObjectBatchUploadsEnabled = !_individualUploads;
        _world.ViewCullingEnabled = !_referenceVisibility;
        _game.AssetStreamingEnabled = !_referenceVisibility;
        typeof(HL2GameModule).GetField("_flashlightOn", Flags)!.SetValue(_game, _torch);
    }

    public void Update(float dt, InputSnapshot actualInput)
    {
        long stamp = Stopwatch.GetTimestamp();
        if (_frames >= 120 && _previousStamp != 0) _intervals.Add(Stopwatch.GetElapsedTime(_previousStamp, stamp).TotalMilliseconds);
        _previousStamp = stamp;
        if (_stress) StressActions();
        if (_frames >= 120) _time += dt;
        Key? next = _frames < 120 ? null : (int)(_time % 2) == 0 ? Key.W : Key.S;
        var keys = new List<KeyEvent>();
        if (_held != next)
        {
            if (_held.HasValue) keys.Add(new KeyEvent(_held.Value, false, ModifierKeys.None));
            if (next.HasValue) keys.Add(new KeyEvent(next.Value, true, ModifierKeys.None));
            _held = next;
        }
        // Use the same relative-mouse path as gameplay; no direct camera/velocity manipulation.
        InputState.SetRelativeMouseDelta(_frames < 120 ? Vector2.Zero : new Vector2((float)Math.Cos(_time * 2) * 80 * dt, 0));
        _game.Update(dt, new Snapshot(keys));
        if (_stress) _peakDebris = Math.Max(_peakDebris, ((System.Collections.ICollection)Field("_fractureDebrisPieces")).Count);
    }

    public void FixedUpdate(float dt)
    {
        Vector3 before = _motor.Position;
        _game.FixedUpdate(dt);
        if (_frames >= 120)
        {
            _ticks++; _simulationTime += dt;
            Vector3 delta = _motor.Position - before;
            _distance += new Vector2(delta.X, delta.Z).Length();
        }
    }
    public void DrawImGui() => _game.DrawImGui();
    public void RenderWorld(Renderer renderer)
    {
        _game.RenderWorld(renderer);
        if (_frames >= 120)
        {
            _objectUploads += _world.ObjectUploadCount;
            _draws += _world.ObjectDrawCount;
            if (_game.StreamingWaiting) _streamWaitFrames++;
        }
    }
    public void RenderOverlay(Renderer renderer) { _game.RenderOverlay(renderer); _frames++; }
    public void Dispose() => _game.Dispose();

    private object Field(string name) => typeof(HL2GameModule).GetField(name, Flags)!.GetValue(_game)!;
    private object? Call(string name, params object?[] args) => typeof(HL2GameModule).GetMethod(name, Flags)!.Invoke(_game, args);
    private void StressActions()
    {
        var entities = (List<Entity>)Field("_runtimeEntities");
        if (_frames == 130)
        {
            var editor = (LevelEditorController)Field("_editor");
            ((HashSet<string>)Field("_solvedPuzzles")).UnionWith(editor.LevelFile.Entities.Where(e => e.Interaction != null).Select(e => e.Interaction!.StateId));
            _stressActions++;
        }
        if (_frames == 150)
        {
            foreach (var crate in entities.Where(e => e.Damageable && !e.IsBroken).OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, _motor.Position)).Take(6).ToArray())
                Call("ApplyEntityDamage", crate, 10000f, "shotgun", (Vector3?)crate.Transform.Position);
            _stressActions++;
        }
        if (_frames == 170)
        {
            var held = entities.Where(e => e.CanPickUp && !e.IsBroken && e.Physics.BoxBody != null && e.Render.Enabled)
                .OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, _motor.Position)).First();
            Call("PickUp", held, true);
            if (!held.IsHeld) throw new InvalidDataException("Stress gravity hold failed.");
            _stressActions++;
        }
        if (_frames == 210)
        {
            Call("DropHeld");
            ((InventoryContainer)Field("_inventory")).Add(ItemCatalog.DamagedCable, 1);
            Call("SetInventoryOpen", true);
            _stressActions++;
        }
        if (_frames == 250) { Call("SetInventoryOpen", false); _stressActions++; }
    }

    private sealed class Snapshot(IReadOnlyList<KeyEvent> keys) : InputSnapshot
    {
        public IReadOnlyList<KeyEvent> KeyEvents => keys;
        public IReadOnlyList<MouseEvent> MouseEvents => Array.Empty<MouseEvent>();
        public IReadOnlyList<char> KeyCharPresses => Array.Empty<char>();
        public Vector2 MousePosition => new(960, 500);
        public float WheelDelta => 0;
        public bool IsMouseDown(MouseButton button) => false;
    }
}

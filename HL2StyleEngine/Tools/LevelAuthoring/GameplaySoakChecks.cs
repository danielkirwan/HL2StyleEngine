using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Input.Devices;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Engine.UI;
using Game;
using Game.Inventory;
using Veldrid;

// Diagnostic wrapper only. All mutations use the existing gameplay operations.
internal sealed class GameplaySoakChecks : IGameModule, IWorldRenderer, IOverlayRenderer, IInputConsumer
{
    private const int CycleFrames = 1200;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly HL2GameModule _game;
    private readonly string _folder;
    private readonly bool _native;
    private readonly bool _puzzles;
    private Vector3 _spawn;
    private readonly List<Interval> _intervals = new();
    private readonly List<object> _events = new(), _memory = new();
    private readonly List<double> _slotTimes = new();
    private readonly List<object> _saveTimes = new();
    private GameplayUiLayer _ui = null!;
    private SourcePlayerMotor _motor = null!;
    private int _frame, _actions, _completedCycles, _peakDebris, _transitions;
    private long _previousStamp, _transitionStart;
    private string _previousPhase = "startup", _phase = "startup";
    private bool _loading = true;
    private Key? _held;
    private double _simulation, _distance;
    private string _savedInventory = "";
    private string _savedBroken = "", _savedDrops = "";
    private int _savedHealth;
    private long _firstSettledPrivate, _lastSettledPrivate, _firstSettledModels, _lastSettledModels;
    private readonly record struct Interval(int Frame, string Phase, double Milliseconds);
    public InputState InputState => _game.InputState;
    private GameplaySoakChecks(string root, string folder, bool native, bool puzzles)
    {
        _folder = folder; _native = native; _puzzles = puzzles;
        _game = new(Path.Combine(root, "Game/Content/Levels", puzzles ? "sixRoomShadowTest.json" : "atmosphereTest.json"),
            saveDirectory: Path.Combine(folder, "Saves"));
    }

    internal static void Run(string root, bool native, int cycles = 6, bool puzzles = false)
    {
        if (cycles is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(cycles));
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "gameplay-soak-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + (native ? "-native" : "-fallback"));
        Directory.CreateDirectory(folder);
        string? previousFrames = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES"), previousReport = Environment.GetEnvironmentVariable("HS2_FRAME_REPORT");
        Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", (cycles * CycleFrames).ToString());
        Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", Path.Combine(folder, "frames.json"));
        using var probe = new GameplaySoakChecks(root, folder, native, puzzles);
        Exception? failure = null;
        try
        {
            using var host = new EngineHost(1920, 1080, "Gameplay stability: " + (native ? "native inventory" : "fallback inventory"));
            host.Run(probe);
            probe.Validate(cycles);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
            {
                Version = 1, Native = native, Puzzles = puzzles, GeometryReuse = Engine.Physics.Collision.WorldColliderCache.ReuseEnabled,
                Cycles = cycles, Frames = probe._frame, Failure = failure?.ToString(),
                Actions = probe._actions, CompletedCycles = probe._completedCycles, PeakDebris = probe._peakDebris,
                Distance = probe._distance, SimulationSeconds = probe._simulation,
                FirstSettledPrivateBytes = probe._firstSettledPrivate, LastSettledPrivateBytes = probe._lastSettledPrivate,
                FirstSettledModelBytes = probe._firstSettledModels, LastSettledModelBytes = probe._lastSettledModels,
                Intervals = probe._intervals, Events = probe._events, Memory = probe._memory,
                SaveSlotSummaryMilliseconds = probe._slotTimes,
                SaveTimings = probe._saveTimes,
                Notes = "Scripted room teleports plus real W/S movement; real save/load in isolated QA folder. Explicit loading intervals retained separately. OS cache uncontrolled; CPU/host timings, not GPU timestamps."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", previousFrames);
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", previousReport);
            Console.WriteLine("[GameplaySoak] " + folder);
        }
        if (failure != null) throw new InvalidDataException("Gameplay soak failed; report retained: " + folder, failure);
    }

    public void Initialize(EngineContext context)
    {
        _game.Initialize(context);
        ((GameplayUiLayer)Field("_gameplayUi")).Dispose();
        var backend = RmlUiBackend.Probe(Path.Combine(AppContext.BaseDirectory, "Content/UI"),
            _native ? Path.Combine(AppContext.BaseDirectory, "HS2RmlUiBridge.dll") : "HS2_missing_soak_bridge");
        if (_native && !backend.HasNativeApi) throw new InvalidDataException("Native test bridge unavailable.");
        _ui = new(backend, _native) { Profile = new UiWorkProfile() };
        _ui.IconResolver = path => context.ImGui!.GetImage(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Content/UI/Runtime", path)));
        Set("_gameplayUi", _ui);
        _motor = (SourcePlayerMotor)Field("_motor");
        _spawn = _motor.Position;
        var inventory = (InventoryContainer)Field("_inventory");
        foreach (var (id, count) in new[] { (ItemCatalog.InkRibbon, 5), (ItemCatalog.MasterKey, 1), (ItemCatalog.Scrap, 78),
            (ItemCatalog.Gunpowder, 14), (ItemCatalog.RepairedCable, 1), (ItemCatalog.DamagedCable, 1), (ItemCatalog.SpareWire, 1) })
            Check(inventory.Add(id, count), "Inventory fixture did not fit.");
        Call("QueueLevelUiImages");
        // Prepare test-only serializers before timed frames. Never perform a warmup
        // SavePrototypeGame call: the first real durable save must stay in the sample.
        _ = JsonSerializer.Serialize(inventory.ToSaveData());
        _ = BrokenState();
        _ = JsonSerializer.Serialize(Call("CaptureDroppedItems"));
        _transitionStart = Stopwatch.GetTimestamp();
    }

    public void Update(float dt, InputSnapshot actualInput)
    {
        _ui.Profile!.BeginFrame();
        long stamp = Stopwatch.GetTimestamp();
        // An interval describes the preceding rendered frame, not this update.
        if (_frame > 120 && _previousStamp != 0)
            _intervals.Add(new(_frame - 1, _previousPhase, Stopwatch.GetElapsedTime(_previousStamp, stamp).TotalMilliseconds));
        _previousStamp = stamp;
        _phase = _loading ? "loading" : "gameplay";
        int elapsed = _frame - 120, local = elapsed < 0 ? -1 : elapsed % CycleFrames, cycle = Math.Max(0, elapsed / CycleFrames);
        var keys = new List<KeyEvent>();
        if (local is 20 or 60 or 120 or 180 or 240 or 300 or 360 or 480 or 540 or 780 or 1000 or 1100)
        {
            long start = Stopwatch.GetTimestamp();
            Action(local, cycle, keys);
            _events.Add(new { Frame = _frame, Cycle = cycle, Action = local, Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds });
            _actions++;
        }
        Key? next = _loading || elapsed < 0 ? null : (elapsed / 90) % 2 == 0 ? Key.W : Key.S;
        if (next != _held)
        {
            if (_held.HasValue) keys.Add(new(_held.Value, false, ModifierKeys.None));
            if (next.HasValue) keys.Add(new(next.Value, true, ModifierKeys.None));
            _held = next;
        }
        InputState.SetRelativeMouseDelta(_loading ? Vector2.Zero : new Vector2(MathF.Cos(elapsed * .02f) * dt * 45, 0));
        _game.Update(dt, new Snapshot(keys));
        _peakDebris = Math.Max(_peakDebris, ((ICollection)Field("_fractureDebrisPieces")).Count);
        Check(float.IsFinite(_motor.Position.X) && float.IsFinite(_motor.Position.Y) && float.IsFinite(_motor.Position.Z) && _motor.Position.Y > -5, "Player became invalid or fell through the level.");
        if (_loading && Stopwatch.GetElapsedTime(_transitionStart).TotalSeconds > 15) throw new InvalidDataException("Loading transition exceeded 15 seconds.");
    }

    private void Action(int local, int cycle, List<KeyEvent> keys)
    {
        var inventory = (InventoryContainer)Field("_inventory");
        var entities = (List<Entity>)Field("_runtimeEntities");
        switch (local)
        {
            case 20: Set("_flashlightOn", cycle % 2 == 0); break;
            case 60:
                var held = entities.Where(e => e.CanPickUp && !e.IsBroken && e.Physics.BoxBody != null && e.Render.Enabled)
                    .OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, _motor.Position)).First();
                Call("PickUp", held, true); Check(held.IsHeld, "Gravity hold failed."); break;
            case 120: Call("DropHeld"); Check(FieldOrNull("_held") == null, "Gravity release failed."); break;
            case 180:
                Check(inventory.Add(ItemCatalog.Fuse, 1), "Overflow fixture did not fit.");
                inventory.MoveStackToSlot(inventory.Stacks.Last(s => s.ItemId == ItemCatalog.Fuse).SlotIndex, inventory.PrimarySlotCapacity);
                Call("SetInventoryOpen", true); break;
            case 240:
                Call("SetInventoryOpen", false);
                Check(inventory.Stacks.All(s => s.SlotIndex < inventory.PrimarySlotCapacity), "Overflow did not return to the world."); break;
            case 300:
                var editor = (Engine.Editor.Editor.LevelEditorController)Field("_editor");
                ((HashSet<string>)Field("_solvedPuzzles")).UnionWith(editor.LevelFile.Entities.Where(e => e.Interaction != null).Select(e => e.Interaction!.StateId));
                break;
            case 360:
                foreach (var crate in entities.Where(e => e.Damageable && !e.IsBroken).OrderBy(e => Vector3.DistanceSquared(e.Transform.Position, _motor.Position)).Take(2).ToArray())
                    Call("ApplyEntityDamage", crate, 10000f, "shotgun", (Vector3?)crate.Transform.Position);
                break;
            case 480:
                var closed = (Dictionary<string, Vector3>)Field("_puzzleDoorClosedPositions");
                if (_puzzles) Check(closed.Count > 0 && entities.Any(e => closed.TryGetValue(e.Name, out var start) && Vector3.Distance(start, e.Transform.Position) > .1f), "Puzzle doors did not animate.");
                long saveStart = Stopwatch.GetTimestamp();
                Check((bool)Call("SavePrototypeGame", "SoakTest", 0)!, "Save failed.");
                double saveMs = Stopwatch.GetElapsedTime(saveStart).TotalMilliseconds;
                long checksStart = Stopwatch.GetTimestamp();
                _savedInventory = JsonSerializer.Serialize(inventory.ToSaveData());
                _savedBroken = BrokenState();
                _savedDrops = JsonSerializer.Serialize(Call("CaptureDroppedItems"));
                _savedHealth = (int)Field("_playerHealth");
                _saveTimes.Add(new { Frame = _frame, GameSaveMilliseconds = saveMs,
                    PersistenceMilliseconds = _ui.Profile!.Snapshot().GetValueOrDefault("save-persistence"),
                    AssertionSnapshotMilliseconds = Stopwatch.GetElapsedTime(checksStart).TotalMilliseconds });
                break;
            case 540:
                inventory.Clear(); Set("_playerHealth", 12);
                BeginTransition();
                Check((bool)typeof(HL2GameModule).GetMethod("TryLoadPrototypeSave", Flags, [typeof(string)])!.Invoke(_game, [Path.Combine(_folder, "Saves/slot_1.json")])!, "Load failed.");
                Check(JsonSerializer.Serialize(inventory.ToSaveData()) == _savedInventory && (int)Field("_playerHealth") == _savedHealth, "Inventory/health did not survive save/load.");
                Check(JsonSerializer.Serialize(Call("CaptureDroppedItems")) == _savedDrops, "Dropped item state did not survive load.");
                break;
            case 780:
                BeginTransition(); keys.Add(new(Key.F5, true, ModifierKeys.None)); keys.Add(new(Key.F5, false, ModifierKeys.None)); break;
            case 1000:
                Check(JsonSerializer.Serialize(inventory.ToSaveData()) == _savedInventory, "Inventory changed after level reload.");
                Check(BrokenState() == _savedBroken, "Broken crate state changed after reload.");
                Check(entities.Where(e => e.Render.Enabled).Select(e => e.Id).Distinct().Count() == entities.Count(e => e.Render.Enabled), "Reload duplicated live entity IDs.");
                _motor.Position = _puzzles ? _spawn : RenderStressTest.Center((cycle * 3 + 3) % 12) + new Vector3(0, .1f, -5);
                _motor.Velocity = Vector3.Zero;
                break;
            case 1100:
                Check(((ICollection)Field("_fractureDebrisPieces")).Count == 0, "Debris did not expire.");
                using (var process = Process.GetCurrentProcess())
                {
                    process.Refresh(); long bytes = process.PrivateMemorySize64;
                    if (_completedCycles == 0) { _firstSettledPrivate = bytes; _firstSettledModels = _game.StreamingResidentBytes; }
                    _lastSettledPrivate = bytes; _lastSettledModels = _game.StreamingResidentBytes;
                    _memory.Add(new { Frame = _frame, PrivateBytes = bytes, WorkingSet = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false),
                        HeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes, ModelBytes = _game.StreamingResidentBytes, Models = _game.StreamingResidentModels,
                        RuntimeEntities = entities.Count, Collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) } });
                }
                _completedCycles++; break;
        }
    }

    private void BeginTransition() { _loading = true; _phase = "loading"; _transitionStart = Stopwatch.GetTimestamp(); _transitions++; }
    public void FixedUpdate(float dt)
    {
        Vector3 before = _motor.Position; _game.FixedUpdate(dt);
        if (_frame >= 120 && !_loading)
        {
            _simulation += dt;
            Vector3 delta = _motor.Position - before;
            _distance += new Vector2(delta.X, delta.Z).Length();
        }
    }
    public void DrawImGui() => _game.DrawImGui();
    public void RenderWorld(Renderer renderer) => _game.RenderWorld(renderer);
    public void RenderOverlay(Renderer renderer)
    {
        _game.RenderOverlay(renderer);
        if (_native && _frame >= 120) Check(_ui.UsesNativePresentation, "Native UI fell back.");
        if (_loading)
        {
            var readiness = _game.GetLoadReadiness();
            Check(readiness.FailedModels == 0, "Required model failed.");
            if (readiness.AssetsReady && readiness.LightingReady && readiness.InputReady)
            {
                _loading = false;
                _events.Add(new { Frame = _frame, ReadyMilliseconds = Stopwatch.GetElapsedTime(_transitionStart).TotalMilliseconds });
            }
        }
        _slotTimes.Add(_ui.Profile!.Snapshot().GetValueOrDefault("save-slot-summaries"));
        _previousPhase = _phase;
        _frame++;
    }
    private void Validate(int cycles)
    {
        Check(_frame == 120 + cycles * CycleFrames && _completedCycles == cycles && _transitions == cycles * 2 && _actions == cycles * 12, "Soak was incomplete.");
        Check(_intervals.Count == cycles * CycleFrames - 1 && _intervals[0].Frame == 120, "Wall intervals included warmup or missed an intermediate frame.");
        Check(_distance > 1 && _simulation > 1 && _peakDebris > 0, "Gameplay simulation was not exercised.");
        Check(!_loading, "Final reload was not ready.");
        var steady = _intervals.Where(i => i.Phase == "gameplay").Select(i => i.Milliseconds).Order().ToArray();
        Check(steady.Length > cycles * 800, "Too little steady gameplay measured.");
        Console.WriteLine($"[SoakFrames] {steady.Length} gameplay intervals; mean {steady.Average():F3}ms, P95 {steady[(int)((steady.Length - 1) * .95)]:F3}ms, >33.3 {steady.Count(x => x > 33.3)}, >100 {steady.Count(x => x > 100)}. Loading intervals are retained separately.");
        Check(steady.Average() <= 17 && steady[(int)((steady.Length - 1) * .95)] <= 21 && !steady.Any(x => x > 100), "Soak gameplay frame budget failed.");
        foreach (var window in _intervals.Where(i => i.Phase == "gameplay").Chunk(300))
            Check(window.Count(i => i.Milliseconds > 33.3) <= 1, "More than one hitch in a 300-gameplay-frame window.");
        Check(_lastSettledPrivate - _firstSettledPrivate <= 128L * 1024 * 1024 && _lastSettledModels - _firstSettledModels <= 8L * 1024 * 1024,
            "Settled memory growth exceeded the diagnostic guard (128 MiB process / 8 MiB model payload).");
        Console.WriteLine($"PASS: {cycles} cycles, {_actions} actions, private growth {(_lastSettledPrivate - _firstSettledPrivate) / 1048576.0:F2} MiB. This is not proof of leak freedom.");
    }
    public void Dispose() => _game.Dispose();
    private string BrokenState() => JsonSerializer.Serialize(((Dictionary<string, string>)Field("_brokenObjectReplacementModels")).OrderBy(p => p.Key));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private object Field(string name) => FieldOrNull(name)!;
    private object? FieldOrNull(string name) => typeof(HL2GameModule).GetField(name, Flags)!.GetValue(_game);
    private void Set(string name, object value) => typeof(HL2GameModule).GetField(name, Flags)!.SetValue(_game, value);
    private object? Call(string name, params object?[] args) => typeof(HL2GameModule).GetMethod(name, Flags)!.Invoke(_game, args);
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

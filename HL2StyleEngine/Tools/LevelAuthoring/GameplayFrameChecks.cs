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
    private bool _residencyRoute;
    private bool _lightingSwitches;
    private bool _referenceRooms;
    private bool _referenceSubmission, _renderStress;
    private long _hardwareDraws, _materialBindings, _instances, _renderAllocations;
    private double _renderCpu, _prepareCpu;
    private readonly Stopwatch _startup = new();
    private double _resourcesReadyMs = -1;
    private int _switchActions;
    private readonly bool _probeTest, _noProbes;
    private double _probeMilliseconds, _maxProbeMilliseconds;
    private readonly List<double> _intervals = new();
    private SourcePlayerMotor _motor = null!;
    private BasicWorldRenderer _world = null!;
    private int _frames, _ticks;
    private long _objectUploads;
    private long _draws;
    private long _roomCulled;
    private double _roomCpu;
    private double _shadowCpuMilliseconds;
    private long _shadowDraws, _shadowPasses;
    private int _streamWaitFrames, _stressActions, _peakDebris;
    private double _time, _simulationTime, _distance;
    private Key? _held;
    private long _previousStamp;
    public InputState InputState => _game.InputState;
    private GameplayFrameChecks(string level, bool torch, bool individualUploads, bool referenceVisibility, bool stress, bool noProbes)
    { _game = new(level); _torch = torch; _individualUploads = individualUploads; _referenceVisibility = referenceVisibility; _stress = stress;
        _probeTest=Path.GetFileName(level)==ProbeLightingRoom.FileName; _noProbes=noProbes; }

    internal static void Run(string root, bool torch, bool individualUploads = false, bool referenceVisibility = false, bool stress = false,
        string? levelFile = null, bool noProbes = false, bool residencyRoute = false, bool lightingSwitches = false, bool referenceRooms = false,
        bool referenceSubmission = false)
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
            using var probe = new GameplayFrameChecks(Path.Combine(root, "Game/Content/Levels", levelFile ?? SixRoomFlashlightTest.FileName), torch, individualUploads, referenceVisibility, stress, noProbes);
            probe._residencyRoute = residencyRoute;
            probe._lightingSwitches = lightingSwitches;
            probe._referenceRooms = referenceRooms;
            probe._referenceSubmission = referenceSubmission;
            probe._renderStress = levelFile == RenderStressTest.FileName;
            if (residencyRoute) Console.WriteLine("[ResidencyRoute] Five test-only teleports during measured frames exercise loading/eviction with Update, physics and presentation active; not a walking-only benchmark.");
            Console.WriteLine($"[GameplayLevel] {levelFile ?? SixRoomFlashlightTest.FileName}");
            if(probe._probeTest) Console.WriteLine($"[ProbePreview] {(noProbes ? "disabled reference" : "enabled")}; static lightmap remains enabled.");
            Console.WriteLine($"[GameplayProfile] torch={(torch ? "on" : "off")}; uploads={(individualUploads ? "individual reference" : "batched")}; actual host loop, scripted W/S and relative mouse, production HUD and launcher UI settings, physics and VSync/presentation; 120 warmup + 300 measured frames.");
            host.Run(probe);
            if (probe._intervals.Count != 300 || probe._distance < .1 || probe._ticks == 0)
                throw new InvalidDataException("Gameplay profile did not complete with real player movement.");
            probe._intervals.Sort();
            Console.WriteLine($"[GameplayMovement] distance {probe._distance:F3}m / simulated {probe._simulationTime:F3}s = {probe._distance / probe._simulationTime:F3}m/s including acceleration/reversals; {probe._ticks} fixed ticks; wall frame mean {probe._intervals.Average():F3}ms p95 {probe._intervals[284]:F3}ms; >16.7ms {probe._intervals.Count(x => x > 16.7)}, >33.3ms {probe._intervals.Count(x => x > 33.3)} / 300.");
            Console.WriteLine($"[GameplayUploads] {probe._objectUploads / 300.0:F1} main-scene uploads/frame; {probe._intervals.Count(x => x > 100)} frames over the 100ms simulation clamp.");
            Console.WriteLine($"[ShadowWork] CPU {probe._shadowCpuMilliseconds/300:F3}ms/frame; {probe._shadowPasses/300.0:F1} rendered faces; {probe._shadowDraws/300.0:F1} draws/frame.");
            Console.WriteLine($"[VisibilityStreaming] reference={referenceVisibility}; draws/frame {probe._draws / 300.0:F1}; streaming wait frames {probe._streamWaitFrames}; resident {probe._game.StreamingResidentModels} models / {probe._game.StreamingResidentBytes / 1048576.0:F1} MiB; {probe._game.StreamingZoneCount} dependency zones; {probe._game.StreamingEvictions} evictions.");
            Console.WriteLine($"[RoomVisibility] {probe._game.RoomVisibilityStatus}; {probe._roomCulled/300.0:F1} skipped entities/frame; {probe._roomCpu/300:F3}ms CPU.");
            Console.WriteLine($"[Submission] reference={referenceSubmission}; hardware draws {probe._hardwareDraws/300.0:F1}; material binds {probe._materialBindings/300.0:F1}; instanced objects {probe._instances/300.0:F1}; render CPU {probe._renderCpu/300:F3}ms; preparation {probe._prepareCpu/300:F3}ms; render-thread allocation {probe._renderAllocations/300.0:F0} bytes/frame; instance buffer {probe._world.InstanceBufferBytes} bytes.");
            if (levelFile is RoomVisibilityChecks.FileName or RenderStressTest.FileName)
                Console.WriteLine($"[RoomReadiness] resources ready {probe._resourcesReadyMs:F1}ms from module initialization; room-only reference={referenceRooms}; warm OS/cooked caches, not cold first-playable.");
            if (probe._renderStress)
            {
                var room=(ReflectionProbeSession)probe.Field("_roomReflections");
                Console.WriteLine($"[TwelveRoomResidency] {room.Status}; {room.ResidentProbeCount} resident probes / {room.ResidentBytes} bytes; capture {room.CaptureGpuBytes}; points {probe._world.ShadowPointLightCount}, spots {probe._world.ShadowSpotLightCount}.");
                if(!room.Ready || room.Busy || room.CaptureGpuBytes!=0 || room.ResidentProbeCount is <1 or >6 || room.ResidentBytes>6.5*1048576 ||
                    probe._world.ShadowPointLightCount!=12 || probe._world.ShadowSpotLightCount!=(torch?1:0) ||
                    probe._intervals.Average()>17 || probe._intervals[284]>21 || probe._intervals.Count(x=>x>33.3)>1 ||
                    probe._intervals.Any(x=>x>100) || probe._streamWaitFrames!=0)
                    throw new InvalidDataException("Twelve-room residency/performance gate failed.");
                Console.WriteLine($"[TwelveRoomBudget] PASS: mean <=17ms, P95 <=21ms, <=1 frame over 33.3ms, none over 100ms or waiting; reflections {room.ResidentBytes} bytes; shadows {probe._world.ShadowGpuBytes} bytes.");
            }
            if (levelFile is PbrTestRoom.FileName or RoomReflectionChecks.FileName or RoomTransitionChecks.FileName or ReflectionStreamingChecks.FileName or PointLightOnlyChecks.FileName or LightingStabilityChecks.FileName or ShadowStabilityChecks.FileName or RoomVisibilityChecks.FileName)
            {
                if (!probe._world.ReflectionReady || probe._world.ReflectionPendingCpuBytes != 0)
                    throw new InvalidDataException("PBR gameplay did not finish loading its reflection environment.");
                long reflectionBytes=probe._world.ReflectionResidentBytes;
                if (levelFile is RoomReflectionChecks.FileName or RoomTransitionChecks.FileName or ReflectionStreamingChecks.FileName or PointLightOnlyChecks.FileName or LightingStabilityChecks.FileName or ShadowStabilityChecks.FileName or RoomVisibilityChecks.FileName)
                {
                    var room = (ReflectionProbeSession)probe.Field("_roomReflections");
                    if (!room.Ready || room.Busy || room.CaptureGpuBytes != 0) throw new InvalidDataException("Room capture not ready, or runtime capture work is active.");
                    if(probe._world.Reflection==null)reflectionBytes+=room.ResidentBytes;
                    Console.WriteLine($"[RoomReflection] {room.Status}; {room.ResidentBytes} GPU bytes; no runtime capture passes.");
                    if (levelFile is ReflectionStreamingChecks.FileName or PointLightOnlyChecks.FileName &&
                        (room.ResidentProbeCount + room.RetiringProbeCount > 3 || room.ResidentBytes > 3.3*1048576))
                        throw new InvalidDataException("Reflection residency budget exceeded.");
                    if (levelFile==LightingStabilityChecks.FileName && (room.ResidentProbeCount!=6 || room.ResidentBytes>(lightingSwitches?13:6.5)*1048576))
                        throw new InvalidDataException("All-resident six-room budget exceeded.");
                    if(levelFile is ShadowStabilityChecks.FileName or RoomVisibilityChecks.FileName)
                    {
                        if(room.ResidentProbeCount!=6 || !room.AlternateStateReady || room.ResidentBytes>13*1048576 || room.PendingCpuBytes!=0 ||
                            probe._world.ShadowPointLightCount!=14 || probe._world.ShadowSpotLightCount!=(torch?1:0) || probe._world.ShadowGpuBytes!=104L*1048576)
                            throw new InvalidDataException("Shadow/prewarm residency budget or slot count violated.");
                        Console.WriteLine($"[ShadowPool] {probe._world.ShadowPointLightCount} point + {probe._world.ShadowSpotLightCount} spot; {probe._world.ShadowGpuBytes/1048576} MiB atlas; alternate reflections ready.");
                    }
                    if (residencyRoute && levelFile is not (LightingStabilityChecks.FileName or ShadowStabilityChecks.FileName or RoomVisibilityChecks.FileName) && (room.ProbeEvictions == 0 || room.ProbeLoads < 6))
                        throw new InvalidDataException("Gameplay route did not exercise room streaming.");
                }
                Console.WriteLine($"[ReflectionResidency] {reflectionBytes} GPU payload bytes including retained room captures, no pending CPU payload.");
                if (probe._intervals.Average() > 17 || probe._intervals[284] > 21 ||
                    probe._intervals.Count(x=>x>33.3)>1 || probe._intervals.Any(x=>x>100) || probe._streamWaitFrames != 0)
                    throw new InvalidDataException("PBR full-host regression budget exceeded.");
                Console.WriteLine("[PbrGameplayBudget] PASS: recorded mean/P95/hitch/streaming-wait limits.");
            }
            if (((LevelEditorController)probe.Field("_editor")).LevelFile.Bake.Enabled)
            {
                var bake = (LevelBakeSession)probe.Field("_bakedLighting");
                if (!bake.Ready || bake.ReadyModels == 0) throw new InvalidDataException("Gameplay profile did not load its bake.");
                Console.WriteLine($"[IndirectLighting] {bake.ReadyModels} ready model bindings; {bake.ResidentBytes/1048576.0:F2} MiB additional bake GPU resources.");
                Console.WriteLine($"[LightProbes] {bake.Probes?.Valid.Count(v=>v) ?? 0} valid probes; sample CPU mean {probe._probeMilliseconds/300:F4} ms, max {probe._maxProbeMilliseconds:F4} ms.");
                if(probe._probeTest && bake.Probes == null)throw new InvalidDataException("Probe profile did not load its probe volume.");
            }
            if (stress)
            {
                if (probe._stressActions != 5 || probe._peakDebris == 0) throw new InvalidDataException("Stress actions did not exercise debris/doors/gravity holding/inventory.");
                Console.WriteLine($"[GameplayStress] PASS: {(probe._probeTest ? "toggled realtime light" : "opened puzzle shutters")}, broke six crates, gravity-held/released a prop, opened/closed inventory; {probe._peakDebris} peak fragment bodies; {probe._stressActions} event groups.");
            }
            if (lightingSwitches)
            {
                if (probe._switchActions != 4 || !((ReflectionProbeSession)probe.Field("_roomReflections")).ScenarioStatus.EndsWith("on",StringComparison.Ordinal))
                    throw new InvalidDataException("Lighting switch profile did not restore its starting state.");
                Console.WriteLine("[LightingSwitches] PASS: four off/on changes during presented gameplay; opt-in prewarm readiness checked before each switch.");
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
        _startup.Start();
        _game.Initialize(context);
        ((LevelBakeSession)Field("_bakedLighting")).PreviewProbes = !_noProbes;
        _motor = (SourcePlayerMotor)typeof(HL2GameModule).GetField("_motor", Flags)!.GetValue(_game)!;
        _world = (BasicWorldRenderer)typeof(HL2GameModule).GetField("_world", Flags)!.GetValue(_game)!;
        _world.MeasureShadowCpuTime=true;
        _world.ObjectBatchUploadsEnabled = !_individualUploads;
        _world.ViewCullingEnabled = !_referenceVisibility;
        _game.AssetStreamingEnabled = !_referenceVisibility;
        _game.RoomVisibilityEnabled = !_referenceVisibility && !_referenceRooms;
        _game.RenderPreparationEnabled = !_referenceSubmission;
        _world.DrawGroupingEnabled = !_referenceSubmission;
        typeof(HL2GameModule).GetField("_flashlightOn", Flags)!.SetValue(_game, _torch);
    }

    public void Update(float dt, InputSnapshot actualInput)
    {
        long stamp = Stopwatch.GetTimestamp();
        if (_frames >= 120 && _previousStamp != 0) _intervals.Add(Stopwatch.GetElapsedTime(_previousStamp, stamp).TotalMilliseconds);
        _previousStamp = stamp;
        if (_residencyRoute && _frames >= 150 && (_frames - 150) % 50 == 0 && _frames <= 350)
        {
            Vector3[] stops = _renderStress
                ? [RenderStressTest.Center(3)+new Vector3(0,.1f,-5),RenderStressTest.Center(11)+new Vector3(0,.1f,-5),
                   RenderStressTest.Center(8)+new Vector3(0,.1f,-5),RenderStressTest.Center(6)+new Vector3(0,.1f,-5),new(0,.1f,-5)]
                : [new(-24,.1f,-11),new(24,.1f,11),new(0,.1f,26),new(-24,.1f,11),new(-24,.1f,-11)];
            _motor.Position = stops[(_frames - 150) / 50]; _motor.Velocity = Vector3.Zero;
        }
        if (_stress) StressActions();
        if (_lightingSwitches && _frames is 160 or 230 or 300 or 370)
        {
            if(((LevelEditorController)Field("_editor")).LevelFile.PrewarmLightingStates &&
                !((ReflectionProbeSession)Field("_roomReflections")).AlternateStateReady)
                throw new InvalidDataException("Lighting switch occurred before the alternate reflection bank was ready.");
            var lever=((List<Entity>)Field("_runtimeEntities")).Single(e=>e.Name=="Fixture Power Switch");
            Call("UseLightSwitch",lever); _switchActions++;
        }
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
        long stamp=Stopwatch.GetTimestamp(), allocated=GC.GetAllocatedBytesForCurrentThread();
        _game.RenderWorld(renderer);
        double cpu=Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        if (_resourcesReadyMs < 0 && _game.VisibilityGraph is { RoomCount: > 0 } && !_game.StreamingWaiting &&
            Field("_roomReflections") is ReflectionProbeSession { Ready: true } ready && (_renderStress || ready.AlternateStateReady) &&
            (_renderStress || Field("_bakedLighting") is LevelBakeSession { Ready: true, ReadyModels: > 0 }))
            _resourcesReadyMs = _startup.Elapsed.TotalMilliseconds;
        if (_frames >= 120)
        {
            var bake=(LevelBakeSession)Field("_bakedLighting");
            _probeMilliseconds+=bake.ProbeMilliseconds;_maxProbeMilliseconds=Math.Max(_maxProbeMilliseconds,bake.ProbeMilliseconds);
            _objectUploads += _world.ObjectUploadCount;
            _draws += _world.ObjectDrawCount;
            _hardwareDraws+=_world.MainDrawCalls;_materialBindings+=_world.MaterialBindings;_instances+=_world.InstancedObjects;
            _renderCpu+=cpu;_prepareCpu+=_game.RenderPreparationEnabled?_game.RenderPreparationMilliseconds:0;_renderAllocations+=bytes;
            _roomCulled += _game.RoomCulledEntities; _roomCpu += _game.RoomVisibilityMilliseconds;
            _shadowCpuMilliseconds+=_world.ShadowCpuMilliseconds;
            _shadowDraws+=_world.ShadowDrawCount;_shadowPasses+=_world.ShadowRenderedPassCount;
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
            if(_probeTest)
            {
                var camera=(FpsCamera)Field("_camera");float yaw=camera.Yaw,pitch=camera.Pitch;
                Vector3 direction=Vector3.Normalize((Vector3)editor.LevelFile.Entities.Single(e=>e.Name=="RealtimeLightSwitch").LocalPosition-camera.Position);
                camera.Yaw=MathF.Atan2(direction.X,direction.Z);camera.Pitch=MathF.Asin(direction.Y);
                bool used=(bool)Call("TryUseFocusedInteractable")!;camera.Yaw=yaw;camera.Pitch=pitch;
                if(!used)throw new InvalidDataException("Probe stress switch was unreachable.");
            }
            else ((HashSet<string>)Field("_solvedPuzzles")).UnionWith(editor.LevelFile.Entities.Where(e => e.Interaction != null).Select(e => e.Interaction!.StateId));
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

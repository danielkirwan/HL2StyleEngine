using System.Diagnostics;
using System.Text.Json;

namespace Engine.Runtime.Hosting;

// Opt-in, bounded captures for comparing the same scene before and after a change.
internal sealed class FrameProfile
{
    private static readonly string[] Stages = { "Update", "Physics", "UI", "World", "Overlay", "Submit/Present" };
    private readonly List<double[]> _samples = new();
    private readonly int _target;
    private int _warmup = 120;
    private long _start;
    private int _stage;
    private double[] _current = new double[Stages.Length];
    private readonly string? _report = Environment.GetEnvironmentVariable("HS2_FRAME_REPORT");
    private readonly List<object> _trace = new();
    private long _inputStart, _allocated, _stageAllocated;
    private long _hostStart;
    private readonly int _nativeThreadId = ThreadCpuClock.NativeThreadId;
    private double _inputMilliseconds;
    private long[] _allocations = new long[Stages.Length];
    private readonly double?[] _cpuMilliseconds = new double?[Stages.Length];
    private readonly ulong?[] _cpuCycles = new ulong?[Stages.Length];
    private ThreadCpuClock.Sample? _frameCpuStart, _stageCpuStart, _cpuEnd;
    private int _gen0, _gen1, _gen2;
    private TimeSpan _pause;
    private int _frame;
    public int Frame => _frame;
    public int TotalFrames => _target + 120;

    public static FrameProfile? FromEnvironment()
        => int.TryParse(Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES"), out int frames) && frames > 0
            ? new FrameProfile(Math.Clamp(frames, 1, 36000)) : null;

    private FrameProfile(int target)
    {
        _target = target;
        Engine.Physics.Collision.MeshCollisionMesh.CollectStatistics = true;
        Engine.Physics.Collision.MeshCollisionMesh.QueryCount = 0;
        Engine.Physics.Collision.MeshCollisionMesh.BoundsTests = 0;
        Engine.Physics.Collision.MeshCollisionMesh.CandidateTriangles = 0;
    }

    public void BeginInput()
    {
        _inputStart = Stopwatch.GetTimestamp();
        if (_report == null) return;
        _frameCpuStart = ThreadCpuClock.Read();
        _allocated = GC.GetAllocatedBytesForCurrentThread();
        _gen0 = GC.CollectionCount(0); _gen1 = GC.CollectionCount(1); _gen2 = GC.CollectionCount(2);
        _pause = GC.GetTotalPauseDuration();
    }

    public void Begin()
    {
        _start = Stopwatch.GetTimestamp();
        _hostStart = _start;
        _inputMilliseconds = Stopwatch.GetElapsedTime(_inputStart, _start).TotalMilliseconds;
        if (_report != null)
        {
            _stageAllocated = GC.GetAllocatedBytesForCurrentThread();
            _stageCpuStart = ThreadCpuClock.Read();
        }
        _stage = 0;
    }

    public void Mark()
    {
        long now = Stopwatch.GetTimestamp();
        _current[_stage] = Stopwatch.GetElapsedTime(_start, now).TotalMilliseconds;
        if (_report != null)
        {
            _cpuEnd = ThreadCpuClock.Read();
            var cpu = ThreadCpuClock.Elapsed(_stageCpuStart, _cpuEnd);
            _cpuMilliseconds[_stage] = cpu?.Milliseconds;
            _cpuCycles[_stage] = cpu?.Cycles;
            _stageCpuStart = _cpuEnd;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            _allocations[_stage] = allocated - _stageAllocated;
            _stageAllocated = allocated;
        }
        _stage++;
        _start = now;
    }

    public bool Finish(int width, int height, Engine.Render.Renderer renderer, Engine.Physics.PhysicsCpuProfile physics,
        double rawSeconds, float simulationSeconds, int fixedSteps, float accumulatorSeconds, bool graphicsDebug, bool vsync, bool windowFocused)
    {
        if (_report != null)
        {
            _trace.Add(new
            {
                Frame = _frame, Warmup = _warmup > 0, InputMilliseconds = _inputMilliseconds,
                InputTimestampQpc = _inputStart, HostTimestampQpc = _hostStart, EndTimestampQpc = _start,
                Milliseconds = _current.ToArray(), AllocatedByStage = _allocations.ToArray(),
                ThreadCpuMilliseconds = _cpuMilliseconds.ToArray(), ThreadCycles = _cpuCycles.ToArray(),
                MainThreadCpuMilliseconds = ThreadCpuClock.Elapsed(_frameCpuStart, _cpuEnd)?.Milliseconds,
                RenderMilliseconds = renderer.CpuProfile.Snapshot(),
                PhysicsMilliseconds = physics.Snapshot(), PhysicsCalls = physics.Calls(),
                PreviousFrameIntervalMilliseconds = rawSeconds * 1000, SimulationDeltaMilliseconds = simulationSeconds * 1000,
                FixedSteps = fixedSteps, FixedRemainderMilliseconds = accumulatorSeconds * 1000,
                WindowFocused = windowFocused,
                MainThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocated,
                Collections = new[] { GC.CollectionCount(0) - _gen0, GC.CollectionCount(1) - _gen1, GC.CollectionCount(2) - _gen2 },
                GcPauseMilliseconds = (GC.GetTotalPauseDuration() - _pause).TotalMilliseconds
            });
        }
        _frame++;
        if (_warmup-- > 0) return false;
        _samples.Add(_current);
        _current = new double[Stages.Length];
        if (_samples.Count < _target) return false;
        double[] totals = _samples.Select(s => s.Sum()).Order().ToArray();
        double average = totals.Average();
        Console.WriteLine($"[FrameProfile] {width}x{height}, {_target} frames after 120 warmup; " +
            $"mean {average:F2} ms ({1000 / average:F1} FPS), p95 {totals[(int)((totals.Length - 1) * .95)]:F2} ms");
        for (int i = 0; i < Stages.Length; i++)
            Console.WriteLine($"[FrameProfile] {Stages[i]}: {_samples.Average(s => s[i]):F2} ms");
        Console.WriteLine($"[MeshPhysics] queries {Engine.Physics.Collision.MeshCollisionMesh.QueryCount}, " +
            $"bounds tests {Engine.Physics.Collision.MeshCollisionMesh.BoundsTests}, candidates {Engine.Physics.Collision.MeshCollisionMesh.CandidateTriangles}");
        Console.WriteLine($"[Textures] cooked cache hits {Engine.Render.TextureCooker.CacheHits}, fallback decodes {Engine.Render.TextureCooker.FallbackDecodes}");
        Console.WriteLine($"[Models] cooked cache hits {Engine.Render.CookedModelCache.CacheHits}, source parses {Engine.Render.CookedModelCache.FallbackLoads}");
        Engine.Physics.Collision.MeshCollisionMesh.CollectStatistics = false;
        if (_report != null)
        {
            renderer.GpuProfile?.Poll(_frame);
            string path = Path.GetFullPath(_report);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Version = 5, Width = width, Height = height, WarmupFrames = 120, Samples = _target,
                ProcessId = Environment.ProcessId, NativeThreadId = _nativeThreadId, QpcFrequency = Stopwatch.Frequency,
                GraphicsDebugRequested = graphicsDebug, VSync = vsync, GpuDeviceFlags = renderer.GpuProfile?.DeviceFlags,
                Stages, RenderStages = Engine.Render.RenderCpuProfile.Stages, PhysicsStages = Engine.Physics.PhysicsCpuProfile.Stages,
                Frames = _trace, GpuFrames = renderer.GpuProfile?.Samples,
                GpuPending = renderer.GpuProfile?.Pending, GpuSkipped = renderer.GpuProfile?.Skipped,
                GpuScope = "D3D11 submitted command list only, excluding earlier immediate uploads and Present. Nonblocking/disjoint-checked; join by Frame, not result arrival.",
                CpuScope = "Current-thread kernel+user time (quantized) and raw cycles at host stage boundaries. Null if unavailable. Cycles are not milliseconds; wall-minus-CPU is not an exact scheduler-wait measurement."
            }));
            Console.WriteLine("[FrameTrace] " + path);
        }
        return true;
    }
}

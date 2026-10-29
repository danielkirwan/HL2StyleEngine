using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Engine.Core.Diagnostics;

// Opt-in launch telemetry. Worker totals overlap and must not be summed into wall time.
public sealed class LoadProfile
{
    private sealed class Stage { public long Ticks, Calls, Bytes; }
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly double _entryDelay = Math.Max(0, (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds);
    private readonly ConcurrentDictionary<string, Stage> _stages = new();
    private readonly Dictionary<string, double> _milestones = new();
    private readonly List<object> _frames = new();
    private static readonly string[] FrameStages = ["startup-update", "startup-physics", "startup-imgui", "startup-world", "startup-overlay", "startup-submit-present"];
    private readonly long[] _lastFrameTicks = new long[FrameStages.Length];
    private int _frameCount, _clampedFrames;
    private double _clampedMilliseconds;
    private readonly string _output;
    public bool Complete { get; private set; }
    public bool Succeeded { get; private set; }
    public bool ExitWhenComplete { get; }
    public double ElapsedMilliseconds => _entryDelay + Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

    public LoadProfile(string output, bool exitWhenComplete = false)
    {
        _output = Path.GetFullPath(output);
        ExitWhenComplete = exitWhenComplete;
        Mark("managed-entry");
    }

    public IDisposable? Measure(string name) => Complete ? null : new Scope(this, name);
    public void Record(string name, long ticks, long bytes = 0)
    {
        if (Complete) return;
        var stage = _stages.GetOrAdd(name, _ => new());
        Interlocked.Add(ref stage.Ticks, ticks);
        Interlocked.Add(ref stage.Bytes, bytes);
        Interlocked.Increment(ref stage.Calls);
    }
    public void Mark(string name) { if (!Complete) _milestones.TryAdd(name, ElapsedMilliseconds); }

    public void ObserveFrame(double rawSeconds, float simulationSeconds, bool assets, bool lighting, bool input)
    {
        if (Complete) return;
        _frameCount++;
        double discarded = Math.Max(0, rawSeconds - simulationSeconds) * 1000;
        if (discarded > .01) { _clampedFrames++; _clampedMilliseconds += discarded; }
        if (assets && lighting) Mark("required-resources-ready");
        var frameStages = new Dictionary<string, double>();
        for (int i = 0; i < FrameStages.Length; i++)
        {
            long ticks = _stages.TryGetValue(FrameStages[i], out var stage) ? Interlocked.Read(ref stage.Ticks) : 0;
            frameStages[FrameStages[i]] = (ticks - _lastFrameTicks[i]) * 1000.0 / Stopwatch.Frequency;
            _lastFrameTicks[i] = ticks;
        }
        if (_frames.Count < 256) _frames.Add(new
        {
            Frame = _frameCount, ProcessMilliseconds = ElapsedMilliseconds,
            RawMilliseconds = rawSeconds * 1000, SimulationMilliseconds = simulationSeconds * 1000,
            AssetsReady = assets, LightingReady = lighting, InputReady = input, HostMilliseconds = frameStages
        });
    }

    public void Finish(string outcome, object? detail)
    {
        if (Complete) return;
        Mark(outcome);
        Complete = true;
        Succeeded = outcome == "first-playable-presented";
        double elapsed = ElapsedMilliseconds;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
            File.WriteAllText(_output, JsonSerializer.Serialize(new
            {
                Version = 1, Outcome = outcome, ProcessMilliseconds = elapsed,
                CacheCondition = Environment.GetEnvironmentVariable("HS2_LOAD_CACHE_LABEL") ?? "Fresh process; OS file cache uncontrolled",
                Milestones = _milestones,
                StartupFrames = _frameCount, ClampedFrames = _clampedFrames,
                ClampedAwayMilliseconds = _clampedMilliseconds, Frames = _frames,
                Stages = _stages.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => new
                { Milliseconds = p.Value.Ticks * 1000.0 / Stopwatch.Frequency, p.Value.Calls, p.Value.Bytes }),
                Detail = detail
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[Loading] {outcome}: {elapsed:F1} ms; {_output}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Succeeded = false; Console.Error.WriteLine("[Loading] Report write failed: " + ex.Message); }
    }

    private sealed class Scope(LoadProfile owner, string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly TimeSpan _jitStart = name == "startup-physics"
            ? System.Runtime.JitInfo.GetCompilationTime(currentThread: true) : default;
        public void Dispose()
        {
            owner.Record(name, Stopwatch.GetTimestamp() - _start);
            if (name == "startup-physics")
            {
                TimeSpan jit = System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - _jitStart;
                owner.Record("startup-physics-main-thread-jit", (long)(jit.TotalSeconds * Stopwatch.Frequency));
            }
        }
    }
}

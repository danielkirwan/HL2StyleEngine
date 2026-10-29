using System.Diagnostics;

namespace Engine.UI;

// Explicitly attached by diagnostic hosts; ordinary gameplay does not collect samples.
public sealed class UiWorkProfile
{
    private readonly Dictionary<string, double> _milliseconds = new();
    public void BeginFrame() => _milliseconds.Clear();
    public Dictionary<string, double> Snapshot() => new(_milliseconds);
    public Scope Measure(string stage) => new(this, stage);

    public readonly struct Scope(UiWorkProfile owner, string stage) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose()
        {
            owner._milliseconds.TryGetValue(stage, out double elapsed);
            owner._milliseconds[stage] = elapsed + Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        }
    }
}

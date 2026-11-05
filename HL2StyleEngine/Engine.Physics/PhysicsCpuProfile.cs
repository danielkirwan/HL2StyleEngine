using System.Diagnostics;

namespace Engine.Physics;

public enum PhysicsWork { Colliders, Bodies, Support, Rotation, Pairs, Player, Held, Platforms, Push }

// Support/Rotation can be nested inside Bodies; timings must not be summed as independent work.
public sealed class PhysicsCpuProfile
{
    private readonly double[] _milliseconds = new double[Enum.GetValues<PhysicsWork>().Length];
    private readonly int[] _calls = new int[Enum.GetValues<PhysicsWork>().Length];
    public static string[] Stages { get; } = Enum.GetNames<PhysicsWork>();
    public bool Enabled { get; set; }
    public void Reset() { if (Enabled) { Array.Clear(_milliseconds); Array.Clear(_calls); } }
    public double[] Snapshot() => _milliseconds.ToArray();
    public int[] Calls() => _calls.ToArray();
    public Scope Measure(PhysicsWork stage) => Enabled ? new(this, stage) : default;
    public readonly struct Scope : IDisposable
    {
        private readonly PhysicsCpuProfile? _owner;
        private readonly PhysicsWork _stage;
        private readonly long _start;
        internal Scope(PhysicsCpuProfile owner, PhysicsWork stage)
        { _owner = owner; _stage = stage; _start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_owner == null) return;
            _owner._milliseconds[(int)_stage] += Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            _owner._calls[(int)_stage]++;
        }
    }
}

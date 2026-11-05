using System.Diagnostics;

namespace Engine.Render;

public enum RenderWork
{
    Retirement, CommandBegin, BakedLighting, CameraAndLighting, ModelPlan, Reflections,
    ModelUploads, ModelEviction, TexturePlan, TexturePump, ScenePreparation, Shadows,
    SceneDraw, DebrisAndPrefetch, Viewmodel, FlushDraws, ObjectUploads, Resolve, CommandEnd, Submit, Present,
    ShadowObjectUpload
}

// Host-side wall timings, including any driver blocking; these are not GPU timestamps.
public sealed class RenderCpuProfile
{
    private readonly double[] _milliseconds = new double[Enum.GetValues<RenderWork>().Length];
    public bool Enabled { get; set; }
    public static string[] Stages { get; } = Enum.GetNames<RenderWork>();
    public void Reset() { if (Enabled) Array.Clear(_milliseconds); }
    public double[] Snapshot() => _milliseconds.ToArray();
    public Scope Measure(RenderWork stage) => Enabled ? new(this, stage) : default;

    public readonly struct Scope : IDisposable
    {
        private readonly RenderCpuProfile? _owner;
        private readonly RenderWork _stage;
        private readonly long _start;
        internal Scope(RenderCpuProfile owner, RenderWork stage)
        { _owner = owner; _stage = stage; _start = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_owner != null)
                _owner._milliseconds[(int)_stage] += Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        }
    }
}

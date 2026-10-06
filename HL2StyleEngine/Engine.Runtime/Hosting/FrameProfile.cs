using System.Diagnostics;

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

    public static FrameProfile? FromEnvironment()
        => int.TryParse(Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES"), out int frames) && frames > 0
            ? new FrameProfile(Math.Clamp(frames, 1, 10000)) : null;

    private FrameProfile(int target)
    {
        _target = target;
        Engine.Physics.Collision.MeshCollisionMesh.CollectStatistics = true;
        Engine.Physics.Collision.MeshCollisionMesh.QueryCount = 0;
        Engine.Physics.Collision.MeshCollisionMesh.BoundsTests = 0;
        Engine.Physics.Collision.MeshCollisionMesh.CandidateTriangles = 0;
    }

    public void Begin()
    {
        _start = Stopwatch.GetTimestamp();
        _stage = 0;
    }

    public void Mark()
    {
        long now = Stopwatch.GetTimestamp();
        _current[_stage++] = Stopwatch.GetElapsedTime(_start, now).TotalMilliseconds;
        _start = now;
    }

    public bool Finish(int width, int height)
    {
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
        return true;
    }
}

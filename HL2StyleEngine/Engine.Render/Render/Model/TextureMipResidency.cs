namespace Engine.Render;

public sealed record TextureMipSource(string Key, TextureSemantic Semantic, bool Compressed,
    int Width, int Height, long[] Costs, Func<int, PreparedTexture> Load)
{
    public static TextureMipSource From(PreparedTexture full, Func<int, PreparedTexture> load)
    {
        if (full.FirstMip != 0) throw new ArgumentException("Register full texture metadata.");
        return new(full.Key, full.Semantic, full.Compressed, full.Width, full.Height,
            Enumerable.Range(0, full.Mips.Length).Select(i => full.Mips.Skip(i).Sum(m => (long)m.Length)).ToArray(), load);
    }
}
public sealed record TextureMipDemand(string Key, int Mip, float Priority, bool Pinned, int MaxMip = 14);

// Opt-in, render-thread scheduler; one CPU job and one texture transaction at a time.
// Source descriptors retain metadata/load delegates, not decoded texture payloads.
public sealed class TextureMipResidency : IDisposable
{
    private sealed class Entry(TextureMipSource source)
    {
        internal readonly TextureMipSource Source = source;
        internal int Requested, Failed = -1;
        internal int Demotion = -1;
        internal long DemotionSince;
        internal float Priority;
        internal string Error = "";
    }
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private Task<PreparedTexture>? _job;
    private PreparedTexture? _prepared;
    private Entry? _pending;
    private int _pendingMip;
    private bool _disposed;
    private readonly TimeProvider _time;
    public TimeSpan DemotionDelay { get; init; } = TimeSpan.FromSeconds(1.5);
    public TextureMipResidency(TimeProvider? time = null) => _time = time ?? TimeProvider.System;
    public int Replacements { get; private set; }
    public int ChangedParts { get; private set; }
    public bool Busy => _job != null || _prepared != null;
    public long EstimatedBytes { get; private set; }
    public bool OverBudget { get; private set; }
    public IEnumerable<string> Errors => _entries.Values.Where(e => e.Error.Length != 0).Select(e => e.Source.Key + ": " + e.Error);

    public void Register(TextureMipSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Costs.Length is < 1 or > 15 || source.Width < 1 || source.Height < 1 || source.Costs.Any(x => x < 0))
            throw new ArgumentException("Invalid texture descriptor.");
        if (_entries.TryGetValue(source.Key, out var old))
        {
            if (old.Source.Semantic != source.Semantic || old.Source.Compressed != source.Compressed || old.Source.Width != source.Width ||
                old.Source.Height != source.Height || !old.Source.Costs.SequenceEqual(source.Costs))
                throw new InvalidDataException("Conflicting shared texture identity.");
            return;
        }
        if (_entries.Count >= 4096) throw new InvalidOperationException("Texture trial descriptor limit exceeded.");
        _entries.Add(source.Key, new(source with { Costs = source.Costs.ToArray() }));
    }

    public void Plan(IEnumerable<TextureMipDemand> usages, long budget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var shared = usages.GroupBy(d => d.Key).ToDictionary(g => g.Key,
            g => new TextureMipDemand(g.Key, g.Min(d => d.Mip), g.Max(d => d.Priority), g.Any(d => d.Pinned), g.Min(d => d.MaxMip)), StringComparer.Ordinal);
        var entries = _entries.Values.ToArray();
        var demands = entries.Select(e => shared.TryGetValue(e.Source.Key, out var use)
            ? new MipDemand(e.Source.Costs[..(Math.Clamp(use.MaxMip, 0, e.Source.Costs.Length - 1) + 1)], use.Mip, use.Priority, use.Pinned)
            : new MipDemand(e.Source.Costs, 0, 100, true)).ToArray(); // Unclassified users stay full quality.
        var plan = MipResidencyBudget.Plan(demands, budget);
        EstimatedBytes = 0;
        long now = _time.GetTimestamp();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            entry.Priority = demands[i].Pinned ? float.MaxValue : demands[i].Priority;
            int target = plan.Mips[i];
            if (target > entry.Requested && DemotionDelay > TimeSpan.Zero)
            {
                if (entry.Demotion != target) { entry.Demotion = target; entry.DemotionSince = now; }
                // Keep the sharper texture through quick turns/door crossings; never delay promotions.
                if (_time.GetElapsedTime(entry.DemotionSince, now) < DemotionDelay) target = entry.Requested;
            }
            else entry.Demotion = -1;
            if (entry.Requested != target) { entry.Failed = -1; entry.Error = ""; }
            entry.Requested = target;
            EstimatedBytes += entry.Source.Costs[target];
        }
        OverBudget = EstimatedBytes > Math.Max(0, budget);
    }
    public int RequestedMip(string key) => _entries[key].Requested;
    public void Retry() { foreach (var e in _entries.Values) { e.Failed = -1; e.Error = ""; } }

    public void Pump(BasicWorldRenderer world, Renderer renderer)
    {
        if (_disposed) return;
        try
        {
            if (_job is { IsCompleted: true }) { _prepared = _job.GetAwaiter().GetResult(); _job = null; }
            if (_prepared != null)
            {
                if (_pending!.Requested != _pendingMip) { _prepared = null; _pending = null; return; }
                var s = _pending.Source;
                if (_prepared.Key != s.Key || _prepared.Semantic != s.Semantic || _prepared.Compressed != s.Compressed ||
                    _prepared.FirstMip != _pendingMip || _prepared.Width != Math.Max(1, s.Width >> _pendingMip) ||
                    _prepared.Height != Math.Max(1, s.Height >> _pendingMip) || _prepared.Mips.Length != s.Costs.Length - _pendingMip)
                    throw new InvalidDataException("Texture loader returned a different identity or mip.");
                if (!world.TryReplaceTexture(_prepared, renderer, out int changed)) return;
                if (changed > 0) { Replacements++; ChangedParts += changed; }
                _pending.Error = ""; _prepared = null; _pending = null;
                return;
            }
            if (_job != null || renderer.HasPendingRetirements) return;
            var active = world.GetTextureResidency();
            var candidate = _entries.Values.Where(e => e.Failed != e.Requested && active.Any(a => a.Key == e.Source.Key && a.FirstMip != e.Requested))
                .OrderBy(e => active.Any(a => a.Key == e.Source.Key && e.Requested < a.FirstMip) ? 0 : 1)
                .ThenByDescending(e => e.Priority)
                .ThenBy(e => e.Requested).FirstOrDefault(); // Promotions before speculative demotions.
            if (candidate == null) return;
            _pending = candidate; _pendingMip = candidate.Requested;
            int mip = _pendingMip;
            _job = Task.Run(() => candidate.Source.Load(mip));
        }
        catch (Exception ex)
        {
            if (_pending != null && _pending.Requested == _pendingMip)
            { _pending.Failed = _pendingMip; _pending.Error = ex.Message; }
            _prepared = null; _job = null; _pending = null;
        }
    }

    public static int ProjectedMip(int textureSize, float radius, float distance, float uvSpan, float verticalFov,
        int viewportHeight, int currentMip = 0)
    {
        if (textureSize < 1 || viewportHeight < 1 || !float.IsFinite(radius + distance + uvSpan + verticalFov) ||
            radius <= 0 || distance <= radius || uvSpan <= 0 || verticalFov <= 0 || verticalFov >= MathF.PI) return 0;
        float pixels = radius * viewportHeight / (MathF.Tan(verticalFov * .5f) * Math.Max(.01f, distance - radius));
        float ratio = textureSize * uvSpan / Math.Max(1, pixels);
        int target = Math.Clamp((int)MathF.Floor(MathF.Log2(Math.Max(1, ratio))), 0, 3);
        if (target > currentMip && currentMip is >= 0 and < 3 && ratio < MathF.Pow(2, currentMip + 1) * 1.25f) return currentMip;
        return target;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_job != null) _ = _job.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        _job = null; _prepared = null; _pending = null; _entries.Clear();
    }
}

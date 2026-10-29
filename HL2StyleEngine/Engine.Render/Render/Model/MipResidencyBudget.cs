namespace Engine.Render;

public sealed record MipDemand(long[] BytesByMip, int PreferredMip, float Priority, bool Pinned);
public sealed record MipPlan(int[] Mips, long EstimatedBytes, bool OverBudget);

public static class MipResidencyBudget
{
    // Conservative per-model sums may double-count shared textures. Never degrade a pin
    // to hide an impossible budget; report that condition to the caller instead.
    public static MipPlan Plan(IReadOnlyList<MipDemand> demands, long budget)
    {
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
        foreach (var d in demands)
            if (d.BytesByMip.Length is < 1 or > 15 || d.BytesByMip.Any(x => x < 0) ||
                d.BytesByMip.Zip(d.BytesByMip.Skip(1)).Any(p => p.First < p.Second) || !float.IsFinite(d.Priority))
                throw new ArgumentException("Invalid mip costs or priority.");
        var mips = demands.Select(d => d.Pinned ? 0 : Math.Clamp(d.PreferredMip, 0, d.BytesByMip.Length - 1)).ToArray();
        long Total() => demands.Select((d, i) => d.BytesByMip[mips[i]]).Sum();
        long bytes = Total();
        while (bytes > budget)
        {
            int candidate = Enumerable.Range(0, demands.Count)
                .Where(i => !demands[i].Pinned && mips[i] + 1 < demands[i].BytesByMip.Length)
                .OrderBy(i => demands[i].Priority).ThenByDescending(i => demands[i].BytesByMip[mips[i]])
                .DefaultIfEmpty(-1).First();
            if (candidate < 0) break;
            mips[candidate]++;
            bytes = Total();
        }
        return new(mips, bytes, bytes > budget);
    }

    public static int DistanceMip(float distance, int current)
    {
        if (!float.IsFinite(distance) || distance < 0) return 0;
        int target = distance < 12 ? 0 : distance < 24 ? 1 : distance < 48 ? 2 : 3;
        if (current is >= 0 and <= 3 && target > current && distance < 12 * MathF.Pow(2, current) * 1.25f) return current;
        return target;
    }

    public static long[] ModelCosts(LoadedModel model, int maxMip = 3)
    {
        if (maxMip < 0 || maxMip > 14) throw new ArgumentOutOfRangeException(nameof(maxMip));
        var textures = model.Parts.SelectMany(p => new[] { p.BaseColorTexture, p.MaterialTexture, p.NormalTexture, p.EmissiveTexture })
            .OfType<PreparedTexture>().DistinctBy(t => (t.Key, t.Compressed)).ToArray();
        return Enumerable.Range(0, maxMip + 1).Select(m => textures.Sum(t => t.MipTail(m).Bytes)).ToArray();
    }
}

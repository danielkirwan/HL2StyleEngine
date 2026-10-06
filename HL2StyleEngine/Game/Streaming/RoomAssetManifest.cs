using System.Numerics;

namespace Game.Streaming;

public readonly record struct AssetPlacement(string Path, Vector3 Min, Vector3 Max);
public readonly record struct StreamingZone(int X, int Z);

// Spatial dependency manifests are derived from authored world-space bounds, never display names.
public sealed class RoomAssetManifest
{
    public float ZoneSize { get; }
    public Dictionary<StreamingZone, HashSet<string>> Zones { get; } = new();
    public HashSet<string> GlobalAssets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RoomAssetManifest(IEnumerable<AssetPlacement> placements, float zoneSize)
    {
        ZoneSize = float.IsFinite(zoneSize) ? Math.Clamp(zoneSize, 8, 128) : 32;
        foreach (var placement in placements)
        {
            if (string.IsNullOrWhiteSpace(placement.Path)) continue;
            var min = placement.Min; var max = placement.Max;
            if (!Finite(min) || !Finite(max) || min.X > max.X || min.Z > max.Z ||
                Vector3.Abs(min).LengthSquared() > 1e12f || Vector3.Abs(max).LengthSquared() > 1e12f)
            { GlobalAssets.Add(placement.Path); continue; }
            var first = ZoneAt(min); var last = ZoneAt(max);
            if ((long)(last.X - first.X + 1) * (last.Z - first.Z + 1) > 4096)
            { GlobalAssets.Add(placement.Path); continue; }
            for (int x = first.X; x <= last.X; x++)
                for (int z = first.Z; z <= last.Z; z++)
                {
                    var zone = new StreamingZone(x, z);
                    if (!Zones.TryGetValue(zone, out var paths)) Zones[zone] = paths = new(StringComparer.OrdinalIgnoreCase);
                    paths.Add(placement.Path);
                }
        }
    }

    public StreamingZone ZoneAt(Vector3 position)
        => new((int)MathF.Floor(position.X / ZoneSize), (int)MathF.Floor(position.Z / ZoneSize));

    public void Gather(Vector3 position, int neighbours, HashSet<string> destination)
    {
        destination.UnionWith(GlobalAssets);
        if (!Finite(position)) return;
        neighbours = Math.Clamp(neighbours, 1, 4);
        var center = ZoneAt(position);
        for (int x = center.X - neighbours; x <= center.X + neighbours; x++)
            for (int z = center.Z - neighbours; z <= center.Z + neighbours; z++)
                if (Zones.TryGetValue(new(x, z), out var paths)) destination.UnionWith(paths);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

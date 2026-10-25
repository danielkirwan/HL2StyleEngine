using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed class ReflectionResidency
{
    private readonly Dictionary<int, double> _lastWanted = new();

    public static void Validate(ReflectionStreamingSettings settings)
    {
        if (settings.MaxResidentProbes is < 3 or > 8 || !float.IsFinite(settings.PrefetchDistance) ||
            settings.PrefetchDistance is < 0 or > 100 || !float.IsFinite(settings.RetainSeconds) || settings.RetainSeconds is < 0 or > 60)
            throw new InvalidDataException("Reflection streaming requires 3-8 slots, 0-100m prefetch and 0-60s retention.");
    }

    public int[] Select(ReflectionRoomLayout layout, ReflectionStreamingSettings settings, Vector3 position,
        double seconds, IReadOnlySet<int> resident, int capture = -1)
    {
        Validate(settings);
        if (!float.IsFinite(position.X + position.Y + position.Z)) return [];
        var wanted = new List<int>();
        void Add(int i) { if (i >= 0 && !wanted.Contains(i)) wanted.Add(i); }
        // Pin the portal pair ahead of speculation. One extra slot permits an editor capture.
        var portal = layout.Doors.FirstOrDefault(d => ReflectionRooms.Contains(position, d.Min, d.Max));
        bool inPortal = layout.Doors.Any(d => ReflectionRooms.Contains(position, d.Min, d.Max));
        if (inPortal) { Add(portal.A); Add(portal.B); }
        else Add(Enumerable.Range(0, layout.Rooms.Length)
            .Where(i => ReflectionRooms.Contains(position, layout.Rooms[i].Min, layout.Rooms[i].Max))
            .OrderByDescending(i => layout.Rooms[i].Priority).ThenBy(i => layout.Rooms[i].Id, StringComparer.Ordinal).DefaultIfEmpty(-1).First());
        Add(capture);
        var active = wanted.ToHashSet();
        var neighbours = layout.Doors.Where(d => active.Contains(d.A) || active.Contains(d.B))
            .Select(d => (Door: d, Distance: Vector3.Distance(position, Vector3.Clamp(position, d.Min, d.Max))))
            .Where(d => d.Distance <= settings.PrefetchDistance)
            .OrderBy(d => d.Distance - (resident.Contains(d.Door.A) && resident.Contains(d.Door.B) ? 1 : 0));
        foreach (var candidate in neighbours) { Add(candidate.Door.A); Add(candidate.Door.B); }
        wanted = wanted.Take(settings.MaxResidentProbes).ToList();
        foreach (int i in wanted) _lastWanted[i] = seconds;
        foreach (int i in resident.OrderByDescending(i => _lastWanted.GetValueOrDefault(i, double.NegativeInfinity)).ThenBy(i => i))
            if (_lastWanted.TryGetValue(i, out double last) && seconds - last < settings.RetainSeconds && wanted.Count < settings.MaxResidentProbes) Add(i);
        return wanted.ToArray();
    }
}

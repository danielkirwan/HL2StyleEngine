using System.Numerics;

namespace Engine.Render;

// Slots follow light identity, not the camera-sorted light array.
public sealed class PointShadowPool
{
    public const int Capacity = 16;
    private readonly WorldPointLight?[] _owners = new WorldPointLight?[Capacity];
    private readonly int[] _indices = new int[Capacity];

    public IReadOnlyList<int> Select(IReadOnlyList<WorldPointLight> lights, Vector3 camera, int limit, float margin)
    {
        limit = Math.Clamp(limit, 1, Capacity);
        margin = float.IsFinite(margin) ? Math.Clamp(margin, 0, 10) : 0;
        var candidates = Enumerable.Range(0, lights.Count).Where(i => lights[i].CastShadows &&
            !(lights[i].SpotAngleDegrees > 0 && lights[i].Direction.LengthSquared() > .001f)).ToArray();
        // A distance advantage for existing owners prevents chatter without overriding authored priority.
        var wanted = candidates.OrderByDescending(i => lights[i].Priority)
            .ThenBy(i => Vector3.Distance(camera, lights[i].Position) -
                (_owners.Take(limit).Any(owner => owner.HasValue && Same(owner.Value, lights[i])) ? margin : 0))
            .Take(limit).ToList();
        Array.Fill(_indices, -1);
        for (int slot = 0; slot < Capacity; slot++)
        {
            int match = slot < limit && _owners[slot].HasValue ? wanted.FindIndex(i => Same(_owners[slot]!.Value, lights[i])) : -1;
            if (match >= 0) { _indices[slot] = wanted[match]; wanted.RemoveAt(match); }
            else _owners[slot] = null;
        }
        foreach (int index in wanted)
        {
            int slot = Array.FindIndex(_indices, 0, limit, i => i < 0);
            _indices[slot] = index;
        }
        for (int slot = 0; slot < Capacity; slot++)
            _owners[slot] = _indices[slot] >= 0 ? lights[_indices[slot]] : null;
        return _indices;
    }

    private static bool Same(WorldPointLight a, WorldPointLight b) => a.Id != null || b.Id != null
        ? a.Id != null && a.Id == b.Id : a.Position == b.Position;
}

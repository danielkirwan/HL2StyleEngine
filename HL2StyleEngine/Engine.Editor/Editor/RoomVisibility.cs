using System.Numerics;
using Engine.Core.Serialization;

namespace Engine.Editor.Level;

public sealed class RoomVisibilitySettings
{
    public bool Enabled { get; set; }
    public int PrefetchDepth { get; set; } = 1;
    public List<RoomVolumeDef> Rooms { get; set; } = new();
    public List<RoomPortalDef> Portals { get; set; } = new();
}

public sealed class RoomVolumeDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Room";
    public SerVec3 Center { get; set; } = new(0, 2, 0);
    public SerVec3 Size { get; set; } = new(8, 4, 8);
    public string ReflectionProbeId { get; set; } = "";
}

public sealed class RoomPortalDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Doorway";
    public string RoomA { get; set; } = "";
    public string RoomB { get; set; } = "";
    public SerVec3 Center { get; set; } = new(0, 1.5f, 0);
    public SerVec3 Size { get; set; } = new(2, 3, .2f);
    public int Axis { get; set; } = 2;
}

// Camera visibility only. This graph never owns entities, physics or shadow visibility.
public sealed class RoomVisibilityGraph
{
    private readonly record struct Room(string Id, Vector3 Min, Vector3 Max, string Probe);
    private readonly record struct Portal(int A, int B, Vector3 Center, int Axis, Vector3[] Corners);
    private readonly Room[] _rooms = [];
    private readonly Portal[] _portals = [];
    private readonly bool[] _visible = [], _prefetch = [];
    private readonly int _depth;
    public string Error { get; } = "";
    public bool Active { get; private set; }
    public int VisibleCount => Active ? _visible.Count(v => v) : _rooms.Length;
    public int RoomCount => _rooms.Length;
    public IReadOnlyList<string> PrefetchProbeIds { get; private set; } = [];
    public string Status => Error.Length > 0 ? "Visibility fallback: " + Error : Active
        ? $"Rooms: {VisibleCount}/{RoomCount} visible; {_prefetch.Count(v => v)} prepared" : "Room visibility: all-visible fallback";

    public RoomVisibilityGraph(RoomVisibilitySettings? settings)
    {
        if (settings is not { Enabled: true }) return;
        try
        {
            if (settings.Rooms == null || settings.Portals == null || settings.Rooms.Count is < 1 or > 64 || settings.Portals.Count > 128)
                throw new InvalidDataException("Use 1-64 rooms and at most 128 doorways.");
            _depth = Math.Clamp(settings.PrefetchDepth, 0, 4);
            _rooms = settings.Rooms.Select(r =>
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Id) || !ValidBox(r.Center, r.Size)) throw new InvalidDataException("Invalid room bounds or ID.");
                return new Room(r.Id, (Vector3)r.Center - (Vector3)r.Size / 2, (Vector3)r.Center + (Vector3)r.Size / 2, r.ReflectionProbeId);
            }).ToArray();
            if (_rooms.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _rooms.Length) throw new InvalidDataException("Duplicate room ID.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            _portals = settings.Portals.Select(p =>
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id) || !ValidBox(p.Center, p.Size) || p.Axis is < 0 or > 2)
                    throw new InvalidDataException("Invalid doorway bounds or ID.");
                int a = Array.FindIndex(_rooms, r => r.Id == p.RoomA), b = Array.FindIndex(_rooms, r => r.Id == p.RoomB);
                if (a < 0 || b < 0 || a == b || !Contains(_rooms[a], p.Center, .25f) || !Contains(_rooms[b], p.Center, .25f))
                    throw new InvalidDataException("Doorway must connect two rooms at their shared boundary.");
                Vector3 u = p.Axis == 0 ? Vector3.UnitZ : Vector3.UnitX;
                Vector3 v = p.Axis == 1 ? Vector3.UnitZ : Vector3.UnitY;
                Vector3 size = p.Size, center = p.Center;
                u *= Vector3.Dot(Vector3.Abs(u), size) / 2; v *= Vector3.Dot(Vector3.Abs(v), size) / 2;
                return new Portal(a, b, center, p.Axis, [center-u-v, center+u-v, center+u+v, center-u+v]);
            }).ToArray();
            _visible = new bool[_rooms.Length]; _prefetch = new bool[_rooms.Length];
        }
        catch (InvalidDataException ex) { Error = ex.Message; }
    }

    public void Update(Vector3 eye, Matrix4x4 viewProjection)
    {
        Active = false; PrefetchProbeIds = [];
        if (Error.Length > 0 || _visible.Length == 0 || !Finite(eye)) return;
        Array.Clear(_visible); Array.Clear(_prefetch);
        var planes = Frustum(viewProjection);
        if (planes.Any(p => !Finite(p.Normal) || !float.IsFinite(p.D))) return;
        int visits = 0;
        for (int i = 0; i < _rooms.Length; i++)
            if (Contains(_rooms[i], eye, .05f)) { Active = true; Visit(i, planes, 0); }
        if (!Active || visits > 128) { Active = false; return; }
        Array.Copy(_visible, _prefetch, _visible.Length);
        for (int step = 0; step < _depth; step++)
        {
            var previous = (bool[])_prefetch.Clone();
            foreach (var portal in _portals)
                if (previous[portal.A] || previous[portal.B]) _prefetch[portal.A] = _prefetch[portal.B] = true;
        }
        PrefetchProbeIds = Enumerable.Range(0, _rooms.Length).Where(i => _prefetch[i])
            .OrderBy(i => Vector3.DistanceSquared(eye, Vector3.Clamp(eye, _rooms[i].Min, _rooms[i].Max)))
            .Select(i => _rooms[i].Probe).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();

        void Visit(int room, Plane[] clip, ulong path)
        {
            if (++visits > 128) return;
            _visible[room] = true; path |= 1UL << room;
            foreach (var portal in _portals)
            {
                int next = portal.A == room ? portal.B : portal.B == room ? portal.A : -1;
                if (next < 0 || (path & (1UL << next)) != 0) continue;
                Vector3 delta = portal.Center - eye;
                float distance = MathF.Abs(portal.Axis == 0 ? delta.X : portal.Axis == 1 ? delta.Y : delta.Z);
                Vector3 apertureMin = portal.Corners.Aggregate(Vector3.Min) - new Vector3(.2f);
                Vector3 apertureMax = portal.Corners.Aggregate(Vector3.Max) + new Vector3(.2f);
                bool crossing = eye.X >= apertureMin.X && eye.X <= apertureMax.X && eye.Y >= apertureMin.Y && eye.Y <= apertureMax.Y && eye.Z >= apertureMin.Z && eye.Z <= apertureMax.Z;
                // Plane-wise rejection is conservative; borderline apertures remain visible.
                // Keep both sides while crossing the near plane, even when looking back.
                if (!crossing && clip.Any(p => portal.Corners.All(c => Plane.DotCoordinate(p, c) < -.02f))) continue;
                var child = new List<Plane>(clip);
                if (!crossing && distance > .1f)
                    for (int edge = 0; edge < 4; edge++)
                    {
                        Vector3 normal = Vector3.Cross(portal.Corners[edge]-eye, portal.Corners[(edge+1)%4]-eye);
                        if (normal.LengthSquared() < .000001f) continue;
                        normal = Vector3.Normalize(normal);
                        if (Vector3.Dot(normal, delta) < 0) normal = -normal;
                        child.Add(new Plane(normal, -Vector3.Dot(normal, eye)));
                    }
                Visit(next, child.ToArray(), path);
            }
        }
    }

    public bool IsVisible(Vector3 min, Vector3 max) => Included(min, max, _visible);
    public bool IsPrefetched(Vector3 min, Vector3 max) => Included(min, max, _prefetch);
    private bool Included(Vector3 min, Vector3 max, bool[] set)
    {
        if (!Active || !Finite(min) || !Finite(max) || min.X > max.X || min.Y > max.Y || min.Z > max.Z) return true;
        bool contained = false;
        for (int i = 0; i < _rooms.Length; i++)
        {
            var r = _rooms[i];
            if (set[i] && min.X <= r.Max.X+.02f && max.X >= r.Min.X-.02f && min.Y <= r.Max.Y+.02f && max.Y >= r.Min.Y-.02f && min.Z <= r.Max.Z+.02f && max.Z >= r.Min.Z-.02f) return true;
            if (Contains(r, min, -.02f) && Contains(r, max, -.02f)) contained = true;
        }
        // Unassigned or boundary-spanning objects are global, not assumed hidden.
        return !contained;
    }
    private static bool Contains(Room r, Vector3 p, float margin) => p.X >= r.Min.X-margin && p.X <= r.Max.X+margin && p.Y >= r.Min.Y-margin && p.Y <= r.Max.Y+margin && p.Z >= r.Min.Z-margin && p.Z <= r.Max.Z+margin;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool ValidBox(Vector3 c, Vector3 s) => Finite(c) && Finite(s) && s.X > 0 && s.Y > 0 && s.Z > 0 && s.X <= 10000 && s.Y <= 10000 && s.Z <= 10000;
    private static Plane[] Frustum(Matrix4x4 m) => [
        Plane.Normalize(new(m.M14+m.M11,m.M24+m.M21,m.M34+m.M31,m.M44+m.M41)),
        Plane.Normalize(new(m.M14-m.M11,m.M24-m.M21,m.M34-m.M31,m.M44-m.M41)),
        Plane.Normalize(new(m.M14+m.M12,m.M24+m.M22,m.M34+m.M32,m.M44+m.M42)),
        Plane.Normalize(new(m.M14-m.M12,m.M24-m.M22,m.M34-m.M32,m.M44-m.M42)),
        Plane.Normalize(new(m.M13,m.M23,m.M33,m.M43)),
        Plane.Normalize(new(m.M14-m.M13,m.M24-m.M23,m.M34-m.M33,m.M44-m.M43))];
}

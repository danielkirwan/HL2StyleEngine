using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public readonly record struct ReflectionRoom(string Id, Vector3 Position, Vector3 Min, Vector3 Max,
    Vector3 ProjectionMin, Vector3 ProjectionMax, int Priority);
public readonly record struct ReflectionDoorway(int A, int B, Vector3 Min, Vector3 Max, Vector3 Direction);

[StructLayout(LayoutKind.Sequential)]
internal struct ReflectionMetadata
{
    public Vector4 Info, CaptureA, MinA, MaxA, ProjectionMinA, ProjectionMaxA;
    public Vector4 CaptureB, MinB, MaxB, ProjectionMinB, ProjectionMaxB;
    public Vector4 DoorMin, DoorMax, Direction;
    public const uint Bytes = 224;
}

// Immutable sets: room/doorway metadata is not rewritten for each object or view.
public sealed class ReflectionRooms : IDisposable
{
    private readonly ReflectionRoom[] _rooms;
    private readonly ReflectionDoorway[] _doors;
    private readonly ResourceSet[] _sets;
    private readonly DeviceBuffer[] _buffers;
    public long MetadataBytes => _buffers.Length * ReflectionMetadata.Bytes;
    public int RoomCount => _rooms.Length;

    internal ReflectionRooms(GraphicsDevice device, ResourceLayout layout, Sampler sampler, ReflectionEnvironment fallback,
        ReflectionRoom[] rooms, ReflectionDoorway[] doors, ReflectionEnvironment?[] environments)
    {
        if (rooms.Length is < 1 or > 8 || environments.Length != rooms.Length || doors.Length > 8)
            throw new ArgumentException("Room reflections support 1-8 rooms and at most eight doorway links.");
        foreach (var room in rooms)
            if (!ValidBox(room.Min, room.Max) || !ValidBox(room.ProjectionMin, room.ProjectionMax) || !Finite(room.Position))
                throw new ArgumentException("Invalid reflection room bounds.");
        foreach (var door in doors)
        {
            Vector3 axis=Vector3.Abs(door.Direction);
            if (door.A < 0 || door.B < 0 || door.A >= rooms.Length || door.B >= rooms.Length || door.A == door.B ||
                !ValidBox(door.Min, door.Max) || (axis != Vector3.UnitX && axis != Vector3.UnitY && axis != Vector3.UnitZ))
                throw new ArgumentException("Invalid reflection doorway.");
        }
        _rooms = rooms.ToArray(); _doors = doors.ToArray();
        _sets = new ResourceSet[rooms.Length + doors.Length]; _buffers = new DeviceBuffer[_sets.Length];
        try
        {
            for (int i = 0; i < _sets.Length; i++)
            {
                int a = i < rooms.Length ? i : doors[i - rooms.Length].A;
                int b = i < rooms.Length ? a : doors[i - rooms.Length].B;
                var first = environments[a] is { IsReady: true } readyA ? readyA : fallback;
                var second = environments[b] is { IsReady: true } readyB ? readyB : fallback;
                var ra = rooms[a]; var rb = rooms[b];
                var data = new ReflectionMetadata
                {
                    Info = new(first.LastMip, first.LutSize, 1, i < rooms.Length ? 1 : 2),
                    CaptureA = new(ra.Position, first.LastMip), MinA = new(ra.Min, 0), MaxA = new(ra.Max, 0),
                    ProjectionMinA = new(ra.ProjectionMin, 0), ProjectionMaxA = new(ra.ProjectionMax, 0),
                    CaptureB = new(rb.Position, second.LastMip),
                    MinB = new(rb.Min, rb.Priority > ra.Priority || (rb.Priority == ra.Priority && string.CompareOrdinal(rb.Id,ra.Id)<0) ? 1 : 0), MaxB = new(rb.Max, 0),
                    ProjectionMinB = new(rb.ProjectionMin, 0), ProjectionMaxB = new(rb.ProjectionMax, 0)
                };
                if (i >= rooms.Length)
                {
                    var door = doors[i - rooms.Length];
                    data.DoorMin = new(door.Min, 0); data.DoorMax = new(door.Max, 0); data.Direction = new(door.Direction, 0);
                }
                _buffers[i] = device.ResourceFactory.CreateBuffer(new(ReflectionMetadata.Bytes, BufferUsage.UniformBuffer));
                device.UpdateBuffer(_buffers[i], 0, data);
                _sets[i] = device.ResourceFactory.CreateResourceSet(new(layout, first.CubeView, first.LutView, sampler, _buffers[i], second.CubeView));
            }
        }
        catch { Dispose(); throw; }
    }

    public static bool Contains(Vector3 p, Vector3 min, Vector3 max) => p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;
    public static bool ValidBox(Vector3 min, Vector3 max) => Finite(min) && Finite(max) && max.X > min.X && max.Y > min.Y && max.Z > min.Z;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool Overlaps(Vector3 min, Vector3 max, Vector3 otherMin, Vector3 otherMax)
        => min.X <= otherMax.X && min.Y <= otherMax.Y && min.Z <= otherMax.Z && max.X >= otherMin.X && max.Y >= otherMin.Y && max.Z >= otherMin.Z;

    public int Select(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * .5f;
        int selected = -1, priority = int.MinValue;
        // Only authored openings allow a pair. Distance to a probe cannot connect rooms through walls.
        for (int i = 0; i < _doors.Length; i++)
        {
            var door = _doors[i]; int rank = Math.Max(_rooms[door.A].Priority, _rooms[door.B].Priority);
            bool crossesRooms = Overlaps(min,max,_rooms[door.A].Min,_rooms[door.A].Max) && Overlaps(min,max,_rooms[door.B].Min,_rooms[door.B].Max);
            if ((Overlaps(min, max, door.Min, door.Max) || crossesRooms) && (selected < 0 || rank > priority))
            { selected = _rooms.Length + i; priority = rank; }
        }
        if (selected >= 0) return selected;
        bool contained = false;
        for (int i = 0; i < _rooms.Length; i++)
        {
            var room = _rooms[i]; bool inside = Contains(center, room.Min, room.Max);
            if (!inside && !Overlaps(min, max, room.Min, room.Max)) continue;
            if (selected < 0 || (inside && !contained) || (inside == contained && room.Priority > priority))
            { selected = i; priority = room.Priority; contained = inside; }
        }
        return selected;
    }

    internal ResourceSet? Select(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        Vector3 center = Vector3.Transform((min + max) * .5f, transform), extent = (max - min) * .5f;
        Vector3 worldExtent = new(
            MathF.Abs(transform.M11)*extent.X + MathF.Abs(transform.M21)*extent.Y + MathF.Abs(transform.M31)*extent.Z,
            MathF.Abs(transform.M12)*extent.X + MathF.Abs(transform.M22)*extent.Y + MathF.Abs(transform.M32)*extent.Z,
            MathF.Abs(transform.M13)*extent.X + MathF.Abs(transform.M23)*extent.Y + MathF.Abs(transform.M33)*extent.Z);
        int index = Select(center-worldExtent, center+worldExtent);
        return index < 0 ? null : _sets[index];
    }

    public static float BlendWeight(Vector3 p, ReflectionDoorway door)
    {
        Vector3 t = (p-door.Min)/(door.Max-door.Min);
        float value = Vector3.Dot(t, Vector3.Abs(door.Direction));
        if (door.Direction.X+door.Direction.Y+door.Direction.Z < 0) value = 1-value;
        value = Math.Clamp(value,0,1); return value*value*(3-2*value);
    }

    public static Vector3 Project(Vector3 point, Vector3 ray, ReflectionRoom room)
    {
        if (!Contains(point,room.ProjectionMin,room.ProjectionMax) || !Finite(ray) || ray.LengthSquared() < 1e-12f) return ray;
        Vector3 far = new(ray.X >= 0 ? room.ProjectionMax.X : room.ProjectionMin.X,
            ray.Y >= 0 ? room.ProjectionMax.Y : room.ProjectionMin.Y, ray.Z >= 0 ? room.ProjectionMax.Z : room.ProjectionMin.Z);
        Vector3 distance = new(MathF.Abs(ray.X)>1e-6f ? (far.X-point.X)/ray.X : 1e20f,
            MathF.Abs(ray.Y)>1e-6f ? (far.Y-point.Y)/ray.Y : 1e20f, MathF.Abs(ray.Z)>1e-6f ? (far.Z-point.Z)/ray.Z : 1e20f);
        Vector3 corrected = point+ray*MathF.Max(0,MathF.Min(distance.X,MathF.Min(distance.Y,distance.Z)))-room.Position;
        return Finite(corrected) && corrected.LengthSquared()>1e-12f ? corrected : ray;
    }

    public void Dispose()
    { foreach (var set in _sets) set?.Dispose(); foreach (var buffer in _buffers) buffer?.Dispose(); }
}

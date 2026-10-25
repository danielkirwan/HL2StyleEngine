using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed record ReflectionRoomLayout(ReflectionRoom[] Rooms, ReflectionDoorway[] Doors)
{
    public static ReflectionRoomLayout Create(LevelEditorController editor)
    {
        var level = editor.LevelFile;
        var probes = level.Entities.Where(e => e.Type == EntityTypes.ReflectionProbe).OrderBy(e => e.Id,StringComparer.Ordinal).ToArray();
        if (probes.Length is < 1 or > 8) throw new InvalidDataException("Automatic reflections require 1-8 room probes.");
        if (probes.Select(e => e.Id).Distinct().Count() != probes.Length) throw new InvalidDataException("Room probe IDs must be unique.");
        var rooms = new ReflectionRoom[probes.Length]; var scales = new Vector3[probes.Length];
        for (int i=0;i<probes.Length;i++)
        {
            var entity = probes[i]; var settings = entity.ReflectionProbe ?? throw new InvalidDataException("Probe settings missing.");
            if (!Guid.TryParseExact(entity.Id,"N",out _) || !ReflectionSnapshot.Invariant(entity,level) ||
                !editor.TryGetEntityWorldTRS(level.Entities.IndexOf(entity),out var position,out var rotation,out var scale) ||
                !float.IsFinite(Quaternion.Dot(rotation,Quaternion.Identity)) || MathF.Abs(Quaternion.Dot(rotation,Quaternion.Identity)) < .99999f || !Finite(position) || !Finite(scale) ||
                scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
                throw new InvalidDataException("Room probes need stable GUIDs and fixed positive-scale, axis-aligned transforms.");
            Vector3 influence=(Vector3)settings.InfluenceSize*scale, projection=(Vector3)settings.ProjectionSize*scale;
            if (!ValidSize(influence) || !ValidSize(projection)) throw new InvalidDataException("Room bounds must be finite, positive and at most 500 metres.");
            rooms[i]=new(entity.Id,position,position-influence/2,position+influence/2,position-projection/2,position+projection/2,settings.Priority);
            scales[i]=scale;
        }
        var doors=new List<ReflectionDoorway>(); var links=new HashSet<(int,int)>();
        for (int i=0;i<probes.Length;i++)
        {
            if (probes[i].ReflectionProbe!.Doorway is not { } settings) continue;
            int other=Array.FindIndex(probes,p=>p.Id==settings.NeighborId);
            if (other<0 || other==i || settings.Axis is <0 or >2 || !links.Add((Math.Min(i,other),Math.Max(i,other))))
                throw new InvalidDataException("Doorway needs a different existing probe, one link per pair and an X/Y/Z axis.");
            Vector3 center=rooms[i].Position+(Vector3)settings.Offset*scales[i], size=(Vector3)settings.Size*scales[i];
            if (!Finite(center) || !ValidSize(size)) throw new InvalidDataException("Doorway bounds are invalid.");
            Vector3 axis=settings.Axis==0 ? Vector3.UnitX : settings.Axis==1 ? Vector3.UnitY : Vector3.UnitZ;
            float delta=Vector3.Dot(rooms[other].Position-rooms[i].Position,axis);
            if (MathF.Abs(delta)<.01f) throw new InvalidDataException("Doorway axis must point between the room captures.");
            axis*=MathF.Sign(delta);
            Vector3 start=center-axis*size/2, end=center+axis*size/2;
            // Require the entire entrance/exit cross-section inside its own room, not just its centre.
            Vector3 cross=size*(Vector3.One-Vector3.Abs(axis))/2;
            if (!ReflectionRooms.Contains(start-cross,rooms[i].Min,rooms[i].Max) || !ReflectionRooms.Contains(start+cross,rooms[i].Min,rooms[i].Max) ||
                !ReflectionRooms.Contains(end-cross,rooms[other].Min,rooms[other].Max) || !ReflectionRooms.Contains(end+cross,rooms[other].Min,rooms[other].Max))
                throw new InvalidDataException("Doorway endpoints must fit inside their respective room influence bounds.");
            doors.Add(new(i,other,center-size/2,center+size/2,axis));
        }
        return new(rooms,doors.ToArray());
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool ValidSize(Vector3 value) => Finite(value) && value.X>0 && value.Y>0 && value.Z>0 && value.X<=500 && value.Y<=500 && value.Z<=500;
}

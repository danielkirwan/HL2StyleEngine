using System.Numerics;
using System.Text.Json.Serialization;
using Engine.Core.Serialization;

namespace Engine.Editor.Level;

[Flags]
public enum ReflectionCaptureMask { FixedGeometry = 1, FixedLights = 2, InvariantEmission = 4, BakedDiffuse = 8, All = 15 }
public enum ReflectionLightPreset { InvariantOnly = 0 }

public sealed class ReflectionProbeSettings
{
    public int Resolution { get; set; } = 128;
    public SerVec3 InfluenceSize { get; set; } = new Vector3(8, 4, 8);
    public SerVec3 ProjectionSize { get; set; } = new Vector3(8, 4, 8);
    public int Priority { get; set; }
    public ReflectionCaptureMask CaptureMask { get; set; } = ReflectionCaptureMask.All;
    public ReflectionLightPreset LightingPreset { get; set; } = ReflectionLightPreset.InvariantOnly;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReflectionDoorwaySettings? Doorway { get; set; }
}

public sealed class ReflectionDoorwaySettings
{
    public string NeighborId { get; set; } = "";
    public SerVec3 Offset { get; set; } = new Vector3(0,0,4);
    public SerVec3 Size { get; set; } = new Vector3(2,3,2);
    public int Axis { get; set; } = 2;
}

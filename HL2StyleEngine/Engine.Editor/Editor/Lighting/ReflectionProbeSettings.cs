using System.Numerics;
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
}

using System.Numerics;

namespace Engine.Render;

public readonly record struct WorldPointLight(Vector3 Position, Vector3 Color, float Intensity, float Range,
    Vector3 Direction = default, float SpotAngleDegrees = 0, bool CastShadows = false, int Priority = 0);

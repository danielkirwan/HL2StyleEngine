using System.Numerics;

namespace Engine.Render;

public readonly record struct WorldPointLight(Vector3 Position, Vector3 Color, float Intensity, float Range);

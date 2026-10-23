using System.Numerics;

namespace Engine.Render;

// Per-instance controls never mutate the shared model/material resource.
public readonly record struct ModelEmission(float Multiplier, Vector3? OverrideColor = null, string? PartKey = null)
{
    public Vector3 Resolve(Vector3 imported, string key, string node, string mesh)
    {
        bool matches = string.IsNullOrWhiteSpace(PartKey) || PartKey == key || PartKey == node || PartKey == mesh;
        Vector3 value = matches && OverrideColor.HasValue ? OverrideColor.Value : imported;
        float multiplier = float.IsFinite(Multiplier) ? Math.Clamp(Multiplier, 0, 100) : 0;
        return Vector3.Min(new Vector3(60000), new Vector3(Safe(value.X), Safe(value.Y), Safe(value.Z)) * multiplier);
    }

    private static float Safe(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 10000) : 0;
}

using System;
using System.Numerics;

namespace Game.World;

public static class EditorPicking
{
    public readonly struct Ray
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Dir; 

        public Ray(Vector3 origin, Vector3 dir)
        {
            Origin = origin;
            Dir = Vector3.Normalize(dir);
        }

        public Vector3 GetPoint(float t) => Origin + Dir * t;
    }

    public static Ray ScreenPointToRay(
        Vector2 screenPos,
        float viewportWidth,
        float viewportHeight,
        Matrix4x4 view,
        Matrix4x4 proj)
    {
        float x = (2f * screenPos.X / viewportWidth) - 1f;
        float y = 1f - (2f * screenPos.Y / viewportHeight);

        Matrix4x4 viewProj = view * proj;
        if (!Matrix4x4.Invert(viewProj, out var invViewProj))
            return new Ray(Vector3.Zero, Vector3.UnitZ);

        Vector4 nearClip = new(x, y, 0f, 1f);
        Vector4 farClip = new(x, y, 1f, 1f);

        Vector4 nearWorld4 = Vector4.Transform(nearClip, invViewProj);
        Vector4 farWorld4 = Vector4.Transform(farClip, invViewProj);

        if (MathF.Abs(nearWorld4.W) > 1e-6f) nearWorld4 /= nearWorld4.W;
        if (MathF.Abs(farWorld4.W) > 1e-6f) farWorld4 /= farWorld4.W;

        Vector3 nearWorld = new(nearWorld4.X, nearWorld4.Y, nearWorld4.Z);
        Vector3 farWorld = new(farWorld4.X, farWorld4.Y, farWorld4.Z);

        Vector3 dir = Vector3.Normalize(farWorld - nearWorld);
        return new Ray(nearWorld, dir);
    }

    public static bool RayIntersectsAabb(Ray ray, Vector3 aabbMin, Vector3 aabbMax, out float tHit)
    {
        tHit = 0f;

        float tmin = float.NegativeInfinity;
        float tmax = float.PositiveInfinity;

        // X
        if (!Slab(ray.Origin.X, ray.Dir.X, aabbMin.X, aabbMax.X, ref tmin, ref tmax)) return false;
        // Y
        if (!Slab(ray.Origin.Y, ray.Dir.Y, aabbMin.Y, aabbMax.Y, ref tmin, ref tmax)) return false;
        // Z
        if (!Slab(ray.Origin.Z, ray.Dir.Z, aabbMin.Z, aabbMax.Z, ref tmin, ref tmax)) return false;

        if (tmax < 0f) return false; 

        tHit = (tmin >= 0f) ? tmin : tmax;
        return true;
    }

    private static bool Slab(float ro, float rd, float mn, float mx, ref float tmin, ref float tmax)
    {
        const float EPS = 1e-8f;

        if (MathF.Abs(rd) < EPS)
        {
            return ro >= mn && ro <= mx;
        }

        float t1 = (mn - ro) / rd;
        float t2 = (mx - ro) / rd;
        if (t1 > t2) (t1, t2) = (t2, t1);

        tmin = MathF.Max(tmin, t1);
        tmax = MathF.Min(tmax, t2);

        return tmin <= tmax;
    }

    public static bool RayIntersectsPlane(Ray ray, Vector3 planeNormal, float planeD, out float t)
    {
        const float EPS = 1e-8f;
        float denom = Vector3.Dot(planeNormal, ray.Dir);

        if (MathF.Abs(denom) < EPS)
        {
            t = 0f;
            return false;
        }

        t = (planeD - Vector3.Dot(planeNormal, ray.Origin)) / denom;
        return t >= 0f;
    }
}

using System.Numerics;

namespace Engine.Render;

public readonly struct ShadowFrustum
{
    private readonly Plane _left, _right, _bottom, _top, _near, _far;

    public ShadowFrustum(Matrix4x4 m)
    {
        // System.Numerics uses row vectors; D3D clip depth is 0..w, not -w..w.
        _left = Plane.Normalize(new(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        _right = Plane.Normalize(new(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        _bottom = Plane.Normalize(new(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        _top = Plane.Normalize(new(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));
        _near = Plane.Normalize(new(m.M13, m.M23, m.M33, m.M43));
        _far = Plane.Normalize(new(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
    }

    public bool IntersectsSphere(Vector3 center, float radius)
    {
        float minimum = -MathF.Abs(radius) - .001f;
        return !(Plane.DotCoordinate(_left, center) < minimum || Plane.DotCoordinate(_right, center) < minimum ||
            Plane.DotCoordinate(_bottom, center) < minimum || Plane.DotCoordinate(_top, center) < minimum ||
            Plane.DotCoordinate(_near, center) < minimum || Plane.DotCoordinate(_far, center) < minimum);
    }

    public bool IntersectsBounds(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        Vector3 center = Vector3.Transform((min + max) * .5f, transform);
        Vector3 e = Vector3.Abs(max - min) * .5f;
        Vector3 extent = new(
            MathF.Abs(transform.M11) * e.X + MathF.Abs(transform.M21) * e.Y + MathF.Abs(transform.M31) * e.Z,
            MathF.Abs(transform.M12) * e.X + MathF.Abs(transform.M22) * e.Y + MathF.Abs(transform.M32) * e.Z,
            MathF.Abs(transform.M13) * e.X + MathF.Abs(transform.M23) * e.Y + MathF.Abs(transform.M33) * e.Z);
        return !(Outside(_left) || Outside(_right) || Outside(_bottom) || Outside(_top) || Outside(_near) || Outside(_far));

        // Invalid bounds fail open. Conservative world AABBs also support negative scale/shear.
        bool Outside(Plane p) => Plane.DotCoordinate(p, center) + Vector3.Dot(Vector3.Abs(p.Normal), extent) < -.002f;
    }
}

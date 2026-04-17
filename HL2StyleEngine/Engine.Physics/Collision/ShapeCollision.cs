using System;
using System.Numerics;

namespace Engine.Physics.Collision;

public static class ShapeCollision
{
    public static bool TryResolve(WorldCollider a, WorldCollider b, out Vector3 normal, out float penetration)
    {
        normal = Vector3.Zero;
        penetration = 0f;

        if (!a.GetAabb().Overlaps(b.GetAabb()))
            return false;

        return (a.Shape, b.Shape) switch
        {
            (WorldColliderShape.Box, WorldColliderShape.Box) => TryResolveBoxBox(a, b, out normal, out penetration),
            (WorldColliderShape.Box, WorldColliderShape.Sphere) => TryResolveBoxSphere(a, b, out normal, out penetration),
            (WorldColliderShape.Sphere, WorldColliderShape.Box) => TryResolveSphereBox(a, b, out normal, out penetration),
            (WorldColliderShape.Sphere, WorldColliderShape.Sphere) => TryResolveSphereSphere(a, b, out normal, out penetration),
            (WorldColliderShape.Capsule, WorldColliderShape.Sphere) => TryResolveCapsuleSphere(a, b, out normal, out penetration),
            (WorldColliderShape.Sphere, WorldColliderShape.Capsule) => TryResolveSphereCapsule(a, b, out normal, out penetration),
            (WorldColliderShape.Capsule, WorldColliderShape.Capsule) => TryResolveCapsuleCapsule(a, b, out normal, out penetration),
            (WorldColliderShape.Capsule, WorldColliderShape.Box) => TryResolveCapsuleBox(a, b, out normal, out penetration),
            (WorldColliderShape.Box, WorldColliderShape.Capsule) => TryResolveBoxCapsule(a, b, out normal, out penetration),
            _ => false
        };
    }

    private static bool TryResolveSphereSphere(WorldCollider a, WorldCollider b, out Vector3 normal, out float penetration)
        => TryResolveSphereSphere(a.Center, a.Radius, b.Center, b.Radius, out normal, out penetration);

    private static bool TryResolveBoxSphere(WorldCollider box, WorldCollider sphere, out Vector3 normal, out float penetration)
    {
        if (!TryResolveSphereObb(sphere.Center, sphere.Radius, box.Center, box.HalfExtents, box.Rotation, out Vector3 sphereToBox, out penetration))
        {
            normal = Vector3.Zero;
            return false;
        }

        normal = -sphereToBox;
        return true;
    }

    private static bool TryResolveSphereBox(WorldCollider sphere, WorldCollider box, out Vector3 normal, out float penetration)
        => TryResolveSphereObb(sphere.Center, sphere.Radius, box.Center, box.HalfExtents, box.Rotation, out normal, out penetration);

    private static bool TryResolveCapsuleSphere(WorldCollider capsule, WorldCollider sphere, out Vector3 normal, out float penetration)
    {
        capsule.GetCapsuleSegment(out Vector3 capsuleA, out Vector3 capsuleB);
        Vector3 pointOnCapsule = ClosestPointOnSegment(capsuleA, capsuleB, sphere.Center);
        return TryResolveSphereSphere(pointOnCapsule, capsule.Radius, sphere.Center, sphere.Radius, out normal, out penetration);
    }

    private static bool TryResolveSphereCapsule(WorldCollider sphere, WorldCollider capsule, out Vector3 normal, out float penetration)
    {
        if (!TryResolveCapsuleSphere(capsule, sphere, out Vector3 capsuleToSphere, out penetration))
        {
            normal = Vector3.Zero;
            return false;
        }

        normal = -capsuleToSphere;
        return true;
    }

    private static bool TryResolveCapsuleCapsule(WorldCollider a, WorldCollider b, out Vector3 normal, out float penetration)
    {
        a.GetCapsuleSegment(out Vector3 a0, out Vector3 a1);
        b.GetCapsuleSegment(out Vector3 b0, out Vector3 b1);
        ClosestPointsOnSegments(a0, a1, b0, b1, out Vector3 pa, out Vector3 pb);
        return TryResolveSphereSphere(pa, a.Radius, pb, b.Radius, out normal, out penetration);
    }

    private static bool TryResolveCapsuleBox(WorldCollider capsule, WorldCollider box, out Vector3 normal, out float penetration)
    {
        capsule.GetCapsuleSegment(out Vector3 segA, out Vector3 segB);
        return TryResolveCapsuleObb(segA, segB, capsule.Radius, box.Center, box.HalfExtents, box.Rotation, out normal, out penetration);
    }

    private static bool TryResolveBoxCapsule(WorldCollider box, WorldCollider capsule, out Vector3 normal, out float penetration)
    {
        capsule.GetCapsuleSegment(out Vector3 segA, out Vector3 segB);
        if (!TryResolveCapsuleObb(segA, segB, capsule.Radius, box.Center, box.HalfExtents, box.Rotation, out Vector3 capsuleToBox, out penetration))
        {
            normal = Vector3.Zero;
            return false;
        }

        normal = -capsuleToBox;
        return true;
    }

    private static bool TryResolveSphereSphere(Vector3 aCenter, float aRadius, Vector3 bCenter, float bRadius, out Vector3 normal, out float penetration)
    {
        Vector3 delta = bCenter - aCenter;
        float distSq = delta.LengthSquared();
        float radiusSum = aRadius + bRadius;

        if (distSq <= 1e-8f)
        {
            normal = Vector3.UnitY;
            penetration = radiusSum;
            return true;
        }

        float dist = MathF.Sqrt(distSq);
        penetration = radiusSum - dist;
        if (penetration <= 0f)
        {
            normal = Vector3.Zero;
            penetration = 0f;
            return false;
        }

        normal = delta / dist;
        return true;
    }

    private static bool TryResolveSphereObb(
        Vector3 sphereCenter,
        float sphereRadius,
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion boxRotation,
        out Vector3 normal,
        out float penetration)
    {
        Quaternion inv = Quaternion.Conjugate(boxRotation);
        Vector3 localCenter = Vector3.Transform(sphereCenter - boxCenter, inv);
        Vector3 closestLocal = Clamp(localCenter, -halfExtents, halfExtents);
        Vector3 deltaLocal = closestLocal - localCenter;
        float distSq = deltaLocal.LengthSquared();

        if (distSq > 1e-8f)
        {
            float dist = MathF.Sqrt(distSq);
            penetration = sphereRadius - dist;
            if (penetration <= 0f)
            {
                normal = Vector3.Zero;
                penetration = 0f;
                return false;
            }

            normal = Vector3.Transform(deltaLocal / dist, boxRotation);
            return true;
        }

        Vector3 toFace = halfExtents - Abs(localCenter);
        if (toFace.X <= toFace.Y && toFace.X <= toFace.Z)
        {
            normal = Vector3.Transform(localCenter.X >= 0f ? Vector3.UnitX : -Vector3.UnitX, boxRotation);
            penetration = sphereRadius + toFace.X;
        }
        else if (toFace.Y <= toFace.X && toFace.Y <= toFace.Z)
        {
            normal = Vector3.Transform(localCenter.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY, boxRotation);
            penetration = sphereRadius + toFace.Y;
        }
        else
        {
            normal = Vector3.Transform(localCenter.Z >= 0f ? Vector3.UnitZ : -Vector3.UnitZ, boxRotation);
            penetration = sphereRadius + toFace.Z;
        }

        return penetration > 0f;
    }

    private static bool TryResolveCapsuleObb(
        Vector3 segA,
        Vector3 segB,
        float radius,
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion boxRotation,
        out Vector3 normal,
        out float penetration)
    {
        Quaternion inv = Quaternion.Conjugate(boxRotation);
        Vector3 localA = Vector3.Transform(segA - boxCenter, inv);
        Vector3 localB = Vector3.Transform(segB - boxCenter, inv);

        float bestT = FindClosestPointOnSegmentToAabb(localA, localB, halfExtents);
        Vector3 segmentPoint = Vector3.Lerp(localA, localB, bestT);
        Vector3 closestBoxPoint = Clamp(segmentPoint, -halfExtents, halfExtents);
        Vector3 deltaLocal = closestBoxPoint - segmentPoint;
        float distSq = deltaLocal.LengthSquared();

        if (distSq > 1e-8f)
        {
            float dist = MathF.Sqrt(distSq);
            penetration = radius - dist;
            if (penetration <= 0f)
            {
                normal = Vector3.Zero;
                penetration = 0f;
                return false;
            }

            normal = Vector3.Transform(deltaLocal / dist, boxRotation);
            return true;
        }

        Vector3 toFace = halfExtents - Abs(segmentPoint);
        if (toFace.X <= toFace.Y && toFace.X <= toFace.Z)
        {
            normal = Vector3.Transform(segmentPoint.X >= 0f ? Vector3.UnitX : -Vector3.UnitX, boxRotation);
            penetration = radius + toFace.X;
        }
        else if (toFace.Y <= toFace.X && toFace.Y <= toFace.Z)
        {
            normal = Vector3.Transform(segmentPoint.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY, boxRotation);
            penetration = radius + toFace.Y;
        }
        else
        {
            normal = Vector3.Transform(segmentPoint.Z >= 0f ? Vector3.UnitZ : -Vector3.UnitZ, boxRotation);
            penetration = radius + toFace.Z;
        }

        return penetration > 0f;
    }

    private static bool TryResolveBoxBox(WorldCollider a, WorldCollider b, out Vector3 normal, out float penetration)
    {
        Span<Vector3> aAxes = stackalloc Vector3[3] { a.AxisX, a.AxisY, a.AxisZ };
        Span<Vector3> bAxes = stackalloc Vector3[3] { b.AxisX, b.AxisY, b.AxisZ };
        Span<float> aExt = stackalloc float[3] { a.HalfExtents.X, a.HalfExtents.Y, a.HalfExtents.Z };
        Span<float> bExt = stackalloc float[3] { b.HalfExtents.X, b.HalfExtents.Y, b.HalfExtents.Z };

        Vector3 centerDelta = b.Center - a.Center;
        float bestOverlap = float.PositiveInfinity;
        Vector3 bestAxis = Vector3.UnitY;

        if (!TestObbAxes(aAxes, bAxes, aExt, bExt, centerDelta, ref bestOverlap, ref bestAxis))
        {
            normal = Vector3.Zero;
            penetration = 0f;
            return false;
        }

        normal = bestAxis;
        penetration = bestOverlap;
        return true;
    }

    private static bool TestObbAxes(
        Span<Vector3> aAxes,
        Span<Vector3> bAxes,
        Span<float> aExt,
        Span<float> bExt,
        Vector3 centerDelta,
        ref float bestOverlap,
        ref Vector3 bestAxis)
    {
        for (int i = 0; i < 3; i++)
        {
            if (!TestSatAxis(aAxes[i], aAxes, bAxes, aExt, bExt, centerDelta, ref bestOverlap, ref bestAxis))
                return false;
        }

        for (int i = 0; i < 3; i++)
        {
            if (!TestSatAxis(bAxes[i], aAxes, bAxes, aExt, bExt, centerDelta, ref bestOverlap, ref bestAxis))
                return false;
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 axis = Vector3.Cross(aAxes[i], bAxes[j]);
                if (axis.LengthSquared() < 1e-8f)
                    continue;

                if (!TestSatAxis(Vector3.Normalize(axis), aAxes, bAxes, aExt, bExt, centerDelta, ref bestOverlap, ref bestAxis))
                    return false;
            }
        }

        return true;
    }

    private static bool TestSatAxis(
        Vector3 axis,
        Span<Vector3> aAxes,
        Span<Vector3> bAxes,
        Span<float> aExt,
        Span<float> bExt,
        Vector3 centerDelta,
        ref float bestOverlap,
        ref Vector3 bestAxis)
    {
        float lenSq = axis.LengthSquared();
        if (lenSq < 1e-8f)
            return true;

        Vector3 n = axis / MathF.Sqrt(lenSq);
        float ra =
            aExt[0] * MathF.Abs(Vector3.Dot(n, aAxes[0])) +
            aExt[1] * MathF.Abs(Vector3.Dot(n, aAxes[1])) +
            aExt[2] * MathF.Abs(Vector3.Dot(n, aAxes[2]));
        float rb =
            bExt[0] * MathF.Abs(Vector3.Dot(n, bAxes[0])) +
            bExt[1] * MathF.Abs(Vector3.Dot(n, bAxes[1])) +
            bExt[2] * MathF.Abs(Vector3.Dot(n, bAxes[2]));

        float distance = MathF.Abs(Vector3.Dot(centerDelta, n));
        float overlap = ra + rb - distance;
        if (overlap <= 0f)
            return false;

        if (overlap < bestOverlap)
        {
            bestOverlap = overlap;
            bestAxis = Vector3.Dot(centerDelta, n) >= 0f ? n : -n;
        }

        return true;
    }

    private static float FindClosestPointOnSegmentToAabb(Vector3 a, Vector3 b, Vector3 halfExtents)
    {
        float lo = 0f;
        float hi = 1f;

        for (int i = 0; i < 18; i++)
        {
            float m1 = lo + (hi - lo) / 3f;
            float m2 = hi - (hi - lo) / 3f;
            float d1 = DistanceSqPointAabb(Vector3.Lerp(a, b, m1), halfExtents);
            float d2 = DistanceSqPointAabb(Vector3.Lerp(a, b, m2), halfExtents);

            if (d1 <= d2)
                hi = m2;
            else
                lo = m1;
        }

        return (lo + hi) * 0.5f;
    }

    private static float DistanceSqPointAabb(Vector3 point, Vector3 halfExtents)
    {
        Vector3 clamped = Clamp(point, -halfExtents, halfExtents);
        return Vector3.DistanceSquared(point, clamped);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float denom = ab.LengthSquared();
        if (denom <= 1e-8f)
            return a;

        float t = Math.Clamp(Vector3.Dot(point - a, ab) / denom, 0f, 1f);
        return a + ab * t;
    }

    private static void ClosestPointsOnSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s;
        float t;

        if (a <= 1e-8f && e <= 1e-8f)
        {
            c1 = p1;
            c2 = p2;
            return;
        }

        if (a <= 1e-8f)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-8f)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                s = denom != 0f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;

                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    private static Vector3 Clamp(Vector3 value, Vector3 min, Vector3 max)
        => Vector3.Min(Vector3.Max(value, min), max);

    private static Vector3 Abs(Vector3 v)
        => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
}

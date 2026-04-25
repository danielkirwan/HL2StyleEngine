using System;
using System.Numerics;

namespace Engine.Physics.Collision;

public static class ShapeCollision
{
    public static bool TryResolve(WorldCollider a, WorldCollider b, out Vector3 normal, out float penetration)
    {
        if (!TryResolve(a, b, out ContactManifold manifold))
        {
            normal = Vector3.Zero;
            penetration = 0f;
            return false;
        }

        normal = manifold.Normal;
        penetration = manifold.Penetration;
        return true;
    }

    public static bool TryResolve(WorldCollider a, WorldCollider b, out ContactManifold manifold)
    {
        manifold = default;

        if (!a.GetAabb().Overlaps(b.GetAabb()))
            return false;

        return (a.Shape, b.Shape) switch
        {
            (WorldColliderShape.Box, WorldColliderShape.Box) => TryResolveBoxBox(a, b, out manifold),
            (WorldColliderShape.Box, WorldColliderShape.Sphere) => TryResolveBoxSphere(a, b, out manifold),
            (WorldColliderShape.Sphere, WorldColliderShape.Box) => TryResolveSphereBox(a, b, out manifold),
            (WorldColliderShape.Sphere, WorldColliderShape.Sphere) => TryResolveSphereSphere(a, b, out manifold),
            (WorldColliderShape.Capsule, WorldColliderShape.Sphere) => TryResolveCapsuleSphere(a, b, out manifold),
            (WorldColliderShape.Sphere, WorldColliderShape.Capsule) => TryResolveSphereCapsule(a, b, out manifold),
            (WorldColliderShape.Capsule, WorldColliderShape.Capsule) => TryResolveCapsuleCapsule(a, b, out manifold),
            (WorldColliderShape.Capsule, WorldColliderShape.Box) => TryResolveCapsuleBox(a, b, out manifold),
            (WorldColliderShape.Box, WorldColliderShape.Capsule) => TryResolveBoxCapsule(a, b, out manifold),
            _ => false
        };
    }

    private static bool TryResolveSphereSphere(WorldCollider a, WorldCollider b, out ContactManifold manifold)
        => TryResolveSphereSphere(a.Center, a.Radius, b.Center, b.Radius, out manifold);

    private static bool TryResolveBoxSphere(WorldCollider box, WorldCollider sphere, out ContactManifold manifold)
    {
        if (!TryResolveSphereObb(
                sphere.Center,
                sphere.Radius,
                box.Center,
                box.HalfExtents,
                box.Rotation,
                out Vector3 sphereToBox,
                out float penetration,
                out Vector3 boxPoint,
                out Vector3 spherePoint))
        {
            manifold = default;
            return false;
        }

        manifold = new ContactManifold(
            normal: -sphereToBox,
            penetration: penetration,
            contactCount: 1,
            point0: (boxPoint + spherePoint) * 0.5f);
        return true;
    }

    private static bool TryResolveSphereBox(WorldCollider sphere, WorldCollider box, out ContactManifold manifold)
    {
        if (!TryResolveSphereObb(
                sphere.Center,
                sphere.Radius,
                box.Center,
                box.HalfExtents,
                box.Rotation,
                out Vector3 normal,
                out float penetration,
                out Vector3 boxPoint,
                out Vector3 spherePoint))
        {
            manifold = default;
            return false;
        }

        manifold = new ContactManifold(
            normal: normal,
            penetration: penetration,
            contactCount: 1,
            point0: (boxPoint + spherePoint) * 0.5f);
        return true;
    }

    private static bool TryResolveCapsuleSphere(WorldCollider capsule, WorldCollider sphere, out ContactManifold manifold)
    {
        capsule.GetCapsuleSegment(out Vector3 capsuleA, out Vector3 capsuleB);
        Vector3 pointOnCapsule = ClosestPointOnSegment(capsuleA, capsuleB, sphere.Center);
        if (!TryResolveSphereSphere(pointOnCapsule, capsule.Radius, sphere.Center, sphere.Radius, out manifold))
            return false;

        return true;
    }

    private static bool TryResolveSphereCapsule(WorldCollider sphere, WorldCollider capsule, out ContactManifold manifold)
    {
        if (!TryResolveCapsuleSphere(capsule, sphere, out ContactManifold capsuleToSphere))
        {
            manifold = default;
            return false;
        }

        manifold = new ContactManifold(
            normal: -capsuleToSphere.Normal,
            penetration: capsuleToSphere.Penetration,
            contactCount: capsuleToSphere.ContactCount,
            point0: capsuleToSphere.Point0,
            point1: capsuleToSphere.Point1,
            point2: capsuleToSphere.Point2,
            point3: capsuleToSphere.Point3);
        return true;
    }

    private static bool TryResolveCapsuleCapsule(WorldCollider a, WorldCollider b, out ContactManifold manifold)
    {
        a.GetCapsuleSegment(out Vector3 a0, out Vector3 a1);
        b.GetCapsuleSegment(out Vector3 b0, out Vector3 b1);
        ClosestPointsOnSegments(a0, a1, b0, b1, out Vector3 pa, out Vector3 pb);
        return TryResolveSphereSphere(pa, a.Radius, pb, b.Radius, out manifold);
    }

    private static bool TryResolveCapsuleBox(WorldCollider capsule, WorldCollider box, out ContactManifold manifold)
    {
        capsule.GetCapsuleSegment(out Vector3 segA, out Vector3 segB);
        if (!TryResolveCapsuleObb(
                segA,
                segB,
                capsule.Radius,
                box.Center,
                box.HalfExtents,
                box.Rotation,
                out Vector3 normal,
                out float penetration,
                out Vector3 boxPoint,
                out Vector3 capsulePoint))
        {
            manifold = default;
            return false;
        }

        Vector3 planeNormal = -normal;
        Vector3 planePoint = GetSupportFeatureCenter(box, planeNormal);
        Span<Vector3> supportPoints = stackalloc Vector3[2];
        int supportCount = GetCapsuleSupportPoints(segA, segB, capsule.Radius, normal, supportPoints);
        Span<Vector3> contacts = stackalloc Vector3[2];
        int contactCount = 0;
        for (int i = 0; i < supportCount; i++)
        {
            Vector3 projected = ProjectPointOntoPlane(supportPoints[i], planePoint, planeNormal);
            contactCount = AddUniquePoint3(contacts, contactCount, projected);
        }

        manifold = new ContactManifold(
            normal: normal,
            penetration: penetration,
            contactCount: contactCount > 0 ? contactCount : 1,
            point0: contactCount > 0 ? contacts[0] : (boxPoint + capsulePoint) * 0.5f,
            point1: contactCount > 1 ? contacts[1] : default);
        return true;
    }

    private static bool TryResolveBoxCapsule(WorldCollider box, WorldCollider capsule, out ContactManifold manifold)
    {
        capsule.GetCapsuleSegment(out Vector3 segA, out Vector3 segB);
        if (!TryResolveCapsuleObb(
                segA,
                segB,
                capsule.Radius,
                box.Center,
                box.HalfExtents,
                box.Rotation,
                out Vector3 capsuleToBox,
                out float penetration,
                out Vector3 boxPoint,
                out Vector3 capsulePoint))
        {
            manifold = default;
            return false;
        }

        Vector3 planeNormal = capsuleToBox;
        Vector3 planePoint = GetCapsuleSupportCenter(capsule, planeNormal);
        Span<Vector3> supportPoints = stackalloc Vector3[4];
        int supportCount = GetBoxSupportPoints(box, -capsuleToBox, supportPoints);
        Span<Vector3> contacts = stackalloc Vector3[4];
        int contactCount = 0;
        for (int i = 0; i < supportCount; i++)
        {
            Vector3 projected = ProjectPointOntoPlane(supportPoints[i], planePoint, planeNormal);
            contactCount = AddUniquePoint3(contacts, contactCount, projected);
        }

        manifold = new ContactManifold(
            normal: -capsuleToBox,
            penetration: penetration,
            contactCount: contactCount > 0 ? contactCount : 1,
            point0: contactCount > 0 ? contacts[0] : (boxPoint + capsulePoint) * 0.5f,
            point1: contactCount > 1 ? contacts[1] : default,
            point2: contactCount > 2 ? contacts[2] : default,
            point3: contactCount > 3 ? contacts[3] : default);
        return true;
    }

    private static bool TryResolveSphereSphere(Vector3 aCenter, float aRadius, Vector3 bCenter, float bRadius, out ContactManifold manifold)
    {
        Vector3 delta = bCenter - aCenter;
        float distSq = delta.LengthSquared();
        float radiusSum = aRadius + bRadius;

        Vector3 normal;
        float penetration;

        if (distSq <= 1e-8f)
        {
            normal = Vector3.UnitY;
            penetration = radiusSum;
        }
        else
        {
            float dist = MathF.Sqrt(distSq);
            penetration = radiusSum - dist;
            if (penetration <= 0f)
            {
                manifold = default;
                return false;
            }

            normal = delta / dist;
        }

        Vector3 pointA = aCenter + normal * aRadius;
        Vector3 pointB = bCenter - normal * bRadius;
        manifold = new ContactManifold(
            normal: normal,
            penetration: penetration,
            contactCount: 1,
            point0: (pointA + pointB) * 0.5f);
        return true;
    }

    private static bool TryResolveSphereObb(
        Vector3 sphereCenter,
        float sphereRadius,
        Vector3 boxCenter,
        Vector3 halfExtents,
        Quaternion boxRotation,
        out Vector3 normal,
        out float penetration,
        out Vector3 boxPoint,
        out Vector3 spherePoint)
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
                boxPoint = Vector3.Zero;
                spherePoint = Vector3.Zero;
                penetration = 0f;
                return false;
            }

            normal = Vector3.Transform(deltaLocal / dist, boxRotation);
            boxPoint = Vector3.Transform(closestLocal, boxRotation) + boxCenter;
            spherePoint = sphereCenter + normal * sphereRadius;
            return true;
        }

        Vector3 faceLocal;
        Vector3 planeLocal;
        Vector3 toFace = halfExtents - Abs(localCenter);

        if (toFace.X <= toFace.Y && toFace.X <= toFace.Z)
        {
            faceLocal = localCenter.X >= 0f ? Vector3.UnitX : -Vector3.UnitX;
            planeLocal = new Vector3(faceLocal.X * halfExtents.X, localCenter.Y, localCenter.Z);
            penetration = sphereRadius + toFace.X;
        }
        else if (toFace.Y <= toFace.X && toFace.Y <= toFace.Z)
        {
            faceLocal = localCenter.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY;
            planeLocal = new Vector3(localCenter.X, faceLocal.Y * halfExtents.Y, localCenter.Z);
            penetration = sphereRadius + toFace.Y;
        }
        else
        {
            faceLocal = localCenter.Z >= 0f ? Vector3.UnitZ : -Vector3.UnitZ;
            planeLocal = new Vector3(localCenter.X, localCenter.Y, faceLocal.Z * halfExtents.Z);
            penetration = sphereRadius + toFace.Z;
        }

        normal = Vector3.Transform(faceLocal, boxRotation);
        boxPoint = Vector3.Transform(planeLocal, boxRotation) + boxCenter;
        spherePoint = sphereCenter + normal * sphereRadius;
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
        out float penetration,
        out Vector3 boxPoint,
        out Vector3 capsulePoint)
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
                boxPoint = Vector3.Zero;
                capsulePoint = Vector3.Zero;
                return false;
            }

            normal = Vector3.Transform(deltaLocal / dist, boxRotation);
            boxPoint = Vector3.Transform(closestBoxPoint, boxRotation) + boxCenter;
            capsulePoint = Vector3.Transform(segmentPoint, boxRotation) + boxCenter + normal * radius;
            return true;
        }

        Vector3 faceLocal;
        Vector3 planeLocal;
        Vector3 toFace = halfExtents - Abs(segmentPoint);

        if (toFace.X <= toFace.Y && toFace.X <= toFace.Z)
        {
            faceLocal = segmentPoint.X >= 0f ? Vector3.UnitX : -Vector3.UnitX;
            planeLocal = new Vector3(faceLocal.X * halfExtents.X, segmentPoint.Y, segmentPoint.Z);
            penetration = radius + toFace.X;
        }
        else if (toFace.Y <= toFace.X && toFace.Y <= toFace.Z)
        {
            faceLocal = segmentPoint.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY;
            planeLocal = new Vector3(segmentPoint.X, faceLocal.Y * halfExtents.Y, segmentPoint.Z);
            penetration = radius + toFace.Y;
        }
        else
        {
            faceLocal = segmentPoint.Z >= 0f ? Vector3.UnitZ : -Vector3.UnitZ;
            planeLocal = new Vector3(segmentPoint.X, segmentPoint.Y, faceLocal.Z * halfExtents.Z);
            penetration = radius + toFace.Z;
        }

        normal = Vector3.Transform(faceLocal, boxRotation);
        boxPoint = Vector3.Transform(planeLocal, boxRotation) + boxCenter;
        capsulePoint = Vector3.Transform(segmentPoint, boxRotation) + boxCenter + normal * radius;
        return penetration > 0f;
    }

    private static bool TryResolveBoxBox(WorldCollider a, WorldCollider b, out ContactManifold manifold)
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
            manifold = default;
            return false;
        }

        Vector3 planeNormal = -bestAxis;
        Vector3 planePoint = GetSupportFeatureCenter(b, planeNormal);
        Span<Vector3> supportPoints = stackalloc Vector3[4];
        int supportCount = GetBoxSupportPoints(a, bestAxis, supportPoints);
        if (supportCount <= 0)
        {
            supportPoints[0] = a.Center - bestAxis * a.HalfExtents.Length();
            supportCount = 1;
        }

        Span<Vector3> contacts = stackalloc Vector3[4];
        int contactCount = 0;
        for (int i = 0; i < supportCount; i++)
        {
            Vector3 projected = ProjectPointOntoPlane(supportPoints[i], planePoint, planeNormal);
            Vector3 clamped = ClampPointToBoxFace(b, projected, planeNormal);
            contactCount = AddUniquePoint3(contacts, contactCount, clamped);
        }

        manifold = new ContactManifold(
            normal: bestAxis,
            penetration: bestOverlap,
            contactCount: contactCount,
            point0: contactCount > 0 ? contacts[0] : planePoint,
            point1: contactCount > 1 ? contacts[1] : default,
            point2: contactCount > 2 ? contacts[2] : default,
            point3: contactCount > 3 ? contacts[3] : default);
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

        const float tieBias = 0.01f;
        if (overlap < bestOverlap - tieBias ||
            (MathF.Abs(overlap - bestOverlap) <= tieBias && MathF.Abs(n.Y) > MathF.Abs(bestAxis.Y)))
        {
            bestOverlap = overlap;
            bestAxis = Vector3.Dot(centerDelta, n) >= 0f ? n : -n;
        }

        return true;
    }

    private static int GetBoxSupportPoints(WorldCollider box, Vector3 direction, Span<Vector3> points)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        GetBoxCorners(box, corners);

        float best = float.NegativeInfinity;
        for (int i = 0; i < corners.Length; i++)
            best = MathF.Max(best, Vector3.Dot(corners[i], direction));

        float tolerance = MathF.Max(0.01f, MathF.Max(box.HalfExtents.X, MathF.Max(box.HalfExtents.Y, box.HalfExtents.Z)) * 0.04f);
        int count = 0;
        for (int i = 0; i < corners.Length; i++)
        {
            if (Vector3.Dot(corners[i], direction) >= best - tolerance)
                count = AddUniquePoint3(points, count, corners[i]);
        }

        return count;
    }

    private static Vector3 GetSupportFeatureCenter(WorldCollider collider, Vector3 direction)
    {
        return collider.Shape switch
        {
            WorldColliderShape.Box => GetBoxSupportCenter(collider, direction),
            WorldColliderShape.Sphere => collider.Center + Vector3.Normalize(direction) * collider.Radius,
            WorldColliderShape.Capsule => GetCapsuleSupportCenter(collider, direction),
            _ => collider.Center
        };
    }

    private static Vector3 GetBoxSupportCenter(WorldCollider box, Vector3 direction)
    {
        Span<Vector3> supportPoints = stackalloc Vector3[4];
        int count = GetBoxSupportPoints(box, direction, supportPoints);
        if (count <= 0)
            return box.Center;

        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < count; i++)
            sum += supportPoints[i];

        return sum / count;
    }

    private static Vector3 GetCapsuleSupportCenter(WorldCollider capsule, Vector3 direction)
    {
        if (!TryNormalize(direction, out Vector3 n))
            return capsule.Center;

        capsule.GetCapsuleSegment(out Vector3 a, out Vector3 b);
        float da = Vector3.Dot(a, n);
        float db = Vector3.Dot(b, n);
        const float tolerance = 0.02f;

        Vector3 segmentPoint;
        if (MathF.Abs(da - db) <= tolerance)
            segmentPoint = (a + b) * 0.5f;
        else
            segmentPoint = da >= db ? a : b;

        return segmentPoint + n * capsule.Radius;
    }

    private static Vector3 ClampPointToBoxFace(WorldCollider box, Vector3 point, Vector3 faceNormal)
    {
        Quaternion invRotation = Quaternion.Conjugate(box.Rotation);
        Vector3 localPoint = Vector3.Transform(point - box.Center, invRotation);
        Vector3 localNormal = Vector3.Transform(faceNormal, invRotation);

        float ax = MathF.Abs(localNormal.X);
        float ay = MathF.Abs(localNormal.Y);
        float az = MathF.Abs(localNormal.Z);

        if (ax >= ay && ax >= az)
        {
            localPoint.X = MathF.Sign(localNormal.X) * box.HalfExtents.X;
            localPoint.Y = Math.Clamp(localPoint.Y, -box.HalfExtents.Y, box.HalfExtents.Y);
            localPoint.Z = Math.Clamp(localPoint.Z, -box.HalfExtents.Z, box.HalfExtents.Z);
        }
        else if (ay >= ax && ay >= az)
        {
            localPoint.X = Math.Clamp(localPoint.X, -box.HalfExtents.X, box.HalfExtents.X);
            localPoint.Y = MathF.Sign(localNormal.Y) * box.HalfExtents.Y;
            localPoint.Z = Math.Clamp(localPoint.Z, -box.HalfExtents.Z, box.HalfExtents.Z);
        }
        else
        {
            localPoint.X = Math.Clamp(localPoint.X, -box.HalfExtents.X, box.HalfExtents.X);
            localPoint.Y = Math.Clamp(localPoint.Y, -box.HalfExtents.Y, box.HalfExtents.Y);
            localPoint.Z = MathF.Sign(localNormal.Z) * box.HalfExtents.Z;
        }

        return Vector3.Transform(localPoint, box.Rotation) + box.Center;
    }

    private static int GetCapsuleSupportPoints(Vector3 segA, Vector3 segB, float radius, Vector3 direction, Span<Vector3> points)
    {
        if (!TryNormalize(direction, out Vector3 n))
        {
            points[0] = segA;
            return 1;
        }

        float da = Vector3.Dot(segA, n);
        float db = Vector3.Dot(segB, n);
        const float tolerance = 0.02f;

        if (MathF.Abs(da - db) <= tolerance)
        {
            points[0] = segA + n * radius;
            points[1] = segB + n * radius;
            return 2;
        }

        points[0] = (da >= db ? segA : segB) + n * radius;
        return 1;
    }

    private static int AddUniquePoint3(Span<Vector3> points, int count, Vector3 point)
    {
        for (int i = 0; i < count; i++)
        {
            if (Vector3.DistanceSquared(points[i], point) <= 0.0004f)
                return count;
        }

        if (count < points.Length)
            points[count] = point;

        return Math.Min(count + 1, points.Length);
    }

    private static void GetBoxCorners(WorldCollider box, Span<Vector3> corners)
    {
        Vector3 hx = box.AxisX * box.HalfExtents.X;
        Vector3 hy = box.AxisY * box.HalfExtents.Y;
        Vector3 hz = box.AxisZ * box.HalfExtents.Z;

        corners[0] = box.Center - hx - hy - hz;
        corners[1] = box.Center + hx - hy - hz;
        corners[2] = box.Center + hx - hy + hz;
        corners[3] = box.Center - hx - hy + hz;
        corners[4] = box.Center - hx + hy - hz;
        corners[5] = box.Center + hx + hy - hz;
        corners[6] = box.Center + hx + hy + hz;
        corners[7] = box.Center - hx + hy + hz;
    }

    private static Vector3 ProjectPointOntoPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        if (!TryNormalize(planeNormal, out Vector3 n))
            return point;

        float distance = Vector3.Dot(point - planePoint, n);
        return point - n * distance;
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

    private static bool TryNormalize(Vector3 v, out Vector3 normalized)
    {
        float lenSq = v.LengthSquared();
        if (lenSq < 1e-8f)
        {
            normalized = Vector3.Zero;
            return false;
        }

        normalized = v / MathF.Sqrt(lenSq);
        return true;
    }

    private static Vector3 Clamp(Vector3 value, Vector3 min, Vector3 max)
        => Vector3.Min(Vector3.Max(value, min), max);

    private static Vector3 Abs(Vector3 v)
        => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
}

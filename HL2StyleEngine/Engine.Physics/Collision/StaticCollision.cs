using System;
using System.Collections.Generic;
using System.Numerics;

namespace Engine.Physics.Collision;

public static class StaticCollision
{
    public static (Vector3 center, Vector3 vel, bool grounded) ResolvePlayerAabb(
        Vector3 center,
        Vector3 vel,
        Vector3 extents,
        IReadOnlyList<WorldCollider> world,
        int iterations = 6)
    {
        bool grounded = false;

        for (int it = 0; it < iterations; it++)
        {
            Aabb player = Aabb.FromCenterExtents(center, extents);
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                WorldCollider collider = world[i];
                if (!player.Overlaps(collider.GetAabb()))
                    continue;

                if (!TryResolveAabbWorldCollider(player, collider, out Vector3 normal, out float penetration))
                    continue;

                any = true;
                center -= normal * penetration;
                ApplyPlayerVelocityResponse(ref vel, normal, ref grounded);
                player = Aabb.FromCenterExtents(center, extents);
            }

            if (!any)
                break;
        }

        return (center, vel, grounded);
    }

    public static (Vector3 center, Vector3 vel, bool grounded) ResolveDynamicAabb(
        Vector3 center,
        Vector3 vel,
        Vector3 extents,
        IReadOnlyList<WorldCollider> world,
        float restitution = 0.05f,
        int iterations = 6)
    {
        bool grounded = false;

        for (int it = 0; it < iterations; it++)
        {
            Aabb box = Aabb.FromCenterExtents(center, extents);
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                WorldCollider collider = world[i];
                if (!box.Overlaps(collider.GetAabb()))
                    continue;

                if (!TryResolveAabbWorldCollider(box, collider, out Vector3 normal, out float penetration))
                    continue;

                any = true;
                center -= normal * penetration;
                ApplyDynamicVelocityResponse(ref vel, normal, restitution, ref grounded);
                box = Aabb.FromCenterExtents(center, extents);
            }

            if (!any)
                break;
        }

        ZeroSmallVelocity(ref vel);
        return (center, vel, grounded);
    }

    public static (Vector3 center, Vector3 vel, bool grounded) ResolveDynamicSphere(
        Vector3 center,
        Vector3 vel,
        float radius,
        IReadOnlyList<WorldCollider> world,
        float restitution = 0.05f,
        int iterations = 6)
    {
        bool grounded = false;

        for (int it = 0; it < iterations; it++)
        {
            Aabb bounds = Aabb.FromCenterExtents(center, new Vector3(radius));
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                WorldCollider collider = world[i];
                if (!bounds.Overlaps(collider.GetAabb()))
                    continue;

                if (!TryResolveSphereWorldCollider(center, radius, collider, out Vector3 normal, out float penetration))
                    continue;

                any = true;
                center -= normal * penetration;
                ApplyDynamicVelocityResponse(ref vel, normal, restitution, ref grounded);
                bounds = Aabb.FromCenterExtents(center, new Vector3(radius));
            }

            if (!any)
                break;
        }

        ZeroSmallVelocity(ref vel);
        return (center, vel, grounded);
    }

    public static (Vector3 center, Vector3 vel, bool grounded) ResolveDynamicCapsule(
        Vector3 center,
        Vector3 vel,
        float radius,
        float height,
        IReadOnlyList<WorldCollider> world,
        float restitution = 0.05f,
        int iterations = 6)
    {
        bool grounded = false;
        float halfY = MathF.Max(height * 0.5f, radius);

        for (int it = 0; it < iterations; it++)
        {
            Aabb bounds = Aabb.FromCenterExtents(center, new Vector3(radius, halfY, radius));
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                WorldCollider collider = world[i];
                if (!bounds.Overlaps(collider.GetAabb()))
                    continue;

                if (!TryResolveCapsuleWorldCollider(center, radius, height, collider, out Vector3 normal, out float penetration))
                    continue;

                any = true;
                center -= normal * penetration;
                ApplyDynamicVelocityResponse(ref vel, normal, restitution, ref grounded);
                bounds = Aabb.FromCenterExtents(center, new Vector3(radius, halfY, radius));
            }

            if (!any)
                break;
        }

        ZeroSmallVelocity(ref vel);
        return (center, vel, grounded);
    }

    public static bool TryResolveAabbAabb(
        Aabb a,
        Aabb b,
        out Vector3 normal,
        out float penetration)
    {
        normal = Vector3.Zero;
        penetration = 0f;

        if (!a.Overlaps(b))
            return false;

        Vector3 aCenter = a.Center;
        Vector3 bCenter = b.Center;

        float aEx = (a.Max.X - a.Min.X) * 0.5f;
        float aEy = (a.Max.Y - a.Min.Y) * 0.5f;
        float aEz = (a.Max.Z - a.Min.Z) * 0.5f;

        float bEx = (b.Max.X - b.Min.X) * 0.5f;
        float bEy = (b.Max.Y - b.Min.Y) * 0.5f;
        float bEz = (b.Max.Z - b.Min.Z) * 0.5f;

        Vector3 d = bCenter - aCenter;

        float ox = (aEx + bEx) - MathF.Abs(d.X);
        float oy = (aEy + bEy) - MathF.Abs(d.Y);
        float oz = (aEz + bEz) - MathF.Abs(d.Z);

        if (ox <= oy && ox <= oz)
        {
            penetration = ox;
            normal = d.X >= 0f ? Vector3.UnitX : -Vector3.UnitX;
        }
        else if (oy <= ox && oy <= oz)
        {
            penetration = oy;
            normal = d.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY;
        }
        else
        {
            penetration = oz;
            normal = d.Z >= 0f ? Vector3.UnitZ : -Vector3.UnitZ;
        }

        return penetration > 0f;
    }

    public static bool TryResolveAabbSphere(
        Aabb box,
        Vector3 sphereCenter,
        float sphereRadius,
        out Vector3 normal,
        out float penetration)
    {
        if (!TryResolveSphereAabb(sphereCenter, sphereRadius, box, out Vector3 sphereToBox, out penetration))
        {
            normal = Vector3.Zero;
            return false;
        }

        normal = -sphereToBox;
        return true;
    }

    public static bool TryResolveAabbCapsule(
        Aabb box,
        Vector3 capsuleCenter,
        float capsuleRadius,
        float capsuleHeight,
        out Vector3 normal,
        out float penetration)
    {
        if (!TryResolveCapsuleAabb(capsuleCenter, capsuleRadius, capsuleHeight, box, out Vector3 capsuleToBox, out penetration))
        {
            normal = Vector3.Zero;
            return false;
        }

        normal = -capsuleToBox;
        return true;
    }

    public static bool TryResolveSphereSphere(
        Vector3 aCenter,
        float aRadius,
        Vector3 bCenter,
        float bRadius,
        out Vector3 normal,
        out float penetration)
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

    public static bool TryResolveSphereCapsule(
        Vector3 sphereCenter,
        float sphereRadius,
        Vector3 capsuleCenter,
        float capsuleRadius,
        float capsuleHeight,
        out Vector3 normal,
        out float penetration)
    {
        Vector3 capsulePoint = ClosestPointOnVerticalCapsuleSegment(capsuleCenter, capsuleHeight, capsuleRadius, sphereCenter);
        return TryResolveSphereSphere(
            sphereCenter,
            sphereRadius,
            capsulePoint,
            capsuleRadius,
            out normal,
            out penetration);
    }

    public static bool TryResolveCapsuleCapsule(
        Vector3 aCenter,
        float aRadius,
        float aHeight,
        Vector3 bCenter,
        float bRadius,
        float bHeight,
        out Vector3 normal,
        out float penetration)
    {
        ClosestPointsBetweenVerticalCapsuleSegments(aCenter, aHeight, aRadius, bCenter, bHeight, bRadius, out Vector3 aPoint, out Vector3 bPoint);
        return TryResolveSphereSphere(aPoint, aRadius, bPoint, bRadius, out normal, out penetration);
    }

    public static bool TryResolveSphereAabb(
        Vector3 sphereCenter,
        float sphereRadius,
        Aabb box,
        out Vector3 normal,
        out float penetration)
    {
        Vector3 closest = Vector3.Clamp(sphereCenter, box.Min, box.Max);
        Vector3 delta = closest - sphereCenter;
        float distSq = delta.LengthSquared();

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

            normal = delta / dist;
            return true;
        }

        float toMinX = sphereCenter.X - box.Min.X;
        float toMaxX = box.Max.X - sphereCenter.X;
        float toMinY = sphereCenter.Y - box.Min.Y;
        float toMaxY = box.Max.Y - sphereCenter.Y;
        float toMinZ = sphereCenter.Z - box.Min.Z;
        float toMaxZ = box.Max.Z - sphereCenter.Z;

        float exitX = MathF.Min(toMinX, toMaxX);
        float exitY = MathF.Min(toMinY, toMaxY);
        float exitZ = MathF.Min(toMinZ, toMaxZ);

        if (exitX <= exitY && exitX <= exitZ)
        {
            bool closerToMin = toMinX <= toMaxX;
            normal = closerToMin ? Vector3.UnitX : -Vector3.UnitX;
            penetration = sphereRadius + exitX;
            return penetration > 0f;
        }

        if (exitY <= exitX && exitY <= exitZ)
        {
            bool closerToMin = toMinY <= toMaxY;
            normal = closerToMin ? Vector3.UnitY : -Vector3.UnitY;
            penetration = sphereRadius + exitY;
            return penetration > 0f;
        }

        bool closerToMinZ = toMinZ <= toMaxZ;
        normal = closerToMinZ ? Vector3.UnitZ : -Vector3.UnitZ;
        penetration = sphereRadius + exitZ;
        return penetration > 0f;
    }

    public static bool TryResolveCapsuleAabb(
        Vector3 capsuleCenter,
        float capsuleRadius,
        float capsuleHeight,
        Aabb box,
        out Vector3 normal,
        out float penetration)
    {
        float segmentHalf = GetVerticalCapsuleSegmentHalf(capsuleHeight, capsuleRadius);
        float segmentMinY = capsuleCenter.Y - segmentHalf;
        float segmentMaxY = capsuleCenter.Y + segmentHalf;

        float closestSegmentY;
        if (segmentMaxY < box.Min.Y) closestSegmentY = segmentMaxY;
        else if (segmentMinY > box.Max.Y) closestSegmentY = segmentMinY;
        else closestSegmentY = Math.Clamp(capsuleCenter.Y, box.Min.Y, box.Max.Y);

        Vector3 segmentPoint = new(capsuleCenter.X, closestSegmentY, capsuleCenter.Z);
        Vector3 closestBoxPoint = Vector3.Clamp(segmentPoint, box.Min, box.Max);
        Vector3 delta = closestBoxPoint - segmentPoint;
        float distSq = delta.LengthSquared();

        if (distSq > 1e-8f)
        {
            float dist = MathF.Sqrt(distSq);
            penetration = capsuleRadius - dist;
            if (penetration <= 0f)
            {
                normal = Vector3.Zero;
                penetration = 0f;
                return false;
            }

            normal = delta / dist;
            return true;
        }

        float toMinX = segmentPoint.X - box.Min.X;
        float toMaxX = box.Max.X - segmentPoint.X;
        float toMinY = segmentPoint.Y - box.Min.Y;
        float toMaxY = box.Max.Y - segmentPoint.Y;
        float toMinZ = segmentPoint.Z - box.Min.Z;
        float toMaxZ = box.Max.Z - segmentPoint.Z;

        float exitX = MathF.Min(toMinX, toMaxX);
        float exitY = MathF.Min(toMinY, toMaxY);
        float exitZ = MathF.Min(toMinZ, toMaxZ);

        if (exitX <= exitY && exitX <= exitZ)
        {
            normal = toMinX <= toMaxX ? Vector3.UnitX : -Vector3.UnitX;
            penetration = capsuleRadius + exitX;
            return penetration > 0f;
        }

        if (exitY <= exitX && exitY <= exitZ)
        {
            normal = toMinY <= toMaxY ? Vector3.UnitY : -Vector3.UnitY;
            penetration = capsuleRadius + exitY;
            return penetration > 0f;
        }

        normal = toMinZ <= toMaxZ ? Vector3.UnitZ : -Vector3.UnitZ;
        penetration = capsuleRadius + exitZ;
        return penetration > 0f;
    }

    private static bool TryResolveAabbWorldCollider(Aabb box, WorldCollider collider, out Vector3 normal, out float penetration)
    {
        return collider.Shape switch
        {
            WorldColliderShape.Box => TryResolveAabbAabb(box, collider.GetAabb(), out normal, out penetration),
            WorldColliderShape.Sphere => TryResolveAabbSphere(box, collider.Center, collider.Radius, out normal, out penetration),
            WorldColliderShape.Capsule => TryResolveAabbCapsule(box, collider.Center, collider.Radius, collider.Height, out normal, out penetration),
            _ => NoContact(out normal, out penetration)
        };
    }

    private static bool TryResolveSphereWorldCollider(Vector3 center, float radius, WorldCollider collider, out Vector3 normal, out float penetration)
    {
        return collider.Shape switch
        {
            WorldColliderShape.Box => TryResolveSphereAabb(center, radius, collider.GetAabb(), out normal, out penetration),
            WorldColliderShape.Sphere => TryResolveSphereSphere(center, radius, collider.Center, collider.Radius, out normal, out penetration),
            WorldColliderShape.Capsule => TryResolveSphereCapsule(center, radius, collider.Center, collider.Radius, collider.Height, out normal, out penetration),
            _ => NoContact(out normal, out penetration)
        };
    }

    private static bool TryResolveCapsuleWorldCollider(Vector3 center, float radius, float height, WorldCollider collider, out Vector3 normal, out float penetration)
    {
        if (collider.Shape == WorldColliderShape.Box)
            return TryResolveCapsuleAabb(center, radius, height, collider.GetAabb(), out normal, out penetration);

        if (collider.Shape == WorldColliderShape.Sphere &&
            TryResolveSphereCapsule(collider.Center, collider.Radius, center, radius, height, out Vector3 sphereToCapsule, out penetration))
        {
            normal = -sphereToCapsule;
            return true;
        }

        if (collider.Shape == WorldColliderShape.Capsule)
            return TryResolveCapsuleCapsule(center, radius, height, collider.Center, collider.Radius, collider.Height, out normal, out penetration);

        return NoContact(out normal, out penetration);
    }

    private static void ApplyPlayerVelocityResponse(ref Vector3 vel, Vector3 normal, ref bool grounded)
    {
        float alongNormal = Vector3.Dot(vel, normal);
        if (alongNormal <= 0f)
            return;

        if (normal.Y < -0.5f && vel.Y <= 0f)
        {
            grounded = true;
            vel.Y = 0f;
            return;
        }

        vel -= alongNormal * normal;
    }

    private static void ApplyDynamicVelocityResponse(ref Vector3 vel, Vector3 normal, float restitution, ref bool grounded)
    {
        float alongNormal = Vector3.Dot(vel, normal);
        if (alongNormal <= 0f)
            return;

        if (normal.Y < -0.5f)
        {
            grounded = true;
            vel.Y = 0f;
            return;
        }

        vel -= (1f + restitution) * alongNormal * normal;
    }

    private static Vector3 ClosestPointOnVerticalCapsuleSegment(Vector3 capsuleCenter, float capsuleHeight, float capsuleRadius, Vector3 point)
    {
        float segmentHalf = GetVerticalCapsuleSegmentHalf(capsuleHeight, capsuleRadius);
        float y = Math.Clamp(point.Y, capsuleCenter.Y - segmentHalf, capsuleCenter.Y + segmentHalf);
        return new Vector3(capsuleCenter.X, y, capsuleCenter.Z);
    }

    private static void ClosestPointsBetweenVerticalCapsuleSegments(
        Vector3 aCenter,
        float aHeight,
        float aRadius,
        Vector3 bCenter,
        float bHeight,
        float bRadius,
        out Vector3 aPoint,
        out Vector3 bPoint)
    {
        float aHalf = GetVerticalCapsuleSegmentHalf(aHeight, aRadius);
        float bHalf = GetVerticalCapsuleSegmentHalf(bHeight, bRadius);

        float aMin = aCenter.Y - aHalf;
        float aMax = aCenter.Y + aHalf;
        float bMin = bCenter.Y - bHalf;
        float bMax = bCenter.Y + bHalf;

        float yA;
        float yB;

        if (aMax < bMin)
        {
            yA = aMax;
            yB = bMin;
        }
        else if (bMax < aMin)
        {
            yA = aMin;
            yB = bMax;
        }
        else
        {
            float overlapMin = MathF.Max(aMin, bMin);
            float overlapMax = MathF.Min(aMax, bMax);
            float y = (overlapMin + overlapMax) * 0.5f;
            yA = y;
            yB = y;
        }

        aPoint = new Vector3(aCenter.X, yA, aCenter.Z);
        bPoint = new Vector3(bCenter.X, yB, bCenter.Z);
    }

    private static float GetVerticalCapsuleSegmentHalf(float height, float radius)
        => MathF.Max(0f, height * 0.5f - radius);

    private static void ZeroSmallVelocity(ref Vector3 vel)
    {
        const float minBounceSpeed = 0.15f;
        if (MathF.Abs(vel.X) < minBounceSpeed) vel.X = 0f;
        if (MathF.Abs(vel.Y) < minBounceSpeed) vel.Y = 0f;
        if (MathF.Abs(vel.Z) < minBounceSpeed) vel.Z = 0f;
    }

    private static bool NoContact(out Vector3 normal, out float penetration)
    {
        normal = Vector3.Zero;
        penetration = 0f;
        return false;
    }
}

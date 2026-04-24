using System;
using System.Numerics;

namespace Engine.Physics.Collision
{
    public readonly struct Ray
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Dir; 

        public Ray(Vector3 origin, Vector3 dir)
        {
            Origin = origin;
            Dir = dir;
        }
    }

    public static class Raycast
    {
        public static bool RayIntersectsObb(in Ray ray, Vector3 center, Vector3 halfExtents, Quaternion rotation, float tMin, float tMax, out float hitT)
        {
            Quaternion inv = Quaternion.Conjugate(rotation);
            Vector3 localOrigin = Vector3.Transform(ray.Origin - center, inv);
            Vector3 localDir = Vector3.Transform(ray.Dir, inv);

            return RayIntersectsAabb(
                new Ray(localOrigin, localDir),
                Aabb.FromCenterExtents(Vector3.Zero, halfExtents),
                tMin,
                tMax,
                out hitT);
        }

        public static bool RayIntersectsAabb(in Ray ray, in Aabb aabb, float tMin, float tMax, out float hitT)
        {
            hitT = 0f;

            for (int axis = 0; axis < 3; axis++)
            {
                float origin = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
                float dir = axis == 0 ? ray.Dir.X : axis == 1 ? ray.Dir.Y : ray.Dir.Z;

                float min = axis == 0 ? aabb.Min.X : axis == 1 ? aabb.Min.Y : aabb.Min.Z;
                float max = axis == 0 ? aabb.Max.X : axis == 1 ? aabb.Max.Y : aabb.Max.Z;

                if (MathF.Abs(dir) < 1e-6f)
                {
                    if (origin < min || origin > max)
                        return false;
                }
                else
                {
                    float invD = 1f / dir;
                    float t0 = (min - origin) * invD;
                    float t1 = (max - origin) * invD;
                    if (t0 > t1) (t0, t1) = (t1, t0);

                    tMin = MathF.Max(tMin, t0);
                    tMax = MathF.Min(tMax, t1);
                    if (tMax < tMin)
                        return false;
                }
            }

            hitT = tMin;
            return true;
        }

        public static bool RayIntersectsSphere(in Ray ray, Vector3 center, float radius, float tMin, float tMax, out float hitT)
        {
            Vector3 oc = ray.Origin - center;
            float a = Vector3.Dot(ray.Dir, ray.Dir);
            float b = 2f * Vector3.Dot(oc, ray.Dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                hitT = 0f;
                return false;
            }

            float sqrt = MathF.Sqrt(discriminant);
            float invDenom = 0.5f / a;
            float t0 = (-b - sqrt) * invDenom;
            float t1 = (-b + sqrt) * invDenom;

            if (t0 > t1)
                (t0, t1) = (t1, t0);

            float t = t0 >= tMin ? t0 : t1;
            if (t < tMin || t > tMax)
            {
                hitT = 0f;
                return false;
            }

            hitT = t;
            return true;
        }

        public static bool RayIntersectsCapsule(
            in Ray ray,
            Vector3 center,
            float radius,
            float height,
            Quaternion rotation,
            float tMin,
            float tMax,
            out float hitT)
        {
            hitT = 0f;

            Quaternion inv = Quaternion.Conjugate(rotation);
            Vector3 localOrigin = Vector3.Transform(ray.Origin - center, inv);
            Vector3 localDir = Vector3.Transform(ray.Dir, inv);

            float halfSegment = MathF.Max(0f, height * 0.5f - radius);
            float bestT = float.PositiveInfinity;
            bool found = false;

            if (halfSegment > 1e-5f)
            {
                float a = localDir.X * localDir.X + localDir.Z * localDir.Z;
                float b = 2f * (localOrigin.X * localDir.X + localOrigin.Z * localDir.Z);
                float c = localOrigin.X * localOrigin.X + localOrigin.Z * localOrigin.Z - radius * radius;

                if (MathF.Abs(a) > 1e-6f)
                {
                    float discriminant = b * b - 4f * a * c;
                    if (discriminant >= 0f)
                    {
                        float sqrt = MathF.Sqrt(discriminant);
                        float inv2A = 0.5f / a;
                        float t0 = (-b - sqrt) * inv2A;
                        float t1 = (-b + sqrt) * inv2A;

                        if (TryAcceptCapsuleT(localOrigin, localDir, halfSegment, t0, tMin, tMax, ref bestT))
                            found = true;
                        if (TryAcceptCapsuleT(localOrigin, localDir, halfSegment, t1, tMin, tMax, ref bestT))
                            found = true;
                    }
                }
            }

            if (RayIntersectsSphere(new Ray(localOrigin, localDir), new Vector3(0f, halfSegment, 0f), radius, tMin, tMax, out float topT) &&
                topT < bestT)
            {
                bestT = topT;
                found = true;
            }

            if (RayIntersectsSphere(new Ray(localOrigin, localDir), new Vector3(0f, -halfSegment, 0f), radius, tMin, tMax, out float bottomT) &&
                bottomT < bestT)
            {
                bestT = bottomT;
                found = true;
            }

            if (!found)
                return false;

            hitT = bestT;
            return true;
        }

        private static bool TryAcceptCapsuleT(
            Vector3 localOrigin,
            Vector3 localDir,
            float halfSegment,
            float t,
            float tMin,
            float tMax,
            ref float bestT)
        {
            if (t < tMin || t > tMax || t >= bestT)
                return false;

            float y = localOrigin.Y + localDir.Y * t;
            if (y < -halfSegment || y > halfSegment)
                return false;

            bestT = t;
            return true;
        }
    }
}

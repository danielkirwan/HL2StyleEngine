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
    }
}

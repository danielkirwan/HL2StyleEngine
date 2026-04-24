using System;
using System.Numerics;

namespace Engine.Physics.Collision;

public readonly struct ContactManifold
{
    public readonly Vector3 Normal;
    public readonly float Penetration;
    public readonly int ContactCount;
    public readonly Vector3 Point0;
    public readonly Vector3 Point1;
    public readonly Vector3 Point2;
    public readonly Vector3 Point3;

    public bool HasContact => ContactCount > 0;
    public Vector3 SurfaceNormal => ContactCount > 0 ? -Normal : Vector3.Zero;

    public ContactManifold(
        Vector3 normal,
        float penetration,
        int contactCount,
        Vector3 point0,
        Vector3 point1 = default,
        Vector3 point2 = default,
        Vector3 point3 = default)
    {
        Normal = normal;
        Penetration = penetration;
        ContactCount = Math.Clamp(contactCount, 0, 4);
        Point0 = point0;
        Point1 = point1;
        Point2 = point2;
        Point3 = point3;
    }

    public Vector3 GetPoint(int index)
    {
        return index switch
        {
            0 => Point0,
            1 => Point1,
            2 => Point2,
            3 => Point3,
            _ => Point0
        };
    }

    public Vector3 GetAveragePoint()
    {
        if (ContactCount <= 0)
            return Vector3.Zero;

        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < ContactCount; i++)
            sum += GetPoint(i);

        return sum / ContactCount;
    }
}

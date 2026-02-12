using Engine.Physics.Collision;
using System.Numerics;

namespace Engine.Physics.Dynamics;

public sealed class BoxBody
{
    public Vector3 Center;
    public Vector3 HalfExtents;

    public Vector3 Velocity;

    public float Mass = 10f;
    public bool UseGravity = true;
    public bool IsKinematic = false;

    public BoxBody(Vector3 center, Vector3 halfExtents)
    {
        Center = center;
        HalfExtents = halfExtents;
    }

    public Aabb GetAabb() => Aabb.FromCenterExtents(Center, HalfExtents);

    public void Step(float dt, IReadOnlyList<Aabb> world, float gravityY)
    {
        if (dt <= 0) return;

        // Gravity
        if (UseGravity && !IsKinematic)
            Velocity = new Vector3(Velocity.X, Velocity.Y - gravityY * dt, Velocity.Z);

        // Integrate
        Center += Velocity * dt;

        // Resolve overlaps
        var (newCenter, newVel) = StaticCollision.ResolveDynamicAabb(
            Center, Velocity, HalfExtents, world);

        Center = newCenter;
        Velocity = newVel;
    }

}

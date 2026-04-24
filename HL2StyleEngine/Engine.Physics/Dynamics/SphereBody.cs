using Engine.Physics.Collision;
using System.Numerics;

namespace Engine.Physics.Dynamics;

public sealed class SphereBody
{
    public Vector3 Center;
    public float Radius;

    public Vector3 Velocity;
    public Vector3 LastContactNormal;
    public ContactManifold LastContactManifold;
    public ContactManifold LastSupportManifold;
    public bool HadContact;

    public float Mass = 10f;
    public bool UseGravity = true;
    public bool IsKinematic = false;

    public float Friction = 0.8f;
    public float Restitution = 0.05f;
    public float LinearDamping = 0.02f;

    public SphereBody(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
        Velocity = Vector3.Zero;
    }

    public void Step(float dt, IReadOnlyList<WorldCollider> world, float gravityY = 20f)
    {
        if (dt <= 0f)
            return;

        if (IsKinematic)
            return;

        if (UseGravity)
            Velocity = new Vector3(Velocity.X, Velocity.Y - gravityY * dt, Velocity.Z);

        if (LinearDamping > 0f)
        {
            float k = MathF.Max(0f, 1f - LinearDamping * dt);
            Velocity *= k;
        }

        Vector3 newCenter = Center + Velocity * dt;

        var (resolvedCenter, resolvedVel, grounded, hadContact, contactNormal, contactManifold, supportManifold) =
            StaticCollision.ResolveDynamicSphere(
                newCenter,
                Velocity,
                Radius,
                world,
                restitution: Restitution);

        Center = resolvedCenter;
        Velocity = resolvedVel;
        HadContact = hadContact;
        LastContactNormal = contactNormal;
        LastContactManifold = contactManifold;
        LastSupportManifold = supportManifold;

        if (grounded && Friction > 0f)
        {
            Vector3 lateral = new Vector3(Velocity.X, 0f, Velocity.Z);
            float speed = lateral.Length();

            if (speed > 1e-5f)
            {
                float drop = Friction * gravityY * dt;
                float newSpeed = MathF.Max(0f, speed - drop);
                float scale = newSpeed / speed;

                lateral *= scale;
                Velocity = new Vector3(lateral.X, Velocity.Y, lateral.Z);
            }
        }
    }

    public Aabb GetAabb()
    {
        Vector3 extents = new(Radius, Radius, Radius);
        return Aabb.FromCenterExtents(Center, extents);
    }
}

using Engine.Physics.Collision;
using System.Numerics;

namespace Engine.Physics.Dynamics;

public sealed class BoxBody
{
    public Vector3 Center;
    public Vector3 HalfExtents;

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

    public BoxBody(Vector3 center, Vector3 halfExtents)
    {
        Center = center;
        HalfExtents = halfExtents;
        Velocity = Vector3.Zero;
    }

    public void Step(float dt, IReadOnlyList<WorldCollider> world, Quaternion rotation, float gravityY = 20f)
    {
        if (dt <= 0) return;

        if (IsKinematic)
            return;

        // Gravity
        if (UseGravity)
            Velocity = new Vector3(Velocity.X, Velocity.Y - gravityY * dt, Velocity.Z);

        // Optional air drag (helps stop endless drifting)
        if (LinearDamping > 0f)
        {
            float k = MathF.Max(0f, 1f - LinearDamping * dt);
            Velocity *= k;
        }

        Vector3 newCenter = Center + Velocity * dt;

        var (resolvedCenter, resolvedVel, grounded, hadContact, contactNormal, contactManifold, supportManifold) =
            StaticCollision.ResolveDynamicAabb(
                newCenter,
                Velocity,
                HalfExtents,
                rotation,
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
                float newSpeed = speed - drop;
                if (newSpeed < 0f) newSpeed = 0f;

                float scale = newSpeed / speed;
                lateral *= scale;

                Velocity = new Vector3(lateral.X, Velocity.Y, lateral.Z);
            }
        }
    }

    public Aabb GetAabb() => Aabb.FromCenterExtents(Center, HalfExtents);

}

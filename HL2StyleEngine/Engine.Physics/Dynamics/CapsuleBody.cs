using Engine.Physics.Collision;
using System.Numerics;

namespace Engine.Physics.Dynamics;

public sealed class CapsuleBody
{
    public Vector3 Center;
    public float Radius;
    public float Height;

    public Vector3 Velocity;
    public Vector3 LastContactNormal;
    public bool HadContact;

    public float Mass = 10f;
    public bool UseGravity = true;
    public bool IsKinematic = false;

    public float Friction = 0.8f;
    public float Restitution = 0.05f;
    public float LinearDamping = 0.02f;

    public CapsuleBody(Vector3 center, float radius, float height)
    {
        Center = center;
        Radius = radius;
        Height = height;
        Velocity = Vector3.Zero;
    }

    public float CylinderHalfHeight => MathF.Max(0f, Height * 0.5f - Radius);

    public void Step(float dt, IReadOnlyList<WorldCollider> world, Quaternion rotation, float gravityY = 20f)
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

        var (resolvedCenter, resolvedVel, grounded, hadContact, contactNormal) =
            StaticCollision.ResolveDynamicCapsule(
                newCenter,
                Velocity,
                Radius,
                Height,
                rotation,
                world,
                restitution: Restitution);

        Center = resolvedCenter;
        Velocity = resolvedVel;
        HadContact = hadContact;
        LastContactNormal = contactNormal;

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
        float halfY = MathF.Max(Height * 0.5f, Radius);
        return Aabb.FromCenterExtents(Center, new Vector3(Radius, halfY, Radius));
    }
}

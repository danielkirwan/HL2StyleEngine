using Engine.Physics.Dynamics;
using System.Numerics;

namespace Engine.Physics.Dynamics;

public sealed class PhysicsObjectMover
{
    // Tuning
    public float Stiffness = 120f; // higher = snappier
    public float Damping = 18f;    // higher = less oscillation
    public float MaxAccel = 120f;  // cap so it doesn’t explode

    public void DriveTo(BoxBody body, Vector3 targetCenter, float dt)
    {
        Vector3 x = targetCenter - body.Center;
        Vector3 v = body.Velocity;

        Vector3 accel = (Stiffness * x) - (Damping * v);

        float aLen = accel.Length();
        if (aLen > MaxAccel)
            accel = accel / aLen * MaxAccel;

        body.Velocity += accel * dt;
    }
}

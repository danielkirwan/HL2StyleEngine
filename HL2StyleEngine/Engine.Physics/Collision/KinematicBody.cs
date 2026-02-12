using System;
using System.Numerics;

namespace Engine.Physics.Collision
{
    public sealed class KinematicBody
    {
        // State
        public Vector3 Center;     
        public Vector3 Extents;
        public Vector3 Velocity;

        // Toggles
        public bool UseGravity = true;

        // Tunables
        public float Gravity = 20f;        
        public float LinearDamping = 0.0f; 
        public float Skin = 0.001f;

        // Surface response
        public float Restitution = 0.0f;   
        public float GroundFriction = 8.0f; 
        public float GroundSnapEpsilon = 0.02f; 

        public bool Grounded { get; private set; }
        public CollisionFlags LastCollisions { get; private set; }

        public KinematicBody(Vector3 center, Vector3 extents)
        {
            Center = center;
            Extents = extents;
        }

        /// <summary>
        /// Simple integrate -> collide/slide -> adjust velocity based on what we hit.
        /// </summary>
        public void Step(float dt, IReadOnlyList<Aabb> world)
        {
            Grounded = false;

            // Forces -> velocity
            if (UseGravity)
                Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);

            // Optional linear damping (air drag)
            if (LinearDamping > 0f)
            {
                float k = MathF.Max(0f, 1f - LinearDamping * dt);
                Velocity *= k;
            }

            // Desired motion this tick
            Vector3 delta = Velocity * dt;

            // Move + collide + slide (axis-by-axis)
            var (newCenter, flags) = KinematicMover.MoveAabb(
                startCenter: Center,
                extents: Extents,
                delta: delta,
                world: world,
                skin: Skin);

            if ((flags & CollisionFlags.HitX) != 0)
                Velocity = new Vector3(Bounce(Velocity.X, Restitution), Velocity.Y, Velocity.Z);

            if ((flags & CollisionFlags.HitZ) != 0)
                Velocity = new Vector3(Velocity.X, Velocity.Y, Bounce(Velocity.Z, Restitution));

            if ((flags & CollisionFlags.HitY) != 0)
            {
                if (Velocity.Y <= 0f)
                    Grounded = true;

                Velocity = new Vector3(Velocity.X, Bounce(Velocity.Y, Restitution), Velocity.Z);
            }

            Center = newCenter;
            LastCollisions = flags;

            // Apply ground friction to lateral velocity when grounded
            if (Grounded && GroundFriction > 0f)
            {
                Vector3 lateral = new(Velocity.X, 0f, Velocity.Z);
                float speed = lateral.Length();
                if (speed > 0.0001f)
                {
                    float drop = GroundFriction * dt;
                    float newSpeed = MathF.Max(0f, speed - drop);
                    float scale = newSpeed / speed;
                    Velocity = new Vector3(lateral.X * scale, Velocity.Y, lateral.Z * scale);
                }
            }
        }

        private static float Bounce(float v, float restitution)
        {
            if (restitution <= 0f) return 0f;
            return -v * restitution;
        }
    }
}

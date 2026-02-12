using System;
using System.Collections.Generic;
using System.Numerics;

namespace Engine.Physics.Collision
{
    [Flags]
    public enum CollisionFlags
    {
        None = 0,
        HitX = 1 << 0,
        HitY = 1 << 1,
        HitZ = 1 << 2,
        HitAny = HitX | HitY | HitZ
    }

    public static class KinematicMover
    {
        /// <summary>
        /// Moves an AABB by delta, resolving collisions against world AABBs and sliding along surfaces.
        /// This is deterministic and stable for kinematic objects (held props, doors, etc.).
        /// </summary>
        public static (Vector3 center, CollisionFlags flags) MoveAabb(
            Vector3 startCenter,
            Vector3 extents,
            Vector3 delta,
            IReadOnlyList<Aabb> world,
            float skin = 0.001f,
            int maxPassesPerAxis = 3)
        {
            Vector3 c = startCenter;
            CollisionFlags flags = CollisionFlags.None;

            // Move X, then resolve overlaps (slides on Y/Z)
            if (MathF.Abs(delta.X) > 0f)
            {
                c.X += delta.X;
                if (ResolveAxis(ref c, extents, world, axis: 0, moveSign: MathF.Sign(delta.X), skin, maxPassesPerAxis))
                    flags |= CollisionFlags.HitX;
            }

            // Move Y
            if (MathF.Abs(delta.Y) > 0f)
            {
                c.Y += delta.Y;
                if (ResolveAxis(ref c, extents, world, axis: 1, moveSign: MathF.Sign(delta.Y), skin, maxPassesPerAxis))
                    flags |= CollisionFlags.HitY;
            }

            // Move Z
            if (MathF.Abs(delta.Z) > 0f)
            {
                c.Z += delta.Z;
                if (ResolveAxis(ref c, extents, world, axis: 2, moveSign: MathF.Sign(delta.Z), skin, maxPassesPerAxis))
                    flags |= CollisionFlags.HitZ;
            }

            return (c, flags);
        }

        /// <summary>
        /// Resolves overlaps after moving along a single axis by snapping the AABB out along that axis.
        /// Returns true if we had to resolve (hit something).
        /// </summary>
        private static bool ResolveAxis(
            ref Vector3 center,
            Vector3 extents,
            IReadOnlyList<Aabb> world,
            int axis,               // 0=X,1=Y,2=Z
            float moveSign,         // +1 or -1 based on movement direction
            float skin,
            int maxPasses)
        {
            bool hit = false;

            // Multiple passes help with corner cases (pun intended)
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool anyOverlapThisPass = false;
                var player = Aabb.FromCenterExtents(center, extents);

                for (int i = 0; i < world.Count; i++)
                {
                    var box = world[i];
                    if (!player.Overlaps(box)) continue;

                    // Only resolve on the axis we're currently processing.
                    // Snap so our AABB sits flush against the obstacle face + skin.
                    if (axis == 0) // X
                    {
                        if (moveSign > 0f)
                            center.X = (box.Min.X - extents.X) - skin;
                        else
                            center.X = (box.Max.X + extents.X) + skin;
                    }
                    else if (axis == 1) // Y
                    {
                        if (moveSign > 0f)
                            center.Y = (box.Min.Y - extents.Y) - skin;
                        else
                            center.Y = (box.Max.Y + extents.Y) + skin;
                    }
                    else // Z
                    {
                        if (moveSign > 0f)
                            center.Z = (box.Min.Z - extents.Z) - skin;
                        else
                            center.Z = (box.Max.Z + extents.Z) + skin;
                    }

                    hit = true;
                    anyOverlapThisPass = true;

                    // Update player AABB immediately so subsequent checks use the corrected position
                    player = Aabb.FromCenterExtents(center, extents);
                }

                if (!anyOverlapThisPass)
                    break;
            }

            return hit;
        }
    }
}

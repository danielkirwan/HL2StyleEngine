using System;
using System.Collections.Generic;
using System.Numerics;

namespace Engine.Physics.Collision;

public static class StaticCollision
{
    public static (Vector3 center, Vector3 vel, bool grounded) ResolvePlayerAabb(
        Vector3 center,
        Vector3 vel,
        Vector3 extents,
        IReadOnlyList<Aabb> world,
        int iterations = 6)
    {
        bool grounded = false;

        for (int it = 0; it < iterations; it++)
        {
            var player = Aabb.FromCenterExtents(center, extents);
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                var box = world[i];
                if (!player.Overlaps(box)) continue;

                any = true;

                float pushPosX = box.Max.X - player.Min.X;
                float pushNegX = player.Max.X - box.Min.X;
                float penX = MathF.Min(pushPosX, pushNegX);
                float dirX = (pushPosX < pushNegX) ? +1f : -1f;

                float pushPosY = box.Max.Y - player.Min.Y;
                float pushNegY = player.Max.Y - box.Min.Y;
                float penY = MathF.Min(pushPosY, pushNegY);
                float dirY = (pushPosY < pushNegY) ? +1f : -1f;

                float pushPosZ = box.Max.Z - player.Min.Z;
                float pushNegZ = player.Max.Z - box.Min.Z;
                float penZ = MathF.Min(pushPosZ, pushNegZ);
                float dirZ = (pushPosZ < pushNegZ) ? +1f : -1f;

                if (penX <= penY && penX <= penZ)
                {
                    center.X += dirX * penX;
                    vel.X = 0f;
                }
                else if (penY <= penX && penY <= penZ)
                {
                    center.Y += dirY * penY;

                    // only "grounded" when we get pushed UP and we were not moving upward
                    if (dirY > 0f && vel.Y <= 0f)
                    {
                        grounded = true;
                        vel.Y = 0f;
                    }
                    else
                    {
                        vel.Y = 0f;
                    }
                }
                else
                {
                    center.Z += dirZ * penZ;
                    vel.Z = 0f;
                }

                player = Aabb.FromCenterExtents(center, extents);
            }

            if (!any) break;
        }

        return (center, vel, grounded);
    }

    // --------------------------------
    // DYNAMIC BODY (bounce + grounded)
    // --------------------------------
    public static (Vector3 center, Vector3 vel, bool grounded) ResolveDynamicAabb(
        Vector3 center,
        Vector3 vel,
        Vector3 extents,
        IReadOnlyList<Aabb> world,
        float restitution = 0.05f,
        int iterations = 6)
    {
        bool grounded = false;

        for (int it = 0; it < iterations; it++)
        {
            var boxA = Aabb.FromCenterExtents(center, extents);
            bool any = false;

            for (int i = 0; i < world.Count; i++)
            {
                var boxB = world[i];
                if (!boxA.Overlaps(boxB)) continue;

                any = true;

                float pushPosX = boxB.Max.X - boxA.Min.X;
                float pushNegX = boxA.Max.X - boxB.Min.X;
                float penX = MathF.Min(pushPosX, pushNegX);
                float dirX = (pushPosX < pushNegX) ? +1f : -1f;

                float pushPosY = boxB.Max.Y - boxA.Min.Y;
                float pushNegY = boxA.Max.Y - boxB.Min.Y;
                float penY = MathF.Min(pushPosY, pushNegY);
                float dirY = (pushPosY < pushNegY) ? +1f : -1f;

                float pushPosZ = boxB.Max.Z - boxA.Min.Z;
                float pushNegZ = boxA.Max.Z - boxB.Min.Z;
                float penZ = MathF.Min(pushPosZ, pushNegZ);
                float dirZ = (pushPosZ < pushNegZ) ? +1f : -1f;

                if (penX <= penY && penX <= penZ)
                {
                    center.X += dirX * penX;

                    if (dirX > 0f && vel.X < 0f) vel.X = -vel.X * restitution;
                    else if (dirX < 0f && vel.X > 0f) vel.X = -vel.X * restitution;
                }
                else if (penY <= penX && penY <= penZ)
                {
                    center.Y += dirY * penY;

                    if (dirY > 0f)
                    {
                        grounded = true;
                        vel.Y = 0f; // no bounce on ground/platform
                    }
                    else if (dirY < 0f && vel.Y > 0f)
                    {
                        vel.Y = -vel.Y * restitution;
                    }
                }
                else
                {
                    center.Z += dirZ * penZ;

                    if (dirZ > 0f && vel.Z < 0f) vel.Z = -vel.Z * restitution;
                    else if (dirZ < 0f && vel.Z > 0f) vel.Z = -vel.Z * restitution;
                }

                boxA = Aabb.FromCenterExtents(center, extents);
            }

            if (!any) break;
        }

        // tiny bounce cleanup
        const float minBounceSpeed = 0.15f;
        if (MathF.Abs(vel.X) < minBounceSpeed) vel.X = 0f;
        if (MathF.Abs(vel.Y) < minBounceSpeed) vel.Y = 0f;
        if (MathF.Abs(vel.Z) < minBounceSpeed) vel.Z = 0f;

        return (center, vel, grounded);
    }
}

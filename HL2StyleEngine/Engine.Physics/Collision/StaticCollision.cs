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
}

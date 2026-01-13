using System.Numerics;

namespace Engine.Physics.Collision
{
    public readonly struct Aabb
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Aabb(Vector3 min, Vector3 max)
        {
            Min = min; 
            Max = max;
        }

        public Vector3 Center => (Min + Max) * 0.5f;

        public static Aabb FromCenterExtents(Vector3 center, Vector3 extents) => new Aabb (center - extents, center +  extents);

        public bool Overlaps(Aabb other) => 
          (Min.X <= other.Max.X && Max.X >= other.Min.X) &&
          (Min.Y <= other.Max.Y && Max.Y >= other.Min.Y) &&
          (Min.Z <= other.Max.Z && Max.Z >= other.Min.Z);


    }
}

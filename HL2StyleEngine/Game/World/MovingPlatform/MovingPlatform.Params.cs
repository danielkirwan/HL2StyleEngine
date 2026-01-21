using System.Numerics;

namespace Game.World.MovingPlatform
{
    public sealed class MovingPlatformParams
    {
        public Vector3 PointA { get; set; }
        public Vector3 PointB { get; set; }
        public float Speed { get; set; } = 1f;
    }
}

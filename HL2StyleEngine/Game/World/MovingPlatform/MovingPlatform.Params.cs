using Engine.Core.Serialization;
using System.Numerics;

namespace Game.World.MovingPlatform
{
    public sealed class MovingPlatformParams
    {
        public SerVec3 PointA { get; set; }
        public SerVec3 PointB { get; set; }
        public float Speed { get; set; } = 1f;
    }
}

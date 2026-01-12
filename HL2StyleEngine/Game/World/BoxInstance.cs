using System.Numerics;

namespace Game.World
{
    public readonly struct BoxInstance
    {
        public readonly Vector3 Position;
        public readonly Vector3 Size;
        public readonly Vector4 Color;

        public BoxInstance(Vector3 position, Vector3 size, Vector4 color)
        {
            Position = position;
            Size = size;
            Color = color;
        }

        public Matrix4x4 ModelMatrix => Matrix4x4.CreateScale(Size) * Matrix4x4.CreateTranslation(Position);
    }
}

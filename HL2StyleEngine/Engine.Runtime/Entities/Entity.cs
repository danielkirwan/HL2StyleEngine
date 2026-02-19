using Engine.Physics.Dynamics;
using Engine.Runtime.Entities.Interfaces;
using System.Numerics;

namespace Engine.Runtime.Entities
{
    public sealed class Entity
    {
        public string Id { get; }
        public string Type { get; }
        public string Name { get; set; }

        public Vector3 BoxSize = Vector3.One;
        public Vector4 BoxColor = new Vector4(0.6f, 0.6f, 0.6f, 1f);

        public string? ParentId { get; set; }

        public Transform Transform;

        public readonly List<IComponent> Components = new();

        public bool CanPickUp = false;

        public bool IsHeld = false;

        public BoxBody? Body;

        public MotionType MotionType = MotionType.Static;

        public Entity(string id, string type, string name)
        {
            Id = id;
            Type = type;
            Name = name;
            Transform = Transform.Identity;
        }
    }

    public struct Transform
    {
        public Vector3 Position;
        public Vector3 RotationEulerDeg;
        public Vector3 Scale;

        public static Transform Identity => new()
        {
            Position = Vector3.Zero,
            RotationEulerDeg = Vector3.Zero,
            Scale = Vector3.One
        };
    }
}

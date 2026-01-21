using Engine.Runtime.Entities.Interfaces;
using System.Numerics;

namespace Engine.Runtime.Entities
{
    public sealed class Entity
    {
        public string Id { get; }
        public string Type { get; }        // optional, useful for debugging
        public string Name { get; set; }

        public string? ParentId { get; set; } // optional (or keep parent reference later)

        public Transform Transform;

        public readonly List<IComponent> Components = new();

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

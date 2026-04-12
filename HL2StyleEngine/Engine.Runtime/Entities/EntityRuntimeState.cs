using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using System.Numerics;

namespace Engine.Runtime.Entities;

public enum RuntimeShapeKind
{
    None = 0,
    Box = 1
}

public sealed class EntityRenderState
{
    public RuntimeShapeKind Shape = RuntimeShapeKind.None;
    public Vector3 Size = Vector3.One;
    public Vector4 Color = new(0.6f, 0.6f, 0.6f, 1f);

    public bool Enabled => Shape != RuntimeShapeKind.None;
}

public sealed class EntityColliderState
{
    public RuntimeShapeKind Shape = RuntimeShapeKind.None;
    public Vector3 Size = Vector3.One;
    public bool IsSolid = true;
    public bool IsMovingPlatform;
    public Vector3 PreviousPosition;
    public Vector3 Delta;

    public bool Enabled => Shape != RuntimeShapeKind.None;

    public Vector3 HalfExtents => Size * 0.5f;

    public Aabb GetAabb(Vector3 position) => Aabb.FromCenterExtents(position, HalfExtents);
}

public sealed class EntityPhysicsState
{
    public MotionType MotionType = MotionType.Static;
    public BoxBody? BoxBody;
}

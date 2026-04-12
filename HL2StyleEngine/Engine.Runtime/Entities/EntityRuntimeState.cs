using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using System.Numerics;

namespace Engine.Runtime.Entities;

public enum RuntimeShapeKind
{
    None = 0,
    Box = 1,
    Sphere = 2
}

public sealed class EntityRenderState
{
    public RuntimeShapeKind Shape = RuntimeShapeKind.None;
    public Vector3 Size = Vector3.One;
    public float Radius = 0.5f;
    public Vector4 Color = new(0.6f, 0.6f, 0.6f, 1f);

    public bool Enabled => Shape != RuntimeShapeKind.None;
}

public sealed class EntityColliderState
{
    public RuntimeShapeKind Shape = RuntimeShapeKind.None;
    public Vector3 Size = Vector3.One;
    public float Radius = 0.5f;
    public bool IsSolid = true;
    public bool IsMovingPlatform;
    public Vector3 PreviousPosition;
    public Vector3 Delta;

    public bool Enabled => Shape != RuntimeShapeKind.None;

    public Vector3 HalfExtents => Size * 0.5f;

    public Aabb GetAabb(Vector3 position)
    {
        return Shape switch
        {
            RuntimeShapeKind.Box => Aabb.FromCenterExtents(position, HalfExtents),
            RuntimeShapeKind.Sphere => Aabb.FromCenterExtents(position, new Vector3(Radius)),
            _ => Aabb.FromCenterExtents(position, Vector3.Zero)
        };
    }
}

public sealed class EntityPhysicsState
{
    public MotionType MotionType = MotionType.Static;
    public BoxBody? BoxBody;
    public SphereBody? SphereBody;
}

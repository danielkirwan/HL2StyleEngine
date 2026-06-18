using Engine.Runtime.Entities;
using System.Numerics;

namespace Game.Weapons;

public readonly struct WeaponTargetHit
{
    public WeaponTargetHit(Entity target, Vector3 hitPoint, float distance)
    {
        Target = target;
        HitPoint = hitPoint;
        Distance = distance;
    }

    public Entity Target { get; }
    public Vector3 HitPoint { get; }
    public float Distance { get; }
}

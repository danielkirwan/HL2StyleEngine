using System.Numerics;
using Engine.Physics.Collision;

namespace Game.Puzzles;

public sealed class PressurePlateSensor
{
    private float _loadedSeconds;
    public bool Active { get; private set; }

    public void Update(float mass, float minimumMass, float settleSeconds, float dt)
    {
        bool loaded = float.IsFinite(mass) && mass >= MathF.Max(0.01f, minimumMass);
        _loadedSeconds = loaded ? _loadedSeconds + MathF.Max(0, dt) : 0;
        Active = loaded && _loadedSeconds >= MathF.Max(0, settleSeconds);
    }

    public static bool SupportsBody(Vector3 plateCenter, Vector3 plateSize, Quaternion rotation,
        Aabb bodyBounds, Vector3 bodyCenter, Vector3 velocity, bool held)
    {
        if (held || MathF.Abs(velocity.Y) > 0.25f ||
            Vector3.Dot(Vector3.Transform(Vector3.UnitY, rotation), Vector3.UnitY) < 0.999f)
            return false;

        // Horizontal, yaw-rotated plates measure a settled body's bottom and centre footprint.
        Vector3 local = Vector3.Transform(bodyCenter - plateCenter, Quaternion.Conjugate(rotation));
        float top = plateCenter.Y + plateSize.Y / 2;
        return MathF.Abs(bodyBounds.Min.Y - top) <= 0.065f &&
               MathF.Abs(local.X) <= plateSize.X / 2 - 0.05f &&
               MathF.Abs(local.Z) <= plateSize.Z / 2 - 0.05f;
    }
}

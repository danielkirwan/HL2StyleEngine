using System.Numerics;

namespace Game;

public sealed class FpsCamera
{
    public Vector3 Position;
    public float Yaw;   
    public float Pitch; 

    public float MoveSpeed = 6f;           
    public float MouseSensitivity = 0.0025f; 

    public FpsCamera(Vector3 startPos)
    {
        Position = startPos;
        Yaw = 0f;
        Pitch = 0f;
    }

    public Vector3 Forward
    {
        get
        {
            var cp = MathF.Cos(Pitch);
            var sp = MathF.Sin(Pitch);
            var cy = MathF.Cos(Yaw);
            var sy = MathF.Sin(Yaw);

            var f = new Vector3(sy * cp, sp, cy * cp);
            return Vector3.Normalize(f);
        }
    }

    public Vector3 Right => -Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Forward));
    public Vector3 Up => Vector3.UnitY;

    public void AddLook(Vector2 mouseDelta)
    {
        Yaw -= mouseDelta.X * MouseSensitivity;
        Pitch -= mouseDelta.Y * MouseSensitivity; 

        
        float limit = 1.55334f; 
        Pitch = Math.Clamp(Pitch, -limit, limit);
        Yaw = WrapAngle(Yaw);
    }

    public void Move(Vector3 wishDir, float dt)
    {
        if (wishDir.LengthSquared() < 1e-8f) return;
        wishDir = Vector3.Normalize(wishDir);
        Position += wishDir * (MoveSpeed * dt);
    }

    private static float WrapAngle(float a)
    {
        const float TwoPi = MathF.PI * 2f;
        a %= TwoPi;
        if (a <= -MathF.PI) a += TwoPi;
        if (a > MathF.PI) a -= TwoPi;
        return a;
    }

}

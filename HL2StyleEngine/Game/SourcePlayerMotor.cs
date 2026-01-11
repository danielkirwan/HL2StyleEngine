using System.Numerics;

namespace Game;

public sealed class SourcePlayerMotor
{
    private readonly SourceMovementSettings _s;

    public Vector3 Position;  
    public Vector3 Velocity;

    public bool Grounded { get; private set; }

    private float _timeSinceGrounded;
    private float _timeSinceJumpPressed;

    public SourcePlayerMotor(SourceMovementSettings settings, Vector3 startFeetPos)
    {
        _s = settings;
        Position = startFeetPos;
        Velocity = Vector3.Zero;
        Grounded = false;
        _timeSinceGrounded = 999f;
        _timeSinceJumpPressed = 999f;
    }

    public void PressJump()
    {
        _timeSinceJumpPressed = 0f;
    }

    /// <summary>
    /// Source-like movement on a flat ground plane (Y=0). 
    /// wishDir should be normalized (or zero). wishSpeed in m/s.
    /// </summary>
    public void Step(float dt, Vector3 wishDir, float wishSpeed)
    {
        _timeSinceJumpPressed += dt;
        _timeSinceGrounded += dt;

        ResolveGroundPlane(ref Position, ref Velocity);

        if (Grounded)
        {
            _timeSinceGrounded = 0f;

            ApplyFriction(dt);
            Accelerate(dt, wishDir, wishSpeed, _s.Accel);

            TryConsumeJump();
        }
        else
        {
            AirAccelerate(dt, wishDir, wishSpeed, _s.AirAccel);
            Velocity = new Vector3(Velocity.X, Velocity.Y - _s.Gravity * dt, Velocity.Z);
        }

        Position += Velocity * dt;

        ResolveGroundPlane(ref Position, ref Velocity);
    }

    private void TryConsumeJump()
    {
        bool buffered = _timeSinceJumpPressed <= _s.JumpBuffer;
        bool coyoteOk = _timeSinceGrounded <= _s.CoyoteTime; 

        if (buffered && coyoteOk)
        {
            Velocity = new Vector3(Velocity.X, _s.JumpSpeed, Velocity.Z);
            Grounded = false;
            _timeSinceJumpPressed = 999f; 
        }
    }

    private void ApplyFriction(float dt)
    {
        Vector3 lateral = new(Velocity.X, 0f, Velocity.Z);
        float speed = lateral.Length();
        if (speed < 0.0001f) return;

        float control = speed < _s.StopSpeed ? _s.StopSpeed : speed;
        float drop = control * _s.Friction * dt;

        float newSpeed = speed - drop;
        if (newSpeed < 0f) newSpeed = 0f;

        if (newSpeed != speed)
        {
            float scale = newSpeed / speed;
            Velocity = new Vector3(Velocity.X * scale, Velocity.Y, Velocity.Z * scale);
        }
    }

    private void Accelerate(float dt, Vector3 wishDir, float wishSpeed, float accel)
    {
        if (wishSpeed <= 0f) return;
        if (wishDir.LengthSquared() < 0.0001f) return;

        // Clamp wish speed to max
        if (wishSpeed > _s.MaxSpeed) wishSpeed = _s.MaxSpeed;

        Vector3 lateralVel = new(Velocity.X, 0f, Velocity.Z);
        float currentSpeed = Vector3.Dot(lateralVel, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0f) return;

        float accelSpeed = accel * wishSpeed * dt;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;

        Vector3 newLateral = lateralVel + wishDir * accelSpeed;
        Velocity = new Vector3(newLateral.X, Velocity.Y, newLateral.Z);
    }

    private void AirAccelerate(float dt, Vector3 wishDir, float wishSpeed, float airAccel)
    {
        if (wishSpeed <= 0f) return;
        if (wishDir.LengthSquared() < 0.0001f) return;

        if (wishSpeed > _s.MaxSpeed) wishSpeed = _s.MaxSpeed;

        Vector3 lateralVel = new(Velocity.X, 0f, Velocity.Z);
        float currentSpeed = Vector3.Dot(lateralVel, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0f) return;

        float accelSpeed = airAccel * wishSpeed * dt;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;

        Vector3 newLateral = lateralVel + wishDir * accelSpeed;
        Velocity = new Vector3(newLateral.X, Velocity.Y, newLateral.Z);
    }

    private void ResolveGroundPlane(ref Vector3 pos, ref Vector3 vel)
    {
        if (pos.Y <= 0f + _s.GroundEpsilon)
        {
            pos = new Vector3(pos.X, 0f, pos.Z);

            if (vel.Y < 0f) vel = new Vector3(vel.X, 0f, vel.Z);
            Grounded = true;
        }
        else
        {
            Grounded = false;
        }
    }
}

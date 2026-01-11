namespace Game;

public sealed class SourceMovementSettings
{
    public float TickRate = 60f;

    public float MaxSpeed = 7.0f;          
    public float Accel = 12.0f;            
    public float AirAccel = 2.0f;          
    public float Friction = 6.0f;          
    public float StopSpeed = 1.5f;         

    public float Gravity = 20.0f;          
    public float JumpSpeed = 6.5f;         

    public float GroundEpsilon = 0.001f;   
    public float JumpBuffer = 0.10f;       
    public float CoyoteTime = 0.08f;       

    public float EyeHeight = 1.8f;         
}

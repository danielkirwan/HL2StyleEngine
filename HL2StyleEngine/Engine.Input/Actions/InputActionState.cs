using System.Numerics;

namespace Engine.Input.Actions;

/// <summary>
/// State for a single action this frame.
/// </summary>
public readonly struct InputActionState
{
    public bool Down { get; }
    public bool Pressed { get; }   
    public bool Released { get; }  

    public float Value1D { get; }  
    public Vector2 Value2D { get; } 

    public InputActionState(bool down, bool pressed, bool released, float value1D, Vector2 value2D)
    {
        Down = down;
        Pressed = pressed;
        Released = released;
        Value1D = value1D;
        Value2D = value2D;
    }

    public static InputActionState Empty =>
        new(false, false, false, 0f, Vector2.Zero);
}

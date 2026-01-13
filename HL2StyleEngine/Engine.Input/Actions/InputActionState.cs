namespace Engine.Input.Actions;

/// <summary>
/// State for a single action this frame.
/// </summary>
public readonly struct InputActionState
{
    public bool Down { get; }
    public bool Pressed { get; }   // went down this frame
    public bool Released { get; }  // went up this frame

    public InputActionState(bool down, bool pressed, bool released)
    {
        Down = down;
        Pressed = pressed;
        Released = released;
    }
}

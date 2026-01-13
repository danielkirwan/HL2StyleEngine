namespace Engine.Input.Actions;

public sealed class InputAction
{
    public string Name { get; }

    internal InputActionState StateInternal;

    public bool Down => StateInternal.Down;
    public bool Pressed => StateInternal.Pressed;
    public bool Released => StateInternal.Released;

    internal InputAction(string name)
    {
        Name = name;
        StateInternal = new InputActionState(false, false, false);
    }

    public override string ToString() => Name;
}

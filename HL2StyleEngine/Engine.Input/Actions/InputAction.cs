using System.Numerics;

namespace Engine.Input.Actions;

public sealed class InputAction
{
    public string Name { get; }

    internal InputActionState StateInternal;

    public bool Down => StateInternal.Down;
    public bool Pressed => StateInternal.Pressed;
    public bool Released => StateInternal.Released;

    public float Value1D => StateInternal.Value1D;
    public Vector2 Value2D => StateInternal.Value2D;

    internal InputAction(string name)
    {
        Name = name;
        StateInternal = InputActionState.Empty;
    }

    public override string ToString() => Name;
}

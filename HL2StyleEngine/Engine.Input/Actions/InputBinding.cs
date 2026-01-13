using Veldrid;

namespace Engine.Input.Actions;

public enum InputBindingType
{
    Key,
    MouseButton
}

public readonly struct InputBinding
{
    public InputBindingType Type { get; }
    public Key Key { get; }
    public MouseButton MouseButton { get; }

    private InputBinding(InputBindingType type, Key key, MouseButton mouseButton)
    {
        Type = type;
        Key = key;
        MouseButton = mouseButton;
    }

    public static InputBinding FromKey(Key key) =>
        new(InputBindingType.Key, key, default);

    public static InputBinding FromMouse(MouseButton mouseButton) =>
        new(InputBindingType.MouseButton, default, mouseButton);
}

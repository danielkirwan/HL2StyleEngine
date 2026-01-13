using System.Numerics;
using Veldrid;

namespace Engine.Input.Actions;

public enum InputBindingType
{
    Key,
    MouseButton,
    GamepadButton,
    GamepadAxis1D,
    GamepadStick2D,
}

public enum GamepadButton
{
    A,
    B,
    X,
    Y,
    Back,
    Start,
    LeftShoulder,
    RightShoulder,
    LeftStick,
    RightStick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
}

public enum GamepadAxis
{
    LeftX,
    LeftY,
    RightX,
    RightY,
    TriggerLeft,
    TriggerRight,
}

public enum GamepadStick
{
    Left,
    Right
}

/// <summary>
/// A single binding (key/mouse/gamepad) that can drive an action.
/// </summary>
public readonly struct InputBinding
{
    public InputBindingType Type { get; }

    // Keyboard / mouse
    public Key Key { get; }
    public MouseButton MouseButton { get; }

    // Gamepad
    public GamepadButton GamepadButton { get; }
    public GamepadAxis GamepadAxis { get; }
    public GamepadStick GamepadStick { get; }

    // Axis options
    public float Deadzone { get; }
    public float Scale { get; }   // multiply after deadzone
    public bool Invert { get; }   // for Y axis etc.

    private InputBinding(
        InputBindingType type,
        Key key,
        MouseButton mouseButton,
        GamepadButton gamepadButton,
        GamepadAxis gamepadAxis,
        GamepadStick gamepadStick,
        float deadzone,
        float scale,
        bool invert)
    {
        Type = type;
        Key = key;
        MouseButton = mouseButton;
        GamepadButton = gamepadButton;
        GamepadAxis = gamepadAxis;
        GamepadStick = gamepadStick;
        Deadzone = deadzone;
        Scale = scale;
        Invert = invert;
    }

    public static InputBinding FromKey(Key key) =>
        new(InputBindingType.Key, key, default, default, default, default, 0, 1, false);

    public static InputBinding FromMouse(MouseButton button) =>
        new(InputBindingType.MouseButton, default, button, default, default, default, 0, 1, false);

    public static InputBinding FromGamepadButton(GamepadButton button) =>
        new(InputBindingType.GamepadButton, default, default, button, default, default, 0, 1, false);

    public static InputBinding FromGamepadAxis(GamepadAxis axis, float deadzone = 0.15f, float scale = 1f, bool invert = false) =>
        new(InputBindingType.GamepadAxis1D, default, default, default, axis, default, deadzone, scale, invert);

    public static InputBinding FromGamepadStick(GamepadStick stick, float deadzone = 0.15f, float scale = 1f, bool invertY = true) =>
        new(InputBindingType.GamepadStick2D, default, default, default, default, stick, deadzone, scale, invertY);
}

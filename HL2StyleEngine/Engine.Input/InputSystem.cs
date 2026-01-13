using System.Numerics;
using Engine.Input.Actions;
using Engine.Input.Devices;
using Veldrid;

namespace Engine.Input;

public sealed class InputSystem
{
    private readonly InputState _state;
    private readonly ActionMap _map;

    private readonly Dictionary<InputAction, bool> _wasDown = new();

    public InputSystem(InputState state, ActionMap map)
    {
        _state = state;
        _map = map;

        foreach (var a in _map.Actions)
            _wasDown[a] = false;
    }

    public void Update()
    {
        foreach (var action in _map.Actions)
        {
            bool down = false;
            bool pressed = false;

            float value1D = 0f;
            Vector2 value2D = Vector2.Zero;

            var binds = _map.GetBindings(action);
            for (int i = 0; i < binds.Count; i++)
            {
                var b = binds[i];

                switch (b.Type)
                {
                    case InputBindingType.Key:
                        if (_state.IsDown(b.Key)) down = true;
                        if (_state.WasPressed(b.Key)) pressed = true;
                        break;

                    case InputBindingType.MouseButton:
                        bool mouseDown =
                            (b.MouseButton == MouseButton.Left && _state.LeftMouseDown) ||
                            (b.MouseButton == MouseButton.Right && _state.RightMouseDown);

                        if (mouseDown) down = true;
                        break;

                    case InputBindingType.GamepadButton:
                        if (_state.GetGamepadDown(b.GamepadButton)) down = true;
                        if (_state.GetGamepadPressed(b.GamepadButton)) pressed = true;
                        break;

                    case InputBindingType.GamepadAxis1D:
                        {
                            float v = _state.GetAxis(b.GamepadAxis);
                            if (b.Invert) v = -v;

                            float av = MathF.Abs(v);
                            if (av < b.Deadzone) v = 0f;
                            else v = MathF.Sign(v) * ((av - b.Deadzone) / (1f - b.Deadzone));

                            v *= b.Scale;

                            if (MathF.Abs(v) > MathF.Abs(value1D))
                                value1D = v;

                            if (MathF.Abs(v) > 0.01f)
                                down = true;

                            break;
                        }

                    case InputBindingType.GamepadStick2D:
                        {
                            Vector2 v = _state.GetStick(b.GamepadStick, invertY: b.Invert);

                            float len = v.Length();
                            if (len < b.Deadzone) v = Vector2.Zero;
                            else
                            {
                                float t = (len - b.Deadzone) / (1f - b.Deadzone);
                                v = Vector2.Normalize(v) * t;
                            }

                            v *= b.Scale;

                            value2D += v;
                            if (value2D.Length() > 1f)
                                value2D = Vector2.Normalize(value2D);

                            if (value2D.LengthSquared() > 0.0001f)
                                down = true;

                            break;
                        }
                }
            }

            bool released = _wasDown[action] && !down;
            _wasDown[action] = down;

            action.StateInternal = new InputActionState(down, pressed, released, value1D, value2D);
        }
    }
}

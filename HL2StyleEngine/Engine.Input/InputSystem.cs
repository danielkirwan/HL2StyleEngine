using Engine.Input.Actions;
using Engine.Input.Devices;
using Veldrid;

namespace Engine.Input;

/// <summary>
/// Converts raw InputState into per-frame action states using an ActionMap.
/// </summary>
public sealed class InputSystem
{
    private readonly InputState _state;
    private readonly ActionMap _map;

    // Track last-frame Down state so we can produce Released.
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

            var binds = _map.GetBindings(action);
            for (int i = 0; i < binds.Count; i++)
            {
                var b = binds[i];

                if (b.Type == InputBindingType.Key)
                {
                    if (_state.IsDown(b.Key)) down = true;
                    if (_state.WasPressed(b.Key)) pressed = true;
                }
                else if (b.Type == InputBindingType.MouseButton)
                {
                    // For now, treat mouse buttons as "Down" only.
                    // You can expand InputState later to provide WasPressed for mouse.
                    bool mouseDown =
                        (b.MouseButton == MouseButton.Left && _state.LeftMouseDown) ||
                        (b.MouseButton == MouseButton.Right && _state.RightMouseDown);

                    if (mouseDown) down = true;
                }
            }

            bool released = _wasDown[action] && !down;
            _wasDown[action] = down;

            action.StateInternal = new InputActionState(down, pressed, released);
        }
    }
}

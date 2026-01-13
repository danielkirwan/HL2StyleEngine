using System.Numerics;
using Veldrid;

namespace Engine.Input.Devices;

/// <summary>
/// Raw input state updated from Veldrid InputSnapshot.
/// This is intentionally "dumb": just tracks keys/buttons and mouse.
/// </summary>
public sealed class InputState
{
    private readonly HashSet<Key> _down = new();
    private readonly HashSet<Key> _pressedThisFrame = new();

    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseDelta { get; private set; }
    public bool RightMouseDown { get; private set; }
    public bool LeftMouseDown { get; private set; }

    private Vector2 _lastMousePos;
    private bool _hasLastMousePos;

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressedThisFrame.Contains(key);

    /// <summary>Allows the host/window to override mouse delta (relative mode).</summary>
    public void OverrideMouseDelta(Vector2 delta) => MouseDelta = delta;

    public void Update(InputSnapshot snapshot)
    {
        _pressedThisFrame.Clear();
        MouseDelta = Vector2.Zero;

        // Keyboard
        foreach (var ke in snapshot.KeyEvents)
        {
            if (ke.Down)
            {
                if (_down.Add(ke.Key))
                    _pressedThisFrame.Add(ke.Key);
            }
            else
            {
                _down.Remove(ke.Key);
            }
        }

        // Mouse buttons
        foreach (var me in snapshot.MouseEvents)
        {
            if (me.MouseButton == MouseButton.Right) RightMouseDown = me.Down;
            if (me.MouseButton == MouseButton.Left) LeftMouseDown = me.Down;
        }

        // Mouse position + delta (absolute)
        MousePosition = snapshot.MousePosition;

        if (_hasLastMousePos)
            MouseDelta = MousePosition - _lastMousePos;

        _lastMousePos = MousePosition;
        _hasLastMousePos = true;
    }
}

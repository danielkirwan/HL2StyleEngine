using System.Numerics;
using Veldrid;

namespace Engine.Input.Devices;
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

    public bool RelativeMouseMode { get; set; }

    private Vector2 _pendingRelativeDelta;

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressedThisFrame.Contains(key);

    public void SetRelativeMouseDelta(Vector2 delta) => _pendingRelativeDelta += delta;

    public void ClearMouseDelta()
    {
        MouseDelta = Vector2.Zero;
        _pendingRelativeDelta = Vector2.Zero;
    }

    public void Update(InputSnapshot snapshot)
    {
        _pressedThisFrame.Clear();

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

        MousePosition = snapshot.MousePosition;

        if (RelativeMouseMode)
        {
            // Use the delta provided by the window/host (SDL relative mode)
            MouseDelta = _pendingRelativeDelta;
            _pendingRelativeDelta = Vector2.Zero;
        }
        else
        {
            // Compute delta from absolute mouse pos
            if (_hasLastMousePos)
                MouseDelta = MousePosition - _lastMousePos;
            else
                MouseDelta = Vector2.Zero;

            _lastMousePos = MousePosition;
            _hasLastMousePos = true;

            // Safety: drop any pending relative delta while not in relative mode
            _pendingRelativeDelta = Vector2.Zero;
        }
    }
}

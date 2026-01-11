using System.Numerics;
using Veldrid;

namespace Game;

public sealed class InputState
{
    private readonly HashSet<Key> _down = new();
    private readonly HashSet<Key> _pressedThisFrame = new();

    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseDelta { get; private set; }
    public bool RightMouseDown { get; private set; }

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressedThisFrame.Contains(key);

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

        foreach (var me in snapshot.MouseEvents)
        {
            if (me.MouseButton == MouseButton.Right)
                RightMouseDown = me.Down;
        }

        MousePosition = snapshot.MousePosition;
    }


}

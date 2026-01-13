using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using static Veldrid.Sdl2.Sdl2Native;

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

    public ActiveInputDevice ActiveDevice { get; private set; } = ActiveInputDevice.KeyboardMouse;

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressedThisFrame.Contains(key);

    public void SetRelativeMouseDelta(Vector2 delta) => _pendingRelativeDelta += delta;

    public void ClearMouseDelta()
    {
        MouseDelta = Vector2.Zero;
        _pendingRelativeDelta = Vector2.Zero;
    }

    private IntPtr _controller; 
    private int _controllerIndex = -1;

    private readonly Dictionary<Engine.Input.Actions.GamepadButton, bool> _padDown = new();
    private readonly Dictionary<Engine.Input.Actions.GamepadButton, bool> _padPressed = new();
    private readonly Dictionary<Engine.Input.Actions.GamepadAxis, float> _axes = new();

    public bool HasGamepad => _controller != IntPtr.Zero;

    private int _lastJoystickCount = -1;
    private double _nextScanLogTime;
    private readonly Stopwatch _scanTimer = Stopwatch.StartNew();

    public void UpdateGamepads()
    {
        int num = SDL_NumJoysticks();
        double now = _scanTimer.Elapsed.TotalSeconds;

        if (num != _lastJoystickCount || now >= _nextScanLogTime)
        {
            _lastJoystickCount = num;
            _nextScanLogTime = now + 2.0;

            Console.WriteLine($"[Input] SDL_NumJoysticks: {num}");

            for (int i = 0; i < num; i++)
            {
                bool isGc = SDL_IsGameController(i);
                Console.WriteLine($"[Input]  idx {i}: SDL_IsGameController={isGc}");
            }

            if (_controller != IntPtr.Zero)
                Console.WriteLine($"[Input] Active controller index: {_controllerIndex}");
        }

        if (_controller != IntPtr.Zero)
        {
            foreach (var k in _padPressed.Keys.ToList())
                _padPressed[k] = false;

            UpdateButton(Engine.Input.Actions.GamepadButton.A, SDL_GameControllerButton.A);
            UpdateButton(Engine.Input.Actions.GamepadButton.B, SDL_GameControllerButton.B);
            UpdateButton(Engine.Input.Actions.GamepadButton.X, SDL_GameControllerButton.X);
            UpdateButton(Engine.Input.Actions.GamepadButton.Y, SDL_GameControllerButton.Y);

            UpdateButton(Engine.Input.Actions.GamepadButton.Back, SDL_GameControllerButton.Back);
            UpdateButton(Engine.Input.Actions.GamepadButton.Start, SDL_GameControllerButton.Start);

            UpdateButton(Engine.Input.Actions.GamepadButton.LeftShoulder, SDL_GameControllerButton.LeftShoulder);
            UpdateButton(Engine.Input.Actions.GamepadButton.RightShoulder, SDL_GameControllerButton.RightShoulder);

            UpdateButton(Engine.Input.Actions.GamepadButton.LeftStick, SDL_GameControllerButton.LeftStick);
            UpdateButton(Engine.Input.Actions.GamepadButton.RightStick, SDL_GameControllerButton.RightStick);

            UpdateButton(Engine.Input.Actions.GamepadButton.DpadUp, SDL_GameControllerButton.DPadUp);
            UpdateButton(Engine.Input.Actions.GamepadButton.DpadDown, SDL_GameControllerButton.DPadDown);
            UpdateButton(Engine.Input.Actions.GamepadButton.DpadLeft, SDL_GameControllerButton.DPadLeft);
            UpdateButton(Engine.Input.Actions.GamepadButton.DpadRight, SDL_GameControllerButton.DPadRight);

            _axes[Engine.Input.Actions.GamepadAxis.LeftX] = ReadAxis(SDL_GameControllerAxis.LeftX);
            _axes[Engine.Input.Actions.GamepadAxis.LeftY] = ReadAxis(SDL_GameControllerAxis.LeftY);
            _axes[Engine.Input.Actions.GamepadAxis.RightX] = ReadAxis(SDL_GameControllerAxis.RightX);
            _axes[Engine.Input.Actions.GamepadAxis.RightY] = ReadAxis(SDL_GameControllerAxis.RightY);

            _axes[Engine.Input.Actions.GamepadAxis.TriggerLeft] = ReadTrigger(SDL_GameControllerAxis.TriggerLeft);
            _axes[Engine.Input.Actions.GamepadAxis.TriggerRight] = ReadTrigger(SDL_GameControllerAxis.TriggerRight);

            bool anyPressed = _padPressed.Values.Any(v => v);
            bool anyAxis =
                MathF.Abs(_axes[Engine.Input.Actions.GamepadAxis.LeftX]) > 0.2f ||
                MathF.Abs(_axes[Engine.Input.Actions.GamepadAxis.LeftY]) > 0.2f ||
                MathF.Abs(_axes[Engine.Input.Actions.GamepadAxis.RightX]) > 0.2f ||
                MathF.Abs(_axes[Engine.Input.Actions.GamepadAxis.RightY]) > 0.2f;

            if (anyPressed || anyAxis)
                ActiveDevice = ActiveInputDevice.Gamepad;

            return;
        }

        if (num <= 0)
            return;

        for (int i = 0; i < num; i++)
        {
            bool isGc = SDL_IsGameController(i);
            if (!isGc) continue;

            IntPtr c = SDL_GameControllerOpen(i);
            if (c != IntPtr.Zero)
            {
                _controller = c;
                _controllerIndex = i;

                InitPadDictionaries();

                Console.WriteLine($"[Input] Gamepad OPENED at index {i} (SDL_IsGameController={isGc})");
                ActiveDevice = ActiveInputDevice.Gamepad;
                break;
            }
        }
    }

    private void InitPadDictionaries()
    {
        foreach (Engine.Input.Actions.GamepadButton b in Enum.GetValues(typeof(Engine.Input.Actions.GamepadButton)))
        {
            _padDown[b] = false;
            _padPressed[b] = false;
        }

        foreach (Engine.Input.Actions.GamepadAxis a in Enum.GetValues(typeof(Engine.Input.Actions.GamepadAxis)))
        {
            _axes[a] = 0f;
        }
    }

    private void UpdateButton(Engine.Input.Actions.GamepadButton b, SDL_GameControllerButton sdlButton)
    {
        bool was = _padDown[b];
        bool now = SDL_GameControllerGetButton(_controller, sdlButton) != 0;
        _padDown[b] = now;
        _padPressed[b] = !was && now;
    }

    private float ReadAxis(SDL_GameControllerAxis axis)
    {
        short v = SDL_GameControllerGetAxis(_controller, axis);
        float f = v / 32767f;
        return Math.Clamp(f, -1f, 1f);
    }

    private float ReadTrigger(SDL_GameControllerAxis axis)
    {
        short v = SDL_GameControllerGetAxis(_controller, axis);
        float f = v / 32767f;
        return Math.Clamp(f, 0f, 1f);
    }

    public bool GetGamepadDown(Engine.Input.Actions.GamepadButton b) =>
        _controller != IntPtr.Zero && _padDown.TryGetValue(b, out var d) && d;

    public bool GetGamepadPressed(Engine.Input.Actions.GamepadButton b) =>
        _controller != IntPtr.Zero && _padPressed.TryGetValue(b, out var p) && p;

    public float GetAxis(Engine.Input.Actions.GamepadAxis a) =>
        _controller != IntPtr.Zero && _axes.TryGetValue(a, out var v) ? v : 0f;

    public Vector2 GetStick(Engine.Input.Actions.GamepadStick stick, bool invertY)
    {
        float x = stick == Engine.Input.Actions.GamepadStick.Left
            ? GetAxis(Engine.Input.Actions.GamepadAxis.LeftX)
            : GetAxis(Engine.Input.Actions.GamepadAxis.RightX);

        float y = stick == Engine.Input.Actions.GamepadStick.Left
            ? GetAxis(Engine.Input.Actions.GamepadAxis.LeftY)
            : GetAxis(Engine.Input.Actions.GamepadAxis.RightY);

        if (invertY) y = -y;
        return new Vector2(x, y);
    }

    public void Update(InputSnapshot snapshot)
    {
        _pressedThisFrame.Clear();

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
            MouseDelta = _pendingRelativeDelta;
            _pendingRelativeDelta = Vector2.Zero;
        }
        else
        {
            if (_hasLastMousePos)
                MouseDelta = MousePosition - _lastMousePos;
            else
                MouseDelta = Vector2.Zero;

            _lastMousePos = MousePosition;
            _hasLastMousePos = true;

            _pendingRelativeDelta = Vector2.Zero;
        }

        // Active device switching based on KB/M usage
        if (_pressedThisFrame.Count > 0)
            ActiveDevice = ActiveInputDevice.KeyboardMouse;

        if (!RelativeMouseMode && MouseDelta.LengthSquared() > 0.01f)
            ActiveDevice = ActiveInputDevice.KeyboardMouse;
    }
}

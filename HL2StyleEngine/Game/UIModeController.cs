using Engine.Platform;
using Engine.Input;
using Engine.Input.Actions;

namespace Game;

public sealed class UIModeController
{
    private readonly GameWindow _window;

    // Optional: lets us “flush” actions after toggling UI, so you don’t get a stuck key edge.
    private readonly InputSystem? _inputSystem;

    public bool IsUIOpen { get; private set; }
    public bool IsMouseCaptured => !IsUIOpen;

    public UIModeController(GameWindow window, InputSystem? inputSystem, bool startInGameplay = true)
    {
        _window = window;
        _inputSystem = inputSystem;

        IsUIOpen = !startInGameplay;
        ApplyMouseState();
    }

    public void OpenUI()
    {
        if (IsUIOpen) return;
        IsUIOpen = true;
        ApplyMouseState();
    }

    public void CloseUI()
    {
        if (!IsUIOpen) return;
        IsUIOpen = false;
        ApplyMouseState();
    }

    public void ToggleUI()
    {
        IsUIOpen = !IsUIOpen;
        ApplyMouseState();
    }

    private void ApplyMouseState()
    {
        bool capture = !IsUIOpen;

        _window.SetMouseCaptured(capture);

        // In relative mode, warping isn't necessary, but harmless if you like it.
        // If you ever get weird camera jumps, comment this out.
        _window.WarpMouseToCenter();

        // Optional “flush” so the frame after toggling UI doesn’t read stale action edges.
        _inputSystem?.Update();
    }
}

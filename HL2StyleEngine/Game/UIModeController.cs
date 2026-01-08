using Engine.Platform;

namespace Game;

public sealed class UIModeController
{
    private readonly GameWindow _window;
    private readonly InputState _input;

    public bool IsUIOpen { get; private set; }
    public bool IsMouseCaptured => !IsUIOpen;

    public UIModeController(GameWindow window, InputState input, bool startInGameplay = true)
    {
        _window = window;
        _input = input;

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
        _window.WarpMouseToCenter();

        _input.OverrideMouseDelta(System.Numerics.Vector2.Zero);
    }
}

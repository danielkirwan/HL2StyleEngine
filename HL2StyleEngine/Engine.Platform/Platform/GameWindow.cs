using System;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using static Veldrid.Sdl2.Sdl2Native;

namespace Engine.Platform;

public sealed class GameWindow
{
    public Sdl2Window Window { get; }
    private Vector2 _cachedMouseDelta;

    public GameWindow(int width, int height, string title)
    {
        var wci = new WindowCreateInfo
        {
            X = 100,
            Y = 100,
            WindowWidth = width,
            WindowHeight = height,
            WindowTitle = title
        };

        Window = VeldridStartup.CreateWindow(ref wci);
        Maximize();

    }

    public InputSnapshot PumpEvents()
    {
        var snapshot = Window.PumpEvents();

        _cachedMouseDelta = Window.MouseDelta;

        return snapshot;
    }

    public void SetMouseCaptured(bool captured)
    {
        Window.CursorVisible = !captured;

        SDL_SetWindowGrab(Window.SdlWindowHandle, captured);
        SDL_SetRelativeMouseMode(captured);
    }

    public Vector2 ConsumeRelativeMouseDelta()
    {
        var d = _cachedMouseDelta;
        _cachedMouseDelta = Vector2.Zero;
        return d;
    }

    public void Maximize()
    {
        SDL_MaximizeWindow(Window.SdlWindowHandle);
    }

    public Vector2 GetWindowCenter()
        => new(Window.Width / 2f, Window.Height / 2f);

    public void WarpMouseToCenter()
    {
        var c = GetWindowCenter();
        Window.SetMousePosition((int)c.X, (int)c.Y);
    }
}

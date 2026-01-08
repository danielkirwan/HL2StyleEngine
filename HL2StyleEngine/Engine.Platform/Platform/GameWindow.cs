using System.Numerics;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Engine.Platform;

public sealed class GameWindow
{
    public Sdl2Window Window { get; }

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
    }

    public void SetMouseCaptured(bool captured)
    {
        Window.CursorVisible = !captured;
    }

    public Vector2 GetWindowCenter()
        => new(Window.Width / 2f, Window.Height / 2f);

    public void WarpMouseToCenter()
    {
        var c = GetWindowCenter();
        Window.SetMousePosition((int)c.X, (int)c.Y);
    }
}

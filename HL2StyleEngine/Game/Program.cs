namespace Game;

internal static class Program
{
    private static void Main()
    {
        using var app = new GameApp();
        app.Run();
    }
}

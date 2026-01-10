using Engine.Runtime.Hosting;

namespace Game;

internal static class Program
{
    private static void Main()
    {
        using var host = new EngineHost(1280, 720, "HL2-Style Engine (Starter)");
        using var game = new HL2GameModule();
        host.Run(game);
    }
}

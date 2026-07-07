using Engine.Runtime.Hosting;

namespace Game;

internal static class Program
{
    private static void Main(string[] args)
    {
        using var host = new EngineHost(1280, 720, "HL2-Style Engine (Starter)");
        using var game = new HL2GameModule(ParseLevelArgument(args));
        host.Run(game);
    }

    private static string? ParseLevelArgument(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--level", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                return args[i + 1];
        }

        return null;
    }
}

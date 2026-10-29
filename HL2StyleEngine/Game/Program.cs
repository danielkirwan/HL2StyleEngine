using Engine.Runtime.Hosting;
using Engine.Core.Diagnostics;
using Engine.Render;

namespace Game;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? Value(string key) => args.SkipWhile(a => a != key).Skip(1).FirstOrDefault();
        var profile = Value("--load-report") is { } report ? new LoadProfile(report, args.Contains("--load-probe")) : null;
        try
        {
            string? level = ParseLevelArgument(args), assetRoot = null;
            AssetPackage? package = null;
            if (Value("--asset-package") is { } manifest)
            {
                assetRoot = Path.GetDirectoryName(Path.GetFullPath(manifest));
                try
                {
                    using var scope = profile?.Measure("package-manifest");
                    var loaded = new AssetPackage(manifest);
                    if (level != null && !Path.GetFullPath(level).Equals(loaded.LevelPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The selected level differs from the package level.");
                    level = loaded.LevelPath;
                    if (!args.Contains("--source-assets")) package = loaded;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
                {
                    Console.Error.WriteLine("[Loading] Package rejected; strict source loading: " + ex.Message);
                    // A damaged manifest cannot choose a fallback level for us.
                    if (level == null || !File.Exists(level)) throw;
                }
            }
            using var host = new EngineHost(1280, 720, "HL2-Style Engine (Starter)", profile);
            using var game = new HL2GameModule(level, assetRoot, package);
            game.PackageLightingEnabled = !args.Contains("--source-lighting");
            host.Run(game);
            return profile?.ExitWhenComplete == true && !profile.Succeeded ? 1 : 0;
        }
        catch (Exception ex)
        {
            profile?.Finish("startup-failed", new { Error = ex.ToString() });
            Console.Error.WriteLine(ex);
            return 1;
        }
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

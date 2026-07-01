namespace Engine.AssetImporter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            RunCommandLineAsync(args).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new AssetImporterForm());
    }

    private static async Task RunCommandLineAsync(string[] args)
    {
        Dictionary<string, string> values = ParseArgs(args);
        if (!values.TryGetValue("source", out string? source) ||
            !values.TryGetValue("destination", out string? destination) ||
            (!values.TryGetValue("name", out string? name) && !values.ContainsKey("all")))
        {
            Console.Error.WriteLine("Usage: Engine.AssetImporter --source <folder> --destination <folder> --name <outputName> [--blender <blender.exe>] [--all] [--mode model|animation] [--animations]");
            Environment.ExitCode = 2;
            return;
        }

        values.TryGetValue("blender", out string? blender);
        bool convertAll = values.ContainsKey("all");
        FbxImportMode importMode = ResolveImportMode(values);

        FbxConversionResult result = await FbxToGlbConverter.ConvertAsync(new FbxImportRequest
        {
            SourceFolder = source,
            DestinationFolder = destination,
            OutputName = name ?? "",
            BlenderExePath = blender,
            ConvertAllFbx = convertAll,
            ImportMode = importMode
        });

        Console.WriteLine(result.Log);
        Environment.ExitCode = result.Success ? 0 : 1;
    }

    private static FbxImportMode ResolveImportMode(Dictionary<string, string> values)
    {
        if (values.ContainsKey("animations") || values.ContainsKey("animation"))
            return FbxImportMode.Animation;

        if (!values.TryGetValue("mode", out string? mode))
            return FbxImportMode.Model;

        return mode.Equals("animation", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("animations", StringComparison.OrdinalIgnoreCase) ||
               mode.Equals("anim", StringComparison.OrdinalIgnoreCase)
            ? FbxImportMode.Animation
            : FbxImportMode.Model;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                continue;

            string normalizedKey = key[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[normalizedKey] = "true";
                continue;
            }

            values[normalizedKey] = args[++i];
        }

        return values;
    }
}
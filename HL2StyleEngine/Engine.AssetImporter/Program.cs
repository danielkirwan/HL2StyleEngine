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
            !values.TryGetValue("name", out string? name))
        {
            Console.Error.WriteLine("Usage: Engine.AssetImporter --source <folder> --destination <folder> --name <outputName> [--blender <blender.exe>]");
            Environment.ExitCode = 2;
            return;
        }

        values.TryGetValue("blender", out string? blender);
        FbxConversionResult result = await FbxToGlbConverter.ConvertAsync(new FbxImportRequest
        {
            SourceFolder = source,
            DestinationFolder = destination,
            OutputName = name,
            BlenderExePath = blender
        });

        Console.WriteLine(result.Log);
        Environment.ExitCode = result.Success ? 0 : 1;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (i + 1 >= args.Length)
                break;

            values[key[2..]] = args[++i];
        }

        return values;
    }
}

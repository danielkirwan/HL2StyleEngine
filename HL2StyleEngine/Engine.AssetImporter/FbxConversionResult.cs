namespace Engine.AssetImporter;

internal sealed class FbxConversionResult
{
    public FbxConversionResult(bool success, string? outputPath, string log, IReadOnlyList<string>? outputPaths = null)
    {
        Success = success;
        OutputPath = outputPath;
        Log = log;
        OutputPaths = outputPaths ?? (outputPath == null ? Array.Empty<string>() : new[] { outputPath });
    }

    public bool Success { get; }
    public string? OutputPath { get; }
    public IReadOnlyList<string> OutputPaths { get; }
    public string Log { get; }
}

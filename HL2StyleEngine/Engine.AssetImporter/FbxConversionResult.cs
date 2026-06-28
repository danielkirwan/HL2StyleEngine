namespace Engine.AssetImporter;

internal sealed class FbxConversionResult
{
    public FbxConversionResult(bool success, string? outputPath, string log)
    {
        Success = success;
        OutputPath = outputPath;
        Log = log;
    }

    public bool Success { get; }
    public string? OutputPath { get; }
    public string Log { get; }
}

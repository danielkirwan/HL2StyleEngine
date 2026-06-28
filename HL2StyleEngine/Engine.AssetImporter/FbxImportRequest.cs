namespace Engine.AssetImporter;

internal sealed class FbxImportRequest
{
    public required string SourceFolder { get; init; }
    public required string DestinationFolder { get; init; }
    public required string OutputName { get; init; }
    public string? BlenderExePath { get; init; }
}

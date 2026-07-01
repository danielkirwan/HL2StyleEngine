namespace Engine.AssetImporter;

internal enum FbxImportMode
{
    Model,
    Animation
}

internal sealed class FbxImportRequest
{
    public required string SourceFolder { get; init; }
    public required string DestinationFolder { get; init; }
    public required string OutputName { get; init; }
    public string? BlenderExePath { get; init; }
    public bool ConvertAllFbx { get; init; }
    public FbxImportMode ImportMode { get; init; } = FbxImportMode.Model;
}
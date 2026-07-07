using System.Text.Json;

namespace HS2Editor;

internal static class EditorJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

internal sealed class EditorProjectSettings
{
    public string ProjectName { get; set; } = "HL2StyleEngine";
    public string ContentRoot { get; set; } = "Game/Content";
    public string StartupLevel { get; set; } = "Game/Content/Levels/interaction_test.json";
    public List<string> RecentLevels { get; set; } = new();
    public List<string> RecentAssets { get; set; } = new();
    public string BlenderExecutablePath { get; set; } = "";
    public string AssetImporterProject { get; set; } = "Engine.AssetImporter/Engine.AssetImporter.csproj";
    public string GameProject { get; set; } = "Game/Game.csproj";
    public Dictionary<string, string> Preferences { get; set; } = new();

    public static EditorProjectSettings LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                EditorProjectSettings? loaded = JsonSerializer.Deserialize<EditorProjectSettings>(File.ReadAllText(path), EditorJson.Options);
                if (loaded != null)
                    return loaded;
            }
            catch
            {
            }
        }

        var settings = new EditorProjectSettings
        {
            BlenderExecutablePath = Environment.GetEnvironmentVariable("HS2_BLENDER_EXE") ?? ""
        };
        settings.Save(path);
        return settings;
    }

    public void AddRecentLevel(string level)
    {
        RecentLevels.RemoveAll(item => string.Equals(item, level, StringComparison.OrdinalIgnoreCase));
        RecentLevels.Insert(0, level);
        if (RecentLevels.Count > 12)
            RecentLevels.RemoveRange(12, RecentLevels.Count - 12);
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, EditorJson.Options));
    }
}

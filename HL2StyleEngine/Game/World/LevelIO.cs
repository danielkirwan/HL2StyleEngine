using System;
using System.IO;
using System.Text.Json;

namespace Game.World;

public static class LevelIO
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static LevelFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Level file not found: {path}");

        string json = File.ReadAllText(path);
        var level = JsonSerializer.Deserialize<LevelFile>(json, _opts);

        if (level is null)
            throw new InvalidDataException($"Failed to deserialize level: {path}");

        if (level.Boxes is null)
            level.Boxes = new();

        return level;
    }

    public static void Save(string path, LevelFile level)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(level, _opts);
        File.WriteAllText(path, json);
    }

    public static LevelFile LoadOrCreate(string path, Func<LevelFile> createDefault)
    {
        if (File.Exists(path))
            return Load(path);

        var level = createDefault();
        Save(path, level);
        return level;
    }
}

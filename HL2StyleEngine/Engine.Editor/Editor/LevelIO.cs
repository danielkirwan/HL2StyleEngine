using System;
using System.IO;
using System.Text.Json;

namespace Engine.Editor.Level;

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

        level.Entities ??= new();

        if (level.Entities.Count == 0 && level.Boxes is not null && level.Boxes.Count > 0)
        {
            foreach (var b in level.Boxes)
            {
                if (string.IsNullOrWhiteSpace(b.Id))
                    b.Id = Guid.NewGuid().ToString("N");
                level.Entities.Add(new LevelEntityDef
                {
                    Id = b.Id,
                    Type = EntityTypes.Box,
                    Name = b.Name,
                    LocalPosition = b.Position,
                    Size = b.Size,
                    Color = b.Color
                });
            }

            level.Version = Math.Max(level.Version, 2);
        }

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
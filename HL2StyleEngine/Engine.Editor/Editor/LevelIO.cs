using System;
using System.IO;
using Engine.Core.Serialization;

namespace Engine.Editor.Level;

public static class LevelIO
{
    public static LevelFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Level file not found: {path}");

        var level = StructuredData.LoadCached<LevelFile>(path);

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

        StructuredData.Save(path, level);
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

using System.Diagnostics;
using System.Text.Json;

namespace Engine.Editor.Level;

// Snapshot of production LevelIO with a benchmark-only codec switch and timings.
public static class LevelIO
{
    public static List<(string Path, double Milliseconds)> Measurements { get; } = new();

    public static LevelFile Load(string path)
    {
        long start = Stopwatch.GetTimestamp();
        if (!File.Exists(path)) throw new FileNotFoundException($"Level file not found: {path}");
        // Practice-template validation still uses JSON; only the requested scene is switched.
        string codec = string.Equals(Path.GetFullPath(path), Environment.GetEnvironmentVariable("HS2_BENCH_LEVEL"),
            StringComparison.OrdinalIgnoreCase) ? BenchmarkCodec.Selected : "stj";
        var level = BenchmarkCodec.ReadFile(path, codec);
        if (level is null) throw new InvalidDataException($"Failed to deserialize level: {path}");
        level.Entities ??= new();
        if (level.Entities.Count == 0 && level.Boxes is not null && level.Boxes.Count > 0)
        {
            foreach (var b in level.Boxes)
            {
                if (string.IsNullOrWhiteSpace(b.Id)) b.Id = Guid.NewGuid().ToString("N");
                level.Entities.Add(new LevelEntityDef
                {
                    Id = b.Id, Type = EntityTypes.Box, Name = b.Name,
                    LocalPosition = b.Position, Size = b.Size, Color = b.Color
                });
            }
            level.Version = Math.Max(level.Version, 2);
        }
        Measurements.Add((path, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        return level;
    }

    public static void Save(string path, LevelFile level)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(level, BenchmarkCodec.Options));
    }

    public static LevelFile LoadOrCreate(string path, Func<LevelFile> createDefault)
    {
        if (File.Exists(path)) return Load(path);
        var level = createDefault();
        Save(path, level);
        return level;
    }
}

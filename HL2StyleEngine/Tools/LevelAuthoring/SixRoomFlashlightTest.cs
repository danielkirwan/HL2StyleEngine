using System.Numerics;
using System.Text.Json;
using Engine.Editor.Level;

internal static class SixRoomFlashlightTest
{
    internal const string FileName = "sixRoomFlashlightTest.json";
    internal const float AmbientLight = .025f;

    private static LevelFile Create(string root)
    {
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
        level.UsePointLights = false;
        level.AmbientLight = AmbientLight;
        level.DirectionalLight = 0;
        level.EnableShadows = true;
        level.Entities.RemoveAll(e => e.Type == EntityTypes.PointLight);
        // These unlit primitive strips would otherwise look powered even without scene lights.
        foreach (var entity in level.Entities.Where(e => e.Name?.StartsWith("FixtureDiffuser_", StringComparison.Ordinal) == true))
            entity.Color = new Vector4(0, 0, 0, 1);
        return level;
    }

    internal static void Build(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        if (File.Exists(path)) throw new IOException("Flashlight test already exists; preserve editor changes instead of regenerating it.");
        LevelIO.Save(path, Create(root));
        Console.WriteLine($"Created {path}; original sixRoomTest.json unchanged.");
    }

    internal static void Validate(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        var level = LevelIO.Load(path);
        if (JsonSerializer.Serialize(level) != JsonSerializer.Serialize(Create(root)))
            throw new InvalidDataException("Flashlight fixture differs from the original beyond its intended lighting changes.");
        if (level.Entities.Count(e => e.Interaction?.Kind == "PuzzleIndicator") != 13)
            throw new InvalidDataException("Expected all 13 red/green puzzle indicators.");
        SixRoomTest.Validate(root, path, flashlightOnly: true);
        PuzzleChecks.Run(root, FileName);
        LevelRenderingChecks.Run(root, flashlightOnly: true);
    }
}

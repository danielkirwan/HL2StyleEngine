using System.Numerics;
using Engine.Render;
using Engine.Editor.Level;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (args.Length > 0 && args[0] == "preview-inventory")
{
    Environment.SetEnvironmentVariable("HS2_RMLUI_NATIVE_PRESENTATION", args.Contains("--native") ? "1" : "0");
    int width = args.Contains("--small") ? 800 : 1280;
    int height = args.Contains("--small") ? 600 : 720;
    using var host = new Engine.Runtime.Hosting.EngineHost(width, height, "Inventory QA");
    using var preview = new InventoryPreview(root);
    host.Run(preview);
    return;
}
if (args.Length == 1 && args[0] == "build-inventory-thumbnails")
{
    InventoryThumbnails.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "validate-redox-inventory")
{
    PersistenceInventoryChecks.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "validate-inventory-input")
{
    try { InventoryInputChecks.Run(root); }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Length == 1 && args[0] == "validate-six-room-puzzles")
{
    PuzzleChecks.Run(root);
    return;
}
if (args.Length == 1 && args[0] == "add-six-room-puzzles")
{
    string path = Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
    var level = LevelIO.Load(path);
    SixRoomPuzzles.AddTo(root, level);
    LevelIO.Save(path, level);
    Console.WriteLine($"Added puzzles to existing layout: {level.Entities.Count} entities.");
    return;
}
if (args.Length == 1 && args[0] == "validate-collider-bounds")
{
    CollisionBoundsChecks.Run();
    return;
}
if (args.Length > 0 && args[0] == "build-six-room-test")
{
    string path = Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
    if (File.Exists(path) && !args.Contains("--replace-generated"))
        throw new IOException("Level already exists. Keep editor changes; generate into a fresh checkout to recreate the initial layout.");
    LevelIO.Save(path, SixRoomTest.Build());
    SixRoomTest.Validate(root, path);
    return;
}
if (args.Length == 1 && args[0] == "validate-six-room-test")
{
    SixRoomTest.Validate(root, Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
    PuzzleChecks.Run(root);
    return;
}
foreach (string name in args)
{
    var model = GlbModelLoader.Load(Path.Combine(root, "Game/Content/Models/ViewModels", name + ".glb"));
    Vector3 min = new(float.MaxValue), max = new(float.MinValue);
    foreach (var part in model.Parts)
        foreach (var position in part.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
    Console.WriteLine($"{name}: min={min}, max={max}, size={max - min}, parts={model.Parts.Count}, textured={model.Parts.Count(p => p.BaseColorPng != null)}");
}

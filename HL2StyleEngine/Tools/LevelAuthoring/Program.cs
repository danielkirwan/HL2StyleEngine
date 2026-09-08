using System.Numerics;
using Engine.Render;
using Engine.Editor.Level;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
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

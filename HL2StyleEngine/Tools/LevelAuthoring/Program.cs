using System.Numerics;
using Engine.Render;
using Engine.Editor.Level;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (args is ["validate-visibility-streaming"])
{
    try { VisibilityStreamingChecks.Run(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-gameplay-flashlight", ..])
{
    try
    {
        if (args.Length < 2 || args[1] is not ("on" or "off") ||
            args.Skip(2).Any(a => a is not ("--individual-uploads" or "--reference-visibility" or "--stress")))
            throw new ArgumentException("Use benchmark-gameplay-flashlight on|off [--individual-uploads] [--reference-visibility] [--stress].");
        GameplayFrameChecks.Run(root, args[1] == "on", args.Contains("--individual-uploads"), args.Contains("--reference-visibility"), args.Contains("--stress"));
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-flashlight"])
{
    try { LevelRenderingChecks.Run(root, flashlightOnly: true, profileFlashlight: true); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["build-six-room-flashlight-test"] or ["validate-six-room-flashlight-test"])
{
    try
    {
        if (args[0].StartsWith("build-", StringComparison.Ordinal)) SixRoomFlashlightTest.Build(root);
        else SixRoomFlashlightTest.Validate(root);
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args is ["benchmark-level-ready", var readyMode] && readyMode is "glb" or "cooked")
{
    try
    {
        CookedModelCache.Enabled = readyMode == "cooked";
        LevelRenderingChecks.Run(root, benchmarkOnly: true);
        Console.WriteLine("Fresh process, warm filesystem. Includes engine/window initialization, asset preparation/upload and GPU readback; excludes OS process launch, user input/playability checks and image encoding.");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length > 0 && args[0] is "validate-model-cache" or "validate-model-cache-load" or "benchmark-models")
{
    try
    {
        if (args is ["validate-model-cache"]) ModelCookingChecks.Run(root);
        else if (args is ["validate-model-cache-load", var path, var expected]) ModelCookingChecks.CheckChild(path, expected == "cooked");
        else if (args is ["benchmark-models", var mode] && mode is "cooked" or "glb") ModelCookingChecks.Benchmark(root, mode == "cooked");
        else throw new ArgumentException("Use validate-model-cache or benchmark-models cooked|glb.");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-lighting-state")
{
    try { LightingChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 2 && args[0] == "benchmark-textures" && args[1] is "cooked" or "rgba")
{
    TextureLoadingBenchmark.Run(root, args[1] == "cooked");
    return;
}
if (args.Length == 1 && args[0] == "validate-level-rendering")
{
    try { LevelRenderingChecks.Run(root); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-rendering")
{
    try { RenderPipelineChecks.Run(); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length == 1 && args[0] == "validate-engine-upgrades")
{
    EngineUpgradeChecks.Run(root);
    return;
}
if (args.Length > 0 && args[0] == "cook-assets")
{
    string folder = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "Game/Content/Models");
    var files = Directory.GetFiles(folder, "*.glb", SearchOption.AllDirectories).Order().ToArray();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    int failures = 0;
    for (int i = 0; i < files.Length; i++)
    {
        try { TextureCooker.CookModel(files[i]); Console.WriteLine($"[{i + 1}/{files.Length}] {Path.GetFileName(files[i])}"); }
        catch (Exception ex) { failures++; Console.Error.WriteLine($"FAILED {files[i]}: {ex.Message}"); }
    }
    Console.WriteLine($"Cook: {timer.Elapsed.TotalSeconds:F1}s; {CookedModelCache.CookedModels} new model caches, {TextureCooker.CookedTextures} new textures, {TextureCooker.CacheHits} texture cache hits, {failures} failures. Source GLBs unchanged.");
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}
if (args.Length == 1 && args[0] == "validate-inventory-visuals")
{
    InventoryVisualChecks.Run(root);
    return;
}
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

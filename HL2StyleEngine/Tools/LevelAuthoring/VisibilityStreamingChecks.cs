using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Game;
using Game.Inventory;
using Game.Streaming;

internal static class VisibilityStreamingChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
    private static void Set(object obj, string name, object? value) => obj.GetType().GetField(name, Flags)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj, args);
    private static void Require(bool pass, string message) { if (!pass) throw new InvalidDataException(message); }

    internal static void Run(string root)
    {
        CheckBoundsAndManifest();
        using var host = new EngineHost(1280, 720, "Visibility and room residency QA");
        using var game = new HL2GameModule(Path.Combine(root, "Game/Content/Levels", SixRoomFlashlightTest.FileName));
        game.Initialize(host.Context);
        var renderer = host.Context.Renderer;
        var world = Field<BasicWorldRenderer>(game, "_world");
        var camera = Field<FpsCamera>(game, "_camera");
        var editor = Field<LevelEditorController>(game, "_editor");
        var entities = Field<List<Entity>>(game, "_runtimeEntities");
        var cache = Field<IDictionary>(game, "_weaponModelCache");
        editor.LevelFile.Streaming = new() { ZoneSize = 8, RetainSeconds = 0, BudgetMiB = 1 };
        string modelA = Path.GetFullPath(Path.Combine(root, "Game/Content/Models/ViewModels/Arcade_Machine_1.glb"));
        string modelB = Path.GetFullPath(Path.Combine(root, "Game/Content/Models/ViewModels/Arcade_Machine_2.glb"));
        var a = Prop("qa-stream-a", modelA, new(0, 1, -3));
        var b = Prop("qa-stream-b", modelB, new(2000, 1, -3));
        entities.Add(a); entities.Add(b);
        Set(game, "_roomAssets", null);
        camera.Position = new(0, 1.65f, 0); camera.Yaw = camera.Pitch = 0;
        Warm();
        Require(Ready(modelA) && !cache.Contains(modelB), $"Distant room loaded eagerly or current room was not prepared: A={Ready(modelA)}, B={cache.Contains(modelB)}, wanted={game.StreamingWantedModels}, waiting={game.StreamingWaiting}, A error={(cache.Contains(modelA) ? cache[modelA]!.GetType().GetField("Error")!.GetValue(cache[modelA]) : "unrequested")}.");
        Require(game.StreamingZoneCount > 2 && game.StreamingManifest!.Zones.Values.Any(paths => paths.Contains(modelB)), "Room dependency manifest omitted a distant asset.");
        Require(game.StreamingOverBudget && Ready(modelA), "A soft budget must not evict the working set.");
        long startBytes = game.StreamingResidentBytes;
        object originalModelA = Model(modelA)!;

        var originalEntities = entities.ToArray();
        var inventory = Field<InventoryContainer>(game, "_inventory");
        inventory.Add(ItemCatalog.DamagedCable, 1);
        Field<HashSet<string>>(game, "_solvedPuzzles").Add("qa-stream-state");
        var lightState = Field<LevelLightState>(game, "_lightState");
        lightState.Restore([new() { EntityId = "qa-stream-light", Enabled = false }]);
        var crate = entities.First(e => e.Damageable); crate.Health = 17;
        var collected = entities.First(e => e.CanPickUp && !e.Damageable);
        collected.Render.Shape = collected.Collider.Shape = RuntimeShapeKind.None;
        var door = entities.First(e => e.Name.Contains("Shutter", StringComparison.OrdinalIgnoreCase));
        Call(game, "MovePuzzleDoorToOpenPosition", door);
        Vector3 doorPosition = door.Transform.Position;

        // A blocked incoming upload must not tear down the old render resources.
        world.UploadBudgetMilliseconds = 0;
        camera.Position = new(2000, 1.65f, 0);
        Frame(); Frame();
        Require(game.StreamingWaiting && ReferenceEquals(originalModelA, Model(modelA)), "Old room released before the next room was GPU-ready.");
        world.UploadBudgetMilliseconds = 2;
        Warm();
        for (int i = 0; i < 80; i++) Frame();
        Require(Ready(modelB) && !cache.Contains(modelA) && game.StreamingEvictions > 0, "Distant assets were not evicted after the transition.");
        long farBytes = game.StreamingResidentBytes;
        Require(farBytes < startBytes, "GPU working-set memory did not fall after leaving the level's populated area.");
        renderer.DrainRetiredResources();
        Require(renderer.PendingRetirementBatches == 0, "GPU retirement fences did not drain.");

        camera.Position = new(0, 1.65f, 0);
        Warm();
        Require(Ready(modelA) && !ReferenceEquals(originalModelA, Model(modelA)), "Returning did not reconstruct evicted resources.");
        Require(entities.SequenceEqual(originalEntities) && entities.Select(e => e.Id).Distinct().Count() == entities.Count,
            "Streaming recreated, removed or duplicated logical entities.");
        Require(crate.Health == 17 && !collected.Render.Enabled && !collected.Collider.Enabled && door.Transform.Position == doorPosition &&
            inventory.Contains(ItemCatalog.DamagedCable) && Field<HashSet<string>>(game, "_solvedPuzzles").Contains("qa-stream-state"),
            "Room reload reset inventory, collected item, door, health or puzzle state.");
        Require(lightState.Capture() is [{ EntityId: "qa-stream-light", Enabled: false }],
            "Room reload reset saved light overrides.");

        // Actual gravity-gun ownership must pin an otherwise distant model.
        var held = entities.First(e => e.CanPickUp && e.Physics.BoxBody != null && e.Render.Enabled && !e.IsBroken);
        Call(game, "PickUp", held, true);
        string heldPath = (string)Call(game, "ResolveModelAssetPath", held.Render.ModelAssetPath)!;
        camera.Position = new(2000, 1.65f, 0);
        Warm();
        for (int i = 0; i < 80; i++) Frame();
        Require(ReferenceEquals(Field<Entity>(game, "_held"), held) && held.IsHeld && Ready(heldPath), "Streaming dropped a held object or released its model.");
        Call(game, "DropHeld");
        Require(!held.IsHeld, "Held-object release failed after streaming.");
        game.AssetStreamingEnabled = false;
        Warm();
        Require(Ready(modelA) && Ready(modelB), "Full-residency reference did not restore both rooms.");
        Console.WriteLine($"PASS: room prefetch, GPU-ready handoff, soft budget, fence-safe eviction/reload, shared resources, gravity-held pins and unchanged entity/puzzle/inventory/door/light/collected state. Owned geometry + shared textures {startBytes / 1048576.0:F1} -> {farBytes / 1048576.0:F1} MiB; {game.StreamingEvictions} evictions.");

        void Frame() { renderer.BeginFrame(); game.RenderWorld(renderer); renderer.ResolveWorldToSwapchain(); renderer.EndFrame(); }
        void Warm()
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Frame();
                if (timer.Elapsed.TotalSeconds > 40) throw new TimeoutException("Streaming dependencies did not become ready.");
                Thread.Sleep(2);
            } while (game.StreamingWaiting || Field<HashSet<string>>(game, "_wantedModels").Any(path => !Ready(path) && !(bool)cache[path]!.GetType().GetField("Failed")!.GetValue(cache[path])!));
        }
        object? Model(string path) => cache.Contains(path) ? cache[path]!.GetType().GetField("Model")!.GetValue(cache[path]) : null;
        bool Ready(string path) => Model(path) != null;
    }

    private static Entity Prop(string id, string model, Vector3 position)
    {
        var entity = new Entity(id, "Prop", id);
        entity.Transform.Position = position;
        entity.Render.ModelAssetPath = model; entity.Render.Shape = RuntimeShapeKind.Box;
        entity.Render.Size = new(1, 2, 1); entity.Render.Color = Vector4.One;
        return entity;
    }

    private static void CheckBoundsAndManifest()
    {
        var random = new Random(901);
        var vp = Matrix4x4.CreateLookAt(new(0, 2, 5), new(0, 1, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 1.5f, .05f, 100);
        var frustum = new ShadowFrustum(vp);
        int visible = 0;
        for (int i = 0; i < 3000; i++)
        {
            float Next() => (float)random.NextDouble();
            var model = Matrix4x4.CreateScale((Next() - .5f) * 12, Next() * 10, Next() * 8) *
                Matrix4x4.CreateFromYawPitchRoll(Next() * 6, Next() * 6, Next() * 6) *
                Matrix4x4.CreateTranslation((Next() - .5f) * 80, (Next() - .5f) * 30, -Next() * 120);
            for (int c = 0; c < 8; c++)
            {
                Vector4 clip = Vector4.Transform(new Vector4((c & 1) - .5f, ((c >> 1) & 1) - .5f, ((c >> 2) & 1) - .5f, 1), model * vp);
                if (clip.W > 0 && MathF.Abs(clip.X) <= clip.W && MathF.Abs(clip.Y) <= clip.W && clip.Z >= 0 && clip.Z <= clip.W)
                { visible++; Require(frustum.IntersectsBounds(new(-.5f), new(.5f), model), "Transformed visible corner was culled."); }
            }
        }
        Require(!frustum.IntersectsBounds(new(-1), new(1), Matrix4x4.CreateTranslation(10000, 0, 0)), "Distant bounds were not rejected.");
        Require(frustum.IntersectsBounds(new(float.NaN), new(1), Matrix4x4.Identity), "Invalid bounds must fail open.");
        var manifest = new RoomAssetManifest([new("shared", new(-1, 0, -1), new(1, 2, 1)), new("far", new(1000), new(1001)),
            new("neighbour", new(20, 0, 0), new(21, 2, 1)), new("second-ring", new(36, 0, 0), new(37, 2, 1)),
            new("large", new(-100000), new(100000))], 16);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        manifest.Gather(Vector3.Zero, 1, paths);
        Require(paths.SetEquals(["shared", "large", "neighbour"]) && manifest.ZoneAt(new(-.1f, 0, -.1f)) == new StreamingZone(-1, -1), "Boundary/global/neighbour dependency mapping failed.");
        paths.Clear();
        manifest.Gather(Vector3.Zero, 2, paths);
        Require(paths.SetEquals(["shared", "large", "neighbour", "second-ring"]), "Neighbour ring setting did not extend the prefetch set.");
        var level = new LevelFile { Streaming = new() { ZoneSize = 24, BudgetMiB = 128, RetainSeconds = 3 } };
        string json = System.Text.Json.JsonSerializer.Serialize(level);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<LevelFile>(json)!;
        Require(loaded.Streaming.ZoneSize == 24 && loaded.Streaming.BudgetMiB == 128 && loaded.Streaming.RetainSeconds == 3,
            "Streaming settings did not round-trip.");
        Console.WriteLine($"PASS: 3000 rotated/non-uniform/negative-scale bounds ({visible} visible corners), offscreen rejection, invalid-bounds fail-open, zone boundaries/shared/global dependencies and settings round-trip.");
    }
}

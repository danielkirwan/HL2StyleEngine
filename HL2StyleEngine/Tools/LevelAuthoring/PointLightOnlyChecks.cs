using System.Collections;
using System.Diagnostics;
using System.Numerics;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class PointLightOnlyChecks
{
    public const string FileName = "sixRoomPointLightTest.json";
    public const string PrefabPath = "Content/Prefabs/CeilingPointLight.prefab.json";
    private const string Tube = "Content/Models/RenderingTests/Sphere_0_4.glb";
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); _checks++; }
    private static string LevelPath(string root) => Path.Combine(root, "Game/Content/Levels", FileName);

    public static void Build(string root)
    {
        string path = LevelPath(root), prefabPath = Path.Combine(root, "Game", PrefabPath);
        if (File.Exists(path) || File.Exists(prefabPath)) throw new IOException("Fixture level/prefab exists; editor changes will not be overwritten.");
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels", ReflectionStreamingChecks.FileName));
        level.AmbientLight = level.DirectionalLight = 0;
        level.ReflectionEnvironment = ReflectionPreset.None;
        level.Bake.Enabled = false;
        level.UsePointLights = level.EnableShadows = true;
        level.Entities.RemoveAll(e => e.Type == EntityTypes.PointLight);
        (string Name, Vector3 Position, Vector3 Colour, float Range)[] lamps =
        [
            ("Hall South", new(0,4.78f,-10), new(1,.86f,.7f), 10.5f),
            ("Hall Centre", new(0,4.78f,0), new(1,.86f,.7f), 10.5f),
            ("Hall North", new(0,4.78f,10), new(1,.86f,.7f), 10.5f),
            ("Freight Store", new(-24,3.28f,-11), new(.7f,.85f,1), 8.5f),
            ("Workshop", new(24,3.28f,-11), new(1,.75f,.5f), 8.5f),
            ("Medical Supplies", new(-24,3.28f,11), new(.7f,1,.85f), 8.5f),
            ("Utility Room", new(24,3.78f,11), new(.65f,.8f,1), 8.5f),
            ("Loading Bay", new(0,3.78f,26), new(1,.8f,.6f), 10.5f)
        ];
        foreach (var lamp in lamps)
            level.Entities.AddRange(Fixture(lamp.Name, lamp.Position, lamp.Colour, lamp.Range));
        var entities = Fixture("Ceiling Point Light", Vector3.Zero, new(1,.86f,.7f), 10.5f);
        PrefabIO.Save(prefabPath, new() { Name = "Ceiling Point Light", RootEntityId = entities[0].Id, Entities = entities });
        LevelIO.Save(path, level);
        Console.WriteLine("Created " + path + " and " + prefabPath + "; existing levels/launchers unchanged. Cook the new room reflections next.");
    }

    internal static List<LevelEntityDef> Fixture(string name, Vector3 position, Vector3 colour, float range)
    {
        string root = Guid.NewGuid().ToString("N"), light = Guid.NewGuid().ToString("N");
        return
        [
            new() { Id = root, Name = name + " Housing", Type = EntityTypes.Prop, LocalPosition = position,
                Size = new(.16f,.15f,1.6f), MeshPath = "Content/Models/ViewModels/Beam_4m.glb", StaticLighting = true, Color = Vector4.One },
            new() { Id = Guid.NewGuid().ToString("N"), Name = name + " Glowing Tube", Type = EntityTypes.Prop, ParentId = root,
                LocalPosition = new(0,-.13f,0), Size = new(.13f,.04f,1.4f), MeshPath = Tube, StaticLighting = true, Color = Vector4.One,
                EmissionOverride = true, EmissionColor = colour, EmissionIntensity = 4, EmissionLightId = light },
            new() { Id = light, Name = name + " Point Light", Type = EntityTypes.PointLight, ParentId = root,
                LocalPosition = new(0,-.28f,0), LightColor = new Vector4(colour,1), Intensity = 6, Range = range,
                BakeIndirect = true, CastShadows = true }
        ];
    }

    public static void Validate(string root)
    {
        string path = LevelPath(root);
        var level = LevelIO.Load(path);
        Check(level.AmbientLight == 0 && level.DirectionalLight == 0 && !level.Bake.Enabled &&
            level.ReflectionEnvironment == ReflectionPreset.None, "Non-fixture fill remains enabled.");
        Check(level.UsePointLights && level.LightingModel == SurfaceLightingModel.Pbr && level.AutomaticRoomReflections &&
            level.ReflectionStreaming?.MaxResidentProbes == 3, "PBR/room streaming defaults changed.");
        var lights = level.Entities.Where(e => e.Type == EntityTypes.PointLight).ToArray();
        Check(lights.Length == 8 && lights.All(e => e.LightEnabled && !e.IsSpotLight && e.CastShadows && e.BakeIndirect && e.ParentId != null),
            "Expected eight fixed point lights attached to visible housings.");
        var original = LevelIO.Load(Path.Combine(root, "Game/Content/Levels", ReflectionStreamingChecks.FileName));
        foreach (var entity in original.Entities.Where(e => e.Type != EntityTypes.PointLight))
            Check(StructuredData.WriteJson(entity).SequenceEqual(StructuredData.WriteJson(level.Entities.Single(e => e.Id == entity.Id))),
                "Existing gameplay/geometry was changed: " + entity.Name);
        foreach (var light in lights)
        {
            var glow = level.Entities.Single(e => e.EmissionLightId == light.Id);
            Check(glow.ParentId == light.ParentId && glow.MeshPath == Tube && glow.EmissionOverride && glow.EmissionIntensity > 0,
                "Fixture lacks a linked visible glow: " + light.Name);
            var state = new LevelLightState();
            state.Restore([new() { EntityId = light.Id, Enabled = false }]);
            Check(LevelLighting.GetEmission(level, glow, state).Resolve(Vector3.Zero,"","","") == Vector3.Zero,
                "Turning off a light leaves its tube glowing.");
        }
        foreach (var probe in level.Entities.Where(e => e.ReflectionProbe != null))
        {
            var snapshot = ReflectionSnapshot.Create(level, path, probe.Id, p => Path.Combine(root,"Game",p));
            Check(snapshot.FixedLightIds.Length == 8 && snapshot.Level.AmbientLight == 0 && snapshot.Level.DirectionalLight == 0,
                "Capture includes old fill or omits fixture lights.");
            Check(ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(path,probe.Id),snapshot.Key()) != null, "Missing/stale point-light capture: " + probe.Name);
        }
        var prefab = PrefabIO.Load(Path.Combine(root,"Game",PrefabPath));
        var editor = new LevelEditorController(); editor.LoadFromMemory("fixture-check.json",new());
        Check(editor.AddPrefabInstance(prefab,PrefabPath) && editor.AddPrefabInstance(prefab,PrefabPath), "Prefab placement failed.");
        var placed = editor.LevelFile.Entities;
        Check(placed.Count == 6 && placed[1].EmissionLightId == placed[2].Id && placed[4].EmissionLightId == placed[5].Id &&
            placed[1].EmissionLightId != placed[4].EmissionLightId && placed[1].ParentId == placed[0].Id, "Prefab links were not independently remapped.");
        Console.WriteLine($"PASS: {_checks} point-light-only level, unchanged gameplay, capture and prefab checks.");
    }

    public static void CheckGpu(string root)
    {
        using var host = new EngineHost(1280,720,"Point-light-only GPU validation");
        var renderer = host.Context.Renderer;
        using var output = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(LevelPath(root));
        game.AssetStreamingEnabled = false; game.Initialize(host.Context);
        Check(!(bool)PbrBaselineChecks.Field(game,"_flashlightOn"), "Flashlight must start off.");
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game,"_editor");
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");
        var session = (ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");
        var state = (LevelLightState)PbrBaselineChecks.Field(game,"_lightState");
        var models = (IDictionary)PbrBaselineChecks.Field(game,"_weaponModelCache");
        var timer = Stopwatch.StartNew();
        world.ViewCullingEnabled = false;
        do
        {
            output.Frame(() => game.RenderWorld(renderer),read:false);
            if (session.Ready && !session.Busy && models.Count > 0 && models.Values.Cast<object>().All(e => PbrBaselineChecks.Field(e,"Model") != null)) break;
            if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException(session.Status);
            Thread.Sleep(5);
        } while (true);
        world.ViewCullingEnabled = true;
        string folder = Path.Combine(AppContext.BaseDirectory,"qa","point-lights-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        uint[] lit = output.Frame(() => game.RenderWorld(renderer));
        state.Restore(editor.LevelFile.Entities.Where(e => e.Type == EntityTypes.PointLight).Select(e => new LightStateOverride { EntityId = e.Id, Enabled = false }));
        uint[] dark = output.Frame(() => game.RenderWorld(renderer));
        Check(session.Status.Contains("incompatible lights use black fallback"), "Disabled fixed lights retained compatible reflections.");
        state.Clear();
        uint[] restored = output.Frame(() => game.RenderWorld(renderer));
        Check(world.ReflectionReady && !session.Status.Contains("black fallback"), "Restored lamps did not restore valid reflections.");
        int affected = 0, visibleWhenOff = 0, maxRestoreError = 0;
        for (int i = 0; i < lit.Length; i++)
        {
            int difference = 0;
            for (int shift = 0; shift < 24; shift += 8)
            {
                difference += (int)((lit[i] >> shift) & 255) - (int)((dark[i] >> shift) & 255);
                maxRestoreError = Math.Max(maxRestoreError,Math.Abs((int)((lit[i] >> shift) & 255) - (int)((restored[i] >> shift) & 255)));
            }
            if (difference > 30) affected++;
            if ((dark[i] & 255) > 3 || ((dark[i] >> 8) & 255) > 3 || ((dark[i] >> 16) & 255) > 3) visibleWhenOff++;
        }
        Check(affected > lit.Length / 10, "Fixture lights did not illuminate enough visible surfaces.");
        Check(visibleWhenOff < lit.Length / 100, "Unwanted fill remains when the fixtures are off.");
        Check(maxRestoreError <= 1, "Light on/off/on did not restore the same image.");
        RenderPipelineChecks.SavePng(lit,output.Width,output.Height,Path.Combine(folder,"lamps-on.png"));
        RenderPipelineChecks.SavePng(dark,output.Width,output.Height,Path.Combine(folder,"lamps-off.png"));
        Console.WriteLine($"PASS: actual game GPU on/off/on; {affected} lit pixels; restoration max {maxRestoreError}/255. Images: {folder}");
    }
}

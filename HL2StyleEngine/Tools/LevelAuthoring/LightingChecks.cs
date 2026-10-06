using System.Numerics;
using System.Reflection;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Game;

internal static class LightingChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidDataException(message);
        _checks++;
    }
    private static T Field<T>(HL2GameModule module, string name)
        => (T)typeof(HL2GameModule).GetField(name, Flags)!.GetValue(module)!;
    private static object? Call(HL2GameModule module, string name, params object[] args)
        => typeof(HL2GameModule).GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(module, args);

    public static void Run()
    {
        var a = Light("a", " Hall ", true);
        var b = Light("b", "hall", false);
        var c = Light("c", "Store", true);
        var level = new LevelFile { UsePointLights = true, Entities = [a, b, c,
            new() { Id = "not-light", Type = EntityTypes.Prop, LightGroup = "Hall" }] };
        var control = new LevelInteractionDef { Kind = "LightSwitch", LightGroup = "HALL", Targets = [a.Id] };
        var state = new LevelLightState();
        state.BindLevel("lighting-qa.json");
        Check(LevelLightState.Targets(level, control).Count() == 2, "Group/direct target union duplicated or included a non-light.");
        Check(state.Toggle(level, control, out bool enabled) && !enabled && !state.IsEnabled(a) && !state.IsEnabled(b) && state.IsEnabled(c), "Mixed group should switch off together.");
        Check(a.LightEnabled && !b.LightEnabled, "Gameplay changed authored defaults.");
        Check(state.Toggle(level, control, out enabled) && enabled && state.IsEnabled(b), "Switch was not repeatable.");
        Check(state.Toggle(level, new() { Kind = "LightSwitch", LightGroup = "hall" }, out enabled) && !enabled, "Second switch did not share actual light state.");
        state.Toggle(level, control, out _);
        state.BindLevel("lighting-qa.json");
        Check(state.IsEnabled(b), "Same-level rebuild lost runtime state.");
        var captured = state.Capture();
        state.Restore(StructuredData.ReadJson<List<LightStateOverride>>(StructuredData.WriteJson(captured)));
        Check(state.IsEnabled(b), "RE:Dox lost light overrides.");
        level.Entities.Remove(b);
        Check(state.Capture().Any(s => s.EntityId == b.Id), "Nonresident entity lost its override.");
        level.Entities.Add(b);
        Check(state.IsEnabled(b), "Restored entity lost its override.");
        state.BindLevel("another-lighting-qa.json");
        Check(!state.IsEnabled(b) && state.Capture().Count == 0, "Overrides leaked into a different level.");
        a.Name = "Renamed fixture";
        var direct = new LevelInteractionDef { Kind = "LightSwitch", Targets = [a.Id] };
        Check(state.Toggle(level, direct, out _) && !state.IsEnabled(a) && state.IsEnabled(c), "ID link failed after renaming.");
        var byName = new LevelInteractionDef { Kind = "LightSwitch", Targets = [c.Name!] };
        Check(state.Toggle(level, byName, out _) && !state.IsEnabled(c), "Legacy named target failed.");
        Check(!state.Toggle(level, new(), out _) && state.Capture().Count == 2, "Empty switch changed unrelated lights.");
        state.Restore(null);
        Check(state.IsEnabled(a) && !state.IsEnabled(b), "Legacy save defaults failed.");

        a.FlickerAmount = .65f; a.FlickerSpeed = 3;
        Check(Enumerable.Range(0, 1000).All(i => LevelLightState.Flicker(a, i / 60.0) is >= .35f and <= 1), "Flicker exceeded its range.");
        Check(LevelLightState.Flicker(a, 1) == LevelLightState.Flicker(a, 1) &&
            LevelLightState.Flicker(a, 1) != LevelLightState.Flicker(a, 2), "Flicker is not deterministic and time-dependent.");
        b.FlickerAmount = a.FlickerAmount; b.FlickerSpeed = a.FlickerSpeed;
        Check(LevelLightState.Flicker(a, 1) != LevelLightState.Flicker(b, 1), "All fixtures flicker in lockstep.");
        Check(LevelLightState.Flicker(c, 1) == 1 && LevelLightState.Flicker(a, double.NaN) == 1, "Disabled/invalid flicker was not safe.");
        a.FlickerSpeed = float.NaN;
        Check(float.IsFinite(LevelLightState.Flicker(a, 1)), "Invalid flicker speed reached renderer.");
        a.FlickerSpeed = 3;
        var editor = new LevelEditorController();
        editor.LoadFromMemory("lighting-qa.json", StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level)));
        Check(editor.LevelFile.Entities[0].FlickerAmount == .65f && editor.LevelFile.Entities[0].LightGroup == " Hall ", "Authoring data did not round-trip.");
        Check(LevelLighting.GetLights(editor, state, 1).Count() == 2, "Disabled fixture reached renderer.");
        Check(MathF.Abs(LevelLighting.GetLights(editor, state, 1).First().Intensity - a.Intensity * LevelLightState.Flicker(a, 1)) < .0001f, "Renderer ignored authored flicker.");
        editor.LevelFile.UsePointLights = false;
        Check(!LevelLighting.GetLights(editor, state, 1).Any(), "Level light toggle ignored.");
        CheckPrefabs();
        CheckGameplay();
        CheckFrustum();
        Console.WriteLine($"PASS: {_checks} lighting checks: groups/IDs, repeated switches, prerequisites, save compatibility, prefab placement/apply/revert, flicker and frustum math.");
    }

    private static LevelEntityDef Light(string id, string group, bool enabled) => new()
    { Id = id, Name = "Fixture " + id, Type = EntityTypes.PointLight, LightGroup = group, LightEnabled = enabled, Intensity = 2, Range = 8 };

    private static void CheckPrefabs()
    {
        var lamp = Light("lamp", "", true); lamp.ParentId = "switch";
        var root = new LevelEntityDef { Id = "switch", Name = "Switch", Type = EntityTypes.Prop,
            Interaction = new() { Kind = "LightSwitch", Targets = [lamp.Id, "external-light"] } };
        var prefab = new PrefabFile { Name = "Switched fixture", RootEntityId = root.Id, Entities = [root, lamp] };
        var editor = new LevelEditorController(); editor.LoadFromMemory("lighting-qa.json", new());
        Check(editor.AddPrefabInstance(prefab, "fixture.prefab.json"), "Could not place fixture prefab.");
        string firstLight = editor.LevelFile.Entities[1].Id;
        Check(editor.LevelFile.Entities[0].Interaction!.Targets.SequenceEqual([firstLight, "external-light"]), "Prefab links not remapped.");
        Check(editor.TryBuildPrefabFromSelectedInstanceForApply("Applied", out var applied, out _), "Could not apply fixture prefab.");
        Check(applied.Entities[0].Interaction!.Targets.SequenceEqual(["lamp", "external-light"]), "Apply retained scene IDs.");
        Check(editor.AddPrefabInstance(applied, "fixture.prefab.json"), "Could not place second fixture.");
        string secondLight = editor.LevelFile.Entities[3].Id;
        Check(firstLight != secondLight && editor.LevelFile.Entities[2].Interaction!.Targets[0] == secondLight, "Second switch controls the original fixture.");
        Check(editor.RevertSelectedPrefabInstance(applied, "fixture.prefab.json"), "Could not revert fixture.");
        Check(editor.LevelFile.Entities[3].Id == secondLight && editor.LevelFile.Entities[2].Interaction!.Targets[0] == secondLight, "Revert invalidated stable IDs.");
    }

    private static void CheckGameplay()
    {
        var a = Light("game-a", "room", true);
        var b = Light("game-b", "room", false);
        var definition = new LevelEntityDef { Id = "switch", Name = "Wall switch", Type = EntityTypes.Prop,
            LocalPosition = new(0, 1.6f, 2), Size = new(.2f, .3f, .1f),
            Interaction = new() { Kind = "LightSwitch", LightGroup = "room", RequiredStates = ["power"] } };
        var module = new HL2GameModule();
        var editor = Field<LevelEditorController>(module, "_editor");
        editor.LoadFromMemory("lighting-qa.json", new() { Entities = [a, b, definition] });
        Call(module, "RebuildRuntimeWorld");
        var state = Field<LevelLightState>(module, "_lightState");
        Entity entity = Field<List<Entity>>(module, "_runtimeEntities").Single(e => e.Id == definition.Id);
        var camera = Field<FpsCamera>(module, "_camera");
        camera.Position = new(0, 1.6f, 0); camera.Yaw = 0; camera.Pitch = 0;
        Check(ReferenceEquals(Call(module, "RaycastGameplayInteractable", 3f), entity), "Switch could not be focused with the gameplay ray.");
        Check((string)Call(module, "GetInteractionPrompt", entity)! == "Power unavailable", "Power prerequisite prompt missing.");
        Call(module, "UseLightSwitch", entity);
        Check(state.IsEnabled(a), "Switch bypassed power prerequisite.");
        Field<HashSet<string>>(module, "_solvedPuzzles").Add("power");
        Check((bool)Call(module, "TryUseFocusedInteractable")!, "Interact binding did not dispatch to the switch.");
        Check(!state.IsEnabled(a) && !state.IsEnabled(b), "Powered switch failed.");
        Call(module, "TryUseFocusedInteractable");
        Check(state.IsEnabled(a) && state.IsEnabled(b), "Powered switch could not turn back on.");
        Call(module, "RebuildRuntimeWorld");
        Check(state.IsEnabled(b), "Runtime rebuild reset toggled lights.");
        typeof(HL2GameModule).GetField("_motor", Flags)!.SetValue(module, new SourcePlayerMotor(new SourceMovementSettings(), Vector3.Zero));

        string folder = Path.Combine(Path.GetTempPath(), "hs2-lighting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string levelPath = Path.Combine(folder, "authoring.json");
            LevelIO.Save(levelPath, editor.LevelFile);
            LevelFile roundTrip = LevelIO.Load(levelPath);
            Check(roundTrip.Entities[0].LightGroup == "room" && roundTrip.Entities[2].Interaction!.Kind == "LightSwitch" &&
                roundTrip.Entities[2].Interaction!.RequiredStates.SequenceEqual(["power"]), "Level save/load lost switch authoring.");
            string path = Path.Combine(folder, "fixture.json");
            Type type = typeof(HL2GameModule).GetNestedType("PrototypeSaveData", Flags)!;
            object dto = Activator.CreateInstance(type, true)!;
            type.GetProperty("LevelName")!.SetValue(dto, "lighting-qa.json");
            type.GetProperty("LightStates")!.SetValue(dto, state.Capture());
            byte[] json = (byte[])typeof(StructuredData).GetMethod("WriteJson")!.MakeGenericMethod(type).Invoke(null, [dto])!;
            File.WriteAllBytes(path, json);
            state.Clear();
            Check((bool)Call(module, "TryLoadPrototypeSave", path)! && state.IsEnabled(b), "Production save loader lost runtime light states.");
            editor.LoadFromMemory("different-level.json", editor.LevelFile);
            Call(module, "RebuildRuntimeWorld");
            Check((bool)Call(module, "TryLoadPrototypeSave", path)! && !state.IsEnabled(b), "Save applied light overrides to another level.");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{\"SaveVersion\":1,\"LevelName\":\"different-level.json\"}"));
            Check((bool)Call(module, "TryLoadPrototypeSave", path)! && state.IsEnabled(a) && !state.IsEnabled(b), "Legacy save did not use authored defaults.");
            Check(a.LightEnabled && !b.LightEnabled, "Save loading modified editor defaults.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void CheckFrustum()
    {
        var random = new Random(714);
        for (int i = 0; i < 2000; i++)
        {
            Vector3 position = new((float)random.NextDouble() * 20 - 10, (float)random.NextDouble() * 10, (float)random.NextDouble() * 20 - 10);
            Vector3 direction = Vector3.Normalize(new Vector3((float)random.NextDouble() - .5f, -.3f, -1));
            var light = new WorldPointLight(position, Vector3.One, 2, 15, direction, 70, true);
            Matrix4x4 matrix = BasicWorldRenderer.LightMatrix(light, i % 7 - 1);
            var frustum = new ShadowFrustum(matrix);
            Vector3 point = position + new Vector3((float)random.NextDouble() * 40 - 20, (float)random.NextDouble() * 40 - 20, (float)random.NextDouble() * 40 - 20);
            Vector4 clip = Vector4.Transform(new Vector4(point, 1), matrix);
            bool inside = clip.X >= -clip.W && clip.X <= clip.W && clip.Y >= -clip.W && clip.Y <= clip.W && clip.Z >= 0 && clip.Z <= clip.W;
            if (inside) Check(frustum.IntersectsSphere(point, 0), "Clip-visible point culled.");
            Vector3 center = point + Vector3.One * .5f;
            if (inside) Check(frustum.IntersectsSphere(center, 1), "Sphere containing a visible point culled.");
        }
    }
}

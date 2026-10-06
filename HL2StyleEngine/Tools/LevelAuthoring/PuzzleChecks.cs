using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Runtime.Entities;
using Game;
using Game.Inventory;
using Game.Puzzles;
using Engine.Render;

internal static class PuzzleChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Call(HL2GameModule module, string name, params object[] args)
        => typeof(HL2GameModule).GetMethod(name, Flags)!.Invoke(module, args);
    private static T Field<T>(HL2GameModule module, string name)
        => (T)typeof(HL2GameModule).GetField(name, Flags)!.GetValue(module)!;
    private static void Set(HL2GameModule module, string name, object value)
        => typeof(HL2GameModule).GetField(name, Flags)!.SetValue(module, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    internal static void Run(string root, string levelFileName = "sixRoomTest.json")
    {
        string path = Path.Combine(root, "Game/Content/Levels", levelFileName);
        LevelFile level = LevelIO.Load(path);
        Require(level.Entities.Count(e => e.Interaction?.Kind == "PuzzleDoor") == 5, "Expected five shutters.");
        var states = level.Entities.Where(e => e.Interaction != null).Select(e => e.Interaction!.StateId).ToArray();
        Require(states.Distinct(StringComparer.OrdinalIgnoreCase).Count() == states.Length, "Puzzle state IDs must be unique.");
        foreach (var entity in level.Entities.Where(e => e.Interaction != null))
        {
            foreach (string target in entity.Interaction!.Targets)
                Require(level.Entities.Any(e => e.Name == target && e.Interaction?.Kind == "PuzzleDoor"), "Invalid shutter target: " + target);
            foreach (string required in entity.Interaction.RequiredStates)
                Require(states.Contains(required), "Missing prerequisite state: " + required);
        }
        var persisted = JsonSerializer.Deserialize<LevelFile>(JsonSerializer.Serialize(level))!;
        Require(persisted.Entities.Single(e => e.Interaction?.Kind == "PressurePlate").Interaction!.PressurePlateMinMass == 8,
            "Pressure plate settings did not round-trip.");

        var module = CreateModule(root, level);
        var entities = Field<List<Entity>>(module, "_runtimeEntities");
        var solved = Field<HashSet<string>>(module, "_solvedPuzzles");
        var inventory = Field<InventoryContainer>(module, "_inventory");
        Entity Find(string name) => entities.Single(e => e.Name == SixRoomPuzzles.Prefix + name);
        Vector3 Position(string name) => Find(name).Transform.Position;
        var framesBefore = SixRoomPuzzles.Destinations.ToDictionary(s => s, s => Position(s + "_Frame"));
        var doorsBefore = SixRoomPuzzles.Destinations.ToDictionary(s => s, s => Position(s + "_Shutter"));

        var camera = Field<FpsCamera>(module, "_camera");
        void LookAt(Vector3 origin, Vector3 target)
        {
            Vector3 direction = Vector3.Normalize(target - origin);
            camera.Position = origin; camera.Yaw = MathF.Atan2(direction.X, direction.Z); camera.Pitch = MathF.Asin(direction.Y);
        }
        foreach (Entity control in entities.Where(e => e.Name.StartsWith(SixRoomPuzzles.Prefix) &&
                     level.Entities.Single(d => d.Id == e.Id).Interaction?.Kind is "PuzzleSlot" or "PuzzleLever"))
        {
            Vector3 face = control.Transform.Position.Z > 15 ? -Vector3.UnitZ :
                control.Transform.Position.X < 0 ? Vector3.UnitX : -Vector3.UnitX;
            Vector3 origin = control.Transform.Position + face * 2; origin.Y = 1.6f;
            LookAt(origin, control.Transform.Position);
            Require(ReferenceEquals(Call(module, "RaycastGameplayInteractable", 3f), control), "Control cannot be selected from the hall: " + control.Name);
        }
        Entity highCable = entities.Single(e => e.Name.StartsWith("Item_WorkshopCable__"));
        LookAt(new(9, 1.6f, -11.9f), highCable.Transform.Position);
        Require(ReferenceEquals(Call(module, "RaycastPickable", 8f, null!), highCable), "Gravity gun cannot target the retrieval cable.");

        // Source movement checks use the actual runtime triangle frames and closed/open collider sets.
        CheckRoutes(module, open: false);
        void Collect(string item, string accessibleRoom)
        {
            Entity pickup = entities.Single(e => e.Name == $"Item_{item}__{SixRoomPuzzles.Prefix}Pickup");
            var room = SixRoomTest.Rooms.Single(r => r.Name == accessibleRoom);
            Require(room.Contains(pickup.Transform.Position.X, pickup.Transform.Position.Z), "Item is behind an unavailable gate: " + item);
            Require(pickup.CanPickUp && pickup.Physics.BoxBody != null && !pickup.Damageable, "Critical item must be grabbable and indestructible: " + item);
            Require(ItemCatalog.Get(item).Type is InventoryItemType.Key or InventoryItemType.Puzzle, "Critical item is discardable: " + item);
            Require(inventory.Add(item, 1), "Inventory cannot hold puzzle item: " + item);
        }
        void Slot(string slot, string item)
        {
            Call(module, "UseSelectedItemOnPuzzleSlot", Find(slot), item);
            Require(solved.Contains(SixRoomPuzzles.Prefix + slot) && !inventory.Contains(item), "Slot did not consume/complete: " + slot);
        }
        void Lever(string name) => Call(module, "UsePuzzleLever", Find(name));

        Lever("LoadingRelease"); Lever("MedicalRelease"); Lever("UtilityRelease");
        Require(solved.Count == 0, "A lever bypassed its prerequisites.");
        Collect(ItemCatalog.MaintenanceKey, "CentralHall");
        Call(module, "UseSelectedItemOnPuzzleSlot", Find("WorkshopSocket"), ItemCatalog.MaintenanceKey);
        Require(solved.Count == 0 && inventory.Contains(ItemCatalog.MaintenanceKey), "Wrong item was accepted/consumed.");
        Slot("FreightOverride", ItemCatalog.MaintenanceKey);
        Collect(ItemCatalog.FreightFeed, "FreightStore");
        Collect(ItemCatalog.WorkshopCable, "CentralHall"); Slot("WorkshopSocket", ItemCatalog.WorkshopCable);
        Collect(ItemCatalog.DamagedCable, "CentralHall"); Collect(ItemCatalog.SpareWire, "Workshop");
        Require(ItemCatalog.CanCombine(ItemCatalog.DamagedCable, ItemCatalog.SpareWire) &&
            ItemCatalog.CanCombine(ItemCatalog.SpareWire, ItemCatalog.DamagedCable), "Repair recipe is not symmetric.");
        Set(module, "_combineSourceSlot", inventory.Stacks.Single(s => s.ItemId == ItemCatalog.DamagedCable).SlotIndex);
        Set(module, "_selectedInventoryStackIndex", inventory.Stacks.Single(s => s.ItemId == ItemCatalog.SpareWire).SlotIndex);
        Call(module, "TryCombineWithSelection");
        Require(inventory.Contains(ItemCatalog.RepairedCable) && !inventory.Contains(ItemCatalog.DamagedCable) &&
            !inventory.Contains(ItemCatalog.SpareWire), "Runtime combination failed.");
        Slot("MedicalSocket", ItemCatalog.RepairedCable); Lever("MedicalRelease");
        Collect(ItemCatalog.MedicalFeed, "MedicalSupplies");

        Entity weight = Find("UtilityWeightCrate"), plate = Find("UtilityPressurePlate");
        Require(!weight.Damageable, "Fallback pressure-plate weight can be destroyed.");
        void PutWeight(Vector3 center, bool held = false)
        {
            weight.Transform.Position = center; weight.Physics.BoxBody!.Center = center;
            weight.Physics.BoxBody.Velocity = Vector3.Zero; weight.IsHeld = held;
        }
        Vector3 onPlate = plate.Transform.Position + new Vector3(0, plate.Collider.Size.Y / 2 + 0.5f, 0);
        var active = Field<HashSet<string>>(module, "_activePuzzleStates");
        PutWeight(onPlate, held: true); Call(module, "UpdatePressurePlates", 1f);
        Require(!active.Contains(SixRoomPuzzles.Prefix + "UtilityWeight"), "A held crate activated the plate.");
        PutWeight(onPlate + new Vector3(0, 1, 0)); Call(module, "UpdatePressurePlates", 1f);
        Require(active.Count == 0, "An airborne crate activated the plate.");
        PutWeight(onPlate); Call(module, "UpdatePressurePlates", 0.1f);
        Require(active.Count == 0, "Plate ignored settling delay.");
        Call(module, "UpdatePressurePlates", 0.3f);
        Require(active.Contains(SixRoomPuzzles.Prefix + "UtilityWeight"), "Resting crate did not activate plate.");
        Require(Find("UtilityPlate_Indicator").Render.Color.Y > 0.8f, "Active plate indicator did not turn green.");
        Lever("UtilityRelease");
        PutWeight(onPlate + new Vector3(-3, 0, 0)); Call(module, "UpdatePressurePlates", 0.1f);
        Require(active.Count == 0 && solved.Contains(SixRoomPuzzles.Prefix + "UtilityRelease"), "Plate did not release or door failed to latch.");
        Require(Find("UtilityPlate_Indicator").Render.Color.X > 0.8f && Find("UtilityReleaseOpen_Indicator").Render.Color.Y > 0.8f,
            "Plate/latched door indicators do not reflect their independent states.");
        Collect(ItemCatalog.UtilityFeed, "UtilityRoom");
        Slot("LoadingFreightSocket", ItemCatalog.FreightFeed); Slot("LoadingMedicalSocket", ItemCatalog.MedicalFeed);
        Lever("LoadingRelease");
        Require(!solved.Contains(SixRoomPuzzles.Prefix + "LoadingRelease"), "Two feeds opened final shutter.");
        Slot("LoadingUtilitySocket", ItemCatalog.UtilityFeed); Lever("LoadingRelease");
        for (int i = 0; i < 300; i++) Call(module, "UpdatePuzzleDoorAnimations", 1f / 60);
        foreach (string room in SixRoomPuzzles.Destinations)
        {
            Require(Vector3.Distance(Position(room + "_Frame"), framesBefore[room]) < 0.0001f, "Frame moved with shutter.");
            Require(Vector3.Distance(Position(room + "_Shutter"), doorsBefore[room] + new Vector3(0, 3.35f, 0)) < 0.0001f, "Shutter did not reach full clearance.");
        }
        CheckRoutes(module, open: true);

        // Saved solved states restore latched doors, but never transient pressure occupancy.
        string savedStates = JsonSerializer.Serialize(solved);
        var restored = CreateModule(root, persisted);
        Field<HashSet<string>>(restored, "_solvedPuzzles").UnionWith(JsonSerializer.Deserialize<string[]>(savedStates)!);
        Call(restored, "ApplyPersistentInteractionStateToRuntime");
        foreach (string room in SixRoomPuzzles.Destinations)
        {
            Entity door = Field<List<Entity>>(restored, "_runtimeEntities").Single(e => e.Name == SixRoomPuzzles.Prefix + room + "_Shutter");
            Require(Vector3.Distance(door.Transform.Position, doorsBefore[room] + new Vector3(0, 3.35f, 0)) < 0.0001f, "Saved shutter closed again.");
        }
        Require(Field<HashSet<string>>(restored, "_activePuzzleStates").Count == 0, "Plate occupancy was persisted.");
        var restoredEntities = Field<List<Entity>>(restored, "_runtimeEntities");
        Require(restoredEntities.Single(e => e.Name == SixRoomPuzzles.Prefix + "LoadingReleaseOpen_Indicator").Render.Color.Y > 0.8f,
            "Solved indicator was not restored.");
        CheckSensor();
        Console.WriteLine("PASS: all five runtime puzzles, wrong-item rejection, repair recipe, three-feed gate, indicators, plate release/latching and solved-state restore.");
        Console.WriteLine("PASS: actual mesh frames remain stationary; closed shutters block the player and all five opened routes are traversable.");
    }

    private static HL2GameModule CreateModule(string root, LevelFile level)
    {
        var module = new HL2GameModule();
        Field<LevelEditorController>(module, "_editor").LoadFromMemory(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"), level);
        // Seed CPU model data without creating a graphics device; production world construction still builds the colliders.
        var cache = Field<IDictionary>(module, "_weaponModelCache");
        Type entryType = typeof(HL2GameModule).GetNestedType("WeaponModelCacheEntry", Flags)!;
        foreach (string path in level.Entities.Where(e => e.Shape == "Mesh").Select(e => e.MeshPath).Distinct())
        {
            var model = GlbModelLoader.Load(Path.Combine(root, "Game", path));
            object entry = Activator.CreateInstance(entryType, nonPublic: true)!;
            entryType.GetField("LoadedModel", Flags)!.SetValue(entry, model);
            entryType.GetField("Bounds", Flags)!.SetValue(entry, Call(module, "CalculateModelBounds", model));
            cache.Add((string)Call(module, "ResolveModelAssetPath", path)!, entry);
        }
        Call(module, "RebuildRuntimeWorld");
        return module;
    }

    private static void CheckRoutes(HL2GameModule module, bool open)
    {
        Call(module, "BuildRuntimeCollidersThisFrame", false, true);
        var colliders = Field<List<WorldCollider>>(module, "_runtimeWorldColliders");
        var routes = new (Vector3 Start, Vector3 End)[]
        {
            (new(-12, 0.05f, -8), new(-18, 0.05f, -8)), (new(12, 0.05f, -8), new(18, 0.05f, -8)),
            (new(-12, 0.05f, 8), new(-18, 0.05f, 8)), (new(12, 0.05f, 8), new(18, 0.05f, 8)),
            (new(0, 0.05f, 14), new(0, 0.05f, 20))
        };
        foreach (var route in routes)
        {
            var motor = new SourcePlayerMotor(new SourceMovementSettings(), route.Start);
            for (int tick = 0; tick < 180; tick++)
            {
                Vector3 delta = route.End - motor.Position; delta.Y = 0;
                if (delta.Length() < 0.2f) break;
                motor.Step(1f / 60, Vector3.Normalize(delta), MathF.Min(5, delta.Length() * 5), colliders);
            }
            float remaining = Vector2.Distance(new(motor.Position.X, motor.Position.Z), new(route.End.X, route.End.Z));
            Require(open ? remaining < 0.2f : remaining > 2f, $"Shutter traversal incorrect (open={open}): {route} ended at {motor.Position}");
        }
    }

    private static void CheckSensor()
    {
        var sensor = new PressurePlateSensor();
        sensor.Update(1.2f, 8, 0.35f, 1);
        Require(!sensor.Active, "A light pickup activated the plate.");
        sensor.Update(12, 8, 0.35f, 0.5f); Require(sensor.Active, "Sufficient mass failed.");
        sensor.Update(0, 8, 0.35f, 0); Require(!sensor.Active, "Removed weight remained active.");
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        Vector3 p = new(4, 0.1f, 2), body = p + new Vector3(0, 0.6f, 0.8f);
        var bounds = Aabb.FromCenterExtents(body, new(0.5f));
        Require(PressurePlateSensor.SupportsBody(p, new(2, 0.2f, 1), rotation, bounds, body, Vector3.Zero, false), "Yaw-rotated footprint failed.");
        Require(!PressurePlateSensor.SupportsBody(p, new(2, 0.2f, 1), Quaternion.Identity, bounds, body, Vector3.Zero, false), "Plate used wrong footprint.");
    }
}

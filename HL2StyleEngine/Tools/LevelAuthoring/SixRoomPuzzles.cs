using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;
using Game.Inventory;

internal static class SixRoomPuzzles
{
    internal const string Prefix = "SRPuzzle_";
    private const string Models = "Content/Models/ViewModels/";
    internal static readonly string[] Destinations = ["Freight", "Workshop", "Medical", "Utility", "Loading"];

    internal static void AddTo(string root, LevelFile level)
    {
        if (level.Entities.Any(e => e.Name?.Contains(Prefix, StringComparison.Ordinal) == true))
            throw new InvalidOperationException("Puzzle pass already exists. Edit it in HS2Editor; do not overwrite it.");
        RollupDoorAssets.Prepare(root);

        LevelEntityDef Add(string name, Vector3 position, Vector3 size, string mesh = "", bool solid = true, float yaw = 0)
        {
            var entity = new LevelEntityDef
            {
                Id = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("sixRoomTest/puzzles/" + name))).ToLowerInvariant(),
                Name = Prefix + name, Type = solid ? EntityTypes.RigidBody : EntityTypes.Prop,
                LocalPosition = position, LocalScale = Vector3.One,
                LocalRotationEulerDeg = new(0, yaw, 0), Size = size,
                Color = Vector4.One, MeshPath = mesh.Length > 0 ? Models + mesh + ".glb" : "",
                Shape = "Box", MotionType = MotionType.Static, Mass = 0, Friction = 1, Restitution = 0
            };
            level.Entities.Add(entity);
            return entity;
        }

        void Door(string room, Vector3 anchor, float yaw)
        {
            Vector3 scale = new(4f / 3, 3.25f / 3, 1);
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180);
            foreach (bool leaf in new[] { false, true })
            {
                string path = leaf ? RollupDoorAssets.LeafPath : RollupDoorAssets.FramePath;
                var bounds = RollupDoorAssets.Bounds(GlbModelLoader.Load(Path.Combine(root, "Game", path)));
                Vector3 position = anchor + Vector3.Transform((bounds.Min + bounds.Max) / 2 * scale, rotation);
                var part = Add(room + (leaf ? "_Shutter" : "_Frame"), position, (bounds.Max - bounds.Min) * scale, yaw: yaw);
                part.MeshPath = path;
                part.Shape = leaf ? "Box" : "Mesh";
                if (leaf) part.Interaction = new() { Kind = "PuzzleDoor", StateId = Prefix + room + "Door", LiftHeight = 3.35f };
            }
        }

        Door("Freight", new(-15, 0, -8), 90);
        Door("Workshop", new(15, 0, -8), -90);
        Door("Medical", new(-15, 0, 8), 90);
        Door("Utility", new(15, 0, 8), -90);
        Door("Loading", new(0, 0, 17), 180);

        void Indicator(string name, Vector3 position, params string[] states)
        {
            var lamp = Add(name + "_Indicator", position, new(0.14f, 0.14f, 0.14f), solid: false);
            lamp.Color = new Vector4(0.9f, 0.13f, 0.06f, 1);
            lamp.Interaction = new() { Kind = "PuzzleIndicator", StateId = Prefix + name + "Lamp", RequiredStates = states.ToList() };
        }

        LevelEntityDef Panel(string name, Vector3 position, float yaw, string item, string prompt, string success, string? door = null)
        {
            var panel = Add(name, position, new(0.85f, 1.15f, 0.18f), "Electrical Panel", yaw: yaw);
            panel.Interaction = new()
            {
                Kind = "PuzzleSlot", StateId = Prefix + name, RequiredItem = item, ConsumesItem = true,
                Prompt = prompt, LockedPrompt = "Needs " + ItemCatalog.GetDisplayName(item), SuccessMessage = success,
                Targets = door == null ? [] : [Prefix + door + "_Shutter"]
            };
            Vector3 face = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180));
            Indicator(name, position + face * 0.15f + new Vector3(0, 0.4f, 0), panel.Interaction.StateId);
            return panel;
        }

        void Lever(string name, Vector3 position, float panelYaw, string door, string blocked, params string[] required)
        {
            var lever = Add(name, position, new(0.2f, 0.65f, 0.4f), "Lever04", yaw: panelYaw - 90);
            lever.Interaction = new()
            {
                Kind = "PuzzleLever", StateId = Prefix + name, Prompt = "Open " + door + " shutter",
                LockedPrompt = blocked, SuccessMessage = door + " shutter released. It will stay open.",
                RequiredStates = required.ToList(), Targets = [Prefix + door + "_Shutter"]
            };
            Indicator(name + "Ready", position + new Vector3(0, 0.5f, 0), required);
            Indicator(name + "Open", position + new Vector3(0, 0.72f, 0), lever.Interaction.StateId);
        }

        void Pickup(string item, Vector3 position, Vector3 size, string mesh, Vector3? rotation = null)
        {
            var pickup = Add(item, position, size, mesh);
            pickup.Name = "Item_" + item + "__" + Prefix + "Pickup";
            pickup.MotionType = MotionType.Dynamic; pickup.Mass = 1.2f; pickup.CanPickUp = true;
            if (rotation.HasValue) pickup.LocalRotationEulerDeg = rotation.Value;
        }

        void Table(string name, Vector3 position, float width = 1.6f)
            => Add(name, position, new(width, 0.8f, 0.9f), "Breakable_Wooden_Crate");

        void CablePickup(string item, Vector3 position, string mesh)
            => Pickup(item, position, new(0.13f, 0.75f, 0.13f), mesh, new(90, 0, 0));

        // Freight key is in the hall, behind movable cover, never a random loot drop.
        Table("KeyStand", new(-11.5f, 0.4f, -13));
        Pickup(ItemCatalog.MaintenanceKey, new(-11.5f, 0.91f, -13), new(0.18f, 0.035f, 0.42f), "Bronze Key");
        var cover = Add("KeyCover", new(-11.5f, 0.54f, -11.5f), Vector3.One, "Breakable_Wooden_Crate");
        cover.CanPickUp = true; cover.MotionType = MotionType.Dynamic; cover.Mass = 12;
        cover.Damageable = true; cover.MaxHealth = 100;
        Panel("FreightOverride", new(-13.7f, 1.45f, -5.1f), 90, ItemCatalog.MaintenanceKey,
            "Use Maintenance Key", "Freight override accepted. Shutter opening.", "Freight");

        // Shelf is outside Workshop. Its overhanging cable can be pulled without entering the room.
        Add("RetrievalShelf", new(11.8f, 4.35f, -12.5f), new(1.3f, 0.16f, 3), "Beam_4m");
        Pickup(ItemCatalog.WorkshopCable, new(11.8f, 4.59f, -11.9f), new(0.2f, 0.9f, 0.2f), "ElectricalWires03_03", new(90, 0, 0));
        Panel("WorkshopSocket", new(13.7f, 1.45f, -5.1f), -90, ItemCatalog.WorkshopCable,
            "Install Workshop Cable", "Workshop connection restored. Shutter opening.", "Workshop");

        // Medical repair starts outside the door; its replacement conductor is in Workshop.
        Table("MedicalCableStand", new(-11.5f, 0.4f, 12));
        CablePickup(ItemCatalog.DamagedCable, new(-11.5f, 0.92f, 12), "ElectricalWires03_01");
        CablePickup(ItemCatalog.SpareWire, new(25.5f, 1.04f, -15), "ElectricalWires03_02");
        Panel("MedicalSocket", new(-13.7f, 1.45f, 10.9f), 90, ItemCatalog.RepairedCable,
            "Install Repaired Cable", "Medical connection repaired. The release lever is ready.");
        Lever("MedicalRelease", new(-13.6f, 1.35f, 12.1f), 90, "Medical", "Repair the medical cable first", Prefix + "MedicalSocket");

        // Low platform is the actual sensing surface. A spare indestructible weight prevents soft-locks.
        var plate = Add("UtilityPressurePlate", new(10.5f, 0.08f, 11.5f), new(2.2f, 0.16f, 2.2f), "Basement_Corridor_C_Grating_Floor_2x2m");
        plate.Interaction = new() { Kind = "PressurePlate", StateId = Prefix + "UtilityWeight", PressurePlateMinMass = 8, PressurePlateSettleSeconds = 0.35f };
        var weight = Add("UtilityWeightCrate", new(8, 0.54f, 11.5f), Vector3.One, "Breakable_Wooden_Crate");
        weight.CanPickUp = true; weight.MotionType = MotionType.Dynamic; weight.Mass = 12;
        Lever("UtilityRelease", new(13.6f, 1.35f, 11.5f), -90, "Utility", "A crate must rest on the pressure plate", Prefix + "UtilityWeight");
        Indicator("UtilityPlate", new(10.5f, 0.19f, 12.4f), Prefix + "UtilityWeight");

        // The final leads are deterministic pickups in three already-opened rooms.
        Table("FreightFeedStand", new(-29, 0.4f, -10));
        CablePickup(ItemCatalog.FreightFeed, new(-29, 0.92f, -10), "ElectricalWires03_01");
        CablePickup(ItemCatalog.MedicalFeed, new(-24.5f, 1.03f, 15), "ElectricalWires03_02");
        Table("UtilityFeedStand", new(27, 0.4f, 15));
        CablePickup(ItemCatalog.UtilityFeed, new(27, 0.92f, 15), "ElectricalWires03_04");
        Panel("LoadingFreightSocket", new(-5.2f, 1.45f, 15.7f), 180, ItemCatalog.FreightFeed,
            "Connect Freight Feed", "Loading Bay: freight feed connected.");
        Panel("LoadingMedicalSocket", new(-4.1f, 1.45f, 15.7f), 180, ItemCatalog.MedicalFeed,
            "Connect Medical Feed", "Loading Bay: medical feed connected.");
        Panel("LoadingUtilitySocket", new(-3, 1.45f, 15.7f), 180, ItemCatalog.UtilityFeed,
            "Connect Utility Feed", "Loading Bay: utility feed connected.");
        Lever("LoadingRelease", new(3, 1.35f, 15.6f), 180, "Loading", "Connect all three Loading Bay power feeds",
            Prefix + "LoadingFreightSocket", Prefix + "LoadingMedicalSocket", Prefix + "LoadingUtilitySocket");

        // Surface-mounted cable runs tie controls to their nearby shutters without adding colliders.
        foreach (var (name, x, z, yaw) in new[]
        {
            ("Freight", -13.73f, -5.1f, 90f), ("Workshop", 13.73f, -5.1f, -90f),
            ("Medical", -13.73f, 10.9f, 90f), ("Utility", 13.73f, 11.5f, -90f)
        })
        {
            Add(name + "_Conduit", new(x, 2.35f, z), new(0.045f, 1.25f, 0.045f), "Cables01_01", false, yaw);
            float doorZ = name is "Freight" or "Workshop" ? -8 : 8;
            var run = Add(name + "_OverheadCable", new(x, 3, (z + doorZ) / 2),
                new(0.045f, MathF.Abs(z - doorZ), 0.045f), "Cables01_01", false);
            run.LocalRotationEulerDeg = new(90, 0, 0);
        }
        for (int i = 0; i < 3; i++)
            Add("LoadingFeedConduit" + i, new(-5.2f + i * 1.1f, 2.45f, 15.74f),
                new(0.045f, 1.35f, 0.045f), "Cables01_01", false);
    }
}

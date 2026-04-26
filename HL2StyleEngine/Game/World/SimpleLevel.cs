using System.Collections.Generic;
using System.Numerics;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;

namespace Game.World;

public static class SimpleLevel
{
    public static List<BoxInstance> BuildRoom01()
    {
        var boxes = new List<BoxInstance>();

        Vector4 floor = new(0.25f, 0.25f, 0.25f, 1f);
        Vector4 wall = new(0.45f, 0.45f, 0.50f, 1f);
        Vector4 prop = new(0.60f, 0.45f, 0.25f, 1f);
        Vector4 pillar = new(0.35f, 0.35f, 0.35f, 1f);

        float roomW = 18f;
        float roomD = 18f;
        float wallH = 4f;
        float wallT = 0.4f;

        boxes.Add(new BoxInstance(new Vector3(0f, -0.1f, 0f), new Vector3(roomW, 0.2f, roomD), floor));

        boxes.Add(new BoxInstance(new Vector3(0f, wallH * 0.5f, roomD * 0.5f), new Vector3(roomW, wallH, wallT), wall));
        boxes.Add(new BoxInstance(new Vector3(0f, wallH * 0.5f, -roomD * 0.5f), new Vector3(roomW, wallH, wallT), wall));
        boxes.Add(new BoxInstance(new Vector3(roomW * 0.5f, wallH * 0.5f, 0f), new Vector3(wallT, wallH, roomD), wall));
        boxes.Add(new BoxInstance(new Vector3(-roomW * 0.5f, wallH * 0.5f, 0f), new Vector3(wallT, wallH, roomD), wall));

        boxes.Add(new BoxInstance(new Vector3(2f, 0.5f, 2f), new Vector3(1f, 1f, 1f), prop));
        boxes.Add(new BoxInstance(new Vector3(-3f, 0.5f, -1f), new Vector3(1f, 1f, 1f), prop));
        boxes.Add(new BoxInstance(new Vector3(0f, 0.5f, -4f), new Vector3(1f, 1f, 1f), prop));

        boxes.Add(new BoxInstance(new Vector3(5f, 2f, 5f), new Vector3(0.8f, 4f, 0.8f), pillar));
        boxes.Add(new BoxInstance(new Vector3(-5f, 2f, 5f), new Vector3(0.8f, 4f, 0.8f), pillar));
        boxes.Add(new BoxInstance(new Vector3(5f, 2f, -5f), new Vector3(0.8f, 4f, 0.8f), pillar));
        boxes.Add(new BoxInstance(new Vector3(-5f, 2f, -5f), new Vector3(0.8f, 4f, 0.8f), pillar));

        boxes.Add(new BoxInstance(new Vector3(0f, 0.25f, 6f), new Vector3(4f, 0.5f, 3f), new Vector4(0.2f, 0.4f, 0.6f, 1f)));

        return boxes;
    }

    public static LevelFile BuildRoom01File()
    {
        var instances = BuildRoom01();

        var level = new LevelFile { Version = 2 };

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PlayerSpawn,
            Name = "PlayerSpawn",
            LocalPosition = new Vector3(0, 0, -5f),
            YawDeg = 0f
        });

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PointLight,
            Name = "Light_Main",
            LocalPosition = new Vector3(0, 3.5f, 0),
            LightColor = new Vector4(1f, 0.95f, 0.8f, 1f),
            Intensity = 4f,
            Range = 12f
        });

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.TriggerVolume,
            Name = "Trigger_Test",
            LocalPosition = new Vector3(0, 1f, 2f),
            TriggerSize = new Vector3(2f, 2f, 2f),
            TriggerEvent = "OnEnter_TestTrigger"
        });

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.Prop,
            Name = "Prop_CratePlaceholder",
            LocalPosition = new Vector3(3f, 0.5f, -2f),
            MeshPath = "Content/Meshes/crate01.mesh",
            MaterialPath = "Content/Materials/crate01.mat",
            LocalRotationEulerDeg = new Vector3(0, 0, 0),
            LocalScale = new Vector3(1, 1, 1)
        });

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.RigidBody,
            Name = "RB_Test",
            LocalPosition = new Vector3(-3f, 0.5f, -2f),
            Shape = "Box",
            Size = new Vector3(1, 1, 1),
            Mass = 10f,
            Friction = 0.8f,
            Restitution = 0.05f,
            IsKinematic = false
        });

        int i = 0;
        foreach (var b in instances)
        {
            level.Entities.Add(new LevelEntityDef
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = EntityTypes.Box,
                Name = $"Box_{i++}",
                LocalPosition = b.Position,
                Size = b.Size,
                Color = b.Color
            });
        }

        return level;
    }

    public static LevelFile BuildInteractionTestFile()
    {
        var level = new LevelFile { Version = 2 };

        static LevelEntityDef Box(
            string name,
            Vector3 position,
            Vector3 size,
            Vector4 color,
            Vector3? rotationEulerDeg = null)
        {
            return new LevelEntityDef
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = EntityTypes.Box,
                Name = name,
                LocalPosition = position,
                LocalRotationEulerDeg = rotationEulerDeg ?? Vector3.Zero,
                Size = size,
                Color = color
            };
        }

        static LevelEntityDef DynamicBox(
            string name,
            Vector3 position,
            Vector3 size,
            Vector4 color,
            float mass = 10f,
            Vector3? rotationEulerDeg = null)
        {
            return new LevelEntityDef
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = EntityTypes.RigidBody,
                Name = name,
                LocalPosition = position,
                LocalRotationEulerDeg = rotationEulerDeg ?? Vector3.Zero,
                Shape = "Box",
                Size = size,
                Color = color,
                CanPickUp = true,
                MotionType = MotionType.Dynamic,
                Mass = mass,
                Friction = 0.85f,
                Restitution = 0.03f
            };
        }

        static LevelEntityDef Light(string name, Vector3 position, Vector4 color, float intensity, float range)
        {
            return new LevelEntityDef
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = EntityTypes.PointLight,
                Name = name,
                LocalPosition = position,
                LightColor = color,
                Intensity = intensity,
                Range = range
            };
        }

        Vector4 floor = new(0.12f, 0.13f, 0.14f, 1f);
        Vector4 carpet = new(0.32f, 0.05f, 0.11f, 1f);
        Vector4 wall = new(0.27f, 0.28f, 0.30f, 1f);
        Vector4 trim = new(0.18f, 0.19f, 0.20f, 1f);
        Vector4 door = new(0.34f, 0.18f, 0.10f, 1f);
        Vector4 brass = new(0.95f, 0.72f, 0.28f, 1f);
        Vector4 ink = new(0.12f, 0.12f, 0.18f, 1f);
        Vector4 desk = new(0.28f, 0.18f, 0.11f, 1f);
        Vector4 save = new(0.12f, 0.22f, 0.34f, 1f);
        Vector4 crate = new(0.45f, 0.32f, 0.18f, 1f);
        Vector4 archive = new(0.22f, 0.27f, 0.24f, 1f);

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.PlayerSpawn,
            Name = "PlayerSpawn",
            LocalPosition = new Vector3(0f, 0f, -11.35f),
            YawDeg = 0f
        });

        level.Entities.Add(Light("Light_Foyer_Dim", new Vector3(-2.5f, 3.0f, -10.2f), new Vector4(1f, 0.75f, 0.48f, 1f), 2.1f, 7.5f));
        level.Entities.Add(Light("Light_Central_Flicker", new Vector3(1.8f, 3.1f, -2.2f), new Vector4(0.8f, 0.95f, 1f, 1f), 1.8f, 8.0f));
        level.Entities.Add(Light("Light_SaveRoom_Cool", new Vector3(-6.1f, 2.8f, 1.6f), new Vector4(0.45f, 0.65f, 1f, 1f), 2.5f, 6.5f));
        level.Entities.Add(Light("Light_Archive_Amber", new Vector3(2.8f, 3.0f, 9.0f), new Vector4(1f, 0.62f, 0.35f, 1f), 2.2f, 7.0f));

        level.Entities.Add(Box("Floor_InteractionTest", new Vector3(0f, -0.1f, 0f), new Vector3(18f, 0.2f, 28f), floor));
        level.Entities.Add(Box("Ceiling_InteractionTest", new Vector3(0f, 3.4f, 0f), new Vector3(18f, 0.2f, 28f), trim));
        level.Entities.Add(Box("Carpet_Foyer", new Vector3(0f, 0.02f, -10.5f), new Vector3(5.2f, 0.05f, 3.4f), carpet));
        level.Entities.Add(Box("Carpet_Archive", new Vector3(2.7f, 0.02f, 9.0f), new Vector3(5.2f, 0.05f, 4.8f), carpet));

        level.Entities.Add(Box("Wall_North", new Vector3(0f, 1.7f, 14f), new Vector3(18f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("Wall_South", new Vector3(0f, 1.7f, -14f), new Vector3(18f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("Wall_West", new Vector3(-9f, 1.7f, 0f), new Vector3(0.35f, 3.4f, 28f), wall));
        level.Entities.Add(Box("Wall_East", new Vector3(9f, 1.7f, 0f), new Vector3(0.35f, 3.4f, 28f), wall));

        level.Entities.Add(Box("Divider_Foyer_LeftOfDoor", new Vector3(-5.25f, 1.7f, -6.4f), new Vector3(7.5f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("Divider_Foyer_RightOfDoor", new Vector3(5.25f, 1.7f, -6.4f), new Vector3(7.5f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("LockedDoor_RustedKey", new Vector3(0f, 1.2f, -6.45f), new Vector3(3.1f, 2.4f, 0.28f), door));

        level.Entities.Add(Box("Divider_Archive_LeftOfDoor", new Vector3(-5.25f, 1.7f, 3.6f), new Vector3(7.5f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("Divider_Archive_RightOfDoor", new Vector3(5.25f, 1.7f, 3.6f), new Vector3(7.5f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("LockedDoor_ArchiveKey", new Vector3(0f, 1.2f, 3.55f), new Vector3(3.1f, 2.4f, 0.28f), door));

        level.Entities.Add(Box("Wall_SaveOffice_Back", new Vector3(-5.7f, 1.7f, 3.4f), new Vector3(6.2f, 3.4f, 0.35f), wall));
        level.Entities.Add(Box("Wall_SaveOffice_Side_South", new Vector3(-3.05f, 1.7f, -4.0f), new Vector3(0.35f, 3.4f, 1.8f), wall));
        level.Entities.Add(Box("Wall_SaveOffice_Side_North", new Vector3(-3.05f, 1.7f, 1.25f), new Vector3(0.35f, 3.4f, 4.1f), wall));
        level.Entities.Add(Box("LockedDoor_ServiceKey", new Vector3(-3.08f, 1.2f, -1.95f), new Vector3(0.28f, 2.4f, 2.5f), door));

        level.Entities.Add(Box("Wall_Utility_Side_South", new Vector3(3.15f, 1.7f, -4.45f), new Vector3(0.35f, 3.4f, 4.3f), wall));
        level.Entities.Add(Box("Wall_Utility_Side_North", new Vector3(3.15f, 1.7f, 1.15f), new Vector3(0.35f, 3.4f, 4.5f), wall));
        level.Entities.Add(Box("Wall_Utility_Back", new Vector3(6.05f, 1.7f, -3.6f), new Vector3(5.9f, 3.4f, 0.35f), wall));

        level.Entities.Add(Box("Desk_RustedKeyTable", new Vector3(-5.65f, 0.45f, -11.2f), new Vector3(2.2f, 0.35f, 1.1f), desk));
        level.Entities.Add(Box("ItemKey_RustedKey", new Vector3(-5.5f, 0.72f, -11.2f), new Vector3(0.42f, 0.08f, 0.18f), brass, new Vector3(0f, 28f, 0f)));
        level.Entities.Add(Box("ItemInkRibbon_Foyer", new Vector3(5.2f, 0.42f, -10.65f), new Vector3(0.48f, 0.12f, 0.32f), ink, new Vector3(0f, -18f, 0f)));

        level.Entities.Add(Box("Shelf_ServiceKey", new Vector3(5.85f, 0.55f, -1.25f), new Vector3(1.7f, 0.35f, 0.9f), archive));
        level.Entities.Add(Box("ItemKey_ServiceKey", new Vector3(5.85f, 0.85f, -1.25f), new Vector3(0.42f, 0.08f, 0.18f), brass, new Vector3(0f, -35f, 0f)));
        level.Entities.Add(Box("SaveDesk", new Vector3(-6.2f, 0.3f, 1.55f), new Vector3(1.8f, 0.35f, 1.1f), desk));
        level.Entities.Add(Box("SavePoint_Typewriter", new Vector3(-6.2f, 0.7f, 1.55f), new Vector3(1.2f, 0.38f, 0.75f), save));
        level.Entities.Add(Box("ItemInkRibbon_SaveOffice", new Vector3(-7.25f, 0.62f, 0.1f), new Vector3(0.48f, 0.12f, 0.32f), ink, new Vector3(0f, 12f, 0f)));
        level.Entities.Add(Box("ItemKey_ArchiveKey", new Vector3(-5.25f, 0.78f, 2.45f), new Vector3(0.42f, 0.08f, 0.18f), brass, new Vector3(0f, 65f, 0f)));

        level.Entities.Add(Box("ArchiveCabinet_A", new Vector3(3.1f, 0.9f, 8.6f), new Vector3(1.1f, 1.8f, 0.7f), archive));
        level.Entities.Add(Box("ArchiveCabinet_B", new Vector3(5.4f, 0.9f, 10.6f), new Vector3(1.1f, 1.8f, 0.7f), archive));

        level.Entities.Add(DynamicBox("Crate_FoyerCorner", new Vector3(5.6f, 0.5f, -12.0f), new Vector3(1f, 1f, 1f), crate));
        level.Entities.Add(DynamicBox("Crate_Utility_A", new Vector3(6.7f, 0.5f, -2.25f), new Vector3(1f, 1f, 1f), crate, 12f, new Vector3(0f, 14f, 0f)));
        level.Entities.Add(DynamicBox("Crate_Utility_B", new Vector3(6.1f, 1.5f, -2.25f), new Vector3(1f, 1f, 1f), crate, 12f, new Vector3(0f, -8f, 0f)));
        level.Entities.Add(DynamicBox("Crate_SaveOffice", new Vector3(-7.15f, 0.5f, 2.45f), new Vector3(1f, 1f, 1f), crate));
        level.Entities.Add(DynamicBox("Crate_ArchiveLoose", new Vector3(1.4f, 0.5f, 10.9f), new Vector3(1f, 1f, 1f), crate, 10f, new Vector3(0f, 30f, 0f)));

        level.Entities.Add(new LevelEntityDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = EntityTypes.TriggerVolume,
            Name = "Trigger_SaveRoomEntry",
            LocalPosition = new Vector3(-4.0f, 1f, 0.8f),
            TriggerSize = new Vector3(2f, 2f, 2.5f),
            TriggerEvent = "Entered_SaveRoom"
        });

        return level;
    }
}

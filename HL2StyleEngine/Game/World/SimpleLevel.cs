using System.Collections.Generic;
using System.Numerics;
using Engine.Editor.Level;

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
}

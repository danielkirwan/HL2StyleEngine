using System.Collections.Generic;
using System.Numerics;

namespace Game.World;

public static class SimpleLevel
{
    public static List<BoxInstance> BuildRoom01()
    {
        var boxes = new List<BoxInstance>();

        // Colors
        Vector4 floor = new(0.25f, 0.25f, 0.25f, 1f);
        Vector4 wall = new(0.45f, 0.45f, 0.50f, 1f);
        Vector4 prop = new(0.60f, 0.45f, 0.25f, 1f);
        Vector4 pillar = new(0.35f, 0.35f, 0.35f, 1f);

        // Room dimensions
        float roomW = 18f;
        float roomD = 18f;
        float wallH = 4f;
        float wallT = 0.4f;

        // Floor (thin box)
        boxes.Add(new BoxInstance(
            position: new Vector3(0f, -0.1f, 0f),
            size: new Vector3(roomW, 0.2f, roomD),
            color: floor));

        // Walls (4 thin boxes around the edges)
        // +Z wall
        boxes.Add(new BoxInstance(
            new Vector3(0f, wallH * 0.5f, roomD * 0.5f),
            new Vector3(roomW, wallH, wallT),
            wall));

        // -Z wall
        boxes.Add(new BoxInstance(
            new Vector3(0f, wallH * 0.5f, -roomD * 0.5f),
            new Vector3(roomW, wallH, wallT),
            wall));

        // +X wall
        boxes.Add(new BoxInstance(
            new Vector3(roomW * 0.5f, wallH * 0.5f, 0f),
            new Vector3(wallT, wallH, roomD),
            wall));

        // -X wall
        boxes.Add(new BoxInstance(
            new Vector3(-roomW * 0.5f, wallH * 0.5f, 0f),
            new Vector3(wallT, wallH, roomD),
            wall));

        // A few props (crates)
        boxes.Add(new BoxInstance(new Vector3(2f, 0.5f, 2f), new Vector3(1f, 1f, 1f), prop));
        boxes.Add(new BoxInstance(new Vector3(-3f, 0.5f, -1f), new Vector3(1f, 1f, 1f), prop));
        boxes.Add(new BoxInstance(new Vector3(0f, 0.5f, -4f), new Vector3(1f, 1f, 1f), prop));

        // Pillars
        boxes.Add(new BoxInstance(new Vector3(5f, 2f, 5f), new Vector3(0.8f, 4f, 0.8f), pillar));
        boxes.Add(new BoxInstance(new Vector3(-5f, 2f, 5f), new Vector3(0.8f, 4f, 0.8f), pillar));
        boxes.Add(new BoxInstance(new Vector3(5f, 2f, -5f), new Vector3(0.8f, 4f, 0.8f), pillar));

        // A simple “step” / low platform
        boxes.Add(new BoxInstance(new Vector3(0f, 0.25f, 6f), new Vector3(4f, 0.5f, 3f), new Vector4(0.2f, 0.4f, 0.6f, 1f)));

        boxes.Add(new BoxInstance(new Vector3(0f, 0.5f, -2f), new Vector3(1f, 1f, 1f), prop));//Yellow cube
        boxes.Add(new BoxInstance(new Vector3(-5f, 2f, -5f), new Vector3(0.8f, 4f, 0.8f), pillar));

        return boxes;
    }

    /// <summary>
    /// Pure-data version of the default room, suitable for saving to JSON.
    /// </summary>
    public static LevelFile BuildRoom01File()
    {
        var instances = BuildRoom01();

        var level = new LevelFile { Version = 1 };

        int i = 0;
        foreach (var b in instances)
        {
            level.Boxes.Add(new BoxDef
            {
                Name = $"Box_{i++}",
                Position = b.Position,
                Size = b.Size,
                Color = b.Color
            });
        }

        return level;
    }
}

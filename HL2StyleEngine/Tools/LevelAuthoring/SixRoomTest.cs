using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Editor.Level;
using Engine.Editor.Editor;
using Engine.Physics.Dynamics;
using Engine.Physics.Collision;
using Engine.Render;
using Game;

internal static class SixRoomTest
{
    internal sealed record Room(string Name, float X, float Z, float Width, float Depth, float Height, string Floor)
    {
        public float Right => X + Width;
        public float Back => Z + Depth;
        public Vector2 Center => new(X + Width / 2, Z + Depth / 2);
        public bool Contains(float x, float z) => x >= X && x <= Right && z >= Z && z <= Back;
    }

    // One hub and five destinations. Connectors are short thresholds, not extra rooms.
    internal static readonly Room[] Rooms =
    [
        new("CentralHall", -14, -16, 28, 32, 5, "B"),
        new("FreightStore", -32, -21, 16, 20, 3.5f, "B"),
        new("Workshop", 16, -21, 16, 20, 3.5f, "D"),
        new("MedicalSupplies", -32, 1, 16, 20, 3.5f, "E"),
        new("UtilityRoom", 16, 1, 16, 20, 4, "F"),
        new("LoadingBay", -10, 18, 20, 16, 4, "B")
    ];

    internal static readonly Room[] Connections =
    [
        new("FreightThreshold", -16, -10, 2, 4, 3.2f, "B"),
        new("WorkshopThreshold", 14, -10, 2, 4, 3.2f, "B"),
        new("MedicalThreshold", -16, 6, 2, 4, 3.2f, "B"),
        new("UtilityThreshold", 14, 6, 2, 4, 3.2f, "B"),
        new("LoadingThreshold", -2, 16, 4, 2, 3.2f, "B")
    ];

    private const string ModelRoot = "Content/Models/ViewModels/";
    private const string Wall = "Basement_Corridor_A_Wall_Double_Sided_4m";
    private const string CrateModel = "Breakable_Wooden_Crate";

    internal static LevelFile Build()
    {
        var level = new LevelFile { UsePointLights = true };
        LevelEntityDef Add(string name, Vector3 position, Vector3 size, string mesh = "", bool solid = true, float yaw = 0)
        {
            var entity = new LevelEntityDef
            {
                Id = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("sixRoomTest/" + name))).ToLowerInvariant(),
                Name = name, Type = solid ? EntityTypes.RigidBody : EntityTypes.Prop,
                LocalPosition = position, LocalScale = Vector3.One,
                LocalRotationEulerDeg = new Vector3(0, yaw, 0), Size = size,
                Color = mesh.Length > 0 ? Vector4.One : Vector4.Zero,
                MeshPath = mesh.Length > 0 ? ModelRoot + mesh + ".glb" : "",
                Shape = "Box", MotionType = MotionType.Static, Mass = 0,
                Friction = 1, Restitution = 0
            };
            level.Entities.Add(entity);
            return entity;
        }

        void ShellFloor(Room room)
        {
            Add(room.Name + "_FloorCollision", new(room.Center.X, -0.2f, room.Center.Y), new(room.Width, 0.4f, room.Depth));
            Add(room.Name + "_CeilingCollision", new(room.Center.X, room.Height + 0.2f, room.Center.Y), new(room.Width, 0.4f, room.Depth));
            for (float x = room.X; x < room.Right; x += 4)
            for (float z = room.Z; z < room.Back; z += 4)
            {
                float width = MathF.Min(4, room.Right - x), depth = MathF.Min(4, room.Back - z);
                bool relief = room.Floor == "B";
                Add($"{room.Name}_Floor_{x}_{z}", new(x + width / 2, relief ? -0.06f : 0, z + depth / 2),
                    new(width, relief ? 0.12f : 0.001f, depth), $"Basement_Corridor_{room.Floor}_Floor_4x4m", false);
                Add($"{room.Name}_Ceiling_{x}_{z}", new(x + width / 2, room.Height + 0.15f, z + depth / 2),
                    new(width, 0.3f, depth), "Basement_Corridor_A_Ceiling_4x4m", false);
            }
        }

        void WallRun(string name, bool alongX, float fixedCoordinate, float from, float to, float bottom, float top)
        {
            for (float start = from; start < to - 0.001f; start += 4)
            {
                float length = MathF.Min(4, to - start);
                var position = alongX
                    ? new Vector3(start + length / 2, (bottom + top) / 2, fixedCoordinate)
                    : new Vector3(fixedCoordinate, (bottom + top) / 2, start + length / 2);
                Add($"{name}_{start}_{bottom}", position, new(0.2f, top - bottom, length), Wall, true, alongX ? 90 : 0);
            }
        }

        void Boundary(Room room, string side, params float[] openings)
        {
            bool alongX = side is "Front" or "Back";
            float edge = side switch { "Front" => room.Z, "Back" => room.Back, "Left" => room.X, _ => room.Right };
            float start = alongX ? room.X : room.Z, end = alongX ? room.Right : room.Back;
            foreach (float center in openings.Order())
            {
                WallRun(room.Name + "_" + side, alongX, edge, start, center - 2, 0, room.Height);
                WallRun(room.Name + "_" + side + "_Header", alongX, edge, center - 2, center + 2, 3.2f, room.Height);
                start = center + 2;
            }
            WallRun(room.Name + "_" + side, alongX, edge, start, end, 0, room.Height);
        }

        foreach (var room in Rooms.Concat(Connections)) ShellFloor(room);
        Boundary(Rooms[0], "Front"); Boundary(Rooms[0], "Back", 0);
        Boundary(Rooms[0], "Left", -8, 8); Boundary(Rooms[0], "Right", -8, 8);
        for (int i = 1; i <= 4; i++)
        {
            Room room = Rooms[i];
            Boundary(room, "Front"); Boundary(room, "Back");
            Boundary(room, i % 2 == 1 ? "Left" : "Right");
            Boundary(room, i % 2 == 1 ? "Right" : "Left", i <= 2 ? -8 : 8);
        }
        Boundary(Rooms[5], "Front", 0); Boundary(Rooms[5], "Back");
        Boundary(Rooms[5], "Left"); Boundary(Rooms[5], "Right");
        foreach (Room link in Connections)
        {
            if (link.Width == 2) { Boundary(link, "Front"); Boundary(link, "Back"); }
            else { Boundary(link, "Left"); Boundary(link, "Right"); }
        }

        var spawn = Add("PlayerSpawn", new(0, 0.06f, -12), Vector3.One, solid: false);
        spawn.Type = EntityTypes.PlayerSpawn;

        void Light(string name, Vector3 position, Vector3 color, float range = 12, float intensity = 1.7f)
        {
            var light = Add("Light_" + name, position, new(0.12f), solid: false);
            light.Type = EntityTypes.PointLight;
            light.LightColor = new Vector4(color, 1);
            light.Intensity = intensity; light.Range = range;
            // Visible fixture geometry stays separate from its light entity.
            Add("Fixture_" + name, position + new Vector3(0, 0.13f, 0), new(0.16f, 0.15f, 1.6f), "Beam_4m", false);
            Add("FixtureDiffuser_" + name, position, new(0.13f, 0.04f, 1.4f)).Color = new Vector4(color, 1);
        }

        foreach (Room room in Rooms)
        {
            Vector3 color = room.Name switch
            {
                "MedicalSupplies" => new(0.78f, 0.94f, 1),
                "Workshop" => new(1, 0.9f, 0.74f),
                "UtilityRoom" => new(0.82f, 1, 0.9f),
                _ => new(1, 0.96f, 0.88f)
            };
            int cols = (int)MathF.Ceiling(room.Width / 8), rows = (int)MathF.Ceiling(room.Depth / 8);
            for (int x = 0; x < cols; x++)
            for (int z = 0; z < rows; z++)
                Light($"{room.Name}_{x}_{z}", new(room.X + (x + 0.5f) * room.Width / cols, room.Height - 0.35f,
                    room.Z + (z + 0.5f) * room.Depth / rows), color);
        }
        foreach (Room link in Connections)
            Light(link.Name, new(link.Center.X, 2.9f, link.Center.Y), Vector3.One, 7, 1.1f);

        void Crate(string name, float x, float z, float bottom = 0.04f, float size = 1)
        {
            var crate = Add("Crate_SixRoom_" + name, new(x, bottom + size / 2, z), new(size), CrateModel);
            crate.CanPickUp = true; crate.MotionType = MotionType.Dynamic; crate.Mass = 12;
            crate.Damageable = true; crate.MaxHealth = 100;
        }
        void Pickup(string item, string name, Vector3 position, Vector3 size, string mesh)
        {
            var pickup = Add($"Item_{item}__SixRoom_{name}", position, size, mesh);
            pickup.CanPickUp = true; pickup.MotionType = MotionType.Dynamic; pickup.Mass = 1.2f;
        }
        void Battery(string name, float x, float z, float bottom = 0.05f)
            => Pickup("SuitBattery", name, new(x, bottom + 0.23f, z), new(0.23f, 0.46f, 0.23f), "Battery07");
        void Medkit(string name, float x, float z, float bottom = 0.05f)
            => Pickup("HealthPack", name, new(x, bottom + 0.18f, z), new(0.63f, 0.36f, 0.39f), "FirstAidKit01");
        void Ammo(string name, float x, float z)
            => Pickup("Bullets", name + "__x24", new(x, 0.3f, z), new(0.5f), CrateModel);
        void Bench(string name, float x, float z, float width = 4, float height = 0.9f)
            => Add(name, new(x, height / 2, z), new(width, height, 1.2f), "Platform_4m");

        // The central spine stays open. Structural frames and equipment flank it.
        foreach (float x in new[] { -8f, 8f })
        foreach (float z in new[] { -6f, 6f })
            Add($"CentralHall_Column_{x}_{z}", new(x, 2.5f, z), new(0.7f, 5, 0.38f), "Basement_Corridor_E_Pillar_A");
        foreach (float x in new[] { -8f, 8f })
            Add($"CentralHall_OverheadBeam_{x}", new(x, 4.8f, 0), new(0.25f, 0.4f, 32), "Beam_4m", false);
        Bench("CentralHall_DispatchDesk", -7, -12, 3);
        Add("CentralHall_DispatchLaptop", new(-7, 1.05f, -12), new(0.55f, 0.3f, 0.42f), "Laptop Opened", false);
        Add("CentralHall_DispatchTelephone", new(-8, 1.05f, -12), new(0.38f, 0.3f, 0.3f), "RedTelephone", false);
        Crate("HallWelcome", 6, -11); Crate("HallNorth", -7, 12);
        Ammo("Welcome", 7.5f, -11); Battery("Hall", -10, 12);

        // Freight store: perimeter stacks, a wide forklift-like aisle and loose throwables.
        for (int row = 0; row < 3; row++)
        {
            float z = -17 + row * 6;
            Crate($"Freight_{row}_A", -28, z);
            Crate($"Freight_{row}_B", -26.8f, z);
            Crate($"Freight_{row}_Top", -28, z, 1.08f);
        }
        Crate("FreightLoose_A", -21, -14); Crate("FreightLoose_B", -22, -4);
        Medkit("FreightCache", -29.5f, -4); Ammo("Freight", -29.5f, -5);

        // Workshop: a repair island and a clearly visible upper shelf for gravity-gun reach.
        Bench("Workshop_RepairBench", 24, -15, 5);
        Add("Workshop_Console", new(24, 1.12f, -15), new(0.8f, 0.4f, 0.7f), "Console 1", false);
        Add("Workshop_UpperShelf", new(29, 2.3f, -15), new(0.7f, 0.15f, 4), "Beam_4m");
        Battery("WorkshopHigh_A", 29, -14, 2.44f); Battery("WorkshopHigh_B", 29, -16, 2.44f);
        Crate("WorkshopLoose_A", 20, -18); Crate("WorkshopLoose_B", 27, -6);
        Add("Workshop_Arcade", new(29, 1.1f, -3), new(0.85f, 2.2f, 0.9f), "Arcade_Machine_1");
        Ammo("Workshop", 21, -18);

        // Medical room: calm supply counter and a separate cache worth walking around it for.
        Bench("Medical_SupplyCounter", -26, 15, 5);
        Medkit("MedicalCounter_A", -27, 15, 0.96f); Medkit("MedicalCounter_B", -25, 15, 0.96f);
        Battery("MedicalCounter", -26, 15, 0.96f);
        Add("Medical_Telephone", new(-24, 1.1f, 15), new(0.38f, 0.3f, 0.3f), "RedTelephone", false);
        Crate("MedicalStore_A", -29, 4); Crate("MedicalStore_B", -29, 5.2f);
        Medkit("MedicalSideCache", -30, 18);

        // Utility room: pipe racks establish two optional approaches around the equipment.
        foreach (float z in new[] { 4f, 10f, 16f })
        {
            Add($"Utility_PipeRack_{z}", new(30, 1.5f, z), new(0.16f, 3, 0.85f), "Utility_Pipes_A");
            Add($"Utility_Panel_{z}", new(25, 1.3f, z), new(1.4f, 2.5f, 0.2f), "Electrical Panel");
        }
        Crate("UtilityLoose_A", 20, 4); Crate("UtilityLoose_B", 20, 16);
        Battery("UtilitySupplies", 29, 18); Medkit("UtilitySupplies", 29, 17);
        Ammo("Utility", 21, 16);

        // Loading bay: long throw/shot lanes terminate at crates and a solid back wall.
        for (int i = 0; i < 5; i++) Crate("LoadingTarget_" + i, -6 + i * 3, 30);
        Crate("LoadingThrow_A", -6, 22); Crate("LoadingThrow_B", -4, 22);
        Ammo("Loading", 6, 22); Ammo("LoadingExtra", 7, 22);
        Bench("Loading_ObservationStepLow", 6, 26, 4, 0.24f);
        Bench("Loading_ObservationStepHigh", 6, 27.2f, 4, 0.48f);
        Battery("LoadingReward", 7, 27.2f, 0.54f);

        return level;
    }

    internal static void Validate(string root, string path)
    {
        var level = LevelIO.Load(path);
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
        Require(level.Entities.Select(e => e.Id).Distinct().Count() == level.Entities.Count, "Duplicate entity IDs.");
        Require(level.Entities.Select(e => e.Name).Distinct().Count() == level.Entities.Count, "Duplicate entity names.");
        Require(level.Entities.Count(e => e.Type == EntityTypes.PlayerSpawn) == 1, "Expected one player spawn.");
        Require(level.Entities.All(e => e.Interaction == null && e.Scripts.Count == 0), "Unexpected puzzle, lock or script.");
        Require(level.UsePointLights, "Scene lighting must be enabled.");
        var editor = new LevelEditorController();
        editor.LoadFromMemory(path, level);
        Require(editor.DrawBoxes.Count == level.Entities.Count, "Editor lost scene entities.");
        for (int i = 0; i < level.Entities.Count; i++)
        {
            Require(editor.TryGetEntityWorldTRS(i, out var position, out _, out _), "Editor transform could not be resolved.");
            Require(Vector3.Distance(position, level.Entities[i].LocalPosition) < 0.001f, "Editor changed an authored position.");
        }
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<LevelFile>(System.Text.Json.JsonSerializer.Serialize(level));
        Require(roundTrip?.UsePointLights == true && roundTrip.Entities.Count == level.Entities.Count, "Level settings did not survive serialization.");

        int parts = 0;
        foreach (string mesh in level.Entities.Select(e => e.MeshPath).Where(p => p.Length > 0).Distinct())
        {
            var model = GlbModelLoader.Load(Path.Combine(root, "Game", mesh));
            Require(model.Parts.Count > 0, "Empty model: " + mesh);
            Require(model.Parts.All(p => p.Positions.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z))), "Invalid vertices: " + mesh);
            parts += model.Parts.Count;
        }

        // Sample player-radius clearance at ground level, then flood-fill from the authored spawn.
        var obstacles = level.Entities.Where(e => e.Type == EntityTypes.RigidBody && e.MotionType == MotionType.Static)
            .Select(e =>
            {
                Vector3 size = e.Size, p = e.LocalPosition, r = e.LocalRotationEulerDeg;
                if (MathF.Abs(r.Y % 180) > 45) size = new(size.Z, size.Y, size.X);
                return (Min: p - size / 2, Max: p + size / 2, e.Name);
            }).Where(b => b.Max.Y > 0.3f && b.Min.Y < 1.8f).ToArray();
        const float step = 0.5f, radius = 0.35f;
        bool Walkable((int X, int Z) cell)
        {
            float x = cell.X * step, z = cell.Z * step;
            if (x < -36 || x > 36 || z < -25 || z > 38) return false;
            return !obstacles.Any(b => x > b.Min.X - radius && x < b.Max.X + radius && z > b.Min.Z - radius && z < b.Max.Z + radius);
        }
        var spawn = (Vector3)level.Entities.Single(e => e.Type == EntityTypes.PlayerSpawn).LocalPosition;
        var start = ((int)(spawn.X / step), (int)(spawn.Z / step));
        Require(Walkable(start), "Spawn is obstructed.");
        var visited = new HashSet<(int X, int Z)> { start };
        var queue = new Queue<(int X, int Z)>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        {
            foreach (var next in new[] { (cell.X + 1, cell.Z), (cell.X - 1, cell.Z), (cell.X, cell.Z + 1), (cell.X, cell.Z - 1) })
                if (Walkable(next) && visited.Add(next)) queue.Enqueue(next);
        }
        foreach (Room room in Rooms.Concat(Connections))
            Require(visited.Contains(((int)(room.Center.X / step), (int)(room.Center.Y / step))), "Unreachable room/threshold: " + room.Name);
        Require(visited.All(p => Rooms.Concat(Connections).Any(r => r.Contains(p.X * step, p.Z * step))), "The level perimeter has a walkable leak.");

        foreach (var entity in level.Entities.Where(e => e.MotionType == MotionType.Dynamic))
        {
            Vector3 p = entity.LocalPosition, size = entity.Size;
            Require(Rooms.Any(r => r.Contains(p.X, p.Z)), "Pickup outside level: " + entity.Name);
            Require(p.Y - size.Y / 2 >= 0.025f, "Dynamic object intersects floor: " + entity.Name);
            Require(!obstacles.Any(b => p.X + size.X / 2 > b.Min.X && p.X - size.X / 2 < b.Max.X &&
                p.Z + size.Z / 2 > b.Min.Z && p.Z - size.Z / 2 < b.Max.Z &&
                p.Y + size.Y / 2 > b.Min.Y && p.Y - size.Y / 2 < b.Max.Y), "Dynamic object intersects static geometry: " + entity.Name);
        }
        float area = Rooms.Concat(Connections).Sum(r => r.Width * r.Depth);
        Require(MathF.Abs(area / (18 * 28) - 5) < 0.1f, "Level does not meet reference scale.");
        Console.WriteLine($"PASS: six rooms, five open connections, {area} m2 ({area / 504:F2}x reference), {visited.Count} reachable floor samples.");
        Console.WriteLine($"PASS: {level.Entities.Count} unique entities; all asset paths and {parts} model parts load; all dynamic props clear static geometry.");
        Console.WriteLine($"PASS: {level.Entities.Count(e => e.Damageable)} breakable crates; {level.Entities.Count(e => e.Type == EntityTypes.PointLight)} lights; no puzzles or locks.");
        Console.WriteLine("PASS: editor loads all entity transforms; lighting and entity count survive serialization.");
        ValidatePhysics(level, Require);
    }

    private static void ValidatePhysics(LevelFile level, Action<bool, string> require)
    {
        var colliders = level.Entities.Where(e => e.Type == EntityTypes.RigidBody && e.MotionType == MotionType.Static)
            .Select(e => WorldCollider.Box(e.LocalPosition, (Vector3)e.Size / 2,
                Quaternion.CreateFromYawPitchRoll(((Vector3)e.LocalRotationEulerDeg).Y * MathF.PI / 180, 0, 0))).ToArray();
        var routes = new Vector2[][]
        {
            [new(0, -8), new(-24, -8), new(-24, -11), new(-24, -8), new(0, -8)],
            [new(0, -8), new(24, -8), new(24, -11), new(24, -8), new(0, -8)],
            [new(0, 8), new(-24, 8), new(-24, 11), new(-24, 8), new(0, 8)],
            [new(0, 8), new(24, 8), new(24, 11), new(24, 8), new(0, 8)],
            [new(0, 0), new(0, 26), new(0, 0)]
        };
        foreach (var route in routes)
        {
            var motor = new SourcePlayerMotor(new SourceMovementSettings(), new(0, 0.06f, -12));
            foreach (var point in route)
            {
                int tick = 0;
                while (Vector2.Distance(new(motor.Position.X, motor.Position.Z), point) > 0.2f && tick++ < 900)
                {
                    Vector3 offset = new(point.X - motor.Position.X, 0, point.Y - motor.Position.Z);
                    motor.Step(1f / 60, Vector3.Normalize(offset), MathF.Min(7, offset.Length() * 5), colliders);
                }
                require(tick < 900, $"Player motor blocked en route to {point}: {motor.Position}");
                require(motor.Position.Y >= -0.01f && motor.Position.Y < 0.1f, "Player feet lost the floor.");
            }
        }
        foreach (var entity in level.Entities.Where(e => e.MotionType == MotionType.Dynamic))
        {
            var box = new BoxBody(entity.LocalPosition, (Vector3)entity.Size / 2)
            {
                Restitution = entity.Restitution, Friction = entity.Friction
            };
            for (int tick = 0; tick < 180; tick++) box.Step(1f / 60, colliders, Quaternion.Identity);
            require(box.Center.Y - box.HalfExtents.Y >= -0.01f, "Physics prop fell through the floor: " + entity.Name);
            require(MathF.Abs(box.Velocity.Y) < 0.05f, "Physics prop failed to settle: " + entity.Name);
        }
        Console.WriteLine("PASS: production player motor traverses all five branches and returns; production box physics settles every pickup/crate on solid geometry.");
    }
}

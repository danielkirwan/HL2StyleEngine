using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Engine.Editor.Editor;
using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using Engine.Runtime.Entities;
using Engine.Runtime.Entities.Interfaces;
using Engine.Runtime.Hosting;
using Game;

internal static class PhysicsReplayChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(string root, string level, string? reference = null)
    {
        if (level is not ("atmosphereTest" or "sixRoomShadowTest")) throw new ArgumentException("Unknown replay level.");
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "physics-replay-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        {
            using var host = new EngineHost(800, 600, "Deterministic physics replay");
            using var game = new HL2GameModule(Path.Combine(root, "Game/Content/Levels", level + ".json"), saveDirectory: Path.Combine(folder, "Saves"));
            game.Initialize(host.Context);
            var entities = (List<Entity>)Field(game, "_runtimeEntities");
            var motor = (SourcePlayerMotor)Field(game, "_motor");
            var platform = Body("probe-platform", RuntimeShapeKind.Box, motor.Position + new Vector3(4, .6f, 0));
            platform.Collider.Size = new(4, .4f, 4);
            platform.Collider.IsMovingPlatform = true;
            platform.Physics.BoxBody!.HalfExtents = platform.Collider.HalfExtents;
            platform.Physics.BoxBody.IsKinematic = true;
            platform.Physics.BoxBody.UseGravity = false;
            platform.Physics.MotionType = MotionType.Kinematic;
            platform.Components.Add(new Mover(platform));
            entities.Add(platform);
            var box = Body("probe-box", RuntimeShapeKind.Box, platform.Transform.Position + new Vector3(0, .7f, 0));
            entities.Add(box);
            entities.Add(Body("probe-stack", RuntimeShapeKind.Box, box.Transform.Position + new Vector3(.1f, 1, 0)));
            var sphere = Body("probe-sphere", RuntimeShapeKind.Sphere, platform.Transform.Position + new Vector3(1.2f, 1.6f, 0));
            entities.Add(sphere);
            entities.Add(Body("probe-capsule", RuntimeShapeKind.Capsule, platform.Transform.Position + new Vector3(-1.1f, 2, 0)));
            var hashes = new List<string>();
            var timings = new List<double>();
            double jit = 0;
            for (int frame = 0; frame < 600; frame++)
            {
                Set(game, "_wishDir", frame % 180 < 90 ? Vector3.UnitZ : -Vector3.UnitZ);
                Set(game, "_wishSpeed", 2f);
                if (frame == 60)
                {
                    var editor = (LevelEditorController)Field(game, "_editor");
                    ((HashSet<string>)Field(game, "_solvedPuzzles")).UnionWith(editor.LevelFile.Entities.Where(e => e.Interaction != null).Select(e => e.Interaction!.StateId));
                }
                if (frame == 120) Call(game, "PickUp", box, true);
                if (frame == 180) Call(game, "DropHeld");
                if (frame == 240)
                {
                    box.Physics.BoxBody!.Center = platform.Transform.Position + new Vector3(0, 3, 0);
                    box.Transform.Position = box.Physics.BoxBody.Center;
                    box.Physics.Rotation = Quaternion.CreateFromYawPitchRoll(.4f, .2f, .3f);
                }
                if (frame == 300) box.Physics.BoxBody!.HalfExtents = (box.Collider.Size = new(.6f, 1.2f, .8f)) * .5f;
                if (frame == 360)
                {
                    sphere.Physics.BoxBody = new(sphere.Physics.SphereBody!.Center, new Vector3(.4f));
                    sphere.Physics.SphereBody = null;
                    sphere.Collider.Shape = sphere.Render.Shape = RuntimeShapeKind.Box;
                    sphere.Collider.Size = new(.8f);
                }
                TimeSpan jitStart = System.Runtime.JitInfo.GetCompilationTime(true);
                long start = Stopwatch.GetTimestamp();
                game.FixedUpdate(1f / 60);
                timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                jit += (System.Runtime.JitInfo.GetCompilationTime(true) - jitStart).TotalMilliseconds;
                using var bytes = new MemoryStream();
                using var writer = new BinaryWriter(bytes);
                Vec(writer, motor.Position); Vec(writer, motor.Velocity);
                foreach (var e in entities)
                {
                    Vec(writer, e.Transform.Position); Vec(writer, e.Transform.RotationEulerDeg);
                    Vec(writer, e.Physics.AngularVelocity);
                    var q = e.Physics.Rotation;
                    writer.Write(q.X); writer.Write(q.Y); writer.Write(q.Z); writer.Write(q.W);
                    writer.Write(e.IsHeld);
                    if (e.Physics.BoxBody is { } b) { Vec(writer, b.Center); Vec(writer, b.Velocity); Contact(writer, b.LastContactManifold); Contact(writer, b.LastSupportManifold); }
                    if (e.Physics.SphereBody is { } s) { Vec(writer, s.Center); Vec(writer, s.Velocity); Contact(writer, s.LastContactManifold); Contact(writer, s.LastSupportManifold); }
                    if (e.Physics.CapsuleBody is { } c) { Vec(writer, c.Center); Vec(writer, c.Velocity); Contact(writer, c.LastContactManifold); Contact(writer, c.LastSupportManifold); }
                }
                foreach (var collider in (List<WorldCollider>)Field(game, "_runtimeWorldColliders"))
                { writer.Write((int)collider.Shape); Vec(writer, collider.GetAabb().Min); Vec(writer, collider.GetAabb().Max); }
                hashes.Add(Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
            }
            string path = Path.Combine(folder, level + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Hashes = hashes, FixedStepMilliseconds = timings, MainThreadJitMilliseconds = jit }, new JsonSerializerOptions { WriteIndented = true }));
            if (reference != null)
            {
                using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(reference, level + ".json")));
                string[] before = expected.RootElement.GetProperty("Hashes").EnumerateArray().Select(e => e.GetString()!).ToArray();
                if (!before.SequenceEqual(hashes))
                    throw new InvalidDataException($"Physics replay changed at frame {Enumerable.Range(0, Math.Min(before.Length, hashes.Count)).FirstOrDefault(i => before[i] != hashes[i], -1)}: {path}");
            }
            Console.WriteLine($"[PhysicsReplay] {level}: 600 steps, {timings.Sum():F2}ms physics, {jit:F2}ms main-thread JIT; {(reference == null ? "reference recorded" : "all state/contact/bounds hashes match")}. {path}");
        }
    }
    private static Entity Body(string name, RuntimeShapeKind shape, Vector3 position)
    {
        var entity = new Entity(name, "RigidBody", name) { CanPickUp = true };
        entity.Transform.Position = position;
        entity.Collider.Shape = entity.Render.Shape = shape;
        entity.Physics.MotionType = MotionType.Dynamic;
        entity.Collider.Size = Vector3.One;
        entity.Collider.Radius = .3f;
        entity.Collider.Height = 1.2f;
        if (shape == RuntimeShapeKind.Box) entity.Physics.BoxBody = new(position, new(.5f));
        if (shape == RuntimeShapeKind.Sphere) entity.Physics.SphereBody = new(position, .3f);
        if (shape == RuntimeShapeKind.Capsule) entity.Physics.CapsuleBody = new(position, .3f, 1.2f);
        return entity;
    }
    private static void Vec(BinaryWriter writer, Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new InvalidDataException("Nonfinite physics state.");
        writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z);
    }
    private static void Contact(BinaryWriter writer, ContactManifold m)
    { Vec(writer, m.Normal); writer.Write(m.Penetration); writer.Write(m.ContactCount); for (int i = 0; i < m.ContactCount; i++) Vec(writer, m.GetPoint(i)); }
    private static object Field(HL2GameModule game, string name) => typeof(HL2GameModule).GetField(name, Flags)!.GetValue(game)!;
    private static void Set(HL2GameModule game, string name, object value) => typeof(HL2GameModule).GetField(name, Flags)!.SetValue(game, value);
    private static object? Call(HL2GameModule game, string name, params object[] args) => typeof(HL2GameModule).GetMethod(name, Flags)!.Invoke(game, args);
    private sealed class Mover(Entity entity) : IComponent
    {
        private readonly Vector3 _start = entity.Transform.Position;
        private float _time;
        public void Update(float dt) { _time += dt; entity.Transform.Position = _start + new Vector3(MathF.Sin(_time) * .7f, MathF.Sin(_time * .5f) * .1f, 0); }
    }
}

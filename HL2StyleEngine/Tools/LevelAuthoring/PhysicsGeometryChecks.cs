using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Core.Diagnostics;
using Engine.Physics.Collision;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game;

internal static class PhysicsGeometryChecks
{
    internal static void Validate()
    {
        bool reuse = WorldColliderCache.ReuseEnabled, stats = WorldColliderCache.CollectStatistics;
        int checks = 0;
        void Check(bool condition) { if (!condition) throw new InvalidDataException("Collider geometry reuse regression."); checks++; }
        void Equal(WorldCollider expected, WorldCollider actual)
        {
            Check(expected.Shape == actual.Shape && expected.Center == actual.Center && expected.Rotation == actual.Rotation &&
                expected.HalfExtents == actual.HalfExtents && expected.Radius == actual.Radius && expected.Height == actual.Height &&
                ReferenceEquals(expected.MeshData, actual.MeshData) && expected.GetAabb().Min == actual.GetAabb().Min && expected.GetAabb().Max == actual.GetAabb().Max);
            var floor = WorldCollider.Box(new(0, -.5f, 0), new(8, .5f, 8), Quaternion.Identity);
            bool a = ShapeCollision.TryResolve(expected, floor, out var ma), b = ShapeCollision.TryResolve(actual, floor, out var mb);
            Check(a == b && ma.Normal == mb.Normal && ma.Penetration == mb.Penetration && ma.ContactCount == mb.ContactCount);
            for (int i = 0; i < ma.ContactCount; i++) Check(ma.GetPoint(i) == mb.GetPoint(i));
        }
        try
        {
            WorldColliderCache.ReuseEnabled = WorldColliderCache.CollectStatistics = true;
            WorldColliderCache.ResetStatistics();
            var cache = new WorldColliderCache();
            var random = new Random(1956);
            for (int i = 0; i < 1000; i++)
            {
                Vector3 p = new(random.NextSingle() * 4, random.NextSingle(), random.NextSingle() * 4);
                Vector3 size = new(.2f + random.NextSingle(), .2f + random.NextSingle(), .2f + random.NextSingle());
                Quaternion q = Quaternion.CreateFromYawPitchRoll(random.NextSingle(), random.NextSingle(), random.NextSingle());
                q *= i % 2 == 0 ? 2 : 1; // Non-normalized input must still hit on repetition.
                foreach (int shape in new[] { 0, 1, 2 })
                {
                    WorldCollider Get() => shape switch { 0 => cache.Box(p, size, q), 1 => cache.Sphere(p, size.X), _ => cache.Capsule(p, size.X, size.Y * 2, q) };
                    WorldCollider Fresh() => shape switch { 0 => WorldCollider.Box(p, size, q), 1 => WorldCollider.Sphere(p, size.X), _ => WorldCollider.Capsule(p, size.X, size.Y * 2, q) };
                    Equal(Fresh(), Get());
                    long builds = WorldColliderCache.Builds;
                    Equal(Fresh(), Get()); Check(WorldColliderCache.Builds == builds);
                    p.X += .01f; Equal(Fresh(), Get()); Check(WorldColliderCache.Builds == ++builds);
                    size.X += .01f; Equal(Fresh(), Get()); Check(WorldColliderCache.Builds == ++builds);
                    if (shape != 1) { q *= 2; Equal(Fresh(), Get()); Check(WorldColliderCache.Builds == ++builds); }
                    if (shape == 2) { size.Y += .1f; Equal(Fresh(), Get()); Check(WorldColliderCache.Builds == ++builds); }
                }
            }
            var meshA = new MeshCollisionMesh([new(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ)]);
            var meshB = new MeshCollisionMesh([new(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX)]);
            Equal(WorldCollider.Mesh(meshA), cache.Mesh(meshA));
            long before = WorldColliderCache.Builds;
            Equal(WorldCollider.Mesh(meshA), cache.Mesh(meshA)); Check(WorldColliderCache.Builds == before);
            Equal(WorldCollider.Mesh(meshB), cache.Mesh(meshB)); Check(WorldColliderCache.Builds == ++before);
            cache.Clear(); Equal(WorldCollider.Mesh(meshB), cache.Mesh(meshB)); Check(WorldColliderCache.Builds == ++before);
            WorldColliderCache.ReuseEnabled = false;
            Equal(WorldCollider.Mesh(meshB), cache.Mesh(meshB)); Check(WorldColliderCache.Builds == ++before);
            WorldColliderCache.ReuseEnabled = true;
            Equal(WorldCollider.Mesh(meshB), cache.Mesh(meshB)); Check(WorldColliderCache.Builds == before);
            Console.WriteLine($"[PhysicsGeometry] PASS: {checks} exact geometry/contact checks; pose, dimensions, raw rotations, shape, mesh replacement, clear and bypass.");
        }
        finally { WorldColliderCache.ReuseEnabled = reuse; WorldColliderCache.CollectStatistics = stats; WorldColliderCache.ResetStatistics(); }
    }

    internal static void StartupChild(string manifest, string report, bool reuse)
    {
        WorldColliderCache.ReuseEnabled = reuse;
        WorldColliderCache.CollectStatistics = true;
        WorldColliderCache.ResetStatistics();
        var profile = new LoadProfile(report, exitWhenComplete: true);
        var package = new AssetPackage(manifest);
        using var host = new EngineHost(1280, 720, "Physics startup comparison", profile);
        using var game = new HL2GameModule(package.LevelPath, package.Root, package);
        host.Run(game);
        File.WriteAllText(report + ".geometry.json", JsonSerializer.Serialize(new { Reuse = reuse, WorldColliderCache.Requests, WorldColliderCache.Builds }));
        if (!profile.Succeeded) throw new InvalidDataException("Startup did not reach playable state.");
    }

    internal static void Benchmark(string root)
    {
        string manifest = LoadingPackageChecks.Build(root);
        string output = Path.Combine(AppContext.BaseDirectory, "qa", "physics-startup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        for (int i = 0; i < 8; i++)
        {
            bool reuse = i % 4 is 1 or 2;
            string report = Path.Combine(output, $"{i:00}-{(reuse ? "reuse" : "rebuild")}.json");
            var start = new ProcessStartInfo(Path.Combine(root, ".dotnet/dotnet.exe")) { UseShellExecute = false, WorkingDirectory = root };
            foreach (string arg in new[] { typeof(PhysicsGeometryChecks).Assembly.Location, "physics-startup-child", manifest, report, reuse.ToString() }) start.ArgumentList.Add(arg);
            start.Environment["HS2_LOAD_CACHE_LABEL"] = "Fresh process; warmed OS cache; same-binary geometry reuse A/B; both include local support reuse";
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Probe did not start.");
            if (!child.WaitForExit(90000)) { child.Kill(true); child.WaitForExit(); throw new TimeoutException("Physics startup exceeded 90 seconds."); }
            if (child.ExitCode != 0) throw new InvalidOperationException("Probe failed: " + report);
            using var json = JsonDocument.Parse(File.ReadAllText(report));
            Console.WriteLine($"[PhysicsStartup] {(i < 2 ? "warmup" : "sample")} {Path.GetFileName(report)}: {json.RootElement.GetProperty("ProcessMilliseconds").GetDouble():F1} ms");
        }
        Console.WriteLine("[PhysicsStartup] Reports: " + output);
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Render;

internal static class EngineUpgradeChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }

    public static void Run(string root)
    {
        CheckTextures();
        CheckMeshQueries();
        var original = LevelIO.Load(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
        original.Exposure = 1.7f; original.AmbientLight = .08f; original.DirectionalLight = 0;
        var light = original.Entities.First(e => e.Type == EntityTypes.PointLight);
        light.IsSpotLight = true; light.CastShadows = true; light.LightEnabled = false; light.SpotAngleDeg = 75;
        var copy = Engine.Core.Serialization.StructuredData.ReadJson<LevelFile>(Engine.Core.Serialization.StructuredData.WriteJson(original));
        Require(copy!.Exposure == 1.7f && copy.AmbientLight == .08f && copy.DirectionalLight == 0, "Lighting settings failed round trip.");
        var copiedLight = copy.Entities.First(e => e.Id == light.Id);
        Require(copiedLight.IsSpotLight && copiedLight.CastShadows && !copiedLight.LightEnabled && copiedLight.SpotAngleDeg == 75, "Fixture settings failed round trip.");
        Console.WriteLine("PASS: scene/fixture lighting settings serialize without changing authored files.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ImageBytes()
    {
        using var image = new Bitmap(16, 8);
        for (int y = 0; y < 8; y++) for (int x = 0; x < 16; x++)
            image.SetPixel(x, y, Color.FromArgb(255, x * 16, y * 32, 128));
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    private static void CheckTextures()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string folder = Path.Combine(Path.GetTempPath(), "HS2TextureChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            byte[] png = ImageBytes();
            var cooked = TextureCooker.PrepareImage(png, TextureSemantic.Color, folder, true);
            Require(cooked.Compressed && cooked.Mips.Length == 5, "BC7 or mip chain missing.");
            Require(cooked.Mips[0].Length == 128, "BC7 payload has wrong size.");
            string path = Path.Combine(folder, cooked.Key + ".hs2tex");
            Require(TextureCooker.TryRead(path, cooked.Key, TextureSemantic.Color, out var read) &&
                read!.Mips.Zip(cooked.Mips).All(pair => pair.First.SequenceEqual(pair.Second)), "LZ4 texture round trip failed.");
            byte[] file = File.ReadAllBytes(path);
            file[^1] ^= 128; File.WriteAllBytes(path, file);
            Require(!TextureCooker.TryRead(path, cooked.Key, TextureSemantic.Color, out _), "Corrupt texture accepted.");
            TextureCooker.PrepareImage(png, TextureSemantic.Color, folder, true);
            Require(TextureCooker.TryRead(path, cooked.Key, TextureSemantic.Color, out _), "Corrupt cache not rebuilt.");
            Require(!TextureCooker.TryRead(path, cooked.Key + "changed", TextureSemantic.Color, out _), "Stale identity accepted.");
            File.WriteAllBytes(path, file[..16]);
            Require(!TextureCooker.TryRead(path, cooked.Key, TextureSemantic.Color, out _), "Truncated texture accepted.");
            var normal = TextureCooker.PrepareImage(png, TextureSemantic.Normal, folder, true);
            Require(normal.Compressed && normal.Key != cooked.Key, "Normal/color resources aliased.");
            var material = TextureCooker.PrepareImage(png, TextureSemantic.Material, null, false);
            Require(!material.Compressed && material.Mips.Length == 5, "Uncooked fallback lost mipmaps.");
            Require(ReferenceEquals(material, TextureCooker.PrepareImage(png, TextureSemantic.Material, null, false)), "Shared image prepared twice.");
            var gamma = TextureCooker.BuildMips([0, 0, 0, 255, 255, 255, 255, 255], 2, 1, TextureSemantic.Color);
            Require(gamma[1][0] is >= 187 and <= 189, "Colour mipmaps averaged encoded sRGB instead of linear light.");
            var odd = TextureCooker.BuildMips(new byte[13 * 7 * 4], 13, 7, TextureSemantic.Material);
            Require(odd.Length == 4 && odd[1].Length == 6 * 3 * 4 && odd[^1].Length == 4, "NPOT mip sizes wrong.");
            Console.WriteLine("PASS: BC7/BC5, LZ4 round trip, hash invalidation, corrupt/truncated cache, fallback, shared images and colour-correct mipmaps.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void CheckMeshQueries()
    {
        var triangles = new List<MeshCollisionTriangle>();
        for (int z = 0; z < 100; z++) for (int x = 0; x < 100; x++)
        {
            triangles.Add(new(new Vector3(x, 0, z), new Vector3(x + 1, 0, z), new Vector3(x, 0, z + 1)));
            triangles.Add(new(new Vector3(x + 1, 0, z), new Vector3(x + 1, 0, z + 1), new Vector3(x, 0, z + 1)));
        }
        var mesh = new MeshCollisionMesh(triangles);
        var collider = WorldCollider.Mesh(mesh);
        var random = new Random(1902);
        try
        {
            for (int i = 0; i < 400; i++)
            {
                Vector3 position = new((float)random.NextDouble() * 110 - 5, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 110 - 5);
                var body = i % 3 == 0 ? WorldCollider.Sphere(position, .45f) : i % 3 == 1
                    ? WorldCollider.Capsule(position, .35f, 1.8f, Quaternion.CreateFromYawPitchRoll(.4f, .2f, .1f))
                    : WorldCollider.Box(position, new Vector3(.5f), Quaternion.CreateFromYawPitchRoll(.7f, .3f, .1f));
                MeshCollisionMesh.AccelerationEnabled = false;
                bool reference = ShapeCollision.TryResolve(body, collider, out var expected);
                MeshCollisionMesh.AccelerationEnabled = true;
                bool accelerated = ShapeCollision.TryResolve(body, collider, out var actual);
                Require(reference == accelerated && (!reference || (Vector3.Distance(expected.Normal, actual.Normal) < 1e-5f && MathF.Abs(expected.Penetration - actual.Penetration) < 1e-5f)), "BVH changed contact resolution.");
                var ray = new Ray(position + Vector3.UnitY * 3, Vector3.Normalize(new Vector3(.1f, -1, .15f)));
                MeshCollisionMesh.AccelerationEnabled = false;
                reference = Raycast.RayIntersectsMesh(ray, mesh, 0, 20, out float before);
                MeshCollisionMesh.AccelerationEnabled = true;
                accelerated = Raycast.RayIntersectsMesh(ray, mesh, 0, 20, out float after);
                Require(reference == accelerated && MathF.Abs(before - after) < 1e-5f, "BVH changed ray hit.");
            }
            var bounds = Aabb.FromCenterExtents(new Vector3(50, 0, 50), new Vector3(.5f));
            var candidates = new List<int>();
            MeshCollisionMesh.CollectStatistics = true;
            foreach (bool accelerated in new[] { false, true })
            {
                MeshCollisionMesh.AccelerationEnabled = accelerated;
                MeshCollisionMesh.QueryCount = MeshCollisionMesh.BoundsTests = MeshCollisionMesh.CandidateTriangles = 0;
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 2000; i++) mesh.Query(bounds, candidates);
                Console.WriteLine($"Mesh query {(accelerated ? "BVH" : "linear")}: {watch.Elapsed.TotalMilliseconds:F2} ms / 2000; bounds tests {MeshCollisionMesh.BoundsTests}; candidates {candidates.Count}.");
            }
            Require(MeshCollisionMesh.BoundsTests < 2000 * 500, "BVH did not prune distant triangles.");
            Console.WriteLine("PASS: 400 randomized sphere/box/capsule contacts and 400 ray hits match linear queries on a 20,000-triangle mesh.");
        }
        finally { MeshCollisionMesh.AccelerationEnabled = true; MeshCollisionMesh.CollectStatistics = false; }
    }
}

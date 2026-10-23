using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Engine.Physics.Collision;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Game;
using K4os.Compression.LZ4;

internal static class CollisionCacheChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool pass, string message) { if (!pass) throw new InvalidDataException(message); }

    internal static void Run(string root)
    {
        string directory = Path.Combine(Path.GetTempPath(), "hs2-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        bool enabled = MeshCollisionCache.Enabled;
        try
        {
            MeshCollisionCache.Enabled = true;
            var triangles = new List<MeshCollisionTriangle>();
            for (int z = 0; z < 100; z++) for (int x = 0; x < 100; x++)
            {
                triangles.Add(new(new(x, 0, z), new(x + 1, 0, z), new(x, 0, z + 1)));
                triangles.Add(new(new(x + 1, 0, z), new(x + 1, 0, z + 1), new(x, 0, z + 1)));
            }
            var mesh = new MeshCollisionMesh(triangles);
            byte[] key = SHA256.HashData("qa-collision"u8);
            bool stored = MeshCollisionCache.TryStore(directory, key, mesh);
            Require(stored, "Collision cook failed: " + MeshCollisionCache.LastWriteError);
            Require(MeshCollisionCache.TryLoad(directory, key, out var loaded), "Collision read failed.");
            Compare(mesh, loaded);
            CompareQueries(mesh, loaded);
            var buildTimes = new List<double>(); var readTimes = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                var watch = Stopwatch.StartNew();
                _ = new MeshCollisionMesh(triangles);
                if (i > 1) buildTimes.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                Require(MeshCollisionCache.TryLoad(directory, key, out _), "Repeated cache read failed.");
                if (i > 1) readTimes.Add(watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"[CollisionCacheSynthetic] 20,000 triangles; seven warm samples after two warmups; median build {buildTimes.Order().ElementAt(3):F3} ms, validated LZ4 read {readTimes.Order().ElementAt(3):F3} ms; file {new FileInfo(MeshCollisionCache.CachePath(directory, key)).Length} bytes.");

            string path = MeshCollisionCache.CachePath(directory, key);
            byte[] original = File.ReadAllBytes(path);
            byte[] stale = (byte[])original.Clone(); stale[4]++;
            Reject(stale, "Wrong cache version accepted.");
            byte[] corrupt = (byte[])original.Clone(); corrupt[^1] ^= 0x80;
            Reject(corrupt, "Corrupt payload accepted.");
            Reject(original[..25], "Truncated header accepted.");
            byte[] oversized = (byte[])original.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(oversized, 40);
            Reject(oversized, "Oversized allocation accepted.");
            byte[] wrongIdentity = (byte[])original.Clone(); wrongIdentity[8] ^= 1;
            Reject(wrongIdentity, "Source/placement identity mismatch accepted.");

            // Recompute the checksum to ensure semantic validation catches malformed trees too.
            byte[] raw = new byte[BitConverter.ToInt32(original, 40)];
            Require(LZ4Codec.Decode(original.AsSpan(80), raw) == raw.Length, "Fixture decompression failed.");
            byte[] malformed = (byte[])raw.Clone();
            BitConverter.GetBytes(-1).CopyTo(malformed, 8 + triangles.Count * 36);
            Reject(Pack(malformed, original), "Invalid triangle index accepted.");
            malformed = (byte[])raw.Clone();
            int rootOffset = 12 + triangles.Count * 40;
            BitConverter.GetBytes(0).CopyTo(malformed, rootOffset + 32);
            Reject(Pack(malformed, original), "Cyclic tree accepted.");
            malformed = (byte[])raw.Clone();
            BitConverter.GetBytes(-1f).CopyTo(malformed, rootOffset + 12);
            Reject(Pack(malformed, original), "Invalid root bounds accepted.");
            Require(MeshCollisionCache.TryStore(directory, key, mesh) && MeshCollisionCache.TryLoad(directory, key, out _), "Corrupt cache did not repair.");
            Require(!MeshCollisionCache.TryStore(path, key, mesh), "Unwritable cache location did not fail safely.");
            MeshCollisionCache.Enabled = false;
            Require(!MeshCollisionCache.TryLoad(directory, key, out _) && !MeshCollisionCache.TryStore(directory, key, mesh), "Reference override ignored.");
            CheckIdentity();
            Console.WriteLine("PASS: collision LZ4 round-trip, exact geometry/bounds, 400 contacts/rays, stale/corrupt/truncated/oversized caches, semantic index/tree validation, repair, unwritable fallback and source/placement invalidation.");

            using var host = new EngineHost(800, 600, "Collision cache QA");
            var baseline = Snapshot(false);
            Snapshot(true); // Populate missing caches without assuming prior runs.
            var cached = Snapshot(true, expectHits: true);
            Require(baseline.Count > 0 && baseline.Keys.Order().SequenceEqual(cached.Keys.Order()), "Cached world changed mesh collider ownership.");
            foreach (string id in baseline.Keys) Compare(baseline[id], cached[id]);
            Console.WriteLine($"PASS: {cached.Count} six-room static mesh colliders match source construction exactly; moving-pose rebuild bypasses disk cache.");

            Dictionary<string, MeshCollisionMesh> Snapshot(bool useCache, bool expectHits = false)
            {
                MeshCollisionCache.Enabled = useCache;
                using var game = new HL2GameModule(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
                game.Initialize(host.Context);
                if (expectHits)
                    Require(game.CollisionCacheHits > 0 && game.CollisionMeshesBuilt == 0, "Production world did not use collision caches for every static mesh.");
                var entities = (List<Entity>)typeof(HL2GameModule).GetField("_runtimeEntities", Private)!.GetValue(game)!;
                var result = entities.Where(e => e.Collider.Mesh != null).ToDictionary(e => e.Id, e => e.Collider.Mesh!);
                var editor = (Engine.Editor.Editor.LevelEditorController)typeof(HL2GameModule).GetField("_editor", Private)!.GetValue(game)!;
                var first = editor.LevelFile.Entities.First(e => result.ContainsKey(e.Id));
                long hits = MeshCollisionCache.Hits, writes = MeshCollisionCache.Writes;
                object?[] args = [first, new Vector3(13, 2, 4), new Vector3(2, 3, 1), Quaternion.Identity, null, false];
                Require((bool)typeof(HL2GameModule).GetMethod("TryCreateMeshCollider", Private)!.Invoke(game, args)!, "Moving-pose construction failed.");
                Require(MeshCollisionCache.Hits == hits && MeshCollisionCache.Writes == writes, "Moving poses touched disk collision cache.");
                if (expectHits)
                {
                    var create = typeof(HL2GameModule).GetMethod("TryCreateMeshCollider", Private)!;
                    foreach (var pose in new[]
                    {
                        (new Vector3(13, 2, 4), new Vector3(2, 3, .2f), Quaternion.CreateFromYawPitchRoll(.8f, .1f, 0)),
                        (new Vector3(-13, -2, -4), new Vector3(4, 1, .5f), Quaternion.CreateFromYawPitchRoll(-1.4f, .5f, .2f))
                    })
                    {
                        object?[] sourceArgs = [first, pose.Item1, pose.Item2, pose.Item3, null, false];
                        Require((bool)create.Invoke(game, sourceArgs)!, "Transformed source collider failed.");
                        sourceArgs[5] = true;
                        var expected = (MeshCollisionMesh)sourceArgs[4]!;
                        Require((bool)create.Invoke(game, sourceArgs)!, "Transformed collider cook failed.");
                        long before = MeshCollisionCache.Hits;
                        Require((bool)create.Invoke(game, sourceArgs)! && MeshCollisionCache.Hits == before + 1, "Transformed collider did not reload from cache.");
                        Compare(expected, (MeshCollisionMesh)sourceArgs[4]!);
                    }
                }
                return result;
            }

            void Reject(byte[] bytes, string message)
            { File.WriteAllBytes(path, bytes); Require(!MeshCollisionCache.TryLoad(directory, key, out _), message); }
        }
        finally
        {
            MeshCollisionCache.Enabled = enabled;
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static void Profile(string root, bool cached, string? level = null)
    {
        MeshCollisionCache.Enabled = cached;
        string path = level ?? Path.Combine(root, "Game/Content/Levels/sixRoomTest.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Choose an existing level JSON to cook.", path);
        var watch = Stopwatch.StartNew();
        using var host = new EngineHost(800, 600, "Collision preparation profile");
        using var game = new HL2GameModule(path);
        game.Initialize(host.Context);
        Console.WriteLine($"[CollisionPreparation] cached={cached}; collision stage {game.CollisionPreparationMilliseconds:F3} ms; {game.CollisionCacheHits} hits, {game.CollisionMeshesBuilt} builds; {MeshCollisionCache.Writes} writes, {MeshCollisionCache.WriteFailures} write failures; host + game initialize {watch.Elapsed.TotalMilliseconds:F3} ms.");
        Console.WriteLine("Warm filesystem, fresh process. Initialization is not GPU-ready/first-playable time; missing static placement caches are generated automatically. Source GLBs and levels unchanged.");
    }

    private static void CheckIdentity()
    {
        var keyMethod = typeof(HL2GameModule).GetMethod("CreateCollisionCacheKey", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] Hash(byte[] source, Vector3 position, Vector3 scale, Quaternion rotation)
            => (byte[])keyMethod.Invoke(null, [source, position, scale, rotation])!;
        byte[] key = Hash(new byte[32], Vector3.Zero, Vector3.One, Quaternion.Identity);
        Require(!key.SequenceEqual(Hash(SHA256.HashData("changed"u8), Vector3.Zero, Vector3.One, Quaternion.Identity)), "Source content absent from cache key.");
        Require(!key.SequenceEqual(Hash(new byte[32], Vector3.UnitX, Vector3.One, Quaternion.Identity)), "Position absent from cache key.");
        Require(!key.SequenceEqual(Hash(new byte[32], Vector3.Zero, new(1, 2, -1), Quaternion.Identity)), "Scale absent from cache key.");
        Require(!key.SequenceEqual(Hash(new byte[32], Vector3.Zero, Vector3.One, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1))), "Rotation absent from cache key.");
    }

    private static byte[] Pack(byte[] raw, byte[] headerSource)
    {
        byte[] packed = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
        int length = LZ4Codec.Encode(raw, packed);
        byte[] result = new byte[80 + length];
        headerSource.AsSpan(0, 80).CopyTo(result);
        BitConverter.GetBytes(raw.Length).CopyTo(result, 40); BitConverter.GetBytes(length).CopyTo(result, 44);
        SHA256.HashData(raw).CopyTo(result, 48); packed.AsSpan(0, length).CopyTo(result.AsSpan(80));
        return result;
    }

    private static void Compare(MeshCollisionMesh expected, MeshCollisionMesh actual)
    {
        Require(expected.CollisionSkin == actual.CollisionSkin && expected.Bounds.Equals(actual.Bounds) && expected.Triangles.Count == actual.Triangles.Count, "Collider bounds/skin/count changed.");
        for (int i = 0; i < expected.Triangles.Count; i++) Require(expected.Triangles[i].Equals(actual.Triangles[i]), "Cached triangle data changed.");
    }

    private static void CompareQueries(MeshCollisionMesh reference, MeshCollisionMesh cached)
    {
        var random = new Random(520);
        for (int i = 0; i < 400; i++)
        {
            Vector3 p = new((float)random.NextDouble() * 110 - 5, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 110 - 5);
            var body = i % 3 == 0 ? WorldCollider.Sphere(p, .45f) : i % 3 == 1 ? WorldCollider.Capsule(p, .35f, 1.8f, Quaternion.Identity)
                : WorldCollider.Box(p, new(.5f), Quaternion.CreateFromYawPitchRoll(.7f, .3f, .1f));
            bool a = ShapeCollision.TryResolve(body, WorldCollider.Mesh(reference), out var contactA);
            bool b = ShapeCollision.TryResolve(body, WorldCollider.Mesh(cached), out var contactB);
            Require(a == b && (!a || contactA.Equals(contactB)), "Cached contact changed.");
            var ray = new Ray(p + Vector3.UnitY * 3, Vector3.Normalize(new Vector3(.1f, -1, .15f)));
            a = Raycast.RayIntersectsMesh(ray, reference, 0, 20, out float tA);
            b = Raycast.RayIntersectsMesh(ray, cached, 0, 20, out float tB);
            Require(a == b && tA == tB, "Cached ray changed.");
        }
    }
}

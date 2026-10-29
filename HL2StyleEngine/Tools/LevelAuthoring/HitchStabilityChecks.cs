using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Physics.Collision;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;
using Game;
using Game.Inventory;

internal static class HitchStabilityChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks++;
        }
        object? Call(object instance, string method, params object[] args)
            => instance.GetType().GetMethod(method, Private)!.Invoke(instance, args);

        // No game initialization: these checks own only managed entities/inventory.
        var game = new HL2GameModule();
        var entities = (List<Entity>)PbrBaselineChecks.Field(game, "_runtimeEntities");
        var random = new Random(7041);
        for (int i = 0; i < 100; i++)
        {
            var entity = new Entity(i.ToString(), "Prop", "Support");
            entity.Transform.Position = new Vector3(Next(12), Next(4), Next(12));
            entity.Transform.RotationEulerDeg = new Vector3(Next(90), Next(180), Next(90));
            entity.Collider.Shape = i % 7 != 0 ? RuntimeShapeKind.Box : RuntimeShapeKind.None;
            entity.Collider.IsSolid = i % 11 != 0;
            entity.Collider.Size = new Vector3(.2f + random.NextSingle(), .2f + random.NextSingle(), .2f + random.NextSingle()) * 2;
            entity.Physics.UseAuthoredStaticPose = true;
            entities.Add(entity);
        }
        var create = typeof(HL2GameModule).GetMethod("CreateWorldCollider", Private, [typeof(Entity)])!;
        var ground = typeof(HL2GameModule).GetMethod("FindFractureDebrisGroundCenterY", Private)!;
        var supports = (List<Aabb>)PbrBaselineChecks.Field(game, "_fractureSupportBounds");
        foreach (int sourceIndex in new[] { 1, 2 })
        {
            if (sourceIndex == 2) entities[1].Collider.Shape = RuntimeShapeKind.None;
            Entity source = entities[sourceIndex];
            Call(game, "SnapshotFractureSupports", source);
            Check(supports.Count == entities.Count(e => e != source && e.Collider.Enabled && e.Collider.IsSolid), "Support exclusions changed.");
            for (int i = 0; i < 200; i++)
            {
                Vector3 center = new(Next(12), Next(4), Next(12));
                float half = .025f + random.NextSingle();
                var tops = entities.Where(e => e != source && e.Collider.Enabled && e.Collider.IsSolid)
                    .Select(e => ((WorldCollider)create.Invoke(null, [e])!).GetAabb())
                    .Where(b => center.X >= b.Min.X - .08f && center.X <= b.Max.X + .08f &&
                                center.Z >= b.Min.Z - .08f && center.Z <= b.Max.Z + .08f && b.Max.Y <= center.Y + half + .05f)
                    .Select(b => b.Max.Y).ToArray();
                float expected = (tops.Length == 0 ? 0 : tops.Max()) + half;
                float actual = (float)ground.Invoke(null, [supports, center, half])!;
                Check(MathF.Abs(actual - expected) < .00001f, "Cached debris ground differs from original per-fragment query.");
            }
        }
        var belowFloor = new[] { new Aabb(new(-1, -3, -1), new(1, -2, 1)) };
        Check((float)ground.Invoke(null, [belowFloor, Vector3.Zero, .1f])! == -1.9f, "Negative-height support changed.");
        Check((float)ground.Invoke(null, [Array.Empty<Aabb>(), Vector3.Zero, .1f])! == .1f, "Empty support fallback changed.");

        entities.Clear();
        entities.Add(new Entity("cable", "Prop", "Item_DamagedCable"));
        entities.Add(new Entity("weapon", "Prop", "Item_" + ItemCatalog.TestPistol));
        entities.Add(new Entity("ammo", "Prop", "Item_" + ItemCatalog.Bullets));
        var inventory = (InventoryContainer)PbrBaselineChecks.Field(game, "_inventory");
        Check(inventory.Add(ItemCatalog.DamagedCable), "Could not prepare inventory fixture.");
        Call(game, "QueueLevelUiImages");
        var pending = (Queue<string>)PbrBaselineChecks.Field(game, "_pendingUiImages");
        int queued = pending.Count;
        Check(queued is > 1 and <= 64 && pending.Distinct(StringComparer.OrdinalIgnoreCase).Count() == queued &&
              !pending.Any(p => Path.GetFileName(p) == "Bullets.png"), "Icons were not deduplicated/bounded, or ammo was queued.");
        string path = pending.Peek();
        Check(File.Exists(path) && Path.GetFileName(path) == "DamagedCable.png", "Current inventory was not prioritized before catalogue thumbnails.");

        using (var host = new EngineHost(640, 480, "Hitch regression checks"))
        {
            var ui = host.Context.ImGui!;
            long before = ui.ImageResidentBytes;
            Check(!ui.TryPreloadImage(path, before), "Zero remaining preload budget was ignored.");
            Check(!ui.TryPreloadImage(path, before + 1), "Oversized preload was admitted.");
            Check(!ui.TryPreloadImage(path + ".missing", 16 * 1024 * 1024), "Missing image preload succeeded.");
            Check(ui.ImageResidentBytes == before, "Rejected preload allocated GPU payload.");
            PbrBaselineChecks.Set(game, "_ctx", host.Context);
            PbrBaselineChecks.Set(game, "_editorEnabled", false);
            PbrBaselineChecks.Set(game, "_loadingOverlayTimer", 0f);
            Call(game, "PrewarmNextUiImage");
            Check(pending.Count == queued && ui.ImageResidentBytes == before, "Prewarm ran outside the loading screen.");
            PbrBaselineChecks.Set(game, "_loadingOverlayTimer", .5f);
            Call(game, "PrewarmNextUiImage");
            var image = ui.GetImage(path);
            long resident = ui.ImageResidentBytes;
            Check(pending.Count == queued - 1 && image.Id != IntPtr.Zero, "Loading-frame preload failed.");
            Check(resident == before + (long)image.Width * image.Height * 4 && resident <= 16 * 1024 * 1024, "Preload accounting/budget mismatch.");
            Check(ui.TryPreloadImage(path, 0) && ui.GetImage(path) == image && ui.ImageResidentBytes == resident, "Cached image was duplicated or evicted.");
            Check((float)PbrBaselineChecks.Field(game, "_loadingOverlayTimer") == .5f && inventory.GetCount(ItemCatalog.DamagedCable) == 1,
                "Prewarm changed readiness or inventory.");
            Call(game, "QueueLevelUiImages");
            Call(game, "PrewarmNextUiImage");
            Check(ui.ImageResidentBytes == resident, "Level requeue duplicated resident images.");
            entities.Add(new Entity("wire", "Prop", "Item_" + ItemCatalog.SpareWire));
            Call(game, "QueueLevelUiImages");
            Check(pending.Count == queued && pending.Take(2).Any(p => Path.GetFileName(p) == "SpareWire.png"), "Level icon was not prioritized.");
            Call(game, "PrewarmNextUiImage");
            Check(pending.Count == queued - 1, "Prewarm processed more than one queued image in a frame.");
            PbrBaselineChecks.Set(game, "_editorEnabled", true);
            Call(game, "PrewarmNextUiImage");
            Check(pending.Count == queued - 1, "Prewarm ran in authoring mode.");
            PbrBaselineChecks.Set(game, "_editorEnabled", false);
            while (pending.Count > 0) Call(game, "PrewarmNextUiImage");
            Check(ui.ImageResidentBytes <= 16 * 1024 * 1024, "Registered thumbnail tail exceeded admission budget.");
            Console.WriteLine($"[UiPrewarm] {image.Width}x{image.Height}, {resident - before} bytes, cached binding reused.");
        }

        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "hitch-contract-" + Guid.NewGuid().ToString("N"));
        string report = Path.Combine(folder, "frames.json");
        string? priorFrames = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES"), priorReport = Environment.GetEnvironmentVariable("HS2_FRAME_REPORT");
        try
        {
            Type type = typeof(EngineHost).Assembly.GetType("Engine.Runtime.Hosting.FrameProfile")!;
            var fromEnvironment = type.GetMethod("FromEnvironment")!;
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", report);
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", null);
            Check(fromEnvironment.Invoke(null, null) == null, "Trace enabled ordinary gameplay profiling.");
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "3");
            object profile = fromEnvironment.Invoke(null, null)!;
            for (int frame = 0; frame < 123; frame++)
            {
                type.GetMethod("BeginInput")!.Invoke(profile, null);
                type.GetMethod("Begin")!.Invoke(profile, null);
                for (int stage = 0; stage < 6; stage++) type.GetMethod("Mark")!.Invoke(profile, null);
                Check((bool)type.GetMethod("Finish")!.Invoke(profile, [640, 480])! == (frame == 122), "Profile completion count changed.");
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(report));
            var rows = doc.RootElement.GetProperty("Frames").EnumerateArray().ToArray();
            Check(rows.Length == 123 && rows.Count(r => r.GetProperty("Warmup").GetBoolean()) == 120, "Trace warmup/sample count mismatch.");
            Check(doc.RootElement.GetProperty("Stages").GetArrayLength() == 6, "Stage names missing.");
            foreach (var row in rows)
            {
                Check(row.GetProperty("Milliseconds").GetArrayLength() == 6 && row.GetProperty("AllocatedByStage").GetArrayLength() == 6 &&
                      row.GetProperty("Collections").GetArrayLength() == 3 && row.GetProperty("InputMilliseconds").GetDouble() >= 0 &&
                      row.GetProperty("GcPauseMilliseconds").GetDouble() >= 0, "Invalid trace row.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", priorFrames);
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", priorReport);
        }
        Console.WriteLine($"PASS: {checks} hitch regression assertions. {report}");
        float Next(float range) => (random.NextSingle() * 2 - 1) * range;
    }
}

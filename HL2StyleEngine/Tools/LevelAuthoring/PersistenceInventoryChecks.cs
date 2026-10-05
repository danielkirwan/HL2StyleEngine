using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.UI;
using Game;
using Game.Inventory;

internal static class PersistenceInventoryChecks
{
    private static readonly JsonSerializerOptions Options = new() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidDataException(message);
        _checks++;
    }
    private static void Equivalent<T>(T a, T b, string label)
        => Check(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(a, Options), JsonSerializer.SerializeToNode(b, Options)), label);

    public static void Run(string root)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "hs2-persistence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (string source in Directory.GetFiles(Path.Combine(root, "Game/Content/Levels"), "*.json"))
            {
                byte[] bytes = File.ReadAllBytes(source);
                var expected = JsonSerializer.Deserialize<LevelFile>(bytes, Options)!;
                var actual = StructuredData.ReadJson<LevelFile>(bytes);
                Equivalent(expected, actual, Path.GetFileName(source) + " JSON compatibility");
                Equivalent(expected, StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(actual)), "REDox JSON round trip");
                string copy = Path.Combine(temporary, Path.GetFileName(source));
                File.WriteAllBytes(copy, bytes);
                Equivalent(expected, StructuredData.LoadCached<LevelFile>(copy), "cache creation");
                Equivalent(expected, StructuredData.LoadCached<LevelFile>(copy), "DOX read");
                Check(File.Exists(StructuredData.CachePath(copy)), "DOX cache was not written");
            }
            foreach (string source in Directory.GetFiles(Path.Combine(root, "Game/Content/Prefabs"), "*.json", SearchOption.AllDirectories))
            {
                var prefab = PrefabIO.Load(source);
                string copy = Path.Combine(temporary, "prefab.json");
                PrefabIO.Save(copy, prefab);
                Equivalent(prefab, PrefabIO.Load(copy), "prefab hierarchy round trip");
            }
            var entity = new LevelEntityDef { Id = "child", ParentId = "root", LocalPosition = new SerVec3(-1, 2, 3),
                LocalRotationEulerDeg = new SerVec3(0, -90, 180), LocalScale = new SerVec3(.2f, 3.85f, 2.84f) };
            var contract = new LevelFile { Entities = [entity] };
            string path = Path.Combine(temporary, "contract.json");
            StructuredData.Save(path, contract);
            Check(File.ReadAllText(path).Contains("\"Parent\""), "Parent alias changed");
            Equivalent(contract, StructuredData.LoadCached<LevelFile>(path), "transforms and aliases");
            DateTime oldTime = File.GetLastWriteTimeUtc(path);
            contract.Entities[0].Name = "Changed in editor";
            StructuredData.Save(path, contract);
            File.SetLastWriteTimeUtc(path, oldTime);
            Equivalent(contract, StructuredData.LoadCached<LevelFile>(path), "content invalidates cache despite same timestamp");
            Check(File.Exists(path + ".bak"), "atomic save backup missing");
            string cache = StructuredData.CachePath(path);
            File.WriteAllBytes(cache, [1, 2, 3]);
            Equivalent(contract, StructuredData.LoadCached<LevelFile>(path), "truncated cache recovery");
            var corrupt = File.ReadAllBytes(cache); corrupt[^1] ^= 7; File.WriteAllBytes(cache, corrupt);
            Equivalent(contract, StructuredData.LoadCached<LevelFile>(path), "checksum recovery");
            var commented = Encoding.UTF8.GetBytes("\ufeff{/* legacy */\"Entities\":[],}");
            Check(StructuredData.ReadJson<LevelFile>(commented).Entities.Count == 0, "BOM/comments/trailing comma compatibility");
            CheckSaveDto(temporary);
            CheckInventory();
            Benchmark(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"), temporary);
            Console.WriteLine($"PASS: {_checks} persistence/inventory checks.");
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static void CheckSaveDto(string temporary)
    {
        Type type = typeof(HL2GameModule).GetNestedType("PrototypeSaveData", Flags)!;
        const string legacy = "{\"InventoryItems\":[{\"ItemId\":\"Scrap\",\"Count\":8,\"SlotIndex\":0,\"Rotated\":false}],\"PlayerHealth\":23,\"PlayerSuit\":11,\"OpenedDoors\":[\"door\"],\"StorageItems\":[]}";
        string path = Path.Combine(temporary, "slot_1.json");
        File.WriteAllText(path, legacy);
        object?[] args = [path, null];
        var read = typeof(HL2GameModule).GetMethod("TryReadSaveData", Flags)!;
        Check((bool)read.Invoke(null, args)!, "legacy save read");
        object data = args[1]!;
        byte[] bytes = (byte[])typeof(StructuredData).GetMethod("WriteJson")!.MakeGenericMethod(type).Invoke(null, [data])!;
        File.WriteAllBytes(path, bytes);
        args[1] = null;
        Check((bool)read.Invoke(null, args)! && (int)type.GetProperty("PlayerHealth")!.GetValue(args[1])! == 23, "private save DTO round trip");
        File.WriteAllText(path, "{\"SaveVersion\":1,\"DroppedItems\":[{\"ItemId\":\"Scrap\",\"Count\":99,\"Name\":\"Item_Scrap__x99__SpawnFixture\",\"X\":1.5,\"Y\":0.45,\"Z\":-2}]}");
        Check((bool)read.Invoke(null, args)!, "world pickup save read");
        bytes = (byte[])typeof(StructuredData).GetMethod("WriteJson")!.MakeGenericMethod(type).Invoke(null, [args[1]])!;
        File.WriteAllBytes(path, bytes);
        Check((bool)read.Invoke(null, args)!, "world pickup save round trip");
        var drops = (System.Collections.IList)type.GetProperty("DroppedItems")!.GetValue(args[1])!;
        Check(drops.Count == 1 && (int)drops[0]!.GetType().GetProperty("Count")!.GetValue(drops[0])! == 99 &&
            (string)drops[0]!.GetType().GetProperty("Name")!.GetValue(drops[0])! == "Item_Scrap__x99__SpawnFixture",
            "world pickup quantity and identity preserved");
        File.WriteAllText(path, "{\"SaveVersion\":99}");
        bool rejected = false;
        try { read.Invoke(null, args); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
        Check(rejected, "future save version rejection");
    }

    private static void CheckInventory()
    {
        var bag = new InventoryContainer(2, 1, 1);
        Check(bag.Add(ItemCatalog.Scrap, 198) && bag.Add(ItemCatalog.InkRibbon, 8), "overflow pickup");
        Check(bag.OverflowStacks.Sum(x => x.Count) == 8, "overflow location");
        Check(!bag.Add(ItemCatalog.Scrap, 1) && bag.GetCount(ItemCatalog.Scrap) == 198, "full grids rollback");
        Check(!bag.TransferStackTo(0, bag) && bag.GetCount(ItemCatalog.Scrap) == 198, "self-transfer conserves quantity");
        Check(!bag.MoveOrMergeOrSwapStackToSlot(0, 1, false, out var result) && result == InventoryMoveResult.StackFull, "full merge leaves source");
        var copy = new InventoryContainer(2, 1, 1);
        copy.LoadFromSave(StructuredData.ReadJson<List<InventoryItemSaveData>>(StructuredData.WriteJson(bag.ToSaveData())));
        Equivalent(bag.ToSaveData(), copy.ToSaveData(), "inventory overflow save");
        bag.Clear();
        Check(bag.Add(ItemCatalog.Scrap, 105), "partial stacks");
        Check(bag.SplitStackAtSlot(0, 20, out int splitSlot, out int split) && split == 20 && splitSlot == 2, "split full main into overflow");
        Check(bag.MoveOrMergeOrSwapStackToSlot(splitSlot, 1, false, out result) && result == InventoryMoveResult.Merged && bag.GetCount(ItemCatalog.Scrap) == 105, "merge back from overflow");
        Check(bag.SplitStackAtSlot(0, 60, out splitSlot, out _) && bag.MoveOrMergeOrSwapStackToSlot(splitSlot, 1, false, out _) && bag.GetCount(ItemCatalog.Scrap) == 105, "split conservation");
        bag.Clear();
        Check(bag.Add(ItemCatalog.MasterKey, 2) && bag.StackCount == 2, "single-stack items not silently consumed");
        var small = new InventoryContainer(1, 1, 1);
        Check(!small.Add(ItemCatalog.CrankHandle), "footprint cannot straddle grids");
        Check(!small.Add(ItemCatalog.InkRibbon, 13) && small.IsEmpty, "oversized add atomic");
        var merging = new InventoryContainer(2, 1, 1);
        merging.LoadFromSave([
            new() { ItemId = ItemCatalog.Scrap, Count = 90, SlotIndex = 0 },
            new() { ItemId = ItemCatalog.Scrap, Count = 50, SlotIndex = 1 }]);
        Check(merging.MoveOrMergeOrSwapStackToSlot(1, 0, false, out _) &&
            merging.ToSaveData().Single(x => x.SlotIndex == 0).Count == 99 &&
            merging.ToSaveData().Single(x => x.SlotIndex == 1).Count == 41, "partial merge preserves remainder");
        var sourceBag = new InventoryContainer(1, 1);
        sourceBag.Add(ItemCatalog.MasterKey);
        Check(!sourceBag.TransferStackTo(0, merging) && sourceBag.GetCount(ItemCatalog.MasterKey) == 1 &&
            !merging.OverflowStacks.Any(), "storage transfer does not silently use overflow");
        var beforeInvalidLoad = merging.ToSaveData();
        bool rejectedLoad = false;
        try { merging.LoadFromSave([new() { ItemId = ItemCatalog.Scrap, Count = 9999, SlotIndex = 0 }]); }
        catch (InvalidDataException) { rejectedLoad = true; }
        Check(rejectedLoad, "over-capacity save rejected");
        Equivalent(beforeInvalidLoad, merging.ToSaveData(), "failed load preserves inventory");

        var module = new HL2GameModule();
        var motorField = typeof(HL2GameModule).GetField("_motor", Flags)!;
        var movement = typeof(HL2GameModule).GetField("_movement", Flags)!.GetValue(module)!;
        motorField.SetValue(module, Activator.CreateInstance(motorField.FieldType, movement, System.Numerics.Vector3.Zero));
        var inv = (InventoryContainer)typeof(HL2GameModule).GetField("_inventory", Flags)!.GetValue(module)!;
        Check(inv.Add(ItemCatalog.Scrap, 99 * 33), "seed runtime overflow");
        typeof(HL2GameModule).GetField("_movingInventoryFromSlot", Flags)!.SetValue(module, 32);
        Check((bool)typeof(HL2GameModule).GetMethod("ReturnOverflowToWorld", Flags)!.Invoke(module, null)!, "drop overflow");
        var capture = typeof(HL2GameModule).GetMethod("CaptureDroppedItems", Flags)!;
        var drops = (System.Collections.IList)capture.Invoke(module, null)!;
        Check(drops.Count == 1 && !inv.OverflowStacks.Any() && inv.GetCount(ItemCatalog.Scrap) == 99 * 32, "only overflow dropped");
        typeof(HL2GameModule).GetMethod("RestoreDroppedItems", Flags)!.Invoke(module, [drops]);
        Check(((System.Collections.IList)capture.Invoke(module, null)!).Count == 1, "world drop restored once");
        Check((int)typeof(HL2GameModule).GetField("_movingInventoryFromSlot", Flags)!.GetValue(module)! == -1, "close cancels drag");
        foreach (var size in new[] { (1920, 1080), (1280, 720), (800, 600), (640, 480) })
        {
            var layout = new InventoryLayout(size.Item1, size.Item2, 8, 8, 4);
            Check(layout.Horizontal ? layout.Navigate(7, 1, 0) == 32 && layout.Navigate(32, -1, 0) == 7
                : layout.Navigate(24, 0, 1) == 32 && layout.Navigate(32, 0, -1) == 24,
                "navigation crosses grids in their visual direction");
            for (int slot = 0; slot < 64; slot++)
            {
                var at = layout.SlotOrigin(slot);
                Check(at.X >= 0 && at.Y >= 0 && at.X + layout.Cell <= size.Item1 && at.Y + layout.Cell <= size.Item2, "responsive grid bounds");
            }
        }
    }

    private static void Benchmark(string source, string temporary)
    {
        string path = Path.Combine(temporary, "timed.json");
        File.Copy(source, path);
        byte[] json = File.ReadAllBytes(path);
        StructuredData.LoadCached<LevelFile>(path);
        static double Median(Action action)
        {
            for (int i = 0; i < 10; i++) action();
            double[] times = new double[51];
            for (int i = 0; i < times.Length; i++) { long start = Stopwatch.GetTimestamp(); action(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
            Array.Sort(times); return times[25];
        }
        Console.WriteLine($"Warm file+decode median, 51 samples: STJ UTF8={Median(() => JsonSerializer.Deserialize<LevelFile>(File.ReadAllBytes(path), Options)):F3}ms, REDox JSON={Median(() => StructuredData.Load<LevelFile>(path)):F3}ms, validated DOX cache={Median(() => StructuredData.LoadCached<LevelFile>(path)):F3}ms.");
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Editor.Level;
using Game;

try
{
    string root = Path.GetFullPath(args[1]);
    string output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    if (args[0] == "--boot")
    {
        StartupCheck.Run(root, output, args[3]);
        return;
    }
    if (args[0] != "--micro") throw new ArgumentException("Use --micro root output, or --boot root output codec.");
    var rows = new List<object>();
    var checks = new List<string>();
    var levels = new List<object>();
    string scratch = Path.Combine(root, "Tools/REDoxBenchmark/.work/cache");
    Directory.CreateDirectory(scratch);
    Environment.SetEnvironmentVariable("HS2_BENCH_CACHE", scratch);
    var options = BenchmarkCodec.Options;

    void Equal(object expected, object actual, Type type, string label)
    {
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected, type, options),
                JsonSerializer.SerializeToNode(actual, type, options)))
            throw new InvalidDataException("Round-trip mismatch: " + label);
    }

    void Dataset(string name, byte[] bytes, Type type, bool measure)
    {
        string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        object expected = BenchmarkCodec.Read(bytes, text, type, "stj");
        foreach (string codec in BenchmarkCodec.Names)
        {
            byte[] input = codec == "redox-dox" ? BenchmarkCodec.Write(expected, type, codec) : bytes;
            object parsed = BenchmarkCodec.Read(input, text, type, codec);
            Equal(expected, parsed, type, name + " read " + codec);
            byte[] saved = BenchmarkCodec.Write(parsed, type, codec);
            object restored = BenchmarkCodec.Read(saved, Encoding.UTF8.GetString(saved), type, codec);
            Equal(expected, restored, type, name + " write/read " + codec);
            if (codec != "redox-dox")
                Equal(expected, BenchmarkCodec.Read(saved, Encoding.UTF8.GetString(saved), type, "stj"), type,
                    name + " legacy-reader " + codec);
            checks.Add(name + ": " + codec + " equivalent read/round-trip" + (codec == "redox-dox" ? "" : "/legacy JSON read"));
            if (!measure) continue;
            string source = Path.Combine(scratch, name + (codec == "redox-dox" ? ".dox" : ".json"));
            File.WriteAllBytes(source, input);
            rows.Add(Measure(name, codec, "deserialize-memory", input.Length, () => BenchmarkCodec.Read(input, text, type, codec)));
            rows.Add(Measure(name, codec, "file-read-and-deserialize", input.Length, () => codec == "stj"
                ? BenchmarkCodec.Read([], File.ReadAllText(source), type, codec)
                : BenchmarkCodec.Read(File.ReadAllBytes(source), "", type, codec)));
            rows.Add(Measure(name, codec, "serialize", saved.Length, () => codec == "stj"
                ? JsonSerializer.Serialize(expected, type, options) : BenchmarkCodec.Write(expected, type, codec)));
            string destination = Path.Combine(scratch, "save-test." + codec);
            rows.Add(Measure(name, codec, "serialize-and-file-write", saved.Length, () =>
            {
                if (codec == "stj") File.WriteAllText(destination, JsonSerializer.Serialize(expected, type, options));
                else File.WriteAllBytes(destination, BenchmarkCodec.Write(expected, type, codec));
                return expected;
            }, batches: 15, operations: 1));
        }
    }

    foreach (string path in Directory.GetFiles(Path.Combine(root, "Game/Content/Levels"), "*.json").Order())
    {
        byte[] bytes = File.ReadAllBytes(path);
        LevelFile level = LevelIO.Load(path);
        levels.Add(new { name = Path.GetFileName(path), bytes = bytes.Length, entities = level.Entities.Count,
            compactJsonBytes = JsonSerializer.SerializeToUtf8Bytes(level, new JsonSerializerOptions(options) { WriteIndented = false }).Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        Dataset(Path.GetFileNameWithoutExtension(path), bytes, typeof(LevelFile), true);
        Console.WriteLine("Verified and measured " + Path.GetFileName(path));
    }
    foreach (string path in Directory.GetFiles(Path.Combine(root, "Game/Content/Prefabs"), "*.json", SearchOption.AllDirectories))
        Dataset("prefab-" + Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path), typeof(PrefabFile), false);

    string contract = """
        {"Version":2,"UsePointLights":true,"Entities":[
          {"Id":"root","Type":"Prop","Name":"Frame"},
          {"Id":"child","Parent":"root","Type":"RigidBody","LocalPosition":{"X":1.25,"Y":-2.5,"Z":3.75},
           "LocalRotationEulerDeg":{"X":15,"Y":90,"Z":-30},"LocalScale":{"X":2,"Y":3,"Z":4},
           "Color":{"X":0.1,"Y":0.2,"Z":0.3,"W":0.4},"MotionType":1,"CanPickUp":true,
           "Scripts":[{"Type":"MovingPlatform","Json":"{\"Speed\":1.5}"}],
           "Interaction":{"Kind":"LockedDoor","RequiredItem":"RustedKey","StateId":"Door_A","Targets":["Door_B"]}}]}
        """;
    Dataset("parent-vector-script-contract", Encoding.UTF8.GetBytes(contract), typeof(LevelFile), false);
    Dataset("legacy-box-contract", Encoding.UTF8.GetBytes("{\"Version\":1,\"Boxes\":[{\"Id\":\"legacy\",\"Name\":\"Box\",\"Position\":{\"X\":2,\"Y\":1,\"Z\":-5}}]}"), typeof(LevelFile), false);
    Type saveType = typeof(HL2GameModule).GetNestedType("PrototypeSaveData", BindingFlags.NonPublic)!;
    string save = """
        {"SaveSlot":1,"SavedAtUtc":"2026-10-02T12:00:00Z","LevelName":"sixRoomTest.json",
         "InventoryItems":[{"ItemId":"RepairedCable","Count":1,"SlotIndex":3,"Rotated":true}],
         "StorageItems":[{"ItemId":"InkRibbon","Count":4,"SlotIndex":1}],
         "OpenedDoors":["Door_A"],"SolvedPuzzles":["SRPuzzle_MedicalSocket"],"CollectedInteractables":["Key_A"],
         "BrokenObjects":[{"Name":"Crate_A","ReplacementModelPath":"Content/Models/ViewModels/DamagedCrate02.glb"}],
         "WeaponStates":[{"WeaponId":"Pistol","Owned":true,"CurrentMagazine":7,"ReserveAmmo":23}],
         "LevelDisplayName":"Six-Room Test","AreaName":"Medical","SavePointName":"Typewriter_A",
         "SavePointDisplayName":"Medical Typewriter","Difficulty":"Prototype","ProfileId":"Benchmark","PlayTimeSeconds":345.5,
         "PlayerHealth":24,"PlayerSuit":15,"PlayerX":1.25,"PlayerY":2.5,"PlayerZ":-9.5,
         "CameraYaw":1.2,"CameraPitch":-0.3,"InkRibbons":4,"SavePointUseCount":2}
        """;
    Dataset("representative-save", Encoding.UTF8.GetBytes(save), saveType, true);

    var parserChecks = new List<object>();
    foreach (string codec in BenchmarkCodec.Names.Where(c => c != "redox-dox"))
        foreach (var sample in new[] { ("comments", "{/* comment */\"Version\":2,\"Entities\":[]}"),
            ("trailing-comma", "{\"Version\":2,\"Entities\":[],}"), ("truncated", "{\"Entities\":[") })
        {
            bool accepted;
            try { BenchmarkCodec.Read(Encoding.UTF8.GetBytes(sample.Item2), sample.Item2, typeof(LevelFile), codec); accepted = true; }
            catch { accepted = false; }
            parserChecks.Add(new { codec, input = sample.Item1, accepted });
        }
    var report = new { labelDate = "2026-10-02", runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        logicalProcessors = Environment.ProcessorCount, configuration = "Release", levels, checks, parserChecks, rows,
        methodology = "5 warmups, 25 batches x 5 ops unless noted; median/p95 of per-operation batch means. OS file cache warm, no cache flushing. File writes are buffered, not durability/fsync timings. Allocations use process-wide GC.GetTotalAllocatedBytes. Background system load uncontrolled." };
    File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Saved " + output);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static object Measure(string name, string codec, string operation, int bytes, Func<object> action, int batches = 25, int operations = 5)
{
    for (int i = 0; i < 5; i++) GC.KeepAlive(action());
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var times = new double[batches];
    var allocated = new double[batches];
    for (int batch = 0; batch < batches; batch++)
    {
        long before = GC.GetTotalAllocatedBytes(true);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < operations; i++) GC.KeepAlive(action());
        times[batch] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / operations;
        allocated[batch] = (GC.GetTotalAllocatedBytes(true) - before) / (double)operations;
    }
    double[] sorted = times.Order().ToArray();
    return new { dataset = name, codec, operation, bytes, batches, operationsPerBatch = operations,
        medianMs = sorted[batches / 2], p95Ms = sorted[(int)Math.Ceiling(batches * .95) - 1],
        meanAllocatedBytes = allocated.Average(), samplesMs = times };
}

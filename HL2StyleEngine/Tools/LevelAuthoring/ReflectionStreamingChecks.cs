using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class ReflectionStreamingChecks
{
    public const string FileName = "sixRoomPbrTest.json";
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); _checks++; }
    private static string Resolve(string root, string path) => Path.GetFullPath(Path.Combine(root, "Game", path));

    public static void Package(string root, string directory, string fileName = FileName)
    {
        directory = Path.GetFullPath(directory);
        string source = Path.Combine(root,"Game/Content/Levels",fileName), packaged = Path.Combine(directory,"Content/Levels",fileName);
        void Same(string a, string b)
        {
            using var left = File.OpenRead(a); using var right = File.OpenRead(b);
            Check(SHA256.HashData(left).SequenceEqual(SHA256.HashData(right)), "Package differs: " + b);
        }
        Same(source, packaged);
        foreach (string shader in new[] { "TexturedModelVS.cso", "TexturedModelPS.cso", "ShadowVS.cso" })
            Same(Path.Combine(AppContext.BaseDirectory,"Shaders",shader),Path.Combine(directory,"Shaders",shader));
        var level = LevelIO.Load(packaged);
        foreach (string model in level.Entities.Select(e=>e.MeshPath).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct())
            Same(Resolve(root,model),Path.Combine(directory,model));
        if (level.LightingScenarios is { Count: > 0 })
        {
            LightingScenarioResolver.Validate(level);
            foreach(var scenario in level.LightingScenarios)
            {
                var view=LightingScenarioResolver.CreateView(level,scenario);
                string sourceView=LightingScenarioResolver.PathFor(source,scenario.Id), packagedView=LightingScenarioResolver.PathFor(packaged,scenario.Id);
                if(view.Bake.Enabled)
                {
                    Same(LightmapFile.PathFor(sourceView),LightmapFile.PathFor(packagedView));
                    var snapshot=BakeSnapshot.Capture(view,p=>Path.Combine(directory,p));
                    Check(LightmapFile.Load(LightmapFile.PathFor(packagedView),snapshot.Key())!=null,"Packaged scenario bake did not validate.");
                }
                Captures(view,sourceView,packagedView);
            }
        }
        else Captures(level,source,packaged);
        Console.WriteLine($"PASS: {_checks} package hash/cache checks using packaged GLBs: {directory}");
        void Captures(LevelFile view,string sourceView,string packagedView)
        {
            foreach (var probe in view.Entities.Where(e=>e.ReflectionProbe!=null))
            {
                var snapshot = ReflectionSnapshot.Create(view,packagedView,probe.Id,p=>Path.Combine(directory,p));
                string cache = ReflectionProbeCache.PathFor(packagedView,probe.Id);
                Same(ReflectionProbeCache.PathFor(sourceView,probe.Id),cache);
                Check(ReflectionProbeCache.Load(cache,snapshot.Key())!=null,"Packaged capture did not validate: "+probe.Name);
            }
        }
    }

    public static void Build(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        if (File.Exists(path)) throw new IOException("PBR level exists; editor changes will not be overwritten.");
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels", SixRoomFlashlightTest.FileName));
        level.LightingModel = SurfaceLightingModel.Pbr; level.AutomaticRoomReflections = true;
        level.ReflectionStreaming = new(); level.AmbientLight = .06f; level.UsePointLights = true;
        foreach (var entity in level.Entities)
        {
            if (entity.MeshPath == PbrCrateChecks.Original) entity.MeshPath = PbrCrateChecks.Corrected;
            entity.StaticLighting = !string.IsNullOrEmpty(entity.MeshPath) && ReflectionSnapshot.Invariant(entity, level);
        }
        (string Name, Vector3 Position, Vector3 Projection, Vector3 Influence, Vector3 Door, int Axis, Vector4 Colour)[] rooms =
        [
            ("CentralHall", new(0,2.5f,0), new(28,5,32), new(30,6,34), default, 0, new(1,.86f,.7f,1)),
            ("FreightStore", new(-24,2,-11), new(16,4,20), new(20,5,22), new(-15,1.5f,-8), 0, new(.7f,.85f,1,1)),
            ("Workshop", new(24,2,-11), new(16,4,20), new(20,5,22), new(15,1.5f,-8), 0, new(1,.75f,.5f,1)),
            ("MedicalSupplies", new(-24,2,11), new(16,4,20), new(20,5,22), new(-15,1.5f,8), 0, new(.7f,1,.85f,1)),
            ("UtilityRoom", new(24,2,11), new(16,4,20), new(20,5,22), new(15,1.5f,8), 0, new(.65f,.8f,1,1)),
            ("LoadingBay", new(0,2,26), new(20,4,16), new(22,5,20), new(0,1.5f,17), 2, new(1,.8f,.6f,1))
        ];
        string hall = "";
        foreach (var room in rooms)
        {
            var probe = new LevelEntityDef { Id = Guid.NewGuid().ToString("N"), Name = room.Name + " Reflection", Type = EntityTypes.ReflectionProbe,
                LocalPosition = room.Position, ReflectionProbe = new() { InfluenceSize = room.Influence, ProjectionSize = room.Projection } };
            if (hall.Length == 0) hall = probe.Id;
            else probe.ReflectionProbe.Doorway = new() { NeighborId = hall, Offset = room.Door - room.Position,
                Size = room.Axis == 0 ? new(4,3,3.6f) : new(3.6f,3,4), Axis = room.Axis };
            level.Entities.Add(probe);
            level.Entities.Add(new() { Id = Guid.NewGuid().ToString("N"), Name = room.Name + " Fixed Reflection Light", Type = EntityTypes.PointLight,
                LocalPosition = room.Position + new Vector3(0, room.Name == "CentralHall" ? 1.8f : .9f, 0),
                LightColor = room.Colour, Intensity = 3, Range = room.Name == "CentralHall" ? 22 : 14,
                BakeIndirect = true, CastShadows = false });
        }
        level.ReflectionProbeId = hall;
        var fixture = LevelIO.Load(Path.Combine(root, "Game/Content/Levels", RoomTransitionChecks.FileName));
        var metal = StructuredData.ReadJson<LevelEntityDef>(StructuredData.WriteJson(fixture.Entities.Single(e => e.Name == "CarryableReflectionTestMetal")));
        metal.Id = Guid.NewGuid().ToString("N"); metal.LocalPosition = new(3,.5f,-10); level.Entities.Add(metal);
        var editor = new LevelEditorController(); editor.LoadFromMemory(path, level);
        ReflectionRoomLayout.Create(editor);
        LevelIO.Save(path, level); Console.WriteLine("Created " + path + "; original level and launchers unchanged.");
    }

    public static void Run(string root)
    {
        string path = Path.Combine(root, "Game/Content/Levels", FileName);
        var level = LevelIO.Load(path); var editor = new LevelEditorController(); editor.LoadFromMemory(path, level);
        var layout = ReflectionRoomLayout.Create(editor);
        Check(layout.Rooms.Length == 6 && layout.Doors.Length == 5, "Expected six rooms and five explicit doorways.");
        var original = LevelIO.Load(Path.Combine(root, "Game/Content/Levels", SixRoomFlashlightTest.FileName));
        foreach (var entity in original.Entities)
        {
            var copy = level.Entities.Single(e => e.Id == entity.Id);
            var normalized = StructuredData.ReadJson<LevelEntityDef>(StructuredData.WriteJson(copy));
            normalized.MeshPath = entity.MeshPath; normalized.StaticLighting = entity.StaticLighting;
            Check(StructuredData.WriteJson(normalized).SequenceEqual(StructuredData.WriteJson(entity)), "Gameplay/transform changed: " + entity.Name);
        }
        var settings = level.ReflectionStreaming!;
        var snapshot = ReflectionSnapshot.Create(level, path, level.ReflectionProbeId!, p => Resolve(root,p));
        string key = snapshot.Key(), diffuse = BakeSnapshot.Capture(level, p => Resolve(root,p)).Key();
        editor.SetReflectionStreaming(null);
        Check(ReflectionSnapshot.Create(level,path,level.ReflectionProbeId!,p=>Resolve(root,p)).Key() == key, "Residency invalidated capture.");
        Check(BakeSnapshot.Capture(level,p=>Resolve(root,p)).Key() == diffuse, "Residency invalidated diffuse bake.");
        Check(editor.Undo() && editor.LevelFile.ReflectionStreaming?.MaxResidentProbes == 3, "Residency undo failed.");
        Check(editor.Redo() && editor.LevelFile.ReflectionStreaming == null, "Residency redo failed.");
        editor.LoadOrCreate(path, () => throw new IOException(path)); level = editor.LevelFile;
        Check(StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level)).ReflectionStreaming?.PrefetchDistance == 8, "Settings serialization failed.");
        var planner = new ReflectionResidency();
        int freight = Array.FindIndex(layout.Rooms,r => r.Position.X == -24 && r.Position.Z == -11);
        var first = planner.Select(layout,settings,new(-24,2,-11),0,new HashSet<int>());
        Check(first.Contains(freight) && first.Length == 2, "Active room / connected prefetch failed.");
        var retained = planner.Select(layout,settings,new(0,2,0),1,first.ToHashSet());
        Check(retained.Contains(freight), "Recently visited room was not retained.");
        var expired = planner.Select(layout,settings,new(0,2,0),10,retained.ToHashSet());
        Check(expired.Length == 1, "Retention failed to expire.");
        foreach (var door in layout.Doors)
        {
            var required = planner.Select(layout,settings,(door.Min+door.Max)/2,11,new HashSet<int>());
            Check(required.Contains(door.A) && required.Contains(door.B) && required.Length <= 3, "Doorway pair not pinned.");
        }
        Check(planner.Select(layout,settings,new(1000),100,new HashSet<int>()).Length == 0, "Outside bounds selected an unrelated room.");
        foreach (var invalid in new[] { new ReflectionStreamingSettings { MaxResidentProbes=2 }, new() { PrefetchDistance=float.NaN }, new() { RetainSeconds=-1 } })
        {
            bool rejected=false; try { ReflectionResidency.Validate(invalid); } catch(InvalidDataException) {rejected=true;}
            Check(rejected, "Invalid residency budget accepted.");
        }
        using var host = new EngineHost(640,480,"Reflection streaming validation");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice,renderer.WorldOutputDescription,"Shaders");
        using var session = new ReflectionProbeSession(); world.LightingModel = SurfaceLightingModel.Pbr;
        Vector3 position = new(0,2,-12);
        int renderThread = Environment.CurrentManagedThreadId;
        string ResolveOnOwner(string p)
        {
            if (Environment.CurrentManagedThreadId != renderThread) throw new InvalidOperationException("Live resolver used on worker.");
            return Resolve(root,p);
        }
        void Frame() => target.Frame(() => { world.BeginFrame(); session.Update(editor,ResolveOnOwner,world,renderer,position:position); },read:false);
        void Wait()
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Frame(); Thread.Sleep(2);
                Check(session.ResidentProbeCount + session.RetiringProbeCount <= 3, "Resident + fenced slots exceed limit.");
                Check(session.ResidentBytes <= 3.3*1048576 && session.CaptureGpuBytes == 0, "Runtime GPU budget/capture violation.");
                if(timer.Elapsed.TotalSeconds>60)throw new TimeoutException(session.Status);
            } while(session.Busy);
        }
        world.UploadBudgetMilliseconds=0;
        for(int i=0;i<10;i++){Frame();Thread.Sleep(5);}
        Check(!session.Ready,"Zero upload budget ignored."); world.UploadBudgetMilliseconds=2; Wait();
        Check(session.Ready,"Initial captures not ready: "+session.Status);
        var route = new[] {new Vector3(-15,2,-8),new(-24,2,-11),new(24,2,11),new(0,2,26),new(-24,2,11),new(24,2,-11),new(-24,2,-11),new(0,2,-12)};
        foreach(var p in route) {position=p;Wait();Check(session.Ready,"Route did not load: "+session.Status);}
        Check(session.ProbeEvictions>0 && session.ProbeLoads>6,"Route did not evict/reload probes.");
        int loads=session.ProbeLoads;
        session.Reload();Wait();Check(session.Ready && session.ProbeLoads>loads,"Forced reload failed.");
        session.Reload(); world.UploadBudgetMilliseconds=0;
        for(int i=0;i<20;i++) {position=route[i%route.Length];Frame();}
        world.UploadBudgetMilliseconds=2;Wait();Check(session.Ready,"Rapid cancellation/replacement did not recover.");
        Check(StructuredData.WriteJson(editor.LevelFile).SequenceEqual(StructuredData.WriteJson(level)),"Streaming mutated gameplay/level state.");
        Console.WriteLine($"[ReflectionStreaming] {session.Status}; peak {session.PeakGpuBytes} tracked GPU bytes.");
        // Missing capture must use black without falling back to a different room's bright data.
        string missing = Path.Combine(AppContext.BaseDirectory,"qa","missing-reflections.json");
        editor.LoadFromMemory(missing,StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level)));Wait();
        Check(!session.Ready && session.Status.Contains("Missing, stale"),"Missing capture did not fall back safely.");
        editor.LoadFromMemory(path,level);Wait();Check(session.Ready,"Recovery from missing capture failed.");
        renderer.DrainRetiredResources();
        Console.WriteLine($"PASS: {_checks} reflection streaming checks.");
    }
}

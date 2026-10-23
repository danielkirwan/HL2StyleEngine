using System.Diagnostics;
using System.Numerics;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class RoomReflectionChecks
{
    public const string FileName = "roomReflectionTest.json";
    private static int _checks;
    private static void Check(bool value, string message) { if(!value) throw new InvalidDataException(message); _checks++; }
    private static string Resolve(string root,string path) => Path.GetFullPath(Path.Combine(root,"Game",path));

    public static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path)) throw new IOException("Room reflection fixture already exists; edits will not be overwritten.");
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",PbrTestRoom.FileName));
        level.ReflectionEnvironment=ReflectionPreset.None;
        level.Entities.Single(e=>e.Name=="MaterialPointLight").BakeIndirect=true;
        foreach (int z in new[]{0,8})
        {
            var probe=new LevelEntityDef {Id=Guid.NewGuid().ToString("N"),Name=$"Room {z/8+1} Reflection",Type=EntityTypes.ReflectionProbe,
                LocalPosition=new(0,1.8f,z),ReflectionProbe=new()};
            level.Entities.Add(probe); level.ReflectionProbeId ??=probe.Id;
        }
        level.Entities.Add(new() {Id=Guid.NewGuid().ToString("N"),Name="Fixed Room Two Fill",Type=EntityTypes.PointLight,
            LocalPosition=new(0,3,8),LightColor=new(.5f,.65f,1,1),Intensity=2,Range=7,BakeIndirect=true});
        LevelIO.Save(path,level); Console.WriteLine("Created "+path);
    }

    public static void CaptureFixture(string root)
    {
        using var host=new EngineHost(640,480,"Room reflection cooking");
        var renderer=host.Context.Renderer;
        using var output=new PbrBaselineChecks.ImageTarget(renderer);
        using var world=new BasicWorldRenderer(renderer.GraphicsDevice,renderer.WorldOutputDescription,"Shaders");
        using var session=new ReflectionProbeSession();
        var editor=new LevelEditorController();
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        editor.LoadOrCreate(path,()=>throw new FileNotFoundException(path));
        foreach(var probe in editor.LevelFile.Entities.Where(e=>e.Type==EntityTypes.ReflectionProbe))
        {
            editor.LevelFile.ReflectionProbeId=probe.Id;
            session.BeginCapture(editor,p=>Resolve(root,p));
            Pump();
            Check(session.Ready,"Room capture did not become ready: "+session.Status);
            var snapshot=ReflectionSnapshot.Create(editor.LevelFile,path,probe.Id,p=>Resolve(root,p));
            var data=ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(path,probe.Id),snapshot.Key());
            Check(data!=null,"Saved room capture did not validate after reload.");
            Console.WriteLine($"Captured {probe.Name}: {data!.PayloadBytes} bytes; peak tracked GPU incl capture {session.PeakGpuBytes/1048576.0:F2} MiB.");
        }
        renderer.GraphicsDevice.WaitForIdle(); renderer.DrainRetiredResources();
        void Pump()
        {
            var timer=Stopwatch.StartNew();
            do
            {
                output.Frame(()=>{world.BeginFrame();session.Update(editor,p=>Resolve(root,p),world,renderer);},read:false);
                if(timer.Elapsed.TotalSeconds>90)throw new TimeoutException(session.Status);
                Thread.Sleep(2);
            } while(session.Busy);
        }
    }

    public static void Run(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        var level=LevelIO.Load(path);
        var snapshot=ReflectionSnapshot.Create(level,path,level.ReflectionProbeId!,p=>Resolve(root,p));
        string key=snapshot.Key();
        var data=ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(path,snapshot.Probe.Id),key);
        Check(data!=null,"Cook the fixture first.");
        Check(snapshot.FixedLightIds.Length==2 && snapshot.Level.Entities.Single(e=>e.Name=="SecondRoomSpot").LightEnabled==false,
            "Switched light entered the invariant capture.");
        var input=ReflectionCaptureInput.Prepare(snapshot,default);
        Check(!input.Models.ContainsKey(PbrCrateChecks.Corrected),"Movable crate entered capture dependencies.");
        var clone=StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level));
        Check(clone.ReflectionProbeId==level.ReflectionProbeId && clone.Entities.Count(e=>e.ReflectionProbe!=null)==2,"Probe JSON/RE:Dox lost settings.");
        string changedKey(Action<LevelFile> change)
        {
            var copy=StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level));change(copy);
            return ReflectionSnapshot.Create(copy,path,copy.ReflectionProbeId!,p=>Resolve(root,p)).Key();
        }
        Check(changedKey(l=>l.Entities.First(e=>e.StaticLighting).LocalPosition += Vector3.UnitX)!=key,"Geometry edits did not invalidate capture.");
        Check(changedKey(l=>l.Entities.First(e=>e.BakeIndirect).Intensity+=1)!=key,"Fixed lighting edits did not invalidate capture.");
        Check(changedKey(l=>l.Entities.First(e=>e.ReflectionProbe!=null).ReflectionProbe!.Resolution=64)!=key,"Resolution did not invalidate capture.");
        Check(changedKey(l=>l.Exposure=2)==key,"Exposure contaminated linear HDR key.");
        var editor=new LevelEditorController();editor.LoadFromMemory(path,clone);
        string diffuseBefore=BakeSnapshot.Capture(level,p=>Resolve(root,p)).Key();
        editor.AddReflectionProbe(new(1,2,3));
        Check(editor.Undo() && editor.LevelFile.Entities.Count==level.Entities.Count,"Probe placement undo failed.");
        Check(editor.Redo() && editor.LevelFile.Entities.Count==level.Entities.Count+1,"Probe placement redo failed.");
        Check(BakeSnapshot.Capture(editor.LevelFile,p=>Resolve(root,p)).Key()==diffuseBefore,"Probe placement invalidated diffuse bake.");
        Check(editor.TryBuildPrefabFromSelection("Probe", "", false, out var prefab) && editor.AddPrefabInstance(prefab,"qa-probe.json"),"Probe prefab round trip failed.");
        var copies=editor.LevelFile.Entities.Where(e=>e.ReflectionProbe!=null).ToArray();
        Check(copies.Select(e=>e.Id).Distinct().Count()==copies.Length && copies[^1].ReflectionProbe!.Resolution==128,"Prefab probe GUID/settings were not cloned.");
        string output=Path.Combine(AppContext.BaseDirectory,"qa","room-reflection-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        string cache=Path.Combine(output,"test.hs2reflection");
        ReflectionProbeCache.Save(cache,key,data!);
        Check(ReflectionProbeCache.Load(cache,key)!=null && ReflectionProbeCache.Load(cache,new string('0',64))==null,"Cache key/reload mismatch.");
        byte[] bytes=File.ReadAllBytes(cache);
        foreach(byte[] corrupt in new[]{bytes[..^1],bytes.Concat(new byte[]{0}).ToArray(),Mutate(bytes,80),Mutate(bytes,4)})
        { File.WriteAllBytes(cache,corrupt); Check(ReflectionProbeCache.Load(cache,key)==null,"Corrupt capture accepted."); }
        File.WriteAllBytes(cache,bytes);
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { ReflectionProbeCache.Save(cache,key,data!,cancelled.Token);throw new InvalidDataException("Cancelled cache save succeeded."); }
            catch(OperationCanceledException) {Check(File.ReadAllBytes(cache).SequenceEqual(bytes),"Cancellation replaced ready cache.");}
        }
        using var host=new EngineHost(640,480,"Room reflection validation");
        Orientation(host,output);
        SessionChecks(root,host,path,level,output);
        Console.WriteLine($"PASS: {_checks} room reflection checks; captures: {output}");
    }

    private static byte[] Mutate(byte[] bytes,int offset) {var copy=bytes.ToArray();copy[offset]^=0x7f;return copy;}

    private static void Orientation(EngineHost host,string output)
    {
        string modelPath=Path.Combine(output,"panel.glb");
        PbrTestRoom.WriteGlb(modelPath,PbrMaterialChecks.Quad(),0,1,Vector4.One);
        var level=new LevelFile {LightingModel=SurfaceLightingModel.Pbr,AmbientLight=1,DirectionalLight=0};
        var probe=new LevelEntityDef {Id=Guid.NewGuid().ToString("N"),Type=EntityTypes.ReflectionProbe,ReflectionProbe=new(){Resolution=32}};
        level.Entities.Add(probe);level.ReflectionProbeId=probe.Id;
        Vector3[] colours=[new(.6f,.1f,.1f),new(.1f,.6f,.1f),new(.1f,.1f,.6f),new(.6f,.6f,.1f),new(.6f,.1f,.6f),new(.1f,.6f,.6f)];
        Vector3[] rotations=[new(0,90,0),new(0,90,0),new(90,0,0),new(90,0,0),Vector3.Zero,Vector3.Zero];
        for(int f=0;f<6;f++)
        {
            Vector3 dir=ReflectionFilter.Direction(f,0,0), marker=ReflectionFilter.Direction(f,.4f,-.3f);
            marker*=1.98f/MathF.Max(MathF.Abs(marker.X),MathF.Max(MathF.Abs(marker.Y),MathF.Abs(marker.Z)));
            level.Entities.Add(new() {Id=Guid.NewGuid().ToString("N"),Type=EntityTypes.Prop,MeshPath=modelPath,StaticLighting=true,
                LocalPosition=dir*2,LocalRotationEulerDeg=rotations[f],Size=new(4,4,.001f),Color=new Vector4(colours[f],1)});
            level.Entities.Add(new() {Id=Guid.NewGuid().ToString("N"),Type=EntityTypes.Prop,MeshPath=modelPath,StaticLighting=true,
                LocalPosition=marker,LocalRotationEulerDeg=rotations[f],Size=new(.4f,.4f,.001f),Color=Vector4.One});
        }
        var snap=ReflectionSnapshot.Create(level,Path.Combine(output,"orientation.json"),probe.Id,p=>p);
        using var capture=new RoomReflectionCapture(host.Context.Renderer.GraphicsDevice,ReflectionCaptureInput.Prepare(snap,default));
        var timer=Stopwatch.StartNew();
        while(!capture.Complete) {capture.Step();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Six-face capture");}
        for(int f=0;f<6;f++)
        {
            Vector3 Pixel(int x,int y)
            {
                int index=(y*32+x)*8;byte[] b=capture.Faces[f];
                return new((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b,index)),(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b,index+2)),(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b,index+4)));
            }
            var middle=Pixel(16,16);var marker=Pixel(22,11);
            Check(Vector3.Distance(middle,colours[f])<.01f,$"HDR face {f} direction mismatch: {middle}.");
            Check(Vector3.Distance(marker,Vector3.One)<.01f,$"Face {f} asymmetric marker flipped: {marker}.");
        }
        var filtered=RoomReflectionCapture.Filter(capture.Faces,32);
        Check(filtered.Faces.All(f=>f.Length>0),"Room filtering lost faces.");
        byte[] source=File.ReadAllBytes(modelPath);
        string sourceKey=snap.Key();
        File.WriteAllBytes(modelPath,source.Concat(new byte[]{0}).ToArray());
        Check(snap.Key()!=sourceKey,"Source content fingerprint did not invalidate capture.");
        File.WriteAllBytes(modelPath,source);
        level.Bake=new() {Enabled=true,TexelsPerTriangle=4,Samples=8,RayDistance=10};
        level.UsePointLights=true;level.AmbientLight=.1f;
        level.Entities.Add(new() {Id=Guid.NewGuid().ToString("N"),Type=EntityTypes.PointLight,LocalPosition=Vector3.Zero,
            BakeIndirect=true,Intensity=2,Range=8});
        string diffusePath=Path.Combine(output,"orientation.json");
        var diffuse=IndirectLightBaker.Bake(BakeSnapshot.Capture(level,p=>p));
        LightmapFile.Save(LightmapFile.PathFor(diffusePath),diffuse);
        var withDiffuse=ReflectionSnapshot.Create(level,diffusePath,probe.Id,p=>p);
        var prepared=ReflectionCaptureInput.Prepare(withDiffuse,default);
        Check(prepared.Diffuse?.Records.Count==12,"Validated diffuse capture dependencies missing.");
        using var diffuseCapture=new RoomReflectionCapture(host.Context.Renderer.GraphicsDevice,prepared);
        timer.Restart();
        while(!diffuseCapture.Complete)
        {diffuseCapture.Step();Thread.Sleep(1);if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Diffuse capture");}
        Check(RoomReflectionCapture.Filter(diffuseCapture.Faces,32).PayloadBytes>0,"Diffuse-integrated capture failed.");
        host.Context.Renderer.GraphicsDevice.WaitForIdle();
    }

    private static void SessionChecks(string root,EngineHost host,string path,LevelFile level,string output)
    {
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var world=new BasicWorldRenderer(renderer.GraphicsDevice,renderer.WorldOutputDescription,"Shaders") {LightingModel=SurfaceLightingModel.Pbr};
        using var session=new ReflectionProbeSession();
        var editor=new LevelEditorController();editor.LoadFromMemory(path,level);
        using var panel=world.CreateRenderModel(new([PbrMaterialChecks.Quad()]));
        world.ReflectionPreviewEnabled=false;
        void Draw()
        {
            world.BeginFrame();session.Update(editor,p=>Resolve(root,p),world,renderer);
            var eye=new Vector3(0,0,4);
            world.UpdateCamera(Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,target.Width/(float)target.Height,.05f,100),eye);
            world.UpdatePointLights([new(new(1,1,3),Vector3.One,3,10,Vector3.Normalize(new(-1,-1,-3)),70,true)],eye);
            world.RenderShadows(renderer,[new(panel,Matrix4x4.Identity,Vector3.Zero,2)],new(0,0,target.Width,target.Height,0,1));
            using(world.BatchObjectUploads())world.DrawModel(renderer.CommandList,panel,Matrix4x4.Identity,Vector4.One);
        }
        void Frame() => target.Frame(Draw,read:false);
        Frame();Wait();Check(session.Ready && world.ReflectionReady,"Restart did not load completed room cache: "+session.Status);
        var before=target.Frame(Draw);
        var prior=world.Reflection;
        session.BeginCapture(editor,p=>Resolve(root,p));
        var captureWait=Stopwatch.StartNew();
        while(session.CompletedFaces<1)
        {
            Frame();Thread.Sleep(1);
            Check(ReferenceEquals(prior,world.Reflection),"Same-input rebuild replaced a ready capture too early.");
            if(!session.Busy || captureWait.Elapsed.TotalSeconds>30)throw new TimeoutException(session.Status);
        }
        session.Cancel();Frame();
        Check(session.Ready && ReferenceEquals(prior,world.Reflection),"Cancelling submitted GPU capture lost ready capture.");
        Check(target.Frame(Draw).SequenceEqual(before),"Capture/cancellation changed main-view camera or cached shadows.");
        var light=level.Entities.First(e=>e.BakeIndirect);
        var state=new LevelLightState();
        // Simulate an unsupported live fixed-light override without changing authored data.
        state.Restore([new LightStateOverride { EntityId=light.Id, Enabled=false }]);
        target.Frame(()=>{world.BeginFrame();session.Update(editor,p=>Resolve(root,p),world,renderer,state);},read:false);
        Check(!world.ReflectionReady,"Overridden fixed lighting left a bright capture active.");
        Frame();Check(world.ReflectionReady,"Restored fixed lighting did not restore ready capture.");
        session.Reload();world.UploadBudgetMilliseconds=0;Frame();
        var noUploadWait=Stopwatch.StartNew();
        while(session.PendingCpuBytes==0)
        {Frame();Thread.Sleep(1);if(noUploadWait.Elapsed.TotalSeconds>30)throw new TimeoutException(session.Status);}
        Check(!world.ReflectionReady,"Zero budget exposed an incomplete capture.");
        world.UploadBudgetMilliseconds=2;Wait();Check(world.ReflectionReady,"Budgeted capture upload did not resume.");
        editor.LevelFile.Entities.First(e=>e.StaticLighting).LocalPosition += Vector3.UnitX;editor.RebuildRuntimeFromLevel();
        Frame();Check(!session.Ready && !world.ReflectionReady,"Edited geometry retained stale reflection.");Wait();
        Check(!session.Ready,"Stale cache was accepted after source change.");
        renderer.GraphicsDevice.WaitForIdle();renderer.DrainRetiredResources();
        void Wait() {var clock=Stopwatch.StartNew();while(session.Busy){Frame();Thread.Sleep(1);if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException(session.Status);}}
    }
}

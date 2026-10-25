using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class RoomTransitionChecks
{
    public const string FileName="roomTransitionTest.json";
    private static int _checks;
    private static void Check(bool value,string message) {if(!value)throw new InvalidDataException(message);_checks++;}
    private static string Resolve(string root,string path)=>Path.GetFullPath(Path.Combine(root,"Game",path));

    public static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path))throw new IOException("Transition fixture exists; edits will not be overwritten.");
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",RoomReflectionChecks.FileName));
        var probes=level.Entities.Where(e=>e.ReflectionProbe!=null).OrderBy(e=>((Vector3)e.LocalPosition).Z).ToArray();
        if(probes.Length!=2)throw new InvalidDataException("Expected the two-room capture fixture.");
        foreach(var probe in probes)
        {
            probe.LocalPosition=new(0,2,((Vector3)probe.LocalPosition).Z);
            probe.ReflectionProbe!.InfluenceSize=new(8.4f,4.4f,8);
            probe.ReflectionProbe.ProjectionSize=new(8,4,8);
            probe.ReflectionProbe.Doorway=null;
        }
        probes[0].ReflectionProbe!.Doorway=new(){NeighborId=probes[1].Id,Offset=new(0,-.5f,4),Size=new(2.4f,3,2),Axis=2};
        level.AutomaticRoomReflections=true;level.ReflectionProbeId=probes[0].Id;
        var metal=StructuredData.ReadJson<LevelEntityDef>(StructuredData.WriteJson(level.Entities.Single(e=>e.Name=="Metal_Roughness_3")));
        metal.Id=Guid.NewGuid().ToString("N");metal.Name="CarryableReflectionTestMetal";metal.Type=EntityTypes.RigidBody;
        metal.LocalPosition=new(1.8f,.5f,-.6f);metal.Size=new(.7f,.7f,.7f);metal.MotionType=MotionType.Dynamic;
        metal.CanPickUp=true;metal.StaticLighting=false;metal.Mass=5;metal.Restitution=0;
        level.Entities.Add(metal);
        LevelIO.Save(path,level);Console.WriteLine("Created "+path);
    }

    public static void Run(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        var editor=new LevelEditorController();editor.LoadOrCreate(path,()=>throw new IOException(path));
        var layout=ReflectionRoomLayout.Create(editor);
        Check(layout.Rooms.Length==2 && layout.Doors.Length==1,"Fixture room links missing.");
        var json=StructuredData.WriteJson(editor.LevelFile);
        var copy=StructuredData.ReadJson<LevelFile>(json);
        Check(copy.AutomaticRoomReflections && copy.Entities.Single(e=>e.ReflectionProbe?.Doorway!=null).ReflectionProbe!.Doorway!.Axis==2,"Automatic settings lost in JSON.");
        string diffuse=BakeSnapshot.Capture(copy,p=>Resolve(root,p)).Key();
        editor.SetAutomaticRoomReflections(false);
        Check(editor.Undo() && editor.LevelFile.AutomaticRoomReflections,"Automatic toggle undo failed.");
        Check(editor.Redo() && !editor.LevelFile.AutomaticRoomReflections,"Automatic toggle redo failed.");
        Check(BakeSnapshot.Capture(editor.LevelFile,p=>Resolve(root,p)).Key()==diffuse,"Automatic mode invalidated diffuse bake.");
        editor.LoadFromMemory(path,copy);
        var snapshot=ReflectionSnapshot.Create(copy,path,copy.ReflectionProbeId!,p=>Resolve(root,p));
        copy.AutomaticRoomReflections=false;
        Check(ReflectionSnapshot.Create(copy,path,copy.ReflectionProbeId!,p=>Resolve(root,p)).Key()==snapshot.Key(),"Preview mode invalidated capture.");
        copy.AutomaticRoomReflections=true;
        var linked=copy.Entities.Single(e=>e.ReflectionProbe?.Doorway!=null);
        string neighbor=linked.ReflectionProbe!.Doorway!.NeighborId;
        linked.ReflectionProbe.Doorway.NeighborId="missing";
        bool rejected=false;try{ReflectionRoomLayout.Create(editor);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Missing neighbor accepted.");linked.ReflectionProbe.Doorway.NeighborId=neighbor;
        var prefabEditor=new LevelEditorController();prefabEditor.LoadFromMemory("probe-prefab.json",StructuredData.ReadJson<LevelFile>(json));
        var prefabRoot=prefabEditor.LevelFile.Entities.Single(e=>e.ReflectionProbe?.Doorway!=null);
        prefabEditor.LevelFile.Entities.Single(e=>e.Id==prefabRoot.ReflectionProbe!.Doorway!.NeighborId).ParentId=prefabRoot.Id;
        typeof(LevelEditorController).GetProperty(nameof(LevelEditorController.SelectedEntityIndex))!.SetValue(prefabEditor,prefabEditor.LevelFile.Entities.IndexOf(prefabRoot));
        Check(prefabEditor.TryBuildPrefabFromSelection("Linked rooms","",false,out var prefab) && prefabEditor.AddPrefabInstance(prefab,"rooms.prefab.json"),"Linked probe prefab failed.");
        var newProbes=prefabEditor.LevelFile.Entities.Where(e=>e.ReflectionProbe!=null).TakeLast(2).ToArray();
        var newLink=newProbes.Single(e=>e.ReflectionProbe!.Doorway!=null).ReflectionProbe!.Doorway!;
        Check(newProbes.Any(e=>e.Id==newLink.NeighborId) && newLink.NeighborId!=neighbor,"Prefab link did not remap to cloned GUID.");
        foreach(var room in layout.Rooms)
        {
            foreach(var ray in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,new Vector3(1e-9f,0,1),new Vector3(-1,0,0)})
            {
                var projected=ReflectionRooms.Project(room.Position+new Vector3(.3f,0,.5f),ray,room);
                Check(float.IsFinite(projected.X+projected.Y+projected.Z) && projected.LengthSquared()>0,"Parallel projection invalid.");
            }
            Check(ReflectionRooms.Project(room.Max+Vector3.One,Vector3.UnitZ,room)==Vector3.UnitZ,"Outside projection did not fall back.");
        }
        var door=layout.Doors[0];float previous=-1;
        for(int i=0;i<=20;i++)
        {
            float t=i/20f; Vector3 p=(door.Min+door.Max)/2 + door.Direction*(door.Max-door.Min)*(t-.5f);
            float weight=ReflectionRooms.BlendWeight(p,door);
            Check(weight>=previous && MathF.Abs(weight-t*t*(3-2*t))<1e-5f,"Doorway blend is not smooth/monotonic.");previous=weight;
        }
        using var host=new EngineHost(640,480,"Room transition checks");
        Gpu(host);
        Session(root,host,editor);
        Console.WriteLine($"PASS: {_checks} room transition checks.");
    }

    private static void Gpu(EngineHost host)
    {
        var renderer=host.Context.Renderer;var device=renderer.GraphicsDevice;var factory=device.ResourceFactory;
        using var world=new BasicWorldRenderer(device,renderer.WorldOutputDescription,"Shaders");
        world.LightingModel=SurfaceLightingModel.Pbr;world.AmbientStrength=world.DirectionalStrength=0;
        world.Diagnostic=LightingDiagnostic.SpecularIndirect;
        ReflectionEnvironmentData Data(Func<Vector3,Vector3> colour)
        {
            Half[] lut=[(Half)1,(Half)0];
            return ReflectionFilter.Build(colour,32,1,64,1) with {Lut=MemoryMarshal.AsBytes(lut.AsSpan()).ToArray()};
        }
        using var red=world.CreateReflectionEnvironment(Data(_=>new(1,0,0)));
        using var blue=world.CreateReflectionEnvironment(Data(_=>new(0,0,1)));
        using var direction=world.CreateReflectionEnvironment(Data(d=>(d+Vector3.One)*.5f));
        foreach(var env in new[]{red,blue,direction})while(!env.IsReady)env.UploadNext();
        ReflectionRoom[] rooms=[new("A",new(0,0,-2),new(-3,-3,-4),new(3,3,0),new(-3,-3,-4),new(3,3,0),0),
            new("B",new(0,0,2),new(-3,-3,0),new(3,3,4),new(-3,-3,0),new(3,3,4),1)];
        ReflectionDoorway[] doors=[new(0,1,new(-1,-2,-1),new(1,2,1),Vector3.UnitZ)];
        using var binding=world.CreateReflectionRooms(rooms,doors,[red,blue]);world.RoomReflections=binding;
        using var blackNeighbor=world.CreateReflectionRooms(rooms,doors,[red,null]);
        var quad=PbrMaterialChecks.Quad();using var model=world.CreateRenderModel(new([new(quad.Positions,quad.Normals,null,quad.Indices,Vector4.One,null,null,1,0)]));
        uint w=renderer.WorldFramebuffer.Width,h=renderer.WorldFramebuffer.Height;
        using var resolved=factory.CreateTexture(TextureDescription.Texture2D(w,h,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.RenderTarget));
        using var staging=factory.CreateTexture(TextureDescription.Texture2D(w,h,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Staging));
        Vector3 Frame(Vector3 p,Vector3? view=null)
        {
            Vector3 look=view??Vector3.UnitZ, eye=p+look*3,up=Vector3.UnitY;
            var transform=Matrix4x4.CreateScale(.2f)*Matrix4x4.CreateWorld(p,-look,up);
            var vp=Matrix4x4.CreateLookAt(eye,p,up)*Matrix4x4.CreateOrthographic(2,1.5f,.05f,100);
            renderer.BeginFrame();world.BeginFrame();world.UpdateCamera(vp,eye);world.UpdatePointLights([],eye);
            using(world.BatchObjectUploads())world.DrawModel(renderer.CommandList,model,transform,Vector4.One);
            renderer.CommandList.ResolveTexture(renderer.WorldFramebuffer.ColorTargets[0].Target,resolved);
            renderer.CommandList.CopyTexture(resolved,staging);renderer.EndFrame();device.WaitForIdle();
            var map=device.Map<ushort>(staging,MapMode.Read);
            try
            {
                Check(world.ObjectUploadCount==1,"Room selection broke batching.");
                for(int y=0;y<h;y++)for(int x=0;x<w*4;x++)CheckFinite(map[x,y]);
                int x0=(int)(w/2)*4,y0=(int)(h/2);
                return new((float)BitConverter.UInt16BitsToHalf(map[x0,y0]),(float)BitConverter.UInt16BitsToHalf(map[x0+1,y0]),(float)BitConverter.UInt16BitsToHalf(map[x0+2,y0]));
            }
            finally{device.Unmap(staging);}
        }
        void Near(Vector3 actual,Vector3 expected,string message,float tolerance=.025f)=>Check(Vector3.Distance(actual,expected)<tolerance,$"{message}: {actual} vs {expected}");
        Near(Frame(new(0,0,-2)),Vector3.UnitX,"First room");Near(Frame(new(0,0,2)),Vector3.UnitZ,"Second room");
        for(int i=0;i<=10;i++)
        {
            float z=-1+i*.2f,t=i*.1f,weight=t*t*(3-2*t);
            Near(Frame(new(0,0,z)),new(1-weight,0,weight),"Doorway HDR blend");
        }
        Near(Frame(new(2,0,-.1f)),Vector3.UnitX,"Bright neighbor leaked through solid wall");
        Near(Frame(new(2,0,.1f)),Vector3.UnitZ,"Second side wall classified incorrectly");
        Near(Frame(new(2,0,-.05f),Vector3.UnitX),Vector3.UnitX,"Spanning part leaked across first side of solid wall");
        Near(Frame(new(2,0,.05f),Vector3.UnitX),Vector3.UnitZ,"Spanning part lost second side room");
        Near(Frame(new(5,0,-2)),Vector3.Zero,"Out-of-room reflection did not fall back");
        world.RoomReflections=blackNeighbor;
        Near(Frame(new(0,0,0)),new(.5f,0,0),"Missing neighbor was renormalized instead of blending to black");
        Near(Frame(new(0,0,2)),Vector3.Zero,"Missing neighbor retained stale bright capture");
        var highA=rooms[0] with {Priority=int.MaxValue-1};
        var highB=rooms[1] with {Min=rooms[0].Min,Max=rooms[0].Max,Priority=int.MaxValue};
        using var priority=world.CreateReflectionRooms([highA,highB],doors,[red,blue]);world.RoomReflections=priority;
        Near(Frame(new(2,0,-2)),Vector3.UnitZ,"Large integer priorities lost precision in GPU metadata");
        using var tied=world.CreateReflectionRooms([highA,highB with {Id="0",Priority=highA.Priority}],doors,[red,blue]);world.RoomReflections=tied;
        Near(Frame(new(2,0,-2)),Vector3.UnitZ,"Equal-priority room overlap did not use stable ID order");
        using var projected=world.CreateReflectionRooms([rooms[0]],[],[direction]);world.RoomReflections=projected;
        var point=new Vector3(1,0,-2);var ray=Vector3.Normalize(new Vector3(.4f,0,1));
        Near(Frame(point,ray),(Vector3.Normalize(ReflectionRooms.Project(point,ray,rooms[0]))+Vector3.One)*.5f,"Box projection GPU/CPU parity",.035f);
        world.ReflectionPreviewEnabled=false;Near(Frame(point),Vector3.Zero,"Automatic preview did not disable");
        world.RoomReflections=null;device.WaitForIdle();renderer.DrainRetiredResources();
        static void CheckFinite(ushort value){if(!Half.IsFinite(BitConverter.UInt16BitsToHalf(value)))throw new InvalidDataException("Nonfinite room shader pixel.");}
    }

    private static void Session(string root,EngineHost host,LevelEditorController editor)
    {
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var world=new BasicWorldRenderer(renderer.GraphicsDevice,renderer.WorldOutputDescription,"Shaders");
        world.LightingModel=SurfaceLightingModel.Pbr;
        using var session=new ReflectionProbeSession();
        var state=new LevelLightState();
        void Frame()=>target.Frame(()=>{world.BeginFrame();session.Update(editor,p=>Resolve(root,p),world,renderer,state);},read:false);
        void Wait(){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(2);if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException(session.Status);}while(session.Busy);}
        world.UploadBudgetMilliseconds=0;for(int i=0;i<10;i++){Frame();Thread.Sleep(5);}
        Check(!session.Ready,"Automatic room ignored zero upload budget.");
        world.UploadBudgetMilliseconds=2;Wait();Check(session.Ready && world.RoomReflections?.RoomCount==2,"Automatic captures not ready: "+session.Status);
        Check(session.ResidentBytes<2.2*1048576 && session.CaptureGpuBytes==0,"Automatic residency/capture budget invalid.");
        session.PreviewEnabled=false;Frame();Check(world.RoomReflections==null && session.Ready,"Preview toggle dropped ready captures.");
        session.PreviewEnabled=true;Frame();Check(world.RoomReflections!=null,"Preview failed to resume.");
        var beforeSwitch=world.RoomReflections;
        state.Restore([new(){EntityId=editor.LevelFile.Entities.Single(e=>e.Name=="SecondRoomSpot").Id,Enabled=false}]);Frame();
        Check(ReferenceEquals(beforeSwitch,world.RoomReflections),"Realtime-only switch changed invariant capture bindings.");
        state.Restore([new(){EntityId=editor.LevelFile.Entities.Single(e=>e.Name=="MaterialPointLight").Id,Enabled=false}]);Frame();
        Check(!ReferenceEquals(beforeSwitch,world.RoomReflections) && session.Status.Contains("incompatible lights"),"Changed fixed light retained bright bindings.");
        state.Clear();Frame();Check(!session.Status.Contains("incompatible lights") && session.Ready,"Fixed-light restoration failed.");
        session.Reload();Wait();Check(session.Ready,"Automatic reload failed.");
        session.BeginCapture(editor,p=>Resolve(root,p));
        var timer=Stopwatch.StartNew();while(session.CompletedFaces==0 && session.Busy){Frame();Thread.Sleep(2);if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException(session.Status);}
        Check(session.CompletedFaces>0,"Automatic capture never submitted a face.");session.Cancel();Frame();Check(session.Ready && !session.Busy,"Cancel lost compatible automatic capture.");
        editor.SetAutomaticRoomReflections(false);Wait();Check(world.RoomReflections==null && world.Reflection!=null,"Manual preview restoration failed.");
        editor.SetAutomaticRoomReflections(true);Wait();Check(session.Ready && world.RoomReflections!=null,"Automatic mode restoration failed.");
        var original=StructuredData.WriteJson(editor.LevelFile);
        editor.LevelFile.Entities.First(e=>e.StaticLighting).LocalPosition+=Vector3.UnitX;editor.RebuildRuntimeFromLevel();Frame();
        Check(!session.Ready,"Edited geometry retained old automatic captures.");Wait();Check(!session.Ready,"Automatic loader accepted stale captures.");
        editor.LoadFromMemory(editor.LevelPath,StructuredData.ReadJson<LevelFile>(original));Wait();Check(session.Ready,"Automatic captures failed after restoring valid source.");
        renderer.GraphicsDevice.WaitForIdle();renderer.DrainRetiredResources();
    }
}

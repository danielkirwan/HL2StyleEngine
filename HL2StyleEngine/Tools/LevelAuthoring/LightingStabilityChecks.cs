using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Engine.Runtime.Entities;

internal static class LightingStabilityChecks
{
    public const string FileName = "sixRoomLightingStabilityTest.json";
    internal static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path))throw new IOException("Stability level exists; author edits will not be overwritten.");
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",PointLightOnlyChecks.FileName));
        level.ReflectionStreaming=null;
        level.PointShadowHysteresis=2;
        level.Bake=new() { Enabled=true,TexelsPerTriangle=4,Samples=16,RayDistance=25,Probes=new(){Spacing=3} };
        foreach(int x in new[]{-9,9})foreach(int z in new[]{-10,0,10})
            level.Entities.AddRange(PointLightOnlyChecks.Fixture($"Hall Side {x} {z}",new(x,4.78f,z),new(.85f,.9f,1),8));
        var ids=level.Entities.Where(e=>e.Type==EntityTypes.PointLight).Select(e=>e.Id).ToArray();
        level.Entities.Add(new(){Id=Guid.NewGuid().ToString("N"),Name="Fixture Power Switch",Type=EntityTypes.Prop,
            LocalPosition=new(1.2f,1.15f,-10.5f),Size=new(.3f,.5f,.3f),Color=Vector4.One,
            MeshPath="Content/Models/ViewModels/Lever04.glb",Interaction=new(){Kind="LightSwitch",Targets=ids.ToList(),Prompt="Toggle fixture power"}});
        level.Entities.Add(new(){Id=Guid.NewGuid().ToString("N"),Name="Fixture Power Switch Stand",Type=EntityTypes.Prop,
            LocalPosition=new(1.2f,.45f,-10.5f),Size=new(.5f,.9f,.5f),Color=Vector4.One,StaticLighting=true,MeshPath="Content/Models/ViewModels/Beam_4m.glb"});
        foreach(var entity in level.Entities.Where(e=>e.StaticLighting))
            entity.ExcludeFromDiffuseBake=!(Path.GetFileName(entity.MeshPath).StartsWith("Basement_Corridor_",StringComparison.Ordinal) ||
                Path.GetFileName(entity.MeshPath)=="Beam_4m.glb");
        var editor=new LevelEditorController();editor.LoadFromMemory(path,level);editor.CreateOnOffLightingScenarios();
        LevelIO.Save(path,editor.LevelFile);
        Console.WriteLine("Created "+path+". Cook on/off states before launching.");
    }

    internal static void Cook(string root,string id)
    {
        string source=Path.Combine(root,"Game/Content/Levels",FileName);
        var level=LevelIO.Load(source);
        var scenario=level.LightingScenarios!.Single(s=>s.Id==id);
        var view=LightingScenarioResolver.CreateView(level,scenario);
        string path=LightingScenarioResolver.PathFor(source,id);
        if(view.Bake.Enabled)
        {
            var timer=Stopwatch.StartNew();int last=-1;
            var snapshot=BakeSnapshot.Capture(view,p=>Path.Combine(root,"Game",p));
            var data=IndirectLightBaker.Bake(snapshot,p=> {int step=(int)(p*10);if(Interlocked.Exchange(ref last,step)!=step)Console.WriteLine($"Bake {step*10}%");});
            LightmapFile.Save(LightmapFile.PathFor(path),data);
            if(LightmapFile.Load(LightmapFile.PathFor(path),snapshot.Key())==null)throw new InvalidDataException("Cooked bake failed validation.");
            Console.WriteLine($"Bake {id}: {data.Records.Count} models; {data.Records.Sum(r=>r.PartTriangles.Sum())} triangles; {data.Size} atlas; {data.Probes!.Valid.Count(v=>v)} valid probes; {timer.Elapsed.TotalSeconds:F1}s");
        }
        RoomReflectionChecks.CaptureFixture(root,Path.GetFileName(path),view);
    }

    internal static void Validate(string root)
    {
        int checks=0;
        void Check(bool ok,string message) {if(!ok)throw new InvalidDataException(message);checks++;}
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        var level=LevelIO.Load(path); LightingScenarioResolver.Validate(level);
        Check(level.AmbientLight==0 && level.DirectionalLight==0 && level.ReflectionStreaming==null,"Fill or residency settings changed.");
        Check(level.Entities.Count(e=>e.Type==EntityTypes.PointLight)==14,"Expected fourteen visible fixtures.");
        var before=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",PointLightOnlyChecks.FileName));
        foreach(var prior in before.Entities)
        {
            var current=StructuredData.ReadJson<LevelEntityDef>(StructuredData.WriteJson(level.Entities.Single(e=>e.Id==prior.Id)));
            current.ExcludeFromDiffuseBake=prior.ExcludeFromDiffuseBake;
            Check(StructuredData.WriteJson(current).SequenceEqual(StructuredData.WriteJson(prior)),"Changed existing geometry/gameplay: "+prior.Name);
        }
        var editor=new LevelEditorController();editor.LoadFromMemory(path,level);
        var resolver=new LightingScenarioResolver();var state=new LevelLightState();
        var sourceBytes=StructuredData.WriteJson(level);
        foreach(var scenario in level.LightingScenarios!)
        {
            state.Restore(scenario.Lights);
            var view=resolver.Select(editor,state);
            Check(view.LevelPath==LightingScenarioResolver.PathFor(path,scenario.Id),"Wrong scenario selected.");
            if(view.LevelFile.Bake.Enabled)
            {
                var snapshot=BakeSnapshot.Capture(view.LevelFile,p=>Path.Combine(root,"Game",p));
                var data=LightmapFile.Load(LightmapFile.PathFor(view.LevelPath),snapshot.Key());
                Check(data?.Probes?.IsValid()==true,"Invalid scenario diffuse/probe cache.");
                Check(data!.Records.Count==view.LevelFile.Entities.Count(e=>e.StaticLighting && !e.ExcludeFromDiffuseBake),"Missing architecture receivers.");
            }
            foreach(var probe in view.LevelFile.Entities.Where(e=>e.ReflectionProbe!=null))
            {
                var snapshot=ReflectionSnapshot.Create(view.LevelFile,view.LevelPath,probe.Id,p=>Path.Combine(root,"Game",p));
                Check(ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(view.LevelPath,probe.Id),snapshot.Key())!=null,"Stale/missing "+scenario.Id+" reflection.");
            }
        }
        state.Restore([new(){EntityId=level.LightingScenarios[0].Lights[0].EntityId,Enabled=false}]);
        var fallback=resolver.Select(editor,state);
        Check(!fallback.LevelFile.Bake.Enabled && !fallback.LevelFile.AutomaticRoomReflections && fallback.LevelFile.ReflectionProbeId==null,"Mixed state reused an incorrect bake.");
        Check(StructuredData.WriteJson(level).SequenceEqual(sourceBytes),"Runtime scenario changed authored scene.");
        editor.SetAuthoredLightingScenario("off");Check(editor.LevelFile.Entities.Where(e=>e.Type==EntityTypes.PointLight).All(e=>!e.LightEnabled),"Editor state preview failed.");
        Check(editor.Undo() && editor.LevelFile.Entities.Where(e=>e.Type==EntityTypes.PointLight).All(e=>e.LightEnabled),"Lighting-state undo failed.");
        foreach(Action<LevelFile> invalidate in new Action<LevelFile>[]
        {
            l=>l.LightingScenarios![0].Id="../escape",
            l=>l.LightingScenarios![0].Id="unmatched",
            l=>l.LightingScenarios![1].Id="ON",
            l=>l.LightingScenarios![1].Lights=l.LightingScenarios[0].Lights,
            l=>l.LightingScenarios![0].Lights.RemoveAt(0),
            l=>l.LightingScenarios![0].Lights[0].EntityId="missing",
            l=>l.Entities.First(e=>e.BakeIndirect).FlickerAmount=.5f
        })
        {
            var invalid=StructuredData.ReadJson<LevelFile>(sourceBytes);invalidate(invalid);
            bool rejected=false;
            try{LightingScenarioResolver.Validate(invalid);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Invalid lighting scenario accepted.");
            var invalidEditor=new LevelEditorController();invalidEditor.LoadFromMemory(path,invalid);
            var safe=new LightingScenarioResolver().Select(invalidEditor,null);
            Check(!safe.LevelFile.Bake.Enabled && !safe.LevelFile.AutomaticRoomReflections,"Invalid scenario did not fail closed.");
        }
        var selector=new PointShadowSelector();
        WorldPointLight left=new(new(-2,3,0),Vector3.One,1,10,CastShadows:true),right=left with{Position=new(2,3,0)};
        Check(selector.Select([left,right],new(-.1f,0,0),2)==0,"Initial shadow selection failed.");
        for(int i=0;i<20;i++)Check(selector.Select([right,left],new(.1f,0,0),2)==1,"Shadow slot oscillated across midpoint.");
        Check(selector.Select([right,left],new(6,0,0),2)==0,"Shadow did not transfer after margin.");
        Check(selector.Select([left],Vector3.Zero,2)==0 && selector.Select([],Vector3.Zero,2)==-1,"Disabled/removed shadow owner remained selected.");
        Console.WriteLine($"PASS: {checks} stability level/scenario/cache/undo/shadow selection checks.");
    }

    internal static void SwitchPixels(string root)
    {
        void Check(bool ok,string message) {if(!ok)throw new InvalidDataException(message);}
        using var host=new EngineHost(1280,720,"Switched lighting validation");
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var game=new Game.HL2GameModule(Path.Combine(root,"Game/Content/Levels",FileName));
        game.AssetStreamingEnabled=false;game.Initialize(host.Context);
        var editor=(LevelEditorController)PbrBaselineChecks.Field(game,"_editor");
        var world=(BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");
        var room=(ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");
        var bake=(LevelBakeSession)PbrBaselineChecks.Field(game,"_bakedLighting");
        var state=(LevelLightState)PbrBaselineChecks.Field(game,"_lightState");
        var camera=(Game.FpsCamera)PbrBaselineChecks.Field(game,"_camera");
        var lever=((List<Entity>)PbrBaselineChecks.Field(game,"_runtimeEntities")).Single(e=>e.Name=="Fixture Power Switch");
        var authored=StructuredData.WriteJson(editor.LevelFile);
        string folder=Path.Combine(AppContext.BaseDirectory,"qa","lighting-switch-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        Wait("on");var on=Frame("on");
        bake.Diagnostic=LightingDiagnostic.DiffuseIndirect;var indirect=Frame("bounce-only");bake.Diagnostic=LightingDiagnostic.Full;
        Check(Energy(indirect)>1000,"Baked diffuse produced no visible energy.");
        UseSwitch();Wait("off");var off=Frame("off");
        Check(!bake.Ready && bake.ActiveResidentBytes==0,"Lights-off state still applies the on-state lightmap.");
        Check(room.Ready && !room.Status.Contains("black fallback"),"Lights-off reflection state was not loaded.");
        Check(Energy(off)<Energy(on)*.1,"Fixture power off retained too much light.");
        // Exercise saved runtime state and rapid cancellation without rewriting authored defaults.
        state.Restore(StructuredData.ReadJson<List<LightStateOverride>>(StructuredData.WriteJson(state.Capture())));
        UseSwitch();Wait("on");var restored=Frame("restored");
        Compare(on,restored,"Switched on/off/on");
        Check(on.Zip(restored).All(p=>Enumerable.Range(0,3).All(c=>Math.Abs((int)((p.First>>(c*8))&255)-(int)((p.Second>>(c*8))&255))<=1)),"Restored on-state pixels changed.");
        for(int i=0;i<8;i++)
        {
            state.Toggle(editor.LevelFile,editor.LevelFile.Entities.Single(e=>e.Name=="Fixture Power Switch").Interaction!,out _);
            target.Frame(()=>game.RenderWorld(renderer),read:false);
        }
        state.Clear();Wait("on");
        Check(bake.ResidentBytes<40*1048576 && room.ResidentBytes<13*1048576 && room.RetainedScenarioProbes<=6,"Lighting state residency exceeded its two-state budget.");
        UseSwitch();Wait("off");
        for(int i=0;i<150;i++)target.Frame(()=>game.RenderWorld(renderer),read:false);
        UseSwitch();Wait("on");Compare(on,Frame("after-idle"),"Retained state after idle",true);
        editor.SetAuthoredLightingScenario("off");state.Clear();Wait("off");
        Check(room.RetainedScenarioProbes==0 && bake.ResidentBytes==0,"Authoring revision retained stale lighting resources.");
        Check(editor.Undo(),"Lighting authoring revision could not be undone.");Wait("on");
        Compare(on,Frame("after-undo"),"Authored state edit and undo",true);
        Check(StructuredData.WriteJson(editor.LevelFile).SequenceEqual(authored),"Switching modified authored defaults.");
        Console.WriteLine($"PASS: focused interaction, on/off caches, saved overrides, visible bounce, exact restore and rapid cancellation. {bake.ResidentBytes/1048576.0:F2} MiB bake GPU; {room.ResidentBytes/1048576.0:F2} MiB reflections. {folder}");
        void UseSwitch()
        {
            float yaw=camera.Yaw,pitch=camera.Pitch;
            var direction=Vector3.Normalize(lever.Transform.Position-camera.Position);
            camera.Yaw=MathF.Atan2(direction.X,direction.Z);camera.Pitch=MathF.Asin(direction.Y);
            try
            {
                bool used=(bool)typeof(Game.HL2GameModule).GetMethod("TryUseFocusedInteractable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(game,null)!;
                Check(used,"Fixture switch cannot be used from spawn.");
            }
            finally{camera.Yaw=yaw;camera.Pitch=pitch;}
        }
        void Wait(string id)
        {
            var timer=Stopwatch.StartNew();double worst=0;world.ViewCullingEnabled=false;
            do
            {
                long start=Stopwatch.GetTimestamp();target.Frame(()=>game.RenderWorld(renderer),read:false);
                worst=Math.Max(worst,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if(room.Ready && !room.Busy && room.ScenarioStatus.EndsWith(id,StringComparison.Ordinal) &&
                    (id=="off" || bake.Ready && bake.ReadyModels==editor.LevelFile.Entities.Count(e=>e.StaticLighting && !e.ExcludeFromDiffuseBake)))break;
                if(timer.Elapsed.TotalSeconds>120)throw new TimeoutException(room.Status+" / "+bake.Status+" / "+bake.LastBindingIssue);
                Thread.Sleep(5);
            }while(true);
            world.ViewCullingEnabled=true;
            Console.WriteLine($"State {id} ready in {timer.Elapsed.TotalMilliseconds:F0}ms; max render-call {worst:F1}ms including synchronous GPU atlas upload, not presented FPS.");
        }
        uint[] Frame(string name)
        {
            var pixels=target.Frame(()=>game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels,target.Width,target.Height,Path.Combine(folder,name+".png"));return pixels;
        }
        static double Energy(uint[] pixels)=>pixels.Sum(p=>(double)((p>>16)&255)+((p>>8)&255)+(p&255));
    }
    internal static void Diagnose(string root, string file = PointLightOnlyChecks.FileName)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        string folder = Path.Combine(AppContext.BaseDirectory,"qa","lighting-diagnosis-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var floor = GlbModelLoader.Load(Path.Combine(root,"Game/Content/Models/ViewModels/Basement_Corridor_B_Floor_4x4m.glb"));
        foreach (var part in floor.Parts)
        {
            using var stream = new MemoryStream(part.MetallicRoughnessPng!);
            using var bitmap = new Bitmap(stream);
            double sum = 0; int samples = 0;
            for (int y=0;y<bitmap.Height;y+=8) for(int x=0;x<bitmap.Width;x+=8) { sum += bitmap.GetPixel(x,y).B / 255.0; samples++; }
            Console.WriteLine($"Floor material: metal factor {part.MetallicFactor}, mapped mean metal {sum/samples*part.MetallicFactor:F3}, roughness factor {part.RoughnessFactor}");
        }
        using var host = new EngineHost(1280,720,"Lighting stability diagnosis");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(Path.Combine(root,"Game/Content/Levels",file));
        game.Initialize(host.Context);
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game,"_editor");
        var session = (ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");
        var bake = (LevelBakeSession)PbrBaselineChecks.Field(game,"_bakedLighting");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game,"_camera");
        var models = (IDictionary)PbrBaselineChecks.Field(game,"_weaponModelCache");
        PbrBaselineChecks.Set(game,"_flashlightOn",false);
        camera.Yaw = -MathF.PI/2; camera.Pitch = -.35f;
        foreach (float x in new[] {-2f,-6f,-10f})
        {
            camera.Position = new(x,1.7f,-10);
            Ready(); uint[] baseline = Frame($"x{x}-normal");
            game.AssetStreamingEnabled = false; Ready();
            Compare(baseline,Frame($"x{x}-assets-resident"),$"x{x} asset streaming disabled",file==FileName);
            var settings = editor.LevelFile.ReflectionStreaming; editor.LevelFile.ReflectionStreaming = null; Ready();
            Compare(baseline,Frame($"x{x}-all-reflections"),$"x{x} all six reflections resident",file==FileName);
            session.PreviewEnabled = false;
            Compare(baseline,Frame($"x{x}-no-reflections"),$"x{x} reflections disabled"); session.PreviewEnabled = true;
            editor.LevelFile.EnableShadows = false;
            Compare(baseline,Frame($"x{x}-no-shadows"),$"x{x} shadows disabled"); editor.LevelFile.EnableShadows = true;
            PbrBaselineChecks.Set(game,"_flashlightOn",true); Frame($"x{x}-torch");
            PbrBaselineChecks.Set(game,"_flashlightOn",false);
            editor.LevelFile.ReflectionStreaming = settings; game.AssetStreamingEnabled = true;
        }
        Console.WriteLine("Diagnosis images: "+folder);
        uint[] Frame(string name)
        {
            uint[] pixels=target.Frame(()=>game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels,target.Width,target.Height,Path.Combine(folder,name+".png"));
            return pixels;
        }
        void Ready()
        {
            var timer=Stopwatch.StartNew();
            world.ViewCullingEnabled=false;
            do
            {
                target.Frame(()=>game.RenderWorld(renderer),read:false);
                if(session.Ready && !session.Busy && models.Count>0 && models.Values.Cast<object>().All(e=>PbrBaselineChecks.Field(e,"Model")!=null) &&
                    (!editor.LevelFile.Bake.Enabled || bake.Ready && bake.ReadyModels==editor.LevelFile.Entities.Count(e=>e.StaticLighting && !e.ExcludeFromDiffuseBake))) break;
                if(timer.Elapsed.TotalSeconds>90)throw new TimeoutException(session.Status);
                Thread.Sleep(5);
            }while(true);
            world.ViewCullingEnabled=true;
        }
    }
    private static void Compare(uint[] a,uint[] b,string label,bool requireSame=false)
    {
        int changed=0,max=0;
        for(int i=0;i<a.Length;i++)
        {
            int error=0; for(int shift=0;shift<24;shift+=8)error=Math.Max(error,Math.Abs((int)((a[i]>>shift)&255)-(int)((b[i]>>shift)&255)));
            if(error>1)changed++; max=Math.Max(max,error);
        }
        Console.WriteLine($"{label}: {changed} pixels >1/255, max {max}/255");
        if(requireSame && changed!=0)throw new InvalidDataException(label+" changed stable pixels.");
    }
}

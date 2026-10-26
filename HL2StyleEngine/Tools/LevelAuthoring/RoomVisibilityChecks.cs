using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;

internal static class RoomVisibilityChecks
{
    public const string FileName = "sixRoomVisibilityTest.json";
    internal static void Build(string root)
    {
        string folder = Path.Combine(root,"Game/Content/Levels"), path = Path.Combine(folder, FileName);
        if (File.Exists(path)) throw new IOException("Preserve existing visibility test edits.");
        var level = LevelIO.Load(Path.Combine(folder,ShadowStabilityChecks.FileName));
        var settings = new RoomVisibilitySettings { Enabled = true };
        foreach (var room in SixRoomTest.Rooms)
            settings.Rooms.Add(new() { Name = room.Name, Center = new(room.Center.X,room.Height/2,room.Center.Y),
                Size = new(room.Width+2,room.Height+2,room.Depth+2),
                ReflectionProbeId = level.Entities.Single(e => e.Name == room.Name+" Reflection").Id });
        for (int i=0; i<SixRoomTest.Connections.Length; i++)
        {
            var c = SixRoomTest.Connections[i]; bool x = i < 4;
            settings.Portals.Add(new() { Name = c.Name, RoomA = settings.Rooms[0].Id, RoomB = settings.Rooms[i+1].Id,
                Center = new(c.Center.X,c.Height/2,c.Center.Y), Size = x ? new(.2f,c.Height+.4f,c.Depth+.4f) : new(c.Width+.4f,c.Height+.4f,.2f), Axis = x ? 0 : 2 });
        }
        Check(new RoomVisibilityGraph(settings).Error.Length == 0,"Invalid generated graph.");
        level.RoomVisibility = settings;
        LevelIO.Save(path,level);
        // Visibility metadata cannot change lighting energy. Validate cache identity separately below.
        foreach (string cache in Directory.GetFiles(folder,"sixRoomShadowTest.lighting-*"))
            File.Copy(cache,Path.Combine(folder,Path.GetFileName(cache).Replace("sixRoomShadowTest.","sixRoomVisibilityTest.",StringComparison.Ordinal)),false);
        Console.WriteLine("Created "+path+" with unchanged geometry and copied lighting caches.");
    }

    internal static void Validate(string root)
    {
        int checks = 0;
        void Assert(bool pass,string why) { Check(pass,why); checks++; }
        var level = LevelIO.Load(Path.Combine(root,"Game/Content/Levels",FileName));
        var original = LevelIO.Load(Path.Combine(root,"Game/Content/Levels",ShadowStabilityChecks.FileName));
        var copy = Clone(level); copy.RoomVisibility = null;
        Assert(StructuredData.WriteJson(copy).SequenceEqual(StructuredData.WriteJson(original)),"Visibility fixture changed gameplay/geometry.");
        var settings = level.RoomVisibility!;
        Assert(settings.Rooms.Count == 6 && settings.Portals.Count == 5,"Expected six rooms/five connections.");
        var graph = new RoomVisibilityGraph(settings);
        Assert(graph.Error.Length==0,"Fixture graph invalid: "+graph.Error);
        for(int i=0;i<settings.Rooms.Count;i++)
        {
            Vector3 eye = settings.Rooms[i].Center;
            for(int angle=0;angle<16;angle++)
            {
                var direction = new Vector3(MathF.Sin(angle*MathF.PI/8),0,MathF.Cos(angle*MathF.PI/8));
                graph.Update(eye,View(eye,direction));
                Assert(graph.Active && graph.IsVisible(eye-new Vector3(.1f),eye+new Vector3(.1f)),"Active room culled.");
                Assert(graph.PrefetchProbeIds.Contains(settings.Rooms[i].ReflectionProbeId),"Current probe not prepared.");
            }
        }
        graph.Update(new(100,2,100),View(new(100,2,100),Vector3.UnitZ));
        Assert(!graph.Active && graph.IsVisible(new(-30,0,-15),new(-29,1,-14)),"Outside camera must fail open.");
        graph.Update(new(-24,2,-11),View(new(-24,2,-11),-Vector3.UnitX));
        Assert(graph.VisibleCount==1 && !graph.IsVisible(new(20,0,4),new(21,1,5)),"Opposite room not rejected.");
        Assert(graph.IsVisible(new(-100),new(100)),"Spanning bounds were culled.");
        Assert(graph.IsVisible(new(float.NaN),Vector3.One),"Invalid bounds were culled.");
        Assert(graph.PrefetchProbeIds.Contains(settings.Rooms[0].ReflectionProbeId),"Connected hall not prefetched behind camera.");
        foreach(var portal in settings.Portals)
        {
            Vector3 eye=portal.Center, axis=portal.Axis==0?Vector3.UnitX:Vector3.UnitZ;
            for(int step=-10;step<=10;step++)
            {
                Vector3 p=eye+axis*step*.01f; graph.Update(p,View(p,axis));
                Assert(graph.Active && graph.VisibleCount>=2,"Doorway crossing lost a neighbour.");
            }
        }
        var broken=Clone(settings); broken.Portals[0].RoomB="missing";
        var fallback=new RoomVisibilityGraph(broken); fallback.Update(Vector3.Zero,Matrix4x4.Identity);
        Assert(fallback.Error.Length>0 && fallback.IsVisible(Vector3.Zero,Vector3.One),"Missing connection did not fail open.");
        broken=Clone(settings); broken.Rooms[1].Id=broken.Rooms[0].Id;
        Assert(new RoomVisibilityGraph(broken).Error.Length>0,"Duplicate IDs accepted.");
        var editor=new LevelEditorController(); editor.LoadFromMemory("rooms.json",Clone(original));
        editor.SetRoomVisibility(settings); Assert(editor.Dirty && editor.LevelFile.RoomVisibility?.Rooms.Count==6,"Editor assignment failed.");
        Assert(editor.Undo() && editor.LevelFile.RoomVisibility==null,"Room undo failed.");
        Assert(editor.Redo() && editor.LevelFile.RoomVisibility?.Portals.Count==5,"Room redo failed.");
        Assert(Clone(editor.LevelFile).RoomVisibility?.Portals[0].RoomA==settings.Portals[0].RoomA,"Room IDs failed JSON roundtrip.");
        string roundtrip=Path.Combine(AppContext.BaseDirectory,"qa","room-roundtrip-"+Guid.NewGuid().ToString("N")+".json");
        LevelIO.Save(roundtrip,editor.LevelFile);
        Assert(LevelIO.Load(roundtrip).RoomVisibility?.Rooms[0].Id==settings.Rooms[0].Id,"Room disk load failed.");
        Assert(File.Exists(StructuredData.CachePath(roundtrip)) && LevelIO.Load(roundtrip).RoomVisibility?.Portals[0].RoomB==settings.Portals[0].RoomB,"Room DOX cache roundtrip failed.");
        var invalid=Clone(settings); invalid.Rooms=null!;
        Assert(new RoomVisibilityGraph(invalid).Error.Length>0,"Null rooms not rejected.");
        graph.Update(Vector3.Zero,new Matrix4x4());
        Assert(!graph.Active,"Invalid frustum did not fail open.");
        var cyclic=new RoomVisibilitySettings { Enabled=true };
        for(int i=0;i<9;i++)cyclic.Rooms.Add(new() { Center=Vector3.Zero, Size=new(8,8,8) });
        for(int i=0;i<9;i++)for(int j=i+1;j<9;j++)cyclic.Portals.Add(new(){RoomA=cyclic.Rooms[i].Id,RoomB=cyclic.Rooms[j].Id,Center=Vector3.Zero});
        var cycleGraph=new RoomVisibilityGraph(cyclic); cycleGraph.Update(Vector3.Zero,View(Vector3.Zero,Vector3.UnitZ));
        Assert(!cycleGraph.Active && cycleGraph.IsVisible(Vector3.Zero,Vector3.One),"Traversal cap did not fail open.");
        var layout=ReflectionRoomLayout.Create(editor);
        var residency=new ReflectionResidency();
        var budget=new ReflectionStreamingSettings {MaxResidentProbes=3,PrefetchDistance=0,RetainSeconds=0};
        var hintIds=layout.Rooms.Reverse().Select(r=>r.Id).Append("missing").ToArray();
        foreach(var door in layout.Doors)
        {
            int capture=Enumerable.Range(0,layout.Rooms.Length).First(i=>i!=door.A && i!=door.B);
            int[] wanted=residency.Select(layout,budget,(door.Min+door.Max)/2,0,new HashSet<int>(),capture,hintIds);
            Assert(wanted.Length==3 && wanted.Contains(door.A) && wanted.Contains(door.B) && wanted.Contains(capture),"Hints displaced active doorway/capture pins or exceeded budget.");
        }
        string Resolve(string p)=>Path.Combine(root,"Game",p.Replace('/',Path.DirectorySeparatorChar));
        foreach(var scenario in level.LightingScenarios!)
        {
            var next=LightingScenarioResolver.CreateView(level,scenario);
            var previous=LightingScenarioResolver.CreateView(original,scenario);
            Assert(BakeSnapshot.Capture(next,Resolve).Key()==BakeSnapshot.Capture(previous,Resolve).Key(),"Room metadata invalidated diffuse bake.");
            string newPath=LightingScenarioResolver.PathFor(Path.Combine(root,"Game/Content/Levels",FileName),scenario.Id);
            string oldPath=LightingScenarioResolver.PathFor(Path.Combine(root,"Game/Content/Levels",ShadowStabilityChecks.FileName),scenario.Id);
            foreach(var probe in next.Entities.Where(e=>e.ReflectionProbe!=null))
            {
                string key=ReflectionSnapshot.Create(next,newPath,probe.Id,Resolve).Key();
                Assert(key==ReflectionSnapshot.Create(previous,oldPath,probe.Id,Resolve).Key(),"Room metadata changed reflection content.");
                Assert(ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(newPath,probe.Id),key)!=null,"Copied capture is invalid.");
            }
        }
        Console.WriteLine($"PASS: {checks} room graph, boundary, prefetch, editor undo/JSON and cache compatibility checks.");
    }

    internal static void Pixels(string root, bool submission = false)
    {
        using var host=new EngineHost(1280,720,"Room visibility references");
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var game=new Game.HL2GameModule(Path.Combine(root,"Game/Content/Levels",FileName));
        game.AssetStreamingEnabled=false; game.Initialize(host.Context);
        var camera=(Game.FpsCamera)PbrBaselineChecks.Field(game,"_camera");
        var world=(BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");
        var editor=(LevelEditorController)PbrBaselineChecks.Field(game,"_editor");
        var rooms=(ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");
        var bake=(LevelBakeSession)PbrBaselineChecks.Field(game,"_bakedLighting");
        var models=(IDictionary)PbrBaselineChecks.Field(game,"_weaponModelCache");
        var entities=(List<Entity>)PbrBaselineChecks.Field(game,"_runtimeEntities");
        string folder=Path.Combine(AppContext.BaseDirectory,"qa",(submission?"scene-submission-":"room-visibility-")+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        byte[] authored=StructuredData.WriteJson(editor.LevelFile);
        var ids=entities.Select(e=>e.Id).ToArray();
        world.ViewCullingEnabled=false; game.RoomVisibilityEnabled=false;
        var wait=Stopwatch.StartNew();
        while(true)
        {
            target.Frame(()=>game.RenderWorld(renderer),read:false);
            if(models.Count>0 && models.Values.Cast<object>().All(e=>PbrBaselineChecks.Field(e,"Model")!=null) && rooms.Ready && rooms.AlternateStateReady && bake.ReadyModels==532)break;
            if(wait.Elapsed.TotalSeconds>90)throw new TimeoutException(bake.Status+" / "+rooms.Status); Thread.Sleep(5);
        }
        int cases=0, saved=0;
        foreach(bool torch in new[]{false,true})
        {
            PbrBaselineChecks.Set(game,"_flashlightOn",torch);
            for(int room=0;room<6;room++) for(int angle=0;angle<8;angle++)
            {
                camera.Position=editor.LevelFile.RoomVisibility!.Rooms[room].Center;
                camera.Yaw=angle*MathF.PI/4; camera.Pitch=-.12f;
                Compare($"room-{room}-{angle}-torch-{torch}");
            }
        }
        var shutters=entities.Where(e=>e.Name.EndsWith("_Shutter",StringComparison.Ordinal)).ToArray();
        foreach(var shutter in shutters)
        {
            var initial=shutter.Transform.Position;
            for(int step=0;step<=4;step++)
            {
                // Render poses only; gameplay/physics motion is exercised by the host stress test.
                shutter.Transform.Position=initial+Vector3.UnitY*step*.84f;
                camera.Position=initial+Vector3.Normalize(new(initial.X,0,initial.Z))*-2+Vector3.UnitY*.2f;
                Aim(initial+Vector3.UnitY*.1f); Compare("shutter-"+shutter.Name+"-"+step);
            }
            shutter.Transform.Position=initial;
        }
        foreach(var portal in editor.LevelFile.RoomVisibility!.Portals)
        {
            Vector3 axis=portal.Axis==0?Vector3.UnitX:Vector3.UnitZ;
            foreach(float offset in new[]{-.3f,-.05f,0,.05f,.3f})
            {camera.Position=(Vector3)portal.Center+axis*offset;Aim(camera.Position+axis);Compare("threshold-"+cases);}
        }
        var held=entities.First(e=>e.CanPickUp && e.Render.Enabled && !string.IsNullOrWhiteSpace(e.Render.ModelAssetPath));
        bool wasHeld=held.IsHeld; Vector3 heldPosition=held.Transform.Position;
        held.IsHeld=true;
        foreach(var portal in editor.LevelFile.RoomVisibility.Portals)
        {
            Vector3 axis=portal.Axis==0?Vector3.UnitX:Vector3.UnitZ;
            camera.Position=(Vector3)portal.Center-axis;
            held.Transform.Position=(Vector3)portal.Center+axis*.3f;
            Aim(held.Transform.Position); Compare("held-crossing-"+cases);
        }
        held.IsHeld=wasHeld; held.Transform.Position=heldPosition;
        Check(submission || saved>0,"No draw reduction observed.");
        Check(entities.Select(e=>e.Id).SequenceEqual(ids),"Visibility changed entity ownership.");
        Check(StructuredData.WriteJson(editor.LevelFile).SequenceEqual(authored),"Checks changed authored scene.");
        PbrBaselineChecks.Set(game,"_editorEnabled",true);
        camera.Position=new(0,2,-4); Aim(new(0,2,17));
        var withoutBounds=target.Frame(()=>game.RenderWorld(renderer));
        editor.ShowRoomBounds=true;
        var withBounds=target.Frame(()=>game.RenderWorld(renderer));
        Check(withBounds.Zip(withoutBounds).Count(p=>p.First!=p.Second)>100,"Room/doorway outlines were not drawn in F2.");
        RenderPipelineChecks.SavePng(withBounds,target.Width,target.Height,Path.Combine(folder,"f2-room-bounds.png"));
        Console.WriteLine($"PASS: {cases} {(submission?"submission":"all-visible")} pixel references, {saved} total draws avoided; entities and source unchanged. {folder}");

        void Aim(Vector3 to){var d=Vector3.Normalize(to-camera.Position);camera.Yaw=MathF.Atan2(d.X,d.Z);camera.Pitch=MathF.Asin(d.Y);}
        void Compare(string name)
        {
            game.RenderPreparationEnabled=true; world.DrawGroupingEnabled=true;
            game.RoomVisibilityEnabled=true; var actual=target.Frame(()=>game.RenderWorld(renderer)); int draws=submission?world.MainDrawCalls:(int)world.ObjectDrawCount, culled=game.RoomCulledEntities;
            var shadows=world.ShadowLightIds.ToArray();
            game.RoomVisibilityEnabled=submission; game.RenderPreparationEnabled=!submission; world.DrawGroupingEnabled=!submission;
            var reference=target.Frame(()=>game.RenderWorld(renderer)); int referenceDraws=submission?world.MainDrawCalls:(int)world.ObjectDrawCount;
            int max=actual.Zip(reference).Max(p=>Enumerable.Range(0,3).Max(c=>Math.Abs((int)((p.First>>(8*c))&255)-(int)((p.Second>>(8*c))&255))));
            if(cases<2 || max>1){RenderPipelineChecks.SavePng(actual,target.Width,target.Height,Path.Combine(folder,name+".png"));RenderPipelineChecks.SavePng(reference,target.Width,target.Height,Path.Combine(folder,name+"-reference.png"));}
            Check(max<=1,name+" differs: "+max+"/255"); Check(shadows.SequenceEqual(world.ShadowLightIds),"Camera visibility changed light owners.");
            saved+=referenceDraws-draws;cases++; Console.WriteLine($"{name}: max {max}/255; {culled} entities skipped; draws {draws}/{referenceDraws}");
        }
    }
    private static Matrix4x4 View(Vector3 p,Vector3 direction)=>Matrix4x4.CreateLookAt(p,p+direction,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,16f/9,.05f,500);
    private static T Clone<T>(T value)=>StructuredData.ReadJson<T>(StructuredData.WriteJson(value));
    private static void Check(bool pass,string why){if(!pass)throw new InvalidDataException(why);}
}

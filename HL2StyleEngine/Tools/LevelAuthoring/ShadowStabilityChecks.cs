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

internal static class ShadowStabilityChecks
{
    public const string FileName = "sixRoomShadowTest.json";
    internal static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path))throw new IOException("Shadow test already exists; preserve author edits.");
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",LightingStabilityChecks.FileName));
        level.PointShadowLimit=16;level.OffsetFlashlight=true;level.PrewarmLightingStates=true;
        LevelIO.Save(path,level);
        Console.WriteLine("Created "+path+". Cook both states before launching.");
    }

    internal static void Validate(string root)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new InvalidDataException(message);checks++;}
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",FileName));
        Check(level.PointShadowLimit==16 && level.OffsetFlashlight && level.PrewarmLightingStates,"Shadow fixture flags missing.");
        var original=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",LightingStabilityChecks.FileName));
        var copy=StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level));
        copy.PointShadowLimit=original.PointShadowLimit;copy.OffsetFlashlight=original.OffsetFlashlight;copy.PrewarmLightingStates=original.PrewarmLightingStates;
        Check(StructuredData.WriteJson(copy).SequenceEqual(StructuredData.WriteJson(original)),"Existing geometry/gameplay changed.");
        var pool=new PointShadowPool();
        var lights=Enumerable.Range(0,5).Select(i=>new WorldPointLight(new(i*2,3,0),Vector3.One,1,12,CastShadows:true,Id:i.ToString())).ToArray();
        var first=pool.Select(lights,Vector3.Zero,4,2).ToArray();
        Check(first.Take(4).SequenceEqual(new[]{0,1,2,3}) && first.Skip(4).All(i=>i<0),"Initial four lights were not selected.");
        for(int i=0;i<20;i++)
        {
            var reverse=lights.Reverse().ToArray();
            var selected=pool.Select(reverse,new(4.1f,0,0),4,2);
            Check(selected.Where(index=>index>=0).Select(index=>reverse[index].Id).SequenceEqual(new[]{"0","1","2","3"}),"Small boundary motion changed owners/slots.");
        }
        Check(pool.Select(lights,new(20,0,0),4,2).Contains(4),"Large movement never released distant owner.");
        lights[0]=lights[0] with { Priority=100 };
        Check(pool.Select(lights,new(20,0,0),4,2).Contains(0),"Hysteresis overrode authored priority.");
        lights[0]=lights[0] with { Position=new(22,3,0) };
        Check(pool.Select(lights,new(20,0,0),4,2).Contains(0),"Moving light lost identity.");
        Check(pool.Select([],Vector3.Zero,4,2).All(i=>i<0),"Removed owners remained assigned.");
        Check(pool.Select(lights,Vector3.Zero,2,2).Count(i=>i>=0)==2,"Lower quality retained extra slots.");
        Check(pool.Select(lights.Select(l=>l with { SpotAngleDegrees=55,Direction=Vector3.UnitZ }).ToArray(),Vector3.Zero,4,2).All(i=>i<0),"Spotlight consumed a point slot.");
        var fixtures=Enumerable.Range(0,14).Select(i=>lights[0] with {Id="fixture-"+i,Position=new(i*3,3,0)}).ToArray();
        var owners=pool.Select(fixtures,Vector3.Zero,16,2).Select(i=>i<0?null:fixtures[i].Id).ToArray();
        for(int step=0;step<40;step++)
        {
            var ordered=fixtures.Reverse().ToArray();
            Check(pool.Select(ordered,new(step*2,1,10),16,2).Select(i=>i<0?null:ordered[i].Id).SequenceEqual(owners),"Visible fixture coverage changed on camera route.");
        }
        Console.WriteLine($"PASS: {checks} shadow pool/fixture compatibility checks.");
    }

    internal static void Pixels(string root)
    {
        void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
        using var host=new EngineHost(1280,720,"Shadow stability validation");
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var game=new Game.HL2GameModule(Path.Combine(root,"Game/Content/Levels",FileName));
        game.AssetStreamingEnabled=false;game.Initialize(host.Context);
        var editor=(LevelEditorController)PbrBaselineChecks.Field(game,"_editor");
        var world=(BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");
        var rooms=(ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");
        var bake=(LevelBakeSession)PbrBaselineChecks.Field(game,"_bakedLighting");
        var camera=(Game.FpsCamera)PbrBaselineChecks.Field(game,"_camera");
        var entities=(List<Entity>)PbrBaselineChecks.Field(game,"_runtimeEntities");
        var models=(IDictionary)PbrBaselineChecks.Field(game,"_weaponModelCache");
        string folder=Path.Combine(AppContext.BaseDirectory,"qa","shadow-stability-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        world.ViewCullingEnabled=false;
        var authored=StructuredData.WriteJson(editor.LevelFile);
        var timer=Stopwatch.StartNew();
        Wait(true);
        Console.WriteLine($"Both reflection banks ready: {timer.Elapsed.TotalMilliseconds:F0}ms through render/readback warmup, not cold first-playable; {rooms.ResidentBytes/1048576.0:F2} MiB.");
        var initial=Frame("on");
        Switch();Frame("off-first-frame");
        Check(rooms.Ready && !rooms.Busy && rooms.AlternateStateReady && bake.ActiveResidentBytes==0,"First switch was not prewarmed or retained bright diffuse lighting.");
        Switch();var restored=Frame("on-first-frame");
        int max=initial.Zip(restored).Max(p=>Enumerable.Range(0,3).Max(c=>Math.Abs((int)((p.First>>(c*8))&255)-(int)((p.Second>>(c*8))&255))));
        Console.WriteLine($"Restore: {rooms.Status}; alternate={rooms.AlternateStateReady}; {bake.Status}; bindings={bake.ReadyModels}; changed={initial.Zip(restored).Count(p=>p.First!=p.Second)}, max={max}/255");
        Check(rooms.Ready && !rooms.Busy && max<=1,"Immediate restored state changed pixels.");
        Check(rooms.ResidentBytes<13*1048576 && rooms.CaptureGpuBytes==0,"Prewarm exceeded two-bank residency or captured at runtime.");
        string crateLight=editor.LevelFile.Entities.Single(e=>e.Name=="Hall Side 9 -10 Point Light").Id;
        for(int step=0;step<3;step++)
        {
            camera.Position=new(6.7f-step*3.35f,1.7f,-14);
            Aim(new(6.7f,.54f,-11));
            var withShadow=Frame("hall-crates-"+step+"-shadow");
            Check(world.ShadowLightIds.Contains(crateLight),"Hall crate light lost its shadow during approach/retreat.");
            editor.LevelFile.EnableShadows=false;var without=Frame("hall-crates-"+step+"-no-shadow-reference");editor.LevelFile.EnableShadows=true;
            int pixels=withShadow.Zip(without).Count(p=>Energy(p.Second)>Energy(p.First)+15);
            Check(pixels>100,$"Hall crate shadows disappeared at distance {step}: {pixels} pixels.");
            Console.WriteLine($"Hall crate view {step}: light retained, {pixels} shadow pixels.");
        }
        string pressureLight=editor.LevelFile.Entities.Single(e=>e.Name=="Hall Side 9 10 Point Light").Id;
        var pressureCrate=entities.Single(e=>e.Name=="SRPuzzle_UtilityWeightCrate");
        var position=pressureCrate.Transform.Position;
        foreach(float distance in new[]{18f,12f,8f,4f,2f,4f,8f,12f,18f})
        {
            camera.Position=position+new Vector3(-3.5f,1.16f,-distance);Aim(position);
            var shadow=Frame("pressure-crate-distance-"+distance);
            Check(world.ShadowLightIds.Contains(pressureLight) && world.ShadowPointLightCount==14,"Pressure-plate crate fixture lost its shadow during approach/retreat.");
            world.ShadowCachingEnabled=world.ShadowCullingEnabled=false;
            var reference=Frame("pressure-crate-distance-"+distance+"-reference");
            world.ShadowCachingEnabled=world.ShadowCullingEnabled=true;
            SamePixels(shadow,reference,"Pressure-plate distance "+distance);
            Console.WriteLine($"Pressure crate {distance}m: all 14 fixture owners retained, pixels match uncached reference.");
        }
        // A held/nearby crate should occlude the actual production flashlight, not just receive illumination.
        Switch();PbrBaselineChecks.Set(game,"_flashlightOn",true);
        var crate=entities.Where(e=>e.Damageable && !e.IsBroken).MinBy(e=>Vector3.DistanceSquared(e.Transform.Position,camera.Position))!;
        camera.Position=crate.Transform.Position+new Vector3(0,1.2f,-3.5f);
        Aim(crate.Transform.Position);
        var flashlight=Torch();
        Check(Vector3.Distance(flashlight.Position,camera.Position) is > .04f and < .05f && flashlight.CastShadows,"Near-camera flashlight offset/shadow flag missing.");
        var shadowed=Frame("flashlight-shadow");
        Check(world.ShadowSpotLightCount==1 && world.ShadowPointLightCount==0,"Flashlight did not keep an independent shadow when fixtures were off.");
        editor.LevelFile.EnableShadows=false;var unshadowed=Frame("flashlight-no-shadow-reference");editor.LevelFile.EnableShadows=true;
        int darker=shadowed.Zip(unshadowed).Count(p=>Energy(p.Second)>Energy(p.First)+15);
        Check(darker>100,$"Production flashlight shadow not visible: {darker} pixels.");
        for(int step=0;step<8;step++)
        {
            float angle=step*MathF.PI/4;
            camera.Position=position+new Vector3(MathF.Sin(angle)*2.5f,1.16f,MathF.Cos(angle)*2.5f);Aim(position);
            var orbit=Frame("pressure-crate-flashlight-orbit-"+step);
            world.ShadowCachingEnabled=world.ShadowCullingEnabled=false;
            var reference=Frame("pressure-crate-flashlight-orbit-"+step+"-reference");
            world.ShadowCachingEnabled=world.ShadowCullingEnabled=true;
            SamePixels(orbit,reference,"Flashlight orbit "+step);
        }
        var saved=entities.ToArray();entities.Clear();camera.Position=Vector3.Zero;camera.Yaw=camera.Pitch=0;
        try
        {
            var wall=new Entity("wall-test",EntityTypes.RigidBody,"Wall");
            wall.Transform.Position=new(-.02f,0,0);wall.Collider.Shape=RuntimeShapeKind.Box;wall.Collider.Size=new(.004f,2,2);
            wall.Render.Size=new(.001f);entities.Add(wall);
            Check(Vector3.Distance(Torch().Position,camera.Position)<.021f,"Near-camera light crossed a collider larger than its render bounds.");
            wall.Transform.Position=Vector3.Zero;
            Check(Torch().Position==camera.Position,"Eye inside collider did not clamp flashlight to eye.");
            entities.Clear();
            Check(Math.Abs(Vector3.Distance(Torch().Position,camera.Position)-MathF.Sqrt(.002f))<.00001f,"Free camera-relative placement did not recover.");
            editor.LevelFile.OffsetFlashlight=false;
            Check(Torch().Position==camera.Position && Torch().Direction==camera.Forward,"Legacy eye light changed.");
            editor.LevelFile.OffsetFlashlight=true;
        }
        finally{entities.Clear();entities.AddRange(saved);}
        var state=(LevelLightState)PbrBaselineChecks.Field(game,"_lightState");
        state.Clear();editor.SetAuthoredLightingScenario("off");
        target.Frame(()=>game.RenderWorld(renderer),read:false);
        Check(!rooms.AlternateStateReady,"Authored revision reused an unvalidated alternate bank.");
        Wait(false);
        Check(bake.ResidentBytes==0 && rooms.ResidentBytes<13*1048576,"Authored edit retained stale bake or extra reflection banks.");
        Check(editor.Undo(),"Lighting edit undo failed.");Wait(true);
        Check(StructuredData.WriteJson(editor.LevelFile).SequenceEqual(authored),"Runtime tests changed authored defaults.");
        Console.WriteLine($"PASS: prewarm/edit/undo, on/off/on restore within 1/255, three hall views, nine pressure-crate approach/retreat views and eight flashlight orbit views within 1/255 of uncached/unculled shadows, wall-safe near-camera light; real flashlight shadow affects {darker} pixels. {folder}");

        void Wait(bool needBake)
        {
            var watch=Stopwatch.StartNew();
            while(true)
            {
                target.Frame(()=>game.RenderWorld(renderer),read:false);
                bool ready=models.Count>0 && rooms.Ready && rooms.AlternateStateReady && rooms.PendingCpuBytes==0 &&
                    (!needBake || bake.Ready && bake.ReadyModels==editor.LevelFile.Entities.Count(e=>e.StaticLighting && !e.ExcludeFromDiffuseBake));
                foreach(object entry in models.Values)
                {
                    Check(!(bool)PbrBaselineChecks.Field(entry,"Failed"),"Model failed during shadow validation.");
                    ready &= PbrBaselineChecks.Field(entry,"Model")!=null;
                }
                if(ready)break;
                if(watch.Elapsed.TotalSeconds>90)throw new TimeoutException(rooms.Status+" / "+bake.Status);
                Thread.Sleep(5);
            }
        }
        void SamePixels(uint[] actual,uint[] reference,string name)
        {
            int difference=actual.Zip(reference).Max(p=>Enumerable.Range(0,3).Max(c=>Math.Abs((int)((p.First>>(c*8))&255)-(int)((p.Second>>(c*8))&255))));
            Console.WriteLine($"{name}: maximum reference difference {difference}/255.");
            Check(difference<=1,name+" differs from uncached, unculled shadows.");
        }
        WorldPointLight Torch()=>(WorldPointLight)typeof(Game.HL2GameModule).GetMethod("CreateFlashlight",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,null)!;
        void Aim(Vector3 point)
        {
            var direction=Vector3.Normalize(point-camera.Position);
            camera.Yaw=MathF.Atan2(direction.X,direction.Z);camera.Pitch=MathF.Asin(direction.Y);
        }
        void Switch()
        {
            var lever=entities.Single(e=>e.Name=="Fixture Power Switch");
            typeof(Game.HL2GameModule).GetMethod("UseLightSwitch",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game,[lever]);
        }
        uint[] Frame(string name)
        {
            var pixels=target.Frame(()=>game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels,target.Width,target.Height,Path.Combine(folder,name+".png"));return pixels;
        }
        static int Energy(uint p)=>(int)((p&255)+((p>>8)&255)+((p>>16)&255));
    }
}

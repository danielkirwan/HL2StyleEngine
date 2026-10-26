using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class RenderStressTest
{
    internal const string FileName = "renderStress12Room.json";
    private const string Models = "Content/Models/ViewModels/";
    internal static Vector3 Center(int room) => new(room % 4 * 22, 0, room / 4 * 22);
    private static string Id(string name) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("renderStress12/"+name))).ToLowerInvariant();

    internal static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path))throw new IOException("Preserve existing stress-level edits.");
        var level=new LevelFile { UsePointLights=true, LightingModel=SurfaceLightingModel.Pbr, AmbientLight=.025f, DirectionalLight=0,
            EnableShadows=true, PointShadowLimit=16, OffsetFlashlight=true, AutomaticRoomReflections=true,
            ReflectionStreaming=new(){MaxResidentProbes=6}, RoomVisibility=new(){Enabled=true},
            Streaming=new(){ZoneSize=22,PreloadNeighbours=1,BudgetMiB=256,RetainSeconds=5} };
        LevelEntityDef Add(string name,Vector3 position,Vector3 size,string mesh="",bool solid=false,float yaw=0)
        {
            var entity=new LevelEntityDef {Id=Id(name),Name=name,Type=solid?EntityTypes.RigidBody:EntityTypes.Prop,
                LocalPosition=position,Size=size,LocalRotationEulerDeg=new(0,yaw,0),Color=mesh.Length>0?Vector4.One:Vector4.Zero,
                MeshPath=mesh.Length>0?Models+mesh+".glb":"",Shape="Box",MotionType=MotionType.Static,Mass=0,Restitution=0,Friction=1,
                StaticLighting=mesh.Length>0,ExcludeFromDiffuseBake=true};
            level.Entities.Add(entity);return entity;
        }
        string[] names=["Reception","Freight","Workshop","Power","Medical","Archive","Dispatch","Service","Storage","Inspection","BreakRoom","Loading"];
        string[] floors=["B","D","E","F"];
        string[] props=["Console 1","Arcade_Machine_1","Electrical Panel","Utility_Pipes_A","Console 2","Arcade_Machine_2",
            "Laptop Opened","Utility_Pipes_B","Arcade_Machine_3","Utility_Pipes_C","Arcade_Machine_4","Utility_Pipes_D"];
        for(int i=0;i<12;i++)
        {
            Vector3 c=Center(i); string name=names[i];
            Add(name+" Floor collider",c+new Vector3(0,-.2f,0),new(20,.4f,20),solid:true);
            Add(name+" Ceiling collider",c+new Vector3(0,4.2f,0),new(20,.4f,20),solid:true);
            // Retain author order, but author repeated tiles together to allow compatible instancing.
            for(int surface=0;surface<2;surface++) for(int x=0;x<5;x++)for(int z=0;z<5;z++)
            {
                bool floor=surface==0, relief=floors[i%4]=="B";
                Add($"{name} Tile {surface}-{x}-{z}",c+new Vector3(-8+x*4,floor?(relief?-.06f:0):4.15f,-8+z*4),
                    new(4,floor?(relief?.12f:.001f):.3f,4),floor?$"Basement_Corridor_{floors[i%4]}_Floor_4x4m":"Basement_Corridor_A_Ceiling_4x4m");
            }
            Boundary(i,0,i%4>0); Boundary(i,1,i%4<3); Boundary(i,2,i/4>0); Boundary(i,3,i/4<2);
            level.RoomVisibility.Rooms.Add(new(){Id=Id(name+" volume"),Name=name,Center=c+new Vector3(0,2,0),Size=new(22,6,22),
                ReflectionProbeId=Id("Reflection "+(i/4*2+i%4/2))});
            level.Entities.AddRange(PointLightOnlyChecks.Fixture(name,c+new Vector3(0,3.8f,0),
                i%3==0?new(1,.85f,.65f):i%3==1?new(.7f,.85f,1):new(.8f,1,.85f),15));
            foreach(float x in new[]{-6f,6f})foreach(float z in new[]{-6f,6f})
                Add($"{name} Column {x}-{z}",c+new Vector3(x,2,z),new(.65f,4,.5f),"Basement_Corridor_E_Pillar_A",true);
            Add(name+" Bench",c+new Vector3(-6,.45f,3),new(4,.9f,1.2f),"Platform_4m",true);
            Add(name+" Equipment",c+new Vector3(-6,1.4f,3),new(1,1,.7f),props[i]);
            for(int n=0;n<2;n++)
            {
                var crate=Add($"Crate_Stress_{i}_{n}",c+new Vector3(6+n*1.2f,.55f,3),new(1.05f),"Breakable_Wooden_Crate",true);
                crate.MeshPath=PbrCrateChecks.Corrected;crate.MotionType=MotionType.Dynamic;crate.Mass=12;crate.CanPickUp=true;
                crate.Damageable=true;crate.MaxHealth=100;crate.StaticLighting=false;
            }
            var phone=Add(name+" Loose phone",c+new Vector3(-5,1.1f,3),new(.38f,.3f,.3f),"RedTelephone",true);
            phone.MotionType=MotionType.Dynamic;phone.Mass=2;phone.CanPickUp=true;phone.StaticLighting=false;
            var item=Add($"Item_{(i%2==0?"HealthPack":"SuitBattery")}__Stress_{i}",c+new Vector3(-7,1.2f,3),
                i%2==0?new(.63f,.36f,.39f):new(.23f,.46f,.23f),i%2==0?"FirstAidKit01":"Battery07",true);
            item.CanPickUp=true;item.MotionType=MotionType.Dynamic;item.Mass=1;item.StaticLighting=false;
        }
        for(int i=0;i<12;i++)
        {
            if(i%4<3)Connection(i,i+1,0);
            if(i/4<2)Connection(i,i+4,2);
        }
        // One fixed reflection capture at the open threshold of each pair, within the eight-probe limit.
        for(int pair=0;pair<6;pair++)
        {
            var p=Center(pair/2*4+pair%2*2)+new Vector3(11,2,0);
            level.Entities.Add(new(){Id=Id("Reflection "+pair),Name="Paired rooms reflection "+pair,Type=EntityTypes.ReflectionProbe,
                LocalPosition=p,ReflectionProbe=new(){InfluenceSize=new(44,6,22),ProjectionSize=new(42,4,20)}});
        }
        level.Entities.Add(new(){Id=Id("Spawn"),Name="PlayerSpawn",Type=EntityTypes.PlayerSpawn,LocalPosition=new(0,.1f,-6)});
        LevelIO.Save(path,level);Validate(root);
        Console.WriteLine("Created "+path+". Existing levels and startup remain unchanged.");

        void Wall(string name,bool x,Vector3 c,float fixedAt,float from,float to,float bottom=0,float top=4)
        {
            for(float v=from;v<to-.001f;v+=4)
            {
                float length=MathF.Min(4,to-v);
                Add(name+" "+v+" "+bottom,c+(x?new Vector3(v+length/2,(top+bottom)/2,fixedAt):new(fixedAt,(top+bottom)/2,v+length/2)),
                    new(.2f,top-bottom,length),"Basement_Corridor_A_Wall_Double_Sided_4m",true,x?90:0);
            }
        }
        void Boundary(int room,int side,bool doorway)
        {
            bool x=side>=2;float edge=side%2==0?-10:10;var c=Center(room);string n=names[room]+" Wall "+side;
            if(!doorway){Wall(n,x,c,edge,-10,10);return;}
            Wall(n,x,c,edge,-10,-2);Wall(n,x,c,edge,2,10);Wall(n+" Header",x,c,edge,-2,2,3.2f);
        }
        void Connection(int a,int b,int axis)
        {
            var c=(Center(a)+Center(b))/2; string name="Threshold "+a+"-"+b;bool x=axis==0;
            Add(name+" floor",c+new Vector3(0,-.2f,0),x?new(2,.4f,4):new(4,.4f,2),solid:true);
            Add(name+" floor visual",c+new Vector3(0,-.06f,0),x?new(2,.12f,4):new(4,.12f,2),"Basement_Corridor_B_Floor_4x4m");
            Add(name+" ceiling",c+new Vector3(0,3.35f,0),x?new(2,.3f,4):new(4,.3f,2),"Basement_Corridor_A_Ceiling_4x4m",true);
            Wall(name+" edge A",x,c,-2,-1,1,0,3.2f);Wall(name+" edge B",x,c,2,-1,1,0,3.2f);
            level.RoomVisibility.Portals.Add(new(){Id=Id(name),Name=name,RoomA=level.RoomVisibility.Rooms[a].Id,RoomB=level.RoomVisibility.Rooms[b].Id,
                Center=c+new Vector3(0,1.6f,0),Size=x?new(.2f,3.3f,4.1f):new(4.1f,3.3f,.2f),Axis=axis});
        }
    }

    internal static void Validate(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);var level=LevelIO.Load(path);
        void Check(bool v,string message){if(!v)throw new InvalidDataException(message);}
        Check(level.Entities.Select(e=>e.Id).Distinct().Count()==level.Entities.Count,"Duplicate entity IDs.");
        Check(level.RoomVisibility?.Rooms.Count==12 && level.RoomVisibility.Portals.Count==17,"Missing rooms/connections.");
        Check(new RoomVisibilityGraph(level.RoomVisibility).Error.Length==0,"Invalid visibility graph.");
        Check(level.Entities.Count(e=>e.Type==EntityTypes.PointLight)==12,"Expected twelve fixed lights.");
        Check(level.Entities.Count(e=>e.Damageable)==24,"Expected twenty-four breakable crates.");
        Check(level.Entities.All(e=>e.Interaction==null && e.Scripts.Count==0),"Stress fixture should not add puzzles/scripts.");
        int parts=0;foreach(var group in level.Entities.Where(e=>e.MeshPath.Length>0).GroupBy(e=>e.MeshPath))
        {
            string file=Path.Combine(root,"Game",group.Key);Check(File.Exists(file),"Missing model "+file);
            parts+=GlbModelLoader.Load(file).Parts.Count*group.Count();
        }
        Check(parts<4096,"Exceeded object buffer limit: "+parts);
        var editor=new LevelEditorController();editor.LoadFromMemory(path,level);ReflectionRoomLayout.Create(editor);
        // Every open doorway must remain free at player height, with floor support across the threshold.
        foreach(var portal in level.RoomVisibility!.Portals)
        {
            Vector3 p=portal.Center;
            for(int n=-12;n<=12;n++)
            {
                var center=p+(portal.Axis==0?Vector3.UnitX:Vector3.UnitZ)*n*.1f;
                Check(!editor.SolidColliders.Any(b=>center.X+.3f>b.Min.X && center.X-.3f<b.Max.X &&
                    center.Z+.3f>b.Min.Z && center.Z-.3f<b.Max.Z && center.Y+.7f>b.Min.Y && center.Y-.7f<b.Max.Y),"Blocked doorway "+portal.Name);
            }
        }
        Console.WriteLine($"PASS: 12 rooms, 17 clear connections, 4,800 m2 room area plus thresholds; {level.Entities.Count} entities, {parts} potential model-part draws, {level.Entities.Select(e=>e.MeshPath).Where(p=>p.Length>0).Distinct().Count()} model assets.");
    }

    internal static void Pixels(string root)
    {
        using var host=new EngineHost(1280,720,"Twelve-room submission references");var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var game=new Game.HL2GameModule(Path.Combine(root,"Game/Content/Levels",FileName));game.AssetStreamingEnabled=false;game.Initialize(host.Context);
        var world=(BasicWorldRenderer)PbrBaselineChecks.Field(game,"_world");var camera=(Game.FpsCamera)PbrBaselineChecks.Field(game,"_camera");
        var reflections=(ReflectionProbeSession)PbrBaselineChecks.Field(game,"_roomReflections");var models=(IDictionary)PbrBaselineChecks.Field(game,"_weaponModelCache");
        string folder=Path.Combine(AppContext.BaseDirectory,"qa","render-stress-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        var timer=Stopwatch.StartNew();world.ViewCullingEnabled=false;game.RoomVisibilityEnabled=false;
        while(true)
        {
            target.Frame(()=>game.RenderWorld(renderer),read:false);
            if(models.Count>0 && models.Values.Cast<object>().All(e=>PbrBaselineChecks.Field(e,"Model")!=null) && reflections.Ready)break;
            if(timer.Elapsed.TotalSeconds>90)throw new TimeoutException(reflections.Status);Thread.Sleep(5);
        }
        int cases=0,saved=0;world.ViewCullingEnabled=true;game.RoomVisibilityEnabled=true;
        foreach(bool torch in new[]{false,true})for(int room=0;room<12;room++)for(int direction=0;direction<4;direction++)
        {
            camera.Position=Center(room)+new Vector3(0,1.65f,-5);camera.Yaw=direction*MathF.PI/2;camera.Pitch=-.1f;
            PbrBaselineChecks.Set(game,"_flashlightOn",torch);game.RenderPreparationEnabled=true;world.DrawGroupingEnabled=true;
            var actual=target.Frame(()=>game.RenderWorld(renderer));int draws=world.MainDrawCalls;
            game.RenderPreparationEnabled=false;world.DrawGroupingEnabled=false;
            var reference=target.Frame(()=>game.RenderWorld(renderer));saved+=world.MainDrawCalls-draws;
            int max=0;for(int i=0;i<actual.Length;i++)for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs((int)(actual[i]>>(8*c)&255)-(int)(reference[i]>>(8*c)&255)));
            if(direction==0 && !torch || max>1)RenderPipelineChecks.SavePng(actual,target.Width,target.Height,Path.Combine(folder,$"room-{room}-{direction}-{torch}.png"));
            if(max>1)throw new InvalidDataException($"Room {room}, view {direction}, torch {torch}: RGB difference {max}.");
            cases++;
        }
        if(saved<100)throw new InvalidDataException("Expected substantial repeated-static draw reduction.");
        Console.WriteLine($"PASS: {cases} twelve-room image comparisons, max RGB <=1/255; {saved} total hardware draws avoided. Captures: {folder}");
    }
}

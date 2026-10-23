using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;

internal static class IndirectLightingRoom
{
    public const string FileName = "indirectLightingRoom.json";
    private const string Models = "Content/Models/ViewModels/";
    public static LevelFile Create()
    {
        var level = new LevelFile { UsePointLights = true, AmbientLight = .01f, DirectionalLight = 0,
            Bake = new() { Enabled = true, TexelsPerTriangle = 16, Samples = 64, RayDistance = 25 } };
        string Id(string name) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("indirect-room/"+name))).ToLowerInvariant();
        LevelEntityDef Add(string name, string model, Vector3 position, Vector3 size, float yaw = 0, bool bake = true)
        {
            var entity = new LevelEntityDef { Id = Id(name), Name = name, Type = EntityTypes.RigidBody,
                MeshPath = Models+model+".glb", LocalPosition = position, LocalRotationEulerDeg = new(0,yaw,0), Size = size,
                Color = Vector4.One, MotionType = MotionType.Static, Shape = "Box", Mass = 0, Restitution = 0, StaticLighting = bake };
            level.Entities.Add(entity); return entity;
        }
        for (int x=-2;x<=2;x+=4) for (int z=-2;z<=2;z+=4)
        {
            Add($"Floor_{x}_{z}","Basement_Corridor_B_Floor_4x4m", new(x,-.08f,z),new(4,.16f,4));
            Add($"Ceiling_{x}_{z}","Basement_Corridor_A_Ceiling_4x4m",new(x,4.1f,z),new(4,.2f,4));
        }
        for (int offset=-2;offset<=2;offset+=4)
        {
            Add($"NorthWall_{offset}","Basement_Corridor_A_Wall_Double_Sided_4m",new(offset,2,4.1f),new(.2f,4,4),90);
            Add($"SouthWall_{offset}","Basement_Corridor_A_Wall_Double_Sided_4m",new(offset,2,-4.1f),new(.2f,4,4),90);
            Add($"EastWall_{offset}","Basement_Corridor_A_Wall_Double_Sided_4m",new(4.1f,2,offset),new(.2f,4,4));
            Add($"WestWall_{offset}","Basement_Corridor_A_Wall_Double_Sided_4m",new(-4.1f,2,offset),new(.2f,4,4));
        }
        Add("BouncePartition","Basement_Corridor_A_Wall_Double_Sided_4m",new(.6f,1.5f,1.7f),new(.2f,3,4.6f));
        var light = new LevelEntityDef { Id = Id("warm-light"), Name = "FixedWarmBounceLight", Type = EntityTypes.PointLight,
            LocalPosition = new(-2,2.8f,2), LightColor = new(1,.63f,.3f,1), Intensity = 5, Range = 9, CastShadows = true, BakeIndirect = true };
        level.Entities.Add(light);
        var blue = new LevelEntityDef { Id = Id("blue-light"), Name = "RealtimeScreenLight", Type = EntityTypes.PointLight,
            LocalPosition = new(-1,2.2f,-1.2f), LightColor = new(.15f,.5f,1,1), Intensity = 1.8f, Range = 3, IsSpotLight = true,
            LocalRotationEulerDeg = new(-90,0,0), SpotAngleDeg = 100, CastShadows = true };
        level.Entities.Add(blue);
        var stand = Add("ConsoleStand","Breakable_Wooden_Crate",new(-1,.45f,-.8f),new(1.1f,.9f,.7f),bake:false);
        var console = Add("RealtimeConsole","Console 1",new(-1,1.25f,-.8f),new(1.2f,.7f,.9f),270,false);
        console.EmissionIntensity = 2; console.EmissionLightId = blue.Id;
        var lever = Add("RealtimeLightSwitch","Lever04",new(-2.1f,1.1f,-.8f),new(.25f,.5f,.25f),bake:false);
        lever.Type = EntityTypes.Prop; lever.Interaction = new() { Kind = "LightSwitch", Targets = [blue.Id] };
        for (int i=0;i<2;i++)
        {
            var crate = Add($"MovableCrate_{i}","Breakable_Wooden_Crate",new(2.2f,.55f,.6f+i*1.6f),new(1,1,1),bake:false);
            crate.MotionType = MotionType.Dynamic; crate.CanPickUp = true; crate.Mass = 12; crate.Damageable = true; crate.MaxHealth = 100;
        }
        level.Entities.Add(new() { Id = Id("spawn"), Name = "Spawn", Type = EntityTypes.PlayerSpawn, LocalPosition = new(-1.8f,0,-2.7f) });
        return level;
    }
    public static void Build(string root)
    {
        string path = Path.Combine(root,"Game/Content/Levels",FileName);
        if (File.Exists(path)) throw new IOException("The lighting test already exists; existing level edits will not be overwritten.");
        LevelIO.Save(path,Create()); Bake(root,path);
    }
    public static void Bake(string root, string path)
    {
        var level=LevelIO.Load(path);
        var snapshot=BakeSnapshot.Capture(level,p=>Path.Combine(root,"Game",p));
        var timer=System.Diagnostics.Stopwatch.StartNew(); int last=-1;
        var data=IndirectLightBaker.Bake(snapshot,p=>
        {
            int step=(int)(p*10);
            if (Interlocked.Exchange(ref last,step)!=step) Console.WriteLine($"Bake {Math.Min(100,step*10)}%");
        });
        LightmapFile.Save(LightmapFile.PathFor(path),data);
        Console.WriteLine($"Baked {data.Records.Count} models, {data.Records.Sum(r=>r.PartTriangles.Sum())} triangles, {data.Size}x{data.Size} HDR atlas ({data.Pixels.Length/1048576.0:F2} MiB) in {timer.Elapsed.TotalSeconds:F2}s. {LightmapFile.PathFor(path)}");
    }
    public static void Inspect(string root)
    {
        foreach(var path in Create().Entities.Where(e=>e.StaticLighting).Select(e=>e.MeshPath).Distinct())
        {
            var model=GlbModelLoader.Load(Path.Combine(root,"Game",path));
            Console.WriteLine($"{path}: {model.Parts.Sum(p=>p.Indices.Length/3)} triangles, {model.Parts.Count} parts; metallic factors {string.Join(',',model.Parts.Select(p=>p.MetallicFactor))}");
        }
    }
}

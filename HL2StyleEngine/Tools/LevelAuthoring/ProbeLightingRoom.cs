using System.Numerics;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;

internal static class ProbeLightingRoom
{
    public const string FileName = "lightProbeTest.json";
    public static void Build(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(path))throw new IOException("Probe test already exists; existing edits will not be overwritten.");
        var level=LevelIO.Load(Path.Combine(root,"Game/Content/Levels",IndirectLightingRoom.FileName));
        level.Bake.Probes=new(){Spacing=1.25f};
        var template=level.Entities.First(e=>e.Name=="MovableCrate_0");
        template.LocalPosition=new(-2.6f,.55f,1.1f);
        Vector3[] positions=[new(-2.5f,.55f,2.7f),new(-.8f,.55f,2.8f),new(3.1f,.55f,-2),new(2.2f,.55f,3.1f),new(-3.3f,.45f,-1.6f)];
        for(int i=0;i<positions.Length;i++)
            level.Entities.Add(new(){Id=$"probe-crate-{i}",Name=i==4?"ProbeCarryCrate":$"ProbeStressCrate_{i}",
                Type=EntityTypes.RigidBody,MeshPath=template.MeshPath,LocalPosition=positions[i],Size=i==4?new(.8f,.8f,.8f):Vector3.One,
                MotionType=MotionType.Dynamic,Shape="Box",CanPickUp=true,Mass=12,Restitution=0,
                Color=Vector4.One,Damageable=i!=4,MaxHealth=100});
        LevelIO.Save(path,level);IndirectLightingRoom.Bake(root,path);
    }
}

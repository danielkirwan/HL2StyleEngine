using System.Numerics;
using Engine.Physics.Collision;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed class LightProbeVolume
{
    public const int MaxTriangles = 65536;
    public Vector3 Origin { get; set; }
    public float Spacing { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public bool[] Valid { get; set; } = [];
    public Vector3[] Irradiance { get; set; } = [];
    public Vector3[] OccluderVertices { get; set; } = [];
    public Vector3 Position(int index) => Origin + new Vector3(index%Width, index/Width%Height, index/(Width*Height))*Spacing;

    public bool IsValid()
    {
        if (!Finite(Origin) || !float.IsFinite(Spacing) || Spacing is < .5f or > 8 ||
            Width is < 1 or > 128 || Height is < 1 or > 128 || Depth is < 1 or > 128) return false;
        int count = Width*Height*Depth;
        return count <= 4096 && Valid != null && Valid.Length == count && Irradiance != null && Irradiance.Length == count*6 &&
            OccluderVertices != null && OccluderVertices.Length > 0 && OccluderVertices.Length <= MaxTriangles*3 && OccluderVertices.Length%3 == 0 &&
            OccluderVertices.All(Finite) && Irradiance.All(v=>Finite(v) && Vector3.Min(v,Vector3.Zero)==Vector3.Zero &&
                Vector3.Max(v,new Vector3(60000))==new Vector3(60000));
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

// Constructed on the loading/baking worker; queries use the existing thread-local BVH scratch storage.
public sealed class LightProbeSampler
{
    public LightProbeVolume Volume { get; }
    private readonly MeshCollisionMesh _occluders;
    public LightProbeSampler(LightProbeVolume volume)
    {
        if (!volume.IsValid()) throw new InvalidDataException("Invalid light probe volume.");
        Volume=volume;
        var triangles=new MeshCollisionTriangle[volume.OccluderVertices.Length/3];
        for(int i=0;i<triangles.Length;i++) triangles[i]=new(volume.OccluderVertices[i*3],volume.OccluderVertices[i*3+1],volume.OccluderVertices[i*3+2]);
        _occluders=new MeshCollisionMesh(triangles,0);
    }

    public ProbeIrradiance Sample(Vector3 position)
    {
        Vector3 grid=(position-Volume.Origin)/Volume.Spacing;
        if (!float.IsFinite(grid.LengthSquared()) || grid.X < -.5f || grid.Y < -.5f || grid.Z < -.5f ||
            grid.X > Volume.Width-.5f || grid.Y > Volume.Height-.5f || grid.Z > Volume.Depth-.5f) return default;
        grid=Vector3.Clamp(grid,Vector3.Zero,new(Volume.Width-1,Volume.Height-1,Volume.Depth-1));
        int x=(int)grid.X,y=(int)grid.Y,z=(int)grid.Z;
        Vector3 f=grid-new Vector3(x,y,z);
        Span<Vector3> sum=stackalloc Vector3[6]; sum.Clear(); float weight=0;
        for(int dz=0;dz<2;dz++) for(int dy=0;dy<2;dy++) for(int dx=0;dx<2;dx++)
        {
            float w=(dx==0?1-f.X:f.X)*(dy==0?1-f.Y:f.Y)*(dz==0?1-f.Z:f.Z);
            if(w<=0)continue;
            int index=Math.Min(x+dx,Volume.Width-1)+Volume.Width*(Math.Min(y+dy,Volume.Height-1)+Volume.Height*Math.Min(z+dz,Volume.Depth-1));
            if(!Volume.Valid[index] || Blocked(position,Volume.Position(index)))continue;
            for(int axis=0;axis<6;axis++)sum[axis]+=Volume.Irradiance[index*6+axis]*w;
            weight+=w;
        }
        if(weight<1e-5f)return default;
        for(int axis=0;axis<6;axis++)sum[axis]/=weight;
        return ProbeIrradiance.Pack(sum);
    }

    private bool Blocked(Vector3 from,Vector3 to)
    {
        Vector3 delta=to-from;float distance=delta.Length();
        return distance>.004f && Raycast.RayIntersectsMesh(new Ray(from,delta/distance),_occluders,.002f,distance-.002f,out _);
    }
}

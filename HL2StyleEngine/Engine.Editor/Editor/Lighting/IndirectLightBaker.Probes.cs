using System.Numerics;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Render;

namespace Engine.Editor.Editor;

public static partial class IndirectLightBaker
{
    private static readonly Vector3[] ProbeAxes = [Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];

    private static LightProbeVolume BakeProbes(LevelBakeSettings settings, List<Surface> surfaces, MeshCollisionMesh mesh,
        List<WorldPointLight> lights, Action<float>? progress, CancellationToken cancel)
    {
        float spacing=settings.Probes!.Spacing;
        if(!float.IsFinite(spacing) || spacing is < .5f or > 8 || surfaces.Count>LightProbeVolume.MaxTriangles)
            throw new InvalidDataException("Probes require spacing 0.5-8 m and at most 65,536 static triangles.");
        Vector3 extent=mesh.Bounds.Max-mesh.Bounds.Min;
        Vector3 counts=Vector3.Max(Vector3.One,new(MathF.Ceiling(extent.X/spacing),MathF.Ceiling(extent.Y/spacing),MathF.Ceiling(extent.Z/spacing)));
        if(!float.IsFinite(counts.LengthSquared()) || counts.X>128 || counts.Y>128 || counts.Z>128 || counts.X*counts.Y*counts.Z>4096)
            throw new InvalidDataException("Probe volume exceeds 4,096 samples; increase spacing or use a smaller level.");
        var volume=new LightProbeVolume { Width=(int)counts.X,Height=(int)counts.Y,Depth=(int)counts.Z,Spacing=spacing,
            Origin=(mesh.Bounds.Min+mesh.Bounds.Max-(counts-Vector3.One)*spacing)*.5f,
            OccluderVertices=surfaces.SelectMany(s=>new[]{s.Triangle.A,s.Triangle.B,s.Triangle.C}).ToArray() };
        int count=volume.Width*volume.Height*volume.Depth,done=0;
        volume.Valid=new bool[count];volume.Irradiance=new Vector3[count*6];
        Parallel.For(0,count,new ParallelOptions{CancellationToken=cancel,MaxDegreeOfParallelism=2},index=>
        {
            cancel.ThrowIfCancellationRequested();
            Vector3 position=volume.Position(index);var candidates=new List<int>();int backFaces=0;
            bool clear=true;
            foreach(var axis in ProbeAxes)
            {
                int hit=Closest(new Ray(position,axis),extent.Length()+1,mesh,candidates,out float distance);
                if(hit<0)continue;
                if(distance<.08f){clear=false;break;}
                var surface=surfaces[hit];
                Vector3 n=Vector3.Normalize(surface.A+surface.B+surface.C);
                if(Vector3.Dot(n,axis)>0)backFaces++;
            }
            // Reject samples buried in closed geometry; visibility filtering still applies at runtime.
            if(clear && backFaces<4)
            {
                volume.Valid[index]=true;
                for(int axis=0;axis<6;axis++)
                {
                    cancel.ThrowIfCancellationRequested();
                    volume.Irradiance[index*6+axis]=Integrate(position,ProbeAxes[axis],(uint)(index*73856093^axis*19349663),
                        settings,surfaces,mesh,lights,candidates);
                }
            }
            progress?.Invoke(.85f+.15f*Interlocked.Increment(ref done)/count);
        });
        if(!volume.Valid.Any(v=>v))throw new InvalidDataException("No usable probes. Adjust spacing or static geometry.");
        return volume;
    }

    private static Vector3 Integrate(Vector3 position, Vector3 normal, uint seed, LevelBakeSettings settings,
        List<Surface> surfaces, MeshCollisionMesh mesh, List<WorldPointLight> lights, List<int> candidates)
    {
        var tangent=Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y)<.9f?Vector3.UnitY:Vector3.UnitX,normal));
        var bitangent=Vector3.Cross(normal,tangent);Vector3 sum=default;
        for(int sample=0;sample<settings.Samples;sample++)
        {
            float r=MathF.Sqrt((sample+.5f)/settings.Samples),angle=2*MathF.PI*RadicalInverse((uint)sample^seed);
            Vector3 direction=tangent*(r*MathF.Cos(angle))+bitangent*(r*MathF.Sin(angle))+normal*MathF.Sqrt(1-r*r);
            var ray=new Ray(position+normal*Bias,direction);
            int hit=Closest(ray,settings.RayDistance,mesh,candidates,out float distance);
            if(hit<0)continue;
            var bounce=surfaces[hit];Vector3 point=ray.Origin+direction*distance;
            Vector3 bary=Barycentric(point,bounce.Triangle);
            Vector3 n=Vector3.Normalize(bounce.A*bary.X+bounce.B*bary.Y+bounce.C*bary.Z);
            if(Vector3.Dot(n,direction)>=0)continue;
            Vector3 direct=default;
            foreach(var light in lights)
            {
                Vector3 delta=light.Position-point;float d=delta.Length();
                if(d<Bias || d>=light.Range)continue;
                Vector3 l=delta/d;float ndotl=MathF.Max(0,Vector3.Dot(n,l));
                if(ndotl<=0 || Raycast.RayIntersectsMesh(new Ray(point+n*Bias,l),mesh,Bias,d-Bias,out _))continue;
                float cone=1;
                if(light.SpotAngleDegrees>0)
                {
                    float outer=MathF.Cos(light.SpotAngleDegrees*MathF.PI/360),inner=MathF.Cos(light.SpotAngleDegrees*.8f*MathF.PI/360);
                    float t=Math.Clamp((Vector3.Dot(-l,light.Direction)-outer)/MathF.Max(.0001f,inner-outer),0,1);cone=t*t*(3-2*t);
                }
                float falloff=1-d/light.Range;
                direct+=light.Color*(light.Intensity*falloff*falloff*ndotl*cone);
            }
            sum+=bounce.Material.Sample(bounce.Ua*bary.X+bounce.Ub*bary.Y+bounce.Uc*bary.Z)*bounce.Tint*direct;
        }
        return Vector3.Clamp(sum/settings.Samples,Vector3.Zero,new(60000));
    }
}

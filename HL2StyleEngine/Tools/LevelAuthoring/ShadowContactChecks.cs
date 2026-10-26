using System.Numerics;
using Engine.Physics.Collision;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class ShadowContactChecks
{
    internal static void Run(bool assert)
    {
        using var host=new EngineHost(960,720,"Shadow contact regression");
        var renderer=host.Context.Renderer;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        using var world=new BasicWorldRenderer(renderer.GraphicsDevice,renderer.WorldOutputDescription,"Shaders");
        world.PointShadowLimit=4;world.LightingModel=SurfaceLightingModel.Pbr;
        world.AmbientStrength=world.DirectionalStrength=0;
        var floor=new LoadedModel([new LoadedModelPart(
            [new(-30,0,-30),new(-30,0,30),new(30,0,30),new(30,0,-30)],
            [Vector3.UnitY,Vector3.UnitY,Vector3.UnitY,Vector3.UnitY],
            [new(0,0),new(0,1),new(1,1),new(1,0)],[0,1,2,0,2,3],Vector4.One,null,null,0,1)
            {BaseColorTexture=new PreparedTexture("contact-white",1,1,TextureSemantic.Color,false,[[255,255,255,255]])}]);
        using var model=world.CreateRenderModel(floor);
        var cube=Matrix4x4.CreateTranslation(0,.5f,0);
        var caster=new ShadowCaster(null,cube,new(0,.5f,0),.87f);
        string folder=Path.Combine(AppContext.BaseDirectory,"qa","contact-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        int failures=0;
        foreach(bool spot in new[]{true,false})
        foreach(float distance in new[]{2f,4f,8f,12f})foreach(float angle in Enumerable.Range(0,8).Select(i=>i*MathF.PI/4))
        {
            var source=new Vector3(MathF.Sin(angle)*distance,1.7f,MathF.Cos(angle)*distance);
            var forward=Vector3.Normalize(new Vector3(0,.5f,0)-source);
            var right=-Vector3.Normalize(Vector3.Cross(Vector3.UnitY,forward));
            var origin=source+right*.24f-Vector3.UnitY*.14f;
            var light=new WorldPointLight(origin,Vector3.One,7,22,Vector3.Normalize(source+forward*8-origin),spot?55:0,true,100,"torch");
            // Independent observer keeps the contact area large even when the light is distant.
            var eye=new Vector3(3,3,5);
            var matrix=Matrix4x4.CreateLookAt(eye,new(0,.5f,0),Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,target.Width/(float)target.Height,.05f,100);
            Matrix4x4.Invert(matrix,out var inverse);
            var bright=Frame(false);var shadow=Frame(true);
            int expected=0,correct=0,lit=0,falseShadow=0;
            for(int y=0;y<target.Height;y+=2)for(int x=0;x<target.Width;x+=2)
            {
                int i=y*target.Width+x;
                var far=Vector4.Transform(new Vector4((x+.5f)/target.Width*2-1,1-(y+.5f)/target.Height*2,1,1),inverse);
                var ray=Vector3.Normalize(new Vector3(far.X,far.Y,far.Z)/far.W-eye);
                if(ray.Y>=-.001f)continue;
                float t=-eye.Y/ray.Y;var point=eye+ray*t;
                if(Math.Abs(point.X)>25 || Math.Abs(point.Z)>25 ||
                    Hit(eye,ray,t,.505f) || Energy(bright[i])<45)continue;
                var to=point-origin;float length=to.Length();to/=length;
                bool occluded=Hit(origin,to,length,.47f),clear=!Hit(origin,to,length,.54f);
                if(occluded){expected++;if(Energy(shadow[i])<Energy(bright[i])*.35f)correct++;}
                if(clear){lit++;if(Energy(shadow[i])<Energy(bright[i])*.65f)falseShadow++;}
            }
            double coverage=correct/(double)Math.Max(1,expected),wrong=falseShadow/(double)Math.Max(1,lit);
            Console.WriteLine($"Contact {(spot?"spot":"point")} distance={distance} angle={angle}: {correct}/{expected} interior shadow pixels ({coverage:P1}); {falseShadow}/{lit} false shadows ({wrong:P2}).");
            if(expected<10 || coverage<.95 || wrong>.01)failures++;
            if(angle==0)
            {
                RenderPipelineChecks.SavePng(shadow,target.Width,target.Height,Path.Combine(folder,$"{spot}-distance-{distance}.png"));
                RenderPipelineChecks.SavePng(bright,target.Width,target.Height,Path.Combine(folder,$"{spot}-distance-{distance}-no-shadow.png"));
            }
            uint[] Frame(bool enabled)=>target.Frame(()=>
            {
                world.BeginFrame();world.UpdateCamera(matrix,eye);world.UpdatePointLights([light with{CastShadows=enabled}],eye);
                world.RenderShadows(renderer,[caster,new(model,Matrix4x4.Identity,Vector3.Zero,43)],new Viewport(0,0,target.Width,target.Height,0,1));
                using(world.BatchObjectUploads())
                {
                    world.DrawModel(renderer.CommandList,model,Matrix4x4.Identity,Vector4.One);
                    world.DrawBox(renderer.CommandList,cube,new(.5f,.2f,.08f,1));
                }
            });
        }
        Console.WriteLine($"Shadow contact: {failures} failing views / 64. {folder}");
        if(assert && failures>0)throw new InvalidDataException("Shadow visibility differs from independent ray/box reference.");
        static bool Hit(Vector3 origin,Vector3 direction,float distance,float half)=>Raycast.RayIntersectsObb(new Ray(origin,direction),new(0,.5f,0),new(half),Quaternion.Identity,0,distance-.001f,out _);
        static int Energy(uint p)=>(int)((p&255)+((p>>8)&255)+((p>>16)&255));
    }
}

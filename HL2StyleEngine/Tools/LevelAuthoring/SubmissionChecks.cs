using System.Numerics;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class SubmissionChecks
{
    public static void Capture(string root)
    {
        string path=Path.Combine(root,"Game/Content/Levels",RenderStressTest.FileName);
        var level=Engine.Editor.Level.LevelIO.Load(path);
        var probe=level.Entities.First(e=>e.ReflectionProbe!=null);
        var snapshot=Engine.Editor.Editor.ReflectionSnapshot.Create(level,path,probe.Id,p=>Path.Combine(root,"Game",p));
        var input=Engine.Editor.Editor.ReflectionCaptureInput.Prepare(snapshot,CancellationToken.None);
        using var host=new EngineHost(640,480,"Private capture submission A/B");var device=host.Context.Renderer.GraphicsDevice;
        var reference=Faces(false);var actual=Faces(true);float max=0;int lit=0;
        for(int f=0;f<6;f++)for(int i=0;i<reference[f].Length;i+=2)
        {
            float a=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(reference[f],i));
            float b=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual[f],i));
            if(!float.IsFinite(a+b))throw new InvalidDataException("Capture contains non-finite pixels.");
            max=Math.Max(max,Math.Abs(a-b)/Math.Max(1,Math.Abs(a)));if(i%8!=6 && a>.02f)lit++;
        }
        if(max>.002f || lit<1000)throw new InvalidDataException($"Capture differs or is blank: relative error {max}, lit {lit}.");
        Console.WriteLine($"PASS: six independent HDR capture faces; maximum relative error {max:G4}; {lit} lit channels. No source caches replaced.");

        byte[][] Faces(bool grouped)
        {
            using var capture=new Engine.Editor.Editor.RoomReflectionCapture(device,input);
            var world=(BasicWorldRenderer)PbrBaselineChecks.Field(capture,"_world");world.DrawGroupingEnabled=grouped;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!capture.Complete)
            {
                capture.Step();if(timer.Elapsed.TotalSeconds>90)throw new TimeoutException("Private capture timed out.");Thread.Sleep(1);
            }
            device.WaitForIdle();return capture.Faces;
        }
    }

    public static void Run()
    {
        using var host = new EngineHost(640, 480, "Submission A/B");
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        using var world = new BasicWorldRenderer(device, renderer.WorldOutputDescription, "Shaders");
        var source = new LoadedModel([new LoadedModelPart(
            [new(-.5f,-.5f,0), new(.5f,-.5f,0), new(.5f,.5f,0), new(-.5f,.5f,0)],
            [Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
            [new(0,0),new(1,0),new(1,1),new(0,1)], [0,1,2,0,2,3], Vector4.One,null,null,0,1)
        { BaseColorTexture = new PreparedTexture("submission-pattern",2,2,TextureSemantic.Color,false,
            [[255,120,60,255,60,190,100,255,70,120,220,255,200,180,150,255]]) }]);
        using var model = world.CreateRenderModel(source);
        int width = (int)renderer.WorldFramebuffer.Width, height = (int)renderer.WorldFramebuffer.Height;
        using var staging = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,
            device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format,TextureUsage.Staging));
        using var color = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,staging.Format,TextureUsage.RenderTarget));
        using var depth = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,PixelFormat.R32_Float,TextureUsage.DepthStencil));
        using var frame = device.ResourceFactory.CreateFramebuffer(new(depth,color));
        var scene = new RenderScene();
        scene.Begin();
        for(int i=0;i<24;i++)
        {
            var center = new Vector3((i%6-2.5f)*1.3f,(i/6-1.5f)*1.3f,-2);
            var transform = Matrix4x4.CreateScale(i%3==0?-.8f:.8f,1.1f,1.4f)*Matrix4x4.CreateRotationY(i*.07f)*
                Matrix4x4.CreateRotationZ(i*.03f)*Matrix4x4.CreateTranslation(center);
            scene.Add(new(i.ToString(),model,transform,new(.5f+i*.02f,.7f,1,1),center,1,AllowInstancing:true));
        }
        scene.Seal();
        bool sealedRejected=false;
        try{scene.Add(scene.Instances[0]);}catch(InvalidOperationException){sealedRejected=true;}
        if(!sealedRejected)throw new InvalidDataException("Sealed frame records can be changed.");
        int checks=0;
        foreach(var lighting in new[]{SurfaceLightingModel.Legacy,SurfaceLightingModel.Pbr})
        foreach(int limit in new[]{1,4})
        foreach(bool barrier in new[]{false,true})
        {
            world.LightingModel=lighting; world.PointShadowLimit=limit;
            var reference=Frame(false,false,barrier);
            int original=world.MainDrawCalls, bindings=world.MaterialBindings;
            Equal(reference,Frame(true,false,barrier));
            Require(world.MainDrawCalls==original && world.MaterialBindings<bindings,"State grouping did not reduce bindings.");
            Equal(reference,Frame(true,true,barrier));
            Require(world.InstancedObjects==24 && world.MainDrawCalls<original && world.InstanceUploads==1,"Static instance draw not used.");
            world.ObjectBatchUploadsEnabled=false;
            Equal(reference,Frame(true,true,barrier));
            Require(world.InstancedDrawCalls==0,"Individual upload path must not instance.");
            world.ObjectBatchUploadsEnabled=true;
            checks+=4;
        }
        // Differing per-pixel uniforms and dynamic eligibility must split the run.
        scene.Begin();
        for(int i=0;i<12;i++)
        {
            var p=new Vector3(i%6-2.5f,i/6-.5f,-2);
            scene.Add(new(i.ToString(),model,Matrix4x4.CreateTranslation(p),Vector4.One,p,1,
                Probe:new ProbeIrradiance{A=new Vector4(i*.01f)},AllowInstancing:i%2==0));
        }
        scene.Seal();
        Equal(Frame(false,false,false),Frame(true,true,false));
        Require(world.InstancedObjects==0,"Incompatible lighting or moving objects merged.");
        scene.Begin();
        for(int i=0;i<4100;i++)
        {
            Vector3 p=i==0?new(0,0,-2):new(1000+i,0,-2);
            scene.Add(new(i.ToString(),model,Matrix4x4.CreateTranslation(p),Vector4.One,p,1,AllowInstancing:true));
        }
        scene.Seal();world.ViewCullingEnabled=false;
        Equal(Frame(false,false,false),Frame(true,true,false));
        Require(world.ObjectDrawCount==4096 && world.InstancedObjects==4096 && world.MainDrawCalls==1,"Instance ring overflow was not bounded.");
        scene.Begin();
        for(int i=0;i<4;i++)
        {
            Vector3 p=new(i-1.5f,0,-2);
            scene.Add(new(i.ToString(),model,Matrix4x4.CreateTranslation(p),new(1,1,1,.5f),p,1,AllowInstancing:true));
        }
        scene.Seal();
        Equal(Frame(false,false,false),Frame(true,true,false));
        Require(world.InstancedObjects==0,"Non-opaque tints entered the static instance run.");
        device.WaitForIdle();
        Console.WriteLine($"PASS: {checks+7} submission checks; Legacy/PBR, single/multi shadow pipelines, transforms, tints, primitive barriers, uniform incompatibility and buffer bounds. RGB tolerance 1/255.");

        uint[] Frame(bool group,bool instances,bool barrier)
        {
            renderer.BeginFrame();world.BeginFrame();world.DrawGroupingEnabled=group;world.StaticInstancingEnabled=instances;
            var eye=new Vector3(0,0,8);
            world.UpdateCamera(Matrix4x4.CreateLookAt(eye,new(0,0,-2),Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(1,width/(float)height,.1f,100),eye);
            world.UpdatePointLights([new(new(0,3,3),Vector3.One,8,20,Vector3.Zero,0,false)],eye);
            world.RenderShadows(renderer,[],new(0,0,width,height,0,1));
            using(world.BatchObjectUploads())
            using(world.GroupModelDraws(renderer.CommandList))
            {
                int i=0;
                foreach(var item in scene.Instances)
                {
                    world.DrawInstance(renderer.CommandList,item);
                    if(barrier && ++i==12)world.DrawBox(renderer.CommandList,Matrix4x4.CreateScale(.2f)*Matrix4x4.CreateTranslation(0,0,1),Vector4.One);
                }
            }
            renderer.ResolveWorldToSwapchain(frame);renderer.CommandList.CopyTexture(color,staging);
            renderer.EndFrame();device.WaitForIdle();
            var map=device.Map<uint>(staging,MapMode.Read);
            try { var result=new uint[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++)result[y*width+x]=map[x,y];return result; }
            finally {device.Unmap(staging);}
        }
        static void Equal(uint[] a,uint[] b)
        {
            int max=0,lit=0;
            for(int i=0;i<a.Length;i++)for(int c=0;c<3;c++)
            {int x=(int)(a[i]>>(c*8)&255),y=(int)(b[i]>>(c*8)&255);max=Math.Max(max,Math.Abs(x-y));if(x>40)lit++;}
            Require(max<=1 && lit>1000,$"Submission image mismatch/blank: max={max}, lit={lit}.");
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    }
}

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;
using Vortice.Direct3D11;

internal static class AtmosphereChecks
{
    internal const string FileName="atmosphereTest.json";
    internal static readonly PostProcessSettings Preset=new() {BloomEnabled=true,GradingEnabled=true,FogEnabled=true,
        Contrast=1.03f,Saturation=.95f,ColorFilter=new Vector3(.95f,1,1.03f),FogDensity=.015f,FogStart=5,FogHeightFalloff=.25f,FogMaxOpacity=.65f};
    private static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}

    internal static void Build(string root)
    {
        string source=Path.Combine(root,"Game/Content/Levels",RenderStressTest.FileName),destination=Path.Combine(root,"Game/Content/Levels",FileName);
        if(File.Exists(destination))throw new IOException("Preserve existing atmosphere-level edits.");
        var level=LevelIO.Load(source);level.PostProcessing=Preset;
        LevelIO.Save(destination,level);
        foreach(var probe in level.Entities.Where(e=>e.ReflectionProbe!=null))
            File.Copy(ReflectionProbeCache.PathFor(source,probe.Id),ReflectionProbeCache.PathFor(destination,probe.Id));
        Console.WriteLine("Created isolated twelve-room atmosphere test and copied six invariant HDR caches. Original level unchanged.");
    }

    internal static void Validate(string root)
    {
        int count=0;void Check(bool value,string message){Require(value,message);count++;}
        var empty=new LevelFile();byte[] original=StructuredData.WriteJson(empty);
        Check(!Encoding.UTF8.GetString(original).Contains("PostProcessing"),"Old levels gained atmosphere JSON.");
        Check(!new PostProcessSettings().Active,"Defaults must be disabled.");
        var clamped=(Preset with {BloomIntensity=float.NaN,BloomThreshold=-1,BloomDownsample=999,FogDensity=float.PositiveInfinity,
            FogStart=-1,FogMaxOpacity=4,Contrast=6,Saturation=-1,ColorFilter=new(float.NaN,-2,8)}).Sanitize();
        Check(clamped.BloomIntensity==.12f && clamped.BloomThreshold==.1f && clamped.BloomDownsample==4 && clamped.FogDensity==.025f &&
            clamped.FogStart==0 && clamped.FogMaxOpacity==.95f && clamped.Contrast==1.5f && clamped.Saturation==0 && (Vector3)clamped.ColorFilter==new Vector3(1,0,2),"Settings not bounded.");
        string folder=Path.Combine(AppContext.BaseDirectory,"qa","atmosphere-data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string save=Path.Combine(folder,"roundtrip.json");var editor=new LevelEditorController();editor.LoadFromMemory(save,empty);
        editor.SetPostProcessing(Preset);Check(editor.Dirty && editor.LevelFile.PostProcessing==Preset,"Editor change failed.");
        Check(editor.Undo() && editor.LevelFile.PostProcessing==null,"Atmosphere undo failed.");
        Check(editor.Redo() && editor.LevelFile.PostProcessing==Preset,"Atmosphere redo failed.");
        LevelIO.Save(save,editor.LevelFile);
        Check(LevelIO.Load(save).PostProcessing==Preset,"JSON settings roundtrip failed.");
        Check(File.Exists(StructuredData.CachePath(save)) && LevelIO.Load(save).PostProcessing==Preset,"RE:Dox settings roundtrip failed.");
        editor.SetPostProcessing(null);Check(original.SequenceEqual(StructuredData.WriteJson(editor.LevelFile)),"Reset changes old JSON.");
        string path=Path.Combine(root,"Game/Content/Levels",RenderStressTest.FileName);var level=LevelIO.Load(path);
        Func<string,string> resolve=p=>Path.Combine(root,"Game",p);
        var bake= BakeSnapshot.Capture(level,resolve).Json;
        var reflections=level.Entities.Where(e=>e.ReflectionProbe!=null).ToDictionary(e=>e.Id,e=>ReflectionSnapshot.Create(level,path,e.Id,resolve).Key());
        level.PostProcessing=Preset;
        Check(bake.SequenceEqual(BakeSnapshot.Capture(level,resolve).Json),"Postprocessing changed diffuse bake inputs.");
        foreach(var pair in reflections)
            Check(pair.Value==ReflectionSnapshot.Create(level,path,pair.Key,resolve).Key(),"Postprocessing changed HDR reflection energy/key.");
        string testPath=Path.Combine(root,"Game/Content/Levels",FileName);var fixture=LevelIO.Load(testPath);
        Check(fixture.PostProcessing==Preset,"Fixture settings changed.");
        fixture.PostProcessing=null;level.PostProcessing=null;
        Check(StructuredData.WriteJson(fixture).SequenceEqual(StructuredData.WriteJson(level)),"Atmosphere copy changed scene geometry/lighting.");
        foreach(var pair in reflections)
            Check(ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(testPath,pair.Key),pair.Value)!=null,"Copied invariant cache invalid.");
        Console.WriteLine($"PASS: {count} atmosphere data/editor/cache checks, JSON + RE:Dox roundtrips, undo/redo/reset, six unchanged capture keys.");
    }

    internal static void Pixels()
    {
        using var host=new EngineHost(640,480,"Atmosphere image checks");var renderer=host.Context.Renderer;var device=renderer.GraphicsDevice;
        Marshal.AddRef(device.GetD3D11Info().Device);using var native=new ID3D11Device(device.GetD3D11Info().Device);
        using var messages=native.QueryInterface<Vortice.Direct3D11.Debug.ID3D11InfoQueue>();
        using var world=new BasicWorldRenderer(device,renderer.WorldOutputDescription,"Shaders");int checks=0;
        foreach(var size in new[]{(640u,480u),(319u,239u),(800u,600u)})
        {
            renderer.Resize(size.Item1,size.Item2);
            using var target=new PbrBaselineChecks.ImageTarget(renderer);
            var eye=new Vector3(0,0,4);var vp=Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(1,size.Item1/(float)size.Item2,.1f,100);
            var viewport=new Viewport(0,0,size.Item1,size.Item2,0,1);
            uint[] Frame(PostProcessSettings? settings,bool cube=false,float depth=.9f,Viewport? rect=null,Matrix4x4? camera=null,Vector3? position=null)
                =>target.Frame(()=>{
                    renderer.CommandList.ClearColorTarget(0,cube?RgbaFloat.Black:new RgbaFloat(.7f,.3f,.1f,1));renderer.CommandList.ClearDepthStencil(cube?1:depth);
                    if(cube){world.BeginFrame();world.UpdateCamera(vp,eye);world.DrawBox(renderer.CommandList,Matrix4x4.CreateScale(.5f),new Vector4(3,3,3,1));}
                    renderer.ConfigurePostProcessing(settings,camera??vp,position??eye,rect??viewport);
                });
            var baseline=Frame(null);Require(renderer.PostProcessPasses==0,"Disabled path allocates passes.");checks++;
            Equal(baseline,Frame(new()));Equal(baseline,Frame(new(){GradingEnabled=true}));checks+=2;
            Equal(baseline,Frame(new(){FogEnabled=true,FogDensity=0,BloomEnabled=true,BloomIntensity=0}));checks++;
            var grade=Frame(new(){GradingEnabled=true,ColorFilter=new Vector3(.5f,1,1)});
            Require(grade.Where((p,i)=>p!=baseline[i]).Count()>100,"Colour grade did not change pixels.");checks++;
            var cubeImage=Frame(null,true);var bloom=Frame(new(){BloomEnabled=true,BloomIntensity=.8f,BloomThreshold=.5f},true);
            Require(renderer.PostProcessPasses==4 && bloom.Where((p,i)=>(cubeImage[i]&0xffffff)==0 && (p&0xffffff)!=0).Count()>50,"Bloom has no halo or wrong passes.");checks++;
            foreach(int downsample in new[]{2,4,2,4})
            {
                Frame(Preset with {BloomDownsample=downsample},true);
                Require(renderer.PostProcessGpuBytes==((size.Item1+downsample-1)/downsample)*((size.Item2+downsample-1)/downsample)*16+8,"Bloom payload not bounded.");checks++;
            }
            Frame(null);Require(renderer.PostProcessGpuBytes==8,"Disabled bloom targets retained.");checks++;
            foreach(float falloff in new[]{0,.3f})foreach(float yaw in new[]{0,.7f})
            {
                var position=new Vector3(3,1,5);var direction=Vector3.Transform(-Vector3.UnitZ,Quaternion.CreateFromYawPitchRoll(yaw,.2f,0));
                var matrix=Matrix4x4.CreateLookAt(position,position+direction,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(1,size.Item1/(float)size.Item2,.1f,100);
                var fog=new PostProcessSettings {FogEnabled=true,FogDensity=.1f,FogStart=0,FogHeightFalloff=falloff,FogColor=new Vector3(.2f,.3f,.5f)};
                float z=.99f;var pixels=Frame(fog,depth:z,camera:matrix,position:position);
                int x=target.Width/2,y=target.Height/2;Matrix4x4.Invert(matrix,out var inverse);
                var p=Vector4.Transform(new Vector4((x+.5f)/target.Width*2-1,1-(y+.5f)/target.Height*2,z,1),inverse);
                Vector3 end=new(p.X/p.W,p.Y/p.W,p.Z/p.W);float h0=-falloff*position.Y,h1=-falloff*end.Y;
                float average=Math.Abs(h1-h0)<.001f?MathF.Exp((h0+h1)/2):(MathF.Exp(h1)-MathF.Exp(h0))/(h1-h0);
                float amount=Math.Min(fog.FogMaxOpacity,1-MathF.Exp(-.1f*Vector3.Distance(position,end)*average));
                var linear=Vector3.Lerp(new(.7f,.3f,.1f),fog.FogColor,amount);var expected=Encode(linear);
                Equal([expected],[pixels[y*target.Width+x]]);checks++;
            }
            var partial=Frame(Preset,rect:new Viewport(size.Item1/4,size.Item2/4,size.Item1/2,size.Item2/2,0,1));
            Require((partial[0]&0xffffff)==0 && (partial[(target.Height/2)*target.Width+target.Width/2]&0xffffff)!=0,"Docked viewport leaked fog.");checks++;
            Equal(baseline,Frame(Preset,camera:new Matrix4x4()));checks++;
            renderer.BeginFrame();renderer.CommandList.ClearColorTarget(0,new(.7f,.3f,.1f,1));renderer.ResolveWorldToSwapchain(target.Framebuffer);
            Require(renderer.PostProcessPasses==0,"Atmosphere state leaked into unconfigured frame.");renderer.EndFrame();checks++;
        }
        device.WaitForIdle();renderer.DrainRetiredResources();
        Require(renderer.PendingRetirementBatches==0,"Resize/quality resources not retired.");
        for(ulong i=0;i<messages.NumStoredMessages;i++)
        {
            var message=messages.GetMessage(i);
            Require((int)message.Severity>2,"D3D11 validation: "+message.Description);
        }
        Console.WriteLine($"PASS: {checks+1} atmosphere GPU checks; disabled/neutral tolerance 1/255, bloom halo, fog CPU oracle across camera rotations, docked viewport, odd-size resize and quality retirement.");
    }

    private static uint Encode(Vector3 radiance)
    {
        static uint Channel(float c){c/=1+c;c=c<=.0031308f?12.92f*c:1.055f*MathF.Pow(c,1/2.4f)-.055f;return (uint)Math.Clamp((int)MathF.Round(c*255),0,255);}
        return 0xff000000|Channel(radiance.Z)|(Channel(radiance.Y)<<8)|(Channel(radiance.X)<<16);
    }
    private static void Equal(uint[] a,uint[] b)
    {
        int max=0;for(int i=0;i<a.Length;i++)for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs((int)(a[i]>>(c*8)&255)-(int)(b[i]>>(c*8)&255)));
        Require(max<=1,$"Atmosphere image mismatch: {max}/255.");
    }

    internal static unsafe void Gpu(bool reverse)
    {
        using var host=new EngineHost(1920,1080,"Atmosphere GPU profile");var renderer=host.Context.Renderer;var device=renderer.GraphicsDevice;
        using var target=new PbrBaselineChecks.ImageTarget(renderer);
        Marshal.AddRef(device.GetD3D11Info().Device);using var native=new ID3D11Device(device.GetD3D11Info().Device);using var context=native.ImmediateContext;
        using var disjoint=native.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
        using var beginQuery=native.CreateQuery(new QueryDescription(QueryType.Timestamp));using var endQuery=native.CreateQuery(new QueryDescription(QueryType.Timestamp));
        var modes=new Dictionary<string,PostProcessSettings?> {{"off",null},{"grade",new(){GradingEnabled=true}},
            {"bloom",new(){BloomEnabled=true}},{"fog",new(){FogEnabled=true}},{"all",Preset}};
        var results=new Dictionary<string,(double Mean,double P95)>();
        Console.WriteLine($"[AtmosphereGPU] {target.Width}x{target.Height}; timestamped resolve + postprocess only, constant HDR/depth, 30 warmup +120 samples/mode; test-only idle/readback, no present/physics/UI.");
        foreach(string mode in reverse?modes.Keys.Reverse():modes.Keys)
        {
            var values=new List<double>();
            for(int i=-30;i<120;i++)
            {
                renderer.BeginFrame();renderer.CommandList.ClearColorTarget(0,new(.7f,.3f,2,1));renderer.CommandList.ClearDepthStencil(.99f);
                renderer.CommandList.End();device.SubmitCommands(renderer.CommandList);device.WaitForIdle();
                renderer.CommandList.Begin();renderer.ConfigurePostProcessing(modes[mode],Matrix4x4.CreatePerspectiveFieldOfView(1,target.Width/(float)target.Height,.1f,100),Vector3.Zero,new(0,0,target.Width,target.Height,0,1));
                renderer.ResolveWorldToSwapchain(target.Framebuffer);renderer.CommandList.End();
                context.Begin(disjoint);context.End(beginQuery);device.SubmitCommands(renderer.CommandList);context.End(endQuery);context.End(disjoint);device.WaitForIdle();context.Flush();
                QueryDataTimestampDisjoint clock=default;ulong begin=0,end=0;var wait=Stopwatch.StartNew();
                while(context.GetData(disjoint,(IntPtr)(&clock),sizeof(QueryDataTimestampDisjoint),AsyncGetDataFlags.DoNotFlush).Code!=0 ||
                    context.GetData(beginQuery,(IntPtr)(&begin),sizeof(ulong),AsyncGetDataFlags.DoNotFlush).Code!=0 ||
                    context.GetData(endQuery,(IntPtr)(&end),sizeof(ulong),AsyncGetDataFlags.DoNotFlush).Code!=0)
                {if(wait.Elapsed.TotalSeconds>5)throw new TimeoutException("GPU timestamp unavailable.");Thread.Sleep(1);}
                Require(!clock.Disjoint && clock.Frequency>0 && end>=begin,"Invalid GPU timestamp.");
                if(i>=0)values.Add((end-begin)*1000.0/clock.Frequency);
            }
            renderer.DrainRetiredResources();values.Sort();results[mode]=(values.Average(),values[113]);
            Console.WriteLine($"[AtmosphereGPU] {mode}: mean {values.Average():F3}ms, P95 {values[113]:F3}ms; active texture payload {renderer.PostProcessGpuBytes} bytes.");
        }
        var delta=results["all"].Mean-results["off"].Mean;var p95=results["all"].P95-results["off"].P95;
        Require(delta<=1 && p95<=1.5,$"Atmosphere GPU increment exceeded budget: {delta:F3}/{p95:F3}ms.");
        Console.WriteLine($"PASS: added resolve/postprocess GPU mean/P95 {delta:F3}/{p95:F3}ms; fixed gate <=1/1.5ms. Full-host profile is the gameplay pacing gate.");
    }
}

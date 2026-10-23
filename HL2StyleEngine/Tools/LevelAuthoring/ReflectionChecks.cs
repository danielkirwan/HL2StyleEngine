using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class ReflectionChecks
{
    private static int _checks;
    private static void Check(bool value,string message) { if(!value) throw new InvalidDataException(message); _checks++; }
    private static void Near(Vector3 actual,Vector3 expected,string message,float tolerance=.025f)
        => Check(Vector3.Distance(actual,expected)<tolerance,$"{message}: {actual} != {expected}");

    public static void Run(string root)
    {
        var legacy=StructuredData.ReadJson<LevelFile>(Encoding.UTF8.GetBytes("{}"));
        Check(legacy.ReflectionEnvironment==ReflectionPreset.None && !Encoding.UTF8.GetString(StructuredData.WriteJson(legacy)).Contains("ReflectionEnvironment"),"Legacy reflection default changed JSON/bake identity.");
        legacy.ReflectionEnvironment=ReflectionPreset.SyntheticStudio;
        Check(StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(legacy)).ReflectionEnvironment==ReflectionPreset.SyntheticStudio,"Reflection setting did not round-trip.");
        var key=Engine.Editor.Editor.BakeSnapshot.Capture(legacy,p=>p).Key();
        var editor=new Engine.Editor.Editor.LevelEditorController();editor.LoadFromMemory("reflection-settings.json",legacy);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(Engine.Editor.Editor.LevelEditorController).GetMethod("BeginEdit",flags)!.Invoke(editor,null);
        editor.LevelFile.ReflectionEnvironment=ReflectionPreset.None;
        typeof(Engine.Editor.Editor.LevelEditorController).GetMethod("EndEditIfAny",flags)!.Invoke(editor,null);
        Check(Engine.Editor.Editor.BakeSnapshot.Capture(editor.LevelFile,p=>p).Key()==key,"Specular preset invalidated diffuse bake key.");
        Check(editor.Undo() && editor.LevelFile.ReflectionEnvironment==ReflectionPreset.SyntheticStudio,"Reflection undo failed.");
        Check(editor.Redo() && editor.LevelFile.ReflectionEnvironment==ReflectionPreset.None,"Reflection redo failed.");
        string path=Path.Combine(Path.GetTempPath(),"hs2-reflections-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            editor.LevelFile.ReflectionEnvironment=ReflectionPreset.SyntheticStudio;LevelIO.Save(path,editor.LevelFile);
            Check(LevelIO.Load(path).ReflectionEnvironment==ReflectionPreset.SyntheticStudio,"RE:Dox reflection persistence failed.");
        }
        finally {File.Delete(path);}
        byte[] file=File.ReadAllBytes(Path.Combine(root,"Engine.Render/Assets/SyntheticStudio.hs2env"));
        var studio=ReflectionEnvironmentData.Read(new MemoryStream(file));
        Check(studio.PayloadBytes==1114096 && studio.Size==128 && studio.MipCount==8,"Unexpected reflection payload budget.");
        var recooked=ReflectionCooker.Build(ReflectionCooker.Studio);
        Check(recooked.Lut.SequenceEqual(studio.Lut) && recooked.Faces.Zip(studio.Faces).All(p=>p.First.SequenceEqual(p.Second)),"Reflection cook is not deterministic.");
        foreach(var bad in new[]{file[..^1],file.Concat(new byte[]{0}).ToArray(),Mutate(file,4),Mutate(file,100)})
        {
            bool rejected=false; try { ReflectionEnvironmentData.Read(new MemoryStream(bad)); } catch(Exception ex) when(ex is IOException or InvalidDataException) {rejected=true;}
            Check(rejected,"Corrupt/versioned reflection accepted.");
        }
        // Independent hemispherical integration, not the cooker's GGX importance distribution.
        foreach(float rough in new[]{.35f,.65f,1f}) foreach(float nv in new[]{.15f,.5f,.95f})
        {
            var expected=IntegrateUniform(nv,rough);
            var actual=ReflectionCooker.Integrate(nv,rough,16384);
            Check(Vector2.Distance(actual,expected)<.012f,$"BRDF integral mismatch at {nv}/{rough}: {actual} vs {expected}");
        }
        bool energy=true;
        for(int i=0;i<studio.Lut.Length;i+=4)
        {
            float a=HalfAt(studio.Lut,i), b=HalfAt(studio.Lut,i+2);
            energy &= a+b<=1.025f;
        }
        Check(energy,"Single-scatter white furnace gains energy.");
        Gpu(studio);
        Console.WriteLine($"PASS: {_checks} reflection checks: version/checksum/bounds, deterministic GGX, independent LUT integral, energy, GPU directions/face rendering, roughness, fallback, diagnostics and uploads.");
    }

    private static byte[] Mutate(byte[] bytes,int offset) {var result=bytes.ToArray(); result[offset]^=0x7f;return result;}
    private static float HalfAt(byte[] b,int i)=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(b,i));
    private static Vector3 Texel(byte[] b,int i)=>new(HalfAt(b,i*8),HalfAt(b,i*8+2),HalfAt(b,i*8+4));
    private static Vector2 IntegrateUniform(double nv,double rough)
    {
        double a2=Math.Pow(rough,4), sx=0,sy=0;
        const int elevations=256, azimuths=512;
        var v=new Vector3((float)Math.Sqrt(1-nv*nv),0,(float)nv);
        for(int y=0;y<elevations;y++) for(int x=0;x<azimuths;x++)
        {
            double nl=(y+.5)/elevations, phi=2*Math.PI*(x+.5)/azimuths;
            var l=new Vector3((float)(Math.Sqrt(1-nl*nl)*Math.Cos(phi)),(float)(Math.Sqrt(1-nl*nl)*Math.Sin(phi)),(float)nl);
            var h=Vector3.Normalize(v+l); double nh=h.Z, vh=Vector3.Dot(v,h);
            double d=a2/(Math.PI*Math.Pow(1-nh*nh+nh*nh*a2,2));
            double visibility=.5/(nl*Math.Sqrt(nv*nv*(1-a2)+a2)+nv*Math.Sqrt(nl*nl*(1-a2)+a2));
            double f=Math.Pow(1-vh,5), weight=d*visibility*nl*2*Math.PI/(elevations*azimuths);
            sx+=(1-f)*weight; sy+=f*weight;
        }
        return new((float)sx,(float)sy);
    }

    private static void Gpu(ReflectionEnvironmentData studio)
    {
        using var host=new EngineHost(640,480,"Reflection HDR validation");
        var renderer=host.Context.Renderer; var device=renderer.GraphicsDevice; var factory=device.ResourceFactory;
        using var world=new BasicWorldRenderer(device,renderer.WorldOutputDescription,"Shaders");
        world.LightingModel=SurfaceLightingModel.Pbr; world.AmbientStrength=world.DirectionalStrength=0;
        uint width=renderer.WorldFramebuffer.Width,height=renderer.WorldFramebuffer.Height;
        using var resolved=factory.CreateTexture(TextureDescription.Texture2D(width,height,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.RenderTarget));
        using var staging=factory.CreateTexture(TextureDescription.Texture2D(width,height,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Staging));
        LoadedModelPart Part(float rough,float metal=1,Vector3 emission=default)
        { var q=PbrMaterialChecks.Quad();return new(q.Positions,q.Normals,null,q.Indices,Vector4.One,null,null,metal,rough){EmissiveFactor=emission}; }
        Vector3 Frame(Vector3 direction,float rough=0,float metal=1,WorldPointLight[]? lights=null,ProbeIrradiance probe=default,Vector3 emission=default,bool backface=false)
        {
            direction=Vector3.Normalize(direction);var eye=direction*3;
            var up=MathF.Abs(direction.Y)<.99f ? Vector3.UnitY : Vector3.UnitZ;
            var transform=Matrix4x4.CreateWorld(Vector3.Zero,-direction,up);
            var vp=Matrix4x4.CreateLookAt(eye,Vector3.Zero,up)*Matrix4x4.CreateOrthographic(4,3,.05f,100);
            var part=Part(rough,metal,emission);
            if(backface) Array.Fill(part.Normals!,-Vector3.UnitZ);
            using var model=world.CreateRenderModel(new([part]));
            renderer.BeginFrame();world.BeginFrame();world.UpdateCamera(vp,eye);world.UpdatePointLights(lights??[],eye);
            using(world.BatchObjectUploads()) world.DrawModel(renderer.CommandList,model,transform,Vector4.One,null,probe:probe);
            renderer.CommandList.ResolveTexture(renderer.WorldFramebuffer.ColorTargets[0].Target,resolved);
            renderer.CommandList.CopyTexture(resolved,staging);renderer.EndFrame();device.WaitForIdle();
            var map=device.Map<ushort>(staging,MapMode.Read);
            try
            {
                for(int y=0;y<height;y++) for(int x=0;x<width*4;x++)
                    if(!Half.IsFinite(BitConverter.UInt16BitsToHalf(map[x,y]))) throw new InvalidDataException("Nonfinite reflection pixel.");
                int cx=(int)(width/2*4),cy=(int)(height/2);
                Check(world.ObjectUploadCount==1,"Reflection broke object batching.");
                return new((float)BitConverter.UInt16BitsToHalf(map[cx,cy]),(float)BitConverter.UInt16BitsToHalf(map[cx+1,cy]),(float)BitConverter.UInt16BitsToHalf(map[cx+2,cy]));
            }
            finally {device.Unmap(staging);}
        }
        Near(Frame(Vector3.UnitZ),Vector3.Zero,"Missing environment is zero-energy");
        var directional=ReflectionCooker.Build(d=>(d+Vector3.One)*.5f,32,1,64,1);
        Half[] identity=[(Half)1,(Half)0]; directional=directional with {Lut=MemoryMarshal.AsBytes(identity.AsSpan()).ToArray()};
        using var directionGpu=world.CreateReflectionEnvironment(directional);
        world.Reflection=directionGpu;
        directionGpu.UploadNext();
        Near(Frame(Vector3.UnitZ),Vector3.Zero,"Incomplete cubemap became visible");
        while(!directionGpu.IsReady) directionGpu.UploadNext();
        foreach(var direction in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ})
        {
            Near(Frame(direction),(direction+Vector3.One)*.5f,"Axis reflection orientation");
            // Asymmetric off-axis directions catch face flips/rotations that axis colours cannot.
            var offAxis=Vector3.Normalize(direction+new Vector3(.19f,.27f,.13f));
            Near(Frame(offAxis),(offAxis+Vector3.One)*.5f,"Off-axis reflection orientation");
        }
        Near(Frame(Vector3.UnitZ,metal:0),new Vector3(.5f,.5f,1)*.04f,"Dielectric F0");
        Near(Frame(Vector3.UnitZ,backface:true),Vector3.Zero,"Backface gained specular environment energy");
        world.Reflection=null;Near(Frame(Vector3.UnitZ),Vector3.Zero,"Disabled reflection persists");
        var whiteData=ReflectionCooker.Build(_=>Vector3.One,2,1,64,1) with {LutSize=studio.LutSize,Lut=studio.Lut};
        using var whiteGpu=world.CreateReflectionEnvironment(whiteData);
        while(!whiteGpu.IsReady) whiteGpu.UploadNext();world.Reflection=whiteGpu;
        foreach(float r in new[]{0,.3f,.7f,1})
        {
            var white=Frame(Vector3.UnitZ,r);
            Check(white.X is > 0 and <=1.025f && white.Y<=1.025f && white.Z<=1.025f,"GPU furnace energy invalid.");
        }
        using var studioGpu=world.CreateReflectionEnvironment(studio);
        while(!studioGpu.IsReady) studioGpu.UploadNext(); world.Reflection=studioGpu;
        var bright=Vector3.Normalize(new Vector3(-.65f,.55f,.5f));
        var sharp=Frame(bright,0);var rough=Frame(bright,1);
        Check(sharp.Length()>rough.Length()*5,"Filtered roughness did not lower the highlight peak.");
        var shoulder=Vector3.Normalize(bright+new Vector3(.6f,-.2f,.2f));
        var shoulderSharp=Frame(shoulder,0);var shoulderRough=Frame(shoulder,.6f);
        Check(shoulderRough.Length()>shoulderSharp.Length()*1.15f,"GGX highlight did not broaden.");
        var probe=ProbeIrradiance.Pack(Enumerable.Repeat(new Vector3(.2f,.1f,.3f),6).ToArray());
        WorldPointLight[] lights=[new(new(0,0,2),Vector3.One,2,8)];var glow=new Vector3(.3f,.2f,.1f);
        var contributions=new List<Vector3>();
        foreach(var diagnostic in new[]{LightingDiagnostic.Direct,LightingDiagnostic.DiffuseIndirect,LightingDiagnostic.SpecularIndirect,LightingDiagnostic.Emission})
        {world.Diagnostic=diagnostic;contributions.Add(Frame(Vector3.UnitZ,.5f,.3f,lights,probe,glow));}
        world.Diagnostic=LightingDiagnostic.Full;
        Near(Frame(Vector3.UnitZ,.5f,.3f,lights,probe,glow),contributions.Aggregate(Vector3.Zero,(a,b)=>a+b),"Reflection diagnostic sum",.005f);
        world.ReflectionPreviewEnabled=false;
        Near(Frame(Vector3.UnitZ,.5f,.3f,lights,probe,glow),contributions[0]+contributions[1]+contributions[3],"Direct-only preview suppresses reflections",.005f);
        world.ReflectionPreviewEnabled=true;
        world.Reflection=null;
        Near(Frame(Vector3.UnitZ,.5f,.3f,lights,probe,glow),contributions[0]+contributions[1]+contributions[3],"Disable preserves direct/diffuse/emission",.005f);
        world.LightingModel=SurfaceLightingModel.Legacy;var old=Frame(Vector3.UnitZ,.5f,0,lights);
        world.Reflection=studioGpu;Near(Frame(Vector3.UnitZ,.5f,0,lights),old,"Legacy ignores environment",.0001f);
        world.Reflection=null; world.LightingModel=SurfaceLightingModel.Pbr;world.EnvironmentPreset=ReflectionPreset.SyntheticStudio;
        world.UploadBudgetMilliseconds=0;
        for(int i=0;i<5;i++){Frame(Vector3.UnitZ);Thread.Sleep(10);}
        Check(!world.ReflectionReady,"Zero upload budget ignored.");
        world.UploadBudgetMilliseconds=2;
        for(int i=0;i<60 && !world.ReflectionReady;i++){Frame(Vector3.UnitZ);Thread.Sleep(5);}
        Check(world.ReflectionReady && world.ReflectionPendingCpuBytes==0 && world.ReflectionResidentBytes<1.1*1048576,"Embedded environment failed readiness/memory contract: "+world.ReflectionStatus);
        FaceTargets(device,renderer);
        device.WaitForIdle();
    }

    private static void FaceTargets(GraphicsDevice device,Renderer renderer)
    {
        var factory=device.ResourceFactory;
        using var cube=factory.CreateTexture(TextureDescription.Texture2D(32,32,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Cubemap|TextureUsage.RenderTarget|TextureUsage.Sampled));
        using var staging=factory.CreateTexture(TextureDescription.Texture2D(32,32,1,6,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Staging));
        var faces=Enumerable.Range(0,6).Select(i=>factory.CreateFramebuffer(new FramebufferDescription(null,new[]{new FramebufferAttachmentDescription(cube,(uint)i)}))).ToArray();
        try
        {
            using var world=new BasicWorldRenderer(device,faces[0].OutputDescription,"Shaders");
            for(int face=0;face<6;face++)
            {
                Vector3 direction=ReflectionCooker.Direction(face,0,0),up=MathF.Abs(direction.Y)<.99f?Vector3.UnitY:Vector3.UnitZ;
                renderer.BeginFrame();var cl=renderer.CommandList;cl.SetFramebuffer(faces[face]);cl.SetFullViewports();cl.ClearColorTarget(0,RgbaFloat.Black);
                world.BeginFrame();world.UpdateCamera(Matrix4x4.CreateLookAt(Vector3.Zero,direction,up)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/2,1,.1f,10));
                using(world.BatchObjectUploads()) world.DrawBox(cl,Matrix4x4.CreateTranslation(direction*3),new Vector4((direction+Vector3.One)*.5f,1));
                renderer.EndFrame();device.WaitForIdle();
            }
            renderer.BeginFrame();renderer.CommandList.CopyTexture(cube,staging);renderer.EndFrame();device.WaitForIdle();
            for(uint face=0;face<6;face++)
            {
                var map=device.Map<ushort>(staging,MapMode.Read,face);
                try
                {
                    var colour=(ReflectionCooker.Direction((int)face,0,0)+Vector3.One)*.5f;
                    float Linear(float c)=>c<=.04045f ? c/12.92f : MathF.Pow((c+.055f)/1.055f,2.4f);
                    Near(new((float)BitConverter.UInt16BitsToHalf(map[64,16]),(float)BitConverter.UInt16BitsToHalf(map[65,16]),(float)BitConverter.UInt16BitsToHalf(map[66,16])),
                        new(Linear(colour.X),Linear(colour.Y),Linear(colour.Z)),"Cubemap face render target");
                }
                finally {device.Unmap(staging,face);}
            }
        }
        finally {foreach(var face in faces) face.Dispose();}
    }
}

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Hosting;
using ImGuiNET;
using Veldrid;

internal static class IndirectBakeChecks
{
    private static int _checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); _checks++; }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or OperationCanceledException or AggregateException) { _checks++; return; }
        throw new InvalidDataException(message);
    }
    public static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"HS2-indirect-qa-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string modelPath=Path.Combine(directory,"cube.glb"); WriteCube(modelPath);
            string levelPath=Path.Combine(directory,"fixture.json"), bakePath=LightmapFile.PathFor(levelPath);
            var level=Fixture("cube.glb");
            BakeSnapshot Capture()=>BakeSnapshot.Capture(level,p=>Path.Combine(directory,p));
            var source=GlbModelLoader.Load(modelPath);
            var positions=source.Parts[0].Positions.ToArray(); var uv=source.Parts[0].TexCoords!.ToArray();
            var snapshot=Capture(); var data=IndirectLightBaker.Bake(snapshot);
            Check(data.Records.Count==8 && data.Records.Sum(r=>r.PartTriangles.Sum())==96,"Static receiver coverage changed.");
            Check(source.Parts[0].Positions.SequenceEqual(positions)&&source.Parts[0].TexCoords!.SequenceEqual(uv),"Baking altered source geometry/material UVs.");
            Check(data.Pixels.SequenceEqual(IndirectLightBaker.Bake(snapshot).Pixels),"Bake samples are nondeterministic.");
            var half=MemoryMarshal.Cast<byte,Half>(data.Pixels).ToArray();
            float Energy(string id)
            {
                var record=data.Records.Single(r=>r.EntityId==id); int columns=data.Size/(data.Texels+4); float sum=0;
                for(int triangle=record.FirstTriangle;triangle<record.FirstTriangle+record.PartTriangles.Sum();triangle++)
                    for(int y=0;y<data.Texels;y++)for(int x=0;x<data.Texels-y;x++)
                    {
                        int px=triangle%columns*(data.Texels+4)+x+2,py=triangle/columns*(data.Texels+4)+y+2;
                        sum+=(float)half[(py*data.Size+px)*4];
                    }
                return sum;
            }
            float lit=Energy("left-floor"),dark=Energy("right-floor");
            Console.WriteLine($"[BakeOcclusion] lit floor energy {lit:F5}; sealed dark floor {dark:F5}.");
            Check(lit>.1f,"No indirect energy reached the lit compartment.");
            Check(dark<lit*.001f,"Indirect light leaked through the sealed partition.");
            var cells=new HashSet<(int,int)>();
            for(int triangle=0;triangle<96;triangle++)
            {
                var a=LightmapCharts.Corner(triangle,0,data.Texels,data.Size);
                var b=LightmapCharts.Corner(triangle,1,data.Texels,data.Size);
                var c=LightmapCharts.Corner(triangle,2,data.Texels,data.Size);
                Check(a.X>0&&a.Y>0&&b.X<1&&c.Y<1&&b.X>a.X&&c.Y>a.Y,"Invalid padded chart.");
                Check(cells.Add(((int)(a.X*data.Size)/(data.Texels+4),(int)(a.Y*data.Size)/(data.Texels+4))),"Charts overlap.");
            }
            LightmapFile.Save(bakePath,data); byte[] valid=File.ReadAllBytes(bakePath);
            var loaded=LightmapFile.Load(bakePath,data.Key);
            Check(loaded!=null&&loaded.Pixels.SequenceEqual(data.Pixels)&&loaded.Records[3].Transform==data.Records[3].Transform,"Lossless HDR/placement round-trip failed.");
            Check(LightmapFile.Load(bakePath,new string('0',64))==null,"Stale bake key accepted.");
            foreach(int offset in new[]{0,4,8,12,48,valid.Length-1})
            {
                var broken=valid.ToArray(); broken[offset]^=0x40; File.WriteAllBytes(bakePath,broken);
                Check(LightmapFile.Load(bakePath,data.Key)==null,"Corrupted bake accepted.");
            }
            File.WriteAllBytes(bakePath,valid[..20]); Check(LightmapFile.Load(bakePath,data.Key)==null,"Truncated bake accepted.");
            File.WriteAllBytes(bakePath,valid);
            using(var cancel=new CancellationTokenSource())
            {
                cancel.Cancel(); Reject(()=>LightmapFile.Save(bakePath,data,cancel.Token),"Cancelled write completed.");
                Check(File.ReadAllBytes(bakePath).SequenceEqual(valid),"Cancelled write replaced the prior valid bake.");
            }
            using(var cancel=new CancellationTokenSource())
            {
                Reject(()=>IndirectLightBaker.Bake(snapshot,p=>cancel.Cancel(),cancel.Token),"Running bake ignored cancellation.");
                Check(File.ReadAllBytes(bakePath).SequenceEqual(valid),"Cancelled baking changed the previous file.");
            }
            Check(Directory.GetFiles(directory,"*.tmp").Length==0,"Temporary bake files leaked.");
            level.Entities[0].LocalRotationEulerDeg=new(0,30,0);
            Check(Capture().Key()!=data.Key,"Rotation did not invalidate the bake.");
            level=Fixture("cube.glb"); level.Entities[0].LocalScale=new(1,2,1);
            Check(Capture().Key()!=data.Key,"Non-uniform scale did not invalidate the bake.");
            level=Fixture("cube.glb"); level.Entities[^1].Intensity+=1;
            Check(Capture().Key()!=data.Key,"Changed light did not invalidate the bake.");
            level=Fixture("cube.glb"); var bytes=File.ReadAllBytes(modelPath); File.WriteAllBytes(modelPath,bytes.Concat(new byte[]{0}).ToArray());
            Check(Capture().Key()!=data.Key,"Changed source model did not invalidate the bake."); File.WriteAllBytes(modelPath,bytes);
            level.Entities[0].CanPickUp=true; Reject(()=>IndirectLightBaker.Bake(Capture()),"Movable receiver baked.");
            level=Fixture("cube.glb"); level.Entities[0].Damageable=true; Reject(()=>IndirectLightBaker.Bake(Capture()),"Destructible receiver baked.");
            level=Fixture("cube.glb"); level.Entities[0].Interaction=new(){Kind="LockedDoor"}; Reject(()=>IndirectLightBaker.Bake(Capture()),"Door receiver baked.");
            level=Fixture("cube.glb"); level.Entities[^1].FlickerAmount=.1f; Reject(()=>IndirectLightBaker.Bake(Capture()),"Flickering light baked.");
            level=Fixture("cube.glb"); level.Entities.Add(new(){Id="switch",Interaction=new(){Kind="LightSwitch",Targets=["light"]}});
            Reject(()=>IndirectLightBaker.Bake(Capture()),"Switch-controlled light baked.");
            level=Fixture("cube.glb"); level.Entities[0].ParentId="parent";
            level.Entities.Add(new(){Id="parent",Type=EntityTypes.Prop,Scripts=[new(){Type="MovingPlatform"}]});
            Reject(()=>IndirectLightBaker.Bake(Capture()),"Moving ancestor baked.");
            level=Fixture("cube.glb"); LevelIO.Save(levelPath,level);
            var saved=LevelIO.Load(levelPath);
            Check(saved.Bake.Enabled&&saved.Entities[0].StaticLighting&&saved.Entities[^1].BakeIndirect,"RE:Dox lost bake metadata.");
            var old=StructuredData.ReadJson<LevelFile>("{\"Entities\":[]}"u8.ToArray());
            Check(!old.Bake.Enabled,"Legacy levels opted into baking.");
            var editor=new LevelEditorController();editor.LoadFromMemory(levelPath,saved);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(LevelEditorController).GetMethod("BeginEdit",flags)!.Invoke(editor,null);
            editor.LevelFile.Entities[0].StaticLighting=false;
            typeof(LevelEditorController).GetMethod("EndEditIfAny",flags)!.Invoke(editor,null);
            Check(editor.Undo()&&editor.LevelFile.Entities[0].StaticLighting,"Undo lost static-lighting metadata.");
            Check(editor.Redo()&&!editor.LevelFile.Entities[0].StaticLighting,"Redo lost static-lighting metadata.");
            CheckBakeUi(directory, levelPath, valid);
            CheckProbes(directory, levelPath);
            Gpu(source);
            Console.WriteLine($"PASS: {_checks} indirect-bake checks: padded charts, source UV preservation, deterministic one-bounce occlusion, HDR/LZ4 validation, stale/cancel fallback, metadata/undo and real GPU sampling.");
        }
        finally { Directory.Delete(directory,true); }
    }

    private static unsafe void CheckBakeUi(string directory, string levelPath, byte[] previousBake)
    {
        IntPtr context = ImGui.CreateContext();
        using var session = new LevelBakeSession();
        Task? running = null;
        try
        {
            var io = ImGui.GetIO(); io.NativePtr->IniFilename = null;
            io.DisplaySize = new Vector2(800, 600); io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            var editor = new LevelEditorController(); var level = Fixture("cube.glb");
            level.Bake.TexelsPerTriangle = 32; level.Bake.Samples = 256;
            editor.LoadFromMemory(levelPath, level);
            int saves = 0; editor.SaveActionOverride = () => saves++;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var draw = typeof(LevelEditorController).GetMethod("DrawBakeActions", flags)!;
            Func<string, string> resolve = p => Path.Combine(directory, p);
            void Frame(Vector2 pointer, bool down, bool enabled = true)
            {
                io.MousePos = pointer; io.MouseDown[0] = down; ImGui.NewFrame();
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(800,600));
                ImGui.Begin("Bake QA", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);
                ImGui.SetCursorScreenPos(new Vector2(40,40));
                draw.Invoke(editor, [session, resolve, enabled]); ImGui.End(); ImGui.Render();
            }
            Vector2 bake = new(80,48);
            void Click(Vector2 p, bool enabled = true) { Frame(p,false,enabled); Frame(p,true,enabled); Frame(p,false,enabled); }
            Frame(bake,false); Click(bake,false);
            Vector2 cancel = new(80,48+ImGui.GetFrameHeightWithSpacing()*2);
            Check(!session.Busy && saves==0, "Disabled Bake button started work.");
            Click(bake);
            Check(session.Baking && saves==1, "Bake button did not save and start background work.");
            running = (Task)typeof(LevelBakeSession).GetField("_job", flags)!.GetValue(session)!;
            Click(bake); Check(saves==1, "Busy Bake button started duplicate work.");
            Click(cancel); Check(!session.Busy && session.Status=="Bake cancelled", "Cancel button failed.");
            try { running.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Check(File.ReadAllBytes(LightmapFile.PathFor(levelPath)).SequenceEqual(previousBake), "UI cancellation replaced the saved bake.");
            editor.SaveActionOverride = () => throw new IOException("QA save failure");
            Click(bake);
            Check(!session.Busy && session.Status.Contains("Cannot save before baking"), "Failed save still started baking.");
            Console.WriteLine("PASS: ImGui Bake/Cancel clicks, disabled/busy states and save-failure handling.");
        }
        finally
        {
            session.Cancel();
            if (running != null) { try { running.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } }
            ImGui.DestroyContext(context);
        }
    }

    private static LevelFile Fixture(string model)
    {
        var level=new LevelFile{UsePointLights=true,AmbientLight=0,DirectionalLight=0,Bake=new(){Enabled=true,TexelsPerTriangle=4,Samples=32,RayDistance=12}};
        void Box(string id,Vector3 p,Vector3 s)=>level.Entities.Add(new(){Id=id,Name=id,Type=EntityTypes.RigidBody,MotionType=MotionType.Static,
            StaticLighting=true,MeshPath=model,Color=Vector4.One,LocalPosition=p,Size=s});
        Box("left-floor",new(-1,-.1f,0),new(2,.2f,4));Box("right-floor",new(1,-.1f,0),new(2,.2f,4));
        Box("ceiling",new(0,3.1f,0),new(4.4f,.2f,4.4f));
        Box("west",new(-2.1f,1.5f,0),new(.2f,3,4.4f));Box("east",new(2.1f,1.5f,0),new(.2f,3,4.4f));
        Box("north",new(0,1.5f,2.1f),new(4.4f,3,.2f));Box("south",new(0,1.5f,-2.1f),new(4.4f,3,.2f));
        Box("partition",new(0,1.5f,0),new(.1f,3,4.4f));
        level.Entities.Add(new(){Id="light",Name="light",Type=EntityTypes.PointLight,BakeIndirect=true,LocalPosition=new(-1,1.5f,0),Intensity=4,Range=4});
        return level;
    }

    private static void CheckProbes(string directory,string levelPath)
    {
        var level=Fixture("cube.glb"); level.Bake.Probes=new(){Spacing=1};
        var snapshot=BakeSnapshot.Capture(level,p=>Path.Combine(directory,p));
        var data=IndirectLightBaker.Bake(snapshot);
        Check(data.Probes is { } volume && volume.Valid.Any(v=>v),"Probe bake has no usable samples.");
        Check(data.PreparedProbes!=null,"Probe acceleration was not prepared on the baking worker.");
        var left=data.PreparedProbes!.Sample(new(-1,1,0));var right=data.PreparedProbes.Sample(new(1,1,0));
        Check(left.HasEnergy && !right.HasEnergy,"Probe bounce leaked into the sealed dark compartment.");
        var repeated=IndirectLightBaker.Bake(snapshot);
        Check(data.Probes!.Irradiance.SequenceEqual(repeated.Probes!.Irradiance),"Probe bake is nondeterministic.");
        string path=LightmapFile.PathFor(levelPath);
        LightmapFile.Save(path,data);var loaded=LightmapFile.Load(path,data.Key);
        Check(loaded?.PreparedProbes!=null && loaded.Probes!.Irradiance.SequenceEqual(data.Probes.Irradiance),"Probe persistence/worker preparation failed.");
        LevelIO.Save(levelPath,level);Check(LevelIO.Load(levelPath).Bake.Probes?.Spacing==1,"Probe settings failed RE:Dox round-trip.");
        data.Probes.Irradiance[0]=new(float.NaN,0,0);
        Check(!data.Probes.IsValid(),"Non-finite probe radiance accepted.");
        data.Probes.Irradiance[0]=new(-1,0,0);LightmapFile.Save(path,data);
        Check(LightmapFile.Load(path,data.Key)==null,"Invalid probe radiance accepted on load.");

        var simple=new LightProbeVolume{Origin=new(-1,0,0),Spacing=2,Width=2,Height=1,Depth=1,Valid=[true,true],
            Irradiance=Enumerable.Range(0,12).Select(i=>i<6?Vector3.One:Vector3.Zero).ToArray(),
            OccluderVertices=[new(8,-4,-4),new(8,4,-4),new(8,4,4)]};
        var sampler=new LightProbeSampler(simple);
        for(int i=0;i<=20;i++)
        {
            float x=-1+i*.1f;
            Check(MathF.Abs(sampler.Sample(new(x,0,0)).Evaluate(Vector3.UnitY).X-(1-x)*.5f)<1e-5f,"Probe interpolation jumped between grid samples.");
        }
        Check(!sampler.Sample(new(5,0,0)).HasEnergy,"Probe volume extrapolated outside its bounds.");
        simple.OccluderVertices=[new(0,-4,-4),new(0,4,-4),new(0,4,4),new(0,-4,-4),new(0,4,4),new(0,-4,4)];
        sampler=new LightProbeSampler(simple);
        Check(sampler.Sample(new(-.01f,0,0)).Evaluate(Vector3.UnitY).X>.99f && !sampler.Sample(new(.01f,0,0)).HasEnergy,
            "Probe interpolation blended through a thin wall.");
        simple.Valid[0]=false;Check(!sampler.Sample(new(-.01f,0,0)).HasEnergy,"Invalid probe was used.");
        simple.Spacing=float.PositiveInfinity;Check(!simple.IsValid(),"Invalid probe spacing accepted.");
        Console.WriteLine($"PASS: {data.Probes.Valid.Count(v=>v)} usable probes; deterministic directional bounce, trilinear motion, thin-wall visibility, persistence and invalid-data fallback.");
    }

    private static void Gpu(LoadedModel source)
    {
        foreach(var part in source.Parts)part.BaseColorTexture=new("bake-qa-white",1,1,TextureSemantic.Color,false,[[255,255,255,255]]);
        using var host=new EngineHost(640,480,"Indirect Lighting QA");var renderer=host.Context.Renderer;var device=renderer.GraphicsDevice;var factory=device.ResourceFactory;
        using var world=new BasicWorldRenderer(device,renderer.WorldOutputDescription,"Shaders"){AmbientStrength=0,DirectionalStrength=0};
        using var model=world.CreateRenderModel(source);
        var half=new Half[32*32*4];for(int i=0;i<half.Length;i++)half[i]=(Half)(i%4==3?1:.5f);
        using var atlas=world.CreateLightmapAtlas(32,MemoryMarshal.AsBytes(half.AsSpan()).ToArray());
        using var binding=new LightmapBinding(source,atlas,0,4,32);while(!binding.IsReady)binding.UploadNext(device);
        int width=(int)renderer.WorldFramebuffer.Width,height=(int)renderer.WorldFramebuffer.Height;
        var format=device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format;
        using var color=factory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,format,TextureUsage.RenderTarget));
        using var depth=factory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,PixelFormat.R32_Float,TextureUsage.DepthStencil));
        using var staging=factory.CreateTexture(TextureDescription.Texture2D((uint)width,(uint)height,1,1,format,TextureUsage.Staging));
        using var framebuffer=factory.CreateFramebuffer(new FramebufferDescription(depth,color));
        var eye=new Vector3(0,0,2);
        var vp=Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,width/(float)height,.05f,50);
        uint[] Frame(bool baked,bool split=false,ProbeIrradiance probe=default,ModelEmission? emission=null)
        {
            renderer.BeginFrame();world.BeginFrame();world.UpdateCamera(vp,eye);world.UpdatePointLights([],eye);
            world.RenderShadows(renderer,[],new Viewport(0,0,width,height,0,1));
            using(world.BatchObjectUploads())
            {
                var matrix=split?Matrix4x4.CreateScale(.6f)*Matrix4x4.CreateTranslation(-.6f,0,0):Matrix4x4.Identity;
                world.DrawModel(renderer.CommandList,model,matrix,Vector4.One,null,emission,baked?binding:null,probe);
                if(split)world.DrawModel(renderer.CommandList,model,Matrix4x4.CreateScale(.6f)*Matrix4x4.CreateTranslation(.6f,0,0),Vector4.One);
            }
            renderer.ResolveWorldToSwapchain(framebuffer);renderer.CommandList.CopyTexture(color,staging);renderer.EndFrame();device.WaitForIdle();
            var mapped=device.Map<uint>(staging,MapMode.Read);
            try{var pixels=new uint[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=mapped[x,y];return pixels;}
            finally{device.Unmap(staging);}
        }
        var off=Frame(false);var on=Frame(true);uint centre=on[height/2*width+width/2];
        bool bgra=format is PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        int Expected(float c){float linear=c*.5f,mapped=linear/(1+linear);return (int)MathF.Round((1.055f*MathF.Pow(mapped,1/2.4f)-.055f)*255);}
        Check(Math.Abs((int)((centre>>(bgra?16:0))&255)-Expected(.65f))<=2&&Math.Abs((int)((centre>>8)&255)-Expected(.35f))<=2,
            "Lightmap did not sample linear HDR irradiance before albedo/tone mapping.");
        Check(Frame(false).SequenceEqual(off),"Lightmap state leaked into unbaked draws.");
        var split=Frame(true,true);int Sum(uint p)=>(int)((p&255)+((p>>8)&255)+((p>>16)&255));
        Check(Enumerable.Range(0,split.Length).Count(i=>i%width<width/2&&Sum(split[i])>200)>1000&&
            Enumerable.Range(0,split.Length).All(i=>i%width<width/2||Sum(split[i])<10),"Shared model instances inherited each other's lightmaps.");
        Vector3[] axes=[Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];
        for(int i=0;i<6;i++)
        {
            var values=new Vector3[6];values[i]=new(.5f);
            var probe=ProbeIrradiance.Pack(values);
            Check(probe.Evaluate(axes[i])==new Vector3(.5f) && probe.Evaluate(-axes[i])==Vector3.Zero,"Probe packing changed directional channels.");
            eye=axes[i]*2;vp=Matrix4x4.CreateLookAt(eye,Vector3.Zero,MathF.Abs(axes[i].Y)>.9f?Vector3.UnitZ:Vector3.UnitY)*
                Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,width/(float)height,.05f,50);
            uint pixel=Frame(false,probe:probe)[height/2*width+width/2];
            Check(Math.Abs((int)((pixel>>(bgra?16:0))&255)-Expected(.65f))<=2,"Directional probe GPU sampling mismatch.");
        }
        eye=new(0,0,2);vp=Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,width/(float)height,.05f,50);
        var uniform=ProbeIrradiance.Pack(Enumerable.Repeat(new Vector3(.5f),6).ToArray());
        var probeOnly=Frame(false,probe:uniform);
        world.AmbientStrength=1;world.DirectionalStrength=1;world.IndirectOnly=true;
        Check(probeOnly.SequenceEqual(Frame(false,probe:uniform,emission:new ModelEmission(10,Vector3.One))),"Indirect-only retained direct, ambient or emissive light.");
        world.IndirectOnly=false;world.AmbientStrength=0;world.DirectionalStrength=0;
        Check(Frame(false).SequenceEqual(off),"Probe state leaked into subsequent unbaked draws.");
        Check(Marshal.SizeOf(typeof(BasicWorldRenderer).GetNestedType("ObjectData",BindingFlags.NonPublic)!)==256,"Probes increased the object upload stride.");
        string output=Path.Combine(AppContext.BaseDirectory,"qa");Directory.CreateDirectory(output);
        RenderPipelineChecks.SavePng(on,width,height,Path.Combine(output,"indirect-linear-gpu.png"));
    }

    private static void WriteCube(string path)
    {
        var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<ushort>();
        void Face(Vector3 n,Vector3 u,Vector3 v)
        {
            int start=positions.Count;
            foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
            {positions.Add((n+u*p.X+v*p.Y)*.5f);normals.Add(n);uv.Add((p+Vector2.One)*.5f);}
            foreach(int i in new[]{0,1,2,0,2,3})indices.Add((ushort)(start+i));
        }
        Face(Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY);Face(-Vector3.UnitZ,-Vector3.UnitX,Vector3.UnitY);
        Face(Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY);Face(-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY);
        Face(Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ);Face(-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ);
        using var binary=new MemoryStream();using var writer=new BinaryWriter(binary);
        var views=new List<object>();var accessors=new List<object>();
        int Floats(float[] values,string type,int stride)
        {int start=(int)binary.Position;foreach(float v in values)writer.Write(v);views.Add(new{buffer=0,byteOffset=start,byteLength=values.Length*4});
            accessors.Add(new{bufferView=views.Count-1,componentType=5126,count=values.Length/stride,type});return accessors.Count-1;}
        int pos=Floats(positions.SelectMany(v=>new[]{v.X,v.Y,v.Z}).ToArray(),"VEC3",3);
        int normal=Floats(normals.SelectMany(v=>new[]{v.X,v.Y,v.Z}).ToArray(),"VEC3",3);
        int coords=Floats(uv.SelectMany(v=>new[]{v.X,v.Y}).ToArray(),"VEC2",2);
        views.Add(new{buffer=0,byteOffset=(int)binary.Position,byteLength=indices.Count*2});foreach(ushort i in indices)writer.Write(i);
        int index=accessors.Count;accessors.Add(new{bufferView=views.Count-1,componentType=5123,count=indices.Count,type="SCALAR"});
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(new{asset=new{version="2.0"},buffers=new[]{new{byteLength=(int)binary.Length}},bufferViews=views,accessors,
            materials=new[]{new{pbrMetallicRoughness=new{baseColorFactor=new[]{.65f,.35f,.2f,1},metallicFactor=0,roughnessFactor=1}}},
            meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=pos,NORMAL=normal,TEXCOORD_0=coords},indices=index,material=0}}}},
            nodes=new[]{new{mesh=0}},scenes=new[]{new{nodes=new[]{0}}},scene=0});
        int padding=(4-json.Length%4)%4;using var file=File.Create(path);using var output=new BinaryWriter(file);
        output.Write(0x46546C67u);output.Write(2u);output.Write(28+json.Length+padding+(int)binary.Length);
        output.Write(json.Length+padding);output.Write(0x4E4F534Au);output.Write(json);for(int i=0;i<padding;i++)output.Write((byte)' ');
        output.Write((int)binary.Length);output.Write(0x004E4942u);output.Write(binary.ToArray());
    }
}

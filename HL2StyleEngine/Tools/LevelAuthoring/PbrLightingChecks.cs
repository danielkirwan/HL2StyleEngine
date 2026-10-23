using System.Numerics;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class PbrLightingChecks
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if(!value) throw new InvalidDataException(message); _checks++; }

    public static void Run()
    {
        using var host = new EngineHost(640,480,"PBR HDR validation");
        var renderer = host.Context.Renderer; var device = renderer.GraphicsDevice;
        using var world = new BasicWorldRenderer(device,renderer.WorldOutputDescription,"Shaders");
        world.LightingModel = SurfaceLightingModel.Pbr; world.AmbientStrength = world.DirectionalStrength = 0;
        uint width = renderer.WorldFramebuffer.Width, height = renderer.WorldFramebuffer.Height;
        using var resolved = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width,height,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.RenderTarget));
        using var staging = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width,height,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Staging));
        var eye = new Vector3(0,0,3);
        var vp = Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreateOrthographic(4,3,.05f,100);
        var pixelPosition = new Vector3(2f/width,-1.5f/height,0);
        var lamp = new WorldPointLight(new(.9f,1.1f,2),Vector3.One,3,10);
        var texels = new Half[32*32*4];
        for(int i=0;i<texels.Length;i++) texels[i]=(Half)(i%4==3 ? 1 : new[]{.2f,.3f,.4f}[i%4]);
        using var atlas = world.CreateLightmapAtlas(32,System.Runtime.InteropServices.MemoryMarshal.AsBytes(texels.AsSpan()).ToArray());

        Vector3[] Frame(LoadedModelPart part, WorldPointLight[] lights, ProbeIrradiance probe = default, float sun = 0, bool useAtlas = false, Matrix4x4? transform = null)
        {
            using var model = world.CreateRenderModel(new([part]));
            using var binding = useAtlas ? new LightmapBinding(new([part]),atlas,0,4,32,pbr:true) : null;
            if(binding != null) while(!binding.IsReady) binding.UploadNext(device);
            renderer.BeginFrame(); world.BeginFrame(); world.DirectionalStrength = sun;
            world.UpdateCamera(vp,eye); world.UpdatePointLights(lights,eye);
            using(world.BatchObjectUploads()) world.DrawModel(renderer.CommandList,model,transform ?? Matrix4x4.Identity,Vector4.One, null, lightmap:binding,probe:probe);
            renderer.CommandList.ResolveTexture(renderer.WorldFramebuffer.ColorTargets[0].Target,resolved);
            renderer.CommandList.CopyTexture(resolved,staging);
            renderer.EndFrame(); device.WaitForIdle();
            var map = device.Map<ushort>(staging,MapMode.Read);
            try
            {
                var pixels = new Vector3[width*height];
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                    pixels[y*width+x] = new((float)BitConverter.UInt16BitsToHalf(map[x*4,y]),(float)BitConverter.UInt16BitsToHalf(map[x*4+1,y]),(float)BitConverter.UInt16BitsToHalf(map[x*4+2,y]));
                Check(pixels.All(p=>float.IsFinite(p.LengthSquared())),"Nonfinite HDR pixels.");
                Check(world.ObjectUploadCount==1,"PBR lost object upload batching.");
                return pixels;
            }
            finally { device.Unmap(staging); }
        }
        Vector3 Centre(Vector3[] pixels) => pixels[height/2*width+width/2];
        void Near(Vector3 actual, Vector3 expected, string label)
            => Check(Vector3.Distance(actual,expected) < .004f + expected.Length()*.012f,$"{label}: {actual} != {expected}");
        LoadedModelPart Part(float metal, float roughness, Vector3? normal = null, Vector2[]? uv = null,
            PreparedTexture? normalMap = null, Vector3 emission = default, PreparedTexture? colorMap = null, PreparedTexture? mr = null)
        {
            var q = PbrMaterialChecks.Quad();
            return new(q.Positions,Enumerable.Repeat(normal ?? Vector3.UnitZ,4).ToArray(),uv,q.Indices,q.Color,null,null,metal,roughness)
            {NormalTexture=normalMap,EmissiveFactor=emission,BaseColorTexture=colorMap,MaterialTexture=mr};
        }
        var baseColor = new Vector3(.6f,.3f,.1f);
        foreach(float metal in new[]{0f,.5f,1f}) foreach(float rough in new[]{0f,.15f,.4f,1f})
        {
            var actual = Centre(Frame(Part(metal,rough),[lamp]));
            var expected = Reference(baseColor,metal,rough,Vector3.UnitZ,Vector3.Normalize(eye-pixelPosition),Vector3.Normalize(lamp.Position-pixelPosition));
            expected *= lamp.Intensity*MathF.Pow(1-Vector3.Distance(lamp.Position,pixelPosition)/lamp.Range,2);
            Near(actual,expected,$"Point m={metal} r={rough}");
        }
        var grazing = Vector3.Normalize(new Vector3(.999f,0,.04f));
        var grazingLamp = lamp with {Position = new(2,0,.15f)};
        var grazingExpected = Reference(baseColor,0,.3f,grazing,Vector3.Normalize(eye-pixelPosition),Vector3.Normalize(grazingLamp.Position-pixelPosition)) *
            grazingLamp.Intensity*MathF.Pow(1-Vector3.Distance(grazingLamp.Position,pixelPosition)/grazingLamp.Range,2);
        Near(Centre(Frame(Part(0,.3f,grazing),[grazingLamp])),grazingExpected,"Grazing angle");
        var sunNormal = Vector3.Normalize(new Vector3(0,1,.35f));
        Near(Centre(Frame(Part(.3f,.5f,sunNormal),[],sun:1)),Reference(baseColor,.3f,.5f,sunNormal,Vector3.Normalize(eye-pixelPosition),Vector3.Normalize(new(-.35f,.65f,-.68f))),"Directional");
        var spot = lamp with {Direction = Vector3.Normalize(-lamp.Position), SpotAngleDegrees=80};
        Near(Centre(Frame(Part(0,.4f),[spot])),Centre(Frame(Part(0,.4f),[lamp])),"Spot centre");
        Near(Centre(Frame(Part(0,.4f),[spot with {Direction=Vector3.UnitZ}])),Vector3.Zero,"Spot outside cone");
        Near(Centre(Frame(Part(0,.4f),[lamp with {Intensity=0}])),Vector3.Zero,"Disabled light");
        Near(Centre(Frame(Part(0,.4f),[lamp with {Range=.1f}])),Vector3.Zero,"Outside range");
        Near(Centre(Frame(Part(0,.4f),[lamp with {Intensity=1.5f}])),Centre(Frame(Part(0,.4f),[lamp]))*.5f,"Flicker intensity");
        var a = Frame(Part(0,.25f),[lamp]); var b = Frame(Part(0,.25f),[lamp with {Position=new(-.9f,1.1f,2)}]);
        Check(a.Zip(b).Count(p=>Vector3.Distance(p.First,p.Second)>.03f)>1000,"Moving local light did not move dielectric highlights.");
        var peakLight = lamp with {Position=new(0,0,2),Intensity=1};
        var sharp = Frame(Part(1,.2f),[peakLight]); var broad = Frame(Part(1,.7f),[peakLight]);
        Check(Centre(sharp).X > Centre(broad).X*3,"Roughness did not reduce the highlight peak.");
        Check(broad.Count(p=>p.X>Centre(broad).X*.5f) > sharp.Count(p=>p.X>Centre(sharp).X*.5f)*3,"Roughness did not broaden the highlight.");
        var extreme = Frame(Part(1,0),[peakLight with {Intensity=100}]);
        Check(extreme.All(p=>p.X<=65504 && p.Y<=65504 && p.Z<=65504),"Extreme highlight exceeded finite RGBA16F range.");
        Check(extreme.Max(p=>p.X)>60000,"Extreme-highlight fixture did not reach the half-float limit.");

        Vector2[] uv = [Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY];
        var tangentMap = new PreparedTexture("qa-pbr-tangent",1,1,TextureSemantic.Normal,false,[[191,128,255,255]]);
        float nx=191/255f*2-1, ny=128/255f*2-1;
        var n = new Vector3(nx,ny,MathF.Sqrt(1-nx*nx-ny*ny));
        Near(Centre(Frame(Part(0,.4f,uv:uv,normalMap:tangentMap),[lamp])),Centre(Frame(Part(0,.4f,n),[lamp])),"Tangent normal");
        Near(Centre(Frame(Part(0,.4f,uv:uv.Select(p=>new Vector2(1-p.X,p.Y)).ToArray(),normalMap:tangentMap),[lamp])),Centre(Frame(Part(0,.4f,new(-nx,ny,n.Z)),[lamp])),"Mirrored UV handedness");
        Near(Centre(Frame(Part(0,.4f,uv:new Vector2[4],normalMap:tangentMap),[lamp])),Centre(Frame(Part(0,.4f),[lamp])),"Degenerate UV fallback");
        Near(Centre(Frame(Part(0,.4f,normalMap:tangentMap),[lamp])),Centre(Frame(Part(0,.4f),[lamp])),"No UV texture ignored");
        Frame(Part(float.NaN,float.PositiveInfinity,Vector3.Zero,uv),[lamp]);
        Near(Centre(Frame(Part(0,.4f),[lamp],transform:Matrix4x4.CreateScale(1,1,0))),Centre(Frame(Part(0,.4f),[lamp])),"Singular normal matrix fallback");
        var grey = new PreparedTexture("qa-pbr-grey",1,1,TextureSemantic.Color,false,[[128,128,128,255]]);
        var channels = new PreparedTexture("qa-pbr-mr",1,1,TextureSemantic.Material,false,[[0,128,64,255]]);
        float linearGrey = MathF.Pow((128/255f+.055f)/1.055f,2.4f);
        var expectedMapped = Reference(baseColor*linearGrey,.8f*64/255,.7f*128/255,Vector3.UnitZ,Vector3.Normalize(eye-pixelPosition),Vector3.Normalize(lamp.Position-pixelPosition))*lamp.Intensity*MathF.Pow(1-Vector3.Distance(lamp.Position,pixelPosition)/lamp.Range,2);
        Near(Centre(Frame(Part(.8f,.7f,uv:uv,colorMap:grey,mr:channels),[lamp])),expectedMapped,"sRGB colour / linear MR channels");

        var probe = ProbeIrradiance.Pack(Enumerable.Repeat(new Vector3(.2f,.3f,.4f),6).ToArray());
        var glow = new Vector3(2,1,.5f); var material = Part(.4f,.5f,emission:glow);
        world.Diagnostic=LightingDiagnostic.Direct; var direct=Centre(Frame(material,[lamp],probe));
        world.Diagnostic=LightingDiagnostic.DiffuseIndirect; var indirect=Centre(Frame(material,[lamp],probe));
        Near(indirect,baseColor*.6f*new Vector3(.2f,.3f,.4f),"Diffuse E/pi applied once");
        Near(Centre(Frame(material,[lamp],probe,useAtlas:true)),indirect,"Atlas replaces probe; no duplicate diffuse");
        world.Diagnostic=LightingDiagnostic.Emission; Near(Centre(Frame(material,[lamp],probe)),glow,"Independent HDR emission");
        world.Diagnostic=LightingDiagnostic.SpecularIndirect; Near(Centre(Frame(material,[lamp],probe)),Vector3.Zero,"No phantom specular environment");
        world.Diagnostic=LightingDiagnostic.Full; world.AmbientStrength=.9f;
        Near(Centre(Frame(material,[lamp],probe)),direct+indirect+glow,"Diagnostic sum / no double ambient");
        world.IndirectOnly=true;
        Near(Centre(Frame(material,[lamp],probe)),indirect,"Legacy indirect-view flag retained");
        Console.WriteLine($"PASS: {_checks} PBR HDR checks: numerical GGX/Smith/Schlick, scalar ramps, local/directional lights, roughness, tangent/mirrored/degenerate UVs, colour spaces, diagnostics and finite pixels.");
    }

    // Double-precision reference written in terms of separate D, correlated G and Fresnel.
    private static Vector3 Reference(Vector3 colour, double metal, double rough, Vector3 n, Vector3 v, Vector3 l)
    {
        double nl=Math.Max(0,Vector3.Dot(n,l)), nv=Math.Max(0,Vector3.Dot(n,v));
        if(nl==0 || nv==0) return Vector3.Zero;
        var h=Vector3.Normalize(v+l);
        double nh=Math.Clamp(Vector3.Dot(n,h),0,1), vh=Math.Clamp(Vector3.Dot(v,h),0,1);
        double alpha=Math.Pow(Math.Clamp(rough,.04,1),2), alpha2=alpha*alpha;
        double d=alpha2/(Math.PI*Math.Pow((1-nh*nh)+nh*nh*alpha2,2));
        double lambdaV=(Math.Sqrt(1+alpha2*(1-nv*nv)/(nv*nv))-1)/2;
        double lambdaL=(Math.Sqrt(1+alpha2*(1-nl*nl)/(nl*nl))-1)/2;
        double g=1/(1+lambdaV+lambdaL);
        float Channel(double c)
        {
            double f0=.04*(1-metal)+c*metal, f=f0+(1-f0)*Math.Pow(1-vh,5);
            return (float)(((1-f)*(1-metal)*c/Math.PI+d*g*f/(4*nv*nl))*nl);
        }
        return new(Channel(colour.X),Channel(colour.Y),Channel(colour.Z));
    }
}

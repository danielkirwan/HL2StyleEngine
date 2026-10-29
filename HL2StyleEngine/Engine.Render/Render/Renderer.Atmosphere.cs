using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed partial class Renderer
{
    private static readonly PostProcessSettings DisabledAtmosphere=new();
    private PostProcessSettings _atmosphere=DisabledAtmosphere;
    private Matrix4x4 _atmosphereInverse;
    private Vector3 _atmosphereCamera;
    private Vector4 _atmosphereViewport;
    private ResourceLayout? _atmosphereLayout;
    private DeviceBuffer? _atmosphereBuffer;
    private Shader[]? _atmosphereShaders;
    private Pipeline? _atmosphereComposite, _bloomExtract, _bloomHorizontal, _bloomVertical;
    private Texture? _bloomA, _bloomB, _blackBloom;
    private TextureView? _bloomAView, _bloomBView, _blackBloomView, _fogDepthView, _fogColorView;
    private Framebuffer? _bloomAFrame, _bloomBFrame;
    private ResourceSet? _atmosphereBlackSet, _atmosphereASet, _atmosphereBSet;
    private uint _bloomWidth, _bloomHeight;
    public int PostProcessPasses { get; private set; }
    public long PostProcessGpuBytes => (long)_bloomWidth*_bloomHeight*16+(_blackBloom==null?0:8);

    public void ConfigurePostProcessing(PostProcessSettings? settings,Matrix4x4 viewProjection,Vector3 camera,Viewport viewport)
    {
        _atmosphere=settings?.Sanitize()??DisabledAtmosphere;
        if(!Matrix4x4.Invert(viewProjection,out _atmosphereInverse) || !Finite(_atmosphereInverse) || !float.IsFinite(camera.X+camera.Y+camera.Z) ||
            !float.IsFinite(viewport.X+viewport.Y+viewport.Width+viewport.Height) || viewport.Width<=0 || viewport.Height<=0)
            _atmosphere=DisabledAtmosphere;
        _atmosphereCamera=camera;
        _atmosphereViewport=new(viewport.X,viewport.Y,viewport.Width,viewport.Height);
    }

    private static bool Finite(Matrix4x4 matrix)
    {
        foreach(float value in MemoryMarshal.CreateReadOnlySpan(ref matrix.M11,16))
            if(!float.IsFinite(value))return false;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AtmosphereUniforms
    {
        public Matrix4x4 Inverse;
        public Vector4 Camera, Viewport, Dimensions, Bloom, Grade, Filter, FogColor, Fog;
    }

    private void DrawAtmosphere(Framebuffer target)
    {
        EnsureAtmosphere();
        uint width=_atmosphere.BloomActive?Math.Max(1,(_w+(uint)_atmosphere.BloomDownsample-1)/(uint)_atmosphere.BloomDownsample):0;
        uint height=_atmosphere.BloomActive?Math.Max(1,(_h+(uint)_atmosphere.BloomDownsample-1)/(uint)_atmosphere.BloomDownsample):0;
        if(width!=_bloomWidth || height!=_bloomHeight)
        {
            ReleaseBloomTargets();_bloomWidth=width;_bloomHeight=height;
            if(width>0)
            {
                var desc=TextureDescription.Texture2D(width,height,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.RenderTarget|TextureUsage.Sampled);
                _bloomA=Factory.CreateTexture(desc);_bloomB=Factory.CreateTexture(desc);
                _bloomAView=Factory.CreateTextureView(_bloomA);_bloomBView=Factory.CreateTextureView(_bloomB);
                _bloomAFrame=Factory.CreateFramebuffer(new(null,_bloomA));_bloomBFrame=Factory.CreateFramebuffer(new(null,_bloomB));
                _atmosphereASet=AtmosphereSet(_bloomAView);_atmosphereBSet=AtmosphereSet(_bloomBView);
            }
        }
        var s=_atmosphere;
        var data=new AtmosphereUniforms {Inverse=_atmosphereInverse,Camera=new(_atmosphereCamera,0),Viewport=_atmosphereViewport,
            Dimensions=new(_w,_h,Math.Max(1,width),Math.Max(1,height)),Bloom=new(s.BloomActive?s.BloomIntensity:0,s.BloomThreshold,0,0),
            Grade=new(float.IsFinite(Exposure)?Math.Clamp(Exposure,.05f,8):1,s.Contrast,s.Saturation,s.GradingEnabled?1:0),
            Filter=new((Vector3)s.ColorFilter,0),FogColor=new((Vector3)s.FogColor,s.FogMaxOpacity),
            Fog=new(s.FogActive?s.FogDensity:0,s.FogStart,s.FogBaseHeight,s.FogHeightFalloff)};
        GraphicsDevice.UpdateBuffer(_atmosphereBuffer!,0,ref data);
        if(width>0)
        {
            Pass(_bloomAFrame!,_bloomExtract!,_atmosphereBlackSet!);
            Pass(_bloomBFrame!,_bloomHorizontal!,_atmosphereASet!);
            Pass(_bloomAFrame!,_bloomVertical!,_atmosphereBSet!);
        }
        Pass(target,_atmosphereComposite!,width>0?_atmosphereASet!:_atmosphereBlackSet!);
    }

    private void Pass(Framebuffer target,Pipeline pipeline,ResourceSet set)
    {
        CommandList.SetFramebuffer(target);CommandList.SetFullViewports();
        CommandList.SetScissorRect(0,0,0,target.Width,target.Height);
        CommandList.SetPipeline(pipeline);CommandList.SetGraphicsResourceSet(0,set);
        CommandList.SetVertexBuffer(0,_presentVB);CommandList.SetIndexBuffer(_presentIB,IndexFormat.UInt16);
        CommandList.DrawIndexed(_presentIndexCount,1,0,0,0);PostProcessPasses++;
    }

    private ResourceSet AtmosphereSet(TextureView bloom)=>Factory.CreateResourceSet(new ResourceSetDescription(
        _atmosphereLayout!,_resolveView,bloom,_fogDepthView!,_fogColorView!,_presentSampler,_atmosphereBuffer!));

    private void EnsureAtmosphere()
    {
        if(_atmosphereBlackSet!=null)return;
        _atmosphereLayout=Factory.CreateResourceLayout(new ResourceLayoutDescription(
            new("SourceTex",ResourceKind.TextureReadOnly,ShaderStages.Fragment),new("BloomTex",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new("SceneDepth",ResourceKind.TextureReadOnly,ShaderStages.Fragment),new("SceneColor",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new("LinearClamp",ResourceKind.Sampler,ShaderStages.Fragment),new("Atmosphere",ResourceKind.UniformBuffer,ShaderStages.Fragment)));
        _atmosphereBuffer=Factory.CreateBuffer(new BufferDescription(192,BufferUsage.UniformBuffer|BufferUsage.Dynamic));
        _blackBloom=Factory.CreateTexture(TextureDescription.Texture2D(1,1,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Sampled));
        GraphicsDevice.UpdateTexture(_blackBloom,new byte[8],0,0,0,1,1,1,0,0);
        _blackBloomView=Factory.CreateTextureView(_blackBloom);
        _fogDepthView=Factory.CreateTextureView(_msaaDepth);_fogColorView=Factory.CreateTextureView(_msaaColor);
        _atmosphereBlackSet=AtmosphereSet(_blackBloomView);
        string directory=Path.Combine(AppContext.BaseDirectory,_shaderDirRelativeToApp);
        _atmosphereShaders=new[]{"ExtractPS","BlurHorizontalPS","BlurVerticalPS","CompositePS"}.Select(entry=>
            Factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,File.ReadAllBytes(Path.Combine(directory,"Atmosphere"+entry+".cso")),entry))).ToArray();
        var layout=new VertexLayoutDescription(new VertexElementDescription("Position",VertexElementSemantic.Position,VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord",VertexElementSemantic.TextureCoordinate,VertexElementFormat.Float2));
        Pipeline Pipeline(Shader pixel,bool hdr)=>Factory.CreateGraphicsPipeline(new GraphicsPipelineDescription {
            BlendState=BlendStateDescription.SingleOverrideBlend,DepthStencilState=DepthStencilStateDescription.Disabled,
            RasterizerState=RasterizerStateDescription.Default,PrimitiveTopology=PrimitiveTopology.TriangleList,
            ResourceLayouts=[_atmosphereLayout],ShaderSet=new([layout],[_presentShaders[0],pixel]),
            Outputs=hdr?new OutputDescription(null,new OutputAttachmentDescription(PixelFormat.R16_G16_B16_A16_Float)):GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription});
        _bloomExtract=Pipeline(_atmosphereShaders[0],true);_bloomHorizontal=Pipeline(_atmosphereShaders[1],true);
        _bloomVertical=Pipeline(_atmosphereShaders[2],true);_atmosphereComposite=Pipeline(_atmosphereShaders[3],false);
    }

    private sealed class RetiredPostResources(params IDisposable?[] resources):IDisposable
    {public void Dispose(){foreach(var resource in resources)resource?.Dispose();}}
    private void ReleaseBloomTargets()
    {
        if(_bloomA!=null)RetireAfterFrame(new RetiredPostResources(_atmosphereASet,_atmosphereBSet,_bloomAFrame,_bloomBFrame,_bloomAView,_bloomBView,_bloomA,_bloomB));
        _atmosphereASet=_atmosphereBSet=null;_bloomAFrame=_bloomBFrame=null;_bloomAView=_bloomBView=null;_bloomA=_bloomB=null;
        _bloomWidth=_bloomHeight=0;
    }
    private void DisposeAtmosphere()
    {
        // Called after idle for resize/disposal, never on the normal frame path.
        _atmosphereASet?.Dispose();_atmosphereBSet?.Dispose();_bloomAFrame?.Dispose();_bloomBFrame?.Dispose();
        _bloomAView?.Dispose();_bloomBView?.Dispose();_bloomA?.Dispose();_bloomB?.Dispose();
        _atmosphereBlackSet?.Dispose();_blackBloomView?.Dispose();_fogDepthView?.Dispose();_fogColorView?.Dispose();_blackBloom?.Dispose();
        _bloomExtract?.Dispose();_bloomHorizontal?.Dispose();_bloomVertical?.Dispose();_atmosphereComposite?.Dispose();
        if(_atmosphereShaders!=null)foreach(var shader in _atmosphereShaders)shader.Dispose();
        _atmosphereBuffer?.Dispose();_atmosphereLayout?.Dispose();
        _atmosphereBlackSet=_atmosphereASet=_atmosphereBSet=null;_bloomAFrame=_bloomBFrame=null;_bloomA=_bloomB=_blackBloom=null;
        _bloomAView=_bloomBView=_blackBloomView=_fogDepthView=_fogColorView=null;
        _bloomExtract=_bloomHorizontal=_bloomVertical=_atmosphereComposite=null;
        _atmosphereShaders=null;_atmosphereBuffer=null;_atmosphereLayout=null;
        _bloomWidth=_bloomHeight=0;
    }
}

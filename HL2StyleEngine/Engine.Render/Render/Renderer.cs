using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed class Renderer : IDisposable
{
    public GraphicsDevice GraphicsDevice { get; }
    public ResourceFactory Factory => GraphicsDevice.ResourceFactory;
    public CommandList CommandList { get; }

    private Framebuffer _worldFramebuffer = null!;
    private Texture _msaaColor = null!;
    private Texture _msaaDepth = null!;

    private Texture _resolveColor = null!;
    private TextureView _resolveView = null!;

    private DeviceBuffer _presentVB = null!;
    private ResourceLayout _presentLayout = null!;
    private ResourceSet _presentSet = null!;
    private Pipeline _presentPipeline = null!;
    private Sampler _presentSampler = null!;
    private Shader[] _presentShaders = null!;
    private DeviceBuffer _presentIB = null!;
    private uint _presentIndexCount;


    private uint _w, _h;

    private const TextureSampleCount MsaaSamples = TextureSampleCount.Count4;

    public OutputDescription WorldOutputDescription => _worldFramebuffer.OutputDescription;

    private readonly string _shaderDirRelativeToApp;

    public Renderer(GraphicsDevice graphicsDevice, string shaderDirRelativeToApp = "Shaders")
    {
        GraphicsDevice = graphicsDevice;
        CommandList = Factory.CreateCommandList();

        _shaderDirRelativeToApp = shaderDirRelativeToApp;

        _w = GraphicsDevice.MainSwapchain.Framebuffer.Width;
        _h = GraphicsDevice.MainSwapchain.Framebuffer.Height;

        CreateOrResizeTargets(_w, _h);
        CreateOrUpdatePresentPass();
    }

    public void Resize(uint w, uint h)
    {
        _w = w;
        _h = h;

        CreateOrResizeTargets(w, h);
        CreateOrUpdatePresentPass();
    }

    private void CreateOrResizeTargets(uint w, uint h)
    {
        _worldFramebuffer?.Dispose();
        _msaaColor?.Dispose();
        _msaaDepth?.Dispose();

        _resolveView?.Dispose();
        _resolveColor?.Dispose();

        var scFb = GraphicsDevice.MainSwapchain.Framebuffer;
        Texture swapchainColor = scFb.ColorTargets[0].Target;

        PixelFormat colorFormat = swapchainColor.Format;

        var scOut = scFb.OutputDescription;
        PixelFormat depthFormat = scOut.DepthAttachment?.Format ?? PixelFormat.R32_Float;

        _msaaColor = Factory.CreateTexture(TextureDescription.Texture2D(
            width: w,
            height: h,
            mipLevels: 1,
            arrayLayers: 1,
            format: colorFormat,
            usage: TextureUsage.RenderTarget,
            sampleCount: MsaaSamples));

        _msaaDepth = Factory.CreateTexture(TextureDescription.Texture2D(
            width: w,
            height: h,
            mipLevels: 1,
            arrayLayers: 1,
            format: depthFormat,
            usage: TextureUsage.DepthStencil,
            sampleCount: MsaaSamples));

        _worldFramebuffer = Factory.CreateFramebuffer(new FramebufferDescription(
            depthTarget: _msaaDepth,
            colorTargets: _msaaColor));

        _resolveColor = Factory.CreateTexture(TextureDescription.Texture2D(
            width: w,
            height: h,
            mipLevels: 1,
            arrayLayers: 1,
            format: colorFormat,
            usage: TextureUsage.RenderTarget | TextureUsage.Sampled,
            sampleCount: TextureSampleCount.Count1));

        _resolveView = Factory.CreateTextureView(_resolveColor);
    }

    private void CreateOrUpdatePresentPass()
    {
        _presentPipeline?.Dispose();
        _presentSet?.Dispose();
        _presentLayout?.Dispose();
        _presentSampler?.Dispose();
        _presentVB?.Dispose();
        _presentIB?.Dispose();


        if (_presentShaders != null)
        {
            foreach (var s in _presentShaders) s.Dispose();
            _presentShaders = null!;
        }

        var verts = new[]{
            new PresentVertex(new Vector2(-1f, -1f), new Vector2(0f, 1f)), 
            new PresentVertex(new Vector2(-1f,  1f), new Vector2(0f, 0f)), 
            new PresentVertex(new Vector2( 1f,  1f), new Vector2(1f, 0f)), 
            new PresentVertex(new Vector2( 1f, -1f), new Vector2(1f, 1f)), 
        };

        ushort[] indices =
        {   0, 1, 2,
            0, 2, 3
        };

        _presentVB = Factory.CreateBuffer(new BufferDescription(
            (uint)(verts.Length * PresentVertex.SizeInBytes),
            BufferUsage.VertexBuffer));
        GraphicsDevice.UpdateBuffer(_presentVB, 0, verts);

        _presentIB = Factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));
        GraphicsDevice.UpdateBuffer(_presentIB, 0, indices);

        _presentIndexCount = (uint)indices.Length;

        _presentSampler = Factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            null,
            0,
            0,
            0,
            0,
            SamplerBorderColor.TransparentBlack));

        _presentLayout = Factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SourceTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SourceSamp", ResourceKind.Sampler, ShaderStages.Fragment)));

        _presentSet = Factory.CreateResourceSet(new ResourceSetDescription(
            _presentLayout, _resolveView, _presentSampler));

        // Load compiled shaders (D3D11 .cso)
        string baseDir = AppContext.BaseDirectory;
        string shaderDir = Path.Combine(baseDir, _shaderDirRelativeToApp);

        byte[] vsBytes = File.ReadAllBytes(Path.Combine(shaderDir, "PresentVS.cso"));
        byte[] psBytes = File.ReadAllBytes(Path.Combine(shaderDir, "PresentPS.cso"));

        _presentShaders = new[]
        {
            Factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vsBytes, "VSMain")),
            Factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, psBytes, "PSMain")),
        };

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        var scOut = GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription;

        var pd = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.Default,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _presentLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _presentShaders),
            Outputs = scOut
        };

        _presentPipeline = Factory.CreateGraphicsPipeline(pd);
    }

    public void BeginFrame()
    {
        CommandList.Begin();

        CommandList.SetFramebuffer(_worldFramebuffer);
        CommandList.ClearColorTarget(0, RgbaFloat.Black);
        CommandList.ClearDepthStencil(1f);
    }

    public void ResolveWorldToSwapchain()
    {
        CommandList.ResolveTexture(_msaaColor, _resolveColor);

        var scFb = GraphicsDevice.MainSwapchain.Framebuffer;
        CommandList.SetFramebuffer(scFb);

        CommandList.SetPipeline(_presentPipeline);
        CommandList.SetGraphicsResourceSet(0, _presentSet);
        CommandList.SetVertexBuffer(0, _presentVB);
        CommandList.SetIndexBuffer(_presentIB, IndexFormat.UInt16);
        CommandList.DrawIndexed(_presentIndexCount, 1, 0, 0, 0);
    }

    public void EndFrame()
    {
        CommandList.End();
        GraphicsDevice.SubmitCommands(CommandList);
        GraphicsDevice.SwapBuffers(GraphicsDevice.MainSwapchain);
    }

    public void Dispose()
    {
        _presentPipeline?.Dispose();
        _presentSet?.Dispose();
        _presentLayout?.Dispose();
        _presentSampler?.Dispose();
        _presentVB?.Dispose();

        if (_presentShaders != null)
            foreach (var s in _presentShaders) s.Dispose();

        _worldFramebuffer?.Dispose();
        _msaaColor?.Dispose();
        _msaaDepth?.Dispose();
        _resolveView?.Dispose();
        _resolveColor?.Dispose();

        CommandList.Dispose();
        GraphicsDevice.Dispose();
        _presentIB?.Dispose();

    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentVertex
    {
        public readonly Vector2 Pos;
        public readonly Vector2 UV;

        public PresentVertex(Vector2 pos, Vector2 uv)
        {
            Pos = pos;
            UV = uv;
        }

        public static uint SizeInBytes => (uint)(4 * sizeof(float));
    }
}

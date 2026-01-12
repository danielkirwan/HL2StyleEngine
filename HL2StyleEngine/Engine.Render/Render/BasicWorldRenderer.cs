using System;
using System.IO;
using System.Numerics;
using Veldrid;

namespace Engine.Render;

public sealed class BasicWorldRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    private readonly DeviceBuffer _cameraBuffer;
    private readonly ResourceLayout _cameraLayout;
    private readonly ResourceSet _cameraSet;

    private readonly Shader[] _shaders;
    private readonly Pipeline _pipeline;

    private readonly Mesh _ground;
    private readonly Mesh _cube;

    public BasicWorldRenderer(GraphicsDevice gd, string shaderDirRelativeToApp)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        _cameraBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _cameraLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("CameraBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _cameraSet = _factory.CreateResourceSet(new ResourceSetDescription(_cameraLayout, _cameraBuffer));

        string vsPath = Path.Combine(AppContext.BaseDirectory, shaderDirRelativeToApp, "BasicVS.cso");
        string psPath = Path.Combine(AppContext.BaseDirectory, shaderDirRelativeToApp, "BasicPS.cso");

        var vsBytes = File.ReadAllBytes(vsPath);
        var psBytes = File.ReadAllBytes(psPath);

        _shaders = new[]
        {
            _factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vsBytes, "VSMain")),
            _factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, psBytes, "PSMain")),
        };

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4));

        var pd = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true,depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None,
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _cameraLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
            Outputs = gd.MainSwapchain.Framebuffer.OutputDescription
        };

        _pipeline = _factory.CreateGraphicsPipeline(pd);

        var (gv, gi) = PrimitiveMeshes.MakeGroundPlane(halfSize: 60f);
        _ground = new Mesh(gd, gv, gi);

        var (cv, ci) = PrimitiveMeshes.MakeCube(half: 0.5f, center: new Vector3(0, 0.5f, 3f));
        _cube = new Mesh(gd, cv, ci);
    }

    public void UpdateCamera(Matrix4x4 viewProj)
    {
        _gd.UpdateBuffer(_cameraBuffer, 0, ref viewProj);
    }

    public void Draw(CommandList cl)
    {
        cl.SetPipeline(_pipeline);
        cl.SetGraphicsResourceSet(0, _cameraSet);

        DrawMesh(cl, _ground);
        DrawMesh(cl, _cube);
    }

    private static void DrawMesh(CommandList cl, Mesh m)
    {
        cl.SetVertexBuffer(0, m.VertexBuffer);
        cl.SetIndexBuffer(m.IndexBuffer, IndexFormat.UInt16);
        cl.DrawIndexed(m.IndexCount, 1, 0, 0, 0);
    }

    public void Dispose()
    {
        _cube.Dispose();
        _ground.Dispose();

        _pipeline.Dispose();
        foreach (var s in _shaders) s.Dispose();

        _cameraSet.Dispose();
        _cameraLayout.Dispose();
        _cameraBuffer.Dispose();
    }
}

using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed class BasicWorldRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;

    private readonly DeviceBuffer _vb;
    private readonly DeviceBuffer _ib;
    private readonly uint _indexCount;

    // b0
    private readonly DeviceBuffer _cameraBuffer;
    private readonly ResourceLayout _cameraLayout;
    private readonly ResourceSet _cameraSet;

    // b1 (ring)
    private readonly DeviceBuffer _objectRingBuffer;
    private readonly ResourceLayout _objectLayout;
    private readonly ResourceSet[] _objectSets;

    private readonly Shader[] _shaders;
    private readonly Pipeline _pipeline;

    // Ring config
    private const uint MaxObjectsPerFrame = 4096; 
    private readonly uint _objectStride;          
    private uint _objectWriteIndex;

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectData
    {
        public Matrix4x4 Model;
        public Vector4 Color;
    }

    public BasicWorldRenderer(GraphicsDevice gd, string shaderDirRelativeToApp)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        // Geometry
        var vertices = CreateCubeVertices();
        var indices = CreateCubeIndices();
        _indexCount = (uint)indices.Length;

        _vb = _factory.CreateBuffer(new BufferDescription(
            (uint)(vertices.Length * Marshal.SizeOf<Vector3>()),
            BufferUsage.VertexBuffer));

        _ib = _factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));

        gd.UpdateBuffer(_vb, 0, vertices);
        gd.UpdateBuffer(_ib, 0, indices);

        _cameraBuffer = _factory.CreateBuffer(new BufferDescription(
            64,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _cameraLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Camera", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _cameraSet = _factory.CreateResourceSet(new ResourceSetDescription(_cameraLayout, _cameraBuffer));

        uint objectDataSize = (uint)Marshal.SizeOf<ObjectData>(); // typically 80
        _objectStride = AlignUp(objectDataSize, 256);

        _objectRingBuffer = _factory.CreateBuffer(new BufferDescription(
            _objectStride * MaxObjectsPerFrame,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _objectLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Object", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        _objectSets = new ResourceSet[MaxObjectsPerFrame];
        for (uint i = 0; i < MaxObjectsPerFrame; i++)
        {
            var range = new DeviceBufferRange(_objectRingBuffer, i * _objectStride, _objectStride);
            _objectSets[i] = _factory.CreateResourceSet(new ResourceSetDescription(_objectLayout, range));
        }

        // Shaders
        string baseDir = AppContext.BaseDirectory;
        string shaderDir = Path.Combine(baseDir, shaderDirRelativeToApp);
        byte[] vsBytes = File.ReadAllBytes(Path.Combine(shaderDir, "BasicVS.cso"));
        byte[] psBytes = File.ReadAllBytes(Path.Combine(shaderDir, "BasicPS.cso"));

        _shaders = new[]
        {
            _factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vsBytes, "VSMain")),
            _factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, psBytes, "PSMain")),
        };

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3));

        var pd = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, 
                PolygonFillMode.Solid,
                FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _cameraLayout, _objectLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, _shaders),
            Outputs = gd.MainSwapchain.Framebuffer.OutputDescription
        };

        _pipeline = _factory.CreateGraphicsPipeline(pd);
    }

    /// <summary>Call once per frame before drawing any objects.</summary>
    public void BeginFrame()
    {
        _objectWriteIndex = 0;
    }

    public void UpdateCamera(Matrix4x4 viewProj)
    {
        _gd.UpdateBuffer(_cameraBuffer, 0, ref viewProj);
    }

    public void DrawBox(CommandList cl, Matrix4x4 model, Vector4 color)
    {
        if (_objectWriteIndex >= MaxObjectsPerFrame)
            return; 

        ObjectData obj = new ObjectData { Model = model, Color = color };

        uint slot = _objectWriteIndex++;
        uint offset = slot * _objectStride;

        _gd.UpdateBuffer(_objectRingBuffer, offset, ref obj);

        cl.SetPipeline(_pipeline);

        cl.SetGraphicsResourceSet(0, _cameraSet);
        cl.SetGraphicsResourceSet(1, _objectSets[slot]);

        cl.SetVertexBuffer(0, _vb);
        cl.SetIndexBuffer(_ib, IndexFormat.UInt16);
        cl.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        foreach (var s in _shaders) s.Dispose();

        foreach (var rs in _objectSets) rs.Dispose();

        _objectLayout.Dispose();
        _objectRingBuffer.Dispose();

        _cameraSet.Dispose();
        _cameraLayout.Dispose();
        _cameraBuffer.Dispose();

        _ib.Dispose();
        _vb.Dispose();
    }

    private static uint AlignUp(uint value, uint alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static Vector3[] CreateCubeVertices()
    {
        return new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f,  0.5f, -0.5f),

            new Vector3(-0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f),
        };
    }

    private static ushort[] CreateCubeIndices()
    {
        return new ushort[]
        {
            // -Z
            0,2,1,  0,3,2,
            // +Z
            4,5,6,  4,6,7,
            // -X
            0,7,3,  0,4,7,
            // +X
            1,2,6,  1,6,5,
            // -Y
            0,1,5,  0,5,4,
            // +Y
            3,7,6,  3,6,2,
        };
    }
}

using System;
using System.Collections.Generic;
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
    private readonly DeviceBuffer _cylinderVb;
    private readonly DeviceBuffer _cylinderIb;
    private readonly uint _cylinderIndexCount;
    private readonly DeviceBuffer _sphereVb;
    private readonly DeviceBuffer _sphereIb;
    private readonly uint _sphereIndexCount;

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

    public BasicWorldRenderer(GraphicsDevice gd, OutputDescription output, string shaderDirRelativeToApp)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;

        var vertices = CreateCubeVertices();
        var indices = CreateCubeIndices();
        _indexCount = (uint)indices.Length;
        var cylinderMesh = CreateCylinderMesh(radialSegments: 24);
        _cylinderIndexCount = (uint)cylinderMesh.indices.Length;
        var sphereMesh = CreateSphereMesh(latitudeSegments: 12, longitudeSegments: 18);
        _sphereIndexCount = (uint)sphereMesh.indices.Length;

        _vb = _factory.CreateBuffer(new BufferDescription(
            (uint)(vertices.Length * Marshal.SizeOf<Vector3>()),
            BufferUsage.VertexBuffer));

        _ib = _factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));

        gd.UpdateBuffer(_vb, 0, vertices);
        gd.UpdateBuffer(_ib, 0, indices);

        _cylinderVb = _factory.CreateBuffer(new BufferDescription(
            (uint)(cylinderMesh.vertices.Length * Marshal.SizeOf<Vector3>()),
            BufferUsage.VertexBuffer));

        _cylinderIb = _factory.CreateBuffer(new BufferDescription(
            (uint)(cylinderMesh.indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));

        gd.UpdateBuffer(_cylinderVb, 0, cylinderMesh.vertices);
        gd.UpdateBuffer(_cylinderIb, 0, cylinderMesh.indices);

        _sphereVb = _factory.CreateBuffer(new BufferDescription(
            (uint)(sphereMesh.vertices.Length * Marshal.SizeOf<Vector3>()),
            BufferUsage.VertexBuffer));

        _sphereIb = _factory.CreateBuffer(new BufferDescription(
            (uint)(sphereMesh.indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));

        gd.UpdateBuffer(_sphereVb, 0, sphereMesh.vertices);
        gd.UpdateBuffer(_sphereIb, 0, sphereMesh.indices);

        _cameraBuffer = _factory.CreateBuffer(new BufferDescription(
            64,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _cameraLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Camera", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

        _cameraSet = _factory.CreateResourceSet(new ResourceSetDescription(_cameraLayout, _cameraBuffer));

        uint objectDataSize = (uint)Marshal.SizeOf<ObjectData>(); 
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
            Outputs = output
        };

        _pipeline = _factory.CreateGraphicsPipeline(pd);
    }

    public void BeginFrame()
    {
        _objectWriteIndex = 0;
    }

    public void UpdateCamera(Matrix4x4 viewProj)
    {
        _gd.UpdateBuffer(_cameraBuffer, 0, ref viewProj);
    }

    public void DrawBox(CommandList cl, Matrix4x4 model, Vector4 color)
        => DrawMesh(cl, model, color, _vb, _ib, _indexCount);

    public void DrawCylinder(CommandList cl, Matrix4x4 model, Vector4 color)
        => DrawMesh(cl, model, color, _cylinderVb, _cylinderIb, _cylinderIndexCount);

    public void DrawSphere(CommandList cl, Matrix4x4 model, Vector4 color)
        => DrawMesh(cl, model, color, _sphereVb, _sphereIb, _sphereIndexCount);

    private void DrawMesh(CommandList cl, Matrix4x4 model, Vector4 color, DeviceBuffer vertexBuffer, DeviceBuffer indexBuffer, uint indexCount)
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

        cl.SetVertexBuffer(0, vertexBuffer);
        cl.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
        cl.DrawIndexed(indexCount, 1, 0, 0, 0);
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

        _cylinderIb.Dispose();
        _cylinderVb.Dispose();
        _sphereIb.Dispose();
        _sphereVb.Dispose();
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

    private static (Vector3[] vertices, ushort[] indices) CreateCylinderMesh(int radialSegments)
    {
        radialSegments = Math.Max(3, radialSegments);

        var vertices = new List<Vector3>();
        var indices = new List<ushort>();

        int sideStart = 0;
        for (int i = 0; i <= radialSegments; i++)
        {
            float t = i / (float)radialSegments;
            float angle = t * MathF.PI * 2f;
            float x = MathF.Cos(angle) * 0.5f;
            float z = MathF.Sin(angle) * 0.5f;

            vertices.Add(new Vector3(x, -0.5f, z));
            vertices.Add(new Vector3(x, 0.5f, z));
        }

        for (int i = 0; i < radialSegments; i++)
        {
            ushort i0 = (ushort)(sideStart + i * 2);
            ushort i1 = (ushort)(i0 + 1);
            ushort i2 = (ushort)(i0 + 2);
            ushort i3 = (ushort)(i0 + 3);

            indices.Add(i0);
            indices.Add(i1);
            indices.Add(i2);

            indices.Add(i1);
            indices.Add(i3);
            indices.Add(i2);
        }

        ushort topCenter = (ushort)vertices.Count;
        vertices.Add(new Vector3(0f, 0.5f, 0f));
        int topRingStart = vertices.Count;
        for (int i = 0; i < radialSegments; i++)
        {
            float t = i / (float)radialSegments;
            float angle = t * MathF.PI * 2f;
            float x = MathF.Cos(angle) * 0.5f;
            float z = MathF.Sin(angle) * 0.5f;
            vertices.Add(new Vector3(x, 0.5f, z));
        }

        for (int i = 0; i < radialSegments; i++)
        {
            ushort current = (ushort)(topRingStart + i);
            ushort next = (ushort)(topRingStart + ((i + 1) % radialSegments));
            indices.Add(topCenter);
            indices.Add(current);
            indices.Add(next);
        }

        ushort bottomCenter = (ushort)vertices.Count;
        vertices.Add(new Vector3(0f, -0.5f, 0f));
        int bottomRingStart = vertices.Count;
        for (int i = 0; i < radialSegments; i++)
        {
            float t = i / (float)radialSegments;
            float angle = t * MathF.PI * 2f;
            float x = MathF.Cos(angle) * 0.5f;
            float z = MathF.Sin(angle) * 0.5f;
            vertices.Add(new Vector3(x, -0.5f, z));
        }

        for (int i = 0; i < radialSegments; i++)
        {
            ushort current = (ushort)(bottomRingStart + i);
            ushort next = (ushort)(bottomRingStart + ((i + 1) % radialSegments));
            indices.Add(bottomCenter);
            indices.Add(next);
            indices.Add(current);
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    private static (Vector3[] vertices, ushort[] indices) CreateSphereMesh(int latitudeSegments, int longitudeSegments)
    {
        var vertices = new List<Vector3>();
        var indices = new List<ushort>();

        for (int lat = 0; lat <= latitudeSegments; lat++)
        {
            float v = lat / (float)latitudeSegments;
            float phi = v * MathF.PI;
            float y = MathF.Cos(phi) * 0.5f;
            float ringRadius = MathF.Sin(phi) * 0.5f;

            for (int lon = 0; lon <= longitudeSegments; lon++)
            {
                float u = lon / (float)longitudeSegments;
                float theta = u * MathF.PI * 2f;
                float x = MathF.Cos(theta) * ringRadius;
                float z = MathF.Sin(theta) * ringRadius;
                vertices.Add(new Vector3(x, y, z));
            }
        }

        int stride = longitudeSegments + 1;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                ushort i0 = (ushort)(lat * stride + lon);
                ushort i1 = (ushort)(i0 + 1);
                ushort i2 = (ushort)(i0 + stride);
                ushort i3 = (ushort)(i2 + 1);

                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i1);

                indices.Add(i1);
                indices.Add(i2);
                indices.Add(i3);
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }
}

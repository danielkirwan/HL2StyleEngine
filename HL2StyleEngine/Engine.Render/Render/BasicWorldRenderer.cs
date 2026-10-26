using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed partial class BasicWorldRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly ResourceFactory _factory;
    private readonly ModelTextureCache _textures;
    private readonly Dictionary<LoadedModel, RenderModel> _pendingUploads = new();
    private double _uploadMilliseconds;
    public long TextureResidentBytes => _textures.ResidentBytes;
    public int ResidentTextureCount => _textures.Count;
    public double UploadBudgetMilliseconds { get; set; } = 2;

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
    private Pipeline? _boundsOverlayPipeline;
    private GraphicsPipelineDescription _boundsOverlayDescription;
    private readonly Shader[] _texturedShaders;
    private readonly Pipeline _texturedPipeline;
    private Pipeline? _multiShadowPipeline;
    private Shader? _multiShadowShader;
    private GraphicsPipelineDescription _texturedDescription;
    private string _shaderDirectory = "";
    private readonly ResourceLayout _textureLayout;
    private readonly Sampler _modelSampler;
    private const int MaxPointLights = 32;
    private readonly DeviceBuffer _lightingBuffer;
    private readonly ResourceLayout _lightingLayout;
    private readonly ResourceSet _lightingSet;
    private readonly Vector4[] _lightingData = new Vector4[2 + MaxPointLights * 4];
    public SurfaceLightingModel LightingModel { get; set; }
    public LightingDiagnostic Diagnostic { get; set; }
    private WorldPointLight[] _activeLights = [];
    public float AmbientStrength { get; set; } = .22f;
    public float DirectionalStrength { get; set; } = 1f;

    // Ring config
    private const uint MaxObjectsPerFrame = 4096;
    private readonly uint _objectStride;
    private uint _objectWriteIndex;
    private readonly PaddedObjectData[] _objectData = new PaddedObjectData[MaxObjectsPerFrame];
    private bool _objectBatchActive;
    public bool ObjectBatchUploadsEnabled { get; set; } = true;
    public int ObjectUploadCount { get; private set; }
    public bool ViewCullingEnabled { get; set; } = true;
    public int ViewCulledCount { get; private set; }
    public uint ObjectDrawCount => _objectWriteIndex;
    private ShadowFrustum _viewFrustum;
    private bool _hasView;

    public bool IntersectsView(Vector3 min, Vector3 max, Matrix4x4 transform)
        => !ViewCullingEnabled || !_hasView || _viewFrustum.IntersectsBounds(min, max, transform);

    private bool ShouldDraw(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        if (IntersectsView(min, max, transform)) return true;
        ViewCulledCount++;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectData
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Vector4 Material;
        public Matrix4x4 NormalMatrix;
        public Vector4 Emission;
        public ProbeIrradiance Probe;
    }

    // Match the constant-buffer range alignment without changing the shader layout.
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct PaddedObjectData
    {
        public ObjectData Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CameraData
    {
        public Matrix4x4 ViewProj;
        public Vector4 CameraPosition;
    }

    public BasicWorldRenderer(GraphicsDevice gd, OutputDescription output, string shaderDirRelativeToApp)
    {
        _gd = gd;
        _factory = gd.ResourceFactory;
        _textures = new ModelTextureCache(gd);

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
            (uint)Marshal.SizeOf<CameraData>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _cameraLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Camera", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

        _cameraSet = _factory.CreateResourceSet(new ResourceSetDescription(_cameraLayout, _cameraBuffer));

        _lightingBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_lightingData.Length * 16), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _lightingLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Lighting", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        _lightingSet = _factory.CreateResourceSet(new ResourceSetDescription(_lightingLayout, _lightingBuffer));
        _gd.UpdateBuffer(_lightingBuffer, 0, _lightingData);

        uint objectDataSize = (uint)Marshal.SizeOf<ObjectData>(); 
        _objectStride = AlignUp(objectDataSize, 256);             
        if (_objectStride != Marshal.SizeOf<PaddedObjectData>())
            throw new InvalidOperationException("Batched object storage must match the constant-buffer stride.");

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
        _shaderDirectory = shaderDir;
        byte[] vsBytes = File.ReadAllBytes(Path.Combine(shaderDir, "BasicVS.cso"));
        byte[] psBytes = File.ReadAllBytes(Path.Combine(shaderDir, "BasicPS.cso"));

        _shaders = new[]
        {
            _factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vsBytes, "VSMain")),
            _factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, psBytes, "PSMain")),
        };

        byte[] texturedVsBytes = File.ReadAllBytes(Path.Combine(shaderDir, "TexturedModelVS.cso"));
        byte[] texturedPsBytes = File.ReadAllBytes(Path.Combine(shaderDir, "TexturedModelPS.cso"));

        _texturedShaders = new[]
        {
            _factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, texturedVsBytes, "VSMain")),
            _factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, texturedPsBytes, "PSMain")),
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
        pd.DepthStencilState = new DepthStencilStateDescription(false, false, ComparisonKind.Always);
        _boundsOverlayDescription = pd;

        _modelSampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Wrap,
            SamplerAddressMode.Wrap,
            SamplerAddressMode.Wrap,
            SamplerFilter.Anisotropic,
            null,
            8,
            0,
            uint.MaxValue,
            0,
            SamplerBorderColor.TransparentBlack));

        _textureLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BaseColorTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BaseColorSamp", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessSamp", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EmissiveTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

        var texturedVertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.Normal, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("EmissiveTexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("LightmapTexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        var texturedPipelineDescription = new GraphicsPipelineDescription
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
            ResourceLayouts = new[] { _cameraLayout, _objectLayout, _textureLayout, _lightingLayout, CreateShadowResources(shaderDir), CreateLightmapResources(), CreateReflectionResources() },
            ShaderSet = new ShaderSetDescription(new[] { texturedVertexLayout }, _texturedShaders),
            Outputs = output
        };

        _texturedPipeline = _factory.CreateGraphicsPipeline(texturedPipelineDescription);
        _texturedDescription = texturedPipelineDescription;
    }

    public void BeginFrame()
    {
        if (_objectBatchActive || _groupCommands != null || _modelDraws.Count != 0)
            throw new InvalidOperationException("End the object upload batch before starting another frame.");
        _objectWriteIndex = 0;
        ObjectUploadCount = 0;
        ViewCulledCount = 0;
        _uploadMilliseconds = 0;
        MainDrawCalls = InstancedDrawCalls = InstancedObjects = MaterialBindings = PipelineBindings = InstanceUploads = 0;
        _usedInstances = false;
    }

    /// <summary>Dispose before submitting the frame's command list, including on early returns.</summary>
    public ObjectUploadBatch BatchObjectUploads()
    {
        if (_objectBatchActive || _objectWriteIndex != 0)
            throw new InvalidOperationException("Start one object upload batch before the frame's first draw.");
        _objectBatchActive = true;
        return new ObjectUploadBatch(this);
    }

    public readonly struct ObjectUploadBatch : IDisposable
    {
        private readonly BasicWorldRenderer _owner;
        internal ObjectUploadBatch(BasicWorldRenderer owner) => _owner = owner;
        public void Dispose() => _owner.EndObjectUploadBatch();
    }

    private void EndObjectUploadBatch()
    {
        if (!_objectBatchActive) return;
        FlushModelDraws();
        UploadInstances();
        _objectBatchActive = false;
        if (ObjectBatchUploadsEnabled && _objectWriteIndex > 0)
        {
            _gd.UpdateBuffer(_objectRingBuffer, 0, _objectData.AsSpan(0, (int)_objectWriteIndex));
            ObjectUploadCount++;
        }
    }

    private uint WriteObject(in ObjectData value)
    {
        uint slot = _objectWriteIndex++;
        if (_objectBatchActive && ObjectBatchUploadsEnabled)
            _objectData[slot].Value = value;
        else
        {
            _gd.UpdateBuffer(_objectRingBuffer, slot * _objectStride, value);
            ObjectUploadCount++;
        }
        return slot;
    }

    public void UpdatePointLights(IEnumerable<WorldPointLight> lights, Vector3 cameraPosition)
    {
        _shadowCameraPosition = cameraPosition;
        UpdateReflectionResources();
        Array.Clear(_lightingData);
        int count = 0;
        _activeLights = lights
            .Where(l => l.Intensity > 0f && l.Range > 0f && float.IsFinite(l.Range) && float.IsFinite(l.Intensity))
            .OrderByDescending(l => l.Priority)
            .ThenBy(l => MathF.Max(0f, Vector3.Distance(l.Position, cameraPosition) - l.Range))
            .ThenBy(l => Vector3.DistanceSquared(l.Position, cameraPosition))
            .Take(MaxPointLights).ToArray();
        _pointShadowIndex = _pointShadowSelector.Select(_activeLights,cameraPosition,PointShadowHysteresis);
        if (PointShadowLimit > 1) EnsureMultiShadowResources();
        foreach (var light in _activeLights)
        {
            _lightingData[1 + count * 4] = new Vector4(light.Position, light.Range);
            _lightingData[2 + count * 4] = new Vector4(Vector3.Max(light.Color, Vector3.Zero), light.Intensity);
            bool spot = light.SpotAngleDegrees > 0 && light.Direction.LengthSquared() > .001f;
            float angle = Math.Clamp(light.SpotAngleDegrees, 5, 150) * MathF.PI / 360;
            _lightingData[3 + count * 4] = new Vector4(spot ? Vector3.Normalize(light.Direction) : Vector3.UnitZ, spot ? MathF.Cos(angle) : -1);
            _lightingData[4 + count * 4] = new Vector4(MathF.Cos(angle * .8f), -1, spot ? 0 : 1, 0);
            count++;
        }
        _lightingData[0] = new Vector4(count, Math.Clamp(AmbientStrength, 0, 2), Math.Clamp(DirectionalStrength, 0, 4), IndirectOnly ? 1 : 0);
        _lightingData[^1] = new Vector4(LightingModel == SurfaceLightingModel.Pbr ? 1 : 0, (int)Diagnostic, 0, 0);
        _gd.UpdateBuffer(_lightingBuffer, 0, _lightingData);
    }

    public void UpdateCamera(Matrix4x4 viewProj, Vector3 cameraPosition = default)
    {
        _viewFrustum = new ShadowFrustum(viewProj);
        _hasView = true;
        CameraData camera = new()
        {
            ViewProj = viewProj,
            CameraPosition = new Vector4(cameraPosition, 1f)
        };
        _gd.UpdateBuffer(_cameraBuffer, 0, ref camera);
    }

    public void DrawBox(CommandList cl, Matrix4x4 model, Vector4 color)
    { if (ShouldDraw(new(-.5f), new(.5f), model)) DrawMesh(cl, model, color, _vb, _ib, _indexCount); }

    // Opt-in authoring bounds remain legible through opaque room walls without writing depth.
    public void DrawBoundsOverlayBox(CommandList cl, Matrix4x4 model, Vector4 color)
    {
        if (!ShouldDraw(new(-.5f), new(.5f), model)) return;
        _boundsOverlayPipeline ??= _factory.CreateGraphicsPipeline(_boundsOverlayDescription);
        DrawMesh(cl, model, color, _vb, _ib, _indexCount, pipeline: _boundsOverlayPipeline);
    }

    public void DrawCylinder(CommandList cl, Matrix4x4 model, Vector4 color)
    { if (ShouldDraw(new(-.5f), new(.5f), model)) DrawMesh(cl, model, color, _cylinderVb, _cylinderIb, _cylinderIndexCount); }

    public void DrawSphere(CommandList cl, Matrix4x4 model, Vector4 color)
    { if (ShouldDraw(new(-.5f), new(.5f), model)) DrawMesh(cl, model, color, _sphereVb, _sphereIb, _sphereIndexCount); }

    public RenderModel LoadGlbModel(string path)
        => CreateRenderModel(TextureCooker.LoadForRendering(path));

    public RenderModel CreateRenderModel(LoadedModel model, bool loadTextures = true)
        => new(_gd, model, _textureLayout, _modelSampler, _textures, loadTextures);

    public bool TryCreateRenderModel(LoadedModel source, out RenderModel? model)
    {
        model = null;
        if (_uploadMilliseconds >= UploadBudgetMilliseconds) return false;
        if (!_pendingUploads.TryGetValue(source, out var pending))
            _pendingUploads[source] = pending = new(_gd, source, _textureLayout, _modelSampler, _textures, deferred: true);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            while (!pending.IsReady)
            {
                pending.UploadNext(_gd, _textureLayout, _modelSampler, _textures);
                if (_uploadMilliseconds + System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= UploadBudgetMilliseconds) break;
            }
            if (!pending.IsReady) return false;
            _pendingUploads.Remove(source); model = pending; return true;
        }
        catch { _pendingUploads.Remove(source); pending.Dispose(); throw; }
        finally { _uploadMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    public void RetirePendingModel(LoadedModel source, Renderer renderer)
    {
        if (_pendingUploads.Remove(source, out var pending)) renderer.RetireAfterFrame(pending);
    }

    public void DrawModel(CommandList cl, RenderModel model, Matrix4x4 transform, Vector4 tint)
        => DrawModel(cl, model, transform, tint, hiddenPartKeys: null);

    public void DrawModelSolidColor(CommandList cl, RenderModel model, Matrix4x4 transform, Vector4 color)
        => DrawModelSolidColor(cl, model, transform, color, hiddenPartKeys: null);

    public void DrawModelSolidColor(CommandList cl, RenderModel model, Matrix4x4 transform, Vector4 color, IReadOnlySet<string>? hiddenPartKeys)
    {
        IReadOnlyList<RenderModelPart> parts = model.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            RenderModelPart part = parts[i];
            if (IsModelPartHidden(part, hiddenPartKeys))
                continue;

            if (!ShouldDraw(part.BoundsMin, part.BoundsMax, transform)) continue;

            DrawMesh(cl, transform, color, part.VertexBuffer, part.IndexBuffer, part.IndexCount, part.IndexFormat);
        }
    }

    public void DrawModel(CommandList cl, RenderModel model, Matrix4x4 transform, Vector4 tint, IReadOnlySet<string>? hiddenPartKeys,
        ModelEmission? emission = null, LightmapBinding? lightmap = null, ProbeIrradiance probe = default)
    {
        bool invertible = Matrix4x4.Invert(transform, out var inverse);
        if (!invertible && LightingModel == SurfaceLightingModel.Pbr) inverse = Matrix4x4.Identity;
        var normalMatrix = Matrix4x4.Transpose(inverse);
        IReadOnlyList<RenderModelPart> parts = model.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            RenderModelPart part = parts[i];
            if (IsModelPartHidden(part, hiddenPartKeys))
                continue;

            if (!ShouldDraw(part.BoundsMin, part.BoundsMax, transform)) continue;

            Vector4 color = new(
                part.Color.X * tint.X,
                part.Color.Y * tint.Y,
                part.Color.Z * tint.Z,
                part.Color.W * tint.W);
            Vector3 glow = (emission ?? new ModelEmission(1)).Resolve(part.EmissiveFactor, part.PartKey, part.NodeName, part.MeshName);
            bool pbr = LightingModel == SurfaceLightingModel.Pbr;
            ResourceSet? reflectionSet = pbr && ReflectionPreviewEnabled && RoomReflections != null
                ? RoomReflections.Select(part.BoundsMin, part.BoundsMax, transform) ?? _blackReflection.Set : null;
            var textureSet = pbr ? part.PbrTextureSet : part.TextureSet;
            Vector4 factors = pbr ? part.PbrMaterialFactors : part.MaterialFactors;
            if (lightmap is { IsReady: true } && i < lightmap.Parts.Count)
            {
                var baked = lightmap.Parts[i];
                Vector4 material = factors; material.W = 1;
                DrawTexturedMesh(cl, transform, color, material, glow, baked.Vertices, baked.Indices, baked.Count,
                    IndexFormat.UInt32, textureSet ?? _lightmapFallbackMaterial!, lightmap.Atlas.Set, reflectionSet: reflectionSet, normalMatrix: normalMatrix);
            }
            else if (pbr && part.PbrVertexBuffer != null && textureSet != null)
            {
                factors.W = probe.HasEnergy ? 2 : 0;
                DrawTexturedMesh(cl, transform, color, factors, glow, part.PbrVertexBuffer, part.PbrIndexBuffer ?? part.IndexBuffer,
                    part.PbrIndexCount, part.PbrIndexBuffer != null ? IndexFormat.UInt32 : part.IndexFormat, textureSet, probe: probe, reflectionSet: reflectionSet, normalMatrix: normalMatrix);
            }
            else if (part.IsTextured && part.TexturedVertexBuffer != null && part.TextureSet != null)
            {
                Vector4 material = part.MaterialFactors; material.W = probe.HasEnergy ? 2 : 0;
                DrawTexturedMesh(cl, transform, color, material, glow, part.TexturedVertexBuffer, part.IndexBuffer, part.IndexCount, part.IndexFormat, part.TextureSet, probe: probe, reflectionSet: reflectionSet, normalMatrix: normalMatrix);
            }
            else
                DrawMesh(cl, transform, color, part.VertexBuffer, part.IndexBuffer, part.IndexCount, part.IndexFormat, glow);
        }
    }

    private static bool IsModelPartHidden(RenderModelPart part, IReadOnlySet<string>? hiddenPartKeys)
    {
        if (hiddenPartKeys == null || hiddenPartKeys.Count == 0)
            return false;

        return hiddenPartKeys.Contains(part.PartKey) ||
               (!string.IsNullOrWhiteSpace(part.NodeName) && hiddenPartKeys.Contains(part.NodeName)) ||
               (!string.IsNullOrWhiteSpace(part.MeshName) && hiddenPartKeys.Contains(part.MeshName));
    }
    private void DrawTexturedMesh(
        CommandList cl,
        Matrix4x4 model,
        Vector4 color,
        Vector4 material,
        Vector3 emission,
        DeviceBuffer vertexBuffer,
        DeviceBuffer indexBuffer,
        uint indexCount,
        IndexFormat indexFormat,
        ResourceSet textureSet, ResourceSet? lightmapSet = null, ProbeIrradiance probe = default, ResourceSet? reflectionSet = null, Matrix4x4? normalMatrix = null)
    {
        if (_objectWriteIndex >= MaxObjectsPerFrame)
            return;

        if (!normalMatrix.HasValue)
        {
            bool invertible = Matrix4x4.Invert(model, out Matrix4x4 inverse);
            if (!invertible && LightingModel == SurfaceLightingModel.Pbr) inverse = Matrix4x4.Identity;
            normalMatrix = Matrix4x4.Transpose(inverse);
        }
        ObjectData obj = new ObjectData
        {
            Model = model, Color = color, Material = material,
            NormalMatrix = normalMatrix.Value, Emission = new Vector4(emission, 0), Probe = probe
        };

        uint slot = WriteObject(obj);

        SubmitModelDraw(cl, new(slot, vertexBuffer, indexBuffer, indexCount, indexFormat, textureSet,
            lightmapSet ?? _emptyLightmap!.Set,
            reflectionSet ?? (ReflectionPreviewEnabled ? ActiveReflection ?? _blackReflection : _blackReflection).Set,
            PointShadowLimit > 1 ? _multiShadowPipeline! : _texturedPipeline, _allowInstancing && color.W >= 1));
    }

    private void DrawMesh(
        CommandList cl,
        Matrix4x4 model,
        Vector4 color,
        DeviceBuffer vertexBuffer,
        DeviceBuffer indexBuffer,
        uint indexCount,
        IndexFormat indexFormat = IndexFormat.UInt16,
        Vector3 emission = default,
        Pipeline? pipeline = null)
    {
        FlushModelDraws();
        if (_objectWriteIndex >= MaxObjectsPerFrame)
            return;

        ObjectData obj = new ObjectData { Model = model, Color = IndirectOnly && pipeline == null ? new Vector4(0,0,0,color.W) : color,
            Material = new Vector4(0f, 1f, 0f, 0f), Emission = new Vector4(IndirectOnly ? Vector3.Zero : emission, 0) };

        uint slot = WriteObject(obj);

        cl.SetPipeline(pipeline ?? _pipeline);
        PipelineBindings++;

        cl.SetGraphicsResourceSet(0, _cameraSet);
        cl.SetGraphicsResourceSet(1, _objectSets[slot]);

        cl.SetVertexBuffer(0, vertexBuffer);
        cl.SetIndexBuffer(indexBuffer, indexFormat);
        cl.DrawIndexed(indexCount, 1, 0, 0, 0);
        MainDrawCalls++;
    }

    public void Dispose()
    {
        DisposeSubmission();
        DisposeReflections();
        foreach (var pending in _pendingUploads.Values) pending.Dispose();
        _pendingUploads.Clear();
        DisposeShadows();
        DisposeLightmaps();
        _textures.Dispose();
        _texturedPipeline.Dispose();
        _multiShadowPipeline?.Dispose();
        _multiShadowShader?.Dispose();
        _pipeline.Dispose();
        _boundsOverlayPipeline?.Dispose();
        foreach (var s in _texturedShaders) s.Dispose();
        foreach (var s in _shaders) s.Dispose();

        _textureLayout.Dispose();
        _modelSampler.Dispose();
        _lightingSet.Dispose();
        _lightingLayout.Dispose();
        _lightingBuffer.Dispose();

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


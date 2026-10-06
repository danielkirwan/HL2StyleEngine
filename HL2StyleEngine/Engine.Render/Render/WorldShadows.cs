using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public readonly record struct ShadowCaster(RenderModel? Model, Matrix4x4 Transform, Vector3 Center, float Radius,
    IReadOnlySet<string>? HiddenParts = null, int Primitive = 0);

public sealed partial class BasicWorldRenderer
{
    private const uint ShadowSize = 1024;
    private const int ShadowSlices = 8, MaxShadowCasters = 2048;
    private Texture _shadowTexture = null!;
    private TextureView _shadowView = null!;
    private Framebuffer[] _shadowFrames = [];
    private DeviceBuffer _shadowMatrices = null!, _shadowObjects = null!;
    private ResourceLayout _shadowLayout = null!, _shadowCameraLayout = null!, _shadowObjectLayout = null!;
    private ResourceSet _shadowSet = null!;
    private ResourceSet[] _shadowCameras = [], _shadowObjectSets = [];
    private DeviceBuffer[] _shadowCameraBuffers = [];
    private Sampler _shadowSampler = null!;
    private Shader _shadowShader = null!;
    private Pipeline _shadowPipeline = null!;
    public int ShadowPassCount { get; private set; }
    public int ShadowDrawCount { get; private set; }
    public int ShadowCastersOmitted { get; private set; }
    public int ShadowRenderedPassCount { get; private set; }
    public int ShadowCacheHits { get; private set; }
    public int ShadowCulledCasters { get; private set; }
    public int ShadowObjectUploadCount { get; private set; }
    public bool ShadowBatchUploadsEnabled { get; set; } = true;
    public bool MeasureShadowCpuTime { get; set; }
    public double ShadowCpuMilliseconds { get; private set; }
    public bool ShadowCachingEnabled { get; set; } = true;
    public bool ShadowCullingEnabled { get; set; } = true;
    public bool ShadowsEnabled { get; set; } = true;
    public bool NeedsShadowCasters => ShadowsEnabled && _activeLights.Any(l => l.CastShadows);
    public bool IsShadowRelevant(Vector3 center, float radius)
    {
        if (!ShadowsEnabled) return false;
        foreach (var light in _activeLights)
            if (light.CastShadows && Vector3.DistanceSquared(center, light.Position) <= MathF.Pow(light.Range + radius, 2)) return true;
        return false;
    }
    private readonly Matrix4x4[] _shadowMatrixData = new Matrix4x4[ShadowSlices];
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct ShadowObjectData { public Matrix4x4 Model; }
    private readonly ShadowObjectData[] _shadowObjectData = new ShadowObjectData[MaxShadowCasters];
    private readonly List<(int Light, int Face)> _shadowPasses = new(ShadowSlices);
    private readonly ShadowPassState[] _shadowStates = Enumerable.Range(0, ShadowSlices).Select(_ => new ShadowPassState()).ToArray();

    private readonly record struct ShadowDraw(RenderModelPart? Part, int Primitive, Matrix4x4 Transform, int ObjectIndex)
    {
        public bool SameGeometry(ShadowDraw other)
            => ReferenceEquals(Part, other.Part) && Primitive == other.Primitive && Transform == other.Transform;
    }

    private sealed class ShadowPassState
    {
        public bool Valid;
        public Matrix4x4 Matrix;
        public List<ShadowDraw> Previous = new(), Current = new();

        public bool Matches(Matrix4x4 matrix)
        {
            if (!Valid || Matrix != matrix || Previous.Count != Current.Count) return false;
            for (int i = 0; i < Current.Count; i++)
                if (!Current[i].SameGeometry(Previous[i])) return false;
            return true;
        }
    }

    private ResourceLayout CreateShadowResources(string shaderDir)
    {
        _shadowTexture = _factory.CreateTexture(TextureDescription.Texture2D(ShadowSize, ShadowSize, 1, ShadowSlices,
            PixelFormat.R32_Float, TextureUsage.DepthStencil | TextureUsage.Sampled));
        _shadowView = _factory.CreateTextureView(_shadowTexture);
        _shadowFrames = Enumerable.Range(0, ShadowSlices).Select(i => _factory.CreateFramebuffer(new FramebufferDescription(
            new FramebufferAttachmentDescription(_shadowTexture, (uint)i), Array.Empty<FramebufferAttachmentDescription>()))).ToArray();
        _shadowMatrices = _factory.CreateBuffer(new BufferDescription(64 * ShadowSlices, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _shadowSampler = _factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp, SamplerFilter.MinPoint_MagPoint_MipPoint, null, 0, 0, 0, 0, SamplerBorderColor.OpaqueWhite));
        _shadowLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowMatrices", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _shadowSet = _factory.CreateResourceSet(new ResourceSetDescription(_shadowLayout, _shadowMatrices, _shadowView, _shadowSampler));
        _shadowCameraLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowCamera", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _shadowObjectLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("ShadowObject", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _shadowCameraBuffers = Enumerable.Range(0, ShadowSlices).Select(_ => _factory.CreateBuffer(new BufferDescription(64,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic))).ToArray();
        _shadowCameras = _shadowCameraBuffers.Select(buffer => _factory.CreateResourceSet(new ResourceSetDescription(_shadowCameraLayout, buffer))).ToArray();
        _shadowObjects = _factory.CreateBuffer(new BufferDescription(256 * MaxShadowCasters, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _shadowObjectSets = Enumerable.Range(0, MaxShadowCasters).Select(i => _factory.CreateResourceSet(new ResourceSetDescription(
            _shadowObjectLayout, new DeviceBufferRange(_shadowObjects, (uint)i * 256, 256)))).ToArray();
        _shadowShader = _factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, File.ReadAllBytes(Path.Combine(shaderDir, "ShadowVS.cso")), "VSMain"));
        _shadowPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.Empty,
            DepthStencilState = new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [_shadowCameraLayout, _shadowObjectLayout],
            ShaderSet = new ShaderSetDescription([new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3))], [_shadowShader]),
            Outputs = _shadowFrames[0].OutputDescription
        });
        return _shadowLayout;
    }

    public void RenderShadows(Renderer renderer, IReadOnlyList<ShadowCaster> casters, Viewport viewport)
    {
        long start = MeasureShadowCpuTime ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try { RenderShadowsCore(renderer, casters, viewport); }
        finally
        {
            ShadowCpuMilliseconds = MeasureShadowCpuTime
                ? System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds : 0;
        }
    }

    private void RenderShadowsCore(Renderer renderer, IReadOnlyList<ShadowCaster> casters, Viewport viewport)
    {
        ShadowObjectUploadCount = 0;
        ShadowPassCount = ShadowDrawCount = ShadowCastersOmitted = 0;
        ShadowRenderedPassCount = ShadowCacheHits = ShadowCulledCasters = 0;
        var matrices = _shadowMatrixData;
        var passes = _shadowPasses;
        passes.Clear();
        int points = 0, spots = 0;
        for (int i = 0; i < _activeLights.Length; i++)
        {
            _lightingData[4 + i * 4].Y = -1;
            var light = _activeLights[i];
            if (!ShadowsEnabled || !light.CastShadows) continue;
            bool spot = _lightingData[3 + i * 4].W >= 0;
            if ((spot && spots >= 2) || (!spot && points >= 1)) continue;
            _lightingData[4 + i * 4].Y = passes.Count;
            int faces = spot ? 1 : 6;
            for (int face = 0; face < faces; face++)
            {
                matrices[passes.Count] = LightMatrix(light, spot ? -1 : face);
                passes.Add((i, face));
            }
            if (spot) spots++; else points++;
        }
        _gd.UpdateBuffer(_lightingBuffer, 0, _lightingData);
        _gd.UpdateBuffer(_shadowMatrices, 0, matrices);
        for (int i = passes.Count; i < _shadowStates.Length; i++)
        {
            _shadowStates[i].Valid = false;
            _shadowStates[i].Previous.Clear(); _shadowStates[i].Current.Clear();
        }
        if (passes.Count == 0) return;
        if (casters.Count > MaxShadowCasters)
        {
            var shadowLights = passes.Select(p => p.Light).Distinct().Select(i => _activeLights[i]).ToArray();
            int originalCount = casters.Count;
            casters = casters.Where(c => shadowLights.Any(l =>
                    Vector3.DistanceSquared(c.Center, l.Position) <= MathF.Pow(l.Range + c.Radius, 2)))
                .OrderBy(c => shadowLights.Min(l => MathF.Max(0, Vector3.Distance(c.Center, l.Position) - c.Radius)))
                .Take(MaxShadowCasters).ToArray();
            ShadowCastersOmitted = originalCount - casters.Count;
        }
        var cl = renderer.CommandList;
        bool uploadedObjects = false;
        for (int pass = 0; pass < passes.Count; pass++)
        {
            var state = _shadowStates[pass];
            state.Current.Clear();
            var frustum = new ShadowFrustum(matrices[pass]);
            var light = _activeLights[passes[pass].Light];
            for (int c = 0; c < casters.Count; c++)
            {
                var caster = casters[c];
                if (Vector3.DistanceSquared(caster.Center, light.Position) > MathF.Pow(light.Range + caster.Radius, 2) ||
                    (ShadowCullingEnabled && !frustum.IntersectsSphere(caster.Center, caster.Radius)))
                {
                    ShadowCulledCasters++;
                    continue;
                }
                if (caster.Model != null)
                {
                    foreach (var part in caster.Model.Parts)
                        if (!IsModelPartHidden(part, caster.HiddenParts)) state.Current.Add(new(part, 0, caster.Transform, c));
                }
                else state.Current.Add(new(null, caster.Primitive, caster.Transform, c));
            }
            if (ShadowCachingEnabled && state.Matches(matrices[pass]))
            {
                ShadowCacheHits++;
                continue;
            }
            if (!uploadedObjects)
            {
                // One upload preserves the 256-byte resource-set offsets without hundreds of driver calls.
                if (ShadowBatchUploadsEnabled)
                {
                    for (int i = 0; i < casters.Count; i++) _shadowObjectData[i].Model = casters[i].Transform;
                    if (casters.Count > 0)
                    {
                        _gd.UpdateBuffer<ShadowObjectData>(_shadowObjects, 0, _shadowObjectData.AsSpan(0, casters.Count));
                        ShadowObjectUploadCount++;
                    }
                }
                else
                {
                    for (int i = 0; i < casters.Count; i++)
                    {
                        Matrix4x4 model = casters[i].Transform;
                        _gd.UpdateBuffer(_shadowObjects, (uint)i * 256, ref model);
                        ShadowObjectUploadCount++;
                    }
                }
                uploadedObjects = true;
            }
            _gd.UpdateBuffer(_shadowCameraBuffers[pass], 0, ref matrices[pass]);
            cl.SetFramebuffer(_shadowFrames[pass]);
            cl.SetViewport(0, new Viewport(0, 0, ShadowSize, ShadowSize, 0, 1));
            cl.ClearDepthStencil(1);
            cl.SetPipeline(_shadowPipeline);
            cl.SetGraphicsResourceSet(0, _shadowCameras[pass]);
            foreach (var draw in state.Current)
            {
                cl.SetGraphicsResourceSet(1, _shadowObjectSets[draw.ObjectIndex]);
                if (draw.Part is { } part)
                {
                    Draw(part.VertexBuffer, part.IndexBuffer, part.IndexCount, part.IndexFormat);
                }
                else if (draw.Primitive == 2) Draw(_sphereVb, _sphereIb, _sphereIndexCount, IndexFormat.UInt16);
                else if (draw.Primitive == 1) Draw(_cylinderVb, _cylinderIb, _cylinderIndexCount, IndexFormat.UInt16);
                else Draw(_vb, _ib, _indexCount, IndexFormat.UInt16);
            }
            state.Matrix = matrices[pass];
            state.Valid = true;
            (state.Previous, state.Current) = (state.Current, state.Previous);
            ShadowRenderedPassCount++;
        }
        ShadowPassCount = passes.Count;
        cl.SetFramebuffer(renderer.WorldFramebuffer);
        cl.SetViewport(0, viewport);

        void Draw(DeviceBuffer vb, DeviceBuffer ib, uint count, IndexFormat format)
        {
            cl.SetVertexBuffer(0, vb); cl.SetIndexBuffer(ib, format); cl.DrawIndexed(count, 1, 0, 0, 0); ShadowDrawCount++;
        }
    }

    public static Matrix4x4 LightMatrix(WorldPointLight light, int face)
    {
        Vector3 direction = face switch { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitY,
            3 => -Vector3.UnitY, 4 => Vector3.UnitZ, 5 => -Vector3.UnitZ, _ => Vector3.Normalize(light.Direction) };
        Vector3 up = MathF.Abs(direction.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY;
        float angle = face < 0 ? Math.Clamp(light.SpotAngleDegrees, 5, 150) * MathF.PI / 180 : MathF.PI / 2;
        return Matrix4x4.CreateLookAt(light.Position, light.Position + direction, up) *
            Matrix4x4.CreatePerspectiveFieldOfView(angle, 1, .05f, MathF.Max(.1f, light.Range));
    }

    private void DisposeShadows()
    {
        _shadowPipeline.Dispose(); _shadowShader.Dispose(); _shadowSet.Dispose();
        foreach (var set in _shadowObjectSets) set.Dispose();
        foreach (var set in _shadowCameras) set.Dispose();
        foreach (var buffer in _shadowCameraBuffers) buffer.Dispose();
        foreach (var framebuffer in _shadowFrames) framebuffer.Dispose();
        _shadowObjects.Dispose(); _shadowMatrices.Dispose(); _shadowSampler.Dispose();
        _shadowView.Dispose(); _shadowTexture.Dispose(); _shadowLayout.Dispose();
        _shadowCameraLayout.Dispose(); _shadowObjectLayout.Dispose();
    }
}

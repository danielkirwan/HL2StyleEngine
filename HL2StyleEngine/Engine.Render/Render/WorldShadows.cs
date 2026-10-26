using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public readonly record struct ShadowCaster(RenderModel? Model, Matrix4x4 Transform, Vector3 Center, float Radius,
    IReadOnlySet<string>? HiddenParts = null, int Primitive = 0);

public sealed partial class BasicWorldRenderer
{
    private const uint ShadowSize = 1024;
    private const int ShadowSlices = 26, MaxShadowCasters = 2048;
    private const int PointShadowFaces = PointShadowPool.Capacity * 6, ShadowMatrices = PointShadowFaces + 2;
    private int _allocatedShadowSlices = 8;
    private bool _shadowTilesActive;
    private readonly List<IDisposable> _retiredShadowAtlases = new();
    private long _retiringShadowBytes;
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
    public float PointShadowHysteresis { get; set; }
    public int PointShadowLimit { get; set; } = 1;
    private readonly PointShadowPool _pointShadowPool = new();
    public int ShadowPointLightCount { get; private set; }
    public int ShadowSpotLightCount { get; private set; }
    public long ShadowGpuBytes => _allocatedShadowSlices * (long)ShadowSize * ShadowSize * 4 + _retiringShadowBytes;
    public IReadOnlyList<string> ShadowLightIds { get; private set; } = [];
    private readonly PointShadowSelector _pointShadowSelector = new();
    private int _pointShadowIndex = -1;
    private Vector3 _shadowCameraPosition;
    public bool NeedsShadowCasters => ShadowsEnabled && _activeLights.Any(l => l.CastShadows);
    public bool IsShadowRelevant(Vector3 center, float radius)
    {
        if (!ShadowsEnabled) return false;
        foreach (var light in _activeLights)
            if (light.CastShadows && Vector3.DistanceSquared(center, light.Position) <= MathF.Pow(light.Range + radius, 2)) return true;
        return false;
    }
    private readonly Matrix4x4[] _shadowMatrixData = new Matrix4x4[ShadowMatrices];
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct ShadowObjectData { public Matrix4x4 Model; }
    private readonly ShadowObjectData[] _shadowObjectData = new ShadowObjectData[MaxShadowCasters];
    private readonly List<(int Light, int Face, int Slice)> _shadowPasses = new(ShadowMatrices);
    private readonly ShadowPassState[] _shadowStates = Enumerable.Range(0, ShadowMatrices).Select(_ => new ShadowPassState()).ToArray();
    private readonly List<ShadowDraw> _shadowGeometry = new();
    private readonly (int Start, int Count)[] _shadowGeometryRanges = new (int, int)[MaxShadowCasters];

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
        _shadowTexture = _factory.CreateTexture(TextureDescription.Texture2D(ShadowSize, ShadowSize, 1, (uint)_allocatedShadowSlices,
            PixelFormat.R32_Float, TextureUsage.DepthStencil | TextureUsage.Sampled));
        _shadowView = _factory.CreateTextureView(_shadowTexture);
        _shadowFrames = Enumerable.Range(0, _allocatedShadowSlices).Select(i => _factory.CreateFramebuffer(new FramebufferDescription(
            new FramebufferAttachmentDescription(_shadowTexture, (uint)i), Array.Empty<FramebufferAttachmentDescription>()))).ToArray();
        _shadowMatrices = _factory.CreateBuffer(new BufferDescription(64 * ShadowMatrices, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
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
        _shadowCameraBuffers = Enumerable.Range(0, ShadowMatrices).Select(_ => _factory.CreateBuffer(new BufferDescription(64,
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
        RenderShadows(renderer.CommandList, renderer.WorldFramebuffer, casters, viewport);
        foreach (var resource in _retiredShadowAtlases) renderer.RetireAfterFrame(resource);
        _retiredShadowAtlases.Clear();
    }

    private void EnsureMultiShadowResources()
    {
        if (_multiShadowPipeline == null)
        {
            _multiShadowShader = _factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,
                File.ReadAllBytes(Path.Combine(_shaderDirectory, "TexturedModelMultiShadowPS.cso")), "PSMain"));
            var description = _texturedDescription;
            description.ShaderSet = new ShaderSetDescription(description.ShaderSet.VertexLayouts, [_texturedShaders[0], _multiShadowShader]);
            _multiShadowPipeline = _factory.CreateGraphicsPipeline(description);
        }
        if (_allocatedShadowSlices == ShadowSlices) return;
        var oldTexture = _shadowTexture; var oldView = _shadowView; var oldFrames = _shadowFrames; var oldSet = _shadowSet;
        long bytes = _allocatedShadowSlices * (long)ShadowSize * ShadowSize * 4;
        _retiringShadowBytes += bytes;
        _retiredShadowAtlases.Add(new ShadowRetirement(() =>
        {
            oldSet.Dispose(); foreach (var frame in oldFrames) frame.Dispose(); oldView.Dispose(); oldTexture.Dispose();
            _retiringShadowBytes -= bytes;
        }));
        _allocatedShadowSlices = ShadowSlices;
        _shadowTexture = _factory.CreateTexture(TextureDescription.Texture2D(ShadowSize, ShadowSize, 1, ShadowSlices,
            PixelFormat.R32_Float, TextureUsage.DepthStencil | TextureUsage.Sampled));
        _shadowView = _factory.CreateTextureView(_shadowTexture);
        _shadowFrames = Enumerable.Range(0, ShadowSlices).Select(i => _factory.CreateFramebuffer(new FramebufferDescription(
            new FramebufferAttachmentDescription(_shadowTexture, (uint)i), []))).ToArray();
        _shadowSet = _factory.CreateResourceSet(new ResourceSetDescription(_shadowLayout, _shadowMatrices, _shadowView, _shadowSampler));
        foreach (var state in _shadowStates) state.Valid = false;
    }

    private sealed class ShadowRetirement(Action release) : IDisposable { public void Dispose() => release(); }

    public void RenderShadows(CommandList commandList, Framebuffer target, IReadOnlyList<ShadowCaster> casters, Viewport viewport)
    {
        long start = MeasureShadowCpuTime ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try { RenderShadowsCore(commandList, target, casters, viewport); }
        finally
        {
            ShadowCpuMilliseconds = MeasureShadowCpuTime
                ? System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds : 0;
        }
    }

    private void RenderShadowsCore(CommandList cl, Framebuffer target, IReadOnlyList<ShadowCaster> casters, Viewport viewport)
    {
        bool tiled = PointShadowLimit > 1;
        if (_shadowTilesActive != tiled)
        {
            foreach (var state in _shadowStates) state.Valid = false;
            _shadowTilesActive = tiled;
        }
        ShadowObjectUploadCount = 0;
        ShadowPassCount = ShadowDrawCount = ShadowCastersOmitted = 0;
        ShadowRenderedPassCount = ShadowCacheHits = ShadowCulledCasters = 0;
        var matrices = _shadowMatrixData;
        var passes = _shadowPasses;
        passes.Clear();
        int points = 0, spots = 0;
        var slots = PointShadowLimit > 1 ? _pointShadowPool.Select(_activeLights, _shadowCameraPosition,
            PointShadowLimit, PointShadowHysteresis) : null;
        var used = new bool[ShadowMatrices];
        for (int i = 0; i < _activeLights.Length; i++)
        {
            _lightingData[4 + i * 4].Y = -1;
            var light = _activeLights[i];
            if (!ShadowsEnabled || !light.CastShadows) continue;
            bool spot = _lightingData[3 + i * 4].W >= 0;
            int slot = slots == null ? -1 : Enumerable.Range(0, slots.Count).FirstOrDefault(s => slots[s] == i, -1);
            if (!spot && (slots == null ? i != _pointShadowIndex : slot < 0)) continue;
            if (spot && spots >= 2) continue;
            int first = slots == null ? passes.Count : spot ? PointShadowFaces + spots : slot * 6;
            _lightingData[4 + i * 4].Y = first;
            int faces = spot ? 1 : 6;
            for (int face = 0; face < faces; face++)
            {
                matrices[first + face] = LightMatrix(light, spot ? -1 : face);
                used[first + face] = true;
                passes.Add((i, face, first + face));
            }
            if (spot) spots++; else points++;
        }
        _gd.UpdateBuffer(_lightingBuffer, 0, _lightingData);
        _gd.UpdateBuffer(_shadowMatrices, 0, matrices);
        ShadowPointLightCount = points; ShadowSpotLightCount = spots;
        ShadowLightIds = passes.Select(p => _activeLights[p.Light].Id ?? "").Distinct().ToArray();
        for (int i = 0; i < _shadowStates.Length; i++)
        {
            if (used[i]) continue;
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
        // Resolve fracture masks once per caster, not once per cube face/light.
        _shadowGeometry.Clear();
        for (int c = 0; c < casters.Count; c++)
        {
            var caster = casters[c];
            int first = _shadowGeometry.Count;
            if (caster.Model != null)
            {
                foreach (var part in caster.Model.Parts)
                    if (!IsModelPartHidden(part, caster.HiddenParts)) _shadowGeometry.Add(new(part, 0, caster.Transform, c));
            }
            else _shadowGeometry.Add(new(null, caster.Primitive, caster.Transform, c));
            _shadowGeometryRanges[c] = (first, _shadowGeometry.Count - first);
        }
        var dirtyLayers = new bool[ShadowSlices];
        for (int pass = 0; pass < passes.Count; pass++)
        {
            int slice = passes[pass].Slice;
            var state = _shadowStates[slice];
            state.Current.Clear();
            var frustum = new ShadowFrustum(matrices[slice]);
            var light = _activeLights[passes[pass].Light];
            for (int c = 0; c < casters.Count; c++)
            {
                var caster = casters[c];
                float reach = light.Range + caster.Radius;
                if (Vector3.DistanceSquared(caster.Center, light.Position) > reach * reach ||
                    (ShadowCullingEnabled && !frustum.IntersectsSphere(caster.Center, caster.Radius)))
                {
                    ShadowCulledCasters++;
                    continue;
                }
                var range = _shadowGeometryRanges[c];
                for (int d = range.Start; d < range.Start + range.Count; d++) state.Current.Add(_shadowGeometry[d]);
            }
            if (!ShadowCachingEnabled || !state.Matches(matrices[slice])) dirtyLayers[Layer(slice)] = true;
        }
        bool uploadedObjects = false;
        var clearedLayers = new bool[ShadowSlices];
        for (int pass = 0; pass < passes.Count; pass++)
        {
            int slice = passes[pass].Slice, layer = Layer(slice);
            var state = _shadowStates[slice];
            // Depth clears affect a whole layer: replay every occupied tile sharing a dirty layer.
            if (!dirtyLayers[layer])
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
            _gd.UpdateBuffer(_shadowCameraBuffers[slice], 0, ref matrices[slice]);
            cl.SetFramebuffer(_shadowFrames[layer]);
            if (!clearedLayers[layer]) { cl.ClearDepthStencil(1); clearedLayers[layer] = true; }
            bool tileFace = slots != null && slice < PointShadowFaces;
            uint size = tileFace ? ShadowSize / 2 : ShadowSize;
            int tile = tileFace ? slice % 4 : 0;
            cl.SetViewport(0, new Viewport((tile % 2) * size, (tile / 2) * size, size, size, 0, 1));
            cl.SetPipeline(_shadowPipeline);
            cl.SetGraphicsResourceSet(0, _shadowCameras[slice]);
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
            state.Matrix = matrices[slice];
            state.Valid = true;
            (state.Previous, state.Current) = (state.Current, state.Previous);
            ShadowRenderedPassCount++;
        }
        ShadowPassCount = passes.Count;
        cl.SetFramebuffer(target);
        cl.SetViewport(0, viewport);

        int Layer(int slice) => slots == null ? slice : slice < PointShadowFaces ? slice / 4 : 24 + slice - PointShadowFaces;

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
        foreach (var resource in _retiredShadowAtlases) resource.Dispose();
        _retiredShadowAtlases.Clear();
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

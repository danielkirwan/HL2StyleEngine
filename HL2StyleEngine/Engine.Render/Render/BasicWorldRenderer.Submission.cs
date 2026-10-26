using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

public sealed partial class BasicWorldRenderer
{
    public bool DrawGroupingEnabled { get; set; } = true;
    public bool StaticInstancingEnabled { get; set; } = true;
    public int MainDrawCalls { get; private set; }
    public int InstancedDrawCalls { get; private set; }
    public int InstancedObjects { get; private set; }
    public int MaterialBindings { get; private set; }
    public int PipelineBindings { get; private set; }
    public int InstanceUploads { get; private set; }
    public long InstanceBufferBytes => _instanceBuffer?.SizeInBytes ?? 0;
    private bool _groupActive, _allowInstancing, _usedInstances;
    private CommandList? _groupCommands;
    private readonly List<ModelDraw> _modelDraws = new();
    private Pipeline? _instancedPipeline, _instancedMultiPipeline;
    private Shader? _instancedShader;
    private DeviceBuffer? _instanceBuffer;
    private readonly InstanceData[] _instanceData = new InstanceData[MaxObjectsPerFrame];

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData { public Matrix4x4 Model, Normal; public Vector4 Color; }
    private readonly record struct ModelDraw(uint Slot, DeviceBuffer Vertices, DeviceBuffer Indices, uint Count,
        IndexFormat Format, ResourceSet Texture, ResourceSet Lightmap, ResourceSet Reflection, Pipeline Pipeline, bool Instance);

    public ModelDrawGroup GroupModelDraws(CommandList commands)
    {
        if (_groupCommands != null) throw new InvalidOperationException("Model draw groups cannot nest.");
        _groupActive = DrawGroupingEnabled; _groupCommands = commands;
        return new(this);
    }
    public readonly struct ModelDrawGroup : IDisposable
    {
        private readonly BasicWorldRenderer _owner;
        internal ModelDrawGroup(BasicWorldRenderer owner) => _owner = owner;
        public void Dispose()
        {
            _owner.FlushModelDraws(); _owner._groupActive = false; _owner._groupCommands = null;
        }
    }
    public void DrawInstance(CommandList commands, in RenderInstance instance)
    {
        bool prior = _allowInstancing;
        try
        {
            _allowInstancing = instance.AllowInstancing;
            DrawModel(commands, instance.Model, instance.Transform, instance.Tint, instance.HiddenParts,
                instance.Emission, instance.Lightmap, instance.Probe);
        }
        finally { _allowInstancing = prior; }
    }

    private void SubmitModelDraw(CommandList commands, in ModelDraw draw)
    {
        if (_groupActive)
        {
            if (!ReferenceEquals(commands, _groupCommands)) throw new InvalidOperationException("Draw group command list changed.");
            _modelDraws.Add(draw);
        }
        else
        {
            BindModelDraw(commands, draw, null, draw.Pipeline);
            commands.SetGraphicsResourceSet(1, _objectSets[draw.Slot]);
            commands.DrawIndexed(draw.Count, 1, 0, 0, 0); MainDrawCalls++;
        }
    }
    private void FlushModelDraws()
    {
        if (_modelDraws.Count == 0) return;
        var commands = _groupCommands!;
        ModelDraw? previous = null; Pipeline? previousPipeline = null;
        for (int i = 0; i < _modelDraws.Count;)
        {
            var draw = _modelDraws[i]; int count = 1;
            if (StaticInstancingEnabled && _objectBatchActive && ObjectBatchUploadsEnabled && draw.Instance)
                while (i+count < _modelDraws.Count && Compatible(draw, _modelDraws[i+count], count)) count++;
            Pipeline pipeline = count > 1 ? InstancePipeline(draw.Pipeline == _multiShadowPipeline) : draw.Pipeline;
            BindModelDraw(commands, draw, previousPipeline == pipeline ? previous : null, pipeline);
            commands.SetGraphicsResourceSet(1, _objectSets[draw.Slot]);
            if (count > 1)
            {
                for (int n = 0; n < count; n++)
                {
                    var data = _objectData[draw.Slot+(uint)n].Value;
                    _instanceData[draw.Slot+(uint)n] = new() { Model = data.Model, Normal = data.NormalMatrix, Color = data.Color };
                }
                commands.SetVertexBuffer(1, _instanceBuffer!, draw.Slot * 144);
                _usedInstances = true; InstancedDrawCalls++; InstancedObjects += count;
            }
            commands.DrawIndexed(draw.Count, (uint)count, 0, 0, 0); MainDrawCalls++;
            previous = draw; previousPipeline = pipeline; i += count;
        }
        _modelDraws.Clear();
    }
    private bool Compatible(in ModelDraw a, in ModelDraw b, int offset)
    {
        if (!b.Instance || b.Slot != a.Slot+offset || a.Vertices != b.Vertices || a.Indices != b.Indices || a.Count != b.Count ||
            a.Format != b.Format || a.Texture != b.Texture || a.Lightmap != b.Lightmap || a.Reflection != b.Reflection || a.Pipeline != b.Pipeline) return false;
        var x = _objectData[a.Slot].Value; var y = _objectData[b.Slot].Value;
        // The existing pixel shader reads these from the first object's constant buffer.
        return x.Material == y.Material && x.Emission == y.Emission && x.Probe.A == y.Probe.A && x.Probe.B == y.Probe.B &&
            x.Probe.C == y.Probe.C && x.Probe.D == y.Probe.D && x.Probe.E == y.Probe.E;
    }
    private void BindModelDraw(CommandList commands, in ModelDraw draw, ModelDraw? previous, Pipeline pipeline)
    {
        if (previous == null)
        {
            commands.SetPipeline(pipeline); PipelineBindings++;
            commands.SetGraphicsResourceSet(0, _cameraSet);
            commands.SetGraphicsResourceSet(3, _lightingSet);
            commands.SetGraphicsResourceSet(4, _shadowSet);
        }
        if (previous?.Texture != draw.Texture) { commands.SetGraphicsResourceSet(2, draw.Texture); MaterialBindings++; }
        if (previous?.Lightmap != draw.Lightmap) commands.SetGraphicsResourceSet(5, draw.Lightmap);
        if (previous?.Reflection != draw.Reflection) commands.SetGraphicsResourceSet(6, draw.Reflection);
        if (previous?.Vertices != draw.Vertices) commands.SetVertexBuffer(0, draw.Vertices);
        if (previous?.Indices != draw.Indices || previous?.Format != draw.Format) commands.SetIndexBuffer(draw.Indices, draw.Format);
    }
    private Pipeline InstancePipeline(bool multi)
    {
        if ((multi ? _instancedMultiPipeline : _instancedPipeline) is { } ready) return ready;
        _instancedShader ??= _factory.CreateShader(new(ShaderStages.Vertex,
            File.ReadAllBytes(Path.Combine(_shaderDirectory, "TexturedModelInstancedVS.cso")), "VSMain"));
        _instanceBuffer ??= _factory.CreateBuffer(new BufferDescription(MaxObjectsPerFrame*144, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        var elements = Enumerable.Range(0,8).Select(i => new VertexElementDescription("InstanceRow"+i, VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4))
            .Append(new("InstanceColor", VertexElementSemantic.Color, VertexElementFormat.Float4)).ToArray();
        var layout = new VertexLayoutDescription(144, 1, elements);
        var description = _texturedDescription;
        description.ShaderSet = new ShaderSetDescription([description.ShaderSet.VertexLayouts[0], layout],
            [_instancedShader, multi ? _multiShadowShader! : _texturedShaders[1]]);
        var result = _factory.CreateGraphicsPipeline(description);
        if (multi) _instancedMultiPipeline = result; else _instancedPipeline = result;
        return result;
    }
    private void UploadInstances()
    {
        if (!_usedInstances) return;
        _gd.UpdateBuffer(_instanceBuffer!, 0, _instanceData.AsSpan(0,(int)_objectWriteIndex)); InstanceUploads++;
    }
    private void DisposeSubmission()
    {
        _instancedPipeline?.Dispose(); _instancedMultiPipeline?.Dispose(); _instancedShader?.Dispose(); _instanceBuffer?.Dispose();
    }
}

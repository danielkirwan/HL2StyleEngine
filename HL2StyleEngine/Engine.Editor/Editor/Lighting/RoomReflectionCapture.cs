using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Editor.Level;
using Engine.Render;
using Veldrid;

namespace Engine.Editor.Editor;

public sealed record ReflectionCaptureInput(ReflectionSnapshot Snapshot, string Key,
    Dictionary<string,LoadedModel> Models, LightmapData? Diffuse)
{
    public static ReflectionCaptureInput Prepare(ReflectionSnapshot snapshot, CancellationToken cancel)
    {
        string key = snapshot.Key(cancel);
        var models = new Dictionary<string,LoadedModel>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var entry in snapshot.ModelPaths)
        {
            cancel.ThrowIfCancellationRequested();
            var model = TextureCooker.LoadForRendering(entry.Value);
            bytes += model.Parts.Sum(p => (long)p.Positions.Length * 64 + p.Indices.Length * 4);
            if (bytes > 256 * 1024 * 1024) throw new InvalidDataException("Capture geometry exceeds 256 MiB; reduce fixed capture geometry.");
            models.Add(entry.Key, model);
        }
        var diffuse = snapshot.DiffuseSnapshot == null ? null : LightmapFile.Load(LightmapFile.PathFor(snapshot.LevelPath), snapshot.DiffuseSnapshot.Key(cancel));
        int parts = snapshot.Level.Entities.Where(IndirectLightBaker.IsStaticGeometry).Sum(e => models[e.MeshPath].Parts.Count);
        if (parts > 4096) throw new InvalidDataException("Capture exceeds the 4096 draw budget; reduce fixed capture geometry.");
        if (snapshot.DiffuseSnapshot != null && diffuse == null) throw new InvalidDataException("Rebake stale/missing diffuse lighting before capturing reflections.");
        if (snapshot.Key(cancel) != key) throw new InvalidDataException("Sources changed during capture preparation.");
        return new(snapshot, key, models, diffuse);
    }
}

// Render-thread owned, with private camera, shadow cache and command/fence resources.
// No main-view state or runtime visibility/residency list is reused.
public sealed class RoomReflectionCapture : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicWorldRenderer _world;
    private readonly Texture _color, _depth, _staging;
    private readonly Framebuffer _frame;
    private readonly CommandList _commands;
    private readonly Fence _fence;
    private readonly ReflectionCaptureInput _input;
    private readonly LevelEditorController _editor = new();
    private readonly Dictionary<string,RenderModel> _models = new();
    private readonly List<(LevelEntityDef Entity, RenderModel Model, Matrix4x4 Transform, LightmapBinding? Binding, ProbeIrradiance Probe)> _draws = new();
    private readonly RenderScene _scene = new();
    private readonly List<ShadowCaster> _casters = new();
    private readonly LightmapAtlas? _atlas;
    private bool _submitted, _prepared;
    private int _face;
    public byte[][] Faces { get; } = new byte[6][];
    public int CompletedFaces => _face;
    public bool Complete => _face == 6;
    public long GpuBytes => _world.TextureResidentBytes + _models.Values.Sum(m => m.GeometryBytes) + (_atlas?.Bytes ?? 0) +
        _draws.Sum(d => d.Binding?.Bytes ?? 0) + _input.Snapshot.Settings.Resolution * _input.Snapshot.Settings.Resolution * 20L + _world.ShadowGpuBytes + _world.InstanceBufferBytes;

    public RoomReflectionCapture(GraphicsDevice device, ReflectionCaptureInput input)
    {
        _device = device; _input = input;
        var factory = device.ResourceFactory;
        uint size = (uint)input.Snapshot.Settings.Resolution;
        _color = factory.CreateTexture(TextureDescription.Texture2D(size,size,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.RenderTarget));
        _depth = factory.CreateTexture(TextureDescription.Texture2D(size,size,1,1,PixelFormat.R32_Float,TextureUsage.DepthStencil));
        _staging = factory.CreateTexture(TextureDescription.Texture2D(size,size,1,1,PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Staging));
        _frame = factory.CreateFramebuffer(new(_depth, _color));
        _commands = factory.CreateCommandList(); _fence = factory.CreateFence(false);
        _world = new(device, _frame.OutputDescription, "Shaders") { LightingModel = SurfaceLightingModel.Pbr, ReflectionPreviewEnabled = false, ViewCullingEnabled = false };
        _editor.LoadFromMemory(input.Snapshot.LevelPath, input.Snapshot.Level);
        if (input.Diffuse is { } diffuse) _atlas = _world.CreateLightmapAtlas(diffuse.Size, diffuse.Pixels);
    }

    public void Step()
    {
        if (Complete) return;
        int size = _input.Snapshot.Settings.Resolution;
        if (_submitted)
        {
            if (!_fence.Signaled) return;
            var mapped = _device.Map<ushort>(_staging, MapMode.Read);
            try
            {
                var pixels = new ushort[size*size*4];
                for (int y=0;y<size;y++) for(int x=0;x<size*4;x++) pixels[y*size*4+x] = mapped[x,y];
                Faces[_face++] = MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
            }
            finally { _device.Unmap(_staging); }
            _submitted = false; _device.ResetFence(_fence);
            if (Complete) return;
        }
        _world.BeginFrame();
        if (!_prepared)
        {
            foreach (var pair in _input.Models)
            {
                if (_models.ContainsKey(pair.Key)) continue;
                if (!_world.TryCreateRenderModel(pair.Value, out var gpu)) return;
                _models.Add(pair.Key, gpu!);
            }
            if (_draws.Count == 0)
                for (int i=0;i<_editor.LevelFile.Entities.Count;i++)
                {
                    var entity = _editor.LevelFile.Entities[i];
                    if (!IndirectLightBaker.IsStaticGeometry(entity)) continue;
                    var draw = _editor.DrawBoxes[i]; var model = _input.Models[entity.MeshPath];
                    var transform = IndirectLightBaker.BoundsFit(model, draw.Position, draw.Size, draw.Rotation);
                    LightmapBinding? binding = null;
                    if (_input.Diffuse is { } diffuse && _atlas != null && !entity.ExcludeFromDiffuseBake)
                    {
                        var record = diffuse.Records.Single(r => r.EntityId == entity.Id);
                        if (record.Transform != transform) throw new InvalidDataException("Reflection/diffuse transforms disagree.");
                        binding = new(model, _atlas, record.FirstTriangle, diffuse.Texels, diffuse.Size, true);
                    }
                    var probe = entity.ExcludeFromDiffuseBake ? _input.Diffuse?.PreparedProbes?.Sample(draw.Position) ?? default : default;
                    _draws.Add((entity, _models[entity.MeshPath], transform, binding, probe));
                }
            foreach (var draw in _draws)
                if (draw.Binding is { IsReady: false } binding) { _world.UploadLightmapPart(binding); if (!binding.IsReady) return; }
            _scene.Begin();
            foreach (var draw in _draws)
            {
                var instance = new RenderInstance(draw.Entity.Id, draw.Model, draw.Transform, draw.Entity.Color,
                    Vector3.Transform(Vector3.Zero, draw.Transform), 500, Emission: LevelLighting.GetEmission(_editor.LevelFile, draw.Entity),
                    Lightmap: draw.Binding, Probe: draw.Probe, AllowInstancing: true);
                _scene.Add(instance); _casters.Add(instance.Shadow);
            }
            _scene.Seal();
            _prepared = true;
        }
        Vector3 direction = ReflectionFilter.Direction(_face,0,0);
        Vector3 up = _face == 2 ? -Vector3.UnitZ : _face == 3 ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 eye = _input.Snapshot.Position;
        var vp = Matrix4x4.CreateLookAtLeftHanded(eye, eye+direction, up) * Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(MathF.PI/2,1,.05f,500);
        _world.UpdateCamera(vp, eye); LevelLighting.Apply(_world, _editor, eye);
        if (!_input.Snapshot.Settings.CaptureMask.HasFlag(ReflectionCaptureMask.FixedLights)) _world.UpdatePointLights([], eye);
        _commands.Begin(); _commands.SetFramebuffer(_frame); _commands.SetFullViewports();
        _commands.ClearColorTarget(0, RgbaFloat.Black); _commands.ClearDepthStencil(1);
        // Conservative casters avoid losing geometry outside the active cube face.
        _world.RenderShadows(_commands, _frame, _casters, new(0,0,size,size,0,1));
        using (_world.BatchObjectUploads())
            _scene.Draw(_world, _commands);
        _commands.CopyTexture(_color, _staging); _commands.End();
        _device.SubmitCommands(_commands, _fence); _submitted = true;
    }

    public static ReflectionEnvironmentData Filter(byte[][] faces, int size, CancellationToken cancel = default)
    {
        Vector3 Texel(int face,int x,int y)
        {
            int offset=(Math.Clamp(y,0,size-1)*size+Math.Clamp(x,0,size-1))*8;
            return new((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(faces[face],offset)),
                (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(faces[face],offset+2)),
                (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(faces[face],offset+4)));
        }
        Vector3 Sample(Vector3 d)
        {
            var a=Vector3.Abs(d); int face; float u,v,major;
            if(a.X>=a.Y && a.X>=a.Z) {face=d.X>0?0:1;major=a.X;u=d.X>0?-d.Z:d.Z;v=-d.Y;}
            else if(a.Y>=a.Z) {face=d.Y>0?2:3;major=a.Y;u=d.X;v=d.Y>0?d.Z:-d.Z;}
            else {face=d.Z>0?4:5;major=a.Z;u=d.Z>0?d.X:-d.X;v=-d.Y;}
            float x=(u/major+1)*.5f*size-.5f, y=(v/major+1)*.5f*size-.5f;
            int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);
            return Vector3.Lerp(Vector3.Lerp(Texel(face,ix,iy),Texel(face,ix+1,iy),x-ix),
                Vector3.Lerp(Texel(face,ix,iy+1),Texel(face,ix+1,iy+1),x-ix),y-iy);
        }
        return ReflectionFilter.Build(Sample,size,cancel:cancel);
    }

    // Retire through the host frame fence if a submitted capture is cancelled.
    public void Dispose()
    {
        foreach(var draw in _draws) draw.Binding?.Dispose();
        foreach(var model in _models.Values) model.Dispose();
        _atlas?.Dispose(); _world.Dispose(); _fence.Dispose(); _commands.Dispose(); _frame.Dispose();
        _staging.Dispose(); _depth.Dispose(); _color.Dispose();
    }
}

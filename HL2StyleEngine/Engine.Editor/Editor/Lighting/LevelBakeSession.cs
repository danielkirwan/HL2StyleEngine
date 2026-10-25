using System.Diagnostics;
using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public enum IndirectLightingView { Full, DirectOnly, IndirectOnly }

// Owned by one editor/game instance. CPU preparation never touches GPU or mutable editor data.
public sealed class LevelBakeSession : IDisposable
{
    private readonly LightingScenarioResolver _scenarios = new();
    public string ScenarioStatus => _scenarios.Status;
    private sealed class Resources(LightmapAtlas atlas) : IDisposable
    {
        public LightmapAtlas Atlas = atlas;
        public readonly Dictionary<string, (LightmapBinding Binding, long Frame)> Bindings = new(StringComparer.OrdinalIgnoreCase);
        public void Dispose() { foreach (var entry in Bindings.Values) entry.Binding.Dispose(); Bindings.Clear(); Atlas.Dispose(); }
    }
    private Resources? _resources;
    private sealed record RetainedBake(string Path, long Revision, Resources Resources, LightmapData Data,
        Dictionary<string,LightmapRecord> Records, BakeSnapshot Snapshot, string Stamp);
    private RetainedBake? _retained;
    private string _scenarioSource = "";
    private LightmapData? _data;
    private Dictionary<string, LightmapRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private Task<LightmapData?>? _job;
    private CancellationTokenSource? _cancel;
    private BakeSnapshot? _snapshot;
    private long _revision = -1, _frame;
    private string _path = "";
    private volatile float _progress;
    private long _nextSourceCheck;
    private string _sourceStamp = "";
    private bool _baking;
    private bool _retireRequested;
    private int _generation;
    public string Status { get; private set; } = "No bake loaded";
    public bool Busy => _job != null;
    public bool Baking => Busy && _baking;
    public IndirectLightingView View;
    public LightingDiagnostic Diagnostic;
    public bool PreviewEnabled { get => View != IndirectLightingView.DirectOnly; set => View = value ? IndirectLightingView.Full : IndirectLightingView.DirectOnly; }
    public bool ShowProbes;
    public bool PreviewProbes = true;
    private bool _contributionEnabled;
    private readonly Dictionary<object, (Vector3 Position, ProbeIrradiance Light, long Frame)> _probeCache = new();
    public int ProbeQueries { get; private set; }
    public double ProbeMilliseconds { get; private set; }
    public LightProbeVolume? Probes => _data?.Probes;
    public string Validity => Ready ? "VALID" : Busy ? "PREPARING" : "UNAVAILABLE / STALE";
    public float Progress => _progress;
    public bool Ready => _data != null;
    public int ReadyModels => _resources?.Bindings.Values.Count(v => v.Binding.IsReady) ?? 0;
    public string LastBindingIssue { get; private set; } = "";
    public long ActiveResidentBytes => (_resources?.Atlas.Bytes ?? 0) + (_resources?.Bindings.Values.Sum(v => v.Binding.Bytes) ?? 0);
    public long ResidentBytes => ActiveResidentBytes + (_retained?.Resources.Atlas.Bytes ?? 0) + (_retained?.Resources.Bindings.Values.Sum(v=>v.Binding.Bytes) ?? 0);

    public void Update(LevelEditorController editor, Func<string, string> resolve, BasicWorldRenderer world, Renderer renderer, LevelLightState? state = null)
    {
        bool scenarios=editor.LevelFile.LightingScenarios is { Count: >0 };
        bool sameScenarioSource=scenarios && _scenarioSource==editor.LevelPath;
        _scenarioSource=scenarios?editor.LevelPath:"";
        if (!sameScenarioSource && _retained!=null) { renderer.RetireAfterFrame(_retained.Resources);_retained=null; }
        if (scenarios) { editor=_scenarios.Select(editor,state); state=null; }
        _frame++;
        ProbeQueries = 0; ProbeMilliseconds = 0;
        world.Diagnostic = editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr ? Diagnostic : LightingDiagnostic.Full;
        world.IndirectOnly = world.Diagnostic == LightingDiagnostic.Full && View == IndirectLightingView.IndirectOnly;
        world.ReflectionPreviewEnabled = world.Diagnostic != LightingDiagnostic.Full || View != IndirectLightingView.DirectOnly;
        if (_retireRequested) { Retire(renderer); _retireRequested = false; }
        bool changed = _revision != editor.Revision || _path != editor.LevelPath;
        if (!changed && _snapshot != null && Stopwatch.GetTimestamp() > _nextSourceCheck)
        {
            _nextSourceCheck = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
            changed = SourceStamp(_snapshot) != _sourceStamp;
        }
        if (changed)
        {
            RetainedBake? restore=_retained;
            if(restore!=null && (restore.Path!=editor.LevelPath || restore.Revision!=editor.Revision || SourceStamp(restore.Snapshot)!=restore.Stamp)) restore=null;
            if(restore!=null)_retained=null;
            if (sameScenarioSource && _path!=editor.LevelPath && _revision==editor.Revision && _resources!=null && _data!=null && _snapshot!=null)
            {
                if(_retained!=null)renderer.RetireAfterFrame(_retained.Resources);
                _retained=new(_path,_revision,_resources,_data,_records,_snapshot,_sourceStamp);_resources=null;
            }
            else if(_revision!=editor.Revision && _retained!=null) {renderer.RetireAfterFrame(_retained.Resources);_retained=null;}
            Cancel(); Retire(renderer); _revision = editor.Revision; _path = editor.LevelPath;
            _data = null; _records = new(StringComparer.OrdinalIgnoreCase); _snapshot = null;
            _probeCache.Clear();
            if(restore!=null)
            {
                _resources=restore.Resources;_data=restore.Data;_records=restore.Records;_snapshot=restore.Snapshot;_sourceStamp=restore.Stamp;
                foreach(var key in _resources.Bindings.Keys.ToArray())_resources.Bindings[key]=(_resources.Bindings[key].Binding,_frame);
                Status="Ready: retained lighting-state bake";
            }
            else if (editor.LevelFile.Bake?.Enabled == true)
            {
                try
                {
                    _snapshot = BakeSnapshot.Capture(editor.LevelFile, resolve); _sourceStamp = SourceStamp(_snapshot);
                    var snapshot = _snapshot; string path = LightmapFile.PathFor(_path);
                    Start(token => LightmapFile.Load(path, snapshot.Key(token)), false);
                }
                catch (Exception ex) { Status = "Bake unavailable: " + ex.Message; }
            }
            else Status = "Indirect bake disabled";
        }
        if (_job?.IsCompleted == true)
        {
            try
            {
                _data = _job.GetAwaiter().GetResult();
                _probeCache.Clear();
                _records = _data?.Records.ToDictionary(r => r.EntityId, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
                Status = _data == null ? "Missing, stale or invalid bake; realtime fallback" : $"Ready: {_data.Size} x {_data.Size}, {_records.Count} static models";
                // The previous valid GPU resources survive failed/cancelled rebakes; changed inputs already retired them.
                if (_data != null) Retire(renderer);
            }
            catch (OperationCanceledException) { Status = "Bake cancelled"; }
            catch (Exception ex) { Status = "Bake failed: " + ex.Message; }
            finally { _job = null; _cancel?.Dispose(); _cancel = null; }
        }
        if (_data != null && _resources == null)
        {
            try { _resources = new Resources(world.CreateLightmapAtlas(_data.Size, _data.Pixels)); }
            catch (Exception ex) { _data = null; Status = "Lightmap upload failed: " + ex.Message; }
        }
        if (_resources != null)
            foreach (var pair in _resources.Bindings.Where(p => _frame - p.Value.Frame > 120).ToArray())
            { renderer.RetireAfterFrame(pair.Value.Binding); _resources.Bindings.Remove(pair.Key); }
        bool wantIndirect = world.Diagnostic == LightingDiagnostic.DiffuseIndirect || world.Diagnostic == LightingDiagnostic.Full && PreviewEnabled;
        _contributionEnabled = wantIndirect && _data != null && editor.LevelFile.UsePointLights;
        if (_contributionEnabled)
            foreach (string id in _data!.LightIds)
            {
                var light = editor.LevelFile.Entities.Find(e => e.Id == id);
                if (light == null || !(state?.IsEnabled(light) ?? light.LightEnabled)) { _contributionEnabled = false; break; }
            }
        if (_frame % 60 == 0 && _probeCache.Count > 0)
            foreach (var key in _probeCache.Where(p => _frame-p.Value.Frame>120).Select(p=>p.Key).ToArray()) _probeCache.Remove(key);
    }

    public void BeginBake(LevelEditorController editor, Func<string, string> resolve)
    {
        if (editor.LevelFile.LightingScenarios is { Count: > 0 }) editor=_scenarios.Select(editor,null);
        if (Busy) return;
        try
        {
            var snapshot = BakeSnapshot.Capture(editor.LevelFile, resolve);
            string stamp = SourceStamp(snapshot);
            if (_snapshot == null || !_snapshot.Json.AsSpan().SequenceEqual(snapshot.Json) || stamp != _sourceStamp)
            { _data = null; _records.Clear(); _probeCache.Clear(); _retireRequested = true; }
            _snapshot = snapshot; _sourceStamp = stamp;
            _revision = editor.Revision; _path = editor.LevelPath;
            string path = LightmapFile.PathFor(_path);
            int generation = _generation + 1;
            Start(token =>
            {
                var data = IndirectLightBaker.Bake(snapshot, p =>
                {
                    if (generation == Volatile.Read(ref _generation)) _progress = p;
                }, token);
                LightmapFile.Save(path, data, token); return data;
            }, true);
        }
        catch (Exception ex) { Status = "Bake failed: " + ex.Message; }
    }

    private void Start(Func<CancellationToken, LightmapData?> action, bool baking)
    {
        Interlocked.Increment(ref _generation);
        _cancel = new CancellationTokenSource(); var token = _cancel.Token; _baking = baking; _progress = 0;
        Status = baking ? "Baking indirect lighting" : "Validating saved bake";
        _job = Task.Run(() => action(token), token);
    }

    public void Cancel()
    {
        if (_job == null) return;
        Interlocked.Increment(ref _generation);
        _cancel?.Cancel();
        var job = _job; var cancel = _cancel;
        // Observe retired work, including failures during a level change, without blocking the UI.
        _ = job.ContinueWith(t => { _ = t.Exception; cancel?.Dispose(); }, TaskScheduler.Default);
        _job = null; _cancel = null; Status = "Bake cancelled";
    }

    public LightmapBinding? GetBinding(LevelEntityDef? entity, LoadedModel source, Matrix4x4 transform,
        LevelFile level, BasicWorldRenderer world, Renderer renderer, LevelLightState? state = null)
    {
        if (!_contributionEnabled || entity == null || _data == null || _resources == null || !level.UsePointLights ||
            !IndirectLightBaker.IsStaticGeometry(entity) || entity.ExcludeFromDiffuseBake || !_records.TryGetValue(entity.Id, out var record)) return null;
        if (!Near(record.Transform, transform))
        { LastBindingIssue = $"{entity.Name}: model transform differs from the bake. Expected {record.Transform}; actual {transform}"; return null; }
        if (record.PartTriangles.Length != source.Parts.Count || source.Parts.Where((p, i) => p.Indices.Length / 3 != record.PartTriangles[i]).Any())
        { LastBindingIssue = $"{entity.Name}: model topology differs from the bake"; return null; }
        if (!_resources.Bindings.TryGetValue(entity.Id, out var entry))
            entry = (new LightmapBinding(source, _resources.Atlas, record.FirstTriangle, _data.Texels, _data.Size,
                world.LightingModel == SurfaceLightingModel.Pbr), _frame);
        _resources.Bindings[entity.Id] = (entry.Binding, _frame);
        if (!entry.Binding.IsReady)
        {
            try { world.UploadLightmapPart(entry.Binding); }
            catch (Exception ex)
            {
                renderer.RetireAfterFrame(entry.Binding); _resources.Bindings.Remove(entity.Id);
                Status = "Lightmap geometry upload failed: " + ex.Message; return null;
            }
        }
        return entry.Binding.IsReady ? entry.Binding : null;
    }

    private static bool Near(Matrix4x4 a, Matrix4x4 b)
    {
        var av = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4,float>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref a,1));
        var bv = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4,float>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref b,1));
        for (int i=0;i<av.Length;i++) if (!float.IsFinite(bv[i]) || MathF.Abs(av[i]-bv[i]) > .0001f) return false;
        return true;
    }
    private static string SourceStamp(BakeSnapshot snapshot)
    {
        try { return string.Join('|', snapshot.ModelPaths.Values.Select(path => { var f = new FileInfo(path); return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}"; })); }
        catch (IOException) { return "missing"; }
        catch (UnauthorizedAccessException) { return "unavailable"; }
    }
    private void Retire(Renderer renderer)
    { if (_resources != null) renderer.RetireAfterFrame(_resources); _resources = null; }
    public void Dispose()
    { Cancel(); _resources?.Dispose(); _retained?.Resources.Dispose(); _retained=null; _resources = null; _data = null; _probeCache.Clear(); }

    public ProbeIrradiance SampleProbe(object key, Vector3 position, LevelEntityDef? entity = null)
    {
        if (!PreviewProbes || !_contributionEnabled || _data?.PreparedProbes is not { } sampler ||
            (entity != null && IndirectLightBaker.IsStaticGeometry(entity) && !entity.ExcludeFromDiffuseBake)) return default;
        if (_probeCache.TryGetValue(key, out var entry) && entry.Position == position)
        { _probeCache[key] = (position,entry.Light,_frame); return entry.Light; }
        long start=Stopwatch.GetTimestamp();
        var light=sampler.Sample(position);
        ProbeMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds; ProbeQueries++;
        if (_probeCache.Count >= 1024) _probeCache.Clear();
        _probeCache[key]=(position,light,_frame);
        return light;
    }

    public void DrawProbeMarkers(BasicWorldRenderer world, Renderer renderer)
    {
        if (!ShowProbes || Probes is not { } probes) return;
        bool comparison=world.IndirectOnly;
        world.IndirectOnly=false;
        try
        {
            for(int i=0;i<probes.Valid.Length;i++)
            {
                Vector3 colour=probes.Valid[i] ? new(.1f,.9f,.4f) : new(.8f,.1f,.1f);
                world.DrawBox(renderer.CommandList,Matrix4x4.CreateScale(.065f)*Matrix4x4.CreateTranslation(probes.Position(i)),new Vector4(colour,1));
            }
        }
        finally { world.IndirectOnly=comparison; }
    }
    public void ReportError(string message) => Status = message;
}

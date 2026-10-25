using System.Diagnostics;
using System.Numerics;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed class ReflectionProbeSession : IDisposable
{
    private readonly LightingScenarioResolver _scenarios = new();
    public string ScenarioStatus => _scenarios.Status;
    private static readonly SemaphoreSlim ValidationWorkers = new(2);
    private sealed record Result(string Key, ReflectionEnvironmentData? Data, ReflectionSnapshot? Snapshot = null, string? Stamp = null);
    private Task<object?>? _work;
    private CancellationTokenSource? _cancel;
    private ReflectionSnapshot? _snapshot;
    private RoomReflectionCapture? _capture;
    private ReflectionCaptureInput? _input;
    private ReflectionEnvironmentData? _data;
    private ReflectionEnvironment? _ready, _pending;
    private readonly List<IDisposable> _retire = new();
    private string _key = "", _pendingKey = "", _path = "", _id = "", _stamp = "";
    private long _revision = -1, _nextCheck;
    private ReflectionRoomSession? _rooms;
    private ReflectionRoomSession? _retainedScenarioRooms;
    private string _scenarioSource="",_scenarioPath="",_retainedScenarioPath="";
    private long _scenarioRevision=-1;
    private string _status = "No room probe selected";
    private bool _compatible = true;
    private long _retiringEnvironmentBytes, _retiringCaptureBytes;
    public string Status { get => _rooms?.Status ?? _status; private set => _status=value; }
    public bool Busy => _rooms?.Busy ?? (_work != null || _capture != null || _data != null);
    public bool Ready => _rooms?.Ready ?? (_ready != null);
    internal ReflectionEnvironment? CompatibleEnvironment => _compatible ? _ready : null;
    public bool ShowBounds;
    public bool PreviewEnabled = true;
    public int CompletedFaces => _rooms?.CompletedFaces ?? _capture?.CompletedFaces ?? 0;
    public long ResidentBytes => (_retainedScenarioRooms?.ResidentBytes ?? 0) + (_rooms?.ResidentBytes ?? ((_ready?.ResidentBytes ?? 0) + (_pending?.ResidentBytes ?? 0) +
        _retiringEnvironmentBytes + _retire.OfType<ReflectionEnvironment>().Sum(r => r.ResidentBytes)));
    public int RetainedScenarioProbes => _retainedScenarioRooms?.ResidentCount ?? 0;
    public long PendingCpuBytes => _rooms?.PendingCpuBytes ?? _data?.PayloadBytes ?? 0;
    public long CaptureGpuBytes => _rooms?.CaptureGpuBytes ?? ((_capture?.GpuBytes ?? 0) + _retiringCaptureBytes +
        _retire.OfType<RoomReflectionCapture>().Sum(r => r.GpuBytes));
    public long PeakGpuBytes { get; private set; }
    public int ResidentProbeCount => _rooms?.ResidentCount ?? (_ready == null ? 0 : 1);
    public int RetiringProbeCount => _rooms?.RetiringCount ?? 0;
    public int ProbeLoads => _rooms?.Loads ?? 0;
    public int ProbeEvictions => _rooms?.Evictions ?? 0;

    public void Update(LevelEditorController editor, Func<string,string> resolve, BasicWorldRenderer world, Renderer renderer, LevelLightState? state = null, string? probeOverride = null, Vector3? position = null, byte[]? sourceJson = null)
    {
        bool scenarios=editor.LevelFile.LightingScenarios is { Count: >0 };
        bool sourceChanged=_scenarioSource!=(scenarios?editor.LevelPath:"") || scenarios && _scenarioRevision!=editor.Revision;
        if(sourceChanged)
        {
            if(_retainedScenarioRooms!=null)_retire.Add(_retainedScenarioRooms);
            _retainedScenarioRooms=null;_retainedScenarioPath="";
            _scenarioSource=scenarios?editor.LevelPath:"";_scenarioRevision=editor.Revision;
        }
        if (scenarios)
        {
            editor=_scenarios.Select(editor,state); state=null;
            if(!sourceChanged && _scenarioPath!=editor.LevelPath && editor.LevelFile.AutomaticRoomReflections)
            {
                ReflectionRoomSession? next=null;
                if(_retainedScenarioPath==editor.LevelPath){next=_retainedScenarioRooms;_retainedScenarioRooms=null;_retainedScenarioPath="";}
                if(_rooms!=null)
                {
                    if(_retainedScenarioRooms!=null)_retire.Add(_retainedScenarioRooms);
                    if(_rooms.Busy){_rooms.Cancel();_rooms.Reload();}
                    _retainedScenarioRooms=_rooms;_retainedScenarioPath=_scenarioPath;
                }
                _rooms=next;
            }
            _scenarioPath=editor.LevelPath;
        }
        foreach (var resource in _retire)
        {
            long environment = resource is ReflectionEnvironment env ? env.ResidentBytes : 0;
            long capture = resource is RoomReflectionCapture cap ? cap.GpuBytes : 0;
            _retiringEnvironmentBytes += environment; _retiringCaptureBytes += capture;
            renderer.RetireAfterFrame(new Retirement(resource, () =>
            { _retiringEnvironmentBytes -= environment; _retiringCaptureBytes -= capture; }));
        }
        _retire.Clear();
        if (editor.LevelFile.AutomaticRoomReflections && probeOverride == null)
        {
            if (_rooms == null) { Cancel(); DropReady(); _rooms=new(); }
            _rooms.Update(editor,resolve,world,renderer,state,PreviewEnabled,position);
            PeakGpuBytes=Math.Max(PeakGpuBytes,_rooms.PeakGpuBytes); return;
        }
        if (_rooms != null) { _retire.Add(_rooms); _rooms=null; _revision=-1; }
        world.RoomReflections=null;
        string id = probeOverride ?? editor.LevelFile.ReflectionProbeId ?? "";
        bool changed = _revision != editor.Revision || _path != editor.LevelPath || _id != id;
        if (!changed && _snapshot != null && Stopwatch.GetTimestamp() > _nextCheck)
        {
            _nextCheck = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
            changed = _snapshot.Stamp() != _stamp;
        }
        if (changed)
        {
            Cancel(); DropReady(); _revision = editor.Revision; _path = editor.LevelPath; _id = id; _snapshot = null;
            if (id.Length > 0)
            {
                try
                {
                    if (sourceJson != null)
                    {
                        string path = _path;
                        Start(token =>
                        {
                            ValidationWorkers.Wait(token);
                            try
                            {
                                token.ThrowIfCancellationRequested();
                                var snapshot = ReflectionSnapshot.CreateOwned(StructuredData.ReadJson<LevelFile>(sourceJson), path, id, resolve);
                                string stamp = snapshot.Stamp(), key = snapshot.Key(token);
                                token.ThrowIfCancellationRequested();
                                return new Result(key, ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(path,id),key),snapshot,stamp);
                            }
                            finally { ValidationWorkers.Release(); }
                        });
                    }
                    else
                    {
                        _snapshot = ReflectionSnapshot.Create(editor.LevelFile, _path, id, resolve); _stamp = _snapshot.Stamp();
                        var snapshot = _snapshot;
                        Start(token => { string key = snapshot.Key(token); return new Result(key, ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(snapshot.LevelPath, snapshot.Probe.Id), key)); });
                    }
                    Status = "Validating room capture";
                }
                catch (Exception ex) { Status = "Room capture unavailable: " + ex.Message; }
            }
            else Status = "No room probe selected";
        }
        try
        {
            if (_work is { IsCompleted: true })
            {
                var work = _work; _work = null;
                switch (work.GetAwaiter().GetResult())
                {
                    case ReflectionCaptureInput input:
                        if (_key != input.Key) DropReady();
                        _input = input; _capture = new(renderer.GraphicsDevice, input); Status = "Preparing capture geometry";
                        break;
                    case Result result:
                        if (result.Snapshot != null) { _snapshot = result.Snapshot; _stamp = result.Stamp!; }
                        if (result.Key != _key) DropReady();
                        _data = result.Data; _pendingKey = result.Key;
                        Status = _data == null ? "Missing, stale or invalid room capture; black fallback" : "Uploading room capture";
                        break;
                }
            }
            if (_capture != null)
            {
                _capture.Step(); PeakGpuBytes = Math.Max(PeakGpuBytes, ResidentBytes + CaptureGpuBytes);
                Status = $"Capturing room: {_capture.CompletedFaces}/6 faces";
                if (_capture.Complete)
                {
                    var faces = _capture.Faces; var input = _input!;
                    _retire.Add(_capture); _capture = null; _input = null;
                    Start(token =>
                    {
                        var filtered = RoomReflectionCapture.Filter(faces, input.Snapshot.Settings.Resolution, token);
                        if (input.Snapshot.Key(token) != input.Key) throw new InvalidDataException("Sources changed during capture; rebuild required.");
                        ReflectionProbeCache.Save(ReflectionProbeCache.PathFor(input.Snapshot.LevelPath, input.Snapshot.Probe.Id), input.Key, filtered, token);
                        return new Result(input.Key, filtered);
                    });
                    Status = "Filtering and saving room capture";
                }
            }
            if (_data != null && (_ready != null || (_retiringEnvironmentBytes == 0 && !_retire.OfType<ReflectionEnvironment>().Any())) &&
                world.UploadReflection(_data, ref _pending))
            {
                if (_ready != null) _retire.Add(_ready);
                _ready = _pending; _pending = null; _data = null; _key = _pendingKey; Status = "Room capture ready";
            }
        }
        catch (Exception ex) { Cancel(); Status = "Room capture failed: " + ex.Message; }
        PeakGpuBytes = Math.Max(PeakGpuBytes, ResidentBytes + CaptureGpuBytes);
        bool validLights = _snapshot == null || _snapshot.FixedLightIds.All(lightId =>
            editor.LevelFile.UsePointLights && editor.LevelFile.Entities.Find(e => e.Id == lightId) is { } light && (state?.IsEnabled(light) ?? light.LightEnabled));
        _compatible=validLights;
        world.Reflection = id.Length > 0 && PreviewEnabled && validLights && editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr ? _ready : null;
        if (id.Length > 0) world.EnvironmentPreset = ReflectionPreset.None;
        if (!validLights && _ready != null) Status = "Fixed light state changed; black fallback";
        else if (validLights && _ready != null && !Busy) Status = "Room capture ready";
    }

    public void BeginCapture(LevelEditorController editor, Func<string,string> resolve, string? probeOverride = null)
    {
        if (editor.LevelFile.LightingScenarios is { Count: > 0 }) editor=_scenarios.Select(editor,null);
        if (editor.LevelFile.AutomaticRoomReflections && probeOverride == null)
        {
            if (_rooms == null) { Cancel(); DropReady(); _rooms=new(); }
            _rooms.BeginCapture(editor,resolve); return;
        }
        Cancel();
        try
        {
            var snapshot = ReflectionSnapshot.Create(editor.LevelFile, editor.LevelPath, probeOverride ?? editor.LevelFile.ReflectionProbeId ?? "", resolve);
            if (_snapshot == null || !_snapshot.Json.AsSpan().SequenceEqual(snapshot.Json) || _stamp != snapshot.Stamp()) DropReady();
            _snapshot = snapshot; _stamp = snapshot.Stamp(); _path = editor.LevelPath; _id = snapshot.Probe.Id; _revision = editor.Revision;
            Start(token => ReflectionCaptureInput.Prepare(snapshot, token)); Status = "Preparing fixed capture dependencies";
        }
        catch (Exception ex) { Status = "Cannot capture: " + ex.Message; }
    }
    public void Reload() { _revision = -1; _rooms?.Reload(); _retainedScenarioRooms?.Reload(); }
    private void Start(Func<CancellationToken, object?> action)
    {
        _cancel?.Dispose(); _cancel = new(); var token = _cancel.Token;
        _work = Task.Run(() => action(token), token);
    }
    private void DropReady() { if (_ready != null) _retire.Add(_ready); _ready = null; _key = ""; }
    public void Cancel()
    {
        _rooms?.Cancel();
        _cancel?.Cancel(); var cancel = _cancel; var work = _work;
        if (work != null) _ = work.ContinueWith(t => { _ = t.Exception; cancel?.Dispose(); }, TaskScheduler.Default);
        else cancel?.Dispose();
        _work = null; _cancel = null; _data = null; _input = null;
        if (_capture != null) _retire.Add(_capture);
        if (_pending != null) _retire.Add(_pending);
        _capture = null; _pending = null; Status = "Capture cancelled";
    }
    public void ReportError(string message) { Status=message; _rooms?.ReportError(message); }
    private sealed class Retirement(IDisposable resource, Action completed) : IDisposable
    { public void Dispose() { resource.Dispose(); completed(); } }
    public void Dispose()
    {
        Cancel(); _rooms?.Dispose(); _rooms=null; _retainedScenarioRooms?.Dispose(); _retainedScenarioRooms=null;
        DropReady(); foreach (var resource in _retire) resource.Dispose(); _retire.Clear();
    }
}

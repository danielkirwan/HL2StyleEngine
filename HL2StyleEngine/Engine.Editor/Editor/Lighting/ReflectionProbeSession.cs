using System.Diagnostics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed class ReflectionProbeSession : IDisposable
{
    private sealed record Result(string Key, ReflectionEnvironmentData? Data);
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
    public string Status { get; private set; } = "No room probe selected";
    public bool Busy => _work != null || _capture != null || _data != null;
    public bool Ready => _ready != null;
    public bool ShowBounds;
    public bool PreviewEnabled = true;
    public int CompletedFaces => _capture?.CompletedFaces ?? 0;
    public long ResidentBytes => (_ready?.ResidentBytes ?? 0) + (_pending?.ResidentBytes ?? 0);
    public long PendingCpuBytes => _data?.PayloadBytes ?? 0;
    public long CaptureGpuBytes => _capture?.GpuBytes ?? 0;
    public long PeakGpuBytes { get; private set; }

    public void Update(LevelEditorController editor, Func<string,string> resolve, BasicWorldRenderer world, Renderer renderer, LevelLightState? state = null)
    {
        foreach (var resource in _retire) renderer.RetireAfterFrame(resource);
        _retire.Clear();
        string id = editor.LevelFile.ReflectionProbeId ?? "";
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
                    _snapshot = ReflectionSnapshot.Create(editor.LevelFile, _path, id, resolve); _stamp = _snapshot.Stamp();
                    var snapshot = _snapshot;
                    Start(token => { string key = snapshot.Key(token); return new Result(key, ReflectionProbeCache.Load(ReflectionProbeCache.PathFor(snapshot.LevelPath, snapshot.Probe.Id), key)); });
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
            if (_data != null && world.UploadReflection(_data, ref _pending))
            {
                if (_ready != null) _retire.Add(_ready);
                _ready = _pending; _pending = null; _data = null; _key = _pendingKey; Status = "Room capture ready";
            }
        }
        catch (Exception ex) { Cancel(); Status = "Room capture failed: " + ex.Message; }
        PeakGpuBytes = Math.Max(PeakGpuBytes, ResidentBytes + CaptureGpuBytes);
        bool validLights = _snapshot == null || _snapshot.FixedLightIds.All(lightId =>
            editor.LevelFile.UsePointLights && editor.LevelFile.Entities.Find(e => e.Id == lightId) is { } light && (state?.IsEnabled(light) ?? light.LightEnabled));
        world.Reflection = id.Length > 0 && PreviewEnabled && validLights && editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr ? _ready : null;
        if (id.Length > 0) world.EnvironmentPreset = ReflectionPreset.None;
        if (!validLights && _ready != null) Status = "Fixed light state changed; black fallback";
        else if (validLights && _ready != null && !Busy) Status = "Room capture ready";
    }

    public void BeginCapture(LevelEditorController editor, Func<string,string> resolve)
    {
        Cancel();
        try
        {
            var snapshot = ReflectionSnapshot.Create(editor.LevelFile, editor.LevelPath, editor.LevelFile.ReflectionProbeId ?? "", resolve);
            if (_snapshot == null || !_snapshot.Json.AsSpan().SequenceEqual(snapshot.Json) || _stamp != snapshot.Stamp()) DropReady();
            _snapshot = snapshot; _stamp = snapshot.Stamp(); _path = editor.LevelPath; _id = snapshot.Probe.Id; _revision = editor.Revision;
            Start(token => ReflectionCaptureInput.Prepare(snapshot, token)); Status = "Preparing fixed capture dependencies";
        }
        catch (Exception ex) { Status = "Cannot capture: " + ex.Message; }
    }
    public void Reload() => _revision = -1;
    private void Start(Func<CancellationToken, object?> action)
    {
        _cancel?.Dispose(); _cancel = new(); var token = _cancel.Token;
        _work = Task.Run(() => action(token), token);
    }
    private void DropReady() { if (_ready != null) _retire.Add(_ready); _ready = null; _key = ""; }
    public void Cancel()
    {
        _cancel?.Cancel(); var cancel = _cancel; var work = _work;
        if (work != null) _ = work.ContinueWith(t => { _ = t.Exception; cancel?.Dispose(); }, TaskScheduler.Default);
        else cancel?.Dispose();
        _work = null; _cancel = null; _data = null; _input = null;
        if (_capture != null) _retire.Add(_capture);
        if (_pending != null) _retire.Add(_pending);
        _capture = null; _pending = null; Status = "Capture cancelled";
    }
    public void ReportError(string message) => Status = message;
    public void Dispose()
    {
        Cancel(); DropReady(); foreach (var resource in _retire) resource.Dispose(); _retire.Clear();
    }
}

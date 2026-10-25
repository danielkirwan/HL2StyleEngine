using System.Diagnostics;
using System.Numerics;
using Engine.Core.Serialization;
using Engine.Render;
using Engine.Editor.Level;

namespace Engine.Editor.Editor;

internal sealed class ReflectionRoomSession : IDisposable
{
    private readonly Dictionary<string, ReflectionProbeSession> _sessions = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _retired = new();
    private ReflectionResidency _residency = new();
    private ReflectionRoomLayout? _layout;
    private ReflectionRooms? _bindings;
    private ReflectionEnvironment?[] _bound = [];
    private string[] _wanted = [];
    private byte[] _sourceJson = [];
    private Func<string, string> _snapshotResolver = path => throw new InvalidDataException("Reflection sources are not prepared: " + path);
    private long _revision = -1, _retiringBytes;
    private int _retiringSlots;
    private string _path = "", _error = "", _captureId = "";
    private bool _captureRequested;
    public bool Busy => _captureRequested || _wanted.Any(id => !_sessions.ContainsKey(id)) || _sessions.Values.Any(s => s.Busy);
    public bool Ready => _layout != null && _wanted.All(id => _sessions.TryGetValue(id, out var s) && s.Ready);
    public int CompletedFaces => _sessions.Values.Sum(s => s.CompletedFaces);
    public long ResidentBytes => _sessions.Values.Sum(s => s.ResidentBytes) + (_bindings?.MetadataBytes ?? 0) + _retiringBytes;
    public long PendingCpuBytes => _sessions.Values.Sum(s => s.PendingCpuBytes);
    public long CaptureGpuBytes => _sessions.Values.Sum(s => s.CaptureGpuBytes);
    public long PeakGpuBytes { get; private set; }
    public int ResidentCount => _sessions.Count;
    public int RetiringCount => _retiringSlots;
    public int Loads { get; private set; }
    public int Evictions { get; private set; }
    public string Status => _error.Length > 0 ? _error :
        $"Automatic rooms: {_sessions.Values.Count(s => s.Ready)}/{_wanted.Length} ready; {_layout?.Rooms.Length ?? 0} authored; {_retiringSlots} retiring; {Loads} loads / {Evictions} evictions" +
        (_sessions.Values.Any(s => s.Ready && s.CompatibleEnvironment == null) ? "; incompatible lights use black fallback" : "") +
        (_sessions.Values.Any(s => !s.Ready || s.Busy) ? "; " + string.Join("; ", _sessions.Values.Where(s => !s.Ready || s.Busy).Select(s => s.Status)) : "");

    private void Synchronize(LevelEditorController editor, Func<string, string> resolve)
    {
        if (_revision == editor.Revision && _path == editor.LevelPath) return;
        _revision = editor.Revision; _path = editor.LevelPath; _error = ""; _layout = null; _wanted = [];
        RetireBindings();
        foreach (string id in _sessions.Keys.ToArray()) Evict(id);
        _residency = new();
        try
        {
            if (editor.LevelFile.ReflectionStreaming is { } settings) ReflectionResidency.Validate(settings);
            _layout = ReflectionRoomLayout.Create(editor);
            // One immutable revision shared by bounded worker loads, never a live editor object.
            _sourceJson = StructuredData.WriteJson(editor.LevelFile);
            var paths = editor.LevelFile.Entities.Select(e => e.MeshPath).Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, resolve, StringComparer.OrdinalIgnoreCase);
            _snapshotResolver = path => paths[path];
        }
        catch (Exception ex) { _error = "Automatic reflections unavailable: " + ex.Message; }
    }

    public void Update(LevelEditorController editor, Func<string, string> resolve, BasicWorldRenderer world,
        Renderer renderer, LevelLightState? state, bool preview, Vector3? position)
    {
        Synchronize(editor, resolve);
        if (_layout != null)
        {
            int limit = _layout.Rooms.Length;
            int[] wanted;
            if (editor.LevelFile.ReflectionStreaming is { } settings && position.HasValue)
            {
                limit = settings.MaxResidentProbes;
                var resident = Enumerable.Range(0, _layout.Rooms.Length).Where(i => _sessions.ContainsKey(_layout.Rooms[i].Id)).ToHashSet();
                int capture = Array.FindIndex(_layout.Rooms, r => r.Id == _captureId);
                wanted = _residency.Select(_layout, settings, position.Value,
                    Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, resident, capture);
            }
            else wanted = Enumerable.Range(0, _layout.Rooms.Length).ToArray();
            _wanted = wanted.Select(i => _layout.Rooms[i].Id).ToArray();
            foreach (string id in _sessions.Keys.Where(id => !_wanted.Contains(id)).ToArray())
            {
                RetireBindings(); Evict(id);
            }
            // Retiring sessions occupy slots until the GPU finishes their last frame.
            foreach (string id in _wanted)
                if (!_sessions.ContainsKey(id) && _sessions.Count + _retiringSlots < limit)
                { _sessions.Add(id, new ReflectionProbeSession()); Loads++; }
            if (_captureRequested && _sessions.TryGetValue(_captureId, out var selected))
            { selected.BeginCapture(editor, _snapshotResolver, _captureId); _captureRequested = false; }
            foreach (string id in _wanted)
                if (_sessions.TryGetValue(id, out var session)) session.Update(editor, _snapshotResolver, world, renderer, state, id, sourceJson: _sourceJson);
            if (!_captureRequested && _sessions.TryGetValue(_captureId, out var captured) && !captured.Busy) _captureId = "";
            ReflectionEnvironment? Environment(string id) => _sessions.TryGetValue(id, out var s) ? s.CompatibleEnvironment : null;
            bool changed = _bindings == null;
            for (int i = 0; i < _layout.Rooms.Length && !changed; i++)
                changed = !ReferenceEquals(_bound[i], Environment(_layout.Rooms[i].Id));
            if (changed)
            {
                RetireBindings();
                _bound = _layout.Rooms.Select(r => Environment(r.Id)).ToArray();
                _bindings = world.CreateReflectionRooms(_layout.Rooms, _layout.Doors, _bound);
            }
        }
        foreach (var resource in _retired) renderer.RetireAfterFrame(resource);
        _retired.Clear();
        world.Reflection = null; world.EnvironmentPreset = ReflectionPreset.None;
        world.RoomReflections = preview && editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr ? _bindings : null;
        PeakGpuBytes = Math.Max(PeakGpuBytes, ResidentBytes + CaptureGpuBytes);
    }

    public void BeginCapture(LevelEditorController editor, Func<string, string> resolve)
    {
        Synchronize(editor, resolve);
        string id = editor.LevelFile.ReflectionProbeId ?? "";
        if (_layout?.Rooms.Any(r => r.Id == id) == true) { _captureId = id; _captureRequested = true; }
        else _error = "Select a valid room probe to capture.";
    }
    public void Cancel() { _captureId = ""; _captureRequested = false; foreach (var session in _sessions.Values) session.Cancel(); }
    public void Reload() { Cancel(); _revision = -1; }
    public void ReportError(string message) => _error = message;
    private void Evict(string id)
    {
        var session = _sessions[id]; session.Cancel(); _sessions.Remove(id); Evictions++;
        Retire(session, session.ResidentBytes + session.CaptureGpuBytes, slot: true);
    }
    private void RetireBindings()
    {
        if (_bindings != null) Retire(_bindings, _bindings.MetadataBytes);
        _bindings = null; _bound = [];
    }
    private void Retire(IDisposable resource, long bytes, bool slot = false)
    {
        _retiringBytes += bytes; if (slot) _retiringSlots++;
        _retired.Add(new Retirement(resource, () => { _retiringBytes -= bytes; if (slot) _retiringSlots--; }));
    }
    private sealed class Retirement(IDisposable resource, Action completed) : IDisposable
    { public void Dispose() { resource.Dispose(); completed(); } }
    public void Dispose()
    {
        _bindings?.Dispose(); foreach (var session in _sessions.Values) session.Dispose();
        foreach (var resource in _retired) resource.Dispose(); _retired.Clear(); _sessions.Clear();
    }
}

using Engine.Render;
using Engine.Editor.Level;

namespace Engine.Editor.Editor;

internal sealed class ReflectionRoomSession : IDisposable
{
    private readonly Dictionary<string,ReflectionProbeSession> _sessions=new(StringComparer.Ordinal);
    private readonly List<IDisposable> _retired=new();
    private ReflectionRoomLayout? _layout;
    private ReflectionRooms? _bindings;
    private ReflectionEnvironment?[] _bound=[];
    private long _revision=-1;
    private string _path="", _error="";
    public bool Busy => _sessions.Values.Any(s=>s.Busy);
    public bool Ready => _layout != null && _sessions.Count>0 && _sessions.Values.All(s=>s.Ready);
    public int CompletedFaces => _sessions.Values.Sum(s=>s.CompletedFaces);
    public long ResidentBytes => _sessions.Values.Sum(s=>s.ResidentBytes)+(_bindings?.MetadataBytes??0);
    public long PendingCpuBytes => _sessions.Values.Sum(s=>s.PendingCpuBytes);
    public long CaptureGpuBytes => _sessions.Values.Sum(s=>s.CaptureGpuBytes);
    public long PeakGpuBytes { get; private set; }
    public string Status => _error.Length>0 ? _error : Busy ? string.Join("; ",_sessions.Values.Where(s=>s.Busy).Select(s=>s.Status)) :
        $"Automatic rooms: {_sessions.Values.Count(s=>s.Ready)}/{_sessions.Count} ready; {_layout?.Doors.Length??0} doorway links" +
        (_sessions.Values.Any(s=>s.Ready && s.CompatibleEnvironment==null) ? "; incompatible lights use black fallback" : "") +
        (_sessions.Values.Any(s=>!s.Ready) ? "; "+string.Join("; ",_sessions.Where(s=>!s.Value.Ready).Select(s=>s.Value.Status)) : "");

    private void Synchronize(LevelEditorController editor)
    {
        if (_revision==editor.Revision && _path==editor.LevelPath) return;
        _revision=editor.Revision; _path=editor.LevelPath; _error=""; _layout=null;
        RetireBindings(); foreach(var session in _sessions.Values) { session.Cancel(); _retired.Add(session); } _sessions.Clear();
        try
        {
            _layout=ReflectionRoomLayout.Create(editor);
            foreach(var room in _layout.Rooms) _sessions.Add(room.Id,new ReflectionProbeSession());
        }
        catch(Exception ex) { _error="Automatic reflections unavailable: "+ex.Message; }
    }

    public void Update(LevelEditorController editor,Func<string,string> resolve,BasicWorldRenderer world,Renderer renderer,LevelLightState? state,bool preview)
    {
        Synchronize(editor);
        foreach(var resource in _retired) renderer.RetireAfterFrame(resource); _retired.Clear();
        foreach(var entry in _sessions) entry.Value.Update(editor,resolve,world,renderer,state,entry.Key);
        if (_layout != null)
        {
            bool changed=_bindings==null;
            for(int i=0;i<_layout.Rooms.Length && !changed;i++)
                changed=!ReferenceEquals(_bound[i],_sessions[_layout.Rooms[i].Id].CompatibleEnvironment);
            if(changed)
            {
                RetireBindings();
                _bound=_layout.Rooms.Select(r=>_sessions[r.Id].CompatibleEnvironment).ToArray();
                _bindings=world.CreateReflectionRooms(_layout.Rooms,_layout.Doors,_bound);
            }
        }
        world.Reflection=null; world.EnvironmentPreset=ReflectionPreset.None;
        world.RoomReflections=preview && editor.LevelFile.LightingModel==SurfaceLightingModel.Pbr ? _bindings : null;
        PeakGpuBytes=Math.Max(PeakGpuBytes,ResidentBytes+CaptureGpuBytes);
    }
    public void BeginCapture(LevelEditorController editor,Func<string,string> resolve)
    {
        Synchronize(editor);
        if (_sessions.TryGetValue(editor.LevelFile.ReflectionProbeId??"",out var session)) session.BeginCapture(editor,resolve,editor.LevelFile.ReflectionProbeId);
        else _error="Select a valid room probe to capture.";
    }
    public void Cancel() { foreach(var session in _sessions.Values) session.Cancel(); }
    public void Reload() { foreach(var session in _sessions.Values) session.Reload(); }
    public void ReportError(string message) => _error=message;
    private void RetireBindings() { if(_bindings!=null)_retired.Add(_bindings);_bindings=null;_bound=[]; }
    public void Dispose()
    {
        _bindings?.Dispose(); foreach(var session in _sessions.Values)session.Dispose();
        foreach(var resource in _retired)resource.Dispose();_retired.Clear();_sessions.Clear();
    }
}

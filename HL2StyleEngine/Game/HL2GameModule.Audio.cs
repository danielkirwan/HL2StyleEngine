using System.Numerics;
using Engine.Runtime.Audio;
using Engine.Runtime.Entities;
using Veldrid;

namespace Game;

public sealed partial class HL2GameModule
{
    private sealed class EmitterBinding(Entity entity, AudioEmitterDef definition)
    {
        public readonly Entity Entity = entity;
        public readonly AudioEmitterDef Definition = definition;
        public VoiceHandle Voice;
        public bool Started;
    }
    private AudioService? _audio;
    private readonly List<EmitterBinding> _audioEmitters = [];
    private readonly Dictionary<string, bool> _audioLoopIntent = new(StringComparer.Ordinal);
    private HashSet<string> _loopingCues = new(StringComparer.Ordinal);
    private string _audioLevel = "";
    private string _audioRoot = "";
    private bool _audioTestLevel;
    public AudioDiagnostics? AudioStatus => _audio?.Diagnostics;

    private void BindLevelAudio()
    {
        _audioEmitters.Clear();
        if (_audioLevel != _editor.LevelPath) { _audioLoopIntent.Clear(); _audioLevel = _editor.LevelPath; }
        string besideLevel = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_audioLevel)!, "..", "Audio"));
        string root = File.Exists(Path.Combine(besideLevel, "cues.json")) ? besideLevel : Path.Combine(_assetRoot, "Content", "Audio");
        if (_editor.AudioAuthoring?.Root != root)
        { _editor.AudioAuthoring?.Dispose(); _editor.AudioAuthoring = new(root); }
        _audioTestLevel = Path.GetFileName(_audioLevel).Equals("audioTest.json", StringComparison.OrdinalIgnoreCase);
        foreach (var pair in _runtimeDefinitions)
            if (pair.Value.AudioEmitter is { } emitter)
            {
                _audioEmitters.Add(new(pair.Key, emitter));
                _audioLoopIntent.TryAdd(pair.Key.Id, emitter.AutoPlay);
            }
        var activeIds = _audioEmitters.Select(b => b.Entity.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _audioLoopIntent.Keys.Where(id => !activeIds.Contains(id)).ToArray()) _audioLoopIntent.Remove(id);
        if (_audioEmitters.Count == 0) { _audio?.Dispose(); _audio = null; return; }
        _loopingCues.Clear();
        try
        {
            var file = SoundCueFile.Load(Path.Combine(root, "cues.json"));
            _loopingCues = file.Cues.Where(c => c.Loop).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { Console.Error.WriteLine("[Audio] " + ex.Message); }
        if (_audio != null && _audioRoot == root && _audio.Diagnostics.Error.Length == 0)
            _audio.Reload();
        else
        { _audio?.Dispose(); _audio = new(root); _audioRoot = root; }
    }
    private void UpdateAudio()
    {
        if (_audio == null) return;
        _audio.SetPaused(_editorEnabled || GameplayModalOpen);
        _audio.SetListener(new(_camera.Position, _camera.Forward, Vector3.UnitY));
        if (_audioTestLevel && !_editorEnabled && !GameplayModalOpen)
        {
            if (_inputState.WasPressed(Key.F7)) _audio.Play(new("relay_click"), _camera.Position + _camera.Forward * 3);
            if (_inputState.WasPressed(Key.F8))
            {
                bool enable = !_audioLoopIntent.Values.Any(v => v);
                foreach (var binding in _audioEmitters) _audioLoopIntent[binding.Entity.Id] = enable;
                ShowGameMessage(enable ? "Audio loops on" : "Audio loops off", 2);
            }
        }
        if (!_audio.Diagnostics.Loaded) return;
        foreach (var binding in _audioEmitters)
        {
            bool enabled = _audioLoopIntent.GetValueOrDefault(binding.Entity.Id, binding.Definition.AutoPlay);
            if (!enabled || binding.Entity.IsBroken || !_runtimeEntities.Contains(binding.Entity))
            { _audio.Stop(binding.Voice); binding.Voice = default; continue; }
            bool loop = _loopingCues.Contains(binding.Definition.Cue);
            if (!_editorEnabled && !GameplayModalOpen && !_audio.IsAlive(binding.Voice) && (!binding.Started || loop))
            {
                binding.Voice = _audio.Play(new(binding.Definition.Cue), binding.Entity.Transform.Position, binding.Definition.Gain);
                binding.Started |= binding.Voice.IsValid;
            }
            _audio.Move(binding.Voice, binding.Entity.Transform.Position);
        }
    }
    private void RestoreAudioIntent(Dictionary<string, bool>? saved)
    {
        _audio?.Reset();
        foreach (var binding in _audioEmitters)
        {
            binding.Voice = default;
            binding.Started = true; // Never replay historical one-shots on load.
            _audioLoopIntent[binding.Entity.Id] = saved?.GetValueOrDefault(binding.Entity.Id, binding.Definition.AutoPlay) ?? binding.Definition.AutoPlay;
        }
    }
    private Dictionary<string, bool> CaptureAudioIntent()
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var binding in _audioEmitters)
            if (_loopingCues.Contains(binding.Definition.Cue))
                result[binding.Entity.Id] = _audioLoopIntent.GetValueOrDefault(binding.Entity.Id, binding.Definition.AutoPlay);
        return result;
    }
}

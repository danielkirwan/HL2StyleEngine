using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Runtime.Audio;

public readonly record struct SoundCueId(string Value);
public readonly record struct VoiceHandle(int Slot, long Generation)
{
    public bool IsValid => Generation > 0;
}
public readonly record struct AudioListener(Vector3 Position, Vector3 Forward, Vector3 Up);
public readonly record struct SemanticNoise(string SourceId, Vector3 Position, float Radius, string Kind);
public enum AudioBus { Effects, Ambience, Music, UI, Dialogue }
public sealed class AudioEmitterDef
{
    public string Cue { get; set; } = "";
    public bool AutoPlay { get; set; } = true;
    public float Gain { get; set; } = 1;
}
public sealed class SoundVariant
{
    public string File { get; set; } = "";
    public float Weight { get; set; } = 1;
}
public sealed class SoundCue
{
    public string Id { get; set; } = "";
    public AudioBus Bus { get; set; }
    public bool Spatial { get; set; } = true;
    public bool Loop { get; set; }
    public bool Stream { get; set; }
    public float Gain { get; set; } = .7f;
    public float MinPitch { get; set; } = 1;
    public float MaxPitch { get; set; } = 1;
    public float MinDistance { get; set; } = 1;
    public float MaxDistance { get; set; } = 20;
    public int Priority { get; set; } = 50;
    public int MaxVoices { get; set; } = 4;
    public List<SoundVariant> Variants { get; set; } = [];
}
public sealed class SoundCueFile
{
    public int Version { get; set; } = 1;
    public List<SoundCue> Cues { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public static SoundCueFile Load(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Cue file exceeds 256 KiB.");
        var file = JsonSerializer.Deserialize<SoundCueFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Empty cue document.");
        if (file.Cues == null || file.Cues.Count > 128 || file.Cues.Any(c => c == null || c.Id == null ||
            c.Variants == null || c.Variants.Count > 16 || c.Variants.Any(v => v == null || v.File == null)))
            throw new InvalidDataException("Invalid cue document structure.");
        return file;
    }
    public void Validate(string root)
    {
        if (Version != 1 || Cues == null || Cues.Count > 128) throw new InvalidDataException("Unsupported cue version/count.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (var cue in Cues)
        {
            if (cue == null || string.IsNullOrWhiteSpace(cue.Id) || cue.Id.Length > 80 || !ids.Add(cue.Id))
                throw new InvalidDataException("Missing/duplicate cue ID.");
            if (!Enum.IsDefined(cue.Bus) || !Range(cue.Gain, 0, 1) || !Range(cue.MinPitch, .5f, 2) ||
                !Range(cue.MaxPitch, cue.MinPitch, 2) || !Range(cue.MinDistance, .1f, 100) ||
                !Range(cue.MaxDistance, cue.MinDistance + .01f, 500) || cue.Priority is < 0 or > 100 ||
                cue.MaxVoices is < 1 or > 32 || cue.Variants == null || cue.Variants.Count is < 1 or > 16)
                throw new InvalidDataException($"Invalid settings: {cue.Id}");
            foreach (var variant in cue.Variants)
            {
                if (variant == null || !Range(variant.Weight, .001f, 100)) throw new InvalidDataException($"Invalid weight: {cue.Id}");
                using var wave = new PcmWave(Resolve(root, variant.File));
                if (cue.Spatial && wave.Channels != 1) throw new InvalidDataException($"Spatial cues require mono: {cue.Id}");
                if (!cue.Stream && wave.Frames > wave.Rate * 10L) throw new InvalidDataException($"Use streaming for cues longer than 10 seconds: {cue.Id}");
            }
        }
    }
    public static bool Range(float value, float min, float max) => float.IsFinite(value) && value >= min && value <= max;
    public static string Resolve(string root, string file)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) throw new InvalidDataException("Audio file must be relative to Content/Audio.");
        string path = Path.GetFullPath(Path.Combine(root, file));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Audio file must be a WAV inside Content/Audio.");
        return path;
    }
}

public sealed record AudioDiagnostics(bool Loaded, bool DeviceReady, string Error, int Voices,
    int PeakRequests, long Rejected, long ResidentBytes, long StreamBytes, ulong Callbacks,
    double MaxCallbackMs, ulong CallbackOverruns, ulong StreamUnderruns, uint PeriodFrames);

public interface IAudioService : IDisposable
{
    AudioDiagnostics Diagnostics { get; }
    VoiceHandle Play(SoundCueId cue, Vector3 position, float gain = 1);
    bool IsAlive(VoiceHandle voice);
    void Move(VoiceHandle voice, Vector3 position);
    void Stop(VoiceHandle voice);
    void SetListener(AudioListener listener);
    void SetBusGain(AudioBus bus, float gain);
    void SetPaused(bool paused);
    void Reset();
}

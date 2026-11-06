using System.Numerics;
using System.Text.Json;
using Engine.Runtime.Audio;
using Engine.Editor.Level;

internal sealed partial class GameplaySoakChecks
{
    private bool _audioCheck;
    private readonly List<AudioDiagnostics> _audioSamples = [];
    private string _savedAudio = "";
    private static string PrepareAudioCopy(string root, string folder, string file)
    {
        string levels = Path.Combine(root, "Game/Content/Levels"), copy = Path.Combine(folder, "Levels");
        Directory.CreateDirectory(copy);
        foreach (string source in Directory.EnumerateFiles(levels, Path.GetFileNameWithoutExtension(file) + ".*"))
            File.Copy(source, Path.Combine(copy, Path.GetFileName(source)));
        string path = Path.Combine(copy, file);
        var level = JsonSerializer.Deserialize<LevelFile>(File.ReadAllText(path))!;
        var crate = level.Entities.First(e => e.Damageable);
        crate.AudioEmitter = new() { Cue = "machine_hum" };
        var second = level.Entities.First(e => e.Type == EntityTypes.PointLight);
        second.AudioEmitter = new() { Cue = "machine_hum" };
        var ambience = level.Entities.First(e => e.Type == EntityTypes.PlayerSpawn);
        ambience.AudioEmitter = new() { Cue = "basement_drone" };
        File.WriteAllText(path, JsonSerializer.Serialize(level));
        return path;
    }
    private void AudioAction(int local, int cycle)
    {
        var service = (AudioService)Field("_audio");
        var intent = (Dictionary<string, bool>)Field("_audioLoopIntent");
        if (local == 20)
        {
            Check(service.Diagnostics.Loaded && service.Diagnostics.Error == "", "Audio definitions did not load.");
            foreach (string id in intent.Keys.ToArray()) intent[id] = cycle % 3 != 1;
        }
        if (local is 60 or 120 or 300)
            for (int i = 0; i < 40; ++i) service.Play(new("relay_click"), _motor.Position + Vector3.UnitZ * 2);
        if (local == 480) _savedAudio = JsonSerializer.Serialize(Call("CaptureAudioIntent"));
        if (local is 540 or 1000)
            Check(JsonSerializer.Serialize(Call("CaptureAudioIntent")) == _savedAudio, "Audio loop intent changed after save/load.");
        if (local == 1100)
        {
            var d = service.Diagnostics; _audioSamples.Add(d);
            Check(d.Loaded && d.DeviceReady && d.Error == "", "Audio device or worker failed.");
            Check(d.Voices <= 3 && d.StreamUnderruns == 0, "Audio voice cleanup/prefetch failed.");
            Check(d.CallbackOverruns == 0, "Audio callback missed its deadline.");
            Check(d.ResidentBytes <= 16 * 1024 * 1024 && d.StreamBytes < 1024 * 1024, "Audio memory exceeded fixture limits.");
        }
    }
}

using System.Diagnostics;
using System.Numerics;
using static Engine.Runtime.Audio.NativeAudio;

namespace Engine.Runtime.Audio;

// One control worker owns all native handles/files. Public calls only update a
// bounded mailbox. No C# delegate is registered on the real-time audio callback.
public sealed class AudioService : IAudioService
{
    public const int VoiceLimit = 32, StreamLimit = 4;
    private struct Request
    {
        public long Generation;
        public SoundCue? Cue;
        public Vector3 Position;
        public float Gain;
    }
    private sealed class Playing
    {
        public long Generation;
        public uint Voice;
        public nint Source;
        public SoundCue Cue = null!;
        public PcmWave? Stream;
        public float[]? Block;
        public long StreamBytes;
    }
    private static long nextGeneration;
    private readonly object sync = new();
    private readonly Request[] requests = new Request[VoiceLimit];
    private readonly float[] buses = [1, .65f, .5f, .8f, 1];
    private readonly Thread worker;
    private readonly ManualResetEventSlim quit = new(false);
    private readonly string root;
    private readonly bool enableOutput;
    private Dictionary<string, SoundCue> cues = new(StringComparer.Ordinal);
    private AudioListener listener = new(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);
    private bool paused, disposed;
    private int peak, outputRequest = -1, reloadRequested;
    private long rejected;
    private AudioDiagnostics diagnostics = new(false, false, "Loading cues", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public AudioDiagnostics Diagnostics => Volatile.Read(ref diagnostics);
    public AudioService(string audioRoot, bool output = true)
    {
        root = Path.GetFullPath(audioRoot); enableOutput = output;
        worker = new Thread(Run) { Name = "HS2 Audio Control", IsBackground = true };
        worker.Start();
    }
    public VoiceHandle Play(SoundCueId id, Vector3 position, float gain = 1)
    {
        lock (sync)
        {
            if (disposed || !Finite(position) || !SoundCueFile.Range(gain, 0, 1) ||
                !cues.TryGetValue(id.Value ?? "", out var cue)) return default;
            int index = -1, count = 0, streams = 0, occupied = 0;
            for (int i = 0; i < requests.Length; ++i)
            {
                ref var item = ref requests[i];
                if (item.Generation == 0) { if (index < 0) index = i; continue; }
                occupied++;
                if (item.Cue == cue) count++;
                if (item.Cue!.Stream) streams++;
            }
            if (count >= cue.MaxVoices || (cue.Stream && streams >= StreamLimit)) { rejected++; return default; }
            if (index < 0)
            {
                // Only a strictly higher-priority request may steal an existing voice.
                int lowest = cue.Priority;
                for (int i = 0; i < requests.Length; ++i)
                    if (requests[i].Cue!.Priority < lowest) { lowest = requests[i].Cue!.Priority; index = i; }
                if (index < 0) { rejected++; return default; }
            }
            long generation = Interlocked.Increment(ref nextGeneration);
            requests[index] = new() { Generation = generation, Cue = cue, Position = position, Gain = gain };
            peak = Math.Max(peak, Math.Min(VoiceLimit, occupied + 1));
            return new(index, generation);
        }
    }
    public bool IsAlive(VoiceHandle h) { lock (sync) return Matches(h); }
    private bool Matches(VoiceHandle h) => !disposed && h.IsValid && h.Slot >= 0 && h.Slot < VoiceLimit && requests[h.Slot].Generation == h.Generation;
    public void Move(VoiceHandle h, Vector3 position) { lock (sync) if (Matches(h) && Finite(position)) requests[h.Slot].Position = position; }
    public void Stop(VoiceHandle h) { lock (sync) if (Matches(h)) requests[h.Slot] = default; }
    public void Reset() { lock (sync) Array.Clear(requests); }
    public void Reload()
    {
        lock (sync)
        {
            if (disposed) return;
            Array.Clear(requests); cues.Clear();
            Interlocked.Exchange(ref reloadRequested, 1);
            Volatile.Write(ref diagnostics, diagnostics with { Loaded = false });
        }
    }
    public void SetListener(AudioListener value)
    {
        if (!Finite(value.Position) || !Finite(value.Forward) || !Finite(value.Up) ||
            value.Forward.LengthSquared() < .001f || Vector3.Cross(value.Forward, value.Up).LengthSquared() < .001f) return;
        lock (sync) listener = value with { Forward = Vector3.Normalize(value.Forward), Up = Vector3.Normalize(value.Up) };
    }
    public void SetBusGain(AudioBus bus, float gain) { lock (sync) if (Enum.IsDefined(bus) && SoundCueFile.Range(gain, 0, 1)) buses[(int)bus] = gain; }
    public void SetPaused(bool value) { lock (sync) paused = value; }
    public void ReconnectOutput() => Interlocked.Exchange(ref outputRequest, 1);
    public void SuspendOutputForTest() => Interlocked.Exchange(ref outputRequest, 0);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private void Run()
    {
        nint native = 0; long residentBytes = 0; ulong finishedUnderruns = 0;
        Dictionary<string, nint> samples = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> previousVariants = new(StringComparer.Ordinal);
        Playing?[] playing = new Playing?[VoiceLimit];
        Request[] batch = new Request[VoiceLimit]; float[] bus = new float[buses.Length];
        ulong[] stats = new ulong[6]; var random = new Random(7301);
        string error = ""; bool loaded = false, wantOutput = enableOutput;
        var timer = Stopwatch.StartNew(); double nextRecovery = 0, nextReport = 0;
        try
        {
            var definitions = ReadDefinitions();
            if (quit.IsSet) return;
            native = hs2_audio_create(enableOutput ? 1 : 0);
            if (native == 0) throw new InvalidOperationException("Native audio initialization failed.");
            LoadSamples(definitions);
            while (!quit.IsSet)
            {
                if (Interlocked.Exchange(ref reloadRequested, 0) != 0)
                {
                    // Level lifetime is not output-device lifetime. Revalidate assets
                    // off the game thread without repeated Windows device creation.
                    loaded = false;
                    foreach (var voice in playing) if (voice != null) Release(voice);
                    Array.Clear(playing);
                    foreach (var sample in samples.Values) hs2_audio_free(sample);
                    samples.Clear(); residentBytes = 0; previousVariants.Clear();
                    LoadSamples(ReadDefinitions()); nextReport = 0;
                }
                int output = Interlocked.Exchange(ref outputRequest, -1);
                if (output >= 0)
                { wantOutput = output == 1; hs2_audio_output(native, output); nextRecovery = timer.Elapsed.TotalSeconds + 2; }
                if (wantOutput && hs2_audio_ready(native) == 0 && timer.Elapsed.TotalSeconds >= nextRecovery)
                { hs2_audio_output(native, 1); nextRecovery = timer.Elapsed.TotalSeconds + 2; }
                AudioListener currentListener; bool currentPause;
                lock (sync)
                { requests.CopyTo(batch, 0); buses.CopyTo(bus, 0); currentListener = listener; currentPause = paused; }
                for (int i = 0; i < VoiceLimit; ++i)
                {
                    ref var request = ref batch[i];
                    var current = playing[i];
                    if (current != null && current.Generation != request.Generation) { Release(current); playing[i] = current = null; }
                    if (request.Generation == 0) continue;
                    if (current == null)
                    {
                        try
                        {
                            var cue = request.Cue!;
                            int variant = SelectVariant(cue, previousVariants.GetValueOrDefault(cue.Id, -1), random);
                            previousVariants[cue.Id] = variant;
                            string path = SoundCueFile.Resolve(root, cue.Variants[variant].File);
                            current = new Playing { Generation = request.Generation, Cue = cue };
                            playing[i] = current;
                            if (cue.Stream)
                            {
                                current.Stream = new PcmWave(path); current.Block = new float[16384];
                                current.Source = hs2_audio_source((uint)current.Stream.Rate, (uint)current.Stream.Channels, 24000, 1, null);
                                current.StreamBytes = 24000L * current.Stream.Channels * 4 + 16384 * 4 + 32768;
                                if (current.Source == 0) throw new InvalidOperationException("Stream buffer allocation failed.");
                                Feed(current);
                            }
                            else current.Source = samples[path];
                            float pitch = cue.MinPitch + random.NextSingle() * (cue.MaxPitch - cue.MinPitch);
                            current.Voice = hs2_audio_play(native, current.Source, cue.Spatial ? 1 : 0, cue.Loop ? 1 : 0,
                                request.Position.X, request.Position.Y, request.Position.Z,
                                request.Gain * cue.Gain * bus[(int)cue.Bus], pitch, cue.MinDistance, cue.MaxDistance);
                            if (current.Voice == 0) throw new InvalidOperationException("Native voice allocation failed.");
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
                        { error = ex.Message; if (current != null) Release(current); playing[i] = null; Retire(i, request.Generation); continue; }
                    }
                    if (hs2_audio_valid(native, current.Voice) == 0)
                    { Release(current); playing[i] = null; Retire(i, request.Generation); continue; }
                    if (current.Stream != null) Feed(current);
                    bool pauseVoice = currentPause && current.Cue.Bus is not (AudioBus.UI or AudioBus.Dialogue);
                    hs2_audio_voice(native, current.Voice, request.Position.X, request.Position.Y, request.Position.Z,
                        request.Gain * current.Cue.Gain * bus[(int)current.Cue.Bus], pauseVoice ? 1 : 0);
                }
                var p = currentListener.Position; var f = currentListener.Forward; var u = currentListener.Up;
                hs2_audio_listener(native, p.X, p.Y, p.Z, f.X, f.Y, f.Z, u.X, u.Y, u.Z);
                if (timer.Elapsed.TotalSeconds >= nextReport)
                {
                    nextReport = timer.Elapsed.TotalSeconds + .25;
                    hs2_audio_stats(native, stats);
                    long streamBytes = 0; ulong underruns = finishedUnderruns;
                    foreach (var voice in playing) if (voice?.Stream != null)
                    { streamBytes += voice.StreamBytes; underruns += hs2_audio_starved(voice.Source); }
                    Volatile.Write(ref diagnostics, new(loaded, hs2_audio_ready(native) != 0, error, (int)stats[4],
                        peak, Interlocked.Read(ref rejected), residentBytes, streamBytes, stats[0], stats[1] / 1e6, stats[2], underruns, (uint)stats[5]));
                }
                quit.Wait(5);
            }
        }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        finally
        {
            foreach (var voice in playing) if (voice != null) Release(voice);
            foreach (var sample in samples.Values) hs2_audio_free(sample);
            if (native != 0) hs2_audio_destroy(native);
            lock (sync) { cues = new(StringComparer.Ordinal); Array.Clear(requests); }
            Volatile.Write(ref diagnostics, new(false, false, error, 0, peak, rejected, 0, 0, stats[0], stats[1] / 1e6, stats[2], finishedUnderruns, 0));
        }
        void Retire(int index, long generation)
        { lock (sync) if (requests[index].Generation == generation) requests[index] = default; }
        SoundCueFile ReadDefinitions()
        {
            var file = SoundCueFile.Load(Path.Combine(root, "cues.json"));
            file.Validate(root); return file;
        }
        void LoadSamples(SoundCueFile definitions)
        {
            foreach (var cue in definitions.Cues)
                if (!cue.Stream) foreach (var variant in cue.Variants)
                {
                    if (quit.IsSet) return;
                    string path = SoundCueFile.Resolve(root, variant.File);
                    if (samples.ContainsKey(path)) continue;
                    using var wave = new PcmWave(path);
                    long bytes = wave.Frames * wave.Channels * sizeof(float);
                    if (residentBytes + bytes > 16 * 1024 * 1024) throw new InvalidDataException("Resident audio budget exceeds 16 MiB.");
                    var source = hs2_audio_source((uint)wave.Rate, (uint)wave.Channels, (uint)wave.Frames, 0, wave.ReadAll());
                    if (source == 0) throw new InvalidOperationException("Could not create audio sample.");
                    samples.Add(path, source); residentBytes += bytes;
                }
            lock (sync)
                if (Volatile.Read(ref reloadRequested) == 0)
                { cues = definitions.Cues.ToDictionary(c => c.Id, StringComparer.Ordinal); loaded = true; }
        }
        void Release(Playing voice)
        {
            if (voice.Voice != 0) hs2_audio_stop(native, voice.Voice);
            if (voice.Stream != null)
            {
                if (voice.Source != 0) { finishedUnderruns += hs2_audio_starved(voice.Source); hs2_audio_free(voice.Source); }
                voice.Stream.Dispose();
            }
        }
    }
    private static void Feed(Playing playing)
    {
        var stream = playing.Stream!; var block = playing.Block!;
        // At most half a second of PCM per voice per service tick.
        int capacity = (int)hs2_audio_space(playing.Source);
        while (capacity > 0)
        {
            int read = stream.Read(block, Math.Min(capacity, block.Length / stream.Channels));
            if (read == 0)
            {
                if (playing.Cue.Loop) { stream.Rewind(); continue; }
                hs2_audio_write(playing.Source, block, 0, 1); break;
            }
            hs2_audio_write(playing.Source, block, (uint)read, 0); capacity -= read;
        }
    }
    public static int SelectVariant(SoundCue cue, int previous, Random random)
    {
        float sum = 0;
        for (int i = 0; i < cue.Variants.Count; ++i) if (i != previous || cue.Variants.Count == 1) sum += cue.Variants[i].Weight;
        float choice = random.NextSingle() * sum;
        for (int i = 0; i < cue.Variants.Count; ++i)
        {
            if (i == previous && cue.Variants.Count > 1) continue;
            choice -= cue.Variants[i].Weight; if (choice <= 0) return i;
        }
        return cue.Variants.Count - 1;
    }
    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        quit.Set(); worker.Join(); quit.Dispose();
    }
}

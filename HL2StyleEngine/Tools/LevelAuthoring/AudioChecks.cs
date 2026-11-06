using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Runtime.Audio;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using static Engine.Runtime.Audio.NativeAudio;

internal static class AudioChecks
{
    public static void Run(string root, bool device)
    {
        string audioRoot = Path.Combine(root, "Game/Content/Audio");
        string output = Path.Combine(AppContext.BaseDirectory, "qa", "audio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        List<string> checks = [];
        void Check(bool passed, string name) { if (!passed) throw new Exception(name); checks.Add(name); Console.WriteLine("PASS " + name); }
        var file = SoundCueFile.Load(Path.Combine(audioRoot, "cues.json")); file.Validate(audioRoot);
        Check(true, "Versioned authored cues and PCM references validate");
        void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException) { rejected = true; } Check(rejected, name); }
        Reject(() => SoundCueFile.Resolve(audioRoot, "../bad.wav"), "Traversal rejected");
        Reject(() => SoundCueFile.Resolve(audioRoot, "C:/bad.wav"), "Absolute path rejected");
        file.Version = 2; Reject(() => file.Validate(audioRoot), "Unsupported version rejected"); file.Version = 1;
        file.Cues.Add(file.Cues[0]); Reject(() => file.Validate(audioRoot), "Duplicate ID rejected"); file.Cues.RemoveAt(file.Cues.Count - 1);
        file.Cues[0].Gain = float.NaN; Reject(() => file.Validate(audioRoot), "Non-finite settings rejected"); file.Cues[0].Gain = .8f;
        string original = file.Cues[0].Variants[0].File; file.Cues[0].Variants[0].File = "missing.wav";
        Reject(() => file.Validate(audioRoot), "Missing sample rejected"); file.Cues[0].Variants[0].File = original;
        int previous = -1; var random = new Random(73); int[] counts = new int[3];
        for (int i = 0; i < 3000; ++i)
        { int next = AudioService.SelectVariant(file.Cues[0], previous, random); if (next == previous) throw new Exception("Repeat prevention failed"); previous = next; counts[next]++; }
        Check(counts.All(n => n > 700), "Weighted variants cover all choices without immediate repeats");

        var editor = new LevelEditorController();
        editor.LoadFromMemory(Path.Combine(output, "editor.json"), new LevelFile());
        editor.AddEntityFromTemplate(new() { Id = "audio-qa", Type = EntityTypes.Prop, Name = "Audio QA" });
        Check(editor.AssignSelectedAudio("machine_hum"), "Emitter assignment");
        Check(editor.Undo() && editor.LevelFile.Entities[0].AudioEmitter == null, "Emitter undo");
        Check(editor.Redo() && editor.LevelFile.Entities[0].AudioEmitter?.Cue == "machine_hum", "Emitter redo");
        editor.Save(); editor.Reload();
        Check(editor.LevelFile.Entities[0].AudioEmitter?.Cue == "machine_hum", "Emitter save/reopen");
        // Select again via a fresh duplicate after Reload, which intentionally clears selection.
        editor.AddEntityFromTemplate(editor.LevelFile.Entities[0]);
        Check(editor.TryBuildPrefabFromSelection("Audio QA", "", false, out var prefab) && prefab.Entities[0].AudioEmitter?.Cue == "machine_hum", "Emitter survives prefab capture");
        Check(JsonSerializer.Deserialize<LevelEntityDef>("{\"Id\":\"old\",\"Type\":\"Prop\"}")?.AudioEmitter == null, "Old entities remain compatible/silent");

        string lightingPath = Path.Combine(root, "Game/Content/Levels/sixRoomShadowTest.json");
        var lightingLevel = LevelIO.Load(lightingPath);
        string ResolveModel(string p) => Path.Combine(root, "Game", p);
        byte[] diffuseBefore = BakeSnapshot.Capture(lightingLevel, ResolveModel).Json;
        string probeId = lightingLevel.Entities.First(e => e.Type == EntityTypes.ReflectionProbe).Id;
        var reflectionBefore = ReflectionSnapshot.Create(lightingLevel, lightingPath, probeId, ResolveModel);
        foreach (var entity in lightingLevel.Entities) entity.AudioEmitter = new() { Cue = "machine_hum" };
        Check(diffuseBefore.SequenceEqual(BakeSnapshot.Capture(lightingLevel, ResolveModel).Json), "Audio-only authoring preserves diffuse cache inputs");
        var reflectionAfter = ReflectionSnapshot.Create(lightingLevel, lightingPath, probeId, ResolveModel);
        Check(reflectionBefore.Json.SequenceEqual(reflectionAfter.Json) &&
            reflectionBefore.DiffuseSnapshot!.Json.SequenceEqual(reflectionAfter.DiffuseSnapshot!.Json), "Audio-only authoring preserves reflection cache inputs");
        Check(lightingLevel.Entities.All(e => e.AudioEmitter != null), "Lighting snapshot cannot remove authored emitters");

        nint native = hs2_audio_create(0), source = 0, stream = 0;
        Check(native != 0, ".NET 10 x64 native ABI initializes without output");
        try
        {
            float[] sine = Enumerable.Range(0, 48000).Select(i => MathF.Sin(i * 2 * MathF.PI * 220 / 48000) * .2f).ToArray();
            source = hs2_audio_source(48000, 1, 48000, 0, sine);
            float[] mix = new float[24000 * 2];
            (double left, double right) Energy(float x, float z)
            {
                var voice = hs2_audio_play(native, source, 1, 1, x, 0, z, 1, 1, 1, 20);
                hs2_audio_voice(native, voice, x, 0, z, 1, 0);
                hs2_audio_listener(native, 0, 0, 0, 0, 0, 1, 0, 1, 0);
                hs2_audio_render(native, mix, 24000); hs2_audio_stop(native, voice);
                double left = 0, right = 0;
                for (int i = 2048; i < mix.Length; i += 2) { left += mix[i] * mix[i]; right += mix[i + 1] * mix[i + 1]; }
                return (left, right);
            }
            var rightEnergy = Energy(3, 3); var leftEnergy = Energy(-3, 3);
            Check(rightEnergy.right > rightEnergy.left * 2 && leftEnergy.left > leftEnergy.right * 2, "Listener handedness and left/right spatial panning");
            var near = Energy(0, 2); var far = Energy(0, 18);
            Check(far.left + far.right < (near.left + near.right) * .03, "Distance attenuation");
            var paused = hs2_audio_play(native, source, 0, 1, 0, 0, 0, 1, 1, 1, 20);
            hs2_audio_render(native, mix, 24000);
            Check(mix.All(v => v == 0), "Voices start paused, without a pre-pause audio burst"); hs2_audio_stop(native, paused);
            stream = hs2_audio_source(48000, 1, 24000, 1, null);
            hs2_audio_write(stream, sine, 24000, 0);
            var streamVoice = hs2_audio_play(native, stream, 0, 1, 0, 0, 0, 1, 1, 1, 20);
            hs2_audio_voice(native, streamVoice, 0, 0, 0, 1, 0);
            hs2_audio_render(native, mix, 12000);
            Check(hs2_audio_starved(stream) == 0, "Prefetched stream consumes without underrun");
            hs2_audio_render(native, mix, 24000);
            Check(hs2_audio_starved(stream) > 0 && mix.All(float.IsFinite), "Starved PCM ring supplies safe silence and increments diagnostics");
            hs2_audio_stop(native, streamVoice);
            for (int i = 0; i < 500; ++i)
            { uint v = hs2_audio_play(native, source, 0, 0, 0, 0, 0, 1, 1, 1, 20); hs2_audio_stop(native, v); }
            ulong[] stats = new ulong[6]; hs2_audio_stats(native, stats);
            Check(stats[4] == 0, "Repeated native voice creation/teardown releases all voices");
        }
        finally { if (stream != 0) hs2_audio_free(stream); if (source != 0) hs2_audio_free(source); hs2_audio_destroy(native); }

        AudioDiagnostics? live = null;
        string boundedRoot = Path.Combine(output, "bounded"); Directory.CreateDirectory(boundedRoot);
        File.Copy(Path.Combine(audioRoot, "relay_01.wav"), Path.Combine(boundedRoot, "sample.wav"));
        var boundedFile = new SoundCueFile { Cues = [
            new() { Id = "low", Spatial = false, Loop = true, MaxVoices = 32, Priority = 1, Variants = [new() { File = "sample.wav" }] },
            new() { Id = "high", Spatial = false, Loop = true, MaxVoices = 32, Priority = 100, Variants = [new() { File = "sample.wav" }] }] };
        File.WriteAllText(Path.Combine(boundedRoot, "cues.json"), JsonSerializer.Serialize(boundedFile, SoundCueFile.Json));
        using (var bounded = new AudioService(boundedRoot, output: false))
        {
            Wait(() => bounded.Diagnostics.Loaded, "Bounded service loading", () => bounded.Diagnostics.Error);
            var low = Enumerable.Range(0, 32).Select(_ => bounded.Play(new("low"), Vector3.Zero)).ToArray();
            Check(low.All(h => h.IsValid), "32 bounded logical slots accepted");
            for (int i = 0; i < 10000; ++i) if (bounded.Play(new("low"), Vector3.Zero).IsValid) throw new Exception("Unbounded requests");
            var high = bounded.Play(new("high"), Vector3.Zero);
            Check(high.IsValid && low.Count(bounded.IsAlive) == 31, "Higher priority steals only one lower-priority slot");
            Wait(() => bounded.Diagnostics.Voices == 32, "Bounded native voice realization");
            Check(bounded.Diagnostics.PeakRequests == 32 && bounded.Diagnostics.Rejected == 10000, "Request flood stays bounded and counts rejections");
            bounded.Reset(); Wait(() => bounded.Diagnostics.Voices == 0, "Bounded reset");
        }
        for (int cycle = 0; cycle < 12; ++cycle)
        {
            var cleanup = new AudioService(audioRoot, output: false);
            Wait(() => cleanup.Diagnostics.Loaded, "Repeated lifecycle startup");
            cleanup.Play(new("machine_hum"), Vector3.Zero); Thread.Sleep(20); cleanup.Dispose();
            Check(cleanup.Diagnostics.Voices == 0 && cleanup.Diagnostics.StreamBytes == 0 && cleanup.Diagnostics.ResidentBytes == 0,
                "Lifecycle " + cycle + " releases voices/samples/stream buffers");
        }
        if (device)
        {
            using var service = new AudioService(audioRoot);
            Wait(() => service.Diagnostics.Loaded, "Audio service loading", () => service.Diagnostics.Error);
            Wait(() => service.Diagnostics.DeviceReady, "Output device", () => service.Diagnostics.Error);
            service.SetListener(new(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY));
            var old = service.Play(new("machine_hum"), new(3, 0, 3)); service.Stop(old);
            var current = service.Play(new("machine_hum"), new(-3, 0, 3)); service.Stop(old); service.Move(old, new(99, 0, 0));
            Check(service.IsAlive(current) && !service.IsAlive(old), "Stale handles cannot stop a reused slot");
            var ambience = service.Play(new("basement_drone"), Vector3.Zero);
            Check(!service.Play(new("basement_drone"), Vector3.Zero).IsValid, "Cue concurrency rejects excess loops");
            service.SetPaused(true); service.Play(new("panel_confirm"), Vector3.Zero); Thread.Sleep(500);
            Check(service.IsAlive(current), "Pause preserves loop intent while UI remains available");
            service.SetPaused(false);
            for (int i = 0; i < 30; ++i)
            {
                service.Move(current, new(MathF.Sin(i * .2f) * 5, 0, 3));
                service.Play(new("relay_click"), new(0, 0, 3)); Thread.Sleep(150);
            }
            service.SuspendOutputForTest(); Wait(() => !service.Diagnostics.DeviceReady, "Simulated output suspension");
            Check(service.IsAlive(current), "Output suspension does not mutate logical loops");
            service.ReconnectOutput(); Wait(() => service.Diagnostics.DeviceReady, "Output recovery"); Thread.Sleep(800);
            live = service.Diagnostics;
            Check(live.Callbacks > 0 && live.ResidentBytes > 0 && live.StreamBytes > 0, "Output callback and bounded sample/stream telemetry");
            Check(live.StreamUnderruns == 0, "Live PCM prefetch has zero underruns");
            ulong callbacksBefore = live.Callbacks;
            service.Reload();
            Check(!service.IsAlive(current), "Asset reload invalidates old voices immediately");
            Wait(() => service.Diagnostics.Loaded && service.Diagnostics.Voices == 0 && service.Diagnostics.Callbacks > callbacksBefore,
                "Device-preserving asset reload", () => service.Diagnostics.Error);
            Check(service.Diagnostics.DeviceReady && service.Diagnostics.ResidentBytes == live.ResidentBytes && service.Diagnostics.StreamBytes == 0,
                "Asset reload revalidates samples and releases streams without recreating output");
            service.Reset(); Wait(() => service.Diagnostics.Voices == 0, "Level reset cleanup");
            Check(!service.IsAlive(current) && !service.IsAlive(ambience), "Level reset invalidates old handles");
        }
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { Checks = checks, DeviceTest = device, Live = live,
            PhysicalUnplug = "Not tested by this command", Hearing = "Requires human listening", Timestamp = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Audio checks passed: {checks.Count}; report: {output}/report.json");
    }
    private static void Wait(Func<bool> ready, string name, Func<string>? detail = null)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { if (timer.Elapsed.TotalSeconds > 12) throw new Exception(name + ": " + detail?.Invoke()); Thread.Sleep(25); }
    }
}

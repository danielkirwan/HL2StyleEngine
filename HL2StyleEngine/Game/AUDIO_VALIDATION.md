# Audio Validation - 6 November 2026

Current status: audio foundation ready for continued playtesting. All 48 focused
checks, both baseline BATs, three existing long controls, all 32 lighting
commands and two post-correction audio long runs passed. Earlier audio long
failures remain recorded below; their input-stage cause is not proven fixed.
Results are not blanket hardware/audio acceptance. C# remains the runtime; physical solver,
timestep, material/lighting parameters and existing source levels are unchanged.

## Automated Evidence

Commands use `.dotnet/dotnet.exe` with
`Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll`. Report folders
below are relative to `Tools/LevelAuthoring/bin/Release/net10.0/qa`.

| Command | Result | Report |
| --- | --- | --- |
| Release build `--no-restore` | 0 warnings/errors | Final console build, 15:36 UTC |
| `check-audio --device` | 46 checks passed | `audio-20261106-142126/report.json` |
| `check-audio --device`, after device-lifetime correction | 48 checks passed | `audio-20261106-151603/report.json` |
| `check-audio --device`, final error-handling build | 48 checks passed | `audio-20261106-153628/report.json` |
| `soak-audio` | 6 cycles/72 actions passed | `gameplay-soak-20261106-142150-939-native` |
| `RunGameplayStabilityTests.bat` | Passed, unchanged | `lighting-acceptance-20261106-142505-978-Baseline` |
| `RunFramePacingTests.bat` | Passed, unchanged | Same Baseline folder |
| `profile-frame-pacing --long --reference --no-debug` | 18 cycles/216 actions; 75.25 MiB private growth; 16.662/17.320 ms gameplay mean/P95; no >33.3 ms | `gameplay-soak-20261106-143547-586-native` |
| `profile-frame-pacing --long --no-debug` | 18 cycles/216 actions; 69.97 MiB; 16.661/16.894 ms; no >33.3 ms | `gameplay-soak-20261106-144155-972-native` |
| `soak-gameplay --native-ui --puzzles --long --memory-trace` | 18 cycles/216 actions; 63.10 MiB; 16.661/16.873 ms; no >33.3 ms | `gameplay-soak-20261106-144803-470-native` |
| `soak-audio --long`, device-preserving reload | Passed 18 cycles/216 actions; 65.96 MiB; 16.661/16.889 ms; zero >33.3 ms | `gameplay-soak-20261106-151641-238-native` |
| `soak-audio --long`, independent repeat | Passed 18 cycles/216 actions; 54.89 MiB; 16.661/16.898 ms; zero >33.3 ms | `gameplay-soak-20261106-152940-630-native` |
| `RunLightingAcceptance.ps1 -Phase Lighting` | 32 commands passed; protected inputs unchanged | `lighting-acceptance-20261106-152354-045-Lighting` |
| Release game publish and bounded load probe | DLL/licences/cues/six WAVs present; first playable presented in 4054.5 ms | `audio-publish-20261106/published-load.json` |

The exact BATs ran sequentially via `RunLightingAcceptance.ps1 -Phase Baseline`.
Its protected-input hashes are unchanged: source levels, shaders, physics and
fixed timestep. The BAT covers 54,497 geometry/contact checks, 1,000 collider
cases, 24 gameplay/startup checks, native/fallback/six-room soaks and reference/
streaming frame profiles. Native/fallback/six-room short private growth was
22.50/24.46/60.06 MiB; all three had zero gameplay intervals >33.3 ms.
The documented `RunLightingAcceptance.ps1 -Phase LongControls` also passed all
six commands with unchanged protected hashes:
`lighting-acceptance-20261106-143435-230-LongControls`. These are separate
workloads, not a measured causal before/after audio cost comparison.

The BATs and existing long controls preceded the audio-only device-lifetime
correction. Both final audio long runs and the full lighting suite followed it.
The final build additionally catches unreadable cue-file errors during game/editor
binding; its 48 focused checks were repeated. No performance workloads overlapped
builds or desktop audition. The lighting suite includes exact 600-step physics
replays for atmosphereTest and sixRoomShadowTest, legacy Game/F2/HS2Editor image
comparisons, shadow/floor/switch/cache checks and texture-transition/editor checks.

The focused suite covers schema/path/version/reference failures, weighted
non-repeating variants, entity assignment/undo/redo/save/reopen/prefab capture,
old silent data, cache input independence, native ABI, left/right PCM energy,
distance attenuation, paused startup, simulated starvation, 500 native voice
lifetimes, 10,000 rejected requests at capacity, priority stealing, 12 service
lifecycles, stale handles, pause, output stop/reopen, reset cleanup and reloading
samples while preserving the healthy output device.

Live focused output: 70,408 resident PCM bytes, 484,608 stream bytes, 512-frame
reported device period; final focused maximum observed callback 0.0480 ms, zero callback
deadline overruns/PCM-ring underruns. This measures callback execution, not
end-to-end acoustic latency or hardware underruns.

The short combined run uses an isolated copy of sixRoomShadowTest, normal
full-detail/VSync/native UI and the existing gravity/break/door/inventory/save/
reload actions. It adds loops and one-shot request bursts, and asserts loop
intent through save/load. Gameplay intervals: mean 16.661 ms, P95 16.987 ms,
zero >33.3 ms and >100 ms; private growth 32.59 MiB. Whole-run mean including
explicit loading is 16.74 ms (59.7 FPS). Do not present a VSync-limited result
as unlimited performance or every frame above 60 FPS.

Settled audio samples have 0-2 live voices, 0-484,608 stream bytes and no reported
underruns/overruns; maximum sampled callback 0.1277 ms. These snapshots are from
the current service after reload, not an uninterrupted whole-run device trace.

## Native And Human Checks

- Audacity 3.6.3 opened/played the generated stereo drone. Waveform and meters
  were visible; saved native project: `Tools/AudioAuthoring/review/basement_drone.aup3`.
- Private HS2Editor preserved docked layout/textured viewport. Audio tab,
  cue selection, audition (one active voice) and Stop (zero voices) were checked.
- Private audioTest game rendered normally. The user answered yes to hearing
  the hum/drone and F7 click/F8 loop test. This is human audibility evidence,
  not a formal headphone mix, loop-seam or crackle measurement.
- Representative screenshots: `Tools/AudioAuthoring/review/audacity-drone.png`,
  `editor-audio.png`, `game-audio.png`.
- Native copies were isolated under `qa/audio-editor-20261106-140221`; both
  processes were closed before performance tests. Original saves/layouts untouched.
- Waveform was not used for these renders; no native Waveform project is claimed.
- Physical device unplug/default-device changes and acoustic latency not tested.

## Retained Failures And Corrections

Initial offline mixer tests at approximately 13:48/13:49 UTC exposed native
heap corruption when a 24,000-frame validation request exceeded SoLoud's fixed
scratch capacity. The bridge now chunks every device/offline mix to at most
512 frames; subsequent offline/live/lifetime checks pass. Windows Application
Error evidence remains in the local event log; those crashes produced no final
JSON report. They are not hidden as successful tests.

The first audio soak (`gameplay-soak-20261106-141631-411-native`) failed the
unchanged 15-second load-readiness guard. Adding emitter metadata incorrectly
invalidated diffuse/reflection cache keys. Only playback metadata is now removed
from owned lighting snapshots; original scenes are not mutated. Regression
checks compare pre/post audio snapshot bytes, and the repeat passed. No shader,
lighting algorithm, cache format, physics or acceptance-limit change was made.

The first audio-enabled long run
(`gameplay-soak-20261106-145505-048-native`) completed all 18 cycles but failed
the unchanged clustered-hitch limit. Gameplay mean/P95 was 16.665/16.942 ms;
four intervals exceeded 33.3 ms, none exceeded 100 ms. Private growth was
82.15 MiB, model growth zero; sampled audio had no underruns/overruns and a
0.0708 ms maximum callback. Good averages do not make this a pass.

| Interval frame | Gameplay interval (ms) | Following host input/window interval (ms) |
| --- | --- | --- |
| 2813 | 34.9961 | 21.0467 |
| 5336 | 78.7585 | 61.6717 |
| 7488 | 44.2093 | 34.8402 |
| 7585 | 43.7006 | 27.0562 |

The input/window interval precedes the module update and includes event pumping,
controller polling and resize checks. Nearby frames report no GC collection or
pause. This localizes the delays but does not prove their root cause, exclude
audio-related system scheduling, or identify them as the older 100-270 ms
incident. The failed report remains retained.

The unchanged repeat (`gameplay-soak-20261106-150328-570-native`) also failed:
two intervals 34.7935/91.4480 ms at frames 14252/14291, corresponding following
input/window intervals 18.9802/75.3466 ms. No nearby GC pause was reported;
private growth was 47.93 MiB. Sampled maximum callback was 0.1735 ms with no
reported ring underruns. This is a reproduced timing pattern, not acceptance.

Device-lifetime correction now keeps a healthy mixer/output device across
same-root world rebuilds. Each reload still stops old voices and revalidates/
reloads cue/WAV contents on the worker; root change, audio-free levels and
application disposal release output. This removes needless Windows device
creation/teardown during every save/load. It is a testable hypothesis for the
input-stage delays, not an established root cause. The expanded 48-check suite
passes, and two post-correction long workloads pass, including an independent
repeat after lighting validation. No input/physics/rendering code or test limit
was changed to accommodate audio.

In the corrected long run, device callback counters continue across level
reloads (1,009 to 16,097 at settled samples), maximum callback is 0.1274 ms,
and cumulative PCM-ring underruns/deadline overruns stay zero. Peak requests
are seven; 1,944 excess one-shot requests were rejected as expected. Resident
sample/stream budgets remain bounded. The independent repeat records 1,156 to
18,229 cumulative callbacks, maximum 0.1119 ms, zero ring underruns/deadline
overruns, the same bounded sample/stream payload and zero model-payload growth.
Two clean runs do not establish the input-stage hitch cause; retain monitoring
and the failed evidence.

## Publish Check

After all performance runs finished:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release --no-restore
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll check-audio --device
.\.dotnet\dotnet.exe publish Game/Game.csproj -c Release --no-build --no-restore -o Tools/LevelAuthoring/bin/Release/net10.0/qa/audio-publish-20261106
```

From the published directory, the bundled dotnet runtime ran `Game.dll --level
Content/Levels/audioTest.json --load-report published-load.json --load-probe`.
It exited successfully at first playable presentation, using the private output
directory for saves/layout. The DLL hash matches the native build, both licences,
cue JSON and all six WAVs are included. This single fresh-process startup probe
has uncontrolled OS file-cache state; it is not a cold-load benchmark or a second
human listening test.

Native DLL SHA-256:
`60235145E4FBE27261954976362ABAC0D009AFF00C2AA86FA43A5F63DCED8A5E`.
Pinned source/build/licences: `Native/HS2AudioBridge/README.md`.

## Preserved Baseline And Next Gate

Gate L and Gate B were accepted before this milestone, B with explicit residual
historical 100-270 ms stall risk. Preserve that disposition; no causal stall fix
is claimed. Run unchanged BATs and long controls sequentially without concurrent
builds or desktop playback. Record any recurrence rather than raising limits.
The audio implementation and measured local regression pass are complete for
Priorities 1/2; physical unplug/default-device migration and formal acoustic QA
remain open. No audio-only rebake/migration is needed; source geometry/light edits still need
their normal derived-cache rebuild. Surface feedback is the next roadmap item,
not part of this implementation.

# Audio Foundation

Implemented 6 November 2026 following accepted Gates L/B. Priorities 1 and 2 of
ENGINE_SYSTEM_UPGRADE_PLAN.md are available for playtesting. All 48 focused
checks, the unchanged baseline BATs/long controls, 32 lighting commands and two
post-correction audio long runs pass. AUDIO_VALIDATION.md separates automated
results, human review, retained failures and outstanding hardware QA.

## Scope

- C# owns validated/versioned cues, listener/emitter state, generation-safe
  handles, bounded voice requests, buses, variation, pause and teardown.
- Selected pinned SoLoud mixing with its bundled miniaudio output behind a small
  C ABI. No managed callbacks or file decoding on the device callback.
- Short PCM effects are resident; long PCM ambience/music use bounded prefetch
  rings filled by the control worker. Other codecs require offline conversion.
- Existing entity data gains an optional emitter. Loop intent is saved, never
  native pointers or historical one-shots. Old scenes remain silent/compatible.
- Existing inspector and content browser gain cue assignment/edit/audition.
- Separate audio test scene, launcher, original Surge XT/jsfxr source renders.
  Native Audacity/editor inspection is recorded separately from automated checks.

## Playtest

Run the root `LaunchAudioTest.bat`. It opens the separate `audioTest.json`, not
the six-room lighting baseline. Two mono machine emitters are attached to a
console and movable crate; a stereo basement drone supplies non-positional
ambience. Carry/move/break the crate to exercise emitter movement and cleanup.

- F7: a varied relay click three metres in front of the player.
- F8: toggle the authored loops. These test shortcuts apply only to audioTest.
- F/F2 retain flashlight/editor behavior. World audio pauses in F2 and modal UI;
  UI/dialogue buses are exempt. Existing save/load stores loop enabled intent.
- Short effects are not replayed from historical saves. Loop playback restarts
  on restore, rather than preserving a sample-accurate musical timeline.

Old levels without emitters remain silent and do not start an audio worker.
Existing launchers, source lighting scenes, game saves and physics are unchanged.

## Authoring

HS2Editor's Content Browser has an Audio tab. In F2, expand an entity's Audio
Emitter/Cue Library section. Select a cue and Assign To Selected, or add an
emitter in the Inspector. Cue ID, Auto Play and emitter gain belong to the
level entity and use existing undo/redo, save/reopen and prefab serialization.

The library edits `Content/Audio/cues.json`: buses, spatial/loop/stream flags,
gain, pitch range, distances, priority, concurrency and weighted WAV variants.
Save Cues validates references/settings and writes atomically. Audition/Stop
use a separate service and expose voice/output status and audition mix sliders.
Save cue edits before auditioning; re-enter play/reload to apply library edits
to a running level. Cue-library edits do not yet participate in level undo/redo;
Reload Cues discards unsaved library edits. The per-bus audition sliders are
session-only and are not the game's options menu.

Use PCM16 WAV, mono for positional sounds; stereo is supported for non-positional
ambience/music. Long files must use Stream PCM. Invalid versions, IDs, weights,
ranges, paths, formats and references are rejected. No automatic transcoder or
compressed-audio codec is introduced here. The existing asset compression
pipeline is not changed.
Stop audition/game loops before overwriting an actively streamed WAV; the stream
owns a read handle until stopped. Cue IDs are stable references: renaming one
does not automatically rename emitter references.

Audio metadata is excluded only from lighting snapshots, preserving pre-audio
cache identities. No rebake/cache migration is required for audio-only edits.
Geometry/material/light edits retain their existing invalidation behavior.

## Ownership And Budgets

C# owns cues, generation-safe handles, listener/emitter intent, mix gains and
persistence. One control worker owns native handles and files. Native SoLoud
mixing with instance-owned miniaudio WASAPI output is a narrow C ABI, not a C++
engine migration. See Native/HS2AudioBridge/README.md for the pinned build/licences.

- 32 request/voice slots, four streams, 16 MiB resident PCM maximum.
- Each stream has a fixed 24,000-frame PCM ring plus bounded decode scratch.
- Per-cue concurrency and strict higher-priority stealing bound overload.
- No managed callback, file IO or decoding on the device callback.
- Nominal stereo 48 kHz output with a requested 512-frame period. Actual period
  is reported; this is not an end-to-end speaker latency measurement.
- Missing device/backend leaves diagnostics and silence, not gameplay changes.
  Output retries every two seconds; editor exposes explicit reconnect.
- Reset/unload/disposal stop voices, release streams/samples and invalidate
  old handles. Startup cancellation is checked between bounded sample loads.

World rebuilds within the same audio root revalidate/reload cues and samples
on the control worker while retaining the healthy native mixer/output device.
Changing audio root, entering an audio-free level, a fatal worker error or
application disposal releases that device. This separates level voice lifetime
from the Windows output-device lifetime; it does not cache stale WAV contents.
Removed entity IDs are pruned from saved loop intent during world rebuilds.

## Original Test Assets

Surge XT 1.3.4 through Python/Pedalboard generated `machine_hum.wav` and
`basement_drone.wav` using original held-note designs and factory presets.
jsfxr 1.4.0 generated three relay variants and `panel_confirm.wav`.
The scripts, MIDI, presets, parameters and hash/level manifests live in
Tools/AudioAuthoring. The other game's rendering helper was reused read-only;
none of its music or third-party samples were copied.

Installed Audacity 3.6.3 opened/played the drone and saved the actual native
project `Tools/AudioAuthoring/review/basement_drone.aup3`. Native HS2Editor
audition/Stop and the game's output were checked; the user confirmed hearing
the game sounds. Waveform remains the arrangement workstation, but no Waveform
project was created or verified in this pass. MIDI/presets are supplied for it.

## Validation Before Acceptance

Test schema/reference failures, stale handles, capacity, priority, variation,
pause, reload/destruction cleanup, missing device and recovery, PCM streaming,
save/load and prefab/undo persistence. Measure native callback time, deadline
overruns, stream underruns, voice/sample memory and command pressure. Device
disconnect tests must distinguish simulated output loss from physical unplug.

Run the existing gameplay/frame-pacing BATs sequentially with normal VSync and
unchanged acceptance limits; compare an audio-enabled long control. Preserve
lighting/physics and source reference scenes. Historical 100-270 ms stalls remain
unresolved and monitored, not fixed. Record failures instead of adjusting limits.

Surface feedback, interaction rules, acoustic propagation, navigation and AI
are later milestones, not part of this implementation.

## Remaining Limits

Physical unplug/default-device switching, speaker/headphone comparisons and
acoustic latency measurements remain separate hardware QA. Simulated output
suspension/recovery is not a substitute. This first pass has simple stereo
panning/distance attenuation, not HRTF, obstruction, room reverb or acoustic
propagation. SemanticNoise is a data contract only; hearing is not implemented.
No generic interaction/footstep/weapon event wiring is claimed yet.

# Audio Tools And Next Audio Milestone

Tool inventory updated 2026-11-02. These tools are the required starting point when audio work begins. Execution order clarified 2026-11-04: **finish and accept the current lighting implementation before any new audio system work or backend prototype.** Close the existing baseline frame-pacing/stability gate as well. The [prioritised systems upgrade plan](ENGINE_SYSTEM_UPGRADE_PLAN.md) defines the subsequent work; this planning update changes no music or audio files.

Research addition (2026-11-04): [Game Style Objective](GAME_STYLE_OBJECTIVE.md) records the updated sound references. [Engine systems research](ENGINE_SYSTEMS_RESEARCH.md) compares runtime audio designs and candidate libraries, with a proposed C# ownership model and acceptance sequence. This adds research only; the inventory date, asset provenance and implementation status below remain unchanged. The current [frame-pacing acceptance](FRAME_PACING_GUIDE.md) precedes runtime audio implementation.

## Installed Tools

Read-only Windows uninstall-registry inspection confirms:

| Tool | Installed version | Location | Intended role |
| --- | --- | --- | --- |
| Waveform Free 14 (user's chosen workstation; registry name Waveform 14) | 14.0.50 | `C:/Program Files/Tracktion/Waveform 14/` | Arrange tracks, edit MIDI and mix |
| Surge XT | 1.3.4 | `C:/Program Files/Common Files/VST3/Surge Synth Team/` | Instrument sound design/synthesis |
| Audacity | 3.6.3 | `C:/Program Files/Audacity/` | Waveform editing, cleanup and export checks |
| jsfxr | Not a registered desktop application | Generator scripts in `F:/DangryGames/Games/2DSpaceShooterRemix/ProjectDocs/AudioSources/Jsfxr/` | Short sound effects rather than music |

These entries verify installed registrations, not successful application launches, plugin loading or project compatibility. Verify those in the audio milestone and use the installed software instead of assuming a new online tool is required.

## Existing Music Provenance

The user clarified that these assets belong to **2DSpaceShooterRemix**, not this engine. Read-only inspection found that project at `F:/DangryGames/Games/2DSpaceShooterRemix`. No audio/MIDI assets were found under this engine's `Game/Content` in this pass. Do not imply that the space-shooter soundtrack is already integrated into the horror game.

The other project's notes and scripts confirm **Surge XT through Python/Pedalboard** rendered its music. MIDI and separate rendered audio tracks were provided for editing in Waveform. **No native Waveform project was created.** Do not claim Waveform rendered them. Audacity is installed, but its use on those tracks is unconfirmed; the documented October menu cleanup used Python/FFmpeg. jsfxr generated short effects, not music.

Read-only source inventory (paths below are relative to the other project):

- `ProjectDocs/approved-audio-integration.md`: approved tracks, replacements, integration and QA history. Newer entries supersede the original audition manifests.
- `ProjectDocs/AudioSources/Music/surge_render.py`: Pedalboard/VST3 rendering, factory-preset loading and validation. It uses `C:/Program Files/Common Files/VST3/Surge Synth Team/Surge XT.vst3/Contents/x86_64-win/Surge XT.vst3` and project-local `.codex-check/music-deps`; `requirements.txt` records dependency pins, not proof of the currently installed versions.
- `ProjectDocs/AudioSources/Music/2026-10-04-hybrid-v1/`: original Blue Orbit/Ion Run/Event Horizon masters, multi-track MIDI, aligned 24-bit stereo stems, presets and manifests. Its README explicitly records the absence of a native Waveform project.
- `ProjectDocs/AudioSources/Music/2026-10-04-action-v2/`, `2026-10-04-boss-v4-long/`, and `2026-10-04-menu-v1/`: later action, boss and menu sources. `clean_menu_20261013.py` documents the later menu derivative; preserve the original render.
- `ProjectDocs/AudioSources/Jsfxr/generate-starter-sounds.cjs` and `generate-ping-v2.cjs`: reproducible effects with outputs in adjacent versioned folders. The starter script defaults to the jsfxr source checkout at `F:/DangryGames/NewGames/FoodTruckSim/jsfxr`; pass an explicit source location when reusing the workflow. It validates mono 44.1 kHz/16-bit PCM output.

No files in the other project were edited, copied, rendered or executed during this investigation. Its menu manifest references FEEL kick/snare samples: check those licenses before any cross-project asset reuse. Reuse the authoring workflow for horror-specific material, not the space-shooter soundtrack automatically. Installed applications and plugin loading still need a hands-on check during the audio milestone.

## Audio Milestone

1. Use the inventoried workflow to author horror-specific test sounds; verify licenses, exports and the local plugin environment before reusing any source. Preserve the other project's files and keep this engine's generated exports separate.
2. Open the stems/MIDI in installed Waveform, verify Surge XT loads, and use Audacity for cleanup and loop/export inspection where needed. Save a real native Waveform project only when one is actually created; document its path and included/external dependencies.
3. Implement and test runtime positional emitters, attenuation and room/door occlusion, followed by room-dependent reverberation. Music is not automatically a positional mono sound effect.
4. Test in-game mixes with headphones/speakers, seamless loops, pauses, transitions, saves/loads, streaming lifetime and performance. Keep audio work off the main simulation/render critical path where supported by the existing engine.

The workstation/synth/editing tools author assets; the engine must provide real-time spatial playback. Their installation does not mean those runtime systems are already implemented. Keep C# as runtime and validate each step.

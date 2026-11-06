# Original HS2 Audio Sources

These are original test sounds, not copied game audio. Runtime exports live in
Game/Content/Audio and are PCM16 WAV. No external recorded sample is used.

- `render-effects.cjs`: jsfxr 1.4.0, deterministic relay variants and UI tone.
  `node Tools/AudioAuthoring/render-effects.cjs <jsfxr-checkout>`; the default is
  the user's installed FoodTruckSim jsfxr checkout, read-only.
- `render-surge.py`: installed Surge XT 1.3.4 via Python/Pedalboard, using the
  read-only 2DSpaceShooterRemix preset-loading helper and music-deps environment.
  Run with the local Python installation that hosts that workflow. Factory
  presets FM Bass 2 / Bell Pad, original notes, conservative peak levels and
  loop crossfades. Source MIDI and exported VST presets are included.
- `effects-manifest.json` / `surge-manifest.json`: exact render settings,
  durations, peaks/RMS and output SHA-256 identities.
- `PrepareAudioTest.ps1`: create-once test level from pbrMaterialTest; refuses
  to overwrite an existing audioTest so subsequent authored edits survive.
- `PrepareEditorReview.ps1`: isolated review build/content/layout, not a normal
  launcher. Original project files and saves are not edited by the review.
- `review/basement_drone.aup3`: actual Audacity project saved during playback
  inspection; screenshots document Audacity, editor audition and game output.

No native Waveform project was made. Import the MIDI and WAVs into Waveform for
arrangement; its playback/plugin compatibility has not been claimed as tested.
Rendering depends on this machine's documented installations, while playing
the committed WAVs requires only the engine's deployed audio bridge.

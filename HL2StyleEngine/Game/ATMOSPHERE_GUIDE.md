# Atmosphere And Post-Processing

Implemented first pass: 2026-10-26. C# remains the runtime; HLSL handles the pixel work. This is step 6 of the upgrade plan, after the accepted room visibility and render submission passes. The other approved research systems are staged in [the roadmap](ENGINE_TECHNOLOGY_ROADMAP.md#approved-research-backlog-2026-10-26), not implemented by this change.

## Playtest And Authoring

Run `LaunchAtmosphereTest.bat` from the repository root. It builds Release and opens `Game/Content/Levels/atmosphereTest.json`: an isolated copy of the twelve-room stress fixture using existing models, fixed lights, movable props, breakable crates and weapons. No new puzzles. Original levels, launchers and startup selection are unchanged. Geometry and light settings are identical to `renderStress12Room.json`; only optional post-processing is added. Six existing invariant HDR reflection caches are copied and fingerprint-validated, not recooked with fog.

Open the new level in HS2Editor, or press F2 in the game. In **Toolbar > Atmosphere**:

- **Bloom:** enable, intensity, HDR threshold, quarter/half-resolution buffers. Quarter is the tested default.
- **Colour grading:** enable, contrast, saturation and linear RGB colour filter.
- **Distance / height fog:** enable, linear fog colour, density, start distance, base height, height falloff and maximum opacity. Zero height falloff produces distance-only fog.
- **Reset atmosphere:** removes the level's optional settings. All changes support undo/redo and Save Level, including JSON/RE:Dox reload.

Existing **Lighting > Exposure** still controls exposure. Start with the fixture's subtle settings; do not compensate for incorrect material factors, light coverage, missing meshes or geometry gaps with fog. Compare each toggle separately, inspect close objects and distant doorways, move the flashlight, hold/break a crate, then open inventory. Long manual playtesting and native docking/control interaction acceptance remain open.

## Rendering Contract

World rendering stays RGBA16F / 4x MSAA. The new composite applies fog, optional thresholded HDR bloom, exposure/linear colour filtering, existing Reinhard tone mapping, saturation/contrast and sRGB output. Bloom uses two bounded RGBA16F targets and extract/horizontal/vertical passes; the final composite is a fourth pass. Fog or grading alone needs only the composite. Missing or disabled settings use the original `PresentPS.cso`, not an approximation of it. Neutral grading matches that reference within 1/255.

Fog reconstructs world position from the actual camera/scene viewport and integrates bounded exponential height density along the view ray. It evaluates colour/depth per MSAA sample before averaging, avoiding foreground/background depth mixing at edges. Camera-relative start distance protects close foreground geometry with the default settings. Docked editor rectangles are respected. Invalid camera matrices disable effects for that frame; settings are finite/clamped and reset between frames.

The world composite runs **before gameplay UI and ImGui**. HUD, inventory, menus and text do not pass through bloom, grading or fog. Their normal transparency can still reveal the changed scene underneath. Editor 3D grid/gizmos remain scene geometry and can receive the presentation effects; docked UI does not.

Post-processing is excluded from diffuse-bake and reflection-capture energy fingerprints. Editing atmosphere does not require recooking lighting. Static geometry, material or light changes still require the existing bake/capture workflow.

Bloom quality changes retire old targets behind frame fences. Disabled bloom releases its large targets; shared shader/pipeline resources, a 192-byte uniform buffer and an 8-byte black texture may remain until resize/disposal. Resize/disposal waits are explicit; no new routine-frame GPU idle wait is introduced. `PostProcessGpuBytes` reports active texture payload only, not driver allocation, uniform/pipeline metadata or transient retiring resources. At 1920x1009, quarter-resolution bloom uses 1,943,048 bytes (about 1.85 MiB); half uses 7,756,808 bytes (about 7.40 MiB). These are additional to existing HDR/MSAA, shadow and reflection resources.

This is **not** volumetric lighting, shadowed light shafts, local fog volumes, temporal fog, automatic exposure, a LUT grading system or transparent-particle depth handling. Fog colour is an authored atmospheric contribution, not simulated scattering from each lamp. It can reveal existing gaps or background openings; it does not fix geometry. No lighting/shadow shader, physics solver or native-runtime rewrite is included.

## Validation

- 24 data/editor checks: disabled defaults, malformed-value bounds, undo/redo/reset, JSON and RE:Dox reload, unchanged scene contents, diffuse inputs, all six reflection keys and copied caches.
- 55 targeted GPU checks: neutral/disabled comparison at <=1/255, HDR bloom halo, zero-strength effects, CPU-oracle fog colours across camera rotations/height falloffs, docked viewport bounds, singular camera fallback, state reset, odd-size resizing and half/quarter target retirement. No D3D11 warning/error messages in this test.
- All 15 historical Game/F2/HS2Editor images remain within their existing 1/255 tolerance; reference images were not replaced.
- Actual atmosphere Game/F2/HS2Editor scene captures inspected. F2's shared spawn camera includes the existing blue player debug body; the standalone editor shows its existing grid/light markers. Captures test rendering, not native docking interaction.
- Release solution and Debug/Release authoring builds are clean. Both app packages pass 50 shader/model/level/cache checks. The exact BAT builds, launches and exits through the 60-frame smoke harness (16.36/25.38ms mean/P95); this short stationary launch check is not a gameplay pacing-gate run.

### Performance

Normal desktop, Release, RTX 3060, D3D11 debug layer enabled, VSync. Full-host runs at 1920x1009 include scripted player movement, Update/physics, production UI, shadows and presentation; 120 warmup + 300 measured frames. Gates remain mean <=17ms, P95 <=21ms, at most one frame >33.3ms, none >100ms and no streaming waits.

| Full-host workload | Mean / P95 ms | Frames >33.3ms | Result |
| --- | --- | --- | --- |
| Effects off, torch on | 16.662 / 17.811 | 0 | Pass |
| All effects, torch on | 16.661 / 17.827 | 0 | Pass |
| All effects, room traversal | 16.665 / 17.794 | 0 | Pass |
| All effects, torch off | 16.608 / 17.806 | 0 | Pass |
| Six-crate/hold/inventory stress, first all-effects run | 16.772 / 17.826 | 2 | Failed hitch gate |
| Identical stress, effects off | 16.656 / 17.787 | 0 | Pass |
| Identical stress, all-effects diagnostic repeat | 16.660 / 17.791 | 0 | Pass |
| Identical stress, second all-effects diagnostic repeat | 16.662 / 17.903 | 0 | Pass |

Stress includes 120 peak fragment bodies and opening/closing the real inventory. The initial two long frames were not reproduced in either diagnostic repeat; their cause is not proven. No renderer change or relaxed gate was used to obtain the repeats. Preserve this as a longer-soak acceptance item, not a claim of locked-minimum 60 FPS. Ordinary movement remains approximately 60 FPS. All runs above had zero streaming waits and no >100ms frames. Model residency remains 30 models / 85.3 MiB, shadows 104 MiB, reflection residency three probes at spawn and six after traversal. No cold-load improvement is claimed.

Isolated GPU timestamps at 1920x1061 measure **resolve + postprocess only**, with constant HDR/depth, 30 warmup +120 samples per mode and test-only synchronous readback. These are not full-game GPU timings:

| Mode | Forward mean / P95 ms | Reverse-order mean / P95 ms |
| --- | --- | --- |
| Disabled reference | 0.091 / 0.091 | 0.090 / 0.091 |
| Grading only | 0.101 / 0.102 | 0.101 / 0.102 |
| Bloom only | 0.145 / 0.147 | 0.145 / 0.146 |
| Fog only | 0.227 / 0.228 | 0.228 / 0.229 |
| All effects | 0.445 / 0.434 | 0.509 / 0.897 |

Combined added GPU mean/P95: 0.354/0.343ms forward and 0.418/0.806ms reverse. Both pass the preselected <=1/1.5ms incremental gate. Active quarter-resolution bloom texture payload here is 2,042,888 bytes. Half-resolution quality is covered by resource/image checks, not these timing claims.

### Reproduce

Use `.dotnet\dotnet.exe` with `Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll` after building `Tools/LevelAuthoring/LevelAuthoring.csproj -c Release`. Commands:

```text
validate-atmosphere
check-atmosphere-pixels
validate-pbr-legacy
capture-atmosphere
benchmark-atmosphere-gpu
benchmark-atmosphere-gpu --reverse
benchmark-atmosphere --effects-off
benchmark-atmosphere
benchmark-atmosphere --torch-off
benchmark-atmosphere --route
benchmark-atmosphere --stress --effects-off
benchmark-atmosphere --stress
validate-atmosphere-package Game/bin/Release/net10.0
validate-atmosphere-package HS2Editor/bin/Release/net10.0
```

Run GPU tests sequentially on the normal desktop with no competing game/editor instance. `build-atmosphere` creates the fixture only when absent and refuses to overwrite edits. Generated PNGs go under the authoring tool's `qa` directory.

HLSL binaries are tracked/copied, not automatically compiled by the .NET build. Compile all four entry points when editing the source:

```powershell
foreach ($entry in 'ExtractPS','BlurHorizontalPS','BlurVerticalPS','CompositePS') {
    & 'C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/fxc.exe' /nologo /T ps_5_0 /E $entry /O3 /Fo "Engine.Render/Shaders/Atmosphere$entry.cso" Engine.Render/Shaders/Atmosphere.hlsl
    if ($LASTEXITCODE -ne 0) { throw 'Shader compilation failed' }
}
```

Primary API references: [Microsoft multisample texture loads](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sm5-object-texture2dms-load) and [Lumix postprocess ordering](https://nem0.github.io/LumixEngine/renderer/postprocess.html). This implementation uses the project's own renderer; no external engine code was imported.

## Next

Manual atmosphere/long stress acceptance first. Next recommended engineering milestone: instrument true first-playable loading stages, then packaged dependency metadata/source-read avoidance with stale-cache fallback. Clustered lighting, deeper occlusion, persistent render jobs, physics work, diagnostics and separate horror-game audio/AI/effects are approved roadmap items, not silently included in this pass.

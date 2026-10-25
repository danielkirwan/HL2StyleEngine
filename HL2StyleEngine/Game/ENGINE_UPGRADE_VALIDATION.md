# Compression, Lighting And Physics Validation

## Lighting Stability And Switched States (2026-10-25)

Separate `LaunchSixRoomLightingStabilityTest.bat`: fourteen fixtures, zero ambient/directional, 532 baked architecture models, 766 valid probes, six resident captures, explicit on/off states and 2m point-shadow hysteresis. Original level/shader/reference files are preserved. 797 new checks plus actual GPU on/off/idle/authoring-undo tests pass; all 15 Legacy images, 282 diffuse/probe, 50 room capture, 106 transition and 747 original fixture checks pass. Game/F2/HS2Editor captures remain textured.

GPU A/B found no asset-streaming pixel changes over 1/255 at three fixed positions; old reflection residency changed 154,839 / 5,160 / 0 pixels. New all-resident fixture removes those differences. This is a measured small-level remedy, not general visibility-aware streaming.

Final verification: Release solution and Debug/Release authoring builds are clean; 797 checks also pass in Debug. Both app output folders pass 60 package hash/cache checks; 937 older streaming checks and GPU PBR shadow/moving-door regression pass. Exact BAT smoke build/run/automatic exit succeeds; its separate 60-frame stationary mean/P95 16.47/22.95ms is not counted as a pacing-gate pass.

Six full-host 1920x1009 Release runs pass unchanged gates. Gameplay/room traversal mean 16.666-16.667ms, P95 17.229-17.292ms, zero >33.3ms frames; switching mean 16.721-16.779ms, P95 17.267-17.279ms, one >33.3ms frame per run. No streaming waits or runtime captures; no locked-60 claim. Matching inactive states stay warm: approximately 12.76 MiB reflections after both load, at most 37.15 MiB bake resources in the all-visible test. First off-state preparation remains asynchronous (~615ms); hot switches take ~15-16ms in the render harness. Full commands, state-authoring rules and remaining limitations: [LIGHTING_STABILITY_GUIDE.md](LIGHTING_STABILITY_GUIDE.md).

## Fixture-Only Point Lighting (2026-10-25)

Existing 15 rendering-reference images remain within 1/255. Exact batch-launch smoke passes build/run; its separate 60-frame stationary sample records 16.65ms mean / 23.75ms P95, so it is not counted as another pacing-gate pass or proof of locked 60 FPS.

`LaunchSixRoomPointLightTest.bat` opens the separate zero-ambient/directional eight-fixture PBR level. 747 level/prefab/capture checks pass; actual game on/off/on GPU images verify illumination, dark fallback and restoration within 1/255. Game/F2/HS2Editor scene captures are textured. Release build has zero warnings/errors; each app's copied assets/caches pass 46 checks. Four 1920x1009 full-host runs average about 60 FPS, P95 18.052-18.108ms, zero >33.3ms and zero streaming waits. No shader or renderer changes. [Exact results, reproduction and limits](POINT_LIGHT_TEST_GUIDE.md).

## Reflection Residency And Six-Room Rollout (2026-10-25)

Unit F first pass: `LaunchSixRoomPbrTest.bat` opens an isolated copy of the six-room flashlight level. Six saved 128-square probes share three resident slots with active/doorway pinning, connected-neighbour prefetch, retention, worker validation and fenced eviction/reload. Existing source levels/launch defaults/shaders are unchanged. Shared F2/HS2Editor controls expose policy and status. Corrected wood crates are now recognized by the existing fracture path. [Full workflow, tests, metrics and limits](REFLECTION_STREAMING_GUIDE.md).

Release full-host stress (torch on/off) mean/P95: **16.716/17.090 ms**, **16.661/16.939 ms**; loading/eviction route: **16.666/17.638 ms**, **16.661/17.027 ms**. All four runs have zero >33.3 ms frames, zero streaming waits and one main object upload/frame. Stress includes 120 peak fracture pieces, shutters, gravity holding and inventory. Loading route uses test-only teleports during real Update/physics/presentation. At most three captures plus eleven binding sets/fallback use **3,345,700 bytes**; observed retirement-inclusive peak about **3.193 MiB**. Runtime capture passes remain zero. The initial synchronous-validation route failed; worker snapshot preparation fixed the hitches without widening limits.

The new GPU suite validates copied gameplay data, policy/undo/serialization, prefetch/retention/pinning, resource limits including fenced slots, zero upload budget, revisits, forced reload, cancellation and missing-cache recovery. Existing stage regressions and publish checks are recorded in the residency guide. New scene images were captured separately, with textured game/standalone views inspected. These are short local tests, not a locked-60 minimum, cold-start measurement or manual editor/controller acceptance. Historical results below describe their original stages.

Final verification: Release/Debug builds clean; 945/957 new assertions pass (scheduling-dependent poll count), along with 106 transition, 50 capture, 282 diffuse/probe, corrected-crate flashlight checks and all 15 Legacy reference images. Game/HS2Editor Release and fresh Game publish each pass 45 packaged hash/capture-validity checks; the published game smoke run exits normally. Final torch-on loading route: **16.661/17.059 ms mean/P95**, zero >33.3 ms frames. No source level, shader or reference image was replaced to make these checks pass.

## Room Transitions (2026-10-25)

Unit E's invariant-light first pass is implemented in separate `roomTransitionTest.json`, launched by `LaunchRoomTransitionTest.bat`. Automatic mode selects one room or an explicit pair per model part, with box-projected directions, strict pixel-space membership and linear-HDR doorway blending. Shared F2/HS2Editor controls author automatic mode, bounds and neighbor/offset/size/axis; linked prefab GUIDs remap. Missing/stale/incompatible captures use black without hiding scene geometry. No live captures, per-draw uniform uploads or routine WaitForIdle were added. All loaded probes remain in a bounded set of up to eight; residency/prefetch and explicit switched presets are still pending. [Full guide and commands](ROOM_TRANSITION_GUIDE.md).

106 transition assertions pass in Release and Debug: authoring/undo/prefabs, missing neighbors, cache-key independence, HDR GPU/CPU projection, parallel rays, room/doorway classification, spanning parts, solid-wall isolation, missing-neighbor weighting, upload readiness, fixed/realtime light overrides, reload, capture cancellation, source invalidation, full-range integer priorities and stable-ID ties. The existing 97 synthetic, 50 room capture and 282 diffuse/probe checks pass, as do GPU shadow/moving-door/batching and corrected-crate flashlight checks. All 15 Legacy, three direct-PBR and three synthetic-reflection images reproduce within 1/255 without replacing references. Debug/Release builds are clean. New scene captures use separate QA output; game/standalone images were inspected. Source and Game/HS2Editor Release level/capture copies have matching SHA-256 hashes. Native mouse/controller acceptance is not claimed.

RTX 3060 Release, D3D11 debug layer, RGBA16F/4xMSAA, 1920x1009, VSync, 120 warmup/300 host frames: torch **on wall mean/P95 16.663/17.042 ms**, **off 16.665/17.030 ms**. Both average about 60 FPS, with no >33.3 ms frames or streaming waits, one main upload/frame, 49.4 MiB models and no runtime capture passes. Ready reflection payload including both captures, bindings and fallback is **2,229,588 bytes (2.126 MiB)**. Old/new retirement overlap, capture scratch and driver overhead are additional.

Repeated render-only traversal at 1920x1061: GPU mean/P95 increment +.116/+.296 ms off, -.155/-.305 ms on; reverse-order after spanning-part adjustment +.191/+.303 ms off, -.020/-.255 ms on. CPU increments remain below +.04 ms. Both orders pass the unchanged <=1 ms GPU gate; negative deltas are clock/load noise. Short local runs do not guarantee a locked-60 minimum, cold first-playable time or other hardware. Full publish, long soak, manual editor/input and real-level migration remain open. HLSL and both matching CSOs were rebuilt; old room caches were recooked for the changed shader fingerprint.

## Authored Room Captures (2026-10-23)

Unit D's first pass is implemented in a new `roomReflectionTest.json`; launch with `LaunchRoomReflectionTest.bat`. Two authored 128-square HDR captures persist beside the level, with shared F2/HS2Editor authoring, source-key validation, cancellation and frame-safe replacement. Only one explicitly selected whole-scene preview is active; box projection, automatic transitions and on/off preset variants remain unit E. Original levels and accepted shaders are unchanged. Full workflow, exclusions, budgets and reproduction: [ROOM_REFLECTION_GUIDE.md](ROOM_REFLECTION_GUIDE.md).

50 room assertions pass in Release/Debug (polling may vary the count), including six-face asymmetric orientation, baked diffuse integration, source/transform/light/resolution staleness, prefab IDs/undo/JSON, corrupt/truncated/versioned caches, cancelled atomic writes, restart, zero upload budget, fixed-light override fallback and cancelled submitted GPU work. Main-view camera/shadow output remains bit-identical after capture/cancellation. Existing 97 synthetic reflection, 282 diffuse/probe and GPU shadow/moving-door checks pass; all 15 Legacy reference images stay within 1/255. Game/F2/standalone scene captures were inspected, not manually mouse/controller playtested.

Full-host Release on RTX 3060, 1920x1009, RGBA16F/4xMSAA, D3D11 debug layer on, 120 warmup/300 measured: torch on mean/P95 **16.661/17.844 ms**, off **16.661/17.878 ms**; about 60 FPS, zero >33.3 ms frames or streaming waits, one main upload/frame and no runtime capture passes. Ready environment + fallback is **1,114,180 bytes**; capture-only tracked GPU scratch/geometry peaks about **43-44 MiB**. Repeated 1920x1061 GPU preview increment mean/P95: -.183/-.189 ms off, +.226/+.057 ms on. Negative deltas are noise, not speedup. Budgets are unchanged; long soak, cold first-playable and native UI acceptance remain open.

Final room recheck: full-host on/off wall mean/P95 **16.667/17.887** and **16.663/17.963 ms**, still zero >33.3 ms frames or streaming waits. Reverse-order steady GPU increments **-.131/-.707 ms off**, **-.243/-.237 ms on** pass, with CPU increments below +.02 ms. All three direct-PBR and three synthetic-reflection reference images pass within 1/255; the corrected-crate flashlight suite also passes. Both build configurations are clean. Source and Game/HS2Editor Release level/capture copies have matching SHA-256 hashes. Full publish and native interaction acceptance remain untested. Probe helpers no longer cast shadows, and the refreshed standalone scene image was inspected. Final room regression: 50 checks passed.

## PBR Crate Flashlight Fix (2026-10-23, Historical)

The original crate GLB's roughness 0 / default metallic 1 caused a tiny mirror-like highlight instead of broad wood illumination. The PBR fixture alone now uses a derived metallic 0 / roughness 0.85 asset, retaining source geometry/textures and all 22 fracture part keys. Original asset and existing levels are unchanged. Reproduction and historical-reference handling: [PBR material guide](PBR_MATERIAL_GUIDE.md#crate-material-correction-2026-10-23).

`validate-pbr-crate` passes deterministic regeneration, unchanged binary chunks, source/cooked geometry/normal/UV/key equality, retained wood texture, corrected cooked scalars, six-face flashlight tests, shadows on/off, rotated crate and light off restoration. GPU images show broad illumination rather than the original tiny highlight. In the actual game renderer with reflections/shadows enabled, 3,600/3,600 central face pixels brighten with the real flashlight. This is scripted render validation, not a manual input playtest.

Final crate checks pass in both Debug and Release; switch-off's central face matches exactly (0/255 difference). Release solution and Debug/Release authoring-tool builds pass with zero warnings/errors. Source, Game Release and HS2Editor Release copies of the corrected GLB have matching SHA-256 hashes. The original source GLB is not modified.

All 15 Legacy references plus three direct-PBR and three reflection references pass within 1/255; historical reference fixtures explicitly retain the old crate in memory, while the dedicated new tests exercise the corrected asset. References are not regenerated. No shaders or runtime rendering algorithms changed.

Corrected-fixture Release full-host route on RTX 3060, 1920x1009, RGBA16F/4xMSAA, D3D11 debug layer on, VSync, 120 warmup + 300 measured frames: torch **on mean/P95 16.667/18.010 ms**, **off 16.666/18.008 ms**. Both average about 60 FPS; zero frames >33.3 ms or >100 ms, zero streaming waits, one main upload/frame. Model payload stays about 49.4 MiB, reflection payload 1,114,180 bytes, no pending reflection CPU payload. These meet the existing local regression gate, not a locked-60 minimum guarantee. Unit D (authored room captures/cache) remains next, not delivered by this fix.

## Controlled Reflections (2026-10-23)

Unit C passes its local automated gates. [REFLECTION_LIGHTING_GUIDE.md](REFLECTION_LIGHTING_GUIDE.md) records the implementation, budgets and exact reproduction route. Added one 128-square GGX-filtered synthetic HDR environment, split-sum LUT, separate bindings and worker/shared-budget uploads. Only `pbrMaterialTest.json` opts in. Existing direct/diffuse/emission and authored levels remain compatible; no room capture, native rewrite or bloom is included.

97 Release / 98 Debug reflection assertions pass (readiness polling can add a checked frame), including independent numerical integration, white-furnace energy, six-face GPU rendering/sampling, asymmetric directions, roughness, back-face guards, fallback/readiness, JSON/RE:Dox/undo and diffuse-cache independence. Existing 126 HDR, 282 bake/probe and GPU shadow/moving-door/batching checks pass. All 15 old images and three reflection-off PBR images match within 1/255. Three new reflection game/F2/standalone scene captures were visually inspected and stored separately in `References/PbrReflections`; these are not a manual editor/input test. Full solution/tool Debug and Release builds pass without warnings/errors.

Full-host 1920x1009, RTX 3060, Release, RGBA16F/4xMSAA, D3D11 debug layer on, VSync: torch on wall mean/P95 **16.661/17.892 ms**, off **16.661/17.844 ms**, about 60 FPS, zero >33.3 ms or >100 ms frames and zero streaming waits in both 300-frame routes. One main upload/frame, 6.476 m/s scripted movement. Model payload stays 49.38 MiB; reflections add **1,114,180 bytes** including fallback/metadata, with no pending CPU payload after upload.

Repeated 1920x1061 render-only on/off GPU increment: torch off +.278/+.290 ms, on +.482/+.213 ms (mean/P95); CPU -0.008/-0.015 and -0.004/-0.007 ms. Counterbalanced order also passes: GPU +.050/-.259 off, -.217/-.595 on. Negative differences are clock/system-load noise, not a speedup claim. Both orders meet the unchanged <=1 ms increment budget; measured host frame limits also pass. Cold first-playable timing, long soak and human editor/input acceptance remain open. Next: unit D's authored captures/cache; steps 3-9 remain pending.

Final back-face correction recheck: geometric-hemisphere guard avoids extra light on rear-facing surfaces without a perturbed-normal grazing cutoff. New unit C references were re-reviewed for that intentional change and now reproduce; Legacy/direct-only references remain untouched and pass. Final host torch on/off mean/P95 **16.667/18.079** and **16.667/17.932 ms**; no >33.3 ms frames or streaming waits. Counterbalanced GPU deltas +.013/-.012 ms off and -.162/+.097 ms on pass. Earlier measurements above are retained, not silently replaced.

## Opt-In Direct PBR (2026-10-22, Historical A/B Results)

Units A/B of `ENGINE_UPGRADE_PLAN.md` are implemented and validated sequentially. See [PBR_MATERIAL_GUIDE.md](PBR_MATERIAL_GUIDE.md) for the full material contract, baseline-before-change methodology, exact measurements, commands and limitations. New launcher: `LaunchPbrMaterialTest.bat`; all original level/launch defaults remain unchanged. Runtime stays C#.

Latest results: 15 material and 126 numerical HDR GPU assertions; all 15 pre-change game/F2/standalone scene captures within 1/255; 387 GLBs/639 parts bit-exact source/cache validation; Legacy and PBR shadow/moving-door/batching checks; existing 282 bake/probe, 298 lighting, 41 emission, level-rendering/F2 and visibility/eviction suites pass. Debug/Release full solution/tool builds pass without warnings/errors; the new material/HDR and existing bake/probe assertions also pass in Debug. Model caches are now v3 for optional corrected normals; texture formats and authored collision geometry are unchanged.

1920x1009 full-host PBR torch on/off averages 60.0 FPS, wall mean/P95 16.668/16.900 and 16.666/16.883 ms, no >33.3 ms frames or streaming waits in either 300-frame run. Legacy six-room torch-on stress: 16.667/17.959 ms with 120 fragments, doors, gravity hold and inventory. 1920x1061 steady paired GPU PBR cost is +0.119/+0.071 ms off and +0.019/-0.015 ms on (mean/P95). The profile explicitly reports startup/order effects and tests repeated/reversed pairs rather than hiding them. Model residency +2.47%, still one main upload/frame. Local gates pass; this does not certify an absolute 60 FPS minimum, long-duration stability or other hardware. Manual editor/input and cold first-playable acceptance remain open. Reflection environments/probes and bloom are not included.

The PBR final HDR-guard recheck also passes: full-host on/off mean/P95 16.666/17.899 and 16.667/17.983 ms; zero >33.3 ms frames or streaming waits. Reversed steady GPU increments +0.188/+0.241 ms off and -0.049/-0.056 ms on stay within budget. Collision-cache round-trip, rays/contacts and all 387 model comparisons pass after the final build. Earlier same-day measurements above/below are retained rather than replaced.

## Light Probes And Lighting Views (2026-10-22)

Delivered after the static-indirect milestone below: optional regular-grid directional probes, visibility-aware interpolation for moving textured models/debris/weapons, cached stationary queries, and shared editor Full/Direct Only/Indirect Only, probe preview and marker controls. `LaunchLightProbeTest.bat` opens a separate already-baked fixture. Original levels/launch selections are unchanged. See [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md) for setup, bounds and limitations.

### Correctness And Compatibility

- Full Debug/Release solution and authoring-tool builds pass with zero warnings/errors. **282 bake/probe assertions pass in both configurations**, including numerical six-axis packing, GPU sampling, no contribution leaking into subsequent unbaked draws, and Indirect Only suppressing direct/ambient/emission. The object buffer remains exactly **256 bytes**.
- A deterministic sealed-compartment bake produces 50 usable fixture probes, with indirect in the lit compartment and none in the sealed dark one. Analytic trilinear movement, both sides of a thin wall, invalid-probe rejection, out-of-volume sampling, non-finite/negative data, persisted settings and file round trips pass. File load constructs the visibility BVH on a worker.
- Actual `lightProbeTest.json`: 196/196 usable probes; all 17 static model bindings ready. Moving the actual crate between the warm and partitioned sides changes upward sample magnitude from **0.1265 to 0.0144** (test irradiance magnitude, not lux). Central crate pixels brighten with probes enabled while the static lightmap stays unchanged. Full/Indirect Only restores correctly; fixed-light overrides, Direct Only and stale edits suppress probes. Stationary queries reuse their cached result. F2 markers appear and disappear without contaminating normal rendering.
- The original version-1 `indirectLightingRoom.hs2lightmap` still loads without modification/rebaking. All 17 bindings, runtime switch, fixed-light override, F2, stale-edit and reference captures pass. It now reports 44,792 brighter spawn pixels; earlier counts below are historical shader-image measurements.
- Release regressions pass: `validate-rendering`, `validate-level-rendering`, `validate-emission` (41), `validate-lighting-state` (298), `validate-visibility-streaming`, `validate-collision-cache`, and `validate-six-room-flashlight-test`. This covers textured game/F2 images, moving doors, batched/reference images, failed/CPU-only replacements, all five puzzles, settling, eviction and persistent state.
- The separate room's source, Game Release and HS2Editor Release bake copies have identical SHA-256 hashes. Source GLBs, existing level layouts, docking settings, movement/physics tuning and weapon HUD are unchanged by this milestone.

### Measured Performance

Sequential fresh-process Release runs, Windows/D3D11, **1920 x 1009 actual client area**, desktop VSync/presentation included, 120 warmup plus 300 measured frames each. The requested window was 1920 x 1080 but the desktop constrained its client size. Production HUD/input/physics run; scripted W/S and mouse movement exercise gameplay. Stress toggles a light (or six-room shutters), breaks six crates (120 peak pieces), gravity-holds/releases a prop and opens/closes inventory. Probes and the atlas were verified resident. No concurrent bake/build ran during measurement.

| Route | Torch | Probes | Wall mean / P95 | Frames >33.3 ms | Probe CPU mean / max |
| --- | --- | --- | --- | --- | --- |
| Probe room walk | Off | On | 16.667 / 18.201 ms | 0 | 0.0136 / 0.0805 ms |
| Probe room walk | On | On | 16.668 / 18.513 ms | 0 | 0.0173 / 0.0446 ms |
| Probe room stress | Off | On | 16.668 / 18.298 ms | 0 | 0.4926 / 6.5145 ms |
| Probe room stress reference | On | Off; atlas on | 16.665 / 18.632 ms | 0 | 0 / 0 ms |
| Probe room stress | On | On | 16.667 / 18.514 ms | 0 | 0.3712 / 6.6048 ms |
| Existing six-room stress | On | No probe volume | 16.666 / 18.129 ms | 0 | No sampling |
| Existing six-room stress | Off | No probe volume | 16.660 / 18.313 ms | 0 | No sampling |

The engine's profile reports **60.1 FPS** for these runs; wall intervals correspond to approximately **60.0 FPS**. This meets the local average target, **not a promise that every frame stays below 16.67 ms or that every machine/level is locked at 60**. Approximately half the measured intervals exceeded 16.7 ms in these presentation-paced runs; P95 is reported above rather than hidden. No measured frame crossed the 100 ms simulation clamp. There was still one main-scene upload per frame.

All table runs had zero streaming-wait frames. An earlier first probe stress run had five streaming-wait frames, 16.668 ms wall mean, 17.329 ms P95 and no >33.3 ms frames; sampling averaged 0.4971 ms with a 6.7074 ms peak. That first run loaded an additional drop dependency (11 resident models/53.3 MiB versus 10/47.8 MiB in later runs). This variability is retained; no universal no-stall claim is made. The final probe-on/off stress comparison recorded 1.12/0.73 ms World CPU, but routes include collisions/random drops and are not deterministic isolated GPU timings. Original six-room stress travelled 5.024 m/s with both torch settings. Player speed/timestep settings were not changed.

### Bake, Memory And Reproduction

The new room uses the same 17 fixed instances/1,308 triangles, a 1024-square atlas (8 MiB), 0.19 MiB full instance buffers and a 1.25 m probe grid. Its offline bake took **24.51 seconds** and the complete file is **1,543,502 bytes**. Probe coefficients use 14,112 bytes and occluder positions 47,088 bytes, plus validity/grid/BVH/cache overhead. This is additional CPU memory, not an extra GPU texture or enlarged upload stride. Data stays resident for the level outside the model streaming budget. Invalid/stale files fall back to existing realtime lighting.

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-indirect-bake
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-probe-room
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-indirect-room
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes off
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes off --stress
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes on --stress --no-probes
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes on --stress
```

`on/off` means flashlight state; `--no-probes` disables only probe contribution for comparison. `build-probe-room` refuses to overwrite an existing level. `bake-level Game/Content/Levels/lightProbeTest.json` regenerates only its derived bake. Captures in the tool output `qa/` include `probe-crate-warm-on/off/indirect.png`, shaded equivalents and `probe-room-markers.png`. Warm on/off and marker images were visually inspected.

Remaining acceptance: subjective gravity-carry lighting transitions, full standalone-editor mouse/docking workflow, longer multi-room and memory-soak runs, and total GPU timestamp profiling. Probes currently sample once per object and account for fixed baked occluders only; moving doors do not dynamically block indirect light. Untextured/blockout draws remain unlit. No general dynamic GI, switchable bake scenarios or guaranteed locked-60 claim is made. Preserve the accepted batching, upload budget, texture appearance and torch settings in later work.

## Static Indirect Milestone (2026-10-22)

Delivered: opt-in static geometry/fixed lights, shared editor bake/settings/progress/cancel/preview/stale UI, one diffuse bounce through the existing triangle BVH, padded per-triangle UVs, linear RGBA16F atlas sampling, validated lossless LZ4 bake files, render-thread uploads and fence-safe retirement. `LaunchIndirectLightingTest.bat` launches a separate room using existing assets. Original levels, source GLBs, project launch profiles, weapon HUD and saves are unchanged. Probes, switchable bake scenarios and bloom are not included. Workflow and limits: [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md).

### Correctness

- Full Debug and Release solution builds pass with zero warnings/errors. Authoring tools build in both configurations; all **234 indirect-bake assertions pass in both**.
- A sealed two-compartment fixture has lit-floor indirect energy 6.98987 versus 0.00000 on the dark floor. This is summed test irradiance, not a physical lux measurement. Repeated bakes match exactly; original positions/material UVs remain unchanged; padded charts do not overlap.
- HDR/LZ4 round-trip, dependency invalidation, malformed/truncated/checksum failure, pre-write and active cancellation, saved-file preservation, metadata and undo/redo pass. Actual headless ImGui mouse clicks test Bake, Cancel, busy/disabled protection and save failure. This is not a manual docking/standalone-editor playtest.
- Numeric real-GPU checks verify linear lightmap sampling before albedo/tone mapping, no lightmap leakage into unbaked draws and independent instances sharing a source model.
- The actual test room loads all 17 bake bindings. Preview comparison yields 45,694 brighter spawn pixels; the partition's shaded side also has a measurable indirect contribution. Game and F2 captures preserve textured geometry. The reachable E-operated switch controls only its realtime screen/light. A forced runtime fixed-light-off override suppresses the whole bake, avoiding ghost indirect illumination. Editing architecture invalidates the active bake immediately.
- Four rotated walls exposed an existing quaternion/Euler round-trip sign inversion. Eligible **Static Lighting** models now use their exact authored quaternion for rendering and collision, making all 17 fitted transforms match the bake. Unmarked legacy objects retain the existing path; this is not a general transform rewrite.
- Existing Release suites pass again: `validate-emission` (41), `validate-lighting-state` (298), `validate-rendering`, `validate-level-rendering`, `validate-visibility-streaming`, `validate-collision-cache`, and `validate-six-room-flashlight-test`. Coverage includes shadow reference images, moving doors, all five puzzle routes, pickup/crate settling, shared-resource eviction/reload, and ready-before-replacement with failed/CPU-only assets.

### Bake And Memory

The new 8 x 8 m room has 17 fixed architecture instances and 1,308 triangles. Texels Per Triangle 16, Samples 64, Ray Distance 25 m produce one 1024-square atlas. The final warm-source Release bake took **20.08 seconds**, using two CPU workers; an earlier run took 23.17 seconds. This is offline baking time, not startup time.

The file is **1,488,314 bytes**, including compressed metadata/pixels. The atlas is **8.00 MiB** uncompressed GPU memory, with **8.19 MiB** total atlas plus all instance buffers. CPU pixels remain resident too. The moving test kept seven visible instance bindings for **8.09 MiB** additional GPU memory; model geometry/shared textures separately used **47.8 MiB**. A single atlas stays resident per level; unused instance buffers retire after 120 frames. These resources are additional to the existing model streaming budget, not included in its counter. Model/lightmap uploads share a soft 2 ms budget, but an individual atlas/part upload is not preemptible.

GPU vertex stride increased from 40 to 48 bytes for the extra lightmap coordinates, including unbaked models. The original six-room route now reports **107.9 MiB** of model geometry/shared texture payload; its 32 shared models remain resident. Updated forced-eviction checks measured **88.0 -> 32.4 MiB** with 36 evictions, preserving gameplay state. Historical numbers later in this report describe earlier layouts/materials; do not present them as current totals or the new bake's memory usage.

### Moving Frame Measurements

Release, normal Windows/D3D11 desktop, 1920 x 1009 actual client area, VSync/presentation included. Each row is one sequential fresh-process run, 120 warmup plus 300 measured frames, scripted W/S/mouse movement, production HUD/physics. The room's valid bake was explicitly verified resident. These are frame intervals, not isolated GPU timings, cold-load measurements or a hardware-independent guarantee.

| Route | Torch | Mean frame | P95 | Frames >33.3 ms | Movement m/s |
| --- | --- | --- | --- | --- | --- |
| New baked room | Off | 16.665 ms | 17.316 ms | 0 | 4.993 |
| New baked room | On | 16.668 ms | 17.037 ms | 0 | 4.907 |
| Existing six-room walk | Off | 16.665 ms | 17.149 ms | 0 | 6.476 |
| Existing six-room walk | On | 16.668 ms | 17.088 ms | 0 | 6.469 |
| Existing six-room stress | Off | 16.720 ms | 17.222 ms | 1 | 5.047 |
| Existing six-room stress | On | 16.667 ms | 17.011 ms | 0 | 5.040 |

All six runs had zero streaming-wait frames and zero frames exceeding the 100 ms simulation clamp. Stress opened shutters, broke six crates (120 peak fragments), held/released a prop and opened/closed inventory. Small movement differences include acceleration/reversals, collision and input timing; no movement settings changed. The small room and six-room route are different layouts, so their travelled speeds are not a before/after comparison. The one off-stress spike is retained here; this is near-60 behaviour, not a locked-60 claim.

### Reproduce And Remaining Checks

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release, then use its output DLL:

```powershell
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-indirect-bake
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-indirect-room
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll bake-level Game/Content/Levels/indirectLightingRoom.json
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-indirect off
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-indirect on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight off --stress
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on --stress
```

`build-indirect-room` refuses to overwrite an existing level; `bake-level` only regenerates its derived bake. Source and Release-output bake copies have matching SHA-256 hashes. GPU captures under the tool output `qa/` include `indirect-linear-gpu.png`, `indirect-room-on/off.png`, `indirect-room-shaded-on/off.png`, and `indirect-room-editor.png`. Game and shaded-side comparisons were visually inspected. F2 still contains existing helper boxes, not missing materials. Standalone docking/mouse workflows, subjective torch feel, a longer playthrough, multiple-room baking and memory soak remain manual/future checks. No production-ready general GI claim is made.

## Earlier Milestones

Date: 2026-10-06. Current Windows/D3D11 machine and project-local .NET 10. Source GLBs, authored level JSON and player saves were not rewritten by this pass. Earlier inventory visibility changes remain intact.

## Reproduce

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` with the project-local SDK, then run its output DLL with one command below. Use Release for performance comparisons. The tool is separate from the solution's normal project list.

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-engine-upgrades
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-lighting-state
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-rendering
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-level-rendering
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-model-cache
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-models glb
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-models cooked
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-level-ready glb
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-level-ready cooked
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-textures cooked
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-textures rgba
```

Other regression commands: `validate-collider-bounds`, `validate-six-room-test`, `validate-redox-inventory`, `validate-inventory-input`, `validate-inventory-visuals`.

Final verification: full Debug and Release solution builds passed with zero warnings/errors. All commands above passed in Release, including 321 persistence/inventory checks, 11 input scenarios, inventory geometry at three resolutions, 1,000 randomized collider bounds, five puzzle routes, shutter collision and pickup/crate settling.

## Cook And Asset Measurements

Full initial cook: 376 GLBs, 168 newly encoded textures, 1,597 existing/shared cache hits, zero failures, 794.1 seconds. Encoding is offline; valid caches are reused. All source files remain authoritative. Cook again after importing new/changed assets, using the importer's Compression tab or `CookAssets.bat`, then rebuild/relaunch to copy caches and replace resident textures.

Three alternating fresh-process Release runs loaded the six-room level's 28 distinct directly referenced GLBs and prepared their 56 unique textures synchronously on one worker. The filesystem was already warm. Each run retained models to preserve shared texture ownership. GPU upload, physics construction, UI, process startup and first-playable-frame timing are excluded.

| Path | Run 1 | Run 2 | Run 3 | Median | Unique mip payload |
| --- | --- | --- | --- | --- | --- |
| RGBA image decode + full mip generation | 7,601.19 ms | 7,081.95 ms | 7,129.09 ms | 7,129.09 ms | 293.85 MiB |
| Valid BC7/BC5 + LZ4 cooked cache | 511.11 ms | 271.46 ms | 270.00 ms | 271.46 ms | 73.47 MiB |

In this initial texture-only measurement, both paths parsed source GLBs and used equivalent full mip chains. The `benchmark-textures` command still calls the source loader directly, isolating texture preparation from the model-cache follow-up below. This compares the new cooked-texture path with its new uncooked fallback, not the old single-mip engine. The roughly 75% mip-payload reduction is not a total RAM/VRAM reduction: geometry, HDR/shadow buffers, CPU source data and other resources also consume memory. LZ4 is lossless; BC texture compression is lossy. Source GLBs plus extra caches may occupy more total disk space than source GLBs alone.

## Rendering Checks

The actual GPU fixture verifies texture sharing/reference lifetime, a non-block-aligned cooked texture, exposure response, a shadowed spotlight, six-face point shadows, moving occluders, disabled lights and a scene over the 2,048-caster budget. Spotlight shadowing changed 17,729 pixels in the reference fixture. HDR presentation uses the actual engine shader/pass; this is not a mock renderer.

Six-room capture verified 32 level/weapon models became GPU-ready, with 74 shared textures using 94.8 MiB, 72 cooked cache reads and zero image-decoding fallbacks. Flashlight-on rendered one shadow pass with 291 mesh/primitive draws. Game, flashlight and F2 editor-path images contain textured detail and were visually inspected for white/pink/blank regressions. F2 capture includes its existing helper geometry; it is not expected to be pixel-identical to gameplay.

Captures are written under `Tools/LevelAuthoring/bin/<configuration>/net10.0/qa`: `spot-shadow.png`, `point-shadow.png`, `six-room-cooked.png`, `six-room-flashlight.png`, `six-room-editor-cooked.png`. The tests capture engine-owned GPU render targets, not desktop screenshots. Standalone editor mouse/docking interactions and a full manual door/crate/gravity-gun playthrough remain human verification tasks.

The production replacement path is tested with a missing model and with CPU preparation complete while GPU uploads are blocked: both retain the original model/collider. Re-enabling uploads swaps only after the replacement becomes ready. The fixture does not save gameplay changes.

## Physics And Frame Measurements

400 randomized sphere/box/capsule contacts and 400 rays matched with BVH acceleration on/off. A synthetic 20,000-triangle Debug benchmark of 2,000 AABB queries measured 732.22 ms scanning versus 4.83 ms with BVH; both returned eight candidates per query. Bounds tests fell from 40,000,000 to 212,000. This isolates triangle candidate selection, not total physics time.

The final Release run of the same query fixture measured 103.87 ms scanning versus 0.91 ms BVH. Compare within each configuration, not Debug against Release.

Bounded stationary six-room Release captures used 1920x1009, 120 warm-up frames and 180 measured frames. Flashlight was off, no movement route or debris burst was played. Each row is one capture, not a multi-run average:

| Capture | Mean frame | P95 frame | Mean update | Mean physics | Mean world |
| --- | --- | --- | --- | --- | --- |
| Before this pass | 11.33 ms (88.2 FPS) | 14.78 ms | 1.75 ms | 1.77 ms | 7.29 ms |
| Cooked/HDR path | 9.68 ms (103.3 FPS) | 13.31 ms | 1.39 ms | 1.25 ms | 6.68 ms |

The after capture recorded 72 cooked hits, zero fallback decodes and zero mesh queries while stationary. Do not attribute that frame-time difference specifically to the BVH. Timers measure CPU/submission work, not separate GPU pass time. Shadow-heavy scenes still need performance tuning; this is not a guaranteed 100 FPS or locked-60 result.

## Light Control And Shadow Follow-Up (2026-10-06)

Completed: named light groups, repeatable switch interactions with optional power prerequisites, explicit light-ID targets, authored flicker, per-light saved enabled overrides, prefab link remapping and stable surviving IDs on revert. Editors preview authored defaults rather than player overrides. No source levels, GLBs, player save slots, dock layouts or weapon HUD settings were rewritten. Authoring instructions are in [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md).

Final follow-up verification: Debug and Release solution builds and the Release authoring-tool build passed with zero warnings/errors. All nine regression commands passed: `validate-lighting-state`, `validate-rendering`, `validate-level-rendering`, `validate-engine-upgrades`, `validate-collider-bounds`, `validate-six-room-test`, `validate-redox-inventory`, `validate-inventory-input`, and `validate-inventory-visuals`.

- 297 lighting assertions cover group/ID targeting, mixed starting states, two switches sharing state, repeated gameplay interaction, power prerequisites, rename-safe links, level serialization, production save loading, legacy/default behavior, wrong-level exclusion, prefab placement/apply/revert, deterministic flicker and randomized frustum geometry. This includes 2,000 random point/sphere scenarios; assertions apply to the clip-visible cases.
- The GPU fixture requires exact pixel equality between reference and optimized paths for cached spot/point shadows, changed intensity, caster removal, disabled/re-enabled lights, mutable hidden crate-part masks, model replacement and 24 moving-door/light frames. It also checks that invisible-caster ordering does not invalidate depth.
- Six real level viewpoints compare flashlight rendering against uncached/unculled output, allowing at most one RGB channel step out of 255. Final comparisons changed only 0-6 pixels per image by one step. At the spawn viewpoint, depth draws fell from the earlier 291 to 75. Each stationary room viewpoint reused its map with zero depth draws.
- Game/F2 textured captures were visually inspected again; F2's existing helper geometry remains visible. Full interactive standalone-editor docking/input and a manual switch/door/crate playthrough are still human checks, not claimed by these render-target tests.
- Existing checks still pass: 321 persistence/inventory checks, 11 input scenarios, inventory geometry at three resolutions, 1,000 collider-bound cases, five puzzle routes, crate/pickup settling, 800 BVH contact/ray comparisons, and ready-before-replacement including failed/CPU-only assets.

### Isolated Shadow Timing

Release D3D11, one stationary point light, six 1024-square faces, 600 small primitive casters. Each mode warms for 10 frames and measures 60. CPU recording is timed separately; native D3D11 timestamp/disjoint queries bracket command-list submission. Query readback is bounded and synchronous in the test tool only. Each mode had 60 valid non-disjoint samples in the final run.

| Mode | CPU shadow recording mean | GPU submitted commands mean | GPU P95 | Depth draws/frame |
| --- | --- | --- | --- | --- |
| Uncached, range-only culling reference | 5.556 ms | 0.104 ms | 0.106 ms | 3,600 |
| Uncached, per-face frustum culling | 2.764 ms | 0.072 ms | 0.075 ms | 666 |
| Unchanged cached maps + culling | 0.099 ms | <0.001 ms | <0.001 ms | 0 |

GPU samples include render-target clears and submitted shadow commands, but exclude main-world shading, PCF receiver cost, presentation, CPU buffer updates and startup. The cached result is near the measurement floor, not free total lighting. Results are one final capture per mode on this machine, not an overall FPS improvement or a realistic moving/debris stress workload. Switching/geometry movement invalidates affected maps and pays redraw cost again.

## Cooked Model Milestone (2026-10-06)

Implemented lossless LZ4 model caches with exact imported geometry, materials and named parts, plus dependencies on existing cooked textures. Source content hashes invalidate edits even when file size and timestamp stay the same. Model/cache corruption, format mismatches and unavailable texture dependencies fall back to source parsing. Runtime does not encode/write models. Source GLBs remain required; reading and hashing them is still part of the cached path.

Cooking all 376 model GLBs with textures already cooked took 7.8 seconds, produced 374 unique model files (identical content deduplicates), encoded no new textures and had zero failures. Their combined raw model payload was 14.88 MiB; stored LZ4 files including headers occupied 7.99 MiB. This excludes texture files and source GLBs, which are retained, so it is not a total-install-size reduction.

`validate-model-cache` compares all 376 GLBs / 628 imported parts: byte-exact positions, normals, UVs and indices; node/mesh/primitive identities; material values; and exact prepared texture keys, semantics and mip bytes. Fault checks cover missing/truncated/corrupt caches, format/source-hash mismatch, malformed decoded payloads with valid checksums, source changes with preserved size/timestamps, recooking, and missing/corrupt texture dependencies in fresh processes. Source bytes remain unchanged and atomic-write temporary files are removed.

### CPU Asset Preparation

Three alternating fresh-process Release runs, warm filesystem, 28 distinct directly referenced six-room GLBs, serial CPU loading, 56 unique compressed textures and zero image decodes in both modes. Models remain referenced during measurement. This excludes GPU uploads, physics/host initialization and first playable frame. The source-parser comparison disables only the model cache, not texture caching.

| Mode | Run 1 | Run 2 | Run 3 | Median | Managed allocations |
| --- | --- | --- | --- | --- | --- |
| GLB parsing + cooked textures | 358.14 ms | 281.59 ms | 286.22 ms | 286.22 ms | 302.87-302.88 MiB |
| Cooked model + cooked textures | 187.67 ms | 185.71 ms | 187.20 ms | 187.20 ms | 208.08-208.10 MiB |

About 34.6% less CPU preparation time and 31.3% fewer managed allocation bytes in this isolated fixture. Allocation bytes are not retained RAM or GPU memory.

### Harness Render-Ready Time

Separate alternating fresh-process Release runs at 1280x720, warm filesystem. Timing starts before creating the engine host, initializes the actual game module, waits for all 32 level/weapon models to upload, renders the complete scene and reads it back from the GPU. It excludes OS process startup, PNG encoding and first-playable input; these are not cold-disk or launch-button-to-control measurements.

| Mode | Run 1 | Run 2 | Run 3 | Median |
| --- | --- | --- | --- | --- |
| GLB parsing + cooked textures | 1,578.28 ms | 1,574.21 ms | 1,577.65 ms | 1,577.65 ms |
| Cooked model + cooked textures | 1,555.51 ms | 1,548.69 ms | 1,555.67 ms | 1,555.51 ms |

The median improvement here is only 22.14 ms (about 1.4%); other initialization/upload work dominates this small level. A later cached regression run measured 1,596.57 ms, illustrating that session-to-session variation can exceed this difference. Do not treat it as a guaranteed startup win or extrapolate the CPU-stage percentage to total startup or a much larger level. Cached runs reported 32 model hits and zero source parses; reference runs reported zero model hits and 32 source parses.

Actual six-room GPU checks still use 74 shared textures / 94.8 MiB, 72 texture-cache reads and zero image decodes. The spawn flashlight submits 75 depth draws; stationary cached maps submit none. Game/F2 textured captures, reference shadow image comparisons and failed/CPU-only replacement readiness are rerun with the new model path. Standalone editor input/docking and a manual gameplay route remain human verification tasks.

Final model-milestone verification: Debug and Release solution builds plus the Release authoring-tool build passed with zero warnings/errors. All ten regression commands passed: `validate-model-cache`, `validate-lighting-state`, `validate-rendering`, `validate-level-rendering`, `validate-engine-upgrades`, `validate-collider-bounds`, `validate-six-room-test`, `validate-redox-inventory`, `validate-inventory-input`, and `validate-inventory-visuals`. Game/F2/flashlight images were visually inspected; F2 still shows its existing helper geometry. No level/source-model/player-save/docking files were rewritten.

### Manual Test

1. Close old game/editor instances, then run root `LaunchSixRoomTest.bat`. Current models are already cooked, and the launcher rebuilds/copies the caches automatically. No reset or new save is required.
2. Check textured walls/floors and weapon models; move through doors, use the gravity gun, break a crate and open/close inventory. These should behave as before, with no changed model placement or missing textures.
3. Run `LaunchEditor.bat`, choose the level you want to edit, and check textured scene models, selection and transforms. The editor still follows the project's selected startup scene; it is not forced to sixRoomTest.
4. Test the earlier lighting milestone using [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md): create a shared Light Group, add a Light Switch targeting it, save, Play and press E at the switch. F toggles the gameplay flashlight. Existing levels were not automatically given switches.
5. After importing new/changed GLBs, run `CookAssets.bat` or Asset Importer > Compression > Cook models and textures. Restart running previews. Uncooked imports still work through the source fallback.

## Flashlight-Only Level Check (2026-10-06)

`validate-six-room-flashlight-test` passes on `sixRoomFlashlightTest.json`: 707 entities, no PointLight entities, zero directional strength, 13 retained puzzle indicators and 27 crates. Ambient was initially zero and is now **0.025** following the visibility playtest. A serialized comparison confirms only the intended lighting changes against the original lit six-room level. Existing player traversal, pickup/crate settling and all five puzzle/shutter tests pass on this copy.

Engine-owned GPU captures confirm the temporary zero-ambient reference is over 99% black while red indicators remain visible. The authored 0.025 ambient image has faint textured room detail (mean RGB 4.85/255 at the fixture's spawn view); enabling the flashlight changes more than 10,000 pixels and renders one shadow face. Disabling it restores the dim image within one RGB channel step, allowing GPU rounding. Marking indicator prerequisites complete in the test's temporary runtime state makes them visible green. This does not solve puzzles or change saves on disk. Off/on/green captures are under the authoring tool's `qa` folder as `six-room-dark-off.png`, `six-room-dark-on.png` and `six-room-dark-green.png`; off/on were visually inspected after the ambient change. The F key retains its existing binding.

## Moving Flashlight Bottleneck (2026-10-06)

Reproduce with the Release authoring-tool command `benchmark-flashlight`. The actual six-room low-light scene is rendered at 1920x1061 on this desktop (1920x1080 requested, window-client size reported). Assets are fully ready first. Each mode warms 30 frames and measures 180 frames, repeated for three rounds with reversed mode order in round two. Stationary and deterministic translating/turning camera routes compare flashlight off, on without shadows, on with individual-transform reference uploads, and on with batched uploads. The world render and HDR resolve paths are the production paths.

Identified bottleneck: moving the flashlight changes the light matrix every frame, so stationary depth-cache reuse cannot avoid redraws. The previous implementation made about 680 individual graphics-driver buffer updates per moving frame for shadow-caster transforms. These updates alone sit inside a shadow recording path measured at 2.16-2.17 ms in the initial zero-ambient baseline and 2.20-10.03 ms in the later paired capture. The F toggle does not change movement-speed settings; rendering delay can make movement feel less responsive.

Implemented one bulk object-transform upload per redraw, using a retained padded array with unchanged 256-byte binding offsets. All shadow passes share that upload; fully cached frames perform none. Caster selection/order, flashlight intensity/range/cone, 1024-square shadow resolution, PCF filtering and player movement remain unchanged. A reference toggle remains available for image/performance comparisons. This trades up to 512 KiB of retained CPU staging storage for far fewer driver calls; it does not change model loading or collision.

### Paired Moving-Route Results

| Round | Upload path | CPU render mean | Shadow CPU mean | Submitted GPU mean | Object upload calls/frame |
| --- | --- | --- | --- | --- | --- |
| 1 | Individual reference | 9.226 ms | 2.202 ms | 1.448 ms | 680.2 |
| 1 | Batched | 17.374 ms | 0.202 ms | 0.729 ms | 1.0 |
| 2 | Individual reference | 27.145 ms | 10.031 ms | 0.718 ms | 680.2 |
| 2 | Batched | 17.286 ms | 0.183 ms | 0.726 ms | 1.0 |
| 3 | Individual reference | 26.891 ms | 9.965 ms | 0.714 ms | 680.2 |
| 3 | Batched | 17.212 ms | 0.179 ms | 0.725 ms | 1.0 |

Both paths submit the same average 98.4 shadow draws per moving frame. Stationary warmed shadow frames submit zero draws/object uploads in both paths. In round three, moving flashlight-off CPU render mean was 16.795 ms, on-without-shadows 16.805 ms, and batched shadows 17.212 ms. GPU means were 0.621, 0.630 and 0.725 ms respectively. These isolate a substantial CPU/driver bottleneck; they do not indicate a dominant GPU bottleneck in this test.

Timing conditions visibly changed during the long capture: off/no-shadow CPU means moved from about 6.7 ms to 16.8 ms while submitted GPU times also changed. The cause was not established. Round-one total render timing therefore is not a clean before/after result; do not infer a guaranteed whole-game FPS gain or percent improvement from these numbers. The consistent evidence is the upload-call reduction, lower shadow CPU cost in every round, unchanged draw counts and matching rendered images.

CPU render recording includes buffer-update calls, scene draw submission setup and tone-map/resolve recording, but not gameplay Update, physics, HUD, swapchain presentation or OS input. Shadow CPU excludes gathering game entities into caster records. GPU timestamps bracket submitted commands including clears, world/shadow rendering and HDR resolve; immediate buffer updates before submission are not separately GPU-timed. Query polling is synchronous and test-only. No cold loading, debris burst, user-controlled traversal or end-to-end input-latency claim is made. Per-frame render-thread allocations remain about 345 KiB off and 692 KiB shadowed; path lookup/caster collection and main-camera draw culling are further optimization candidates, not fixed by this upload change.

Correctness coverage includes exact fixture image comparison with the old per-object upload path and 24 moving-door/light frames; six real-room shadow comparisons also use the old upload reference. The low-light fixture checks ambient visibility, on/off restoration, red/green indicators, all five puzzles, player routes and pickup/crate settling. Raw timing output from this run is `Tools/LevelAuthoring/bin/Release/net10.0/qa/flashlight-profile-comparison.txt` (disposable); this table preserves the measured results.

Final verification: Debug/Release solution and authoring-tool builds passed with zero warnings/errors. `validate-six-room-flashlight-test` passed in both configurations. Release `validate-rendering`, `validate-level-rendering`, `validate-lighting-state`, `validate-six-room-test` and `validate-inventory-visuals` also passed. The only authored level-value change is the test copy's ambient strength; the original lit level, layout, colliders, saves and movement settings were not edited. Actual human input responsiveness remains a playtest check, not a result measured by the camera-route fixture.

## Full Gameplay Flashlight Follow-Up (2026-10-06)

The render-only test above missed a second driver bottleneck in the main scene and the cost of the Debug gameplay loop. The scene issued **762 separate object-buffer updates per frame**, including frames where the shadow pass was already batched. Main-scene uniforms now use one retained, 256-byte-padded upload per frame in Game (including F2) and HS2Editor. Textures, model/normal transforms, draw order, shader binaries, shadow quality, movement settings and collision are unchanged. The staging array is bounded at 1 MiB per world renderer; the existing 4,096-draw limit is unchanged. Shadow uploads remain independent.

`LaunchSixRoomFlashlightTest.bat` now explicitly builds Release and runs that build without rebuilding again. It retains launch-profile UI settings, reports build failures and still opens the low-ambient test copy without regenerating it. Other launchers and Visual Studio configurations are unchanged. A bounded test of this actual batch file built successfully and ran/exited normally. Dependency restore required normal desktop permissions; it failed without useful build diagnostics inside the restricted test environment.

### Normal Desktop Gameplay Comparison

Fresh-process Release runs, 1920x1009 client area, warm assets, VSync requested, 120 warmup + 300 measured frames. The test wraps the actual EngineHost/game module, alternating W/S input and relative mouse motion; it includes Update, fixed-step physics, the production HUD, world rendering and presentation. Launcher UI environment settings are explicitly applied. No camera/velocity teleportation or GPU readback waits are inserted. These final runs use normal desktop execution, matching the launcher permission context.

| Mode | Mean frame | P95 frame | Physics/frame | World/frame | Submit/present | Main object uploads/frame |
| --- | --- | --- | --- | --- | --- | --- |
| Flashlight on, individual-upload reference | 23.93 ms (41.8 FPS) | 25.99 ms | 3.11 ms | 19.29 ms | 0.10 ms | 762 |
| Flashlight on, batched | 16.66 ms (60.0 FPS) | 16.81 ms | 1.94 ms | 3.65 ms | 9.60 ms | 1 |
| Flashlight off, batched | 16.66 ms (60.0 FPS) | 16.86 ms | 1.93 ms | 3.22 ms | 10.07 ms | 1 |

The optimized runs spend spare time waiting in presentation rather than spending it on per-object driver updates. Both measured 300 fixed ticks / 5.000 simulated seconds: 32.382 m with the light on and 32.347 m off, including acceleration and reversals. Neither recorded frames over 33.3 ms or the host's 100 ms simulation clamp. Small differences reflect frame timing/input integration, not a changed movement-speed setting. Raw logs are `qa/gameplay-on-reference-final.txt`, `gameplay-on-batched-final.txt` and `gameplay-off-batched-final.txt` under the Release authoring-tool output.

Earlier restricted-environment exploratory runs had different presentation timing: Release flashlight-on averaged 32.76/32.27 ms with individual uploads versus 7.31/7.14 ms batched, with almost no presentation wait. They establish the same CPU/driver improvement but must not be presented as normal displayed FPS. Initial probes also inherited UI environment settings instead of forcing the launcher settings; the current tool fixes that. Production HUD logic still selects its existing supported presentation path.

Debug exploratory captures showed the extra failure mode: flashlight-on averaged 126.97 ms before main-scene batching (73.12 ms physics, 51.44 ms world), versus 20.21 ms after (13.82 ms physics, 4.34 ms world). The original 300-frame run advanced only 30 simulated seconds over about 38 wall-clock seconds because of the existing 100 ms frame-delta clamp. That can genuinely slow movement, not just camera responsiveness. Reducing work and selecting Release removes that condition in these tests; the timestep/clamp and player speed were not altered. Fixed ticks per rendered frame increase as rendering falls behind, which magnifies Debug physics cost. This is not evidence that the flashlight intentionally changes walking speed.

### Reproduce And Verify

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release --no-restore
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on --individual-uploads
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight off
```

Run profiles sequentially, with other game/editor instances closed. Each opens a native game window and exits after the bounded sample; it does not save player progress. The reference flag disables only main-scene batching, leaving the previous shadow optimization enabled. Environmental flags are restored when the tool exits. Fixed frame counts cover different simulated durations in slower runs, so this is a repeatable input pattern, not an identical per-frame camera trace. Results are short local samples, not a locked-60 guarantee across the whole level or input-to-display latency measurements.

Verification: Debug/Release solution builds passed with zero warnings/errors. `validate-rendering` passes empty batches, 4,096-object overflow bounds, mixed textured/primitive comparison and 24 moving-door/light frames with the individual-upload reference. `validate-level-rendering` passes all six room comparisons and textured F2 output (1,612 individual uploads versus one, maximum RGB difference 1/255). `validate-six-room-flashlight-test` passes textured on/off/ambient/indicator captures, all five puzzles, production player routes, shutter collisions and pickup/crate settling. Flashlight image comparison replaced 762 uploads with one, with only two pixels differing by one RGB step; the on capture was visually inspected. Original lit-level data, the 0.025 ambient value, saves, layouts and authored geometry were untouched in this follow-up. Standalone-editor mouse/docking behavior and a manual controller/heavy-debris playthrough remain human checks.

The authoring tool also built cleanly in both configurations. The low-light fixture passed again in Debug (maximum channel difference 1/255), and native inventory visual-layout checks passed at 800x600, 1300x775 and 1920x1080.

## Baseline Accepted And Standardized (2026-10-06)

User playtest feedback confirms the flashlight movement is now much smoother. Keep main-scene and shadow batching as the production default; the individual-upload switches are test references, not alternative normal settings. The approved baseline and next milestone order are in [ENGINE_TECHNOLOGY_ROADMAP.md](ENGINE_TECHNOLOGY_ROADMAP.md).

`LaunchGame.bat` and `LaunchSixRoomTest.bat` now follow the flashlight launcher's explicit Release build/run flow, retaining their original `sixRoomTest.json` target and launch-profile UI settings. Both passed normal-desktop bounded launch tests (120 warmup + 60 measured frames, 1920x1009): mean 16.66 ms, P95 24.80/24.79 ms respectively. These short stationary smoke tests verify launch/build behavior, not stutter-free traversal or heavy gameplay performance. Build failures preserve a nonzero exit code instead of being hidden by the pause.

HS2Editor Play Selected Level / Launch Game From Level now requests Release while retaining save-then-launch and the selected level path. The Release editor build passed with zero warnings/errors; the editor button was not interactively clicked in this verification. Visual Studio still honors the selected solution configuration, so use Release there for comparable playtests. No level data, startup selection, authored lighting, collision, renderer quality or movement settings changed in this standardization. The subsequent culling/visual-streaming implementation is recorded below; the earlier measurements above describe their own implementation stages.

## Visibility And Visual Streaming (2026-10-06)

Implemented main-view transformed-bounds culling and spatial-zone model dependency prefetch/residency. The default level settings are enabled, 32-unit XZ zones, one neighbour ring, 256 MiB soft budget and 15 seconds retention. Two CPU preparation workers feed the existing soft 2 ms GPU upload budget. Visible/near/shadow-relevant/held/debris/weapon dependencies remain pinned; unused model resources retire behind GPU fences. Old resources stay alive while incoming resources prepare. Logical entities, colliders and gameplay state are not unloaded. See [STREAMING_GUIDE.md](STREAMING_GUIDE.md) for controls and limitations.

### Full-Host Measurements

Fresh-process Release, normal desktop, 1920x1009 client area, VSync requested, warm cooked assets, 120 warmup + 300 measured frames. The same scripted W/S/relative-mouse input and production HUD/physics/presentation path were used. Both references retain the accepted main/shadow uniform batching; `--reference-visibility` disables only main-view culling and visual streaming.

| Run | Mean frame | P95 frame | World CPU/frame | Main draws/frame | Streaming wait frames |
| --- | --- | --- | --- | --- | --- |
| Walking, flashlight on, reference | 16.66 ms (60.0 FPS) | 16.88 ms | 3.72 ms | 762.0 | 0 |
| Walking, flashlight on, new defaults | 16.66 ms (60.0 FPS) | 16.88 ms | 2.12 ms | 368.1 | 0 |
| Walking, flashlight off, new defaults | 16.66 ms (60.0 FPS) | 16.89 ms | 1.87 ms | 369.6 | 0 |
| Combined stress, flashlight on, reference | 16.66 ms (60.0 FPS) | 16.89 ms | 4.11 ms | 829.7 | 0 |
| Combined stress, flashlight on, new defaults | 16.71 ms (59.8 FPS) | 16.88 ms | 2.27 ms | 388.5 | 0 |

Walking runs each advanced 300 fixed ticks / 5.000 simulated seconds and travelled 32.382 m including acceleration/reversals, with no frames above 33.3 ms. Stress runs invoke the production shutter-animation, crate-damage, gravity-hold/release and inventory open/close paths; six crates produce a peak of 120 fragment bodies. The new stress run recorded one frame above 33.3 ms and none above the host's 100 ms clamp; the reference recorded neither. All runs used one main-object upload/frame. This demonstrates reduced submission work, not a guaranteed FPS increase at the VSync cap or a zero-stutter claim.

The inventory pause means stress movement distance is not directly comparable with the walking-only run: 25.203 m / 5.017 simulated seconds (new) versus 25.122 m / 5.000 seconds (reference), both 5.024 m/s including pauses/reversals. Player speed/timestep settings were not changed. World CPU timings include driver/submission work and are not GPU timestamps. Restricted execution had different presentation timing; final comparisons use matching normal-desktop execution. Raw disposable logs: `Tools/LevelAuthoring/bin/Release/net10.0/qa/visibility-reference.txt`, `visibility-streaming-on.txt`, `visibility-streaming-off.txt`, `visibility-reference-stress.txt`, `visibility-streaming-stress.txt`.

All walking/stress runs retained 32 models / 99.7 MiB of owned geometry plus shared texture payload, with ten derived dependency zones and no evictions. Nearby rooms share these models; culling reduces draws but does not imply a residency reduction on this route.

### Correctness And Residency

- `validate-visibility-streaming`: 3,000 randomized rotated/non-uniform/negative-scale bounds, 15,124 clip-visible corners, offscreen rejection and invalid-bound fail-open checks. Shared/global/boundary dependencies, negative coordinates, one/two-ring prefetch and level-setting serialization pass.
- An in-memory two-area fixture adds distinct existing arcade models 2,000 units apart, with 8-unit zones, zero retention and a deliberately tiny 1 MiB soft budget. It verifies no eager distant load, required assets surviving budget pressure, blocked incoming GPU uploads retaining the old working set, fence-safe retirement, real reconstruction on return, and gravity-held pins.
- That fixture's owned geometry/shared texture payload fell from 81.4 to 28.6 MiB when moving away; it observed 36 cache evictions across its transitions. This counter includes unused registered entries, not only GPU models. These figures are not total process memory/VRAM or results from the normal walking route. Fence retirement can defer physical release beyond the ownership count.
- The same entity references/order/IDs, damaged health, collected pickup flags, inventory contents, solved state, open-door position and light override remain intact after eviction/reload. Separate existing persistence checks exercise actual save contracts; the transition fixture deliberately leaves player saves untouched.
- `validate-level-rendering` compares six room viewpoints and textured F2 output against the no-culling reference within one RGB step out of 255. The spawn image goes from 762 to 412 main-view draws. Failed and CPU-only replacements retain their original visual/collider until GPU-ready.
- `validate-rendering` covers mixed primitive/textured output, 4,096-object upload bounds and 24 moving-door/light frames. The low-light test retains ambient visibility, red/green indicators, flashlight on/off restoration, traversable open shutters and stable pickup/crate physics.
- Lighting-state (297 checks), persistence/inventory (326 checks), native/fallback inventory input, native inventory visuals at three sizes, compressed-asset/mesh-query regressions, 1,000 collider-bound cases and the lit six-room routes/puzzles pass.

Final builds: the solution and authoring tool build in both Debug and Release with zero warnings/errors. `validate-visibility-streaming` passes in both configurations, and the low-light fixture passes again in Debug. Release level-rendering and renderer fixtures were rerun after the final code changes. The flashlight-on and F2 textured captures were visually inspected. Scoped `git diff --check` reports no whitespace errors.

Reproduction commands are in [STREAMING_GUIDE.md](STREAMING_GUIDE.md). The bounded stress test is automated, not a manual controller/door/interactions playthrough. Cold OS-cache loading, complete game GPU timing, standalone-editor mouse/docking validation and a long memory soak remain unverified. No authored model, level layout, lighting values, source art or player saves were edited for this milestone.

## Collision Cache Milestone (2026-10-21)

Static mesh placements now use checksum-validated, lossless LZ4 caches of transformed triangles, index ordering and BVH nodes. The source-content/pose/skin/build key protects edited architecture; moving mesh-door poses explicitly bypass the disk cache. Missing/invalid/unwritable cache files retain the original construction path. No authored level, model, solver, movement, lighting or ammo pickup was changed. The user is sourcing a dedicated ammo model; the crate placeholder remains intentional until then. See [COLLISION_CACHE_GUIDE.md](COLLISION_CACHE_GUIDE.md).

Three fresh-process Release source/cached pairs, warm filesystem and normal-desktop permissions:

| Sample | Source collision stage | Cached collision stage | Source host/game init | Cached host/game init |
| --- | --- | --- | --- | --- |
| 1 | 31.313 ms | 13.939 ms | 1085.396 ms | 1049.831 ms |
| 2 | 30.407 ms | 14.135 ms | 1067.538 ms | 1054.602 ms |
| 3 | 31.020 ms | 13.771 ms | 1061.827 ms | 1055.769 ms |
| Median | 31.020 ms | 13.939 ms | 1067.538 ms | 1054.602 ms |

Each cached run hits all five six-room static mesh colliders with zero builds/writes. Source GLB hashing remains part of the cached stage. The current level uses mostly primitive colliders, so total initialization gains are modest. These figures exclude GPU-ready/first-playable time and true OS-cold loading. First-time cache generation costs extra and was not included in the cached timing column.

Synthetic 20,000-triangle tests (seven measured warm samples after two warmups) recorded 49.690 ms build / 3.950 ms validated read initially, then 23.306 / 5.373 ms on the final Release rerun. The file is 400,969 bytes. The variation prevents a single reliable synthetic speedup claim; neither run represents whole-game loading.

Verification:

- Solution and authoring-tool builds pass in Debug and Release with zero warnings/errors.
- `validate-collision-cache` passes in both configurations: exact triangle/bounds/skin comparison, 400 contacts/rays, moved/rotated/non-uniform placements, stale/version/key/corrupt/truncated/oversized payload rejection, checksum-valid bad index/tree rejection, repair and unwritable fallback. Production colliders match the original path, and moving-pose calls do not touch disk caches.
- Release `validate-six-room-test`, `validate-six-room-flashlight-test`, `validate-level-rendering`, `validate-visibility-streaming` and `validate-collider-bounds` pass. This covers player routes, shutter blocking/opening, puzzle state, pickup/crate settling, textured Game/F2 output, replacement readiness and streaming ownership/state preservation.
- Normal desktop Release full-host profiles (1920x1009, 120 warmup + 300 measured frames) retain approximately 60 FPS: torch on mean 16.53 ms / P95 17.33 ms, off 16.64 / 18.16 ms, combined stress 16.70 / 17.25 ms. Wall-frame means were 16.662, 16.662 and 16.717 ms. No measured frame exceeded 33.3 ms or the 100 ms clamp; streaming waits were zero. Stress produced 120 peak fragment bodies and completed shutter/crate/gravity-hold/inventory event groups. These are short regression samples, not a new universal performance guarantee or manual input-latency test.
- Restricted execution denied atomic file replacement; normal desktop tests verified successful writes. The cache correctly fell back rather than removing collision in the unwritable case. No source art or player save was edited.

## Remaining Work

See the October 22 emission milestone below for the latest material/lighting validation.

- Cold and warm end-to-end first-playable startup, full-game/per-pass GPU timings, input-to-display latency and longer heterogeneous-asset/debris memory-soak routes. Short full-host walking and combined door/crate/gravity/inventory stress are covered above.
- Full logical/physics room streaming, portal-aware dependencies and behind-wall occlusion. Automatic spatial visual dependencies, neighbour prefetch, shared-resource eviction and state retention are implemented.
- Shared local-space collision instances, disk-cache pruning, vertex/index optimization and packaged dependency metadata. Static-placement collision/BVH and imported model caches are implemented; source GLBs remain authoritative.
- Probes, larger/multi-room bake budgets, bloom and further shadow-budget improvements. The bounded static-indirect milestone above, imported emission/fixture controls, main-view frustum culling, switches/groups/flicker, saved enabled state and shadow caching/culling are complete within their documented scope.
- Full physics-solver evaluation only if representative profiles justify it; no language rewrite is part of this pass.

Keep existing visuals/colliders alive until replacements are ready. Missing/corrupt texture caches may use a compatible RGBA path, but a failed model replacement must retain the original object.

## Emissive Fixtures (2026-10-22)

Implemented GLB emissive maps/factors/strength, emissive UV selection/transform, BC7/sRGB preparation, model-cache v2, linear HDR emission, per-instance intensity/optional part-colour overrides, and stable-ID links to authored lights. Game, F2 and HS2Editor use the same emission-state calculation. Light editor markers no longer become opaque runtime boxes/shadow casters. Source GLBs and the existing lit/dim levels are unchanged. The new `sixRoomEmissionTest.json` preserves all 707 baseline entities and adds five test entities, with a separate Release launcher. Ammo remains deferred.

Final solution and authoring-tool builds pass in Debug and Release with zero warnings/errors. All 41 emission checks pass in both configurations, including a numeric sRGB decode/linear radiance/tone-map check. The existing collision-cache regression also passes after these changes.

Verification:

- `validate-emission`: synthetic embedded GLB emission without a base-colour image, emissive-strength extension, secondary UV channel/texture transform, v2 metadata round-trip, v1/truncated cache fallback and emissive texture repair. GPU readbacks check source versus BC7, emission with no lights, switch-off restoration, per-part override isolation, two differently configured instances of a shared model, factor-only/no-UV fallback and exposure. State tests cover disabled/zero/missing links, independent editor defaults, deterministic flicker, RE:Dox level/state round-trips, prefab placement/apply/revert and undo/redo.
- `validate-lighting-state`: 298 checks including the runtime light-helper fix. `validate-model-cache`: all 376 GLBs / 628 parts preserve geometry, UVs, identities, material/emission fields and prepared texture mips; fresh-process failure cases pass. Cooking took 54.1 seconds with existing texture caches, producing 374 unique v2 model caches and seven new image caches, no failures. This is offline cooking, not level loading time.
- `validate-rendering`, `validate-level-rendering`, `validate-six-room-flashlight-test` and `validate-visibility-streaming` pass. They cover real spot/point shadows, textured F2 output, normal materials, culling/reference agreement, ready-before-replacement, traversable shutters, pickup/crate settling, puzzles, GPU-ready eviction/reload and persistent state. The updated residency fixture measured 87.3 -> 31.8 MiB of owned buffers/shared textures; figures exclude total process/driver memory.
- `validate-six-room-emission-test`: gameplay interaction reaches the power lever from spawn, turns the actual light and screen emission off, and restores the image on repeated use. An emission-only frame isolates glow from the paired light. On/off screen captures were visually inspected; fixture facing and light-marker visibility were corrected before the final capture.

Normal-desktop Release gameplay tests, 1920x1009 client area, VSync requested, warm assets, 120 warmup + 300 measured frames:

| Route | Mean frame | P95 frame | World CPU | Frames >33.3 ms | Streaming waits |
| --- | --- | --- | --- | --- | --- |
| Original dim level, torch off | 16.65 ms | 16.83 ms | 2.17 ms | 0 | 0 |
| Original dim level, torch on | 16.65 ms | 16.80 ms | 2.40 ms | 0 | 0 |
| Original dim level, torch on + stress | 16.71 ms | 17.94 ms | 2.72 ms | 1 | 0 |
| Emission test, fixture and torch on | 16.65 ms | 16.89 ms | 2.81 ms | 0 | 0 |

Walking on/off both travelled 32.382 m over 5.000 simulated seconds with 300 fixed ticks, including acceleration/reversals. The stress run completed all shutter/crate/gravity/inventory groups with 120 peak fragments. The new test's scripted path hits its console stand, so its 23.382 m movement is not a speed comparison. All runs used one main-object upload/frame and no frame crossed the 100 ms simulation clamp. These short local runs remain near the accepted 60 FPS cap, not a universal zero-stutter guarantee or input-latency measurement.

Owned geometry plus shared texture payload on the normal route is now 107.1 MiB versus the previous 99.7 MiB: newly supported emissive images and the additional UV channel have a real memory cost. It still retains 32 models with ten zones and no route evictions. Standalone editor docking/mouse interaction, long memory soaks, true cold startup and full-game GPU timings remain manual/future checks. Restricted execution blocks atomic replacement in temporary-file tests; those checks were rerun successfully with normal desktop permissions.

Reproduce with `Tools/LevelAuthoring` in Release: `validate-emission`, `validate-lighting-state`, `validate-model-cache`, `validate-rendering`, `validate-six-room-emission-test`, `benchmark-gameplay-flashlight on`, `benchmark-gameplay-flashlight off`, `benchmark-gameplay-flashlight on --stress`, and `benchmark-gameplay-emission on`. Disposable captures are under `Tools/LevelAuthoring/bin/Release/net10.0/qa/` (`emission-*.png` and `six-room-emission-*.png`). No baked GI, probes, bloom, new solver or C++ migration is implemented in this milestone.

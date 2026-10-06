# Compression, Lighting And Physics Validation

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

## Remaining Work

- Cold and warm end-to-end first-playable startup, full-game/per-pass GPU timings, input-to-display latency and longer heterogeneous-asset/debris memory-soak routes. Short full-host walking and combined door/crate/gravity/inventory stress are covered above.
- Full logical/physics room streaming, portal-aware dependencies and behind-wall occlusion. Automatic spatial visual dependencies, neighbour prefetch, shared-resource eviction and state retention are implemented.
- Cooked collision/BVH acceleration, vertex/index optimization and packaged dependency metadata. Imported model geometry/material caching with lossless LZ4 is complete; source GLBs remain authoritative.
- Baked indirect lighting/probes, emission and further shadow-budget improvements. Main-view frustum culling, switches/groups/flicker, saved enabled state and shadow caching/culling are complete.
- Full physics-solver evaluation only if representative profiles justify it; no language rewrite is part of this pass.

Keep existing visuals/colliders alive until replacements are ready. Missing/corrupt texture caches may use a compatible RGBA path, but a failed model replacement must retain the original object.

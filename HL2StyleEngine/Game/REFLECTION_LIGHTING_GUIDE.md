# Controlled Specular Environment (Unit C)

Implemented 2026-10-23 under [the upgrade plan](ENGINE_UPGRADE_PLAN.md). This stage uses one synthetic distant lighting rig to validate specular environment math. It is **not a capture of the room**, a mirror, or a switch-aware room probe. C# remains the runtime. Existing levels default to no environment and retain their accepted images.

## Try It

Run root `LaunchPbrMaterialTest.bat`. Only `pbrMaterialTest.json` opts into PBR and the synthetic studio. In F2 or HS2Editor, **Toolbar > Lighting > Specular Environment** selects Off or Synthetic Test Studio, with save/load and undo/redo. **Toolbar > Indirect Lighting > PBR Diagnostic > Specular Indirect** isolates reflections; Full restores normal lighting. F toggles the flashlight. The second-room light switch still controls its direct lamp/emission; it does not control the independent synthetic studio.

## Contract

Fixture acceptance follow-up: the crate uses a corrected dielectric wood material as of 2026-10-23; the imported original was effectively polished metal and showed only a tiny flashlight highlight. See [material correction and tests](PBR_MATERIAL_GUIDE.md#crate-material-correction-2026-10-23). This is a local asset correction, not a reflection/shadow shader change. Existing reference images retain the historical crate via an explicitly logged in-memory test assignment; fresh captures use the corrected crate.

- One 128x128 six-face RGBA16F cubemap, eight explicitly GGX-prefiltered mips; no ordinary mip-generation substitution. Face order is +X, -X, +Y, -Y, +Z, -Z using the D3D convention tested on the GPU.
- Perceptual roughness is clamped to .04-1; alpha=roughness squared. Filter mip i uses i/7; shader samples roughness*7. The first mip retains the source radiance. Filtering uses 256 deterministic Hammersley samples and cosine-weighted normalization, with N=V=R.
- The 128x128 RG16F LUT uses 1024 samples per texel, height-correlated Smith GGX and Schlick Fresnel, matching direct PBR. Coordinates are (NoV, perceptual roughness). Split sum is filtered radiance*(F0*A+B), F0=lerp(.04,baseColour,metallic). This is a single-scatter approximation, not multi-scatter compensation.
- The cubemap adds **specular only**. Existing diffuse E/pi lightmaps/probes, direct lighting and emission retain their separate contributions. The synthetic environment has no dependency on dynamic puzzle lights.
- Separate resource set 6: t6 cubemap, t7 LUT, s4 trilinear clamp sampler, b4 immutable metadata. The 256-byte object slot and one main object upload remain unchanged. A valid black cubemap/LUT is always bound when absent or incomplete; disabled sampling is skipped.
- `ReflectionEnvironment` defaults to 0/None and is omitted from JSON, preserving old bake identities; 1 selects the synthetic test. Both editor hosts and game resolve the same setting. No original authored level or model is migrated.
- The specular preset is excluded from diffuse-bake snapshot keys. Changing it does not require rebaking. The existing Direct Only view suppresses reflection sampling; contribution diagnostics can explicitly isolate it.

## Cooking And Lifetime

Build the authoring tool, then run `cook-reflection-environment`; **rebuild afterward** to embed the generated `Engine.Render/Assets/SyntheticStudio.hs2env`. It is included in the renderer assembly for game/editor/publish without depending on a working directory. The cooker writes atomically, with filter version 1, bounded dimensions/payload, SHA-256 and finite non-negative texel validation. Corrupt/missing/version-mismatched data fails to the zero-energy fallback.

The worker reads and validates the asset. Resource creation and one-face/one-mip transfers share the renderer's existing 2 ms **soft** upload budget; individual API calls cannot be preempted. All faces and the LUT must be ready before binding. No runtime capture, filtering, or routine-frame `WaitForIdle` occurs. The single immutable preset is retained until renderer shutdown to make toggles immediate, even when disabled; this is not general probe residency. Public caller-owned environments must be retired through `Renderer.RetireAfterFrame` before disposal after replacement.

GPU texture payload: 1,048,560 bytes cubemap + 65,536 bytes LUT = **1,114,096 bytes**. Metadata and the black fallback add 84 bytes, for **1,114,180 bytes (1.063 MiB)** per renderer that has loaded the preset. A renderer that never requests it owns only the 68-byte fallback. The prepared CPU payload is released after upload. Embedded assembly data, checksum/read buffers, driver allocations and tooling/filter scratch are additional, not included in GPU payload counters. Cook time observed locally: 2.30 seconds; not level-load time.

## Acceptance Budget

Unit C keeps the A/B whole-host gate: mean <=17 ms, P95 <=21 ms, at most one >33.3 ms sample in 300, none >100 ms, zero streaming waits. Target remains about 60 FPS with VSync, not a guarantee of a 60 FPS minimum on all hardware/content.

Before reflection profiling: incremental on/off GPU mean/P95 <=1 ms; CPU mean <=.5 ms and P95 <=1 ms, same repeated/counterbalanced route. Additional GPU payload <=1.1 MiB, no pending prepared payload after warmup, one main upload/frame. Record all blocks; startup, GPU clocks and system load can bias the first block. Do not compare it to a later warm block as a feature-cost estimate.

Pre-C repeated PBR baseline on RTX 3060, D3D11 debug layer on, Release, 1920x1061, RGBA16F/4xMSAA: torch off CPU .514/.561 ms and GPU 1.873/2.305 ms; torch on CPU .553/.611 ms and GPU 2.498/2.974 ms (mean/P95). Model payload 49.38 MiB. True cold first-playable timing and manual input/docking acceptance remain open.

## Verification

Local results: 97 Release / 98 Debug reflection assertions (the readiness polling frame count can vary), 126 direct-PBR HDR assertions, 282 bake/probe checks and GPU shadow/moving-door/batching regressions pass. All 15 legacy and three reflection-off PBR references remain within 1/255. Three new reflection images were visually reviewed (game, F2, standalone scene renderer); F2's existing spawn helper still occupies the foreground at the fixed capture camera. Debug/Release full solution and tool builds pass without warnings/errors.

Repeated render-only final pair at 1920x1061:

| Reflections / torch | CPU mean / P95 (ms) | GPU mean / P95 (ms) |
| --- | --- | --- |
| Off / off | .509 / .558 | 1.749 / 1.945 |
| On / off | .501 / .543 | 2.027 / 2.234 |
| Off / on | .571 / .618 | 2.016 / 2.385 |
| On / on | .567 / .611 | 2.498 / 2.598 |

GPU increments +.278/+.290 ms off and +.482/+.213 ms on pass. Counterbalanced order also passes (+.050/-.259 and -.217/-.595 ms); negative values are noise/clock effects, not a speedup. All blocks are printed. Model GPU payload remains 49.38 MiB; additional reflection payload 1.063 MiB. Warm steady allocations 9.87/11.70 KiB/frame; final enabled blocks GC 0/0/0, peak managed 58.8 MiB and working set 330.5 MiB in this run. These are process observations, not isolated effect allocations or memory guarantees.

Full-host 1920x1009, same RTX 3060/configuration, 120 warmup + 300 measured: torch on mean/P95 **16.661/17.892 ms**, off **16.661/17.844 ms**, about 60 FPS. Both have no >33.3 ms frames, no >100 ms clamp frames and no streaming waits. One main upload/frame and 6.476 m/s scripted movement. These measurements pass the recorded gates. No human controller/mouse/docking playtest or true cold-load result is claimed.

Final geometric back-face guard recheck: reflection-reference images deliberately reviewed/updated only for that correction, then all three reproduce within 1/255. The untouched Legacy/direct-only references also pass. Guard uses the interpolated geometric hemisphere, not a hard cutoff on detailed normal-map normals (which caused grazing speckles in an intermediate check and was rejected). Final torch on/off host mean/P95 **16.667/18.079 ms** and **16.667/17.932 ms**, zero >33.3 ms frames/streaming waits. Counterbalanced GPU increments +.013/-.012 ms off and -.162/+.097 ms on remain within budget; clock/load noise is still present.

Authoring-tool commands: `validate-reflections`, `capture-reflection-room`, `validate-reflection-reference`, `profile-reflections [--reverse]`, `benchmark-gameplay-pbr on|off`. Existing `validate-pbr-legacy` and `validate-pbr-reference` explicitly disable reflections and retain their original references. New reflection images are kept separately, never substituted for old references.

The reflection harness covers deterministic recooking, binary rejection, an independently integrated BRDF, white-furnace energy bounds, all six GPU face targets, axis and asymmetric off-axis reflected directions, dielectric F0, roughness broadening, incomplete/disabled fallback, contribution sum, legacy isolation, shared upload budget and embedded resource readiness. Numeric checks use pre-tonemap HDR readbacks. Rendering captures exercise game/F2/standalone scene paths, not manual mouse/controller input.

Follow-ups delivered: **unit D** adds authored captures/cache ([guide](ROOM_REFLECTION_GUIDE.md)); **unit E's first pass** adds automatic room selection, box projection and doorway blending with invariant-light fallback ([guide](ROOM_TRANSITION_GUIDE.md)). Explicit switched presets, residency and real-level rollout remain later. The synthetic test still is not a capture of its room.

Math reference: [Filament's image-based lighting and integration discussion](https://google.github.io/filament/main/filament.html). Implementation is local; no external engine code was copied.

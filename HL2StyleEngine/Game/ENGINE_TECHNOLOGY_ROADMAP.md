# Engine Technology Roadmap

Current physics follow-up (2026-10-28): first-settling/JIT profiling led to one-entry collider geometry caches and same-integration support reuse. Simulation results match 1,200 pre-change frames exactly; warm-cache same-binary startup median improves 3189.3 -> 2900.7 ms. No solver, C#, visual quality, fixed-step or readiness changes. [Measurements and gameplay acceptance](PHYSICS_STARTUP_GUIDE.md). Next feature milestone: texture-level ownership and room/projected-size mip demand, then a controlled real-level trial with full-detail restoration. Spatial audio follows separately. Cold evidence and manual long playtests remain open; historical status entries follow.

Current stability/startup pass (2026-10-28): longer scripted gameplay/save-load/reload tests now cover both UI backends with isolated QA saves; a separate six-room route verifies actual door movement. Hidden save-slot reads are removed; save serialization prepares during loading, retaining atomic writes/backups/disk flush. Same-frame startup timings isolate early physics settling and catch-up, without changing simulation or claiming a total-load improvement. [Implementation, current evidence and remaining limits](GAMEPLAY_STABILITY_GUIDE.md). C#, full-detail textures, lighting and source levels remain unchanged. Next: first-physics profiling/behaviour-preserving optimization, longer acceptance and cold candidates, then texture-granularity/room-aware mip demand. Spatial audio remains separate. Older entries below are historical.

Current inventory follow-up (2026-10-27): measured first-open work is reduced on both UI paths through unused-document suppression, bounded text metadata and loading-time native/icon preparation. Populated native first-open 204.880 ms becomes 26.293/17.668 ms in repeats; all seven latest source/package trials pass unchanged near-60-FPS limits. Native and fallback are tested separately. C#, source assets, lighting and saves stay unchanged. Next: longer save/load/level-reload acceptance and measured startup critical path, then texture-granularity/room-aware mip demand before production rollout. Spatial audio stays the following separate milestone. [Implementation, evidence and limits](INVENTORY_PERFORMANCE_GUIDE.md). Earlier status entries below are historical.

Current stability follow-up (2026-10-27): delivered opt-in frame-stage/allocation/GC tracing, isolated fresh-process source/package comparisons, per-crate support-bound reuse and bounded inventory-icon prewarming. Median six-crate break Update falls about 31%; seven current trials pass the original near-60-FPS gates with one recorded hitch. The fresh-process baseline also passed, so the earlier failures are not proven package-specific. Next: first-inventory document/layout/JIT costs, longer real-game acceptance and measured startup critical path; then texture-granularity/room-aware mip integration. Spatial audio remains the following separate milestone. [Evidence and limitations](HITCH_STABILITY_GUIDE.md). Historical entries below retain their original results.

Latest first pass (2026-10-27): expanded startup diagnostics plus opt-in mip-tail loading, budget planning and fence-safe replacement are available through `LaunchTextureMipTest.bat`. Existing game/editor texture quality is unchanged. 483 mip checks and GPU restore/retry/lifetime tests pass; initial gallery soak is near 60 FPS. Full-game torch-on stress fails twice, so production adoption stays gated on profiling and visual acceptance. [Evidence and limitations](TEXTURE_MIP_GUIDE.md). This is model-granularity experimental residency, not finished adaptive texture streaming or a proven startup speedup.

Latest follow-up (2026-10-27): packaged reflection/diffuse dependency validation and no-build loading probes are delivered. Model identities are reused only for explicit immutable snapshots; shader/payload checks and strict editor/source paths remain. Reflection-key worker cost drops about 94%, avoiding 216.5 MiB of repeated reads, but total first-playable improvement is not demonstrated. 16 new image pairs, collision/fallback regressions and approximately-60-FPS host gates pass. [Current evidence and remaining startup work](LOADING_PACKAGE_GUIDE.md#lighting-dependency-follow-up). The earlier package results below remain historical, not this follow-up's claimed speedup.

Latest delivery (2026-10-27): user accepted the atmosphere/editor controls. Loading first pass adds opt-in process-to-playable reports and dependency-only immutable playtest snapshots; ordinary authoring stays strict-source. Run `LaunchLoadingTest.bat`; C# and normal launchers stay unchanged. Warm-filesystem median first playable improves 3137 -> 3049 ms (2.8%); model CPU 294 -> 209 ms, avoiding 102.5 MiB of model-preparation GLB reads. Frame-time gates pass near 60 FPS with one recorded stress hitch, not a locked minimum. [Workflow, evidence and limits](LOADING_PACKAGE_GUIDE.md). True cold-disk measurements, lighting dependency packaging and mip residency remain open.

## Approved Research Backlog (2026-10-26)

October 28 acceptance note: physics replays and final gameplay repeats pass, but
one six-room soak exceeded the private-memory guard. Preserve that failure and
investigate longer memory traces before promoting adaptive streaming; it is not
resolved merely because the unchanged repeat passes. See the physics guide above.

The user approved adding the following research-derived systems to the roadmap. These are staged work, not claims that all systems are implemented or authorization for an engine rewrite. The current C# runtime, stable entity IDs, RE:Dox persistence and ready-before-replacement contract remain mandatory.

| Order | System | First deliverable and acceptance | Reference / status |
| --- | --- | --- | --- |
| Implemented; user accepted controls/appearance, soak open | Atmosphere and post-processing | Optional HDR bloom, colour grading and distance/height fog; per-level editor settings, preserved disabled rendering/UI, GPU and host timing checks | [Implementation and stress caveat](ATMOSPHERE_GUIDE.md); Wicked/Lumix ordering principles |
| Model/lighting validation and isolated mip experiment implemented; rollout open | Loading diagnostics and packaged assets | Startup phase/frame reports, immutable snapshots, lighting identity reuse, strict fallback, no-build probes and budgeted mip gallery delivered; cold evidence, startup optimization, texture-granularity replacement and real-level acceptance remain | [Loading](LOADING_PACKAGE_GUIDE.md), [mip experiment and failed stress gates](TEXTURE_MIP_GUIDE.md); ezEngine, Lumix, Atom principles |
| Profile-led | Tiled/clustered lights | Compare spatial light lists against the existing 32-light list with many overlapping fixtures; bound overflow and include list-building cost | Godot, Wicked, ezEngine; shadow allocation is a separate budget |
| Profile-led | Door-state/occlusion visibility | Extend conservative room traversal with closed-door/simplified occluder tests; validate partial doors, fast turns, held objects and independent shadow views | RBDOOM/ezEngine; existing portal visibility is only the first pass |
| Profile-led | Persistent renderer data and jobs | Dirty-instance updates, explicit render ownership, then worker extraction/double buffering when measured useful | ezEngine, Atom, Wicked; frame records/batching are already implemented |
| Geometry/local-support reuse implemented; broader work profile-led | Physics improvements | Exact pose-keyed per-entity geometry reuse, support reuse and deterministic/live gameplay tests; broader contact/sleeping/debris work and mature-solver evaluation require demonstrated need | [Current physics evidence](PHYSICS_STARTUP_GUIDE.md); no automatic C++ migration |
| Ongoing | Diagnostics and gameplay QA | Structured level/entity/asset errors and repeatable inventory, overflow, door, gravity-gun, streaming and save/load routes | CAPCOM research takeaways; build our own tools, no assumed access to internal RE tools |
| Separate gameplay milestone | Spatial audio | Positional emitters/listeners, room obstruction and reverb authoring with profiling and gameplay tests | Additional horror-game priority, not an imported research implementation |
| Separate gameplay milestone | Enemy navigation/perception | Navigation authoring, door-aware paths, sight/hearing and debuggable encounter state | Additional gameplay roadmap; scope before implementation |
| Separate gameplay milestone | Particles and decals | Bounded dust/debris effects and persistent surface marks, pooling and quality limits | Additional presentation roadmap; do not conceal replacement/physics defects |

The atmosphere first pass and the subsequent opt-in loading-package first pass are delivered. No path tracing, volumetric shafts, native solver or full logical/physics room streaming is bundled into them. Original levels remain unchanged. User accepted the atmosphere controls/appearance; longer soak remains open, including the initial heavy-stress failure recorded in its guide. Subsequent work follows measured bottlenecks, not this table alone. Earlier milestone entries below are historical.

Latest rendering milestone (2026-10-26): gameplay frame extraction, private capture records, ordered binding reuse and compatible static instancing are implemented in C#. `LaunchRenderStressTest.bat` adds a separate 12-room fixture using existing assets. Larger-level draws decrease about 73% and measured render CPU about 23% in matched runs; approximately 60 FPS is retained. Existing baked six-room draw/CPU work is essentially unchanged, with fewer bindings and modest allocation reduction. Image references remain compatible. [Full test report, architecture limits and next work](RENDER_SUBMISSION_GUIDE.md). No C++ rewrite, fog/bloom, cold-load gain or locked-minimum-60 claim. Earlier milestones below are historical.

Latest milestone (2026-10-26): explicit room/doorway visibility and connected prefetch are implemented in separate `sixRoomVisibilityTest.json`. Shared F2/HS2Editor authoring includes through-wall outlines, stable IDs, undo/save; invalid graphs fail open. Main-view culling never unloads gameplay/collision or controls shadow casters. 347 CPU checks, 151 GPU reference views and six host runs pass. Side-room route main draws decrease about 56.5% beyond frustum culling, maintaining approximately 60 FPS; no memory/startup improvement or locked-60 claim. C# stays. [Test launcher, evidence and next work](ROOM_VISIBILITY_GUIDE.md). This supersedes older next-step notes below without declaring the flashlight visual concern resolved.

Flashlight placement follow-up (2026-10-26): replaced the large shoulder offset with 4cm right / 2cm camera-relative down and continuous wall retraction. Close-up/held-object, 20 camera-pose and wall-boundary checks pass; held world models still cast shadows. Environment coverage, illumination, C# runtime and lighting caches are unchanged. Same `LaunchSixRoomShadowTest.bat`; no recook required for this placement-only fix. [Validation and remaining limits](SHADOW_STABILITY_GUIDE.md#flashlight-mount-follow-up). Next remains manual/soak acceptance before broader streaming or atmosphere work.

Shadow acceptance correction (2026-10-26): the initial four-point pool still popped at distance. The same shadow-test level now uses 16 point slots for all 14 fixtures, packed into the same 104 MiB atlas; two independent spot maps retain 1024-square resolution. World-space bias, receiver-plane filtering and stable identities correct distance/contact errors. Full orbit/contact GPU references and normal-desktop stress/traversal/switch runs pass at about 60 FPS. C# and original levels remain unchanged. [Evidence, earlier failed diagnostics and limits](SHADOW_STABILITY_GUIDE.md). Next: manual/soak acceptance, alternate diffuse prefetch/cold-load instrumentation, room-aware visibility/prefetch, then atmosphere. Over-budget handoffs and soft area-light shadows remain open. Earlier entries are historical.

Lighting stability (2026-10-25): explicit fixed-light on/off scenarios, retained matching diffuse/reflection data, point-shadow hysteresis and a larger baked architecture scope are implemented in separate `sixRoomLightingStabilityTest.json`. Side coverage uses fourteen fixtures and real baked bounce with zero global ambient. Six resident reflections address the reproduced approach-dependent dark-floor artifact. [Results, editor workflow and bounds](LIGHTING_STABILITY_GUIDE.md). Next: cold first-state prewarming/instrumentation, longer soak/manual acceptance, broader visibility-aware residency, then budgeted atmosphere. Earlier entries below describe historical stages.

Fixture-only follow-up (2026-10-25): separate `sixRoomPointLightTest.json` and `LaunchSixRoomPointLightTest.bat` remove ambient/directional fill, replace invisible fill sources with eight visible point-light fixtures, and use freshly cooked room reflections. A reusable ceiling-light prefab is included. No renderer/runtime rewrite; four short full-host runs meet existing gates at about 60 FPS. [Authoring, shadow limits, results and remaining work](POINT_LIGHT_TEST_GUIDE.md).

Latest unit F follow-up (2026-10-25): bounded reflection residency and a controlled real-level copy are implemented. Run `LaunchSixRoomPbrTest.bat` for six authored captures / three slots, connected doorway prefetch, retained rooms and fence-safe reload. Worker preparation removes the measured room-jump hitches. Short full-host stress and streaming runs average about 60 FPS; originals and C# runtime remain unchanged. [Residency guide](REFLECTION_STREAMING_GUIDE.md). Next: manual/long traversal acceptance, switched-light capture scenarios, residency visual polish and cold first-playable instrumentation before wider rollout or post-processing. The following entries are historical stage reports.

For implementation stages, see [the upgrade plan](ENGINE_UPGRADE_PLAN.md). Updated 2026-10-25: A/B/C/D and unit E's invariant-light room selection/projection/blending first pass are implemented. C# remains the runtime and original levels stay Legacy. New `LaunchRoomTransitionTest.bat` opens an isolated automatic two-room fixture; older synthetic/manual capture fixtures remain. Local torch-on/off host runs average about 60 FPS; two ready captures plus bindings/fallback use 2.126 MiB. Next is unit F residency/prefetch, packaging and one-level rollout; explicit switched presets also remain pending. Workflow, measurements and manual/cold-start limits: [transition guide](ROOM_TRANSITION_GUIDE.md), [capture guide](ROOM_REFLECTION_GUIDE.md), [PBR guide](PBR_MATERIAL_GUIDE.md). Historical entries below predate this update.

Updated: 2026-10-22. Scope: loading, memory, horror lighting, physics and native/managed architecture. This document separates implemented foundations from remaining work. The earlier inventory visibility repair in [PERSISTENCE_AND_INVENTORY.md](PERSISTENCE_AND_INVENTORY.md) is preserved.

## Recommendation

Implementation authorized and first pass delivered on 2026-10-06: compressed texture cooking and shared GPU resources, controllable/shadowed lighting, and measured physics improvements. C# gameplay/editor code, source GLBs, authored layouts and inventory behaviour remain in place. No C++ rewrite or physics-solver migration was made.

Keep C# gameplay, editor, interactions, inventory and RE:Dox persistence. Improve the asset pipeline and measure CPU/GPU work before choosing any C++ migration. A native core is possible, but moving the same expensive work to another language is not a substitute for caching, streaming, efficient collision algorithms or appropriate GPU formats.

For this horror game, prioritize predictable frame times, controlled light and shadow, reliable doors/physics, and assets ready before the player can see them. Do not start with a full engine rewrite or ray tracing.

## Approved Performance Baseline (2026-10-06)

The smoother flashlight playtest is user-confirmed and is now the default baseline, not an optional test-only optimization:

- Keep main-scene and shadow object uploads batched in production. Individual-update paths exist only for regression/reference measurements.
- Gameplay batch launchers build/run Release, including the separate emission test. HS2Editor Play Selected Level also launches Release, preserving its save-then-play selected-level workflow. Visual Studio retains Debug for debugging; choose Release there when assessing playtest performance. Editor/importer app launch configurations are otherwise unchanged.
- Preserve the current textured materials, mesh/collider alignment, shadow resolution/filtering, movement settings and ready-before-replacement rule. The flashlight test keeps 0.025 ambient fill and visible red/green indicators; other levels keep their authored lighting.
- Changes to rendering, lighting, loading or physics must rerun relevant image/physics checks and full-host moving flashlight on/off comparisons. Aim for the 16.7 ms total frame budget; record mean/P95, configuration, resolution and presentation conditions. The measured local 60 FPS baseline is not a universal hardware or full-level guarantee.
- Include doors, gravity-held objects, crate debris and inventory transitions in regression stress routes. The first combined route is now covered; a render-only benchmark cannot certify gameplay responsiveness.

## Next Implementation Order

### Latest Delivery: Light Probes And Preview Views

Follow-up delivered 2026-10-22: opt-in regular-grid directional probes give movable textured models/debris/weapons fixed-light bounce, with wall-visibility rejection and cached stationary sampling. Shared editor controls now offer Full, Direct Only, Indirect Only, probe preview, markers and validity. Version-2 bake files remain compatible with version-1 static bakes. `LaunchLightProbeTest.bat` opens a separate 196-probe test; the prior room and all launch selections are preserved. The existing 256-byte object upload stride and one-upload batching remain unchanged. Full-host walking and 120-fragment stress tests averaged approximately 60 FPS locally, with no >33.3 ms frames in the latest pairs; P95 was 18.1-18.6 ms, so this is not a locked-60 guarantee. Probe CPU mean ranged from 0.014 ms walking to 0.50 ms under debris load. See the validation report for all timings, first-run variability and limits.

### Prior Delivery: Static Indirect Lighting

First pass delivered 2026-10-22: opt-in static architecture, one fixed-light diffuse bounce, editor bake/progress/cancel/preview/stale controls, separate padded per-triangle UVs, validated linear-HDR/LZ4 bake files and `LaunchIndirectLightingTest.bat`. Existing levels/source model UVs and realtime switch/torch behaviour remain unchanged. Eligible opt-in static objects now retain the authored quaternion for matching render/collision poses; the legacy unmarked path is preserved. The 17-instance room uses 8.19 MiB additional GPU resources. See [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md) and the validation report for limits/results. Switchable bake scenarios, bloom and multi-room atlas/probe streaming remain later milestones.

1. **Main-view visibility culling and expanded stress tests: implemented.** Conservative transformed primitive/model-part bounds reject offscreen draws, including rotated/non-uniform/negative scales. Shadow casters remain independent; weapon/UI rendering is protected. Game/F2 and moving-door image references pass. Full-host stress coverage includes shutters, six broken crates, 120 peak fragments, gravity holding and inventory transitions. This is frustum culling, not behind-wall occlusion.
2. **Room dependency manifests, preloading and visual streaming: first pass implemented.** Automatic XZ zones prefetch neighbouring shared model/debris/replacement dependencies. Two preparation workers, budgeted GPU uploads, soft byte budgets, retention and fence-safe eviction/reload are active. Gameplay entities/colliders remain resident, preserving IDs, inventory, pickups, puzzles, doors, light overrides and held objects. Toolbar > Asset Streaming saves per-level settings. This does not yet unload logical rooms/physics or use door portals. True cold/warm first-playable comparisons remain outstanding; see [STREAMING_GUIDE.md](STREAMING_GUIDE.md).
3. **Cooked collision acceleration: first pass implemented 2026-10-21.** Static mesh placements load validated lossless LZ4 triangle/BVH caches, keyed by source content, world pose/size, skin and build/version identity. Missing/invalid/unwritable caches use original construction; moving mesh-door poses bypass disk caching. All five six-room static mesh colliders match source construction exactly. Median collision preparation measured 31.020 ms source versus 13.939 ms cached, while host/game initialization changed only from 1067.538 to 1054.602 ms. See [COLLISION_CACHE_GUIDE.md](COLLISION_CACHE_GUIDE.md); this is not full first-playable loading or a physics rewrite.
4. **Horror-lighting polish: emission, static indirect and probe first passes implemented 2026-10-22.** Embedded GLB emission maps/factors/strength survive version-2 model cooking and render in HDR. Shared Inspector controls edit per-instance intensity, optional part/colour overrides and stable-ID light links. Switches and flicker affect both visible emission and illumination; prefab instances remap links. `LaunchSixRoomEmissionTest.bat` opens a separate switched-screen test. The one-room static/probe bakes and comparison controls are described above. Next: explicit switchable-light scenarios, larger-area budgets and longer stress testing. Bloom/volumetrics remain pending.

Items 1-3 and the emission/static-indirect/probe portions of item 4 are delivered within the scope above. Historical and latest October 22 measurements are retained separately in the validation report, including earlier spikes and streaming waits. The six-room route still needs all 32 shared models; its residency is not reduced. A forced distant-area fixture verifies actual eviction/reload separately. A C++ rewrite or physics-solver replacement is not currently justified by these measurements or the user's positive PC playtest.

## Implemented Foundations

1. Assets/loading: BC7 colour/material maps, BC5 normals, full mip chains, LZ4 disk mip payloads, source-hash/version/checksum validation, atomic cache writes, shared CPU preparation/GPU texture ownership and worker-side image preparation. Game/editor submit model parts under a soft 2 ms upload budget. Queued object replacements retain their original visuals/colliders until ready; failures retain the original. The importer has a Compression tab and the root has `CookAssets.bat`. All 376 current GLBs were cooked without errors; 168 unique texture caches were created.
2. Lighting: linear/sRGB handling, RGBA16F HDR world buffers, exposure/tone mapping, normal maps, spotlights, budgeted spot/point shadow maps and a gameplay F-key flashlight. Level-wide lighting controls and per-light enabled/shadow/cone controls are editable and saved. Existing lights remain unshadowed unless opted in. Game/editor use the same material and light settings.
3. Physics: per-mesh BVH acceleration for contacts and rays, deterministic candidate order, reusable candidate buffers and bounded-profile query counters. The existing C# solver and authored collider transforms are unchanged. Randomized comparisons, collider-bound checks and level traversal/settling checks guard correctness.

These foundations now support visual streaming and the opt-in one-room indirect bake, not full logical/physics room streaming, general dynamic GI or a new physics engine. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for reproducible measurements, GPU captures and test limitations, [Engine.Render](../Engine.Render/README.md) for runtime details, and [Engine.AssetImporter](../Engine.AssetImporter/README.md) for cooking instructions.

## Lighting Follow-Up Completed

The next milestone adds editor-authored light groups, reusable `LightSwitch` interactions with optional puzzle prerequisites, explicit ID targets, per-light flicker and saved enabled-state overrides. Prefab placement/apply remaps internal light IDs; revert keeps existing IDs. Per-face conservative shadow culling and unchanged-depth caching are enabled in both game and editor. Moving doors/lights and hidden crate parts invalidate affected maps.

See [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md) for authoring. Regression coverage includes production save loading, prefab links, GPU image comparisons, six-room camera samples and a timestamped synthetic shadow benchmark. These complete the switch/state/cache items below, not baked GI, streaming or representative end-to-end profiling.

Moving-flashlight playtest follow-up: `sixRoomFlashlightTest.json` now has faint 0.025 ambient fill, with scene/directional lights still off and red/green puzzle indicators preserved. Profiling found hundreds of per-caster driver updates on moving shadow redraws. A retained padded buffer now uploads them in one batch, preserving shadow quality and movement speed. Paired real-level camera-route measurements and old/new image checks are documented in the validation report; this is not yet a complete gameplay/debris/input-latency profile.

Full-host follow-up found the remaining main-scene per-object upload bottleneck and Debug physics catch-up cost. Game/F2 and standalone editor now batch main object uniforms too (762 updates to one in the flashlight test), retaining transforms/materials and a bounded 1 MiB staging array. The flashlight test launcher builds/runs Release. Under normal desktop VSync, scripted walking/turning measured 60 FPS with the light on and off, versus 41.8 FPS for the Release individual-upload reference; movement distance was effectively unchanged. These are short local gameplay samples, not a heavy-debris/full-level guarantee. Reproduction, earlier timing variability and the timestep-clamp finding are documented in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

## Cooked Model Milestone Completed

The next loading milestone is implemented: versioned, source-hash/checksum-validated, lossless LZ4 caches for the existing GLB loader's geometry/material output and named part identities. These reference shared cooked textures. `CookAssets.bat` and the importer's Compression tab now cook both. All 376 current GLBs passed source/cache comparisons, including corruption/staleness and texture-dependency fallback/repair checks. Source art, level transforms and gameplay state are unchanged.

Warm serial CPU preparation of 28 six-room models measured a median 286.22 ms using GLB parsing versus 187.20 ms cached, but harness start-to-complete-GPU-readback improved only from 1,577.65 ms to 1,555.51 ms. Source reads/hashing, GPU upload and other startup work remain. These are separate measurements, not a claim of 35% faster first-playable startup. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

The subsequent visual-streaming milestone implements dependency manifests and staged prefetch/residency with persistent IDs and ready-before-replacement guarantees; it is separate from model cooking. Static placement collision/BVH caching followed on October 21. Full logical room streaming and true cold-start profiles remain pending. Model cooking itself adds no animation system.

## Current Source Audit

| Area | Current implementation | Consequence |
| --- | --- | --- |
| Structured data | `Engine.Core/Serialisation/StructuredData.cs` uses RE:Dox and validated DOX level caches; authored JSON remains authoritative. | Helps data decoding, not image decoding, uploads or physics construction. Inventory reads live memory when opened. |
| Models | `CookedModelCache` reads/hashes the source GLB and loads a validated lossless LZ4 snapshot of imported geometry/materials, or falls back to `GlbModelLoader`. Worker loading and resident path-based model caching remain. | Skips parsing while preserving exact geometry/part IDs. Source I/O and GPU uploads remain; collision construction has its own cache. |
| Textures | `TextureCooker.LoadForRendering` resolves model-cache texture dependencies or prepares source images on fallback. `ModelTextureCache` owns shared reference-counted GPU resources. | Avoids repeated texture resources and runtime image decoding for cooked content. Source GLBs are still read/hashed; residency remains model-level, not per-mip. |
| Visibility/residency | Main-view bounds culling, automatic XZ dependency zones, neighbour prefetch and fence-safe unused-model eviction. | Reduces offscreen draws and can release distant render assets. Logical state/colliders stay resident; visible/near/shadow/held/debris owners override the soft budget. |
| Texture format | BC7 colour/material and BC5 normals with full mip chains; RGBA fallback for missing caches/unsupported formats. | Reduces GPU payload and distant shimmer. Extra cooked files can increase total deployment size while source GLBs are also shipped. |
| Lighting | HDR, normal maps, linked emissive fixtures, opt-in one-bounce static lightmaps/directional probes, comparison views, 32 point/spot lights, two spot plus one point shadow budget, culling/caching, groups/switches/flicker and saved enabled overrides. | Supports controllable horror lighting. Switchable bake scenarios, larger/multi-room bakes, directional shadows, bloom and volumetrics remain pending. |
| Physics | Custom C# solver with collider bounds/support pruning, per-mesh BVH queries and validated static-placement collision caches. | Reduces triangle search and repeat startup construction without changing shapes/solver behaviour. Native/mature solver evaluation remains conditional on measured bottlenecks. |
| Native boundary | RmlUi already runs behind `Native/HS2RmlUiBridge` with a C ABI consumed from C#. | The project already has an example of a managed/native split. |

Historical asset-ready startup was around six seconds in the October 2 test; faster serialization did not produce a demonstrated total-startup win. See [REDOX_EVALUATION.md](REDOX_EVALUATION.md). New warm asset-stage and frame captures are in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md); they are not cold-start measurements or a promise about a 100-times-larger level.

## Compression Choices

Compression has different jobs: reducing disk reads, reducing CPU decoding work, and reducing GPU memory/bandwidth. One algorithm does not cover all three.

| Data | Candidate | Intended use and trade-off |
| --- | --- | --- |
| PC colour textures | BC7 with offline-generated mips | Implemented for colour/material images, with GPU format checks and fallback. |
| Normal maps | BC5 | Implemented with two stored channels, reconstructed Z and matching shader support. |
| HDR environment/light textures | BC6H | A future HDR asset option, not a fix for the current LDR shader. |
| Cross-platform textures | KTX2/Basis Universal | Portable storage with transcoding to supported GPU formats. Transcoding has a cost; direct cooked PC formats are simpler for the present Windows target. |
| Meshes | meshoptimizer | Optimize vertex/index access and compress buffers; add compatible loader support before exporting compressed assets. Preserve named crate parts, material assignments and collision alignment. |
| Asset-pack chunks | Zstandard or LZ4 | LZ4 implemented per cooked texture mip and imported model payload. General asset packs, optimized mesh buffers and Zstandard comparison are not implemented. |
| Later Windows I/O work | DirectStorage / GDeflate | Consider after measuring I/O/decompression bottlenecks. GPU decompression requires appropriate graphics/backend integration; it is not a drop-in C# speed switch. |

A 4096x4096 RGBA8 texture occupies 64 MiB for its base level; BC7 occupies 16 MiB. A full mip chain adds approximately one third to either total. This is an illustrative storage calculation, not a measured loading-time improvement or a promise of four-times-faster gameplay.

References: [Microsoft BC formats](https://learn.microsoft.com/en-us/windows/desktop/direct3d11/texture-block-compression-in-direct3d-11), [Khronos KTX](https://www.khronos.org/ktx/), [meshoptimizer](https://github.com/zeux/meshoptimizer), [Zstandard](https://github.com/facebook/zstd), [LZ4](https://github.com/lz4/lz4), [Microsoft DirectStorage](https://github.com/microsoft/DirectStorage).

## Remaining Work And Direction

The numbered stages below remain the longer-term plan. Texture/model cooking, texture sharing, budgeted uploads, initial shadowed HDR lighting and mesh-query acceleration are implemented as described above; broader acceptance gates are not all complete.

### 1. Measure A Repeatable Baseline

Record cold and warm startup separately, including file reads, GLB parsing, image decoding, mesh/collider construction, upload submission, GPU completion and first fully rendered playable frame. Capture CPU allocations, RAM/VRAM, frame-time percentiles and stalls while opening doors, inventory and producing debris. Use the same Release build, route, resolution and machine settings. Report real cold-cache methodology rather than calling the first loop iteration cold.

Preserve a small visual/physics reference level alongside `sixRoomTest.json`. Include textured doorframes, moving doors, breakable crates and gravity-gun pickups. A 60 FPS goal means a 16.7 ms total frame budget, not 16.7 ms available independently to every subsystem.

### 2. Cook Assets Before Play

Implemented: versioned GPU-ready texture mips, lossless imported-model buffers/materials with texture dependency keys, static-placement collision/BVH caches, and derived spatial-zone dependency manifests. Remaining: vertex/index optimization, shared local-space collision instances and packaged/portal-aware dependency metadata. Keep source GLB/FBX files and editable level JSON. Hash source content plus import settings/tool version, rebuild changed or invalid assets and retain the source compatibility path. A model-loader semantics change must bump the model cache version.

Use sRGB formats for colour textures and linear formats for material data. Test colour-space changes against editor and game reference captures. Preserve node/part identities, transform conventions and authored collision shapes; compression must not reopen the previous wall/door alignment regressions.

### 3. Share Resources And Bound Upload Work

Introduce image/material caching independent of the model cache, with explicit ownership and lifetime management. Separate worker-side reading/decompression/decoding from bounded render-thread upload batches. Keep frequently used UI fonts, thumbnails and weapon resources resident. Generate UI thumbnails during import rather than loading full world models to open inventory.

Represent readiness explicitly: requested, CPU-ready, GPU-ready and failed. Never destroy or hide an existing object until its replacement's required rendering resources are ready. If preparation fails, retain the current valid object and report the error. This preserves the established crate/replacement rule.

### 4. Stream Rooms And Dependencies

The visual-resource first pass is implemented as documented in [STREAMING_GUIDE.md](STREAMING_GUIDE.md). The following requirements continue to apply to future full logical/physics room streaming and portal-aware prefetch; those extensions are not implemented yet.

Use room/zone-sized chunks with a shared asset pool. Prefetch adjacent rooms before the player opens their doors; retain a small neighbouring set to avoid repeated load/unload stalls. Budget residency by bytes and dependencies, not just object count. Visibility culling and streaming are separate concerns: an invisible room may still need collision, puzzle state, sound or preloaded assets.

Keep object IDs and persistent gameplay deltas independent of whether the room is resident. Save unlocked doors, solved circuits and collected items without requiring every mesh to stay loaded. Unloading a room must not reset puzzles, duplicate pickups or drop a gravity-held object unexpectedly.

### 5. Build Horror Lighting In Stages

Establish correct colour spaces, HDR render targets, tone mapping and controlled exposure first. Then implement shadowed spotlights for flashlights and focused fixtures, followed by a budgeted set of shadowed point lights. Check that walls block light and moving doors cast/update shadows. Add normal maps so the existing masonry/metal assets read properly under grazing light.

Completed: light groups, repeatable switches, authored flicker, saved enabled states and shadow caching/frustum culling. Keep editor preview and runtime lighting consistent while separating authored defaults from gameplay overrides.

The static indirect and probe first passes now combine one baked bounce with selective realtime lights and movable-object sampling. Next validate larger areas and design explicit switchable-light scenarios. Do not bake a movable door's closed shadow into a permanent wall/floor texture or probe occluder set. Volumetrics and more advanced reflections can follow after these foundations meet frame budgets. Main-camera frustum culling and combined gameplay/debris CPU profiles are complete; full-game GPU timestamps and longer stress/soak profiles remain.

### 6. Profile Physics Before Choosing A Replacement

Measure broad-phase candidate counts, triangle tests, solver work, sleeping bodies and debris bursts. Keep static architecture separate from dynamic objects, precompute mesh acceleration structures and avoid rebuilding static shapes during normal play. Confirm gravity-gun holding, character movement and hinged-door contacts remain stable.

If a mature solver is warranted, compare [BEPUphysics v2](https://github.com/bepu/bepuphysics2) (C#) and [Jolt](https://github.com/jrouwe/JoltPhysics) (C++) on the same scenes. Benchmark total integration cost and gameplay correctness, not only isolated solver throughput. Existing character/gravity-gun behaviour and serialization require explicit adapters and migration tests either way.

## C# And C++ Boundary

October 21 decision: retain the working C#/.NET 10 runtime and implement measured improvements first. C++ services remain a future option, not an authorized full rewrite. Both a native core and C# gameplay would execute at runtime; this is a responsibility/ownership split, not a distinction between runtime and non-runtime code.

Capcom's [2023 C# architecture presentation](https://docswell.com/s/CAPCOM_RandD/KW1NQ1-RE2023) describes C# gameplay on its own REVM, custom collection and ahead-of-time tooling. That is a much larger toolchain than adding a C++ DLL beneath stock .NET. This project already has a native/C# boundary for RmlUi, but does not implement REVM or equivalent C#-to-C++ tooling.

Any future native service needs a measured workload, a small versioned C ABI, explicit thread/lifetime ownership, batched calls, compatible data layouts, error propagation, cancellation, managed/native profiling and reference correctness tests. Prefer `LibraryImport` and `SafeHandle` for suitable .NET bindings, following [Microsoft's interoperability guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices). C++ alone does not improve GPU shader cost, make lighting more realistic or remove asset I/O. Preserve C# level/editor/interaction/inventory/RE:Dox contracts.

A suitable eventual split would keep gameplay, editor tools, content authoring and persistence in C#, with only measured heavy services in C++ where justified: physics, asset decoding or specialized render/backend work. C# can also drive native compression/physics libraries without migrating the rest of the engine. C is useful as a stable interoperability interface; it does not require writing the implementation itself in C.

Use a small versioned C ABI, opaque handles and batched arrays/commands. Specify ownership, disposal, threading and error reporting. Avoid thousands of calls per object per frame, shared mutable managed/native state, or exceptions crossing the boundary. Prefer generated interop and safe handle ownership where appropriate. [Microsoft native interoperability guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices).

Keep RE:Dox in the managed data layer even if native services are added. GPU shaders already run on the GPU; moving their C# orchestration to C++ does not automatically speed the shader. Address excessive allocation, duplicate work and poor data access in the existing code before committing to a rewrite.

## Acceptance Gates

- Inventory shade/grids remain visible and interactive in native and fallback rendering; no file read on every open.
- Authored transforms, hierarchy IDs, named destruction pieces and visible mesh/collider alignment remain unchanged.
- Save/restore survives room streaming without lost or duplicated pickups and puzzle state.
- Asset replacement remains ready-before-destroy, including preparation failure.
- Editor and game show matching materials and lighting; compression artifacts are checked close up and at a distance.
- Loading-time and frame-time improvements are measured separately. Smaller files, faster JSON reads and fewer allocations alone are not proof of faster first playable frames.

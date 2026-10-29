**HL2StyleEngine: step-by-step upgrade plan**

Current physics follow-up (2026-10-28): bounded per-entity collider geometry reuse and local support-query reuse are implemented without changing solver settings, timing policy, visuals or C#. All 1,200 pre-change replay frames match exactly; 54,497 focused geometry/contact checks pass. Same-binary warm-cache startup median improves 3189.3 -> 2900.7 ms, with lower early physics work. [Current acceptance, commands and limits](PHYSICS_STARTUP_GUIDE.md). Next: manual acceptance/cold candidates, then texture-granularity ownership and room-aware mip demand in a separately gated level trial; spatial audio follows. Older entries below retain their original findings.

Current stability/startup pass (2026-10-28): a repeatable real-game save/load/reload runner, isolated QA saves, native/fallback routes and a separate puzzle-door route are implemented. Hidden save menus no longer deserialize save slots every frame, and save serialization contracts prepare during loading without touching disk/live state. Per-frame startup host timings identify substantial first physics-settling work; no startup speedup, physics-policy change or adaptive-mip rollout is claimed. [Current tests, measurements and limits](GAMEPLAY_STABILITY_GUIDE.md). Next: measured first-physics optimization and longer acceptance, then texture-granularity/room-aware mip integration. Older entries below remain historical.

Current inventory follow-up (2026-10-27): unused fallback RML work is removed, text-image metadata is cached, and bounded icons/current-item titles/native renderer resources prepare during existing loading frames. The populated native first-open failure (204.880 ms) falls to 26.293/17.668 ms in repeats; seven source/package stress runs pass near 60 FPS, and native/fallback rendering/input/persistence regressions pass. No visual, C++ runtime, default UI backend or save-format changes. Next: longer real-game/save-load acceptance and re-measured startup critical path, then texture-granularity/room-aware mip integration. [Current evidence and limits](INVENTORY_PERFORMANCE_GUIDE.md). Earlier status entries below are historical, including their unresolved first-open reports.

Current stability follow-up (2026-10-27): per-frame stage/allocation/GC reports and fresh-process source/package comparisons identify crate-break and first-inventory work. Support bounds are now calculated once per broken crate; bounded item-icon prewarming uses existing loading frames. Six torch-on samples show median break-frame Update **21.98 -> 15.23 ms**; all seven post-change trials pass unchanged near-60-FPS gates, with one 34.195 ms hitch retained. Inventory first-open work and total-startup improvement remain unresolved. Mip integration stays gated; C#, full texture quality and lighting are unchanged. [Implementation, tests and next work](HITCH_STABILITY_GUIDE.md). The entries below are historical stages, not newer acceptance claims.

Latest first pass (2026-10-27): startup frame/phase diagnostics and an isolated texture-mip residency gallery are implemented in C#. `LaunchTextureMipTest.bat` tests budget selection, ready-before-replacement and full-detail restoration; normal Game/F2/HS2Editor remain full resolution. CPU/GPU integrity checks pass and the initial 3,600-sample gallery run averages about 60 FPS. Full-game torch-on destruction stress fails the hitch gate twice; rollout is **not accepted**, and no total-startup speedup is claimed. [Current scope, results and next actions](TEXTURE_MIP_GUIDE.md). Next: investigate those hitches and startup phases, then texture-granularity residency/real-level validation. Spatial audio and broader stability work remain separate milestones; older entries below are historical.

Latest loading follow-up (2026-10-27): reflection/diffuse validation reuses declared immutable model identities without changing lighting keys or payload validation. Strict authoring, shader hashing and collision source hashing remain. New timing separates lighting preparation/validation/upload work; `PrepareLoadingProbe.bat` and `RunLoadingProbe.bat` enable a no-build post-restart candidate, without automated restart/cache flushing. Reflection-key worker time falls 316 -> 18 ms and repeated source reads fall about 216.5 MiB, but total startup improvement is **not demonstrated** (3058 -> 3156 ms median in this sample). Visual/physics/fallback checks and near-60-FPS gates pass, with one recorded stress hitch. [Full current results](LOADING_PACKAGE_GUIDE.md#lighting-dependency-follow-up). Next: measured startup critical path, manual cold candidates, then bounded mip residency; previous dated results below are historical.

Loading follow-up implemented (2026-10-27), after atmosphere acceptance: opt-in first-playable diagnostics and dependency-only immutable playtest snapshots, with strict authoring validation and invalid-cache fallback preserved. `LaunchLoadingTest.bat` tests the current atmosphere level without changing normal launchers. Warm-filesystem median first playable 3137 -> 3049 ms; 56 package checks, the 388-model suite, GPU/legacy references and approximately-60-FPS gameplay gates pass. One stress hitch remains recorded. [Results, scope and next measurements](LOADING_PACKAGE_GUIDE.md). Next: cold-start evidence, lighting dependency validation/upload profiling, then a bounded mip-residency experiment. C# stays; older entries below are historical.

Step 6 first pass implemented (2026-10-26): optional HDR bloom, bounded colour grading and analytic distance/height fog, with shared Game/F2/HS2Editor settings, undo/save and unchanged disabled rendering. `LaunchAtmosphereTest.bat` opens a separate twelve-room copy; original levels and bake/reflection energy keys remain unchanged. 24 data checks, 55 GPU checks and all 15 historical images pass. Ordinary host runs remain approximately 60 FPS; added postprocess GPU work is about 0.35-0.42ms in the isolated test. One initial heavy stress run failed the hitch gate; a diagnostic repeat passed without renderer changes or relaxed limits, so longer acceptance remains open. [Workflow, results and limitations](ATMOSPHERE_GUIDE.md). Remaining research systems are in the [approved backlog](ENGINE_TECHNOLOGY_ROADMAP.md#approved-research-backlog-2026-10-26); next recommendation is first-playable loading instrumentation and packaged dependency metadata, not a C++ rewrite. Older entries below are historical.

Step 8 first pass implemented (2026-10-26), following user acceptance of room visibility: sealed per-frame model records shared by gameplay main/shadow views, private capture scenes, consecutive binding reuse and compatible static instancing. Original ordered submission remains an A/B reference. `LaunchRenderStressTest.bat` opens a separate 12-room/4,800 m2 fixture. 151 existing-level and 96 larger-level image comparisons pass; matched larger-level hardware draws fall from 242 to 64.5/frame and render CPU from 4.98 to 3.82ms, near 60 FPS. Existing baked-scene CPU/draw counts are effectively unchanged. C#, original levels/pixel shaders and physics remain intact. [Results, reproduction and explicit limits](RENDER_SUBMISSION_GUIDE.md). Persistent/worker render scenes and cold first-playable remain open; opt-in fog/bloom/grading follow manual acceptance rather than being included here.

Room visibility first pass delivered (2026-10-26): step 7 brought ahead of atmosphere. `LaunchSixRoomVisibilityTest.bat` opens an isolated six-room copy with explicit room/aperture authoring, conservative main-view traversal and connected model/bake-binding/reflection prefetch. Logical state, colliders, shadow casters and held props stay independent. 347 CPU checks, 151 all-visible GPU comparisons and six full-host runs pass. Side-room route draws fall from 188.3 to 81.9/frame beyond existing frustum culling, with approximately 60 FPS; central-hall work, memory and startup are not claimed faster. [Workflow, measurements and remaining limits](ROOM_VISIBILITY_GUIDE.md). The user chose to move on from the flashlight appearance issue; this milestone does not mark that concern resolved. Earlier dated entries below are historical.

Flashlight mount follow-up (2026-10-26): the accepted environment-shadow fix is preserved. The shadow-test flashlight now sits 4cm right / 2cm camera-relative down instead of the exaggerated shoulder offset, with continuous 5mm wall clearance. Held props still cast shadows and illumination settings are unchanged. Actual-game close-up/held, camera-basis and wall-sweep checks pass. No shader/cache/geometry changes or recook are needed. [Current validation and playtest](SHADOW_STABILITY_GUIDE.md#flashlight-mount-follow-up). Manual visual acceptance remains ahead of the next feature milestone; older entries below describe their original stages.

Shadow acceptance correction (2026-10-26): the initial four-slot pool below still popped at distance. The same shadow-test level now covers all 14 fixtures using a 16-point packed pool at the same 104 MiB, plus full-resolution flashlight maps. World-space bias/receiver-plane filtering correct distant contact distortion. CPU, GPU ray-reference, real-crate orbit/approach and normal-desktop host performance checks pass; [current evidence and limits](SHADOW_STABILITY_GUIDE.md#correction-validation). Keep correctness and manual acceptance ahead of new atmosphere features. This is not a soft-area-shadow implementation or unrestricted light budget.

Shadow follow-up (2026-10-26): the next controlled milestone is implemented in separate `sixRoomShadowTest.json` / `LaunchSixRoomShadowTest.bat`: four stable cached point-shadow slots, two separate spotlight slots, wall-safe shoulder flashlight and background preparation of the alternate reflection state. Old levels/shaders remain available. GPU crate/torch, switch/undo, packaged-cache and frame-time checks pass. [Implementation, measurements and remaining limits](SHADOW_STABILITY_GUIDE.md). This is not completion of the whole plan: alternate diffuse-bake prefetch, cold first-playable instrumentation, longer soak/manual acceptance, visibility-aware room loading and atmosphere remain open. The entries below record earlier milestones.

Lighting stability follow-up (2026-10-25): implemented a separate fourteen-fixture six-room test with fixture-derived bounce/probes, explicit on/off reflection/bake scenarios, bounded retained-state resources and point-shadow hysteresis. GPU A/B isolated reflection availability in the reported side-floor issue; this small level keeps all six captures resident. Existing levels/shaders remain unchanged. [Workflow, validation and performance limits](LIGHTING_STABILITY_GUIDE.md). The first explicit switched-preset milestone is now delivered; broader visibility-aware streaming, first-state prewarming, cold-load instrumentation and soak/manual acceptance remain before further rollout.

Fixture-only playtest (2026-10-25): `LaunchSixRoomPointLightTest.bat` opens an isolated eight-fixture version of the six-room PBR level, with ambient/directional fill removed and reflections recooked. Reusable ceiling-light prefab added; renderer/shaders and accepted original levels remain unchanged. Four short full-host runs average about 60 FPS and pass existing gates. This is lighting authoring/validation, not completion of switched presets or atmosphere work. [Workflow and results](POINT_LIGHT_TEST_GUIDE.md).

Prepared: 22 October 2026. Updated: 25 October 2026. Status: units A-E first passes and unit F's bounded residency/controlled six-room rollout are implemented, with explicit switched states in the follow-up above. C# remains the runtime. `LaunchSixRoomPbrTest.bat` retains the earlier separate six-room PBR copy with six cooked captures, three resident slots, doorway prefetch, retention and fenced unload/reload. Existing levels/startup defaults are unchanged. [Residency workflow, measurements and limits](REFLECTION_STREAMING_GUIDE.md). Historical A/B: [PBR_MATERIAL_GUIDE.md](PBR_MATERIAL_GUIDE.md); C: [REFLECTION_LIGHTING_GUIDE.md](REFLECTION_LIGHTING_GUIDE.md); D: [ROOM_REFLECTION_GUIDE.md](ROOM_REFLECTION_GUIDE.md); E: [ROOM_TRANSITION_GUIDE.md](ROOM_TRANSITION_GUIDE.md). Broader visibility-aware residency, first-state prewarming, cold first-playable instrumentation, long soak/manual acceptance and steps 6-9 remain open. This is not completion of all shipping gates.

Goal: improve atmospheric indoor visuals inspired by Half-Life 2, BioShock and Resident Evil Requiem, starting with PBR lighting and room reflections. Retain the .NET 10 C# runtime, gameplay lifecycle, editor, interactions, inventory and persistence.

2026-10-23 acceptance follow-up: the test-room crate's imported material was effectively polished metal (roughness 0, omitted metallic defaults to 1). The fixture now uses a material-only dielectric wood variant; the original asset/levels are preserved. Actual crate flashlight and six-face GPU checks pass, and torch on/off host routes still average 60 FPS. See the [material correction](PBR_MATERIAL_GUIDE.md#crate-material-correction-2026-10-23) and [validation report](ENGINE_UPGRADE_VALIDATION.md). Review authored material factors when migrating additional assets; do not change valid glTF defaults. This fixes unit C acceptance feedback, not implementation of unit D.

Background: [open-source engine research](OPEN_SOURCE_ENGINE_RESEARCH.md). Existing baseline and historical delivery notes: [technology roadmap](ENGINE_TECHNOLOGY_ROADMAP.md) and [validation report](ENGINE_UPGRADE_VALIDATION.md).

**Architecture and delivery rules**

C# continues to own simulation, scripts, entities, level data, editor tools, asset scheduling and rendering orchestration. HLSL performs material/light evaluation on the GPU. Unit C's small synthetic environment is filtered deterministically by an offline C# cooker; GPU filtering can be evaluated for later room-capture tools, not added to gameplay frames. The first graphics release uses the current Veldrid/Direct3D 11 backend. A later native subsystem is conditional on profiling.

Keep [IGameModule](../Engine.Runtime/Hosting/IGameModule.cs) and the Update/FixedUpdate lifecycle. Rendering can gradually gain a cleaner boundary without changing how gameplay is authored.

Deliver the work in small reviewable stages with a running fixture at each stage. Use a proposed explicit per-level lighting-model setting whose missing value preserves the legacy path. Develop PBR in a separate fixture, then migrate one real level intentionally. A missing setting must not silently reinterpret all existing level lighting. Use additive level fields and established JSON/RE:Dox compatibility.

The first shippable graphics milestone comprises steps 0-5. Steps 6-9 extend it after acceptance.

| Step | Deliverable | Depends on |
| --- | --- | --- |
| 0 | Reproducible visual/performance baseline and PBR fixture | Current engine |
| 1 | Correct material inputs and PBR direct lighting | 0 |
| 2 | Prefiltered HDR environment specular | 1 |
| 3 | Authored, baked room reflection probes | 2 |
| 4 | Room transitions, switch compatibility and reflection residency | 3 |
| 5 | Game/editor integration and controlled rollout | 1-4 |
| 6 | Bloom, grading and budgeted atmosphere | 5 |
| 7 | Room/portal visibility and improved prefetch | 5 |
| 8 | Render extraction and draw grouping | 5; coordinate with 7 |
| 9 | Profile-led lighting, loading, jobs and native evaluation | Evidence from prior stages |

**Pre-implementation source findings**

- [TexturedModel.hlsl](../Engine.Render/Shaders/TexturedModel.hlsl) currently has power-based directional specular and diffuse-only point/spot lighting.
- [RenderModel.cs](../Engine.Render/Render/Model/RenderModel.cs) uses a missing metallic/roughness texture with blue zero; multiplying by that sample suppresses the scalar metallic factor. Some scalar-only materials also miss the lit material path. Fix these deliberately under the new material contract.
- [BasicWorldRenderer](../Engine.Render/Render/BasicWorldRenderer.cs) already batches object uploads. Its current object payload occupies the full 256-byte slot after diffuse-probe data. Material.w distinguishes lightmap/probe modes; LightInfo.w carries the indirect-only diagnostic. Neither field is free for unrelated flags.
- [Renderer](../Engine.Render/Render/Renderer.cs) already provides RGBA16F world rendering, 4x MSAA and frame-fenced retirement.
- [LightProbeVolume](../Engine.Editor/Editor/Lighting/LightProbeVolume.cs), [ProbeIrradiance](../Engine.Render/Render/ProbeIrradiance.cs) and [LevelBakeSession](../Engine.Editor/Editor/Lighting/LevelBakeSession.cs) already provide diffuse probes. Some written status notes predate that code.
- Current streaming uses XZ dependency zones, not authored rooms or door portals. Reflection probe volumes must not assume an existing room graph.
- HLSL and tracked CSO files must be rebuilt together. The .NET build currently copies shaders; it does not compile HLSL.

**Step 0 — Establish the reference before changing shading**

1. Capture the current six-room flashlight, emission, indirect-light and probe fixtures in Release, including game, F2 and standalone-editor views.
2. Create a separate proposed PBR/reflection fixture: roughness and metallic ramps, dielectric/metal spheres, a wall/floor corner, an emissive screen and one movable prop. Include point/spot lights, a switched fixture and a doorway into a differently lit room. Retain real imported GLBs alongside simple controlled geometry.
3. Add cases for scalar-only and missing textures, mirrored UVs, degenerate UV derivatives, non-uniform/negative transforms and near-zero roughness. These expose material and normal-path errors independently of artistic tuning.
4. Record resolution, device/driver, Release configuration, graphics debug-layer status, exposure, light values, shadow settings, MSAA and presentation conditions. Measure both the normal gameplay route and uncapped/offscreen GPU work where supported.
5. Record CPU/GPU pass times, frame mean/P95, allocations/GC activity, draw/upload counts, first-playable loading and resident/peak resources. Existing capped 60 FPS observations do not quantify unused GPU headroom.
6. Set explicit per-stage budgets from this baseline. The existing 16.7 ms whole-frame target applies to all overlapping engine work; it is not a budget for each effect separately.

**Completion gate:** saved reference captures and repeatable measurements, an isolated fixture, and agreed recorded budgets. No performance acceptance numbers are claimed by this plan.

**Step 1 — Correct material inputs, then implement direct PBR**

Affected existing code: [GlbModelLoader](../Engine.Render/Render/Model/GlbModelLoader.cs), [LoadedModel](../Engine.Render/Render/Model/LoadedModel.cs), [RenderModel](../Engine.Render/Render/Model/RenderModel.cs), [BasicWorldRenderer](../Engine.Render/Render/BasicWorldRenderer.cs), [TexturedModel.hlsl](../Engine.Render/Shaders/TexturedModel.hlsl), and [LevelTypes](../Engine.Editor/Editor/LevelTypes.cs).

1. Document a material contract: base colour/emission textures sample as sRGB-to-linear; metallic/roughness/normal data remains linear. Keep roughness as one declared perceptual quantity at import, shader and filter boundaries.
2. For imported metallic/roughness materials, use a neutral missing-map value that preserves the stored scalar factors. Separate this from the default material for a generic dielectric blockout primitive. Give changed fallback content distinct cache identities so the shared texture cache cannot confuse old and new defaults.
3. Route scalar-only imported materials through the lit path using shared fallback textures. Materials without UVs need a lit geometry path with valid normals and harmless dummy UVs when no maps are sampled. Keep debug helpers and UI explicitly unlit.
4. Add a shared HLSL BRDF helper, proposed as PbrLighting.hlsli: GGX specular distribution, Smith visibility, Schlick Fresnel and an energy-aware diffuse term. Start with a metallic workflow and a conventional dielectric reflectance of approximately 0.04. Define the roughness remapping once and use it consistently.
5. Use the helper for directional, point and spot direct lighting. Preserve current authored range, spotlight cone, intensity semantics and shadow visibility for this stage. A later change to physical light units or distance attenuation should be a separate migration with rebaking.
6. Apply direct-light visibility to that light's diffuse and specular contributions. Preserve emission as an independent HDR contribution. Diagnose direct, diffuse indirect, specular indirect and emission separately.
7. Define the units/convention of existing diffuse bake/probe data before changing its combination with the BRDF. Avoid double-applying base colour, a diffuse normalization factor or ambient fill. Keep the existing diffuse system working while direct PBR is validated.
8. Rebuild affected VS/PS CSOs with HLSL. If importer semantics or serialized material data change, version/recook the applicable model cache; a shader-only change does not automatically require a texture-format change.

Reference: [Filament's material and lighting model](https://google.github.io/filament/main/filament.html), plus the renderer/shader reading routes in the research report.

**Completion gate:** metals retain their scalar values without textures; dielectric highlights respond to moving local lights; roughness broadens highlights predictably; extreme inputs remain finite; shadowed direct light and switch/flicker behaviour remain correct. Check intentional PBR image changes against a new reviewed reference, and legacy-mode output against its original reference.

**Step 2 — Prove specular environment lighting with one known cubemap**

This isolates reflection math and resource bindings before adding room capture.

Delivered in unit C (2026-10-23): 128-square RGBA16F cubemap with eight GGX mips, 128-square RG16F split-sum LUT, separate resource set, black fallback and shared-budget staged uploads. Only the PBR fixture opts in. Reflection-only/on-off controls, 97+ focused assertions, old/reference image comparisons and full-host torch on/off tests pass. Added GPU payload is 1.063 MiB; paired GPU increments remain under the 1 ms budget, full-host averages about 60 FPS. The synthetic distant rig does not depict room geometry or follow puzzle switches; those are later units.

1. Verify sampled HDR cubemaps and face rendering on the selected Veldrid/D3D11 path. Start with a deterministic six-direction colour test plus a synthetic HDR environment. Confirm handedness, face order, camera conventions and reflected directions.
2. Generate a deterministic GGX-prefiltered cubemap and a split-sum BRDF lookup texture through the authoring/cooking tools. Ordinary downsampled mipmaps are not a replacement for roughness-dependent specular filtering. Record the filter version, roughness-to-mip convention and LUT assumptions together.
3. Sample the filtered environment using the same roughness convention as direct PBR. Combine it with the BRDF lookup and material reflectance. Keep existing diffuse lightmaps/probes as the diffuse source; a new cubemap must not automatically add a second diffuse environment term.
4. Use linear HDR storage initially. A full 128-square RGBA16F cubemap mip chain is approximately 1 MiB; 256-square is approximately 4 MiB. These are texture payload estimates, excluding scratch captures, CPU copies and driver overhead. Start at 128 and compare quality before increasing it.
5. Add a separate reflection resource layout rather than enlarging or repurposing the full object slot. Bind one cubemap, the LUT, sampler and reflection metadata. Verify explicit HLSL/Veldrid bindings and backend limits. Always bind a valid zero-energy fallback.
6. Add reflection-on/off and specular-only diagnostic views. Retain object upload batching and collect the new sample/binding costs separately.

Proposed new classes/files are ReflectionEnvironment, ReflectionResources and a specular-filtering tool; names are implementation suggestions.

**Completion gate:** a shiny sphere reflects the expected directions, roughness produces stable broadening, matte surfaces avoid excessive specular energy, and disabling reflections leaves direct/diffuse lighting intact. Capture the reflection-only GPU and memory increment.

**Step 3 — Add authored static room captures**

Existing integration points: [LevelTypes](../Engine.Editor/Editor/LevelTypes.cs), shared Inspector/bake authoring, [LevelBakeSession](../Engine.Editor/Editor/Lighting/LevelBakeSession.cs), level rendering, and the authoring tool. Introduce a separate ReflectionProbeSession/ReflectionProbeCache so diffuse-bake ownership remains clear.

1. Add a serializable reflection probe definition: stable ID, capture position, influence bounds, projection bounds, priority, resolution, capture mask and an explicit fixed lighting preset. Begin with axis-aligned boxes; add oriented volumes after transform tests.
2. Support placement, bounds/position previews, undo/redo, save/load, prefab cloning/remapping, capture/rebuild/cancel and cache-validity status in the shared editor path. Author one probe per simple room initially. Influence bounds select affected geometry; projection bounds approximate the reflecting room's surfaces.
3. Render six 90-degree views to a linear HDR target before exposure, tone mapping or UI. Include eligible static geometry, fixed lighting, validated diffuse indirect and invariant emission. Exclude the player, held props, debris, flashlight, editor helpers and other transient content.
4. Explicitly disable environment-reflection sampling during capture to prevent feedback. This is a one-capture approximation, not a recursive multibounce solution. Capture a coherent fixed snapshot rather than mixing six unrelated live states.
5. Request all capture dependencies through the asset system, including content outside the gameplay camera view and any required shadow casters. A capture must not inherit only the current camera's streamed/visible subset.
6. Use capture-specific view resources and conservative culling. Multiple capture views must not overwrite the game's camera/object data or corrupt its shadow-cache state. Keep the first capture workflow editor/tool-owned.
7. Prefilter the completed cubemap using step 2. Write a versioned cache keyed by geometry/material/source fingerprints, transforms, capture mask, fixed lighting, diffuse-bake identity, resolution and filter version. Use bounded reads, checksums, atomic replacement and stale/missing fallback.
8. Load prepared results on workers and upload under a shared render-thread budget with model/lightmap uploads. Account for non-preemptible uploads; split mip/face transfers where practical. Static gameplay must sample completed cached probes without running capture passes.

Cubemap capture and room-oriented probe authoring are established patterns in [Godot's reflection guide](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/reflection_probes.html).

**Completion gate:** a room capture survives restart and packaging; edited dependencies invalidate it; cancellation/corruption fails safely; no UI/player/torch appears in the capture; all six directions contain their expected geometry. Return to the game view and verify its cached shadows/culling are still correct.

**Step 4 — Make room transitions, switches and residency reliable**

Units E/F first passes (25 October 2026): automatic mode selects one room or an explicitly linked pair per model part, with pixel membership, finite box projection and smooth linear-HDR doorway blending. Optional residency pins the active/doorway rooms, prefetches connected neighbours and frame-fences eviction; the earlier six-room copy uses three slots. Null residency settings preserve the all-loaded mode used by the new lighting-stability test. Fixed-light mismatch suppresses incompatible captures; explicit bounded on/off scenarios now select matching caches in the new fixture. The old global preview remains available with automatic mode off. See [the residency guide](REFLECTION_STREAMING_GUIDE.md) and [lighting-state follow-up](LIGHTING_STABILITY_GUIDE.md).

1. Add box-projected/parallax-corrected sampling for rectangular interiors. Handle nearly parallel rays, points outside projection bounds and invalid volumes with finite fallback. Test wall reflections while moving the camera close to surfaces.
2. Select at most two relevant probes per model part initially. Use explicit room membership/volume priority and small authored transition zones. Blend in linear HDR. Distance alone must not select a bright probe through a solid wall.
3. Bind immutable or infrequently updated resources for a selected probe pair, with bounds/capture positions in their metadata; derive spatial weights from the pixel's world position. This keeps the current per-object payload intact and avoids a constant-buffer upload for every draw. If per-instance overrides become necessary, budget a separate batched buffer explicitly.
4. For very large parts spanning several rooms, split authoring/render parts or use a conservative assigned probe. Do not promise accurate per-pixel room classification from a two-probe assignment. Doors and moving props need stable transition selection.
5. Treat reflected lighting states as a correctness requirement. Validate that capture contributors and inherited diffuse bakes are invariant. Exclude unsupported switched/flickering contributors, or author a small explicit set of valid presets such as room-on/room-off.
6. When live light state no longer matches a cached preset, select a matching ready capture or fade to a declared safe fallback. Avoid retaining a bright on-state reflection after its light switches off. Do not attempt every combination of puzzle light states.
7. Add room/nearby-probe dependencies to residency without treating existing XZ zones as actual room IDs. Pin active probes, prefetch upcoming ones, count bytes, retain useful neighbours and retire old GPU resources through the existing frame fences. Include old/new overlap and capture/filter scratch in peak-memory reporting.
8. Preserve ready-before-replacement. For the same valid dependencies, keep the previous result until its replacement is GPU-ready. For changed geometry or incompatible lighting, suppress the invalid result and use fallback. Failure must not hide scene geometry or reset puzzle state.

Reference implementations to study: [rbfx probe selection, blending and budgeted updates](https://rebelfork.io/docs/rendering-reflection-probes.html), and ezEngine/RBDOOM source routes in the research report.

**Completion gate:** carry a metal prop through the doorway, open/close doors and toggle lights. Reflections transition without persistent wrong-room light leakage or stale switched fixtures. Forced eviction/reload preserves gameplay state, retains safe GPU lifetimes and respects measured memory/upload budgets.

Static probes reflect captured room structure. They do not supply current images of an opening door, the player or other transient objects. Live updates, planar mirrors or screen-space detail can be evaluated later as separate features.

**Step 5 — Integrate, validate and roll out one level**

1. Use the same material/probe resolution and capture metadata in Game, F2 and HS2Editor. UI composition remains after scene exposure/tone mapping. Verify player/weapon, debris, hidden crate parts and replacement models in the appropriate lighting paths.
2. Complete cache cooking/build/publish copies and missing-cache handling. New optional level fields must survive editor saves, prefab operations and older save-state data.
3. Compare legacy/PBR modes, reflections off/on, direct/indirect diagnostics, ready/stale/missing captures and light-switch presets using fixed camera points. Update only reviewed references that intentionally change.
4. Extend the existing rendering harness with focused BRDF/material GPU checks, cubemap orientation/filter checks, room-transition captures and reflection-cache invalidation/corruption tests. A/B commands and `validate-reflections`, `validate-room-reflections`, `validate-room-transitions`, `profile-room-transitions [--reverse]` and `benchmark-gameplay-room-transitions on|off` are implemented. Cold-first-playable, forced probe eviction and real-level rollout checks remain to be added.
5. Run the existing relevant validation/flashlight/stress routes listed below. Include manual standalone-editor use and player input/feel; offscreen tests alone do not cover them.
6. Migrate one real level explicitly, tune its PBR lighting/exposure and rebuild affected bakes/captures. Keep a recorded setting that restores its legacy rendering while reviewing the new look.
7. Reconcile delivered status and reproduction data in the roadmap, validation report and renderer documentation. Only change defaults for broader content after the migrated level meets the gates.

**First-release gate:** PBR direct light, existing diffuse indirect and room specular reflections work together; editors and game agree; switches/puzzles and source assets remain reliable; CPU/GPU/P95/loading/memory measurements meet the recorded budget. No routine frame introduces a WaitForIdle. Every failed or missing reflection resource has a working visual fallback.

**Step 6 — Add controlled post-processing and atmosphere**

1. Add modest HDR bloom before tone mapping, followed by a deliberate colour-grading/tone-mapping choice. Keep exposure fixed in the acceptance fixture so bloom cannot conceal material/lighting errors.
2. Add a simple budgeted fog model, then evaluate volumetric shafts only where they help the scene. Compare fog history/noise and shadow interaction if temporal accumulation is introduced.
3. Retain the existing 4x MSAA baseline. Investigate specular antialiasing or a temporal path only if the new materials show a measured visual need; a temporal method requires motion/history and disocclusion design.
4. Store shared authoring settings and quality limits; measure each enabled effect separately.

Study Wicked's render paths and Lumix's pre/post-tone-map hooks. **Gate:** preserved UI, readable dark scenes, stable camera movement and a measured GPU/memory increment for each effect.

**Step 7 — Improve room visibility and prefetch**

First pass delivered 2026-10-26 in the separate room-visibility test. Main-view/prefetch gates pass for this fixture; whole-atlas/physics streaming, door-state occlusion, cold-start measurement and larger-level/manual acceptance remain open. See [ROOM_VISIBILITY_GUIDE.md](ROOM_VISIBILITY_GUIDE.md).

1. Introduce explicit room volumes and door/portal connections independently of the current asset zones. Store persistent gameplay IDs outside renderer residency.
2. Apply conservative room/portal traversal or prototype software occlusion using large simplified occluders. Test fast turns, partially open doors and thin geometry against a no-occlusion reference.
3. Use connectivity to prefetch models, diffuse bakes and reflection captures before a doorway reveals them. Keep camera visibility distinct from light visibility, audio, collision and puzzle execution.
4. Keep held objects, debris/replacement dependencies and shadow-relevant content available.

Study RBDOOM portals and ezEngine occlusion. **Gate:** reduced submitted work in genuinely occluded rooms without missing pixels/shadows or new traversal stalls.

**Step 8 — Separate rendering data and reduce repeated draw work**

1. Introduce a small managed RenderView/RenderScene snapshot and renderer handles. Extract immutable transform/material/light/probe assignments from gameplay; reuse one representation for main, capture and shadow views.
2. Gradually move direct Veldrid access behind rendering services. The current concrete Renderer exposure in EngineContext/IWorldRenderer requires an explicit transition; IGameModule gameplay remains C#.
3. Group compatible draws and cache pipeline/material bindings. Add instancing for repeated static meshes only where counts justify it, preserving transparent ordering and independent hidden/damaged parts.
4. Consider worker extraction/double buffering after ownership and profiling are clear. Double buffering has latency, memory and synchronization costs that belong in the measurements.

Study ezEngine extraction, rbfx batching and Atom's feature processors. **Gate:** reduced recording/state costs, bounded allocations and unchanged entity, capture and shadow behaviour.

**Step 9 — Use profiles to choose the next performance system**

- If the 32-light global list or per-pixel light loop is the constraint, prototype tiled/clustered lists. Measure list construction plus shading, define overflow behaviour and retain a small-scene reference. Shadow allocation remains a separate budget.
- If startup/residency dominates, evaluate packaged manifests, avoided repeated source reads, geometry optimization, shared collision instances or finer texture-mip streaming. Track source/cache compatibility and first-playable time separately.
- If CPU simulation/extraction dominates, first examine work grouping, data layout and appropriate C# jobs. Evaluate a mature physics subsystem against contact stability and gravity-held/door/debris behaviour when warranted.
- If a native subsystem then offers a demonstrated benefit, keep C# as the public runtime. Use a versioned C ABI, stable opaque handles, batched data, explicit callback/lifetime rules and fence-safe resource release. Measure interop/copying alongside the subsystem itself.

Study Godot for clustered rendering and managed/native integration, Wicked for jobs, and the resource systems catalogued in the research report. **Gate:** choose the next subsystem from representative evidence; record the tradeoffs and demonstrated benefit before expanding the migration.

**Existing verification commands**

These command names were checked in [Tools/LevelAuthoring/Program.cs](../Tools/LevelAuthoring/Program.cs). Run from the repository root after building the tool in Release, using the bundled SDK when available:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-rendering
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-level-rendering
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-lighting-state
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-indirect-bake
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-probe-room
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-visibility-streaming
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on --stress
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight off --stress
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-probes on --stress
```

Add model-cache/collision/inventory checks when the corresponding code changes; avoid repeatedly running unrelated expensive suites. Existing commands do not by themselves validate the new PBR/reflection work. Keep new tests tied to observable material, capture, transition and cache behaviour.

**Suggested review units**

| Unit | Concrete reviewable result |
| --- | --- |
| A | Step 0 fixture/baseline, explicit lighting-mode compatibility and corrected scalar/missing-map materials |
| B | Direct PBR helper and shader integration, with material/shadow evidence |
| C | One synthetic filtered environment, BRDF LUT, bindings and incremental cost |
| D | Authored editor room capture, versioned cache and restart/invalid-cache evidence |
| E | Box projection, room-boundary selection and switched-state correctness |
| F | Residency, build packaging, editor/game parity and one migrated real level |

Each unit should leave the game runnable. Start implementation with A, then B; defer wider architecture changes until the first graphics milestone has concrete evidence.


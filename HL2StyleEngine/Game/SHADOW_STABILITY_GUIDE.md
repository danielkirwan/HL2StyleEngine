# Shadow Stability And Flashlight Test

Implemented 2026-10-26. Launch **`LaunchSixRoomShadowTest.bat`** from the repository root. It builds Release and opens the new `Content/Levels/sixRoomShadowTest.json`, a copy of the fourteen-fixture lighting-stability level with three optional settings enabled. Original levels, launch defaults, source models/materials, physics and inventory are unchanged. Runtime remains C#.

Acceptance correction (2026-10-26): the initial four-slot implementation did **not** eliminate the reported pop-in. This same test level now uses 16 point slots, covering all 14 fixtures simultaneously. Fixed perspective-depth bias and filtering keep flashlight shadows attached at distance. No additional level copy is needed. Older launchers deliberately retain their old lighting settings.

Flashlight mount correction (2026-10-26): the previous 24cm sideways / 14cm world-down source exaggerated visible shadow displacement in close-ups. The enabled offset is now **4cm right / 2cm camera-relative down**, with continuous wall retraction. This is a game-side placement correction, not another shader change. The accepted environment-shadow coverage, held-object shadow casting and flashlight brightness remain unchanged. Use the same BAT; this mount-only update needs no level edits or lighting recook.

## Playtest

1. Use **F** to toggle the flashlight. Aim across a crate onto the floor or wall behind it, then move sideways. Shadows are easier to distinguish with the fixture lights off.
2. Use **E** on the power lever beside spawn to toggle the ceiling fixtures. Puzzle indicators remain independent.
3. Approach and retreat from the hall crates and rolling-door housings. Carry/throw a crate, break it, and watch the shadows update.
4. Open this JSON in HS2Editor or F2 for the same textured scene. Existing docking/layout and selection controls are unchanged.

## What Changed

- The initial four-point pool could still evict a visible fixture during approach/retreat. The level now uses **16 point slots plus two independent spotlights**, so all its 14 fixtures remain shadowed. Slots retain stable entity IDs across camera sorting. Hysteresis/priority still apply if a scene exceeds the configured budget.
- Point faces are packed four per physical layer at 512-square; spotlights retain full 1024-square maps. There are 98 logical faces in 26 physical layers, retaining the previous **104 MiB** steady atlas allocation. Each dirty layer redraws its occupied neighbour tiles after a whole-layer clear. Quality changes invalidate the layout cache. Fracture visibility is resolved once per caster, not for every light face; matrices still upload once.
- The former fixed nonlinear depth bias grew into a large world-space offset at distance. The new variant uses a 2mm receiver bias converted to projected depth, a small floating-point floor, geometric receiver-plane correction and bilinear percentage-closer filtering. Normal-map details no longer distort the shadow bias. Filtering cannot sample neighbouring atlas tiles. No routine GPU idle wait was added.
- **Camera-Relative Flashlight** offsets the source 4cm right and 2cm down in the camera basis, aiming toward the crosshair at 8m. Its down direction follows camera pitch instead of world Y. A short collider ray includes the 5mm clearance in its reach, allowing continuous retraction toward the eye near walls rather than a jump at the query boundary. Colour, intensity 7, range 22m and 55-degree cone are unchanged. Held world objects still cast shadows; the first-person weapon viewmodel does not.
- **Prewarm Alternate Reflections** prepares the second explicit reflection state after the active state becomes ready, using the existing bounded worker/upload path. Updating the active bank last preserves visible bindings. There are no runtime scene captures. This fixture starts on, so its diffuse bake is already loaded and retained before the all-off state, which needs no diffuse atlas.

## Authoring

Shared **Toolbar > Lighting** controls: **Point Shadow Slots** (1-16), **Point Shadow Selection Margin**, **Camera-Relative Flashlight**, and **Prewarm Alternate Reflections**. Changes use existing undo/save. **Toolbar > Room Reflections** shows whether the alternate bank is ready. The renamed flashlight checkbox still serializes as `OffsetFlashlight`; existing saves require no migration.

Optional JSON fields: `PointShadowLimit`, `OffsetFlashlight`, `PrewarmLightingStates`. Missing/zero point slots mean original one-point mode; missing booleans are false. Old levels retain their original shader variant. Settings do not silently rewrite other levels or startup selections.

After editing these settings or fixed scene content, save and rebuild the affected diffuse bake and both reflection states. The test ships with one on-state `.hs2lightmap` and twelve on/off `.hs2reflection` files. Virtual `.lighting-on.json` / `.lighting-off.json` names identify caches, not playable levels. Build game/editor after cooking to refresh outputs. See [the state-authoring workflow](LIGHTING_STABILITY_GUIDE.md).

The new variant is `TexturedModelMultiShadowPS.cso`; old `TexturedModelPS.cso`/VS binaries are preserved. The .NET build copies compiled shaders, not HLSL compilation. Rebuild the new variant with the installed Windows SDK:

```powershell
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T ps_5_0 /E PSMain /D MULTI_POINT_SHADOWS=1 /Fo Engine.Render/Shaders/TexturedModelMultiShadowPS.cso Engine.Render/Shaders/TexturedModel.hlsl
```

## Limits

- This covers all fourteen point lights in this test, **not unlimited shadowed lights**. A lower setting or more than sixteen eligible points can still require handoffs; visibility-aware ranking and gradual transitions remain future work. The active-light list remains capped at 32. Static/dynamic shadow separation remains future work.
- The 26 physical 1024-square R32 layers remain **104 MiB**, versus the original one-point mode's 32 MiB. The tradeoff is 512-square point faces, not increased VRAM. Flashlight maps remain 1024-square. Initial expansion can overlap the old atlas for **136 MiB** until its frame fence completes. Lowering the slot setting does not shrink an expanded atlas until renderer disposal. Figures exclude uniforms, geometry and driver allocations.
- These are filtered point-source shadows, not physical soft area-light shadows. A nearby flashlight can legitimately project a large, sharp silhouette; zero ambient and fixtures off make it black. The correction does not add artificial fill light, contact-hardening penumbrae or ray tracing.
- Prewarming supports exactly **two valid automatic-reflection scenarios**, retaining at most one inactive bank. This fixture uses about **12.76 MiB** for six active and six inactive captures. Revisions invalidate both banks; queued and retiring room resources are included in memory reporting.
- Background prewarming is not a first-playable/loading gate. A very early switch, missing/stale cache or unmatched state still uses dark fallback until compatible data is ready. The readiness indicator exposes this. Warm-process render/readback preparation measured roughly **2.1-2.8s**, not cold disk or process-start time.
- This does **not** pre-upload an arbitrary alternate diffuse bake. Starting a level off can still require first-use preparation of its on-state diffuse atlas. General diffuse-state prefetch and cold-first-playable instrumentation remain open.
- Ambient/directional remain zero. No global fill, texture edits, fog, bloom, realtime GI, portal occlusion or native-runtime migration was introduced.

## Correction Validation

### Flashlight Mount Follow-Up

`validate-flashlight-mount` exercises the actual game renderer with loaded models, bake and both reflection states. It verifies 20 yaw/pitch combinations (including near-vertical views), unchanged illumination/convergence, a continuous 61-position wall sweep, eye-inside-collider clamping, free-space recovery and the unchanged legacy eye-origin setting. Five close-up crate/railing views and three held-crate poses agree with uncached/unculled references within 1/255. Pickup/release uses the real gravity-gun path; the three test poses are positioned deterministically, not a full simulated holding playthrough. The held model is explicitly verified in the shadow-caster geometry.

Before/after diagnostic images use the same viewpoints and fixture-off lighting. Counts below are whole-frame pixels darkened by more than 15 summed RGB levels versus shadows disabled, not isolated silhouette areas or a quality score:

| View | Old 27.78cm source displacement | New 4.47cm displacement |
| --- | --- | --- |
| Crate front | 17,099 | 6,387 |
| Crate side | 17,620 | 6,907 |
| Crate downward | 2,447 | 542 |
| Railing downward | 52,215 | 16,259 |
| Railing oblique | 22,637 | 8,963 |

Fixture-only before/after frames agree within 1/255 and authored level data is unchanged. The close-up and held images were inspected: shadows remain present but sit much closer behind the object from the player's viewpoint. A near-eye source naturally hides more shadow behind the object; this is not disabling shadows or adding area-light softness. QA captures are in the authoring tool's `qa/flashlight-mount-*` folders.

The existing `check-shadow-pixels` approach/retreat, eight-view orbit, prewarm, wall and edit/undo suite passes again, retaining all 14 fixture owners and matching uncached/unculled images within 1/255. `validate-shadow-test` still passes 69 checks. No environment shaders, light caches, scene geometry, source models or physics settings changed for this mount follow-up. Older correction results below describe the preceding shader/pool work.

Normal-desktop Release follow-up on the same RTX 3060, 1920x1009, VSync, 120 warmup + 300 measured full-host frames per run:

| Workload | Flashlight | Wall mean | Wall P95 | Frames >33.3ms |
| --- | --- | --- | --- | --- |
| Six-crate destruction, shutters, gravity hold/release, inventory | On | 16.772ms | 17.606ms | 0 |
| Same stress | Off | 16.775ms | 17.562ms | 0 |
| Five test-only room teleports with simulation/presentation | On | 16.555ms | 17.721ms | 0 |
| Four prewarmed fixture switches | On | 16.657ms | 18.340ms | 0 |

All pass the unchanged mean <=17ms / P95 <=21ms / <=1 frame >33.3ms / no >100ms or streaming-wait gates. Measured averages are approximately 59.6-60.4 FPS, not a locked minimum 60. Both stress cases reach 120 fragments; all runs retain the 104 MiB atlas, about 12.76 MiB reflection banks and one main-scene upload/frame, with zero runtime captures. This is short local validation, not a long soak or cold-load benchmark.

Release solution and Debug/Release authoring builds are clean; the mount suite also passes in Debug. All 15 older rendering references still match within 1/255. Both Release app outputs pass 61 package hash/cache checks. The exact BAT builds, launches and exits in the 60-frame smoke harness (16.65ms mean / 21.03ms P95, not a gameplay pacing-gate run). No reference images or source-level files were replaced for this follow-up.

### Environment And Contact Correction

- `validate-shadow-contact`: 64 GPU views (point/spot, full orbit, 2/4/8/12m), compared with independent CPU ray/box intersections including the floor as a caster. All pass: spot interior coverage 100%; point coverage at least 99.6%; false shadows at most 0.032% of clearly lit samples. The pre-fix 24-view diagnostic failed six distant views, with only 64.1-94.0% coverage. These are sampled geometric tests, not proof for every mesh/material.
- `validate-shadow-test`: 69 pool/fixture checks, including all 14 owners retained through a 40-position route and unchanged authored geometry/gameplay.
- `validate-multi-shadows`: full 16-point/two-spot capacity, stable identities, cache/culling references, shared-layer dirty-neighbour redraw, quality-mode transitions, removed/re-enabled lights and bounded allocation. The old unshadowed shader comparison remains unchanged.
- `check-shadow-pixels`: the actual pressure-plate crate has a nine-view 18m-to-2m-and-back route and eight flashlight orbit views. All retain the relevant fixture shadow and agree with uncached/unculled rendering within 1/255. Three older hall views, prewarm/on-off-on, wall clamp and authoring undo remain covered. Game captures were visually inspected.
- Both lighting states were recooked; the on bake still contains 532 models / 766 valid probes. Only the shadow-test JSON slot setting changed; source meshes, materials, geometry, physics and older levels were not edited.

Final checks: Release solution and Debug/Release authoring builds have zero warnings/errors; the 69 CPU checks also pass in Debug. All 797 older lighting-stability checks and 15 Legacy reference images pass (within 1/255, references untouched). Game and HS2Editor Release each pass 61 package shader/asset/cache checks. Fresh game/F2/HS2Editor captures remain textured; the standalone scene image was inspected. The exact BAT builds, launches and exits under the 60-frame smoke harness; its stationary 16.65ms mean / 24.61ms P95 is launch verification, not another gameplay pacing pass. Final diff whitespace check is clean.

Normal-desktop RTX 3060, D3D11 Release, 1920x1009, VSync, 120 warmup + 300 measured host frames after the correction:

| Workload | Flashlight | Mean | P95 | Frames >33.3ms |
| --- | --- | --- | --- | --- |
| Six broken crates, shutters, gravity hold, inventory | On | 16.661ms | 16.811ms | 0 |
| Same stress | Off | 16.661ms | 16.959ms | 0 |
| Five room teleports with simulation/presentation | On | 16.661ms | 16.803ms | 0 |
| Same room route | Off | 16.660ms | 16.825ms | 0 |
| Four switches with prewarmed state checks | On | 16.661ms | 16.811ms | 0 |
| Same switches | Off | 16.661ms | 16.786ms | 0 |

All six pass unchanged mean <=17ms / P95 <=21ms / <=1 frame >33.3ms / no >100ms or streaming waits gates. About 60 FPS locally, not a guaranteed minimum on every frame or PC. Stress peaks at 120 fragments, one main upload/frame, 34 models / 108.2 MiB and 104 MiB shadow depth. Shadow CPU averages 2.18-2.21ms in stress and 0.45-0.63ms on room routes; torch-off traversal reuses every depth face. Reflection banks remain 12.76 MiB; no runtime captures.

Restricted-sandbox stress diagnostics failed: off mean/P95 17.819/23.056ms before mesh-mask deduplication and 17.485/22.644ms after. The latter recorded three >33.3ms frames. They are not counted as passes. Final comparisons use normal-desktop permissions, matching the launcher and historical baseline; reduced shadow work alone is not claimed to explain the presentation difference. Long soaks, arbitrary light counts and manual controller acceptance remain open.

## Initial Four-Slot Validation (Historical)

The measurements below preceded the repeated pop-in report. Their limited camera route did not establish full-level stability and is superseded by the correction checks above.

Release solution and Debug/Release authoring builds pass with zero warnings/errors. New CPU suite: **29** pool/fixture compatibility checks. GPU tests cover four points plus two spots (26 slices), stable IDs/reordering, depth reuse, bounded allocation/retirement, all-lights off/on, and twelve moving-light/door frames against uncached/unculled rendering. The new shader matches the old unshadowed PBR fixture exactly.

Actual-game images verify the first prewarmed off/on change, restoration within 1/255, authoring edits/undo invalidating both banks, and no runtime capture passes. Hall-crate shadows remain at three camera positions (29,325 / 33,949 / 28,153 pixels darkened versus shadows disabled). The real shoulder flashlight affects **8,031 pixels** in its shadow on/off comparison. Wall tests cover a collider larger than render bounds, eye overlap, recovery and unchanged legacy placement. These are sampled views, not proof of every viewpoint.

Existing 797 stability checks, old switch/idle/undo GPU tests, GPU PBR shadows and all 15 Legacy reference images pass without replacing references. Game/F2/standalone scene captures remain textured; Game and standalone captures were inspected. Game and HS2Editor Release outputs each pass **61 package identity/cache checks**. Native docking/controller acceptance and long gameplay soak remain manual.

The 29 new CPU checks also pass in Debug. The exact BAT builds, opens the new level and exits normally under the automatic 60-frame smoke harness. That short stationary smoke sample was 16.28ms mean / 23.28ms P95; it validates launch/exit, not the gameplay pacing gate below. The restricted sandbox initially could not restore packages; the launcher passed with access to the installed package cache.

RTX 3060, D3D11, Release, 1920x1009 client, VSync, 120 warmup + 300 measured host frames:

| Workload | Flashlight | Mean | P95 | Frames >33.3ms |
| --- | --- | --- | --- | --- |
| Six broken crates, shutters, gravity hold, inventory | On | 16.666ms | 16.800ms | 1 |
| Same gameplay stress | Off | 16.667ms | 16.792ms | 0 |
| Five room teleports with simulation/presentation | On | 16.661ms | 17.146ms | 0 |
| Same room route | Off | 16.667ms | 17.069ms | 0 |
| Four switches, alternate-ready assertion before each | On | 16.719ms | 19.030ms | 0 |
| Same switching sequence | Off | 16.715ms | 18.669ms | 0 |

All pass unchanged local gates: mean <=17ms, P95 <=21ms, at most one frame >33.3ms, none >100ms, no asset-streaming waits. One main-scene object upload/frame; 34 resident models / 108.2 MiB; no runtime capture. Stress reached 120 fragment bodies. Regular workloads average about 60 FPS; switches about **59.8 FPS**, not a locked minimum 60. An earlier switch-on diagnostic run recorded 16.770ms mean / 19.165ms P95 and one >33.3ms frame. These short samples do not establish other-PC or cold-launch performance.

## Reproduction

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release. Run with `.dotnet/dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll`:

```text
validate-shadow-test
validate-shadow-contact
validate-multi-shadows
check-shadow-pixels
validate-flashlight-mount
capture-shadow-test
benchmark-shadow-test on
benchmark-shadow-test off
benchmark-shadow-route on
benchmark-shadow-route off
benchmark-shadow-switches on
benchmark-shadow-switches off
validate-shadow-package Game/bin/Release/net10.0
validate-shadow-package HS2Editor/bin/Release/net10.0
validate-pbr-legacy
```

`build-shadow-test` refuses to overwrite an existing level. `cook-shadow-state on` rebuilds diffuse plus six captures; `cook-shadow-state off` rebuilds six captures. Offline cooking recorded 23.5s for diffuse and up to 232.39 MiB tracked capture GPU resources including temporary atlas overlap. This is offline work, not a per-frame cost.

## Next Work

1. Manual crate/door/held-object acceptance and longer traversal/switch soak. Profile remaining switch-frame overhead and true cold first-playable time; extend preparation to alternate diffuse bakes.
2. Room/portal-aware visibility and prefetch, keeping camera visibility separate from shadow-caster, physics and puzzle residency. Validate against an all-visible reference.
3. Improve over-budget shadow handoffs, point-face quality and static/dynamic separation within explicit memory/frame-time budgets before increasing light counts globally.
4. Add budgeted fog/colour grading/bloom after those correctness/performance checks. C# remains the runtime.

# Six-Room Lighting Stability

Follow-up (2026-10-26): [the separate shadow test](SHADOW_STABILITY_GUIDE.md) now covers all 14 fixtures with a 16-point packed shadow pool, corrected distance/contact bias, near-camera flashlight shadows and two-state reflection prewarming. Its initial four-slot pass was insufficient. The later mount correction uses 4cm right / 2cm camera-relative down instead of the large shoulder offset, preserving brightness and held-object shadows. This document retains the original October 25 fixture's one-point budget and historical cold-state timings; that earlier level is unchanged.

Implemented 2026-10-25. Run root `LaunchSixRoomLightingStabilityTest.bat` for the separate `Content/Levels/sixRoomLightingStabilityTest.json`. It builds Release and explicitly opens the source level. Original levels, startup selections, imported materials, colliders, physics, HUD and accepted shaders are unchanged. Runtime remains C#.

## Playtest

- Fourteen visible ceiling fixtures illuminate the hall and five rooms. Six additional side fixtures cover areas outside the previous centre lights' ranges.
- Ambient and directional strengths remain **zero**. Subtle one-bounce diffuse lighting comes from the fixtures, not a global ambient increase. Moving props, debris and weapons sample its light probes.
- Aim at the lever on the stand near spawn and press **E** to switch the fixtures off/on. Direct lights, linked tube emission, diffuse bounce and room reflections follow the matching state. Existing red/green puzzle indicators remain independent.
- **F** toggles the initially-off flashlight. Walk along both side walls, inspect the floor from a distance, approach doorways, carry objects, and toggle the lever repeatedly.
- Six room reflections remain resident in this small test level. The original three-slot streaming fixtures remain available unchanged for comparison.

## What Caused The Dark Floor

Fixed-camera GPU comparisons on the previous point-light test, at x=-2/-6/-10, found no changes over 1/255 when disabling asset streaming and loading all models. Making all six room reflections resident changed 154,839 / 5,160 / 0 pixels over that tolerance. Thus reflection availability reproduced approach-dependent lighting changes; missing geometry/texture streaming did not in these tests. The 28m hall also exceeded the reach of its three centre lights.

The floor's glTF metallic factor is 1, but its metallic texture channel averages 0: it is not the earlier polished-crate material bug. Its source asset was not modified. On the new level, all three positions produce no differences over 1/255 when disabling asset streaming or forcing all reflections resident. This validates the tested views, not every possible viewpoint or large-level streaming policy.

## Shared Editor Controls

1. Open the new JSON in HS2Editor or F2. Existing textured scene rendering and docking remain unchanged.
2. In **Toolbar > Indirect Lighting > Lighting States**, **Create On / Off States** creates two explicit configurations of the fixed `Bake Indirect` lights. A separate `LightSwitch` targets their IDs. Fixture transforms/parents must remain fixed, without scripts, pickup behaviour or flicker.
3. **Set Authored State: on/off** changes the level's starting light flags and supports undo. This is an authored edit, not a temporary diagnostic preview. Save the intended starting state before launching.
4. Select **on**, Save Level, then **Bake Lighting**. Capture/rebuild each room reflection using **Toolbar > Room Reflections** and its selected probe. Select **off**, save and capture the six off-state reflections. The all-off state needs no diffuse atlas. Restore **on** and save for the default playtest.
5. Bake/cache names include the scenario: `level.lighting-on.hs2lightmap` and `level.lighting-on.<probeId>.hs2reflection` / `level.lighting-off.<probeId>.hs2reflection`. The intermediate scenario JSON names are virtual cache identities, not additional levels to create or launch. Build the game/editor after cooking to refresh copied outputs.
6. Fixed detailed props can use **Static Lighting > Reflection Only / Probe Lit**. They remain visible in room captures and receive probe lighting, but do not consume diffuse triangle charts or occlude the diffuse bake. Architecture stays in the diffuse bake. Excluded props therefore cannot cast baked bounce shadows.
7. **Toolbar > Lighting > Point Shadow Selection Margin** is 2m in this level. It retains the current eligible point-light shadow until another is meaningfully closer, reducing midpoint oscillation. Default 0 preserves old levels.

Changing geometry, fixed materials/lights or scene data invalidates derived lighting. Rebuild matching bakes/captures; never conceal stale data by changing source transforms or restoring bright on-state caches while lights are off. Runtime switches use saved light overrides without modifying authored JSON.

## Bounds And Limits

- Up to four explicitly authored scenarios with unique safe IDs, each listing every fixed bake light exactly once (maximum 32). Unsupported mixed states or invalid scenarios use direct lighting with black indirect/reflection fallback. Arbitrary combinations, realtime GI and flickering baked fixtures are not implemented.
- The active reflection bank and one inactive bank can stay warm. In this test that is six active plus six inactive captures, approximately **12.76 MiB** after both states load, versus **6.38 MiB** for one. Older streaming levels still use their configured slot budgets. This is a deliberate small-level quality/memory tradeoff, not a scalable replacement for portal-aware prefetch.
- One active/inactive diffuse bake pair is retained. The all-off state has no atlas, so this fixture stays below **40 MiB** bake GPU resources (37.15 MiB with all 532 bindings). Authored edits invalidate retained resources; GPU retirement remains frame-fenced. Temporary retirement overlap, capture scratch and driver overhead are additional to steady-state payload figures.
- First use of a cold lighting state validates and uploads its saved captures in the background. Direct light changes immediately; unavailable indirect data stays dark. The first off-state preparation measured about **615ms**, then retained switches about **15-16ms** in the render/readback harness. That is not a cold game-launch measurement or a guarantee on other hardware. Avoidable repeated loading is removed; first-state prewarming remains future work.
- Bake: 532 architecture models / 34,632 triangles, 2048-square RGBA16F atlas, 4 texels per triangle, 16 samples, 25m ray limit, 3m probe spacing, **766 valid probes**. Offline diffuse cooking took 22.8s locally. Detailed props use probe lighting to keep chart allocation bounded.
- Probe occluders now allow up to **65,536 triangles**; probe count remains bounded at 4,096. Lightmap metadata is capped at 16 MiB and file payload at 64 MiB, with integrity and dependency validation unchanged. Existing version-1/2 files remain compatible.
- Shadow budget is still **one point light plus two spotlights**, not fourteen shadowed lights. Other lights may leak through walls. Hysteresis reduces ownership chatter, not every shadow handoff discontinuity. Movable doors are not baked occluders; captures are fixed-scene approximations.
- No bloom, grading, fog, realtime reflections, native rewrite, or geometry/physics streaming is included.

## Validation

New level/scenario/cache/undo/shadow checks: **797 passed**. Actual-game GPU tests cover focused E interaction from spawn, matching on/off caches, saved overrides, visible indirect-only energy, rapid switching, retained states after idle, editor state edits/undo and resource invalidation. Restored images match within 1/255. Game/F2/HS2Editor scene captures are textured; game and standalone images were inspected. Native docking/input acceptance remains a manual check.

Release solution and Debug/Release authoring builds pass with zero warnings/errors. The 797 scenario checks also pass in Debug. Game and HS2Editor Release outputs each pass 60 packaged level/shader/model/cache identity and validity checks. Existing regressions pass: 282 diffuse/probe checks, 50 room capture checks, 106 room transition checks, 937 streaming checks (poll count varies), 747 original point-light fixture checks, GPU PBR shadow/cache/moving-door checks, and all 15 legacy rendering-reference images within 1/255. References and shaders were not regenerated to make tests pass.

RTX 3060, D3D11, Release, 1920x1009 client, VSync, 120 warmup + 300 measured host frames:

| Actual host workload | Flashlight | Mean frame | P95 | Frames >33.3ms |
| --- | --- | --- | --- | --- |
| Six broken crates / shutters / gravity hold / inventory | On | 16.667ms | 17.254ms | 0 |
| Same gameplay stress | Off | 16.667ms | 17.241ms | 0 |
| Five room teleports with simulation/presentation active | On | 16.666ms | 17.229ms | 0 |
| Same room traversal stress | Off | 16.667ms | 17.292ms | 0 |
| Four fixture-state switches, including cold off load | On | 16.779ms | 17.279ms | 1 |
| Same switch sequence | Off | 16.721ms | 17.267ms | 1 |

All meet unchanged local gates (mean <=17ms, P95 <=21ms, at most one frame >33.3ms, none >100ms, no asset-streaming waits). One main-scene object upload/frame, 34 resident models / 108.2 MiB, no runtime capture passes. Regular stress averages 60 FPS; switching averages about 59.6-59.8 FPS. These are short local tests, **not a locked minimum 60 FPS claim**, a long soak, or continuous walking through every room. The original pre-retention switch delay and initial 59.8-FPS stress sample were diagnostic runs, not silently substituted benchmarks.

The exact BAT launcher builds/runs/exits successfully in an automatic 60-frame smoke test. Its separate stationary sample recorded 16.47ms mean / 22.95ms P95: launch evidence only, not another pacing-gate pass. Renderer/readback tests and scripted gameplay do not substitute for a manual level/editor playtest.

Reproduce after building `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release, using `.dotnet/dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll`:

```text
validate-lighting-stability
check-lighting-switch-pixels
diagnose-lighting-stability
diagnose-updated-lighting
capture-lighting-stability
benchmark-lighting-stability on
benchmark-lighting-stability off
benchmark-lighting-stability-route on
benchmark-lighting-stability-route off
benchmark-lighting-switches on
benchmark-lighting-switches off
validate-stability-package Game/bin/Release/net10.0
validate-stability-package HS2Editor/bin/Release/net10.0
```

`build-lighting-stability` refuses to overwrite the authored level. `cook-lighting-state on` rebuilds its diffuse bake and six captures; `cook-lighting-state off` rebuilds six off captures. Use those only when intentionally recooking this fixture. Next: first-state prewarming/cold-load instrumentation, longer traversal/soak and manual acceptance, then broader visibility-aware residency and budgeted atmosphere.

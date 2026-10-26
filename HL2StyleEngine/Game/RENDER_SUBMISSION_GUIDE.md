# Render Submission And Twelve-Room Test

Implemented first pass: 2026-10-26. C# remains the runtime; Veldrid/D3D11 and the existing material/pixel shaders remain in use. This follows the accepted room-visibility milestone. No flashlight appearance change, fog, bloom or native-engine rewrite is included.

## Playtest

Run `LaunchRenderStressTest.bat` in the repository root. It builds Release and opens `Game/Content/Levels/renderStress12Room.json`. Open the same JSON in HS2Editor to edit it. Existing levels, launchers and Visual Studio's startup selection are unchanged.

The separate fixture contains twelve 20m x 20m rooms (4,800 m2, plus thresholds), seventeen open connections, twelve shadow-casting ceiling fixtures, twenty-four breakable/throwable crates, loose phones, health packs and suit batteries. There are 1,112 entities, 26 referenced model assets and at most 1,592 model-part draws before culling/fracture masks. All assets already existed. It is an exploration/rendering stress fixture, not a finished puzzle level: connections are open, with no new door scripts or puzzles.

Use normal movement, F for the flashlight, the gravity gun for loose objects, and the crowbar/pistol for crates. Walk through the full grid, turn around in doorways, carry an object between rooms, and compare flashlight on/off. The equipment and room lighting vary while the central routes remain clear.

This fixture deliberately uses direct PBR lighting, a small 0.025 ambient term, zero directional light and no diffuse lightmap. Six freshly cooked reflection captures each cover an adjacent room pair, within the existing eight-probe authoring limit. This paired-room reflection approximation is not equivalent to twelve independently captured/blended rooms. Visibility hints load captures under a six-slot budget; three were resident in the start-room run, six after the route. Original fixture-only and baked-lighting levels retain their own lighting settings.

## Rendering Contract

- `RenderScene` / `RenderInstance` provide sealed, frame-local model values and borrowed renderer handles. Game extraction resolves a ready model, authoritative transform, fracture mask and visible-object lighting data once, before main/shadow submission. Simulation continues to own entities; this is not an ECS conversion.
- Main-camera and shadow visibility remain independent. Off-camera shadow casters and held props are preserved. Fallback primitives retain author order and their existing shape. Unready model visuals keep the existing streaming fallback behaviour.
- Reflection captures build a separate immutable scene with their own resources, camera and shadow cache. They never reuse the game's culled subset. Capture-owned bindings remain alive through their fence.
- `GroupModelDraws` only groups consecutive submissions. It reuses compatible pipeline/material/geometry bindings without sorting. Primitives flush the group, preserving overlay and depth ordering. Group scopes cannot nest and must finish before the next frame.
- Static instancing requires identical vertex/index geometry, index format, material/lightmap/reflection bindings, pixel-shader material/emission and diffuse-probe values. Transform, normal matrix and tint travel in an instance vertex buffer. Dynamic/held/destructible/interactive/scripted gameplay objects are excluded. Non-opaque tints use individual draws. Incompatible baked surfaces also remain individual.
- The new `TexturedModelInstancedVS.cso` is an additive vertex-shader variant. The existing VS and both pixel shaders are unchanged by this milestone. Existing normal-matrix handling is retained and computed once per model rather than per part.
- One batched instance upload is used when needed. Maximum 4,096 object slots is unchanged; the lazy GPU instance buffer adds 589,824 bytes (0.5625 MiB), plus a same-sized managed staging array per renderer. This is not a memory-saving feature. Shadow submissions retain their existing cached per-part path.
- Frame handles borrow existing owners' fenced lifetimes; extraction follows model pumping/eviction. There are no new routine GPU idle waits. Current frame preparation and submission run on the render thread. Worker extraction, persistent scene handles and double buffering are later work.
- Game play-mode extraction and capture extraction use the new records. F2 and standalone-editor authoring drawing retain their existing immediate path, overlays, picking and textured views; they are not fully migrated to a persistent render scene.

Reference switches are code/test diagnostics, not saved scene settings: `HL2GameModule.RenderPreparationEnabled`, `BasicWorldRenderer.DrawGroupingEnabled` and `StaticInstancingEnabled`. `ObjectDrawCount` still counts logical object/part slots. New `MainDrawCalls` counts actual main-pass hardware draws; `InstancedObjects` counts model-part instances, not unique entities. Material/pipeline binding and instance-upload counters are separate.

## Verification

- 39 isolated GPU/record checks: Legacy/PBR, one/multiple point-shadow pipelines, negative/non-uniform scales, rotations, tints, primitive ordering barriers, incompatible probe data, dynamic/non-opaque fallbacks, immediate-upload reference and the 4,096-slot limit. RGB tolerance is <=1/255.
- 151 actual six-room A/B views: both torch states, all headings, animated shutter poses, thresholds and held props. Original versus extracted/grouped submission remains within 1/255; authored scene data and entity ownership are unchanged. Existing lightmapped objects do not merge, as intended.
- 96 twelve-room A/B views: both torch states and four headings in every room. All within 1/255; 8,160 hardware draws avoided in total. Representative room images were visually inspected.
- All six HDR faces of a private reflection capture match the ungrouped reference exactly. The comparison does not replace any saved captures.
- All 15 existing Legacy Game/F2/HS2Editor images remain within 1/255. Original references were not replaced.
- New fixture checks cover IDs, model existence/part budget, reflection layout, twelve room volumes, seventeen connections and player-size doorway clearance.
- Release solution and Debug/Release authoring builds pass without warnings/errors. The 39 submission checks also pass in Debug; 347 existing room/prefetch/cache tests pass. Both Game and HS2Editor Release packages pass 44 shader/model/reflection hash/cache checks.
- The exact BAT builds, launches the new level and exits normally under the 60-frame smoke harness. Its 16.68ms mean / 22.66ms P95 is a stationary launch check, not a gameplay pacing-gate pass. New Game/F2/HS2Editor scene readbacks remain textured; native docking/input interaction and long playtesting remain manual. F2's same-position capture includes its existing foreground player debug body.

Image outputs: `Tools/LevelAuthoring/bin/Release/net10.0/qa/scene-submission-20261026-132740`, `qa/render-stress-20261026-133411` and `qa/stress-editor-20261026-135441`. Generated QA output is not a shipping dependency.

## Performance Results

Sequential normal-desktop Release runs, RTX 3060, 1920x1009, VSync, actual game Update/physics/HUD/presentation, 120 warmup + 300 measured frames. Original submission references retain the same room/frustum culling, assets, lighting and gameplay settings. No benchmarks run alongside builds or reflection cooking.

| Workload | Wall mean / P95 ms | Hardware draws/frame | Material binds/frame | Render CPU ms | Render-thread allocation bytes/frame |
| --- | --- | --- | --- | --- | --- |
| Twelve-room, torch on, original submission | 16.660 / 17.431 | 242.0 | 242.0 | 4.983 | 160,766 |
| Twelve-room, torch on, new submission | 16.660 / 17.412 | 64.5 | 62.1 | 3.816 | 152,705 |
| Twelve-room, torch off, new submission | 16.662 / 16.808 | 64.5 | 62.1 | 3.088 | 152,234 |
| Twelve-room distant-room route, torch on | 16.661 / 16.915 | 50.0 | 47.9 | 3.413 | 151,005 |
| Existing six-room destruction stress, original submission | 16.827 / 17.790 | 424.3 | 388.6 | 8.051 | 248,057 |
| Existing six-room destruction stress, new submission | 16.827 / 17.629 | 425.1 | 310.5 | 8.023 | 227,104 |
| Existing six-room distant-room route, new submission | 16.640 / 16.813 | 81.7 | 60.0 | 3.488 | 162,698 |

The matched larger-level start-room run uses about 73% fewer main hardware draws and 23% less measured render CPU. VSync-capped FPS is still about 60, not claimed faster. The original baked six-room workload has effectively unchanged draw counts/CPU, fewer material binds and modestly lower allocation in these samples. Its physics/destruction timeline is not bitwise deterministic across wall-time runs. Neither path is allocation-free.

All recorded runs above pass unchanged gates: mean <=17ms, P95 <=21ms, at most one >33.3ms frame, none >100ms, and no streaming-wait frames during measurement. New twelve-room runs had zero >33.3ms frames; new six-room destruction had one. The latter averages about 59.4 FPS, not a locked minimum 60. Six-room stress opened shutters, broke six crates, held/released a prop and opened/closed inventory, with 120 peak fragment bodies.

The route uses five test-only teleports with normal simulation/movement between them, not uninterrupted doorway walking. The new scene's 30 resident models including player/weapons use 85.3 MiB; reflection payload is about 3.19-6.38 MiB and the shadow atlas stays 104 MiB. Existing six-room model/texture, bake and alternate-reflection budgets remain about 108.2, 37.15 and 12.76 MiB. No model evictions occurred in these short runs; this fixture stresses repeated geometry rather than unique-asset memory pressure.

Warm resource readiness in the new runs was 1.12-1.39s from module initialization, versus 1.32s for the reference sample. Existing six-room samples were 2.77-3.41s. These are not cold first-playable measurements or evidence of a loading-speed improvement. The first twelve-room run met timing limits but failed a mistaken test assertion requiring all six captures resident at spawn; the assertion was corrected to the actual 1-6 resident budget, and the optimized/reference runs repeated. Performance thresholds were not loosened.

## Reproduce

Build `Tools/LevelAuthoring/LevelAuthoring.csproj -c Release` with `.dotnet/dotnet.exe`, then run its Release DLL with:

```text
validate-submission
validate-submission-capture
check-submission-pixels
validate-render-stress
check-render-stress-pixels
capture-render-stress-views
benchmark-submission
benchmark-submission --reference
benchmark-submission --torch-off
benchmark-submission --route
benchmark-submission --six-room --stress
benchmark-submission --six-room --stress --reference
benchmark-submission --six-room --route
validate-pbr-legacy
validate-submission-package Game/bin/Release/net10.0
validate-submission-package HS2Editor/bin/Release/net10.0
```

`build-render-stress` creates the fixture only when absent; it refuses to overwrite editor changes. `cook-render-stress` recooks only this fixture's six captures. Rebuild the game/editor outputs after recooking. Keep GPU comparisons, cooking and benchmarks sequential on the normal desktop.

Compile the additive shader with the installed Windows SDK `fxc.exe`:

```text
fxc /T vs_5_0 /E VSMain /O3 /Fo Engine.Render/Shaders/TexturedModelInstancedVS.cso Engine.Render/Shaders/TexturedModelInstanced.hlsl
```

## Next

Manual traversal/destruction/lighting acceptance and a longer soak come next. Then implement opt-in, budgeted atmosphere: modest HDR bloom, deliberate grading and simple fog, each with disabled-path image references and the same performance gates. Cold-start instrumentation, larger unique-asset eviction tests, broader editor scene extraction, worker ownership and arbitrary-room reflection transitions remain separate tasks. C++ is still conditional on a measured need; this work does not require it.

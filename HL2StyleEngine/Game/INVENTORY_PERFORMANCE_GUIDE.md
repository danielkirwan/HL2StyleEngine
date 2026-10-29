# Inventory First-Open Performance

Implemented 2026-10-27 after the [crate/hitch investigation](HITCH_STABILITY_GUIDE.md). C# stays. This closes the measured first-open cases, not all possible UI hitches or the adaptive-texture-streaming milestone. Inventory appearance, interactions, overflow, saves, lighting and source levels are unchanged.

## What Changed

- The ImGui fallback no longer generates unused native RML or raster-text PNGs every frame. Native presentation and the explicit diagnostic document still update normally. Direct backend calls still generate documents for offline tools.
- Raster-text image dimensions are cached instead of repeatedly reopening PNGs. The metadata cache is bounded to 1,024 entries and checks image length/write time and font write time. Missing/deleted/changed files invalidate entries. This is metadata caching, not a new image format or a promise of font hot reload.
- Inventory/storage/current-level icons get first admission to the existing loading queue, followed by registered item thumbnails. This covers known recipe/reward items which have not spawned yet. Weapon/ammo images remain excluded; missing or rejected images retain normal on-demand loading.
- Native RmlUi has a separate texture cache from ImGui. During existing loading frames, it now prepares queued icons and current inventory/storage item-name images in a resource document which is never shown or focused. Its pipeline and initial buffers are also prepared without submitting visible draw commands. The inventory is not opened or altered during loading.
- Optional UI timing separates state construction, document generation/refresh, text-image resolution, icon lookup, native geometry/upload/submission and fallback drawing. Ordinary gameplay has no profiler attached.

No bridge ABI/C++ changes, shader changes, reduced texture quality, new persistence format, default-backend switch or extra loading/readiness gate were introduced.

## Bounds And Limits

Use existing loading frames only, with at most one icon attempt per frame in each cache. Discovery admits at most 64 distinct paths, prioritizing actual inventory/storage/level contents before catalog items. The ImGui resident-image-payload admission limit is 16 MiB. Native icon admission separately reserves up to 16 MiB/64 paths, restricted to the UI content root. Native selected-item titles admit at most 64 names of at most 256 characters, one per loading frame, with up to 4 MiB of image payload. The hidden document and native resources are owned by the backend context and released with it.

These are session-wide **preload admission** limits, not a cap on total RAM/VRAM. Native and ImGui copies are separate; CPU decoding, driver allocation and existing HUD resources add memory. On-demand loading can exceed the limits. There is no new eviction policy, automatic retry queue or hard per-frame millisecond budget. A large PNG can still cost time during loading. Late/new items, exhausted budgets and short loading windows can still reach the on-demand path. File metadata checks do not detect content changed while preserving both size and timestamp.

Work is moved earlier into loading, not eliminated. This pass does not demonstrate a total-startup improvement; re-measure first-playable time before claiming one. Normal gameplay/editor models retain full mip chains. The separate mip gallery is not promoted into the game by these changes.

## Test It

Run root `LaunchAtmosphereTest.bat` (source level) or `LaunchLoadingTest.bat` (packaged copy), as before. Open inventory for the first time after collecting items; test Combine, stack/split, overflow return-to-world and repeated opening. Also test loading a populated save and changing levels. No asset import or new configuration is needed.

From the repository root, run GPU checks sequentially on the normal desktop, without concurrent builds or other benchmarks:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-inventory-warmup
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-hitch-stability
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-inventory-input
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-inventory-visuals
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-redox-inventory
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll profile-gameplay-hitches
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-atmosphere --stress --native-ui --loaded-inventory
```

`--native-ui` explicitly loads the packaged bridge by absolute path in the test host and asserts native presentation remains active. The default `dotnet LevelAuthoring.dll` host used the fallback here because bare-name bridge discovery did not resolve its DLL; DLL discovery can differ with the Game apphost. The seven-run default suite therefore does **not** substitute for the explicit native test. Production discovery/selection is unchanged.

`--loaded-inventory` round-trips eight item types through RE:Dox JSON and the real inventory restore method before the loading phase, including stacks, a recipe result and one overflow item. It does not write a player's save. The existing stress actions still break six crates, hold/release a prop, add DamagedCable/open inventory at frame 210 and close at frame 250. This fixture is not a full interactive save/load playthrough.

Reports live under `Tools/LevelAuthoring/bin/Release/net10.0/qa/`. Each `.gameplay.json` contains `UiFrames`, including the actual native/fallback mode, plus explicit-native/populated-fixture flags. Timing scopes are nested: do not add `text-image` to `document-build`, or native sub-stages to `native-render-upload`, as independent costs. They are CPU/host times, not GPU timestamps or separate JIT attribution. First-open wall time is interval 91, covering preceding frame 210; the main trace also records six host stages, allocations and GC deltas.

## Measurements

Release, RTX 3060, D3D11, 1920x1009 client, VSync, warmed filesystem, fresh process per run. Each gameplay trial has 120 warmup frames and 300 measured intervals. Existing gates stay unchanged: mean <=17 ms, P95 <=21 ms, at most one interval >33.3 ms, none >100 ms, zero streaming waits. Approximately 60 FPS is not a guaranteed minimum.

First-open evidence, all report stems below are in `qa/`:

| Case | Before ms | After ms | Report stems |
| --- | ---: | ---: | --- |
| One-item fallback, wall interval | 22.627 | 17.271 | `gameplay-20261027-120634-942`, `gameplay-20261027-121247-015` |
| One-item native, wall interval | 42.216 | 19.969 | `gameplay-20261027-120959-823`, `gameplay-20261027-121622-970` |
| Populated native, wall interval | **204.880 (failed)** | 26.293; repeat 17.668 | `gameplay-20261027-122536-917`, `gameplay-20261027-123000-274`, `gameplay-20261027-123442-673` |

The populated native failure was found after the initial one-item fix: ImGui image preparation did not populate the native cache. Its first-open native refresh cost 154.822 ms, text-image work 17.205 ms and native overlay 20.626 ms. After native preparation, refresh costs 3.600/1.995 ms, text-image work 0.128/0.113 ms and overlay 8.152/5.235 ms in the two repeats. Keep the failed sample; do not average it away. These are short individual event comparisons, not a general loading-speed multiplier.

Both populated native repeats pass the original frame gates: mean/P95 **16.991/19.338 ms** and **16.661/17.510 ms**, with zero intervals >33.3 ms or streaming waits. Populated fallback also passes at **16.661/17.362 ms**, first-open 16.811 ms (`gameplay-20261027-122615-954`).

Final default source/package suite: `qa/hitch-suite-20261027-123316/summary.json`.

| Mode | Mean ms | P95 ms | Intervals >33.3 ms |
| --- | ---: | ---: | ---: |
| Source 0 | 16.716 | 17.863 | 0 |
| Package 1 | 16.661 | 17.407 | 0 |
| Package 2 | 16.721 | 17.601 | 0 |
| Source 3 | 16.663 | 17.754 | 0 |
| Source 4 | 16.664 | 17.493 | 0 |
| Package 5 | 16.659 | 17.435 | 0 |
| Package, torch off | 16.661 | 17.580 | 0 |

All seven pass, zero >100 ms/waits, 120 peak fragments, packaged runs 30 model hits/zero fallbacks. These 2,100 measured intervals are spread over fresh processes, not a continuous soak. Earlier failures in the hitch/mip guides remain historical evidence.

## Regression Coverage

- 1,053 inventory-warmup assertions: bounded metadata/reuse/invalidation, fallback document suppression, direct document generation, icon path/deduplication/admission guards and hidden native preparation. Prepared versus on-demand native inventory captures are identical, **0/255** maximum difference (`qa/inventory-warmup-20261027-124152/`).
- 670 hitch assertions retain support-query equivalence, loading/F2 gating, icon budgets/reuse and frame-report completeness. The original first-open fixture still adds its cable at frame 210; the catalog tail prepares that registered thumbnail without changing the event.
- Existing inventory input/combine/keyboard/controller/quick-click/drag/overflow/ammo-crafting checks pass. Native visual checks cover backgrounds and all 64 cells at 800x600, 1300x775 and 1920x1080. RE:Dox inventory/persistence regression passes 392 checks.
- All 15 historical Game/F2/HS2Editor images remain within 1/255, with no baseline replacement. Release solution and Debug/Release authoring builds pass with zero warnings/errors; diff whitespace checks pass. The inventory's appearance and world shading are not redesigned.

## Next Work

Follow-up 2026-10-28: [longer gameplay and startup checks](GAMEPLAY_STABILITY_GUIDE.md)
now exercise actual isolated saves and level reloads on both UI paths. Hidden
save-slot deserialization is removed; open menus remain fresh. The items below
record the original follow-up list, not a claim that this closes long-session,
cold-start or adaptive-streaming acceptance.

1. Longer full-game traversal/destruction/inventory/save-load and level-reload acceptance. Include both UI backends and loading windows too short to finish prewarming; retain failures and the original frame budgets.
2. Re-measure first-playable critical-path work, including this earlier UI preparation, with matched warm and manually collected post-restart samples. No automated cache purge/restart or total-startup claim.
3. Texture-granularity, projected-size and room-aware demand, then a gated real-level mip trial. Preserve ready-before-replacement and full-detail restoration. Do not use whole-model replacement for ordinary gameplay by default.
4. Spatial audio and the remaining research backlog follow separately in [the roadmap](ENGINE_TECHNOLOGY_ROADMAP.md).

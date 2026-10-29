# Gameplay Hitch Investigation

Implemented 2026-10-27, following the mip-gallery milestone. C# stays. This is a targeted stability pass, not production mip streaming, a new lighting feature, or completion of the research backlog.

Subsequent same-day inventory work is documented in [Inventory First-Open Performance](INVENTORY_PERFORMANCE_GUIDE.md). It adds a bounded catalog tail, separate native preparation and finer UI timing; the populated native failure and passing repeats are retained there. The measurements and unresolved first-open report below describe the earlier crate-focused stage, not the latest inventory result.

## What Changed

- Crate breakup snapshots eligible support collider bounds once per broken crate instead of recomputing every support for every fragment. The snapshot is rebuilt for the next crate after the previous source is hidden. Source/disabled/non-solid exclusions, support heights, debris colour, lifetime, motion and rewards are unchanged.
- Inventory/storage/current-level item icons are queued when the existing loading overlay opens. At most one queued image is attempted per loading Update, with at most 64 distinct paths and a 16 MiB resident-image-payload admission budget. Weapon/ammo icons are excluded. Cached bindings are reused. Missing/over-budget images retain normal on-demand loading.
- Icon prewarming does not extend the overlay, run in F2, mutate inventory, or delay readiness. This budget covers cached ImGui image payload, not total VRAM, driver overhead, CPU decoding or a hard millisecond bound. Normal on-demand loading may exceed it. There is no new eviction policy; later levels can exhaust this session-wide preload allowance.
- Opt-in frame reports record input pumping, six host stages, main-thread allocations by stage, collection counts and GC pause deltas. Reports are written after sampling, and hitch console output is deferred until the run ends.
- A fresh-process comparison runner prepares the package once in its parent, then runs source/package trials in separate children. This removes in-process package cooking/validation heap history from the measured game. It does not clear OS file caches or prove package preparation caused the earlier failures.

No authored levels/models, shaders, visual quality, physics stepping, simulation clamp, save formats, RE:Dox integration or ordinary launch selections changed. The texture-mip gallery remains isolated.

## Playtest

Use `LaunchLoadingTest.bat` for the packaged twelve-room atmosphere level, or `LaunchAtmosphereTest.bat` for the source version. Walk with the flashlight, break crates, hold/release a prop with the gravity gun, and open/close inventory. No new import or configuration is needed. The rendering should look the same.

For repeatable diagnostics from the repository root:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-hitch-stability
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll profile-gameplay-hitches
```

Run GPU tests sequentially on the normal desktop without building or running other GPU benchmarks concurrently. The suite opens and closes seven game windows automatically: source/package/package/source/source/package with torch on, then package with torch off. Each run uses 120 warmup frames and 300 measured intervals, real Update/physics/presentation, six-crate destruction, gravity holding/release and inventory transitions. These are short repeated tests, not a long playthrough or soak.

Reports are under `Tools/LevelAuthoring/bin/Release/net10.0/qa/hitch-suite-<UTC>/`:

- `summary.json`: mode, exit status, frame mean/P95/hitch count and report location; failures are retained, not discarded. Missing/incomplete reports fail the suite. Child timeout is 120 seconds.
- `<run>.json`: frame trace including warmup, zero-based frame IDs and stage names.
- `<run>.gameplay.json`: chronological measured wall intervals, timed stress events, streaming waits, peak fragments and package hit/fallback counts.
- `<run>.log`: console output and errors, including failed performance gates.

The wall interval recorded at Update N includes the preceding frame's work. In the 300-interval gameplay report, interval index zero corresponds to preceding frame 119. Stress events are at frames 130/150/170/210/250. Stage times are CPU/host timing, including blocking in Submit/Present, **not GPU timestamps**. GC collection/pause deltas are process-wide; allocations are for the main thread only. Worker allocations, driver memory and JIT time are not separately attributed. Report collection itself adds small allocations.

Ordinary play does not record these traces. For an explicit capture, set both `HS2_PROFILE_FRAMES` (1-10000 measured frames after 120 warmup) and `HS2_FRAME_REPORT` (JSON path). Profiling auto-exits after its sample target. Closing early does not produce a completed trace. The existing startup reports separately include `startup-ui-image-prewarm` scopes when an image is attempted during their recording window; no new first-playable speedup is claimed here.

## Evidence

Release, RTX 3060, D3D11, actual client 1920x1009, VSync, warmed OS files, fresh process per run. Original gates are unchanged: mean <=17 ms, P95 <=21 ms, at most one interval >33.3 ms, none >100 ms, no streaming waits. Approximately 60 FPS is not a guaranteed minimum 60 FPS.

The instrumented same-process package run and fresh-process baseline both locate expensive work around crate destruction and first inventory opening. The seven-run fresh-process **baseline already passes** before the optimization. The prior two failed package trials remain historical failures; there is not enough evidence to blame packaging alone.

Baseline: `qa/hitch-suite-20261027-113253/`. Post-change: `qa/hitch-suite-20261027-113857/`. Across the six torch-on samples, median Update time on the six-crate break frame falls **21.98 -> 15.23 ms** (about 31%). Median timed break-action work falls **19.56 -> 12.76 ms**. This is event CPU work, not a whole-frame/FPS or startup improvement. Debris peaks at 120 in each run.

| Post-change mode | Mean ms | P95 ms | Intervals >33.3 ms |
| --- | ---: | ---: | ---: |
| Source 0 | 16.551 | 18.083 | 0 |
| Package 1 | 16.552 | 17.729 | 0 |
| Package 2 | 16.773 | 17.852 | 0 |
| Source 3 | 16.774 | 18.442 | 1 |
| Source 4 | 16.772 | 17.793 | 0 |
| Package 5 | 16.771 | 17.686 | 0 |
| Package, torch off | 16.770 | 18.268 | 0 |

All seven pass; the remaining source hitch is **34.195 ms**. No interval exceeds 100 ms and no streaming waits occur. Every packaged trial has 30 model hits / zero fallbacks. Every run contains 420 trace rows and 300 measured intervals.

Final seven-run repeat with the completed support/query regression checks and report validation also passes: `qa/hitch-suite-20261027-115149/`, mean **16.656-16.722 ms**, P95 **18.120-18.615 ms**, zero intervals >33.3 ms. This does not erase the first repeat's 34.195 ms hitch. These two post-change suites contain 4,200 measured intervals in total, spread across fresh processes, not a continuous long soak.

The first inventory opening remains expensive: post-change torch-on runs have approximately **10-12 ms Update plus 11-15 ms UI work** on that event. Those event frames show no GC pause. The synthetic stress fixture introduces DamagedCable only when opening inventory, so it was not in the level/inventory preload queue. This is intentionally not disguised by changing the fixture or claiming the first-open hitch fixed. Separately validated preloading reuses the real 256x256 cable icon's 262,144-byte GPU allocation; the remaining document/layout/JIT/first-use costs still need finer measurement.

669 focused assertions pass: 400 old-versus-cached support queries with rotations/exclusions and consecutive breaks; negative/empty supports; image discovery/deduplication/budget/missing-file/reuse, one attempt per frame and F2/loading gating; unchanged inventory/overlay; and opt-in frame-report schema/warmup/completion. Synthetic timing in the schema test is not a game-performance result. All 15 historical Game/F2/HS2Editor rendering references remain within 1/255 without replacing baselines. Existing inventory input tests pass, including cable combine, keyboard/controller, quick clicks, native/fallback overflow drag and ammo crafting outside inventory.

Inventory visual checks pass at 800x600, 1300x775 and 1920x1080, including the backdrop and all 64 cell backgrounds/borders. Existing collision-cache checks pass 400 contacts/rays and exact source comparison for five real static mesh colliders, including invalid-cache fallback. Release solution and Debug/Release authoring builds have zero warnings/errors. Git diff whitespace validation passes.

## Still Open

1. First inventory document/layout/render profiling and populated-save/recipe-item fixtures are now implemented in the [inventory follow-up](INVENTORY_PERFORMANCE_GUIDE.md). Measured cases pass after native preparation; longer sessions and arbitrary late/new items remain open. This is not separate JIT attribution or a claim to eliminate every UI stall.
2. Longer full-game traversal/destruction/save-load sessions and manual acceptance. Keep every failed sample and unchanged frame budgets.
3. Re-measure startup's actual critical path and manually collected post-restart candidates. Worker timing sums are not additive wall-time savings; no reboot or OS cache purge is automated.
4. Texture-granularity replacement and projected-size/room-aware demand, then a gated real-level mip trial. Do not switch normal gameplay/editor to the gallery's whole-model replacement path.
5. Spatial audio and later research systems remain separate milestones in the technology roadmap.

# Gameplay Stability And Startup Follow-Up

Implemented 28 October 2026. This is the next acceptance/profiling pass after
[inventory preparation](INVENTORY_PERFORMANCE_GUIDE.md), not an engine rewrite
or a rollout of adaptive texture streaming. C# remains the runtime. Normal
texture resolution, lighting, collision behaviour and source levels are unchanged.

## Delivered

- A repeatable real-game runner covers populated inventory, overflow returning
  to the world, gravity hold/release, flashlight on/off, crate destruction,
  debris expiry, atomic RE:Dox save/load and F5 level reload.
- Six cycles exercise 7,200 rendered measurement frames after 120 warmup frames,
  about two minutes per run at 60 Hz. Each cycle saves, changes inventory/health,
  restores them, reloads the level and verifies saved broken-object state and
  unique live entity IDs. Immediate dropped-item state is checked after loading.
- Atmosphere tests use real W/S movement plus scripted room teleports. The
  separate `--puzzles` route uses `sixRoomShadowTest.json`, returns to spawn each
  cycle and asserts that existing rolling doors actually move. It supplies solved
  puzzle state; this is not an automated playthrough of puzzle input/solutions.
- Test saves are isolated below the report's `Saves` folder. Ordinary game saves
  still use the existing executable-side `Saves` directory. No source level reset,
  player save deletion or save-format change is performed.
- Save-slot summaries are no longer read/deserialized while both save/load
  menus are hidden (including F2). Open menus retain fresh reads, including
  external changes, missing and corrupt files; no stale metadata cache was added.
- Nested RE:Dox save serializer contracts are exercised once during existing
  loading frames using a small disposable DTO. No live state is captured and no
  file is written. Atomic saving, backups and `Flush(true)` stay unchanged. This
  moves first-use serialization work earlier; it does not eliminate disk latency
  or imply a total-startup improvement.
- Optional startup reports now record same-frame `HostMilliseconds` for update,
  physics, ImGui, world, overlay and submit/present. Physics subscopes identify
  body stepping, runtime collider construction, dynamic pairs and player movement.
  Existing `RawMilliseconds` describes the preceding frame interval. These are
  host CPU timings, not GPU timestamps; nested/worker totals are not additive.

## Run It

`RunGameplayStabilityTests.bat` builds Release, runs focused checks, then executes
native atmosphere, fallback atmosphere and native six-room puzzle tests sequentially.
Allow roughly seven minutes and leave the test windows focused/unobstructed.
It stops on failure and retains reports. `LaunchAtmosphereTest.bat` remains the
ordinary interactive playtest; there is no new import or editor setting to enable.

Individual commands, after building `Tools/LevelAuthoring/LevelAuthoring.csproj`:

```powershell
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-gameplay-stability
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll soak-gameplay --native-ui
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll soak-gameplay
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll soak-gameplay --native-ui --puzzles
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-loading
```

Use `--quick` for one cycle, not for long-run acceptance. Native runs require the
existing `HS2RmlUiBridge.dll` and fail if presentation falls back. Fallback runs
deliberately select the ImGui path; neither changes the production backend default.

Reports are under `Tools/LevelAuthoring/bin/Release/net10.0/qa/gameplay-soak-*`:
`frames.json` contains host stage/allocation/GC data; `summary.json` retains wall
intervals, action timings, loading transitions, memory samples and any failure.
There are 7,199 complete inter-update intervals for 7,200 measured rendered frames;
the final present has no following update. Warmup intervals are excluded.
`SaveTimings` separates the real game save, its nested persistence call and the
test-only assertion snapshots. Test-only JSON contracts prepare before sampling;
the real first durable save remains inside the timed gameplay window.

## Acceptance And Limits

The existing gameplay limits remain mean <=17 ms, P95 <=21 ms, no >100 ms interval
and at most one >33.3 ms interval per 300 gameplay samples. These mean approximately
60 FPS, not a guaranteed minimum of 60. Explicit save-load/F5 loading intervals
remain in reports but are evaluated separately (15-second readiness guard).
Ordinary saving and inventory opening are **not** excluded from gameplay timing.

Settled samples require process-private growth <=128 MiB and resident model-payload
growth <=8 MiB from the first completed cycle to the last. These predeclared
diagnostic guards are not a total-memory cap or proof of leak freedom. Process
memory includes managed heap retention, JIT and diagnostic traces. A one-cycle
run cannot establish a trend. Longer manual sessions remain necessary.

## Initial Evidence

All measurements below use this machine's RTX 3060 / D3D11 Release build,
1920x1009 client area. Builds and other GPU tests are kept outside timed runs.

Before the save-menu change, the native atmosphere six-cycle run passed at
16.671 ms mean / 18.435 ms P95, with two >33.3 ms intervals and none >100 ms;
settled process-private growth was 39.68 MiB. It did not test actual doors because
the atmosphere fixture has none. A subsequently added movement assertion correctly
rejected that fixture; the failed `gameplay-soak-20261028-083959-990-native` report
is retained and the explicit six-room route addresses the coverage gap.

The initial one-cycle baseline recorded a 93.44 ms first-save frame. The synchronous
durable save/flush contract is preserved; this pass does not claim hitch-free saves.
Hidden save-slot parsing averaged 0.1925 ms/frame in that baseline and peaked at
3.2377 ms. Focused regression checks verify that this scope is absent with menus
closed, while menu contents still refresh correctly.

Two later fallback runs failed the unchanged >100 ms gameplay gate on the first
save frame: 102.50 ms (`084934-979`) and 101.68 ms (`085422-738`). Both reports are
retained. Finer timing in the second run separates **64.189 ms real game save**
(54.555 ms persistence) from **29.548 ms test-only snapshot serialization**.
There was no GC pause in the first failing frame. Do not attribute the entire
observed delay to the UI backend or disk.

The test now prepares its own assertion contracts before sampling, without
performing a warmup game save. The game independently prepares its save contracts
during loading. The one-cycle fallback repeat (`085739-075`) passes with real
first save **10.826 ms**, persistence **2.614 ms**, assertion snapshots **2.382 ms**,
and full save frame **20.06 ms**. Mean/P95 is **16.660/18.198 ms**, no >33.3 ms.
This is a first-use preparation result, not a guaranteed upper bound on disk I/O.

The first startup comparison (`LoadingTestPackages/F2054EEB70EE2390C40E6239/qa/20261028-082247`)
recorded source/package medians of 3100.6/3024.4 ms after one warmup per mode.
All were fresh processes with warmed/uncontrolled OS file caches, not cold-disk tests.

The more detailed package probe `qa/startup-physics-20261028.json` took 3376.2 ms:
resources were ready at 2713.7 ms; physics then took 85.9/250.8/256.6 ms across
three early frames. Body stepping accounts for 381.9 ms and dynamic pairs for
140.2 ms of the 594.6 ms physics total. The existing fixed-step catch-up contributes
to the later frame cost. No time-step clamp, loading duration or readiness endpoint
was weakened to hide this work. One instrumented sample is not a speed comparison.

## Final Validation

Final six-cycle repeats all pass the original gameplay limits (21,600 rendered
measurement frames total, 216 scripted actions, 36 explicit load/reload transitions).
All have zero >33.3 ms gameplay intervals, zero >100 ms gameplay intervals,
40 peak debris pieces, zero model-payload growth and zero hidden save-summary work.

| Route | Mean / P95 gameplay ms | Max gameplay ms | First real save ms | Process-private growth MiB | Report suffix |
| --- | --- | --- | --- | --- | --- |
| Native atmosphere | 16.653 / 18.250 | 25.631 | 10.192 | +20.08 | `090059-526-native` |
| Fallback atmosphere | 16.658 / 18.301 | 23.305 | 10.325 | +30.38 | `090306-316-fallback` |
| Native six-room doors | 16.663 / 18.320 | 28.058 | 10.182 | -36.04 | `090512-713-native` |

Full directory names start `gameplay-soak-20261028-`. All-frame host means,
including explicit loading, are 16.72/16.71/16.86 ms. Loading intervals are **not**
hitch-free: maxima 74.86/71.70/292.99 ms respectively. Readiness transitions stayed
under the 15-second guard; longest observed initial readiness in the six-room run
was about 4.0 seconds. Memory results are guard checks, not proof of no leaks.

24 startup/save-menu checks, 392 persistence/inventory checks and 670 hitch
assertions pass. Native/fallback inventory input/combine/overflow, native visual
checks at three resolutions and all five puzzle chains/open-close collision routes
pass. All 15 historical Game/F2/HS2Editor reference images remain within 1/255;
references were not replaced.

Final Release solution and Debug/Release authoring builds pass with zero warnings
or errors. Whitespace validation passes. The stability BAT runs the same verified
commands sequentially and stops at the first failed check.

Final fresh-process startup reports are in package QA folder `20261028-090741`.
Each mode discards one warmup and retains three samples:

| Mode | Earlier median ms | Final median ms |
| --- | --- | --- |
| Source | 3100.6 | 3251.2 |
| Immutable package | 3024.4 | 3333.4 |

Startup is **not faster** in this comparison. Save-contract preparation averages
66.0/66.1 ms inside loading; physics/world preparation and scheduling also vary.
The total before/after difference must not all be attributed to that one scope.
This trades earlier preparation for reduced first-save work, preserving the real
first-playable endpoint. No cold-disk measurement was performed. The ordinary
`Game.exe` apphost startup smoke also succeeds at 3393.4 ms
(`qa/startup-apphost-20261028.json`), not a matched benchmark.

## Next Work

1. First physics profiling and behaviour-preserving geometry/local-support reuse
   are now implemented. See the [follow-up](PHYSICS_STARTUP_GUIDE.md) for exact
   replay comparisons and startup measurements; the results above are historical.
2. Collect manually prepared post-restart loading candidates and longer playtests;
   do not infer cold-load speed or leak freedom from these short warm-cache runs.
3. Continue texture-granularity ownership, projected-size/room-aware mip demand
   and a separately gated real-level trial. Keep ready-before-replacement and
   full-detail restoration; ordinary Game/F2/HS2Editor stay full resolution.
4. Spatial audio follows as a separate horror-game milestone in the
   [technology roadmap](ENGINE_TECHNOLOGY_ROADMAP.md).

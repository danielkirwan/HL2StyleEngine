# Frame Pacing Investigation

Updated 2026-11-06. This follows the measured dynamic-buffer upload fix. C#, accepted lighting, texture quality, solver integration, fixed timestep and original acceptance limits are preserved. Adaptive streaming remains opt-in.

## Latest Acceptance And Trace

[The final recheck](LIGHTING_ACCEPTANCE_RECHECK.md) records passing exact BATs
and three post-correction sequential untraced long controls (zero long-run gameplay
intervals over 33.3 ms, 60.68-106.91 MiB growth). The warm lighting-state correction
also passes all 32 lighting checks and final targeted graphics validation.

Version 5 opt-in profiles add QPC boundaries and native process/thread identity.
The [approved scheduling capture](SCHEDULING_TRACE_GUIDE.md) has zero lost events,
complete selected-frame coverage and no recurrence of severe gameplay stalls.
Its largest physics wait matches a loading-phase GC pause, not the historical
no-GC stall. Raw ETL is retained locally. It is not an untraced performance pass
or proof of a driver/solver cause. **Gate B is accepted with explicit residual
risk**, following the user's 6 November decision to move on while keeping the
old issue under observation. The historical severe stall remains open/not
reproduced, not fixed. Re-run the same BATs/long controls for audio changes; if
it returns, retain the failure and correlate a new bounded trace with that
event before advancing. No always-on recorder or relaxed limits. Loading/save
frames remain separate (up to 364.56 ms in the last six-room run). Older pending
statuses, memory failures and stall reports below describe their original rounds.

## New Diagnostics

### Allocation Correction Pass (2026-11-06)

Diffuse payload/atlas reuse and UI staging are implemented in C# without
changing GPU shaders, full-detail quality, VSync, physics or acceptance limits.
[Validation](ENGINE_UPGRADE_VALIDATION.md) records each stage independently.
The diffuse-only unpaused 18-cycle route passes gameplay pacing at
16.666/16.811 ms mean/P95, zero >33.3 ms, but fails memory at 128.05 MiB.
This does not solve the historical severe intermittent full-detail stalls.
Paused address-space matrices remain diagnostics, not pacing acceptance.

With both fixes, the unpaused 18-cycle repeat passes at **80.55 MiB** process
growth, zero asset growth, **16.666/17.990 ms** gameplay mean/P95 and zero
intervals >33.3 ms (`qa/gameplay-soak-20261106-105655-761-native`). No forced
GC or intrusive probes, normal full-detail/VSync. This does not erase or
explain earlier rare full-detail stalls; broader and repeated controls remain.

The fresh six-cycle repeat also passes with **50.46 MiB** growth, zero asset
growth and **16.661/17.769 ms** gameplay mean/P95, zero >33.3 ms:
`qa/gameplay-soak-20261106-110421-568-native`. It uses the same unpaused full-detail
configuration without `--long`. Both runs preserve the normal frame recorder
and loading intervals. Full BAT/manual acceptance is not claimed in this pass.

### Address-Space Follow-Up (2026-11-06)

The [memory attribution round](RELOAD_MEMORY_INVESTIGATION.md) uses three paused
checkpoints per diagnostic run. These intentionally fail the original frame
guard; they cannot be counted as pacing acceptance or silently filtered out.
The subsequent normal-desktop six-cycle control disables those probes and
passes at **16.667/16.793 ms** gameplay mean/P95, zero >33.3 ms, **101 MiB** private
growth (`qa/gameplay-soak-20261106-095755-199-native`). No production correction
was made, so this pass demonstrates variability, not a stall or memory fix.
The long diagnostic still exceeds memory at 139.52 MiB. Historical severe
full-detail stalls remain unresolved, independently of the new CLR attribution.

### Reload Investigation Follow-Up (2026-11-05)

The [memory investigation](RELOAD_MEMORY_INVESTIGATION.md) reproduces the guard
failure with and without new memory probes. Its long instrumented controls
have zero gameplay intervals over 33.3 ms; the native reload still fails memory.
The final normal-desktop, probe-disabled six-cycle control passes gameplay
pacing at 16.666/16.812 ms mean/P95 but fails memory at 134.27 MiB. This does
not explain or clear the historical severe intermittent full-detail stalls.

A preceding restricted launch of the same command reports 36.925/40.967 ms
gameplay timing and 29.95 ms mean host presentation, despite focused frames,
normal VSync and graphics debug off. It is retained separately in
`qa/gameplay-soak-20261105-201246-094-native/`; the normal-desktop repeat is
`qa/gameplay-soak-20261105-201804-227-native/`. The launch-context comparison
does not establish the cause of older normal-desktop stalls. No scheduling
trace, quality reduction, GC workaround or physics change is claimed.

### Latest Lighting Handover Repeats (2026-11-04)

Later clean repeat after the native ImGui layout correction:
`qa/lighting-acceptance-20261104-154729-227-Baseline` passes the exact short
frame-pacing BAT. The gameplay BAT instead fails six-room memory growth at
**142.51 MiB**, with no gameplay interval over 33.3 ms. The fresh long
full-detail report `qa/gameplay-soak-20261104-155834-340-native` passes:
18 cycles/216 actions, **16.662/17.417 ms** mean/P95, zero >33.3/>100 ms,
**73.18 MiB** private growth. These passes do not explain the historical
intermittent stalls below. No OS scheduling trace or demonstrated stall fix
is claimed; Gate B stays pending. An earlier overlapped attempt at 15:45 is
explicitly invalid and excluded in the handover.

Final clean long repeat adds streaming **16.661/17.418 ms**, zero >33.3/>100 ms,
**+52.72 MiB**, pass (`qa/gameplay-soak-20261104-160442-442-native`), and native
six-room **16.661/16.924 ms**, zero >33.3/>100 ms, **+153.75 MiB**, memory fail
(`161050-816-native`). Both finish 18 cycles/216 actions. Full-detail and
streaming GPU coverage is complete (21,600 valid each, zero pending/skipped/
invalid); offline analyses are retained beside both traces. Severe stalls
do not recur in this repeat, but no stall-specific cause/fix is demonstrated.
The baseline gate remains open on memory and unresolved intermittent history.

The earlier same-day evidence remains retained:

The [lighting acceptance handover](LIGHTING_ACCEPTANCE_HANDOVER.md) reruns the exact gameplay/frame-pacing BATs and three sequential long controls, with normal VSync, full-detail reference quality and unchanged limits. The gameplay BAT passes, but the frame-pacing BAT fails its short full-detail control with two >33.3 ms intervals. A separate short streaming run passes; it does not erase the failure.

All three 18-cycle/216-action long routes fail frame acceptance: full-detail package **16.702/17.127 ms mean/P95, 13 >33.3 ms / 1 >100 ms**; streaming package **16.679/17.472 ms, 11 / 0**; native six-room puzzle **16.675/17.703 ms, 5 / 0**. Reports are `qa/gameplay-soak-20261104-143650-840-native`, `144300-366-native` and `144909-753-native`. The six-room private-memory growth is also over its 128 MiB guard at **+140.38 MiB** (zero model growth); its frame assertion throws before the memory assertion, so do not label memory accepted. Package growth is +32.20/+64.45 MiB. Zero old tracked entities/meshes survive post-run diagnostic collection, which does not prove overall leak freedom.

Full-detail frame 20600 records **270.839 ms host work / 270.911 ms interval**, including 132.454 ms physics wall time over three ticks, 25,490,020 physics execution cycles, 5.125 ms GPU interval and no GC pause. It is unfocused; the earlier severe focused repeat remains relevant. All 21,600 measured GPU samples are valid with no pending/skipped/invalid queries. The retained `pacing-analysis.json` is generated only after performance runs finish. The exact source of waiting/descheduling remains unknown. **Gate B pending**, including both pacing and the latest memory excess; no quality/solver/timestep/limit changes are authorized to hide them.

### Diagnostic Format

Version 4 frame reports retain the previous host/render fields and version 3 additions: physics substages/call counts, executed fixed ticks, fixed-step remainder, raw preceding-frame interval, clamped simulation delta and window focus. A long physics frame can now be distinguished from several ordinary catch-up ticks after an earlier stall. Fixed tick counts include callbacks that return early while gameplay is paused; stage call counts identify actual body/player work. Support/rotation timings can be nested inside body work; do not sum nested timings as independent work.

Version 4 additionally records current-thread CPU milliseconds and raw cycles at host-stage boundaries, only during explicit report capture. Unsupported/failed counters are null, not zero. [GetThreadTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes) measures kernel/user execution, with quantized results rather than per-stage stopwatch precision. [QueryThreadCycleTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-querythreadcycletime) provides execution cycles; these are deliberately not converted to milliseconds. Wall-minus-CPU is not an exact scheduling-wait measurement. Coarse boundaries avoid native calls per body/support query. No process priority, affinity or system power settings are changed.

An opt-in D3D11 GPU trace uses 16 reusable timestamp/disjoint-query slots. It polls earlier frames with `DoNotFlush`, never waits, never calls `WaitForIdle`, and skips samples rather than blocking if all slots are busy. Samples are bounded to the requested capture limit, associated with the originating frame ID and rejected if disjoint/invalid. Final pending/skipped/invalid counts are reported, not hidden. No GPU queries run during ordinary gameplay.

The timestamp interval surrounds submitted command-list execution, excluding earlier immediate uploads and Present itself. It is elapsed GPU-timeline time, **not a pure GPU-busy counter**: queue dependencies, clock changes and presentation-related scheduling can affect it. Do not subtract it from CPU Present time or assume their sum is whole-frame cost. The [D3D11 query contract](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_query) requires a valid timestamp/disjoint pair; [GetData](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-getdata) can return not-ready, which this implementation defers to a later frame.

`profile-frame-pacing` runs the existing isolated texture-level route with GPU/physics tracing. `--reference` keeps full texture detail, `--long` uses the existing 18-cycle native-inventory reload route, `--no-debug` disables D3D validation for that diagnostic process, and `--no-vsync` disables synchronization for a diagnostic only. These overrides require an explicit frame profile; ordinary launcher settings are not changed by these switches. All test summaries retain the original frame/memory gates and source levels/saves remain isolated.

`analyze-frame-pacing <frames.json>` writes `pacing-analysis.json` beside a version 3 or newer trace. It joins GPU samples by frame ID, reports coverage and worst frames with fixed ticks, physics stage, Present, GC and available thread CPU/cycle counters. Host work excludes inter-frame gaps; original route wall-interval summaries remain the acceptance authority.

## Release Device Policy

Release builds now request a normal D3D11 device, instead of always enabling API debug validation. Debug builds retain validation. `HS2_PROFILE_DEBUG=1` explicitly restores the old validation-enabled path during an opted-in capture; `profile-frame-pacing` selects that control unless `--no-debug` is supplied. VSync remains on by default in both configurations. This changes validation overhead, not shading, draw ranges, assets, physics or synchronization policy. It is not presented as a proven cure for the historical stalls.

## Initial Controls

Release / RTX 3060 / D3D11 / 1920x1009 desktop, sequential processes, warm uncontrolled filesystem cache. The first two 3,600-frame adaptive routes both pass their original gates. The default debug-on run has 12.387ms mean GPU command interval and 1.714ms mean physics; disabling D3D debug validation reduces measured host World mean from 3.96 to 3.42ms in this pair, while total frame mean remains about 16.65ms. This single pair is not proof of a hitch fix or a GPU speedup. Reports: `qa/texture-level-20261104-102235-976` and `102510-917`.

The uncapped, debug-off full-detail diagnostic has 1.465ms mean GPU command interval, 0.059ms mean Present and 1.816ms mean physics per executed tick (`qa/texture-level-20261104-102707-256`). It executes far fewer simulation ticks per rendered frame and covers less real time, so **its 331 FPS host mean is not a like-for-like gameplay improvement**. These controls support investigating synchronization/scheduling before reducing visual quality; they do not establish the root cause of the historical >100ms stalls. All three collect 3,600 valid measured GPU samples with no invalid, pending or skipped queries.

## Long Controls And Regressions

All `qa/...` paths here are under `Tools/LevelAuthoring/bin/Release/net10.0/`. Long runs use the existing 18 cycles, 216 actions and 21,600 measured frames, native inventory, immutable atmosphere package, isolated saves, full-detail settled checkpoints and unchanged frame/memory gates. Loading intervals remain separate. No forced collection is used.

| Configuration | Gameplay mean / P95 | >33.3ms / >100ms | Private growth | Result |
| --- | --- | --- | --- | --- |
| Streaming, D3D validation on | 16.667 / 17.859 ms | 1 / 0 | +78.91 MiB | Pass |
| Streaming, D3D validation off | 16.666 / 17.437 ms | 0 / 0 | +101.25 MiB | Pass |
| Full detail, D3D validation off | 16.683 / 17.772 ms | 7 / 1 | +98.98 MiB | Frame gate failed |
| Full detail, validation off, thread counters | 16.689 / 17.364 ms | 9 / 1 | +109.17 MiB | Frame gate failed |

Reports: `qa/gameplay-soak-20261104-102935-016-native` and `103920-587-native`. Each has 21,600 valid measured GPU results and zero invalid/pending/skipped queries. Host World mean falls **3.83 -> 3.29ms**, consistent with the short comparison's 0.54ms reduction. This is measured CPU headroom, not a GPU speedup or guaranteed minimum FPS. Both original memory guards pass; no memory reduction is claimed. The longer validation-off run includes periods without window focus, so OS/desktop conditions are not perfectly controlled.

In the validation-on run, frame 15392 is the smaller gameplay hitch: **31.528ms Present**, **34.446ms GPU command interval**, **1.566ms physics**, one fixed tick, no GC pause and window focused. This rules against physics catch-up/GC as the source of that particular spike, but does not distinguish GPU execution, queue/resource dependencies, driver scheduling or presentation throttling. Other worst host frames are marked loading by the original route. The analyzer now includes that phase classification and lists worst gameplay frames separately when a matching soak summary is available.

The subsequent full-detail control (`qa/gameplay-soak-20261104-105022-176-native`) **fails** and reproduces a severe CPU-side stall. Frame 21580 takes 104.970ms host work, including 75.084ms physics (72.609ms body simulation, two ticks), but only 0.150ms Present and a 4.453ms GPU interval, with no GC pause. The following frame executes six catch-up callbacks. Other late frames stall in support/rotation or unrelated rendering preparation despite normal body-call counts. The window is unfocused in these samples, which alone does not identify a cause. All 21,600 measured GPU samples are valid with no pending/skipped queries. Memory stays within its guard, but the overall run fails. This is why version 4 adds thread execution counters before any solver changes. Do not claim the intermittent issue is fixed or change the solver/timestep to hide it.

The version 4 repeat (`qa/gameplay-soak-20261104-110534-300-native`) also **fails**, with all 18 cycles/216 actions completed and all 21,600 measured GPU samples valid (zero pending/skipped/invalid). The worst host frame is 119.779ms; physics is 77.100ms over four catch-up ticks, GPU interval 5.859ms, Present 0.129ms and no GC pause. Crucially, frame 3384 has 30.370ms physics wall time but 9,008,660 execution cycles, versus the preceding frame's 2.182ms and 8,263,100 cycles, both one tick. The worst frame's physics uses 34,048,380 cycles over four ticks, not a wall-time-proportional workload increase. Quantized thread CPU measurements likewise do not account for the large wall delays. The game is **focused** during these failures, ruling out focus loss as a necessary trigger.

These results support waiting/descheduling rather than a correspondingly expensive physics calculation for those samples. They do not identify a particular driver, competing process, page fault or engine wait. The next diagnostic step is a bounded OS scheduling/wait trace correlated with these frame IDs, followed by a targeted fix and repeated unchanged full-detail/streaming acceptance. Do not replace that evidence with process-priority changes, reduced visual quality or dropped physics ticks. Step 1's stability acceptance remains open.

Focused validation:

- Disabled render/physics scopes are allocation-free; stage attribution, detached snapshots, reset and unchanged fractional/catch-up fixed ticks pass in Release/Debug. Thread counter tests also pass in both builds: sleep is not treated as CPU work, execution increments cycles, known differences are exact, and missing/cross-thread/reversed samples are unavailable. The analyzer reads both version 3 and version 4 captures.
- GPU lifetime tests pass in Release/Debug: 16-slot saturation skips instead of blocking, source-frame identity, valid disjoint-checked results, reuse, bounded sample count, nested-request rejection, pending disposal and reconfiguration. Actual D3D flags match the requested build policy.
- **1,200** saved replay frames match state/contact/bounds hashes exactly, across atmosphere and six-room shadow levels. Reports: `qa/physics-replay-20261104-104712-907` and `104732-603`.
- **15** historical Game/F2/HS2Editor images pass within **1/255**, without baseline changes. Release solution and Release/Debug authoring builds pass without warnings/errors.

Historical failed runs remain in [RENDER_UPLOAD_STABILITY_GUIDE.md](RENDER_UPLOAD_STABILITY_GUIDE.md); later passes do not erase them. Continue scheduling/wait diagnosis and manual/repeat pacing acceptance before promoting streaming. This step adds diagnostics and removes unnecessary Release validation work; it is not a declaration that all rare stalls are solved. No audio work is included here.

## Reproduce

`RunFramePacingTests.bat` builds Release, checks diagnostic lifetime/accounting and runs short full-detail/streaming controls with VSync on and graphics validation off. It uses isolated QA saves and stops on a failed original gate. Allow the automated route to run without interacting with the window; focus is recorded, not forced. Normal play remains `LaunchSixRoomShadowTest.bat`, with diagnostics off.

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release and run its DLL sequentially:

```text
validate-render-profile
check-gpu-frame-profile
profile-frame-pacing
profile-frame-pacing --no-debug
profile-frame-pacing --reference --no-debug --no-vsync
profile-frame-pacing --long
profile-frame-pacing --long --reference
profile-frame-pacing --long --no-debug
profile-frame-pacing --long --reference --no-debug
analyze-frame-pacing <qa-run>/frames.json
```

Never use uncapped averages, forced collections, dropped physics ticks or lower lighting settings to pass the normal 60-FPS acceptance workload.

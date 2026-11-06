# Lighting Cache Memory Follow-Up

Final acceptance (2026-11-06): [full recheck](LIGHTING_ACCEPTANCE_RECHECK.md).
Both final BATs and all three post-correction untraced long controls pass; long
private growth is 60.68/90.93/106.91 MiB against the unchanged 128 MiB guard.
78 reload/lifetime checks, all 32 lighting commands, final graphics validation
and native review support acceptance. **Gates L/B complete**, with the user's
explicit acceptance of the separate historical, non-reproduced stall risk.
Memory correction is accepted for these workloads, not unlimited-session leak
freedom. Preserve the approximately 21 MiB remaining guard margin in the native
long route when adding audio; no raised limits or forced GC. Earlier pending
statuses below are historical.

Correction update (2026-11-06): [diffuse reuse and UI staging](MEMORY_GROWTH_FIX_PLAN.md)
are now implemented and measured separately. Unchanged diffuse reads allocate
384 bytes instead of approximately 50.6 MB; the 64-command UI fixture falls
from 72,976 to 653 bytes/frame. Cache invalidation/cancellation, GPU lifetime,
lighting states and original inventory pixels remain checked. These are
component allocation results, not proof that total process growth is fixed.
The diffuse-only unpaused long run still fails the 128 MiB guard at 128.05 MiB.
Combined unpaused 18-/6-cycle repeats pass at 80.55/50.46 MiB growth, zero asset
growth and zero gameplay intervals >33.3 ms. See [latest whole-process results](ENGINE_UPGRADE_VALIDATION.md);
broader/repeated/manual acceptance and historical stalls remain open, so Gate B
is pending. Historical diagnostic-only statements below precede these fixes.

Attribution follow-up (2026-11-06): [paused OS/CLR snapshots and GPU usage](RELOAD_MEMORY_INVESTIGATION.md)
account for the reproduced **139.52 MiB** long-run growth in CLR-owned capacity:
active regions +143.125 MiB, other CLR regions -2.375 MiB, unattributed private
pages -1.477 MiB. Short controls also identify reusable CLR free-region pools.
No tracked old entities/meshes survive post-timing collection. This narrows
the earlier native/driver ambiguity without claiming universal leak freedom.
[Proposed correction order and acceptance](MEMORY_GROWTH_FIX_PLAN.md): large
lighting reload allocation first, UI staging reuse separately, unchanged tests.
No production fix was applied in that attribution-only round; Gate B remains pending.

Investigation refresh (2026-11-05): [controlled runs and heap comparisons](RELOAD_MEMORY_INVESTIGATION.md)
reproduce **154.34 MiB** native six-room private-memory growth. Managed commitment
grows **151.57 MiB** and fragmentation **125.86 MiB**, with flat asset payloads.
Separate forced-collection snapshots show bounded level/reflection/lightmap
counts; much of their smaller live-object growth is retained test history.
This points toward allocation churn and GC heap capacity, not evidence of
ever-growing model copies. It is not proof of native leak freedom. No production
allocation fix or GC-policy change is included yet; Gate B remains pending.

A later six-cycle normal-desktop repeat with the new probes disabled also
fails at **134.27 MiB**, while reported GC commitment falls. The long run's
managed-commit correlation is therefore not the whole explanation; synchronized
GC/process mapping and native/driver ownership remain open in the linked report.

Acceptance refresh (2026-11-04): [current handover and long-run evidence](LIGHTING_ACCEPTANCE_HANDOVER.md). Cache ownership/dependency checks and original memory guards are rerun without forced gameplay collection or changed limits. Passing memory samples are not proof of leak freedom or clearance of the separate frame-pacing gate.

The earlier native six-room 18-cycle sample grows **140.38 MiB**, exceeding the 128 MiB private-memory guard, with zero model-payload growth. Its frame assertion fails first; that is not a memory pass. Post-run diagnostic collection finds zero old tracked entities/meshes alive, but private allocation/fragmentation/native ownership still require investigation. This does not revoke the historical allocation measurements below or establish a particular leak cause. Normal gameplay still does not force GC.

The clean follow-up six-cycle puzzle route also fails the guard at **142.51 MiB**
(`qa/gameplay-soak-20261104-155147-381-native`), despite zero gameplay intervals
over 33.3 ms and unchanged model payload. Both routes complete all scripted
actions. The later full-detail package 18-cycle control passes at **73.18 MiB**
(`155834-340-native`); a pass on that different scene does not close the
six-room reload issue. No memory-limit increase or forced gameplay GC is used.

The final native six-room 18-cycle repeat also fails: **+153.75 MiB** private,
zero model/texture payload growth, all 216 actions complete, no gameplay
interval >33.3 ms (`qa/gameplay-soak-20261104-161050-816-native`). Between its
settled samples, managed commitment grows 76.50 MiB and fragmentation 62.98 MiB,
while estimated managed bytes grow 3.45 MiB. These counters are not additive;
they do not explain all process growth. Post-timing collection leaves zero of
13,757 tracked old entities and 90 old meshes alive. Next: attribute reload
allocations/heap commitment and native reservations before choosing a fix.
Passing cache ownership tests or the separate streaming control (+52.72 MiB)
does not clear this failure.

Implemented 2026-11-02. This follows the long-reload memory failure in the
[texture streaming trial](TEXTURE_STREAMING_TRIAL_GUIDE.md), not a new lighting
model or a C++ migration. C# remains the runtime.

## Changes

- `LightmapFile.Load` reuses one exact-sized idle buffer for each of the
  compressed/decompressed roles and deserializes metadata from a span instead
  of copying it. Atomic borrowing gives concurrent reads separate buffers; only
  one returned buffer per role is retained, including after corrupt, stale or
  truncated input. Each allocation remains subject to the existing 64 MiB
  validation limit. Only owned pixel/probe arrays are published.
- `ReflectionEnvironmentData.Read` validates dimensions and exact payload length,
  reads directly into the final face/LUT arrays, and hashes incrementally. There
  is no duplicate full cubemap buffer. Partial reads/non-seekable streams work;
  the caller retains ownership of its stream.
- SHA-256, LZ4, finite-radiance, metadata, version and stale-key checks remain.
  Existing files are byte-compatible. No source level, asset, shader, save
  format, texture resolution, physics setting or acceptance limit was changed.
- Soak memory samples additionally distinguish lightmap GPU payload, reflection
  GPU payload, pending reflection CPU bytes and capture GPU bytes from model
  textures and managed/process memory. These are engine-owned payload counters,
  not a measurement of all driver/VRAM reservations.

Scratch reuse trades bounded retained capacity for less large-object-heap
churn; it does not make scratch memory disappear or establish a total RAM cap.
Idle scratch is bounded to two buffers (at most 128 MiB in total); concurrent
in-flight readers have separate validated buffers. The current six-room bake
uses **38,227,879 decoded + 4,092,505 compressed bytes**, about **40.36 MiB**.
The initial standard array-pool implementation retained 68 MiB due to power-of-two
rounding. The exact-sized follow-up avoids that unused capacity without changing
file data or limiting concurrent correctness.
Snapshot JSON clones, probe geometry, CPU pixels and graphics-driver memory
still have costs. No forced collection or LOH compaction is added to gameplay.

## Focused Evidence

Release, six-room scene, six warm iterations per operation, same machine.
`check-lighting-allocations` uses per-thread allocation counters, not GPU timers
or a cold filesystem test. Timings varied and do not establish a load speedup.

| Reader | Before bytes/load | After bytes/load | Reduction |
| --- | ---: | ---: | ---: |
| Lightmap | 97,588,317 | 50,594,234 | 48.2% |
| Reflection | 2,234,776 | 1,120,544 | 49.9% |

Reports under `Tools/LevelAuthoring/bin/Release/net10.0/qa/`:
`lighting-allocations-20261102-081717-361` (before),
`lighting-allocations-20261102-082228-988` (initial pooled implementation) and
`lighting-allocations-20261102-084422-015` (final exact-sized scratch).
Scene/diffuse/reflection JSON hashes, both dependency keys, lightmap pixels,
all reflection faces and LUT hashes match exactly. Source scenes are unchanged.

39 new ownership/reader checks pass, including concurrent differently sized
lightmaps, retained-result isolation, invalid LZ4, stale keys, truncation,
checksum failures, payload length limits, non-seekable short reads, trailing
data and checksummed non-finite reflection radiance. Existing 282 indirect-bake
and 98 reflection checks pass, including actual GPU sampling and fallback.
The bake suite needed an unrestricted rerun after sandbox fixture creation was
denied; that restricted attempt is not counted as a pass.

## Long Gameplay Acceptance

The initial pooled native-UI run `qa/gameplay-soak-20261102-082323-808-native` completes all 18
cycles / 216 actions and 21,600 measured frames. Gameplay intervals average
**16.670 ms**, P95 **17.125 ms**, with one >33.3 ms and none >100 ms. The
existing gameplay frame gates pass. Including explicit loading frames, host-stage
totals average 16.80 ms; these are different measurement populations.

**That initial memory gate fails:** process-private memory grows **165.69 MiB**
(1,083.29 -> 1,248.97 MiB), above the unchanged 128 MiB guard. The previous
run's +146.32 MiB failure is not superseded. This change reduces allocations,
not demonstrated overall RAM usage. Scratch pooling intentionally retains
capacity, and GC timing/fragmentation differs between runs.

Total allocated bytes at the last settled sample fall from approximately
9.43 GiB in the earlier native run to **8.42 GiB**. Model-plus-texture payload
stays **113,422,478 bytes**; reflection GPU payload stays **13,376,768 bytes**,
with no pending reflection CPU/capture payload at the settled samples. Lightmap
GPU payload fluctuates around 35-36 MiB as geometry bindings expire/reappear,
without an increasing trend. These counters do not prove driver leak freedom.
Post-timing diagnostic collection again finds zero surviving tracked old scene
entities and collider meshes. GC heap statistics describe the last collection,
not a synchronized breakdown of current process-private bytes.

The same initial implementation's fallback-UI comparison,
`qa/gameplay-soak-20261102-083526-382-fallback`, completes 18 cycles / 216
actions and passes: **16.668 ms mean / 18.062 ms P95**, two >33.3 ms in separate
accepted windows, none >100 ms, **+71.02 MiB** private growth
(1,017.92 -> 1,088.94 MiB). This makes native-UI allocation/retention worth
isolating, but one backend comparison does not establish a leak's cause.
Both runs precede the exact-sized scratch adjustment.

The final exact-sized implementation's first native repeat,
`qa/gameplay-soak-20261102-084452-857-native`, **passes both unchanged gates**:
18 cycles / 216 actions, **16.650 ms gameplay mean / 18.401 ms P95**, two
>33.3 ms in separate accepted windows and none >100 ms. Private growth is
**37.49 MiB** (1,051.50 -> 1,088.99 MiB); total allocated at the final sample
is 8.40 GiB. Model/texture/reflection payload stays flat, lightmap bindings stay
bounded, and post-timing old entity/mesh counts are zero. The lower final process total is an observed run result, not
a guaranteed saving on every level or proof that all memory issues are closed.

An unchanged final repeat, `qa/gameplay-soak-20261102-085510-907-native`, also
passes all 18 cycles / 216 actions: **16.660 ms gameplay mean / 18.098 ms P95**,
one >33.3 ms, none >100 ms, and **+52.02 MiB** private growth. Thus the final
version passes the original 128 MiB guard twice, without changing the first
settled sampling point, imposing GC during gameplay, or relaxing frame limits.
The failed earlier versions remain recorded above. These full-detail reload
runs do not substitute for long streaming-enabled trials or manual playtesting.

All 15 historical Game/F2/HS2Editor image comparisons pass within their existing
1/255 tolerance; no reference images were replaced. The final scratch adjustment
also passes the 282 bake checks and all eight groups of before/after keys/pixel
hashes. Release solution and Release/Debug authoring builds pass with zero
warnings/errors. The 24 texture ownership/restoration checks and 24
gameplay/startup regression checks also pass. Production texture streaming
remains opt-in pending broader memory and transition-quality acceptance.

## Test And Play

Normal playtesting still uses `LaunchSixRoomShadowTest.bat` or
`LaunchAtmosphereTest.bat`. No import, rebake, save reset or graphics-setting
change is needed. Use Release. The texture-only experiment remains separate:
`LaunchTextureStreamingLevelTest.bat` with F6 full-detail comparison.

Build Release `Tools/LevelAuthoring/LevelAuthoring.csproj`, then run its DLL:

```text
check-lighting-allocations
validate-indirect-bake
validate-reflections
soak-gameplay --native-ui --puzzles --long --memory-trace
```

Run desktop performance tests one at a time, without concurrent builds. The
long route contains 18 cycles / 21,600 measurement frames, real save/load/F5,
inventory overflow, gravity hold/release, flashlight toggles, crate destruction
and puzzle-door movement. Loading intervals are reported separately. Optional
diagnostic GC happens only after timing/acceptance and cannot change its verdict.

## Historical Next Gates (2026-11-02)

The pass statement below describes that earlier pair only. Subsequent failures
above and the current handover supersede it; new systems remain blocked.

The local full-detail six-room reload gate now passes in two final runs. Next,
validate texture quality after fast room/door transitions and repeat longer
streaming-enabled versus full-detail controls before broad mip rollout. Keep
the original 128 MiB guard and frame-time limits. Actual F2/full-detail restoration
and longer manual playtests remain necessary.

Continue profiling transient scene/snapshot preparation and native UI geometry
as allocation follow-ups; the earlier backend comparison is not proof of a
native leak. No native UI rewrite was made in this pass. Spatial audio is the
following separate horror-game milestone.

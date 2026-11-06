# Reload Memory Investigation

Started 5 November 2026. Scope: attribute the existing six-room memory failure,
not new systems or a change to acceptance limits. The investigation below is
historical evidence. The [final 6 November recheck](LIGHTING_ACCEPTANCE_RECHECK.md)
accepts the subsequent diffuse/UI allocation corrections: both BATs and all three
post-correction long controls pass. Gates L/B are accepted with an explicitly
monitored historical stall risk, not a proven stall fix. C#, physics, source
levels, shaders, VSync and full-detail quality remain unchanged.

Attribution-only follow-up 6 November 2026: synchronized address-space attribution below adds
evidence for CLR reusable/free-region capacity. The proposed correction order
is in [Memory Growth Correction Plan](MEMORY_GROWTH_FIX_PLAN.md). No production
allocation fix or acceptance-limit change is included in this diagnostic work.

## Allocation Correction Follow-Up (6 November)

The [first two targeted corrections](MEMORY_GROWTH_FIX_PLAN.md) are now
implemented: validated diffuse payload/atlas reuse, followed independently by
bounded UI geometry reuse. The earlier diagnostic-only results below remain
historical evidence. No C++ migration, forced gameplay GC, physics, shader, level,
quality, cache-format or acceptance-limit change was used.

Commands and component/pixel checks are in [validation](ENGINE_UPGRADE_VALIDATION.md).
Both correction stages repeat the original six-cycle address-space matrix:

```powershell
& ./Tools/LevelAuthoring/RunReloadMemoryInvestigation.ps1 -AddressSpace
& ./Tools/LevelAuthoring/SummarizeReloadAddressSpace.ps1 -ReportFolder <soak-folder>
```

All paths below are relative to `Tools/LevelAuthoring/bin/Release/net10.0/qa/`.
Runner folders: diffuse only `reload-memory-20261106-102609-717`; both fixes
`reload-memory-20261106-104954-519`. Commands/logs/hashes and original failures
are retained. Every control completes six cycles/72 actions. Protected inputs
are unchanged, asset payloads stay flat, all address category sums/heap walks
validate, and post-timing collection leaves zero tracked old entities/meshes.

| Stage / control | Settled private growth MiB | Soak folder |
| --- | ---: | --- |
| Diffuse only: native/no reload | 66.75 | `gameplay-soak-20261106-102609-902-native` |
| Diffuse only: fallback/reload | 54.36 | `gameplay-soak-20261106-102819-086-fallback` |
| Diffuse only: native/reload | 89.19 | `gameplay-soak-20261106-103029-021-native` |
| Both fixes: native/no reload | 6.86 | `gameplay-soak-20261106-104954-705-native` |
| Both fixes: fallback/reload | -12.00 | `gameplay-soak-20261106-105204-452-fallback` |
| Both fixes: native/reload | 53.72 | `gameplay-soak-20261106-105415-498-native` |

These are **diagnostic, timing-failing runs**, not acceptance passes: each
contains three intrusive capture stalls over 100 ms. The second matrix's
native/no-reload control also has two additional intervals over 33.3 ms; they
are retained, not filtered or explained away. Use the original settled samples,
not the paused or collected values, for the memory guard.

Native/reload paused growth after diffuse-only is 87.05 MiB (active GC +58.09,
other CLR +33.16, unknown-private -4.31); after both fixes it is 53.78 MiB
(active GC +29.02, other CLR +27.08, unknown-private -2.39). Residual/stack
categories account for the rest; consult each `address-comparison.json`.
These repeated samples still indicate CLR capacity, not a growing asset set.

The component improvement is stronger evidence than a single process delta:
for native six-cycle routes, UI drawing allocations are **405.626 MiB** both
before and after the diffuse-only fix, then **0.625 MiB** after UI reuse.
Total allocations between settled samples are 2,313.25 MiB originally,
2,066.92 MiB after diffuse-only, and 1,738.49 MiB after both. These are temporary
allocation totals, not retained memory. The source runs are respectively
`094640-238-native`, `103029-021-native`, `105415-498-native` on 20261106.
Heap capacity still varies: the diffuse-only process delta is higher than the
earlier 46.23 MiB short sample despite lower allocations. No monotonic process
improvement or universal leak freedom is claimed.

An unpaused diffuse-only 18-cycle control completes all 216 actions but
**fails memory at 128.05078125 MiB**, with zero asset growth and passing
16.666/16.811 ms gameplay mean/P95, zero intervals >33.3 ms:
`gameplay-soak-20261106-103337-843-native`. No forced collection or new memory
probes are enabled. The unchanged limit is 128 MiB; this is not rounded to a
pass. See validation for the subsequent unpaused result with both fixes.

The unpaused combined-fix long repeat **passes**:
`gameplay-soak-20261106-105655-761-native`, 18 cycles/216 actions,
**80.5546875 MiB** process growth, zero model-payload growth and
16.666/17.990 ms gameplay mean/P95 with zero intervals >33.3 ms.
It uses `soak-gameplay --native-ui --puzzles --long` with no new memory probes
or forced collection. All 62 protected inputs still match. This is a measured
passing repeat, not proof of universal leak freedom or resolution of the
independent historical full-detail stalls. Gates remain pending.

A fresh unpaused six-cycle repeat also passes: `gameplay-soak-20261106-110421-568-native`,
72 actions, **50.46 MiB** process growth, zero asset growth,
**16.661/17.769 ms** gameplay mean/P95, zero intervals >33.3 ms. Command:
`soak-gameplay --native-ui --puzzles`. As with the long repeat, no new memory
probes or forced GC. The remaining work is unchanged broader/manual acceptance
and the separate stall investigation, not a new engine-system milestone.

## Method

Build Release once, then run each workload in a fresh process, sequentially:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release --no-restore
& .\Tools\LevelAuthoring\RunReloadMemoryInvestigation.ps1
& .\Tools\LevelAuthoring\RunReloadMemoryInvestigation.ps1 -Long
```

The runner preserves hashes for level JSON, shaders, physics and fixed timestep.
Saves live in individual QA folders. No concurrent build/game/editor is run.
Normal VSync and texture quality are retained; the observed client viewport is
1920 x 1009 on this desktop (the host requests a 1920 x 1080 window).

Three controls use the same six-room fixture and existing scripted actions:
native UI without reload, fallback UI with reload, native UI with reload.
`--no-reload` skips only destructive inventory reset/save restore and F5; it
still performs movement, inventory, saves, gravity manipulation, breakables,
flashlight and door actions. Dropped objects can therefore accumulate as real
gameplay state in this control; it is not a perfectly identical scene-state
comparison after the first cycle. Short/long runs use 6/18 cycles.

`--memory-stages` adds null-by-default game-side stage scopes and records
main-thread allocation totals for level read/editor rebuild, runtime rebuilding,
UI update/drawing, diffuse/reflection update and model upload. It samples
process/GC counters at the first call per loading stage, readiness and settling.
Background lighting allocations are not attributed to main-thread stage totals.
Stages may finish asynchronously; first-call boundaries are not complete loads.
There is no new unload barrier or forced collection inside ordinary gameplay.

`StageAllocations.Bytes` is cumulative allocation, not retained memory. Process
private bytes, managed commitment and fragmentation overlap and are not additive.
GC heap/generation counters describe the last collection, not a simultaneous
process-wide breakdown; the report retains the collection index. Callback
observer allocations are recorded separately (not all test-harness allocations).
The unchanged settled checkpoint and 128 MiB process / 8 MiB asset guards remain.
Instrumented/control runs are labelled `DiagnosticOnly`, not gate clearance.

## Short Controls

Reports: `Tools/LevelAuthoring/bin/Release/net10.0/qa/reload-memory-20261105-193453-443/`.
All protected hashes match; all three complete 6 cycles / 72 scripted action slots.

| Control | Soak report suffix | Private growth | Gameplay mean/P95 | >33.3 ms |
| --- | --- | ---: | ---: | ---: |
| Native, no reload | `20261105-193454-701-native` | 70.67 MiB | 16.657 / 18.090 ms | 0 |
| Fallback, reload | `20261105-193702-402-fallback` | 71.71 MiB | 16.655 / 17.643 ms | 0 |
| Native, reload | `20261105-193909-528-native` | 29.53 MiB | 16.661 / 16.811 ms | 0 |

These pass the existing numerical guards in this diagnostic repeat but do not
erase the prior 142.51/153.75 MiB failures. Native reload private memory ranges
from 1054.8 to 1186.7 MiB across settled checkpoints and ends at 1084.3 MiB.
This is not monotonic growth. Collection timing and heap layout matter.

Native reload managed allocations on observed main-thread stages:
UI overlay 405.63 MiB; UI update 162.78 MiB; reflection update 194.40 MiB;
diffuse update 94.12 MiB; level read/editor rebuild 26.54 MiB; runtime rebuild
6.50 MiB; model uploads 4.47 MiB. Unobserved/background stages are additional.
The no-reload overlay is almost identical at 405.64 MiB, while fallback overlay
is zero. Thus native UI has measurable allocation churn even without reload;
these numbers alone do not prove retained UI objects or a native leak.

One fallback reload (frame 900 to input-ready at 947) goes from about 932.5 to
1008.4 MiB process-private; total allocated bytes increase by about 153 MiB
over the measured read-through-readiness interval. Runtime entity rebuild alone
allocates less than 1 MiB per call. Lighting preparation merits separate
background allocation and retained-object inspection, rather than blaming
entity count or assuming all bytes are native UI allocations.

## Long Controls

Reports: `qa/reload-memory-20261105-194309-575/`. All protected hashes match.
Each control completes 18 cycles / 216 action slots; model/texture payloads do
not grow. All gameplay frame guards pass with zero intervals over 33.3 ms.

| Control | Soak report suffix | Private growth | Gameplay mean/P95 | Memory guard |
| --- | --- | ---: | ---: | --- |
| Native, no reload | `20261105-194309-810-native` | 36.81 MiB | 16.667 / 16.808 ms | Pass |
| Fallback, reload | `20261105-194916-256-fallback` | 120.17 MiB | 16.666 / 16.832 ms | Pass |
| Native, reload | `20261105-195525-316-native` | **154.34 MiB** | 16.666 / 16.819 ms | **Fail** |

The native failure reproduces the prior result without a rendering/physics
change. Its first/final settled comparison is +154.34 MiB private memory,
+151.57 MiB managed commitment, +125.86 MiB reported fragmentation, but only
+7.02 MiB estimated managed bytes. About 7.406 GiB is allocated between those
checkpoints. This strongly points toward allocation/GC heap behaviour as the
dominant contributor in this repeat, not cumulative model/texture payloads.
It is not a synchronized byte-for-byte attribution or proof against native leaks.
Of the reported fragmentation increase, 111.55 MiB is in generation index 3
(the large-object heap), strengthening the case for inspecting large temporary
buffers. This is a component of the fragmentation number, not extra memory.

Native long observed stage allocation totals: UI overlay **1216.8 MiB**, UI
update 451.9 MiB, reflections 555.5 MiB, diffuse 253.8 MiB, level read/editor
rebuild 79.6 MiB, runtime rebuild 17.4 MiB and model uploads 4.5 MiB. Callback
observer allocation totals only 1.82 MiB; this excludes other test recording.
UI drawing creates per-command vertex/index arrays, growable aggregate lists
and final `ToArray` upload copies every frame in `RmlUiOverlayRenderer.Render`.
That is a concrete churn source, not evidence those arrays remain alive.

Runtime entities increase 753 -> 776 through intentional saved overflow drops
and break rewards; payloads remain constant. After post-timing collection,
none of 13,763 tracked old entities or 90 old collider meshes remain alive.
The no-reload route also intentionally accumulates world drops. Do not present
these modest live gameplay-state increases as leaked duplicate levels.

## Heap Comparison

Microsoft `dotnet-gcdump` 10.0.750501 is installed only in ignored
`.dotnet/diagnostics`, not added as an engine dependency. Separate diagnostic:

```powershell
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll soak-gameplay --native-ui --puzzles --memory-trace --memory-stages --heap-snapshots --long
```

This records `heap-early.gcdump` after cycle one and `heap-late.gcdump` after the
final cycle, with collector logs and counters after collection. The collector
forces full GC and pauses gameplay; these runs cannot validate timing, normal
memory growth or ordinary GC behaviour. Original test assertions are not
relaxed; a timing failure during collection is expected and retained.
See [Microsoft's collector behaviour](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump).

Use heap differences to identify retained types and test-recorder costs. Check
native/driver allocation separately if process growth remains unexplained.
Passing asset counters or zero tracked old entities is not proof of leak freedom.

Completed report: `qa/gameplay-soak-20261105-200208-153-native/`, 18 cycles /
216 actions. Both `.gcdump` files, collector logs and full `heap-early.txt` /
`heap-late.txt` reports are retained. The two collection interruptions cause two
gameplay intervals over 100 ms and the unchanged frame assertion fails. This is
an intentionally intrusive diagnostic, not a performance regression verdict.
Both collector logs warn that this .NET 10 runtime lacks non-lossy (Block)
event buffering and that a large heap capture may be incomplete. Neither log
reports a specific loss count. Treat heap totals/type counts as supporting
evidence, not a complete proof of retention or native leak freedom; the
separate process/GC counters do not depend on this event capture.

| Live managed data after collection | Early (frame 1220) | Late (frame 21620) |
| --- | ---: | ---: |
| Heap bytes | 269,570,139 | 289,043,341 |
| Objects | 240,889 | 461,079 |
| LevelFile | 19 | 19 |
| LevelEntityDef | 14,402 | 14,402 |
| ReflectionSnapshot | 12 | 12 |
| LightmapData | 1 | 1 |
| Runtime Entity | 754 | 775 |
| FrameProfile trace records | 1,220 | 21,620 |
| UiVertex arrays | 1 (empty) | 1 (empty) |

The live heap increases **18.57 MiB**, not 154 MiB. These are different runs and
the collector changes GC behaviour, so the difference cannot be subtracted
from the failing control to calculate a native leak. Level/scenario snapshots
remain bounded in this sample. The additional gameplay entities are consistent
with saved drops/rewards; this route is not a constant-object-count idle test.

The test recorder deliberately retains one frame record and its timing/counter
arrays per frame. The extra 20,400 records alone occupy 3,100,800 bytes before
their arrays/list storage. Double-array counts grow by 81,600 and long-array
counts by 20,399, consistent with the recorder's copies. Weak references and
soak interval lists also grow. Much of the live-object increase is therefore
test evidence, not new level copies. No retained non-empty UI vertex array is
present at these two checkpoints; that does not rule out temporary spikes.
The text report groups some arrays by size range: its `Object Bytes` column
must not be multiplied by a bucket count to claim exact retained bytes.

## Regression Checks

With the memory observer disabled, existing automated checks still pass:

- `validate-gameplay-stability`: 24 checks,
  `qa/stability-checks-20261105-201125-337/`.
- `validate-physics-replay sixRoomShadowTest Tools/LevelAuthoring/bin/Release/net10.0/qa/physics-replay-20261028-111902-477`:
  all 600 historical state/contact/bounds hashes match,
  `qa/physics-replay-20261105-201207-638/`. An initial command incorrectly used
  the `.json` extension and was rejected before testing; it is not a pass.
- `check-lighting-dependency-images --baked`: all three strict/package lighting
  image pairs match exactly, including F2;
  `qa/lighting-images-20261105-201225/`.

These are automated regressions, not new manual Game/F2 acceptance. Release
authoring builds pass with zero warnings/errors. No source level, shader,
physics setting, save format or cache format changes; no rebake or migration.

### Uninstrumented Reload Repeat

Command: `.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll soak-gameplay --native-ui --puzzles`.
No memory-stage observer, weak-reference tracking, heap captures or forced
post-run collection. The existing frame recorder and settled counters remain.

The initial restricted launch (`qa/gameplay-soak-20261105-201246-094-native/`)
completes six cycles / 72 actions but fails pacing: gameplay mean/P95
36.925/40.967 ms, all 6,825 gameplay intervals over 33.3 ms. Host presentation
averages 29.95 ms; private growth is 121.18 MiB. VSync is on, graphics debug off,
and every recorded frame is focused. Retain this environment-sensitive result;
do not label it a performance pass or a demonstrated engine regression.

The unchanged normal-desktop repeat outside the sandbox
(`qa/gameplay-soak-20261105-201804-227-native/`) completes six cycles / 72 actions
at **16.666/16.812 ms** gameplay mean/P95, zero intervals over 33.3 ms. It still
**fails memory at 134.27 MiB**, with zero model/texture growth. This confirms
that the newly added observer/heap capture is not required to reproduce failure.
No source or build change separates these two runs. This pair supports a
launch-environment effect on presentation, not a diagnosis of older severe stalls.

Importantly, this normal repeat reports **-14.61 MiB managed commitment**,
**+70.31 MiB fragmentation**, and **-149.75 MiB estimated managed bytes**, while
private memory rises. Thus the instrumented long run's close private/GC-commit
correlation is not a universal explanation. Last-collection counters and live
process counters differ in timing; native heaps, graphics-driver reservations
and contemporaneous GC segments still need attribution. Do not call the issue
a managed-only leak, or assume removing UI churn will necessarily close it.

## Address-Space Attribution (2026-11-06)

`Tools/MemoryProbe` is a separate .NET 10 diagnostic executable using Microsoft's
ClrMD 4.2.751201 package. It is not referenced by Game, HS2Editor or the engine
libraries. Its self-test verifies overlapping ownership precedence, a known
64 MiB managed array, a known 24 MiB native allocation, target resume and release.
Report: `Tools/MemoryProbe/bin/Release/net10.0/qa/self-test-20261106-094031/`.
Both helper and authoring Release builds pass with zero warnings/errors.

The helper attaches only to the owned QA process and pauses it while reading
current CLR segments/native ranges and Windows `VirtualQueryEx` regions. It
then resumes the process before writing the report. No collection, trimming,
memory write or GPU flush is performed by the helper. Committed private ranges
are partitioned with precedence, never counted twice. CLR free-region pools
are distinguished from active heap segments and from unattributed private pages.
See [ClrMD native-heap enumeration](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/ClrRuntime.cs)
and [Windows address-space queries](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex).

The soak additionally samples DXGI local/nonlocal budget usage before each
capture, with all temporary COM references released. These are video-memory
usage measurements, not an extra amount to add to process private bytes. They
are not synchronized with the paused OS snapshot and can reflect eviction or
residency changes. The existing model/texture/lightmap/reflection payload counts
remain recorded separately.

Commands, sequential normal-desktop execution, no concurrent builds:

```powershell
.\.dotnet\dotnet.exe build Tools/MemoryProbe/MemoryProbe.csproj -c Release
.\.dotnet\dotnet.exe Tools/MemoryProbe/bin/Release/net10.0/MemoryProbe.dll self-test
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release --no-restore
& .\Tools\LevelAuthoring\RunReloadMemoryInvestigation.ps1 -AddressSpace
& .\Tools\LevelAuthoring\RunReloadMemoryInvestigation.ps1 -AddressSpace -Long -Control native-reload
& .\Tools\LevelAuthoring\SummarizeReloadAddressSpace.ps1 <individual-soak-report-folder>
```

Captures occur after cycles 1, midpoint and last. A separate `post-run-collected`
capture follows the existing post-timing diagnostic collection, after frame
report serialization. It is not substituted for a normal settled sample. All
capture runs remain `DiagnosticOnly`; their three intentional pauses fail the
unchanged frame guard. The runner preserves those failures and isolated saves.
No post-hoc removal of the pauses is used to claim performance acceptance.

Short matrix: `qa/reload-memory-20261106-094212-536/`. Each run completes 6 cycles /
72 action slots. Protected inputs unchanged; all heap walks consistent with zero
invalid objects. `address-comparison.json` verifies category sums against the
private-commit region total and retains GPU usage, counters and raw paths.

| Control / soak suffix | Original settled growth | Paused private growth | Active GC regions | Other CLR regions | Unattributed private |
| --- | ---: | ---: | ---: | ---: | ---: |
| Native no reload, `20261106-094213-755-native` | 31.51 MiB | 28.14 MiB | +11.38 MiB | +20.22 MiB | -3.47 MiB |
| Fallback reload, `20261106-094429-351-fallback` | 86.04 MiB | 82.40 MiB | +80.39 MiB | +5.24 MiB | -3.55 MiB |
| Native reload, `20261106-094640-238-native` | 46.23 MiB | 43.55 MiB | -27.20 MiB | +73.04 MiB | -2.49 MiB |

Original settled checkpoints precede collector startup and GPU diagnostic warmup;
paused comparisons are separate observations, not replacement acceptance metrics.
Small managed-stack and process-versus-private-region accounting differences are
retained in the JSON rather than silently assigned to a category. Mapped/image
regions can contain private copy-on-write pages; the full residual is not VRAM.

In the short native reload, **71.61 MiB** of the other-CLR increase is explicitly
`GCFreeRegion` plus `GCFreeGlobalHugeRegion`: reusable capacity still committed
to the process, not live textures or active managed objects. Remaining CLR
growth is mostly bookkeeping. Local GPU usage stays about 440 MiB (440.04 to
440.02), nonlocal usage falls 13.88 to 12.76 MiB, and asset payloads stay flat.
This explains why looking only at active objects or last-GC heap statistics
misses part of current process commitment. It does not establish that every
historical spike has the same attribution or that a native leak is impossible.

Object tables in these snapshots include unreachable objects awaiting collection,
unlike a rooted-live-object analysis. They must not be treated as retained-object
proof. A read-only heap walk also does not attribute allocation call stacks.

### Long Attribution Repeat

Runner: `qa/reload-memory-20261106-094925-742/`; soak:
`qa/gameplay-soak-20261106-094925-941-native/`. All 18 cycles / 216 actions
complete. Protected inputs unchanged; zero invalid objects in all four walks.
The original settled comparison is **139.52 MiB**, over the unchanged 128 MiB
guard. The three intentional capture pauses trip the earlier frame assertion;
this is not an automated memory or timing pass.

| Paused comparison to cycle 1 | Cycle 9 | Cycle 18 |
| --- | ---: | ---: |
| Process private | +153.98 MiB | **+139.52 MiB** |
| Active GC regions | +125.66 MiB | **+143.125 MiB** |
| Other CLR-owned regions | +29.22 MiB | -2.375 MiB |
| Unattributed private pages | -1.27 MiB | -1.477 MiB |
| Managed-thread stacks | -0.023 MiB | -0.098 MiB |
| Process/private-region accounting residual | +0.38 MiB | +0.348 MiB |

At cycle 18, 103.27 MiB of the active-region increase is large-object-heap
commitment. Local GPU usage falls **440.04 -> 395.19 MiB**, nonlocal usage falls
**13.91 -> 12.76 MiB**; model/texture payload growth is zero. The additional
process commitment is accounted for by CLR regions in this reproduced failure,
not an increasing unknown-private or video-memory category. The residual delta
is small and retained explicitly; this is not universal proof of driver leak
freedom across workloads.

The active heap contains both current objects and uncollected previous reload
objects: LevelFile counts are 38/19/59 at cycles 1/9/18, then **19** after the
post-run collection. That is not 59 permanently retained levels. Post-timing
collection leaves **zero of 13,852 tracked old entities and 90 old meshes** alive.
It still leaves **247.80 MiB of free holes inside active segments**, alongside
unused CLR regions. The 127.49 MiB post-collection private increase is not
substituted into the failing normal checkpoint. It also follows frame-report
serialization and collection, so it is not a clean experiment changing GC alone.

Native UI drawing allocates **1,275,856,528 bytes cumulatively** in this route;
diffuse/reflection main-thread updates allocate 264,104,416 / 583,317,104 bytes,
with worker allocations additional. Stage counters identify code regions, not
allocation call-stack samples. Source inspection finds repeated geometry copies,
large owned diffuse pixels and revision-triggered snapshot/cache reloads. These
are concrete correction candidates; the effect of each must be measured, not
assumed from cumulative allocation alone.

Fresh observer-disabled automated regressions also pass:

- 24 gameplay/startup checks: `qa/stability-checks-20261106-095639-936/`.
- All 600 historical physics state/contact/bounds hashes:
  `qa/physics-replay-20261106-095715-257/`.
- Three exact strict/package lighting image comparisons, including F2:
  `qa/lighting-images-20261106-095736/`.

No new manual Game/F2 acceptance is claimed. No source level, solver/timestep,
shader, lighting quality, normal GC policy, cache format or save format changed.
No reimport, rebake or migration is required.

Final normal-desktop control, with memory stages, weak-reference tracking,
address/heap snapshots and forced collection disabled:
`soak-gameplay --native-ui --puzzles`,
`qa/gameplay-soak-20261106-095755-199-native/`. All six cycles / 72 actions pass,
with **101.00 MiB** private growth, **16.667/16.793 ms** gameplay mean/P95,
zero intervals over 33.3 ms and flat model payload. Its ordinary frame recorder
remains enabled as before. This demonstrates variability and disabled-probe
compatibility, not a production fix or reversal of the earlier failures.

### Current Conclusion And Plan

The new synchronized snapshots resolve the broad ownership ambiguity in these
runs: **CLR active/free-region capacity dominates measured growth**. Allocation
churn, delayed collection and fragmentation explain why a smaller active/live
heap is not equivalent to less process-private memory. A permanently growing
texture cache or native-UI leak is not supported by these captures.

Next: [the staged correction plan](MEMORY_GROWTH_FIX_PLAN.md), beginning with
unnecessary large lighting reload allocations, followed by bounded UI staging
reuse, one independently verified change at a time. No production optimization
has yet been applied. **Gate L pending; Gate B pending; new upgrades blocked.**
Historical full-detail stalls remain a separate unresolved issue.

## Status After The First Round (2026-11-05)

Historical status below is retained for the evidence trail. The 6 November
attribution and correction plan above supersede its proposed next diagnostic.

The initial controlled round and early/late heap comparison are complete; exact
ownership of all process growth is not. Temporary managed allocation churn,
large-object fragmentation and diagnostic retention are measured contributors.
The long instrumented failure closely tracks GC commitment, but the later
uninstrumented failure does not. Native/driver ownership and synchronized
process/GC address-space evidence remain the next diagnostic requirement.

Concrete allocation-reduction candidates are bounded native-UI geometry staging
buffers and redundant lighting reload/snapshot allocations. They are not yet
proven fixes for every process-memory failure. Attribute the remaining growth,
change one contributor at a time, and repeat the same controls and image/lifetime
checks while preserving owned results, dependency validation, cancellation and
fallback. Do not hide the failure with forced gameplay GC, a higher limit,
weaker texture quality or physics changes.

No speculative production optimization or garbage-collection policy change
has been made in this investigation. Gate L remains pending manual Game/F2
acceptance; Gate B remains pending memory correction and the separate severe
intermittent stall investigation. New system upgrades remain blocked.

# Reload Memory Investigation

Started 5 November 2026. Scope: attribute the existing six-room memory failure,
not new systems or a change to acceptance limits. Gate L and Gate B remain
pending. C#, physics, source levels, shaders, VSync and full-detail quality stay
unchanged. See [the acceptance handover](LIGHTING_ACCEPTANCE_HANDOVER.md).

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

## Status

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

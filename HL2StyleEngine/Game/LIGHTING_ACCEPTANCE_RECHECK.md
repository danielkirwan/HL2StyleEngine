# Lighting Acceptance Recheck

Date: 2026-11-06. Follow-up to the diffuse/UI allocation corrections. This is
existing lighting/baseline acceptance only; no new engine system is authorized.
C# runtime, authored atmosphere, materials, source levels, physics, timestep,
normal VSync, texture quality and acceptance limits are preserved.

## Automated Baseline

All report folders below are under
`Tools/LevelAuthoring/bin/Release/net10.0/qa/`. Workloads ran sequentially on the
normal desktop, D3D11/RTX 3060, Release, 1920x1009 client and normal VSync.
The reference package uses full-detail textures; streaming is separate.

`RunLightingAcceptance.ps1 -Phase Baseline`:
`lighting-acceptance-20261106-112348-861-Baseline`.
Both original BATs pass, with zero failed commands and unchanged protected inputs.
Geometry: 54,497 checks; collider bounds: 1,000 cases plus mesh/default checks;
gameplay/startup: 24 checks. Both short frame-pacing controls and GPU ring checks pass.

| Six-cycle gameplay route | Report | Private growth | Gameplay mean / P95 | >33.3 / >100 ms |
| --- | --- | ---: | ---: | ---: |
| Native atmosphere | `gameplay-soak-20261106-112357-435-native` | 42.19 MiB | 16.660 / 17.879 ms | 0 / 0 |
| Fallback atmosphere | `gameplay-soak-20261106-112602-977-fallback` | 9.74 MiB | 16.661 / 17.924 ms | 0 / 0 |
| Native six-room | `gameplay-soak-20261106-112808-054-native` | 53.10 MiB | 16.663 / 18.001 ms | 1 / 0 |

Every route completes 72 actions. The six-room outlier is frame 3979,
33.84 ms, within the unchanged allowance; it is not hidden by the average.
Short frame-pacing reports: `texture-level-20261106-113023-111` (reference)
and `texture-level-20261106-113129-999` (streaming).

`RunLightingAcceptance.ps1 -Phase LongControls`:
`lighting-acceptance-20261106-113341-231-LongControls`; all commands pass and
protected inputs match. Every long route completes 18 cycles / 216 actions:

| Long route | Report | Private growth | Gameplay mean / P95 | >33.3 / >100 ms |
| --- | --- | ---: | ---: | ---: |
| Full-detail reference | `gameplay-soak-20261106-113453-054-native` | 71.74 MiB | 16.664 / 18.034 ms | 0 / 0 |
| Experimental streaming | `gameplay-soak-20261106-114100-819-native` | 74.63 MiB | 16.663 / 17.892 ms | 0 / 0 |
| Native six-room | `gameplay-soak-20261106-114708-343-native` | 76.80 MiB | 16.663 / 17.897 ms | 0 / 0 |

The six-room `--memory-trace` captures retention only after timing/acceptance;
forced collection does not improve the settled-memory result. No heap/address
capture pauses occur in these runs. These are near-60-FPS averages, not a locked
minimum 60 FPS or proof of leak freedom. Historical severe stalls remain distinct.

## Lighting Regression Found

Full suite `lighting-acceptance-20261106-115359-596-Lighting` fails 2 of 32
commands: `check-shadow-pixels` (immediate on-state restore, maximum 37/255)
and `validate-flashlight-mount` (fixture preservation, maximum 26/255).
Other checks pass, including materials/PBR/emission, diffuse/reflection
validation, transitions, 64 shadow-contact views, atmosphere, floor correction,
cache dependencies, strict/package/F2 images, 15 legacy images, packaged assets,
texture transitions/editor entry, and both 600-step historical physics replays.
All protected inputs remain unchanged. This failed run is retained.

Cause: the allocation correction treated an already-validated retained lighting
scenario like a new reload, clearing its active diffuse data while validation
ran. An on/off/on switch could therefore temporarily lose indirect illumination.

Correction: restore the resident bank immediately only for the same scenario
source, authored revision and unchanged dependency/cache metadata. Changed
retained inputs retire the bank. Actual reloads, new revisions and changed files
still take the full source-key/content validation path. Same-size/timestamp
corruption remains covered on reload. Existing live file-change detection uses
metadata polling; this does not claim immediate detection of invisible external
same-metadata edits to an already resident state.

Focused final checks:

- `check-lighting-reload`: 78 checks, `lighting-reload-20261106-115914-961`.
  Adds four immediate warm switches, changed corrupt alternate cache retirement
  and rejection after a new authored revision, alongside full reload/cancellation
  and same-metadata corruption tests.
- `check-shadow-pixels`: `shadow-stability-20261106-115918`; immediate restore
  has zero changed pixels, all nine distance and eight orbit comparisons match
  the uncached reference exactly, wall-safe flashlight and edit/undo pass.
- `validate-flashlight-mount`: `flashlight-mount-20261106-115938`; close-up,
  held-object/reference, 20 camera poses and continuous wall clamp pass.

The baseline/long tables above precede this warm-switch correction. The full
post-correction lighting rerun is `lighting-acceptance-20261106-120827-398-Lighting`:
**all 32 commands pass**, build has zero warnings/errors, protected inputs match.
It includes all 15 legacy image comparisons (within the existing 1/255 limit),
both exact 600-step historical physics replays, all 62 package/cache checks per
package, and 12 exact texture-transition comparisons. No reference was regenerated.

Representative reports from that rerun:

- `shadow-stability-20261106-120928`, `flashlight-mount-20261106-120947`.
- `floor-lighting-20261106-120954-807`: zero false self-shadow at 4/8/12 m.
- `lighting-allocations-20261106-120958-557`: 63 checks; reuse 384 bytes/4.13 ms,
  full decode 50,594,504 bytes/298.53 ms (component measurements, not level load).
- `lighting-images-20261106-121008`: exact strict/package/F2 image pairs.
- `shadow-views-20261106-121024`: game/F2/standalone-editor captures.
- `texture-transitions-20261106-121051-563`, `texture-level-20261106-121134-000`:
  transition pixels and scripted F2/reset coverage, not native input acceptance.
- `physics-replay-20261106-121154-122`, `physics-replay-20261106-121157-913`.

No rebake, reimport, cache migration or authoring change is required.

## Final Untraced Controls

Post-correction `RunLightingAcceptance.ps1 -Phase Baseline`:
`lighting-acceptance-20261106-124209-523-Baseline`. **Both original BATs pass**,
zero failed commands, protected inputs unchanged, Release builds zero warnings/errors.
No private review app, recorder or other test ran concurrently.

| Six-cycle route | Report | Private growth | Gameplay mean / P95 | >33.3 / >100 ms |
| --- | --- | ---: | ---: | ---: |
| Native atmosphere | `gameplay-soak-20261106-124218-747-native` | 24.28 MiB | 16.661 / 17.089 ms | 0 / 0 |
| Fallback atmosphere | `gameplay-soak-20261106-124424-823-fallback` | 24.18 MiB | 16.661 / 17.078 ms | 0 / 0 |
| Native six-room | `gameplay-soak-20261106-124630-328-native` | 77.15 MiB | 16.661 / 17.028 ms | 0 / 0 |

Each route completes 72 actions; asset payloads do not grow. Short full-detail
and streaming pacing controls both pass: `texture-level-20261106-124844-366`
and `texture-level-20261106-124951-336`. Normal desktop applications are not
controlled; these are not cold-cache benchmarks or minimum-FPS guarantees.

Post-correction `RunLightingAcceptance.ps1 -Phase LongControls`:
`lighting-acceptance-20261106-125138-307-LongControls`. **All six commands pass**,
protected inputs unchanged. These measurements are separate from the earlier
long runs and the instrumented scheduling capture. Each long route completes
18 cycles / 216 actions:

| Long route | Report | Private growth | Gameplay mean / P95 | >33.3 / >100 ms |
| --- | --- | ---: | ---: | ---: |
| Full-detail reference | `gameplay-soak-20261106-125250-062-native` | 60.68 MiB | 16.661 / 16.896 ms | 0 / 0 |
| Experimental streaming | `gameplay-soak-20261106-125857-595-native` | 90.93 MiB | 16.661 / 16.905 ms | 0 / 0 |
| Native six-room | `gameplay-soak-20261106-130505-433-native` | 106.91 MiB | 16.661 / 16.952 ms | 0 / 0 |

The six-room memory-trace retention collection occurs after timing/acceptance,
not during gameplay or before the settled-memory decision. The 128 MiB process
and 8 MiB asset guards are unchanged; assets do not grow. Its remaining private
memory margin is approximately 21 MiB, not unlimited headroom for future audio.

Offline `analyze-frame-pacing <frames.json>` reports are retained beside all
three traces. Full-detail and streaming have 21,600 valid GPU samples each with
zero invalid/pending/skipped samples; the ordinary native soak does not enable
GPU profiling. Worst profiled gameplay host work is 18.46/18.33/19.84 ms.
Loading remains explicitly separate: worst host work is 49.69/51.39/**364.56 ms**
respectively. The latter is frame 900, the first durable save, with 1.08 ms
physics and 6.014 ms GC pause. It is not a recurrence of the historical gameplay
physics-wall stall, nor a promise of hitch-free saving/loading. Host averages
including loading can be below 60 FPS even though gameplay meets the existing gate.

Final targeted `-Phase ValidationDebug`:
`lighting-acceptance-20261106-131202-401-ValidationDebug`; all six commands pass,
protected inputs match, build zero warnings/errors. This uses the existing
graphics-validation commands, not a new separate whole-engine Debug-build claim.
Moving light/door comparisons, multi-shadow resources, legacy rendering,
24 atmosphere data/editor/cache and 55 atmosphere GPU checks pass.

## Scheduling Investigation

The ordinary WPR permission attempt was rejected (`0xc5585011`), without starting
a recording. The user then approved UAC for a bounded local trace. The separate
[scheduling guide](SCHEDULING_TRACE_GUIDE.md) documents the tooling and limits.
The analyzer self-test and Release build pass. QPC correlation is diagnostic-only;
the analyzer is not referenced by the game.

Approved capture: `scheduling-20261106-120019-217`, normal full-detail/VSync,
D3D11 debug off, workload `gameplay-soak-20261106-120027-506-native`.
The recorder stopped its own uniquely named session; no recording remains active.
18 cycles/216 actions complete; 61.70 MiB growth, gameplay mean/P95
16.666/17.979 ms, zero >33.3/>100 ms. These are **diagnostic results**, not
untraced performance acceptance. Raw ETL remains local (2,771,386,368 bytes).

The rebuilt offline analyzer and interval self-test pass. Reanalysis includes
the worst gameplay host frames as well as overall host/physics frames:
35,683 matching switch-ins, zero lost events and complete selected-frame coverage.
Worst selected gameplay host work is 19.44 ms, with 13.68 ms off-CPU, mostly
waiting rather than ready time. This does not reproduce the historical severe
stall. The 49.33 ms worst overall host frame is loading. The 6.66 ms worst
physics frame is also loading, with approximately 5.06 ms waiting and a matching
5.061 ms GC pause. That cannot explain the older no-GC 100-270 ms incidents.
142 main-thread hard-fault events across the trace are not a causal attribution.
No driver, solver or scheduler root cause is claimed.

The recorder's checked temporary-file size did not establish a hard disk cap;
merged output exceeded 2 GiB. The timeout/session ownership worked. Keep free
disk space available and treat the size check as best-effort, not a guarantee.

## Native Review And Gates

Native Game/F2/HS2Editor evidence is recorded below, with human and automated
coverage explicitly separated.
Earlier manual evidence remains in [the handover](LIGHTING_ACCEPTANCE_HANDOVER.md).

Private fixture: `lighting-editor-review-20261106-121241-266`. Its executable
directory now has a Content junction to its private Content tree; an earlier
missing-content launch is an invalid fixture, not a renderer regression.
`native-game-on.jpg` records the correctly textured hall. The user independently
confirmed that F and F2 respond normally; injected key attempts did not, so those
attempts are not counted as passed native input. An earlier physical Escape
stopped desktop automation, which was respected until the user resumed checks.

During the resumed review, an incorrect assistant checklist suggested F6 as a
lighting control. F6 actually resets the level from its default factory, replacing
the private authored copy with blockout geometry. That invalid review state was
preserved as `reset-default-level.json`, then the private level was restored from
the unchanged source. F toggles the flashlight; fixture lighting uses the room's
switch; F5 reloads. There is no F9 load shortcut. Original source/project hashes
still match. No rendering or authoring change was made to compensate.

The user then completed the restored private game's short walkthrough and
explicitly confirmed walking, flashlight switching, carrying, throwing and
breaking the crate all behaved normally. This is **user-performed manual
acceptance**, not automation. `native-user-f2.jpg` captures the resulting native
textured F2 view. Save/load and detailed fixture-state transitions remain covered
by the separate automated checks rather than this short walkthrough.

Representative final automated captures were also visually inspected:
`shadow-stability-20261106-120928/floor-corner-0-torch.png`,
`pressure-crate-distance-18.png` in the same folder, and
`flashlight-mount-20261106-120947/crate-down.png`. Textured floors/walls/crates
remain visible at close and distant viewpoints; the previously reported broad
floor bands are not evident in these captures. Static screenshots do not by
themselves prove absence of temporal flicker.

The user also performed the standalone-editor movement/edit/Undo/Redo/Save
check and clarified that the edited control was **Stream Room Reflections**.
`native-user-editor-saved.jpg` shows that checkbox enabled and a Save confirmation.
After restart, `native-editor-reopened.jpg` records the same dock layout and a
clean document. File inspection confirms the saved change from source
`ReflectionStreaming: null` to `{MaxResidentProbes: 3, PrefetchDistance: 8,
RetainSeconds: 5}`. Saved JSON SHA-256 remains
`82030D3A3E067150D065FB92D2697F4970A3E62FAA9F9BDFB16657E237832EDE`
across reopen; entity and lighting-scenario data remain identical to source.
The initial comparison accidentally read AutomaticRoomReflections, a different
flag; the corrected property check above establishes the actual persisted edit.
The editor camera starts outside the hall: the dark exterior reopening image is
layout/settings evidence, not an interior lighting capture. Earlier native
lights-off/Undo/Redo/Save/reopen images remain the changed lighting-state evidence.
Prefab browsing is historical native coverage; emission/prefab isolation and
reference remapping are automated coverage, not a fresh manual prefab-build pass.

All private review processes were closed before the final performance runs.
Only the owned QA apps were stopped; unrelated desktop applications were left
untouched. Source project/level hashes still match the review's before snapshot.

- **Gate L: complete.** Existing lighting is accepted within the documented
  limits using automated checks, inspected captures, prior native editor evidence
  and the user's completed gameplay/editor review. This is not acceptance of
  every possible prefab-authoring workflow or controller device.
- **Gate B: complete, with explicitly accepted residual risk.** Both final BATs
  and all three long controls pass unchanged numerical limits. The old severe
  stalls remain open/not reproduced, not fixed; the user's disposition below
  allows advancement while requiring regression monitoring.
- **New system upgrades: ready.** Next is the existing small C# contracts/audio
  milestone. No audio, interaction, editor feature, navigation, AI prototype or
  preparatory upgrade was implemented during this handover.

## Historical-Stall Disposition

On 6 November the user explicitly chose to move on if the stall is no longer
occurring in the current checks, while keeping it under observation. Final
baseline acceptance required the last post-correction long control to pass;
it now has. This does **not** mean the older stall has a proven fix.
This decision supersedes the earlier requirement to keep new systems blocked
solely because those retained incidents lack a root cause. Original numerical
frame/memory limits, quality, VSync and physics requirements remain unchanged.

Keep the 100-270 ms incident open as an accepted, non-reproduced residual risk.
Use the exact BATs and applicable long full-detail/native controls as regression
gates for the next audio milestone, with the same loading/gameplay separation.
Do not hide a recurrence behind average FPS or a clean retry. Retain a failing
report, pause advancement, then use the existing opt-in version-5 frame profiler
and a bounded approved Windows scheduling trace to correlate the actual event.
Compare ready/wait/running time, GC, hard faults, GPU/Present and workload phase;
inspect stacks/DPC/ISR events in WPA if attribution is still unclear. Capture
after idle, reload and representative manual transitions where relevant.

No always-on recorder, automatic desktop watcher, system-setting change or new
audio implementation is introduced by this decision. Clean traces cannot
retrospectively establish a cause. The accepted baseline is specific to the
recorded build/configuration and does not promise a locked minimum FPS on all PCs.

## Accepted Baseline To Preserve

`accepted-lighting-baseline-20261106/manifest.json` records 227 source/project/
binary hashes plus the final report paths. Its linked LongControls
`protected-after.json` records source levels, lighting shaders, physics and fixed
timestep hashes. Original reference images remain unchanged. These local QA
artifacts identify the actual accepted working tree, not an invented Git commit.

Preserve normal full-detail play/VSync, material/PBR/emission behavior, floor
normal/shadow correction, bounded point/spot shadows and flashlight mounting,
validated diffuse/reflection scenario restoration, room membership/readiness,
cache invalidation/fallback/cancellation and fenced resource release. Preserve
the authored atmosphere (density 0.1, start 3.102 m, falloff 0.034, maximum opacity
0.744), C# runtime, physics replay outcomes and editor layout/input/save behavior.
Experimental texture streaming is not promoted to the default path.

The handover's finite light budgets, fixed one-bounce indirect approximation,
non-realtime reflections and cold/missing-cache darkness remain limitations.
Same-metadata external cache edits require full reload validation for guaranteed
detection; live resident polling is metadata-based. No rebake, reimport, cache
migration or user save reset is required. Future legitimate changes to fixed
geometry/material/light inputs still require matching derived lighting rebuilds.

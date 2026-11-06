# Memory Growth Correction Plan

6 November 2026. Diffuse-reload and UI-staging corrections implemented and accepted.
Both final BATs and three post-correction long controls pass (60.68-106.91 MiB);
native acceptance and the frozen baseline are in [the recheck](LIGHTING_ACCEPTANCE_RECHECK.md).
This follows [the investigation](RELOAD_MEMORY_INVESTIGATION.md), not a new
engine-system milestone. Gate L and Gate B are complete with explicit user
acceptance of the separate historical stall risk. New systems are ready next;
the old stall is monitored/not reproduced, not causally fixed. Older pending
statements below refer to the earlier focused measurements.

The broader lighting suite exposed a first-frame warm-scenario restore regression.
It is corrected: only unchanged, already validated resident states restore
immediately; real reloads and changed inputs still validate fully. The expanded
reload check passes 78 assertions and the full lighting suite passes 32/32 commands.
No additional allocation refactor is justified solely to chase lower memory in
already passing controls. Historical frame stalls remain a separate investigation.

## First Correction Implemented

`LightmapFile.LoadValidated` produces a session-owned validation receipt. On
reload the source/dependency key is recomputed and the entire bounded cache
file is SHA-256 checked, including its header and compressed payload. Only a
matching key and identical previously validated file can reuse decoded pixels,
records and prepared probes. Changed/missing/corrupt files still take the full
validation/fallback path. Published data is treated as immutable; there is no
global cache of levels or changed cache format.

`LevelBakeSession` holds the existing atlas while validation runs, but does not
apply unvalidated lighting to the new revision. A matching result reuses that
GPU atlas. Model bindings are fence-retired on revision changes and also checked
against the current model instance, avoiding references to replaced models.
The existing active/one-alternate scenario bound is preserved. Cancellation,
fallback, level changes and disposal release the corresponding resources.
Periodic invalidation now also watches the derived cache's file metadata;
every reload reuse decision verifies full content, even with unchanged size/time.

Warm diffuse-read allocation: **50,594,234 bytes before** versus **384 bytes on
validated reuse** (full decode remains approximately 50.6 MB). Six reloads in
the isolated GPU test preserve the same payload/atlas and exact rendered pixels.
63 reader checks, 60 reload/lifetime checks, 282 existing indirect-bake checks,
on/off/idle/save-load/undo comparisons and three strict/package/F2 image pairs
pass. See [validation results](ENGINE_UPGRADE_VALIDATION.md) for report paths
and subsequent whole-process measurements. These are not a blanket gate pass.

The diffuse-only unpaused 18-cycle repeat completes 216 actions and passes
gameplay timing (16.666 ms mean, 16.811 ms P95, zero >33.3 ms), but **fails**
the unchanged memory guard at **128.05 MiB**. That failure prompted the next
independent correction below; it is not rounded down or replaced by a GC sample.

## Second Correction Implemented

`RmlUiOverlayRenderer` now owns reusable vertex/index/prepared-command staging
lists. Capacity grows in powers of two only after the existing limits pass:
65,536 vertices, 131,072 indices and 512 commands. Only populated spans upload.
Local read-only native spans replace boxing marshalling calls; their pointers
never escape the render-data lifetime. Retained commands contain only texture
IDs, scissor values and draw offsets, not native addresses. Disposal releases
staging capacity. All signed-index, finite-value, count, fallback and clipping
checks remain. No HUD, inventory, interaction, texture or shader redesign.

Warm 64-command synthetic overlay allocation falls from **72,976 to 653 bytes
per frame**, including test/reflection/renderer overhead. 36 geometry/lifetime
checks pass against the pre-change pixel reference; the real inventory PNGs
are byte-identical to the pre-change captures. 1,053 warmup assertions,
three-resolution native geometry and existing inventory input checks pass.

Reflection sessions and snapshot construction remain unchanged. No forced
gameplay GC, physics/quality changes, migration or rebake. Whole-process
measurements after both fixes are recorded in the linked validation results.

The unpaused 18-cycle combined repeat passes at **80.55 MiB** growth, zero
asset growth, 16.666/17.990 ms gameplay mean/P95 and zero intervals >33.3 ms.
The fresh six-cycle repeat also passes at **50.46 MiB**, 16.661/17.769 ms
gameplay mean/P95, zero asset growth and zero intervals >33.3 ms.
This supports keeping both corrections, not closing all acceptance. Next is
the unchanged broader/repeated/manual acceptance and independent stall
investigation. Only pursue additional reflection/snapshot reuse if subsequent
measurements justify it; do not expand the fix merely because it was listed.

## What The Next Tests Establish

The new test helper suspends only the owned soak process, maps Windows committed
regions against CLR active segments and reusable/native CLR regions, and resumes
it before writing its report. It does not force a collection. GPU local/nonlocal
budget usage is sampled separately. The earlier last-collection GC counters
cannot by themselves classify all current process commitment.

The three short controls locate most growth in **CLR-owned capacity**, including
reusable free-region pools. Unattributed private memory falls slightly at the
matched endpoints. GPU usage does not show accumulating texture payloads.
This supports reducing allocation pressure, not a graphics-driver rewrite or
an assumption that every increase represents a permanently leaked object.
Long-repeat details and limitations are retained in the investigation report.

The 18-cycle repeat reproduces **139.52 MiB process growth**: active GC regions
increase **143.125 MiB**, other CLR regions decrease 2.375 MiB, and unattributed
private pages decrease 1.477 MiB. GPU usage falls and asset payloads stay flat.
After post-timing diagnostic collection, no tracked old entities/meshes survive,
but substantial free capacity remains. This is evidence for allocation/lifetime
correction, not a promise that the first proposed edit will solve every run.

These intrusive captures deliberately fail the unchanged frame limits while
paused. They establish attribution, not acceptance. The ordinary 128 MiB
process / 8 MiB asset guard still applies to final uninstrumented tests.

## 1. Avoid Unnecessary Lighting Reload Allocation

First production correction, kept inside the existing lighting implementation.
The diffuse portion above is implemented; reflection reuse remains unimplemented:

- Investigate reuse of the current validated diffuse/reflection results when
  F5 reloads unchanged source content. An editor revision change alone should
  not force large equivalent payloads to be decoded and uploaded again.
- Retain only a bounded current-level/current-scenario working set. Account for
  CPU payload, scratch, in-flight jobs and GPU retirement together. No global
  unbounded cache of every level visited; never retain old runtime entities or
  colliders through lighting ownership.
- Reuse requires matching source/dependency and cache-content identity. Check
  model/material/light changes, external cache replacement/corruption, format
  version and scenario state. Path equality or a timestamp alone is insufficient.
- Preserve owned immutable results. Scratch buffers must not escape to active
  users. Keep cancellation, stale/missing/corrupt fallback and independent
  concurrent readers. GPU disposal must still wait for its last use, and a
  replacement must be ready before the old visible resource is retired.
- Start with diffuse payload/revision handling in `LevelBakeSession` and
  `LightmapFile`; then measure before changing reflection sessions. Do not
  combine all ownership changes into one unreviewable patch.

Why first: repeated lighting preparation creates large temporary buffers and
snapshot copies; previous reader measurements still show about 50.6 MB of
managed allocation per diffuse load. The new address maps identify GC large/
free-region capacity as a significant contributor. This is a measured target,
not proof in advance that a particular cache design will clear the guard.

Verification: allocation counters for unchanged versus genuinely changed
reloads; existing lighting cache/ownership/cancellation tests; on/off/on and
save/load states; exact image references and resource retirement checks.

## 2. Reuse UI Geometry Staging Storage

Implemented independently after measuring the diffuse-only result. Preserve:

- Reuse bounded vertex/index/command staging capacity per renderer. Upload only
  the valid used ranges instead of allocating per-command arrays, aggregate
  lists and final `ToArray` copies every frame.
- Preserve maximum counts, signed-index bounds, finite vertex/UV validation,
  texture fallback, scissor, draw order and rejection of malformed commands.
- Clear valid lengths/references between frames; never retain native command
  pointers beyond their documented lifetime. Release storage on disposal.
- Do not change inventory layout, stacking, overflow, input or gameplay rules.

Why separate: the earlier 18-cycle route allocates about 1.2 GiB cumulatively
in UI drawing even without attributing background workers. This is avoidable
churn, not 1.2 GiB of retained UI data or proof of a native UI leak. The fallback
reload control also grows, so UI-only correction cannot be assumed sufficient.

Verification: native inventory/HUD pixel checks, warmup checks, malformed command
tests, resize/disposal/recreation and per-frame allocation measurements after
warmup, followed by the same native/fallback reload comparisons.

## 3. Re-measure Before Expanding The Fix

Repeat the same attribution matrix after each isolated change. If significant
growth remains, profile the remaining large allocations and reduce redundant
lighting snapshot/JSON construction only where measured. Keep this within the
existing lighting lifetime; no general engine/editor architecture refactor.

The diagnostic frame recorder itself retains bounded history. Keep its cost
visible and use a separate no-recorder memory-only control if needed. Do not
subtract an estimate from the guard, truncate evidence or force collections
to manufacture an acceptance pass. Escalate to native allocation-stack tracing
only if the unattributed/native or GPU categories actually grow in a repeat.

## 4. Acceptance Before Handover

Run sequentially on the normal desktop, without concurrent builds:

1. Release build, allocation/ownership tests and existing gameplay/physics/image
   regressions. Compare historical references, not newly replaced baselines.
2. `RunGameplayStabilityTests.bat` and `RunFramePacingTests.bat`, with normal
   VSync, full texture detail and unchanged limits.
3. Documented 18-cycle full-detail, streaming-control and six-room reload runs;
   repeat the six-room control to check variability. Keep intrusive address
   snapshots out of these acceptance runs.
4. Complete outstanding manual gameplay/F2 lighting acceptance, including moving
   objects, repeated lighting states, room transitions and save/load. Preserve
   standalone-editor layout and input.

Required outcome: existing memory and frame limits pass without changing the
solver, timestep, physical parameters, quality, or GC policy. Investigate the
historical intermittent full-detail stalls separately; stable memory or average
FPS does not explain them. Record remaining limits honestly before clearing
either gate. No C++ migration, new audio/AI/editor system, cache migration or
rebake is justified merely by this diagnostic round.

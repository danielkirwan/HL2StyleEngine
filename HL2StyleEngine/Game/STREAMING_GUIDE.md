# Visibility And Asset Streaming

Current stability work (2026-10-27): gameplay hitch diagnostics and per-crate support reuse do not change model streaming or physics ownership. Inventory preparation is separate: at most one icon attempt per loading frame in each cache, with independent 16 MiB ImGui/native icon admission budgets and bounded native title preparation. [Current first-open results and limits](INVENTORY_PERFORMANCE_GUIDE.md), [earlier crate investigation](HITCH_STABILITY_GUIDE.md). This is not adaptive model residency; normal game models still use full mip chains.

Experimental finer residency (2026-10-27): an isolated gallery now requests lower-resolution mip tails, plans texture budgets with pins/hysteresis and performs worker-prepared, fence-safe model replacement. Normal game streaming below is unchanged and still loads full texture chains. Budget estimates exclude transient overlap/geometry/driver memory; texture-granularity and room-aware detail demand remain open. [Launcher, validation, full-game hitch failures and explicit limits](TEXTURE_MIP_GUIDE.md).

Loading follow-up (2026-10-27): optional immutable asset snapshots now bypass redundant model-preparation source reads, with first-playable diagnostics and strict-source fallback. Existing required/wanted pins, two workers, upload budgets, eviction and ready-before-replacement are unchanged. Default authoring stays content-hash validated; this does not add mip-level or logical/physics streaming. [Launcher, warm timing evidence and cold-start work still open](LOADING_PACKAGE_GUIDE.md).

Second loading pass: the same package metadata now avoids repeated model hashing during reflection/diffuse validation, including propagated room requests. Shader and lighting payload validation remain; collision hashing remains strict. Timing coverage and no-build probes are available, but total-startup improvement is not demonstrated. Streaming budgets, prioritization and resource retirement are unchanged; inspect the current guide results before assuming a cold-load or memory gain.

Render extraction follow-up (2026-10-26): gameplay snapshots are built after model pumping/eviction and borrow the existing fenced handles for one frame. Shared main/shadow transforms do not change loading, pinning, replacement warm-up, entity ownership or save state. `renderStress12Room.json` also opts into room visibility; its larger area exercises repeated geometry, not a larger unique-asset memory budget. Three to six paired-room reflection captures reside during the tested route. No cold-load improvement or model-eviction result is claimed. [Measurements and architecture](RENDER_SUBMISSION_GUIDE.md).

Optional room-aware extension (2026-10-26): `RoomVisibility` adds explicit main-view room/aperture traversal and connected dependency prefetch. Missing/invalid/disabled graphs keep the XZ-zone path below. Models, replacement/debris dependencies, existing upload/fence budgets and shadow/held owners retain their contracts. Bake instance bindings are prepared in bounded batches; the whole atlas stays resident. Reflection hints follow active/doorway/capture pins under the existing slot budget. `sixRoomVisibilityTest.json` was the initial opt-in fixture; the twelve-room fixture above also uses it. [Workflow, A/B measurements and limits](ROOM_VISIBILITY_GUIDE.md).

Implemented first pass: 2026-10-06. Enabled by default for existing levels; no level conversion or new launch command is needed.

## What Changes

- Main-camera frustum culling skips offscreen primitives and model parts using conservative transformed bounds. Rotations, negative/non-uniform scales and moving objects are supported. Off-camera shadow casters still render when relevant to a shadow light. Weapons and gameplay UI are not culled with the world.
- Runtime dependency manifests divide the level into XZ spatial zones. Each zone records model paths, including replacement/debris dependencies, from world-space bounds. A model spanning multiple zones is shared, not duplicated. Very large/invalid bounds conservatively create global dependencies.
- Current and neighbouring zones are prefetched. Visible/nearby entities, shadow-relevant entities, player/weapon assets, held objects and live debris keep their resources available. Current replacement visuals remain pinned while the next model prepares.
- Two workers prepare model data. Existing soft 2 ms render-thread upload budgeting remains. Required models are serviced before speculative prefetch; missing critical resources show the loading overlay and pause fixed-step gameplay until ready or failed. Neighbour-only prefetch does not pause gameplay.
- Distant unused models are evicted after the retention interval, at most two cache entries per frame. GPU resources retire only after a submission fence completes, without a per-frame `WaitForIdle`. Shared textures release only when their last model owner releases them. The previous working set is retained until incoming dependencies are GPU-ready or have failed.

This is **visual asset streaming**, not full entity/physics streaming. Logical entities, stable IDs, colliders, scripts, inventory, collected-item flags, puzzle/door/light state and saves stay resident. Leaving an area does not reconstruct or reset them. Inventory/UI resources retain their existing independent ownership. F2 editing uses full model residency; standalone HS2Editor retains its own authoring cache and benefits from shared main-view culling.

## Editor Settings

Shared Toolbar > Asset Streaming exposes level settings saved in the level's `Streaming` object:

| Setting | Default | Meaning |
| --- | --- | --- |
| Enabled | true | Enable runtime visual residency management. Disable for a full-residency reference. |
| Zone Size | 32 | Spatial cell width/depth in world units. These are automatic cells, not named rooms or door portals. |
| Neighbour Rings | 1 | Prefetch one surrounding ring (up to nine cells including the current one); editor range 1-4. |
| Soft Budget MiB | 256 | Budget for owned model GPU geometry plus shared model texture payload. This is not total process RAM or total VRAM. |
| Retain Seconds | 15 | Keep unused resources briefly to reduce repeated loads while backtracking. Under budget pressure the grace period is at most two seconds. |

The budget is deliberately soft: visible, near, shadow-relevant, held and otherwise required resources are never evicted just to meet it. Large sightlines, shared assets or overlapping zones can keep the whole level's models resident. Frame-fenced retirement can temporarily retain physical allocations after the ownership counters decrease. Model-level residency does not stream individual texture mips.

The current six-room walking route shares its 32 models across nearby zones and remains at approximately 99.7 MiB of owned geometry/shared textures. Do not expect a memory saving there. A separate forced distant-area test verified release/reload, reducing that fixture's owned payload from 81.4 to 28.6 MiB. These are different fixtures, not before/after measurements of the same level.

## Testing

### Baked Lighting Ownership (2026-10-22)

Opt-in indirect baking retains one validated atlas for the current level, independently of zone/model residency. Per-instance baked geometry is created lazily and retired after 120 frames without use; atlas and buffer retirement is frame-fenced. Model and lightmap uploads share the soft 2 ms budget, but one atlas/part upload is not preemptible. Missing/stale bakes render through the existing realtime path. This does not add full atlas/probe streaming.

Optional probe volumes are also retained for the current level. Their fixed-geometry visibility BVH is built on the loading/baking worker, not during render submission. Bounds cap volumes at 4,096 samples and 16,384 occluder triangles; runtime sampling caches at most 1,024 object keys and retires inactive entries. The 196-probe fixture has 14,112 bytes of coefficients and 47,088 bytes of occluder positions, plus CPU BVH/metadata/cache overhead, outside the model residency counter. Probes add no GPU texture or extra per-object upload: packed coefficients fit the existing 256-byte slot. Level changes/stale edits discard the probe contribution and cache. Future atlas/probe area streaming must retain these ownership guarantees.

Bake allocations are additional to the model soft budget/counters: the test uses 8 MiB for its atlas plus about 0.19 MiB for all 17 instance buffers. CPU pixel storage is retained too. Model vertices now include a second generated lightmap coordinate (48-byte GPU stride), increasing geometry memory even for unbaked models; source/model cache geometry is unchanged. The latest six-room route reports 107.9 MiB of model geometry/shared textures, and the updated forced-eviction fixture reports 88.0 -> 32.4 MiB. Earlier measurements above are historical, not current totals. See [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md).

### Regression Commands

Use `LaunchSixRoomFlashlightTest.bat` for the existing dim-ambient flashlight test; `LaunchSixRoomTest.bat` retains the lit version. Both use Release. Walk, turn, open doors, break crates and carry objects across areas, then return. Materials, collision and interaction state should remain unchanged. Large jumps into uncached areas can show loading; no zero-stall guarantee is made.

After building the authoring tool in Release:

```powershell
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-visibility-streaming
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-level-rendering
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on --reference-visibility
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight off
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-gameplay-flashlight on --stress
```

`--reference-visibility` disables main-view culling and enables full visual residency, while keeping the accepted uniform batching. It can be combined with `--stress`. Run comparisons sequentially under the same normal-desktop presentation conditions. The scripted stress test opens shutters, destroys six crates, holds/releases a prop and opens/closes inventory; it does not save progress or replace a manual input/feel playtest.

See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for measured results. Remaining work includes true cold/warm first-playable measurements, portal/occlusion-aware dependencies, full entity/collider streaming and longer heterogeneous-asset memory-soak tests. Do not remove logical objects or their colliders as a visibility optimization.

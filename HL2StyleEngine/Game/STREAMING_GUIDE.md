# Visibility And Asset Streaming

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

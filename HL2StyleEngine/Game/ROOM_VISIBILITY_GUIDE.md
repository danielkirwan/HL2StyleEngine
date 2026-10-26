# Room Visibility And Connected Prefetch

First pass implemented and validated, 2026-10-26. Step 7 was brought ahead of atmosphere to protect larger indoor-level performance. C# remains the runtime. No atmosphere effects, physics replacement, flashlight retuning or language migration are part of this change. This does not declare the earlier flashlight appearance concern resolved.

## Scope

- Optional level-level `RoomVisibility` graph, independent of automatic XZ streaming zones and reflection influence volumes. Stable room/doorway IDs, world-space axis-aligned bounds, rectangular aperture and optional probe link.
- Shared F2/HS2Editor Toolbar > Rooms And Doorways: create/edit/remove rooms and connections, visible bounds, undo/redo and save. Room geometry is authored explicitly; this does not automatically discover openings or infer airtight rooms from meshes.
- Conservative main-view portal traversal. Whole potentially visible rooms remain drawable. Doorways remain potentially open even when a door is closed or partly open; door-driven occlusion is deliberately not assumed from collision bounds. All openings/windows between authored rooms must have a connection.
- Connected-room model/replacement prefetch; prepare eligible bake instance bindings in small batches through the shared upload budget. The level-wide diffuse atlas/probes remain resident. Linked reflection probes are speculative hints under the existing slot budget, after active/doorway/capture pins.
- Shadows, held objects, live debris, collision, scripts, inventory and puzzle/save state are independent of main-camera visibility. No entity or collider unloading. Spanning/unassigned/invalid bounds are global. Invalid graphs, excessive traversal or cameras outside rooms fail open.
- New isolated `sixRoomVisibilityTest.json` and `LaunchSixRoomVisibilityTest.bat`. Source geometry, lights, materials, existing test levels and startup defaults remain unchanged. Visibility metadata is excluded from diffuse/reflection energy fingerprints so compatible caches can be reused without rebaking.

## Playtest And Authoring

Run `LaunchSixRoomVisibilityTest.bat` at the repository root. It builds Release and explicitly loads `sixRoomVisibilityTest.json`. F toggles the torch; E operates the existing fixture-power lever. Existing puzzles, shutters, pickups, gravity gun and crates are unchanged. Visit side rooms, turn quickly at thresholds, carry a prop across a doorway and compare lights on/off. The old launchers and project startup selection are unchanged.

In HS2Editor, open that level and expand **Toolbar > Rooms And Doorways** (also available in F2):

1. Enable **Show Room Bounds** for cyan room outlines and green doorway apertures. They draw through walls without changing scene depth or filling models with colour; turn the overlay off for a clean view.
2. Edit room names, world-space centres/sizes and optional reflection probe links. These volumes are independent of geometry, physics and reflection influence bounds. No room-volume gizmo dragging or prefab embedding is added in this pass.
3. Connect Room A/B using each doorway's world centre, aperture size and normal axis. X means passage across X, with an opening in Y/Z; Z means passage across Z, with an opening in X/Y. Fit the aperture around the **entire** opening, not the moving door leaf. Include windows and every alternative opening. Rooms should meet at doorway centres.
4. Enable **Room Visibility**, choose **Prefetch Connections** (0-4, default 1), save, then play. Removing a room removes its connections. Undo/redo and JSON/RE:Dox loading preserve IDs. Graph-only edits do not require rebaking; geometry or light edits still do.

Invalid bounds/links, more than 64 rooms or 128 connections, excessive traversal, invalid camera matrices or a camera outside the volumes use all-visible rendering. A 20cm aperture crossing band keeps both sides eligible around the near plane. Missing connections in an otherwise valid graph cannot be detected automatically: disable Room Visibility while correcting authoring errors.

## Validation Results

- 347 CPU checks: room membership/turns, 105 threshold positions, invalid/null/duplicate data, cyclic traversal cap, global/spanning bounds, connected prefetch, reflection pin priority under a three-slot limit, undo/redo, disk JSON/DOX round trips and both lighting-state cache identities.
- 151 actual-game GPU comparisons against room/frustum culling disabled: 96 room/direction/torch combinations, 25 closed/partial/open shutter render poses, 25 threshold views and five held-prop crossing poses. Maximum RGB difference 1/255; shadow light owners and entity IDs unchanged. 68,234 draws avoided in aggregate versus the fully all-visible reference, not versus existing frustum culling. Full gameplay stress below separately exercises real physics/holding/destruction.
- F2 room-outline image check passes. Game/F2/standalone editor scene captures remain textured. All 15 existing Legacy references pass within 1/255 without replacing images. Existing 943 reflection-streaming checks and 3,000 transformed-bound cases plus GPU-ready handoff/eviction/state tests pass.
- Initial validation caught and fixed near-plane threshold rejection and depth-hidden authoring bounds. These failures were not accepted by relaxing the image threshold.
- Captures: `Tools/LevelAuthoring/bin/Release/net10.0/qa/room-visibility-20261026-122241` and `qa/room-editor-20261026-122041`. Native mouse/docking interaction and long manual gameplay acceptance remain open.
- Release solution and Debug/Release authoring builds are clean; the 347 checks also pass in Debug. The earlier 69 shadow fixture checks pass. Game and HS2Editor Release each pass 61 package hash/cache checks. Exact `LaunchSixRoomVisibilityTest.bat` builds, loads and exits normally under the 60-frame smoke harness; 16.58ms mean / 24.08ms P95 is a stationary launch check, not an additional pacing-gate pass. Whitespace validation passes.

Normal-desktop Release, RTX 3060, 1920x1009 client area, VSync, 120 warmup + 300 measured frames per run:

| Workload | Room visibility | Wall mean / P95 ms | Main draws/frame | Warm resources ready ms |
| --- | --- | --- | --- | --- |
| Torch on, destruction/holding/inventory stress | On | 16.775 / 17.899 | 423.8 | 2926.1 |
| Same stress | Off, frustum/streamer retained | 16.661 / 17.833 | 423.4 | 2642.2 |
| Torch off, stress | On | 16.661 / 17.794 | 419.7 | 2704.1 |
| Torch on, six-room route | On | 16.659 / 18.179 | 81.9 | 3155.5 |
| Same route | Off, frustum/streamer retained | 16.660 / 17.823 | 188.3 | 2864.4 |
| Torch on, four lighting switches | On | 16.658 / 17.934 | 389.3 | 3024.6 |

All six runs pass unchanged mean <=17ms / P95 <=21ms / hitch / streaming-wait gates, with zero >33.3ms or >100ms frames and zero streaming waits. Stress reaches 120 fragments and five event groups. Approximately 59.6-60.0 FPS by wall mean, **not a locked minimum 60**. The route uses five test-only teleports plus normal movement, not continuous walking through doors. Portal traversal averages 0.013-0.023ms CPU; this excludes entity-bound tests and prefetch work. Host World time for the route was 3.16ms enabled versus 3.46ms reference; short sequential runs are not a statistically isolated GPU speedup measurement.

The side-room route submits about **56.5% fewer main draws** beyond existing frustum culling. The central-hall stress has effectively identical draws because connected rooms are already potentially visible. Shadow work stays independent. There is no claimed memory or startup-speed saving: all runs retain 34 shared models / 108.2 MiB geometry+textures, 104 MiB shadow atlas, and about 12.76 MiB for both six-probe reflection banks. Bake resources reach 37.15 MiB with proactive bindings (reference stress 35.84 MiB). Prefetch trades some memory/preparation work for readiness. The listed startup measurements begin at module initialization, with warm OS/cooked caches, and require critical models, both reflection states and a ready bake with at least one binding. They exclude earlier host creation and are **not cold disk or full first-playable measurements**.

## Reproduction

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release, then use `.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll` with:

```text
validate-room-visibility
check-room-visibility-pixels
capture-room-visibility
benchmark-room-visibility on --stress
benchmark-room-visibility on --stress --reference-rooms
benchmark-room-visibility off --stress
benchmark-room-visibility on --route
benchmark-room-visibility on --route --reference-rooms
benchmark-room-visibility on --switches
validate-room-visibility-package Game/bin/Release/net10.0
validate-room-visibility-package HS2Editor/bin/Release/net10.0
```

Run GPU benchmarks sequentially on the normal desktop, without concurrent builds/cooking. `--reference-rooms` disables only this milestone, retaining frustum culling and the prior zone streamer. `--reference-visibility` also disables frustum culling/model streaming and is a broader diagnostic. `build-room-visibility` creates the fixture only if absent and never overwrites editor changes.

## Remaining Work

Whole-atlas/diffuse-probe, logical entity and physics streaming; arbitrary occluders; automatic portal generation; door-state-driven occlusion; cold OS-cache first-playable profiling; larger unique-asset stress and long manual gameplay soak are not delivered here. Reflection hints obey existing budgets, but this accepted small fixture intentionally retains all captures to preserve lighting stability. Alternate diffuse-state prefetch remains separate work. Next, validate a larger authored room graph and consider a managed render-scene snapshot/draw grouping before adding budgeted fog/bloom. Do not replace the current lighting shaders or migrate to C++ without profiling evidence.

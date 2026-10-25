# Reflection Residency And Six-Room PBR Test

Visual follow-up (2026-10-25): fixed-camera tests exposed approach-dependent darkening from three-slot reflection availability in the fixture-only variant. New `sixRoomLightingStabilityTest.json` intentionally keeps all six captures resident, with up to one inactive on/off bank. This original streaming level and its three-slot budgets remain unchanged. [Measured diagnosis, quality/memory tradeoff and future visibility-aware prefetch](LIGHTING_STABILITY_GUIDE.md).

Implemented 25 October 2026: unit F's first bounded residency and controlled real-level rollout. C#/.NET remains the runtime. This is not room/entity/physics unloading or a new renderer. Original levels, startup selections, shaders, HUD and docking are unchanged.

## Try It

Run root `LaunchSixRoomPbrTest.bat`. It builds Release and opens `Game/Content/Levels/sixRoomPbrTest.json`, an independent copy of the six-room flashlight level. The original `LaunchSixRoomFlashlightTest.bat` is still available for comparison.

- F toggles the flashlight. The copy also has faint ambient fill and six low-intensity, fixed room lights, with warmer/cooler room colours.
- Existing puzzles, shutters, pickups, weapons, object transforms and colliders remain. Open rooms using their existing puzzles. A carryable metal sphere near the starting area makes reflection differences easier to inspect with the gravity gun.
- Six 128-square HDR captures are already cooked beside the level. At most three probe slots are occupied, including fenced evictions. No capture/filter pass runs during gameplay.
- Crates use the existing material-only wood correction. Runtime fracture recognition now includes that variant, preserving hidden pieces, destruction, rewards and debris instead of falling back to generic breaking.
- Open this same JSON in HS2Editor, or press F2. **Toolbar > Room Reflections** contains **Stream Room Reflections**, resident limit, doorway prefetch distance, retention, **Unload / Reload Rooms**, bounds and preview toggles. Status shows authored/ready counts, loads, evictions and retiring slots. GPU bytes include resources pending retirement.

Walk towards a doorway and back, carry the metal sphere into another room, break a crate with the flashlight on, and reload the room captures from the toolbar. Reload must not reset doors, collected items or inventory. Temporarily disabling Preview Room Reflections is the clean specular comparison; switching the entire lighting model is a different comparison.

## Policy And Ownership

Optional level field `ReflectionStreaming` contains `MaxResidentProbes` (3-8), `PrefetchDistance` (0-100 metres) and `RetainSeconds` (0-60 seconds). Defaults when enabled are 3, 8 and 5. Null preserves the earlier all-resident automatic mode. The manual global-preview mode is unchanged. Settings support JSON/RE:Dox, save and undo/redo and do not invalidate diffuse or reflection bake keys.

Selection prioritizes the active room, both endpoints while inside an authored doorway, then a selected in-progress editor capture. Connected doorway neighbours within the prefetch distance follow; recently visited residents fill unused slots until retention expires. A small resident preference reduces equal-distance churn. Unconnected rooms are never prefetched solely because they are nearby. Outside all volumes, retained rooms eventually expire. Residency does not determine shader membership: existing per-part selection, pixel membership and doorway blending still apply.

One immutable level revision and resolved asset-path map are prepared on the owning thread. Up to two workers prepare/validate cached captures; they never access mutable editor/game resolver caches. Source edits cancel outdated work. Face/mip uploads share the existing soft 2 ms render-thread budget. Cancelled/evicted GPU resources use the renderer's frame fence; retiring sessions retain their slots until disposal, so teleporting cannot allocate an unbounded replacement set. No routine WaitForIdle is added. Resource sets are retired before the environments they reference.

Each 128 capture uses 1,114,320 GPU payload bytes including metadata. Three captures, eleven room/link sets and the shared black fallback use **3,345,700 bytes (3.191 MiB)**; measured route peak including retired binding sets was about **3.193 MiB**. Driver/common resources are additional. Editor recapture temporarily requires private capture geometry/scratch and can retain old/new captures; it is not constrained to the gameplay payload budget. Cooking this larger level peaked around **91.13 MiB** tracked GPU resources, not runtime cost. Six disk caches total about 6.38 MiB.

Missing/stale/corrupt captures stay black for specular indirect; they do not hide models, change colliders or replace a missing room with another room's bright capture. Fixed-light incompatibility still suppresses a capture. Gameplay entities, IDs and puzzle/inventory/light state remain independently resident. The existing XZ model/texture streaming system is separate.

## Validation

New `validate-reflection-streaming` checks all original gameplay entities against the copied fixture, settings round-trip and undo, capture/diffuse-key independence, portal pinning, neighbour prefetch, retention/expiry, invalid settings, six-room GPU loading, bounded resident-plus-retiring slots, zero upload budget, distant visits and revisits, forced unload/reload, rapid cancellation, main-thread resolver isolation, missing-file fallback and recovery. Polling assertions vary with scheduling.

Actual host tests use Release/D3D11 debug layer, RTX 3060, RGBA16F/4x MSAA, VSync, 1920x1009 client, 120 warmup plus 300 measured frames. Full gameplay stress opens shutters, breaks six crates (120 peak pieces), holds/releases a prop and opens/closes inventory. A separate loading route performs five test-only player teleports during measured frames, with ordinary Update/physics/input/UI/presentation active; it is not a walking-only route.

| Route | Flashlight | Wall Mean | Wall P95 | Frames >33.3 ms |
| --- | --- | ---: | ---: | ---: |
| Gameplay stress | On | 16.716 ms | 17.090 ms | 0 |
| Gameplay stress | Off | 16.661 ms | 16.939 ms | 0 |
| Loading/eviction | On | 16.666 ms | 17.638 ms | 0 |
| Loading/eviction | Off | 16.661 ms | 17.027 ms | 0 |

All pass unchanged mean <=17 ms, P95 <=21 ms, <=1 frame over 33.3 ms, no >100 ms frame and zero model-streaming waits. One main object upload/frame remains; 33 resident models use 108.1 MiB. These short local results average about 60 FPS, not a guaranteed minimum on all hardware. The initial synchronous-preparation route failed (17.160 ms mean, two >33.3 ms frames); snapshot validation was moved off the render thread before acceptance. No limits were widened.

Game/F2/HS2Editor scene captures were generated separately; game and standalone textures were visually inspected. Scene captures are not native docking/controller acceptance. Warm-filesystem capture readiness through game/F2 readbacks was 2.52 seconds; this is **not** cold first-playable timing.

Final verification: Release/Debug builds have zero warnings/errors; residency suites passed 945/957 assertions (poll count varies), including 17 admissions / 14 evictions in the cancellation/revisit run. Existing 106 transition, 50 capture and 282 diffuse/probe checks pass. All 15 Legacy images stay within 1/255 without changing references; corrected-crate flashlight checks pass. Final flashlight-on loading-route recheck: **16.661 ms mean / 17.059 ms P95**, zero >33.3 ms frames. Game Release, HS2Editor Release and a fresh Game publish each pass 45 SHA-256/cache-validity checks using their own packaged GLBs, all six captures and matching shaders. The published Game process completed a 60-frame smoke profile after 120 warmup and exited normally (16.65 ms mean). This is a local framework-dependent publish, not a clean-machine installer test.

Commands after building `Tools/LevelAuthoring`:

```text
build-six-room-pbr-test          (only if the fixture does not exist)
capture-six-room-pbr
validate-reflection-streaming
capture-six-room-pbr-views
benchmark-six-room-pbr on
benchmark-six-room-pbr off
benchmark-six-room-pbr-route on
benchmark-six-room-pbr-route off
validate-reflection-package Game/bin/Release/net10.0
validate-reflection-package HS2Editor/bin/Release/net10.0
validate-reflection-package <published-game-directory>
```

## Remaining Limits

- This is an opt-in six-room copy, not migration of every level or completion of every shipping gate.
- At most eight authored axis-aligned rooms; one outgoing doorway per probe, with multiple rooms allowed to link into a hub. Overlapping doorway volumes and complex multi-room surfaces still need careful authoring.
- Distant rooms visible through an open doorway can have black specular until prefetched. There is no visibility-driven pinning or residency fade yet. Teleports can temporarily show fallback while data becomes ready.
- Captures are invariant-light approximations. Movable doors/props, flashlight, puzzle state changes and flicker are excluded. Closed shutters do not update the captured view. Explicit switched presets and matching baked diffuse scenarios remain open.
- Long traversal/soak, cold first-playable instrumentation, manual editor/input acceptance and other-GPU testing remain. Validate these before broadening defaults or adding bloom/atmosphere cost.

See [upgrade plan](ENGINE_UPGRADE_PLAN.md), [transition contracts](ROOM_TRANSITION_GUIDE.md) and [validation report](ENGINE_UPGRADE_VALIDATION.md).

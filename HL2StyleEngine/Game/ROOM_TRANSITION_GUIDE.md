# Room Reflection Transitions

Implemented 25 October 2026. Unit E's first pass adds room-aware selection, box projection and doorway blending on top of the validated room capture/cache system. C# remains the runtime. Existing lighting defaults, gameplay, source models, collision geometry and the two older reflection fixtures are unchanged. Their capture caches were recooked because the shader fingerprint changed; reference PNGs were not replaced.

## Test Level

Run root `LaunchRoomTransitionTest.bat`. It builds Release and opens new `Content/Levels/roomTransitionTest.json`, with two saved captures, one doorway transition and a carryable metal test object. Use the gravity gun to move the object between the rooms; F toggles the flashlight. The room-two lamp/screen switch remains realtime-only. The roughness/metal ramps remain available for comparison.

In F2 or HS2Editor, open **Toolbar > Room Reflections**:

- Enable **Automatic Room Reflections** for room-aware rendering. Disable it to return to the previous whole-scene single-probe preview. The setting supports undo/save/load and defaults off in older levels.
- **Capture Probe** selects which probe Capture / Rebuild operates on, not which room renders. Capture/reload/cancel and the preview checkbox remain available. All ready room captures are retained when preview is disabled.
- In the probe Inspector, **Influence Size** defines room membership and **Projection Size** approximates its reflecting surfaces. Both are axis-aligned boxes centered on the capture position, with positive inherited scale. Priority resolves overlaps; equal priority uses stable GUID order.
- Set **Doorway Neighbor** on one of the two probes, then edit **Doorway Offset**, **Doorway Size** and **Travel Axis**. Offset/size are local to that probe. The axis direction is inferred toward the neighboring capture. Use one link per pair, tightly enclosing the actual opening, not the shared wall. Both endpoint cross-sections must fit within their respective room influence bounds. Select None to remove the link.
- **Show Reflection Bounds** draws cyan influence bounds, orange projection bounds and green doorway zones. Links within a duplicated prefab remap to its cloned probe IDs. External links still need review when placing prefabs in a different level.

Automatic mode currently supports **1-8 probes and at most eight doorway links**, one outgoing link per probe (other probes may link back to the same hub). Invalid IDs, missing neighbors, duplicate pair links, invalid bounds/axes, rotated or moving probe ancestry fail to a black reflection fallback with a status message. They do not hide models or disable direct lighting. Bounds must fit the actual architecture; this is authored room membership, not a wall/portal discovery system.

## Rendering Contract

Selection uses each model part's transformed bounds, never distance from the camera alone. Each part binds one room, or one explicitly linked pair if it intersects their doorway or spans both rooms. The shader uses pixel world position to restrict the contribution to the room's influence bounds. Outside the doorway it selects the containing room without mixing across the shared wall. Inside the authored opening it uses a smoothstep spatial weight and blends linear HDR radiance before the material BRDF and tone mapping.

At most two filtered cubemaps are sampled. An unavailable neighbor remains black with its original weight; weights are not renormalized toward an unrelated bright room. Missing/stale captures and incompatible fixed-light state therefore cannot leave stale bright reflections behind. Resources become visible only after upload is complete. Replacement and mode changes use frame-fenced retirement; ordinary gameplay has no new WaitForIdle or capture passes.

Box projection intersects the reflected ray with the room's projection box, then samples from the capture position toward that exit point. Nearly parallel axes are ignored safely; points outside projection bounds fall back to the uncorrected direction. This improves wall positioning but is still an approximation, not a mirror, SSR or a reflection of live doors/characters. Door openings do not automatically become occlusion barriers for captured reflections when closed.

Room/pair resource sets and their metadata are immutable between configuration/readiness changes. The existing 256-byte per-object payload and batched object upload remain unchanged. Resource set 6 adds a second cube at t8 and expands its own b4 metadata to 224 bytes; t6/t7/s4 retain their existing roles. Matching TexturedModel VS/PS CSOs are rebuilt alongside HLSL. Old synthetic and direct/Legacy reference images remain within 1/255.

This stage keeps all validated probes in the small bounded set resident. It is **not** room-distance eviction/prefetch, and the existing asset XZ zones are not treated as room IDs. A large model part spanning more than one connected pair still needs splitting or more conservative authoring. Overlapping/unconnected volumes cannot infer which side of a wall an object belongs to. There is no automatic capture of moving objects.

## Lighting And Cache Compatibility

This original fixture's captures remain **Invariant Only**. Switch-controlled/flickering lights and their linked emission remain excluded. Disabling a captured fixed light immediately substitutes black bindings; restoring the compatible state reuses the retained ready data. The separate [lighting-stability fixture](LIGHTING_STABILITY_GUIDE.md) now supports explicit bounded on/off scenarios with matching bakes/captures; arbitrary combinations still fall back safely.

Automatic-mode and doorway-link edits do not change capture energy and are omitted from capture keys. Influence/projection/priority edits conservatively invalidate caches, along with the existing geometry, shader, material, resolution and light dependencies. Rebuild after those changes. Probe authoring and automatic mode do not contaminate diffuse bake keys. Caches remain versioned/checksummed `.hs2reflection` files next to their source level.

Two ready 128-square environments, three room/pair metadata sets and black fallback use **2,229,588 bytes (2.126 MiB)** of tracked GPU payload. This includes a separate LUT per environment; it excludes driver/common resource overhead, capture scratch and old/new retirement overlap. The bounded eight-probe maximum is about 8.51 MiB before replacement overlap. Future residency work will add active/neighbor pinning and eviction rather than silently raising this limit.

## Validation

Current Release and Debug transition suites: **106 assertions**, including JSON/undo, diffuse/capture-key separation, cloned prefab links, missing-neighbor rejection, finite parallel-ray projection, smooth weights, HDR GPU/CPU projection parity, room/doorway GPU classification, spanning parts, solid-wall isolation, missing-neighbor black fallback, preview controls, source invalidation, fixed/realtime light changes, reload, cancellation after GPU submission and shared upload budgeting. Large integer priorities and stable-ID tie-breaking also match on the GPU. Per-pixel finite checks and single-upload assertions run through GPU readbacks. The new game/F2/standalone scene images are captured separately from historical references.

Debug/Release solution and authoring-tool builds pass without warnings or errors. Existing 97 synthetic reflection, 50 room capture and 282 indirect-bake checks pass, along with GPU shadows/moving-door/batching and corrected-crate flashlight tests. All 15 Legacy, three direct-PBR and three synthetic-reflection reference images stay within 1/255 without replacing references. Game and standalone scene captures were visually inspected. Source/new level and both fixtures' saved captures match their Game/HS2Editor Release output copies by SHA-256. Full publish and native mouse/controller/docking acceptance remain untested.

Release RTX 3060, D3D11 debug layer on, RGBA16F/4x MSAA, VSync, actual 1920x1009 client, 120 warmup + 300 measured host frames:

| Flashlight | Wall Mean | Wall P95 | Frames >33.3 ms | Streaming Waits |
| --- | ---: | ---: | ---: | ---: |
| On | 16.663 ms | 17.042 ms | 0 | 0 |
| Off | 16.665 ms | 17.030 ms | 0 | 0 |

Both average about 60 FPS, retain one main upload/frame and show no runtime capture work. These are short local regression checks, not a locked-60 minimum or other-hardware guarantee.

1920x1061 render-only traversal through both rooms, reflection preview off/on, 30 warmup + 180 measured frames per repeated mode: GPU mean/P95 increment +0.116/+0.296 ms torch off and -0.155/-0.305 ms on. Reverse order after the spanning-part adjustment: +0.191/+0.303 ms off and -0.020/-0.255 ms on. CPU increments are below +0.04 ms. Both orders pass the unchanged <=1 ms GPU increment gate; negative differences are clock/load noise, not speedups. This route includes camera traversal, but is not a manual player/door/gravity-gun playtest.

Commands after building `Tools/LevelAuthoring`:

```text
build-room-transition-test       (only if the fixture does not exist)
capture-room-transitions
validate-room-transitions
capture-room-transition-views
profile-room-transitions
profile-room-transitions --reverse
benchmark-gameplay-room-transitions on
benchmark-gameplay-room-transitions off
```

Follow-up: unit F's bounded active/neighbor residency and a separately migrated six-room copy are available in `LaunchSixRoomPbrTest.bat`; explicit switched presets are now available in the separate [lighting-stability test](LIGHTING_STABILITY_GUIDE.md). This two-room fixture retains its original all-resident mode. See [residency guide](REFLECTION_STREAMING_GUIDE.md) for forced unload/reload, budgets and packaging checks. Cold first-playable instrumentation, longer soak and manual editor/input acceptance remain open; do not broaden rendering defaults before acceptance.

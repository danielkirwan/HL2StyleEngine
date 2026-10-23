# Authored Room Captures (Unit D)

Implemented 2026-10-23 after the controlled synthetic environment. C# remains the runtime. This is the first editor-owned capture/cache stage, not automatic multi-room blending or a live mirror. Original levels, the accepted PBR fixture, source art, physics and startup selections are unchanged.

## Try It

Run root `LaunchRoomReflectionTest.bat`. It builds Release and opens the new `Content/Levels/roomReflectionTest.json`, a separate copy of the two-room material fixture. Two 128-square room captures are already saved beside the level. The first room's capture is selected initially. The smooth metal spheres show the actual walls, ceiling and floor instead of the synthetic studio. F still toggles the flashlight; the second-room switch still operates its realtime lamp/screen.

F2 and HS2Editor share **Toolbar > Room Reflections**:

- **Add Reflection Probe** places a named, selectable entity at the selected object's position (otherwise 0, 1.8, 0). Existing transform tools move it.
- Select **Preview Probe**, then **Capture / Rebuild**. The level saves first. Progress/status covers dependency preparation, six faces, filtering, saving and uploading. **Cancel Capture** cancels outstanding work; **Reload Capture** validates the saved cache again.
- **Preview Room Reflections** toggles the contribution without discarding its ready cache. Existing PBR Specular Indirect/Full diagnostics still apply.
- **Show Reflection Bounds** draws cyan influence and orange projection boxes. The Inspector edits resolution (32/64/128), bounds, priority and capture mask. Fixed geometry is required; fixed lights, invariant emission and baked diffuse can be toggled separately.

For now, **the selected probe supplies the whole scene**. Bounds and priority are saved authoring metadata for unit E; they do not yet select/blend probes or parallax-correct pixels. Preview the second probe manually when inspecting room two. Do not use this first pass as a finished multi-room lighting system.

Probe transforms currently support fixed positive scale and axis-aligned parent chains. Rotated/movable parents are rejected with a status message. Stable entity GUIDs, undo/redo, JSON/RE:Dox saves, duplication and prefab cloning preserve settings; new prefab instances get fresh IDs and need their own capture. Adding/changing probe definitions conservatively invalidates affected level captures; recapture after editing.

## Capture Contract

Only eligible GLB entities marked **Static Lighting** participate: static, non-pickup, non-destructible, non-kinematic, without scripts/interactions or movable ancestors. All their dependencies are requested independently of gameplay camera visibility/streaming. Geometry is limited to 1,024 instances, 4,096 part draws and 256 MiB of estimated prepared geometry. Primitive blockout boxes are not captured in this pass.

The only preset is **Invariant Only**. Local lights must opt into **Fixed Light: Bake Indirect**, remain static and have no switch targets/groups, flicker, scripts or moving parents. Authored ambient/directional fill is retained. Unsupported lights and linked emission are excluded, not frozen on. Baked diffuse is included only when requested, enabled and valid against its current source key; stale/missing enabled diffuse requires rebaking before capture. Runtime overrides disabling a captured fixed light suppress its reflection until compatible again. Room-on/off preset variants are not implemented.

Six 90-degree views render linear RGBA16F before exposure, tone mapping and UI. The capture has private world/camera/object/shadow resources, a separate command list and a polled fence. One face is submitted at a time; buffers/readback are reused only after completion. It excludes the player, viewmodel, flashlight, held/dynamic props, debris, movable doors and helpers. Environment sampling is disabled to avoid recursive feedback. Probe markers are not runtime entities, colliders or shadow casters.

The shared `ReflectionFilter` preserves unit C's GGX/LUT convention and deterministic synthetic output. A worker filters captured HDR faces with cancellation checks; no capture/filter passes run during ordinary gameplay. This is a static single-capture approximation. Animated doors and current character images are not reflected.

## Cache And Resource Safety

Each probe writes `<level-name>.<probe-guid>.hs2reflection` beside its level. Versioned headers include a SHA-256 dependency key; the nested bounded environment payload has its own checksum, dimensions, finite HDR checks and filter version. Keys cover the coherent authored snapshot, transforms/materials, source GLB contents, compiled capture shaders, mask/preset/resolution and optional diffuse bake identity/content. Exposure and active preview choice are excluded. Other authored changes may conservatively invalidate more than necessary.

Writes use a temporary file and atomic replacement. Missing, stale, truncated, corrupt or incompatible files use black reflection fallback while direct/diffuse/emissive lighting and scene geometry keep working. Runtime reads/hash validation run on workers; revision changes invalidate immediately, and source length/mtime checks run every two seconds to trigger full key validation. Explicit Reload also revalidates contents.

GPU creation and face/mip/LUT uploads share the existing 2 ms **soft** main-renderer budget. The old capture remains active during same-input recapture until the replacement is fully uploaded; changed dependencies suppress it immediately. Cancellation/failure retains a compatible prior ready result. Retired capture/replacement resources use frame fences; shutdown drains before disposal. There is no new routine-frame `WaitForIdle`.

One ready 128-square environment uses **1,114,112 bytes**, plus the renderer's 68-byte fallback: **1.063 MiB**. Old/new upload overlap can temporarily double that. The fixture's tracked capture GPU payload peaks at approximately **43-44 MiB**, largely its private 32 MiB shadow array plus models/textures/targets; this is an editor/tool cost, not steady gameplay residency. Driver resources, common buffers, CPU source/filter scratch and retirement overlap are additional. Both cache files total about 2.13 MiB on disk. They copy to Game/HS2Editor outputs and publish through the Game content items. No shader changes or C++ rewrite were required.

## Validation

Release and Debug room checks pass (50 assertions in the final measured runs; polling can add one). Coverage includes six colored HDR faces with asymmetric orientation markers, deterministic shared filtering, validated diffuse capture, stable IDs/prefabs/undo, source/geometry/light/resolution invalidation, exposure independence, checksums/truncation/version/cancellation, restart, zero-budget readiness, fixed-light fallback and cancellation after GPU submission. Main camera/shadow output is bit-identical after capture/cancellation. Existing 97 synthetic reflection, 282 diffuse/probe, shadow/moving-door and 15 Legacy reference checks also pass. Game/F2/standalone scene images were inspected; native mouse/docking/controller acceptance is still a manual playtest.

RTX 3060, Release, D3D11 debug layer on, RGBA16F/4xMSAA, 1920x1009 client, VSync, 120 warmup + 300 measured frames:

| Torch | Wall Mean | Wall P95 | Frames >33.3 ms | Streaming Waits |
| --- | ---: | ---: | ---: | ---: |
| On | 16.661 ms | 17.844 ms | 0 | 0 |
| Off | 16.661 ms | 17.878 ms | 0 | 0 |

Both average about 60 FPS, keep one main object upload and report no runtime capture work. These short local runs are not a locked-60 minimum guarantee. Repeated offscreen GPU increment versus reflection preview disabled: -0.183/-0.189 ms torch off, +0.226/+0.057 ms on (mean/P95), within the existing <=1 ms gate. Negative results reflect clock/load noise, not a claimed speedup. Cold first-playable timing, longer soak and real-level migration remain open.

Final recheck after excluding probe helpers from shadow casting: full-host wall mean/P95 **16.667/17.887 ms on**, **16.663/17.963 ms off**, again zero frames >33.3 ms and zero streaming waits. Reverse-order repeated GPU increments are -0.131/-0.707 ms off and -0.243/-0.237 ms on; CPU increments +0.018/+0.014 ms off and +0.013/-0.011 ms on. Both orders pass the existing budgets; negative GPU differences remain clock/load noise. All three direct-PBR and three synthetic-reflection reference images pass within 1/255 without replacing references; the corrected-crate flashlight suite passes. Debug/Release solution and tool builds are clean. Source, Game Release and HS2Editor Release level/capture copies have matching SHA-256 hashes; publish metadata is configured but a full publish was not separately tested. The final room check still passes 50 assertions.

Build `Tools/LevelAuthoring/LevelAuthoring.csproj`, then use:

```text
build-room-reflection-test       (only when the fixture does not exist)
capture-room-reflections         (rebuild both fixture probes)
validate-room-reflections
capture-room-reflection-views
profile-room-reflections
profile-room-reflections --reverse
benchmark-gameplay-room-reflections on
benchmark-gameplay-room-reflections off
```

Next is **unit E**: box projection, bounded room-aware selection/blending and explicit switched-light presets. Unit F adds residency/prefetch and controlled rollout to one real level. Keep the synthetic and Legacy references unchanged.

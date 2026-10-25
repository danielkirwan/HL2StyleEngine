# Fixture-Only Six-Room Lighting Test

Follow-up (2026-10-25): this original eight-fixture level is preserved. Use `LaunchSixRoomLightingStabilityTest.bat` for the new fourteen-fixture variant with side coverage, fixture-derived bounce, resident reflections and matched on/off states. [Diagnosis, tests and authoring](LIGHTING_STABILITY_GUIDE.md). The invariant-only limitations below describe this older fixture.

Implemented and validated 2026-10-25. Run root `LaunchSixRoomPointLightTest.bat` to open `Content/Levels/sixRoomPointLightTest.json` in Release. This is a separate copy of `sixRoomPbrTest.json`; existing levels, launchers and startup defaults are unchanged.

## What Lights The Level

- Ambient and directional strengths are **0**. No synthetic environment or diffuse lightmap is enabled.
- Eight fixed PBR point lights: three down the central hall and one in each of the five connected rooms. Each has a visible housing and glowing tube. Dark areas between their ranges are intentional.
- The flashlight starts **off**. Press **F** to enable it for comparison.
- Red/green puzzle indicators remain visible; they do not light surrounding surfaces. Puzzles, pickups, doors, physics, corrected wooden crates and the carryable metal test object are preserved.
- Six newly cooked room captures contain the new fixed fixture illumination and emission, with no old ambient fill. These supply specular reflections, not ambient diffuse illumination. The existing three-slot streaming policy remains enabled.

No dedicated lamp-named GLB was found in the current model collection. Existing ceiling fixtures use `Beam_4m.glb` housings with simple diffusers. This test reuses that housing and scales the existing dielectric `RenderingTests/Sphere_0_4.glb` into a glowing tube. The earlier unpowered decorative fixtures remain in place. A custom lamp model can replace the housing/tube later without replacing the lighting system.

## Place A Light In The Editor

Open the **Prefabs** tab in the Content Browser and place `CeilingPointLight.prefab.json` from `Content/Prefabs`.

1. Move the **Housing** root to the desired ceiling position. Its **Glowing Tube** and **Point Light** children follow it. Fixture meshes are visual-only Props; no new solid collision is added.
2. Select the **Point Light** child to adjust colour, intensity, range, enabled state and Cast Shadows. Defaults are intensity 6 and range 10.5; side-room instances use range 8.5. Prefer adjusting light range rather than scaling the whole hierarchy.
3. Select **Glowing Tube > Emission** to adjust its glow colour/intensity. Its **Linked Light** points to the child light, so disabling that light suppresses the glow. New prefab instances receive independent internal IDs. Glow colour is authored separately from light colour.
4. Keep **Scene Point Lights**, **PBR**, and global **Shadows** enabled. The fixture model alone does not illuminate the room; its point-light child does.
5. Save Level. After editing fixed fixtures/geometry, rebuild each affected room capture through **Toolbar > Room Reflections > Capture / Rebuild**. Current dependency hashing is conservative, so all six may become stale. Stale caches use black specular fallback until recaptured.

For direct-light switches, put **LightSwitch** on a separate switch object and target the point-light child. A switched/flickering fixture must not be treated as invariant baked lighting. The current capture system does not supply matching switched-light reflection presets: changing a captured fixed light selects black fallback, not a freshly relit room. Leave this test's lights fixed when comparing its authored captures.

## Current Limits And Next Lighting Work

- Up to 32 selected point/spot lights illuminate surfaces. At most **one point light and two spotlights** cast shadows simultaneously. Other selected lights are unshadowed and can leak through walls. This test retains that performance budget; Cast Shadows marks eligibility, not an unlimited shadow guarantee.
- Add matching baked/reflection states for switched lights, then smoother residency fades and broader visibility/prefetch handling.
- Add budgeted bloom, colour grading and fog/atmospheric shafts after the lighting-state work. These are not implemented by this level.
- Continue longer traversal/memory-soak tests, cold first-playable measurement and testing on other GPUs. No general realtime GI or locked minimum 60 FPS claim is made.

## Validation

Release build: zero warnings/errors. **747** level/prefab/cache checks pass, including unchanged non-light entities, independent prefab links, zero ambient/directional contribution, and all six fresh caches. Actual game GPU on/off/on changes **520,495** pixels by more than 30 summed RGB units; restoration differs by at most **1/255**. With fixtures off, fewer than 1% of pixels exceed 3/255 (puzzle indicators remain). Game, F2 and standalone-editor scene captures render textured surfaces and local light pools. This is not a manual docking/input acceptance test.

RTX 3060, D3D11, Release, 1920x1009 client area, VSync, 120 warmup + 300 measured frames per run:

| Full-host test | Flashlight | Mean frame | P95 | Frames >33.3ms |
| --- | --- | --- | --- | --- |
| Gameplay/debris/inventory stress | Off | 16.667ms | 18.091ms | 0 |
| Gameplay/debris/inventory stress | On | 16.666ms | 18.052ms | 0 |
| Room loading/eviction route | Off | 16.671ms | 18.108ms | 0 |
| Room loading/eviction route | On | 16.666ms | 18.077ms | 0 |

All four meet unchanged mean/P95/hitch gates, average about 60 FPS, and have zero streaming-wait frames. Stress includes six broken crates / 120 fragment bodies, opened shutters, gravity holding/releasing and inventory. Loading routes use five test-only teleports with actual update/physics/UI/presentation, not continuous walking. Three ready captures plus bindings/fallback use 3,345,700 GPU bytes; no runtime capture passes. Model residency is 34 models / 108.2 MiB. Both Game and HS2Editor copied outputs pass 46 shader/model/level/cache identity and validity checks.

All 15 historical game/F2/HS2Editor rendering references still pass within 1/255. The exact new batch launcher also builds and runs successfully. Its separate stationary 60-frame smoke profile after 120 warmup frames reports mean 16.65ms / P95 23.75ms; that short run is startup/launch evidence, not an additional full-host performance-gate pass. Frame pacing still varies, so these results do not establish a locked minimum 60 FPS.

Reproduce with `Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll` using the bundled `.dotnet/dotnet.exe` after building the authoring project in Release:

```text
validate-point-light-test
check-point-light-pixels
capture-point-light-views
benchmark-point-lights off
benchmark-point-lights on
benchmark-point-light-route off
benchmark-point-light-route on
validate-point-light-package Game/bin/Release/net10.0
validate-point-light-package HS2Editor/bin/Release/net10.0
```

`build-point-light-test` is a one-time generator and refuses to overwrite the level or prefab. `capture-point-light-test` explicitly rebuilds the six source-level reflection caches after fixed-light edits. Rebuild the game/editor afterward to copy changed assets/caches into their output folders. The launcher builds Game before running and explicitly opens the source level.

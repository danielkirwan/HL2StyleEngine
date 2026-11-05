# Flashlight Floor Artifact Correction

Acceptance refresh (2026-11-04): [current handover and captures](LIGHTING_ACCEPTANCE_HANDOVER.md). Controlled 4/8/12 m views again measure zero false self-shadow pixels; actual corner images were reviewed. No additional material/shader change or rebake is needed for this acceptance pass. The separate frame-pacing issue remains open.

Updated 2026-11-02. Use **`LaunchSixRoomShadowTest.bat`**. No reimport, reset or manual rebake is needed for the supplied test level after this update. Turn ceiling fixtures off with the power lever (**E**), then toggle the torch with **F** and inspect the floor at shallow and steep angles.

## Two Separate Causes

The initial normal-map diagnosis explained the small black speckles, but not all of the wider dark bands. Full-scene shadow-on/off captures exposed the second cause; reducing the normal map alone was not sufficient.

- **Surface shading:** the imported floor normal map can turn the shading normal away from the light/view at grazing angles. With no ambient light, its direct contribution becomes black. The test level now uses `Content/Models/RenderingTests/BasementFloorPbr.glb`, with tangent-space normal slopes reduced to 0.5 strength. This is an offline, BC5-cooked material variant, not a per-frame image conversion or a global normal-map reduction.
- **False self-shadowing:** receiver-plane depth correction used interpolated, smoothed vertex normals. Those are not the rasterized triangle's actual plane. The floor has 288 upward/downward-facing triangles and up to about four degrees of disagreement. At shallow angles this produced dark shadow bands. The PBR multi-shadow shader now derives the receiver normal from world-position derivatives, outside per-light branches. Lighting still uses the original smooth/detail normals. The 2mm depth bias, shadow resolution, filtering, light positions and intensity are unchanged.

The original GLB, colour/metallic/roughness images, geometry, UVs, mesh keys and collision remain unchanged. Only 101 floor `MeshPath` values change in `sixRoomShadowTest.json`; all other scene fields are checked for equality. The original floor in other levels is not silently replaced. The triangle-plane shadow correction applies to PBR multi-shadow levels generally; legacy/single-shadow shader binaries remain unchanged.

No ambient/emissive fill was added. Real shadows and unlit areas should still be dark. This does not remove every dark texel from the authored colour texture or add ray tracing/area-light shadows.

## Validation

Direct3D 11, RTX 3060, Release, 960x720 controlled floor grid; same light, camera, exposure and geometry. Each row counts black pixels only where a flat-normal, shadow-disabled reference is clearly lit (RGB sum >= 60). Black means RGB sum <= 3.

| Aim distance | Original normal-map black pixels, shadows off | Corrected material, shadows off/on | Original false floor-shadow pixels, flat normal control | Corrected false floor-shadow pixels |
| --- | ---: | ---: | ---: | ---: |
| 4m | 616 | 8 / 8 | 32 | 0 |
| 8m | 532 | 2 / 2 | 81 | 0 |
| 12m | 452 | 4 / 4 | 41 | 0 |

19,449-43,922 pixels still differ materially from the flat-normal control, so detail has not simply been disabled. Torch-off images remain black. The deterministic asset test checks raw/cooked geometry, UVs, keys, colour and remaining material inputs. Source/corrected normal comparisons use BC5 compression and the ordinary mip pipeline.

Evidence under `Tools/LevelAuthoring/bin/Release/net10.0/qa/`:

- `floor-lighting-20261102-094251-832`: original versus 0.5/0.35/0.25 material candidates; 0.5 selected to retain more detail.
- `floor-lighting-20261102-094654-980`: material-only correction passed the speckle checks, but still had false floor shadows.
- `shadow-stability-20261102-095304`: full-level shadow-on/off images exposed the remaining band; do not treat the material-only stage as complete.
- `floor-lighting-20261102-095717-157`: both corrections; zero measured false floor-shadow pixels in all three controls.
- `contact-20261102-095937`: all 64 point/spot distance/orbit contact cases pass existing limits; real shadows remain.

Final `floor-lighting-20261102-101642-500` repeats these results with a regression assertion for zero measured false floor shadows. `shadow-stability-20261102-101206` validates the actual level, approach/retreat, moving-view flashlight shadows, light switching and three corner views; final corner captures were visually inspected. Three short real-game runs average about 60 FPS (wall mean 16.661-16.663ms, P95 16.825-17.338ms), with zero >33.3ms intervals or streaming waits. Full methodology and limits are recorded in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

## Reproduction And Authoring

From the repository workspace:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release --no-restore
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll prepare-floor-material
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll check-floor-lighting
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-shadow-contact
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll check-shadow-pixels
```

`prepare-floor-material` refuses to overwrite a differently edited variant, is idempotent, and preserves unrelated scene data. It does not cook lighting. When regenerating/changing this floor, cook both `cook-shadow-state on` and `cook-shadow-state off` before testing. The supplied source/caches are already prepared.

A shadow-shader change also invalidates reflection dependencies. Recompile `TexturedModelMultiShadowPS.cso` with the documented Windows SDK command in [SHADOW_STABILITY_GUIDE.md](SHADOW_STABILITY_GUIDE.md). Then refresh the four affected fixtures, one command/process per state:

```powershell
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections sixRoomShadowTest.json on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections sixRoomShadowTest.json off
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections sixRoomVisibilityTest.json on
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections sixRoomVisibilityTest.json off
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections renderStress12Room.json default
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll refresh-shadow-reflections atmosphereTest.json default
```

An initial all-states-in-one-process cook stopped after the first window was disposed. The final command deliberately performs one GPU session per process; completed captures validate their keys after disk reload. Do not bypass dependency validation or reuse old shader-keyed captures. Rebuild game/editor after cooking to refresh copied output assets. Existing immutable packaged playtests must be rebuilt to include changed assets/shader caches.

## Next Milestone

After this visual acceptance: finish fast room-transition texture quality checks and longer streaming-enabled versus full-detail memory/performance controls. Keep adaptive streaming opt-in until those pass; retain ready-before-replacement and full-detail restoration. Spatial audio/room acoustics is the next separate feature milestone, not part of this floor fix. C# remains the runtime.

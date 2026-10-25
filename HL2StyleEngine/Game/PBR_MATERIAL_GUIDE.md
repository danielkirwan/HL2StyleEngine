# PBR Lighting: Staged Implementation

Current follow-up (2026-10-25): authored room capture, automatic transitions and bounded reflection residency are now available. LaunchSixRoomPbrTest.bat opens the isolated real-level copy; it uses the corrected wood variant and preserves its fracture behaviour. [Residency guide](REFLECTION_STREAMING_GUIDE.md). The baseline discussion below is historical; old levels/defaults remain unchanged.

Started 2026-10-22 under [ENGINE_UPGRADE_PLAN.md](ENGINE_UPGRADE_PLAN.md), units A then B. Unit C followed on 2026-10-23: a controlled prefiltered HDR environment and BRDF LUT, documented in [REFLECTION_LIGHTING_GUIDE.md](REFLECTION_LIGHTING_GUIDE.md). C#/.NET 10 owns the runtime; this is not a native rewrite. Authored room captures, bloom and wider migration remain later work. The measurements below retain the original A/B baseline.

## Try It

Run root `LaunchPbrMaterialTest.bat`. It builds Release and opens only `pbrMaterialTest.json`. Existing launchers, Visual Studio defaults, startup level and existing authored levels are unchanged. Press F for the flashlight. The second room has an E-operated light/screen switch. In F2 or HS2Editor use **Toolbar > Lighting > Lighting Model** to compare Legacy/PBR. This setting saves with the level and supports undo/redo. Changing it invalidates an existing bake; deliberately migrated levels need rebaking.

In **Toolbar > Indirect Lighting > PBR Diagnostic**, choose Full, Direct, Diffuse Indirect, Specular Indirect or Emission. Diagnostics are temporary and do not save/rebake. Specular Indirect now isolates unit C's synthetic environment; **Toolbar > Lighting > Specular Environment** switches it off/on. The fixture has no baked diffuse indirect, so its Diffuse Indirect view is black. Existing bake preview controls still work; debug helpers remain unlit. This environment is a controlled distant test rig, not a capture of the room.

## Baseline And Budgets

### Crate Material Correction (2026-10-23)

The movable crate in this fixture now uses `Content/Models/RenderingTests/WoodenCratePbr.glb`. The original imported wood material declares roughness 0 and omits metallic (which correctly defaults to 1). Under PBR it therefore behaves as polished metal: almost no flashlight response except a tiny specular highlight. This was an asset-material problem, not a missing flashlight or a reason to change the shader's metallic defaults.

The derived fixture explicitly uses metallic 0 and perceptual roughness 0.85. Its geometry, all 22 part keys, normals, UVs and embedded texture bytes are unchanged. The original `Breakable_Wooden_Crate.glb` and all other level assignments are untouched. No runtime filename special case, global brightness increase, collision change or importer-default change was introduced. Restart the test launcher to load the new assignment.

Authoring-tool `prepare-pbr-crate` reproducibly derives/cooks the material-only asset and refuses to overwrite a different existing variant. `validate-pbr-crate` checks source/variant/cooked geometry and material parity, six GPU views with flashlight off/on, shadowed/unshadowed and rotated cases, plus the actual game's flashlight/reflection/shadow path. Standalone checks brightened about 73,563 pixels per face versus 2,849-3,262 with the original material. All 3,600 sampled central face pixels brighten in the game; switching off restores that face within 1/255. Generated on/off images are in the tool's `bin/<configuration>/net10.0/qa/crate-*` folder.

Historical `PbrStage0`, `PbrDirect` and `PbrReflections` comparisons retain the original crate assignment **in memory only**, explicitly logged by the harness. Their images are not overwritten to accept this content change. Fresh captures outside those reference directories use the corrected asset; the dedicated crate test covers the correction. Similar imported props need their authored metallic/roughness values reviewed before migrating a real level to PBR; do not blanket-convert every material to wood.

Before material/shader changes, 15 fixed 1280x720 captures were saved under `Tools/LevelAuthoring/References/PbrStage0`: game, F2 and standalone scene-renderer paths for the flashlight, emission, static-indirect, probe and new material fixtures. These are scene captures, not a manual docking/input test. The harness bypasses standalone startup preference writes. References must not be regenerated merely to make a regression pass; legacy-mode comparisons allow at most one RGB code value of rounding difference.

The separate `pbrMaterialTest.json` has two contained rooms with an open doorway, roughness/metallic spheres, a no-normal scalar case, a movable crate, existing textured basement GLBs and an E-operated screen/light switch in the second room. Controlled test GLBs are generated in `Content/Models/RenderingTests`; original models and levels are untouched. Normal/UV stress cases are exercised separately in the numeric GPU harness.

Baseline hardware: NVIDIA RTX 3060, Windows driver 32.0.16.1088 dated 2026-07-22. D3D11 debug layer is ON (the existing EngineHost setting), Release, linear RGBA16F world target, 4x MSAA, exposure 1, ambient 0.025, directional 0.1. Fixture point intensity/range 6/10, spot 7/9, torch 7/22; authored falloff/cones and shadow settings are retained.

Pre-change warmed render-only profile at actual 1920x1061, 30 warmup + 180 measured frames, no present/gameplay/UI: torch off CPU mean/P95 1.525/5.925 ms, GPU command mean/P95 5.733/7.635 ms; torch on CPU 1.156/1.465 ms, GPU 5.668/6.383 ms. Mean allocations 9.84/11.66 KiB/frame; GC collections 1/1/0 and 0/0/0. Residency 48.19 MiB, one main upload/frame. Timestamp waits are test-only; these are total submitted world/resolve/tone-map GPU timings, not individual shader-pass timings.

Pre-change full-host torch-on route at actual 1920x1009: 120 warmup + 300 measured, wall mean/P95 16.668/19.935 ms, no frames >33.3 ms, 6.469 m/s including acceleration/reversals, zero streaming waits, one upload/frame. Window size can be constrained by the desktop; record the actual client area for every run.

Recorded A/B acceptance budgets, set before material changes:

- Existing level images: maximum RGB difference 1/255; no authored-default migration.
- Keep the 256-byte object slot and one main upload; no routine-frame WaitForIdle.
- Same-fixture paired direct-PBR increment: GPU mean/P95 <= +1.0 ms, CPU mean <= +0.5 ms and P95 <= +1.0 ms relative to Legacy in the same run. Repeat noisy runs; do not hide failures by widening budgets.
- Full-host target remains 16.7 ms/about 60 FPS. Regression gate: mean <=17.0 ms, P95 <=21 ms, at most one >33.3 ms sample per 300 and no >100 ms clamp frames on this machine. These short tests do not certify a locked-60 minimum on arbitrary content/hardware.
- Fixture model GPU payload growth <=5%; track allocations, cache warmup and peaks separately. Scalar/no-UV models now need normals/material vertex buffers, so zero memory growth is not assumed.

The 7,025 ms initial tool measurement ended at complete asset GPU readback and included first-time fixture preparation. It is not a cold-start or first-playable result. True cold OS-cache tests and full input-to-first-playable instrumentation remain open; no loading-speed improvement is claimed by this shading work.

## Material Contract

Implemented contract: imported base colour/emission images are sRGB; factors and metallic/roughness/normal samples are linear. Perceptual roughness is clamped to 0.04-1 for shading, then squared exactly once to GGX alpha. Missing metallic/roughness textures are neutral (G=1, B=1), preserving scalar factors. glTF's missing scalar factors remain 1; generic debug/blockout primitives remain explicitly unlit and are not reinterpreted as glTF metals. MR uses G for roughness and B for metallic. UV-less materials ignore maps without usable coordinates and use scalar factors; imported emissive UVs remain independent.

Legacy (0) is the missing/default per-level `LightingModel`; PBR is 1. The default is omitted when serializing, preserving old bake identities. Legacy keeps its original fallbacks and equations. PBR uses lit scalar/no-UV geometry, flat triangle normals when absent, inverse-transpose normals for nonuniform embedded glTF nodes, guarded instance normal matrices and mirrored/degenerate-UV-safe normal mapping. Original imported legacy normals are retained alongside optional corrected PBR normals.

The neutral MR texture has a distinct cache identity. Both modes share textures and vertex buffers where possible. Corrected normals require **model-cache version 3**; old v1/v2 caches fall back to source parsing. All 387 current GLBs were recooked successfully (385 unique caches); source assets were not rewritten, apart from newly generated test fixtures. Texture formats/cache versions and collision geometry are unchanged. Use `CookAssets.bat` or importer **Compression > Cook models and textures** on another checkout.

The direct helper in `TexturedModel.hlsl` uses GGX, height-correlated Smith visibility, Schlick Fresnel, dielectric F0=0.04 and `(1-F)*(1-metallic)*base/pi` diffuse. Directional, point and spot lights share it. Authored intensities, squared finite-range falloff, cone edges and shadow budgets remain unchanged; visibility attenuates both direct lobes. These are artistic units, not a migration to physical watts/lumens.

Existing lightmaps/directional probes store the diffuse response **E/pi**. The shader applies receiving base colour and non-metal fraction once, with no extra division by pi. Atlas and probe contributions are mutually exclusive. PBR uses authored ambient only when no atlas/probe contribution is bound, avoiding duplicate ambient fill. Emission adds independent linear HDR radiance before tone mapping; it does not illuminate neighbours or bloom. Unit C's opt-in environment contributes specular only, with a zero-energy fallback when absent.

The lighting buffer appends one 16-byte settings vector after the existing 32 light records. `Material.w`, `LightInfo.w`, lightmap flags and the 256-byte object slot are unchanged. Both tracked TexturedModel VS/PS CSOs were rebuilt. No routine-frame wait or per-draw uniform upload was added.

## Validation Results (2026-10-22)

Unit A passed its automated gates before direct PBR was implemented: material tests, 387-model bit-exact cache comparisons and all 15 pre-change captures. After PBR, the same 15 legacy captures still pass at maximum 1/255 rounding difference. New game/F2/standalone scene captures show the intentional roughness/metallic changes; visually inspected references are stored separately in `References/PbrDirect`. F2 includes its existing spawn helper in the foreground at this fixed camera. This is not a manual mouse, docking or controller playtest.

- 15 material assertions: legacy/default compatibility, structured level/RE:Dox persistence, undo/redo, corrected node normals, source/cooked equality, scalar/no-UV/no-normal rendering and negative/nonuniform transforms.
- 126 HDR GPU assertions: independent double-precision BRDF reference, metallic/roughness ramps including zero roughness, grazing angles, directional/point/spot lights, moving highlights, falloff/off states, intensity changes, highlight broadening, mirrored/degenerate UVs, singular/invalid normals, sRGB/linear channels, atlas/probe combination and diagnostic isolation. Extreme smooth highlights are bounded to finite RGBA16F storage (65504) before writing the target.
- Both Legacy and PBR pass real GPU spot/point shadow, cache invalidation, visibility and 24 moving-door/light reference frames, shared texture ownership and object batching.
- Existing 282 bake/probe, 298 light-state, 41 emission, six-room rendering/F2, model-cache and visibility/eviction/state-preservation suites pass. The old v1 static bake and v2 probe fixtures retain their original outputs.
- Full solution and authoring-tool Debug/Release builds pass without warnings/errors. The 15 material, 126 HDR and 282 bake/probe assertions also pass in Debug.

Paired render-only measurement at 1920x1061, final repeated pair:

| Mode / torch | CPU mean / P95 (ms) | GPU mean / P95 (ms) |
| --- | --- | --- |
| Legacy / off | 0.388 / 0.496 | 5.331 / 5.599 |
| PBR / off | 0.444 / 0.685 | 5.450 / 5.670 |
| Legacy / on | 0.449 / 0.557 | 5.900 / 6.144 |
| PBR / on | 0.477 / 0.620 | 5.919 / 6.129 |

These pass the unchanged increment budgets. Model payload is 49.38 MiB, up 2.47% from 48.19 MiB (under 5%). There is still one main upload and 43.2 average draws; allocations remain about 9.83/11.66 KiB per frame. Latest repeated PBR blocks recorded GC 1/0/0 off and 0/0/0 on, peak managed 66.7 MiB and process working set 361.1 MiB. Startup/GC peaks vary by process; this is not a claim of reduced total memory.

The initial profile block gave approximately 0.9 ms GPU in **either** lighting mode, while later blocks were 5-6 ms. Reversing order reproduced the effect, so comparing first Legacy to later PBR was not a valid increment estimate. The tool now reports every block, repeats both modes, and gates the last pair. Reversed-order steady measurements also pass (PBR off 5.558/5.889 versus Legacy 5.380/5.660 ms; on 5.947/6.304 versus 5.887/6.193 ms). This timing dependence is documented, not treated as a PBR speedup. GPU clocks/system load were not held fixed; longer controlled profiling remains useful.

Full-host PBR at 1920x1009: torch on wall mean/P95 **16.668/16.900 ms**, off **16.666/16.883 ms**, approximately 60 FPS with VSync. Both have zero >33.3 ms frames, zero >100 ms clamp frames and zero streaming waits across 300 measured frames. Movement is 6.469/6.476 m/s including acceleration/reversals. Legacy six-room flashlight stress also averages 60 FPS, wall mean/P95 16.667/17.959 ms with 120 fragment bodies, doors, gravity holding and inventory. These meet the recorded local regression gates, not a guarantee of every frame exceeding 60 FPS on every machine.

## Reproduce

Final HDR-guard recheck: 126 HDR assertions pass in Debug and Release; all 15 legacy references still pass. Reversed-order steady GPU deltas remain within budget: +0.188/+0.241 ms torch off and -0.049/-0.056 ms on (mean/P95). Absolute clocks/load differed from the table, so the negative delta is not a speedup claim. Final full-host budget runs pass at mean/P95 16.666/17.899 ms on and 16.667/17.983 ms off, both approximately 60 FPS with no >33.3 ms frames or streaming waits. Collision-cache geometry/ray/contact compatibility and all 387 model-cache comparisons were rerun and pass.

Build `Tools/LevelAuthoring/LevelAuthoring.csproj -c Release` with the bundled SDK, then run its DLL with:

```text
validate-pbr-materials
validate-pbr-lighting
validate-pbr-shadows
validate-pbr-legacy
validate-pbr-reference
capture-pbr-room
profile-pbr-room
profile-pbr-room --reverse
benchmark-gameplay-pbr on
benchmark-gameplay-pbr off
```

Reference capture must not overwrite `References/PbrStage0`. New captures go into the tool's `qa` directory. Gameplay profiling uses the real host update/physics/UI loop with scripted input; render-only profiling excludes those costs and presentation. Unit C has separate `References/PbrReflections`; existing direct-PBR comparisons explicitly disable reflections. Unit D's capture/cache ([guide](ROOM_REFLECTION_GUIDE.md)) and unit E's automatic selection/box projection/doorway blend first pass ([guide](ROOM_TRANSITION_GUIDE.md)) are available in separate fixtures. Explicit switched presets, residency/real-level rollout, later steps, cold first-playable instrumentation and a manual input/editor playtest remain open.

References: [glTF metallic/roughness materials](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#metallic-roughness-material) and [Filament BRDF model](https://google.github.io/filament/main/filament.html). These inform the implementation; no external engine source is copied.

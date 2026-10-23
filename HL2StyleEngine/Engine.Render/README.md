# World Rendering

## Room Captures (2026-10-23)

Unit D's first pass adds shared cancellable `ReflectionFilter` cooking, budgeted caller-owned reflection uploads and a shadow-rendering overload for capture-owned command lists/targets. Editor-owned captures isolate camera/object/shadow state, poll fences, read linear HDR before presentation and never sample reflections recursively. Gameplay samples validated saved files, not live captures. One selected preview applies globally; box projection/blending remain next. Shader payloads and existing images are unchanged. See [room capture guide](../Game/ROOM_REFLECTION_GUIDE.md) for eligibility, cache contracts, memory/performance and tests.

## Controlled Reflections (2026-10-23)

Unit C adds `ReflectionEnvironmentData` (versioned/checksummed bounded RGBA16F cubemap + RG16F LUT), `ReflectionEnvironment` (staged GPU transfers) and a separate renderer resource set 6 (t6/t7/s4/b4). One deterministic synthetic preset is cooked offline and embedded in this assembly. Worker read/validation and shared 2 ms soft upload budget keep incomplete data hidden behind a black fallback. No runtime capture/filtering, routine `WaitForIdle`, object-slot expansion or added per-draw uniform upload. The loaded preset stays resident until renderer shutdown; callers must frame-fence retirement of explicitly supplied resources. Legacy and reflection-off references remain unchanged.

Only the PBR test level opts into the synthetic environment. GPU payload including fallback/metadata is 1.063 MiB; local full-host torch on/off averages about 60 FPS and paired GPU increments pass <=1 ms. See [reflection guide](../Game/REFLECTION_LIGHTING_GUIDE.md) for the GGX/LUT convention, tests and limitations. Unit D's separate room captures are described above; projection and room transitions remain pending.

## Opt-In Direct PBR (2026-10-22)

`SurfaceLightingModel.Legacy` is the missing/default per-level mode; existing images remain compatible. PBR uses GGX/height-correlated Smith/Schlick for directional/point/spot lights with the original artistic attenuation/shadow semantics. Scalar/no-UV imports use lit normals and neutral MR fallbacks (G/B=1); missing normals expand into flat triangle vertices. Corrected inverse-transpose glTF node normals are optional `PbrNormals`, stored in **model cache v3**, alongside unchanged Legacy normals. Shared buffers/textures avoid duplication where possible; source GLBs and collision shapes are unchanged. Recook v1/v2 model caches; they safely fall back to source.

`ShadingSettings` appends 16 bytes to b2 after the 32 lights, for explicit lighting mode and diagnostic selection. The object payload remains 256 bytes. Existing `Material.w`/`LightInfo.w` meanings are retained. PBR diffuse bake/probe data is E/pi, multiplied by base colour/non-metal fraction once, with no duplicate ambient when bound. Emission remains independent HDR. Both TexturedModel CSOs were rebuilt. Generic helpers/UI remain unlit; unit C now supplies optional specular indirect through its own binding. Contract and original A/B evidence: [PBR guide](../Game/PBR_MATERIAL_GUIDE.md).

## Directional Light Probes (2026-10-22, Legacy Baseline)

`ProbeIrradiance` packs six directional RGB samples into five float4s (80 bytes). These use the spare space in the existing 256-byte main-scene object slot; `ObjectData` is now exactly 256 bytes without increasing upload stride or adding a resource set/texture. `Material.w` is 0 for ordinary draws, 1 for static lightmaps and 2 for probe-lit textured draws. CPU sampling/interpolation and fixed-wall visibility rejection live in the editor-owned bake session. Stationary object samples are reused; normal-mapped world normals evaluate the ambient cube in the shader. No baked contribution is added twice to static atlas receivers.

Lighting header `LightInfo.w` selects Indirect Only: direct/ambient/emission are suppressed and unlit primitive helpers are black, except explicitly drawn editor probe markers. Full and Direct Only retain normal realtime rendering. The existing untextured/no-UV path remains unlit. Both TexturedModel CSOs were rebuilt. Numeric GPU tests verify axis packing, linear values, diagnostic isolation and the 256-byte layout. Actual model movement, original six-room image checks and full-host torch/debris profiles pass; see [validation](../Game/ENGINE_UPGRADE_VALIDATION.md).

## Static Indirect Lightmaps (2026-10-22)

`LightmapBinding` supplies optional per-instance expanded vertex/index buffers and `LightmapAtlas` owns one linear RGBA16F texture. Padded per-triangle UVs stay separate from imported material/emission UVs. `TexturedVertex` now has a 48-byte GPU stride; source/cooked model v2 layout stays unchanged because bake coordinates are derived per level instance. Model memory counters therefore increase even on unbaked levels.

Resource set 5 uses lightmap texture t5/sampler s3, after shadow t4/s2. `Material.w == 1` enables atlas sampling; other draws bind the shared black fallback, with the probe-specific flag described above. Linear indirect diffuse is multiplied by base colour/non-metal fraction before emission and tone mapping. Separate instances never mutate shared source vertices or materials. Both tracked TexturedModel CSO files were rebuilt alongside HLSL.

The editor-owned bake session hashes/loads on workers and uploads on the render thread. Atlas and per-part upload times share the existing soft 2 ms model budget; a single upload is not preemptible. The single atlas remains resident for its level, and unseen instance bindings retire after 120 frames through frame fences. This is additional to model-zone streaming budgets, not full lightmap streaming. The new room uses 8.19 MiB additional GPU resources. See [the bake guide](../Game/BAKED_LIGHTING_GUIDE.md) for restrictions, transform parity and fallback guarantees.

## Baseline

Confirmed baseline (2026-10-06): production main-scene and shadow uniform uploads stay batched; individual-update toggles are regression references only. Preserve current materials, transforms and shadow quality. Future rendering changes require image comparisons and full-host moving-flashlight on/off measurements, not just isolated pass timings. See `Game/ENGINE_TECHNOLOGY_ROADMAP.md` for the approved baseline and next milestones.

`BasicWorldRenderer` draws primitives and textured GLB model parts. `TexturedModel.hlsl` and its tracked `TexturedModelVS.cso` / `TexturedModelPS.cso` files must be updated together; the normal .NET build copies compiled shaders but does not compile HLSL.

## Compressed Textures (2026-10-06)

Emission update (2026-10-22): embedded glTF `emissiveFactor`, `emissiveTexture` and `KHR_materials_emissive_strength` now flow through the source/cooked/GPU paths. Emissive UV channel selection and `KHR_texture_transform` use a separate float UV stream; images use the existing shared BC7/sRGB colour cache and linear mip filtering. Model cache version is now **2**; recook v1 assets (fallback parsing remains safe). A shared white fallback handles factor-only material emission. Per-instance `ModelEmission` controls intensity/optional named-part colour overrides without mutating shared models. Linear emission is added before exposure/tone mapping, with no bloom or automatic scene illumination. Game and both editors resolve optional fixture-to-light state through `LevelLighting.GetEmission`; see `Game/LIGHTING_GUIDE.md`.

Run `CookAssets.bat` or Asset Importer > Compression > Cook models and textures after converting/importing models. The cooker reads GLBs without rewriting them and creates versioned, source-image-hashed `.hs2tex` files in `Game/Content/.hs2cache/textures`. Game builds/publishes copy those files; they are disposable and ignored by Git. Missing/corrupt caches fall back to decoded RGBA mipmaps; runtime never runs the slow BC encoder. Restart running previews after cooking.

`TextureCooker` uses BC7 for colour/material textures, BC5 for normal maps, and LZ4 HC for independently stored disk mip payloads. SHA-256 verifies decoded payloads. Cache writes use temporary files and atomic replacement. Bump the cache version when encoding settings, format or mip rules change. BCn is lossy; LZ4 is lossless. GPU format support is checked and unsupported BC formats decode to RGBA. Windows image decoding is still required for uncooked imports.

Colour textures use sRGB sampling and linear-light mip filtering; material maps stay linear and normal-map mips renormalize vectors. A full mip chain and anisotropic filtering replace the old single-level texture path. `ModelTextureCache` shares reference-counted GPU textures across parts/models. Mesh geometry, material assignments and destruction part keys are unchanged.

Game and standalone editor prepare images on worker tasks. `TryCreateRenderModel` uploads complete parts under a soft 2 ms per-frame CPU submission budget. A single large part can exceed that budget. A partially uploaded model is not exposed as ready. Synchronous `CreateRenderModel` remains for fixtures and explicit callers; pass prepared textures to avoid image decoding there. Replacement callers retain the original object until the replacement is ready, including on load failure.

## Cooked Models (2026-10-06)

The same cooking command now writes lossless LZ4 `.hs2model` files under `Content/.hs2cache/models`. `TextureCooker.LoadForRendering` delegates to `CookedModelCache`: it reads/hashes the source GLB, then loads validated cached vertex/index buffers, materials, named parts and references to shared cooked textures. A cache hit skips GLB parsing and embedded-image extraction. It does not skip the source file read. Geometry, normals, UVs, triangle order and part identities match the existing importer byte-for-byte; no quantization, mesh simplification or transform changes are applied.

The versioned format checks the source SHA-256, decompressed payload checksum, bounded arrays/strings, indices and texture dependencies. Writes are atomic. Missing, stale, corrupt or incompatible caches fall back to the source loader; runtime never writes/encodes model caches. Offline cooking repairs invalid model/texture dependencies. Bump the model format version when loader/import semantics change; texture cooking has its own version. The format currently targets the engine's little-endian Windows runtime.

Models use `PreparedTexture` references instead of retaining the GLB's encoded PNG data on cache hits. Game/editor scene rendering and shared collision geometry use this path; the lightweight Content Browser preview still uses its original loader. Build/publish copies both cache directories. Set `HS2_MODEL_CACHE=0` before launching for a source-parser comparison that still uses cooked textures. Bounded frame profiles report model cache hits and source parses.

Source GLBs remain required and authoritative. These caches supplement them, so total deployment size is not necessarily smaller. Model cooking does not itself cook collision BVHs or add skeletal animation. The subsequent visual-streaming implementation below manages runtime residency separately.

## Main-View Culling And Resource Retirement (2026-10-06)

`ViewCullingEnabled` defaults on. The camera's row-vector view/projection frustum tests conservative transformed AABBs; each `RenderModelPart` stores bounds computed from its real positions. Primitive/model-part draw entry points share the check, so Game, F2 and standalone editor use the same geometry rules. Non-uniform/negative scales are supported and invalid bounds fail open. Game also rejects whole offscreen entities before requesting models. First-person weapons temporarily bypass world culling; UI is outside this path. Shadow rendering keeps its own light-frustum tests and never inherits the camera's rejection.

`ObjectDrawCount` reports submitted main draws; `ViewCulledCount` counts rejected low-level draw calls (not Game's earlier coarse rejections). The reference toggle changes only culling, not shader/material/transform semantics. This is not occlusion culling: objects behind walls but inside the camera frustum remain eligible.

Game's zone dependency manifest and residency manager share model assets, prefetch neighbours, pin active owners and evict unused entries under a soft budget. `RenderModel.GeometryBytes` plus shared texture payload supplies the owned GPU-resource counter, not total VRAM. `Renderer.RetireAfterFrame` fences disposal after queued GPU use; normal frames only poll completed fences. `DrainRetiredResources` waits during shutdown/tests, before disposing the shared texture owner. Pending partial uploads also retire safely. Do not insert `WaitForIdle` in normal streaming frames or immediately dispose a model still referenced by submitted commands.

See [Game/STREAMING_GUIDE.md](../Game/STREAMING_GUIDE.md) for defaults and ownership. Logical entities and colliders are not unloaded. F2 forces full visual residency and standalone editor keeps its authoring cache. `validate-visibility-streaming` tests real GPU eviction/reload and state retention; `--reference-visibility` and `--stress` extend the full-host gameplay benchmark. Six-room image comparisons and 24 moving-door/light frames cover the culling reference.

## Lighting And Presentation

Main-scene object uniforms are also batched: after `BeginFrame`, Game/F2 and HS2Editor open one `BatchObjectUploads()` scope around all world draws. Disposing it uploads the used range before submitting the command list, including on early returns. A retained 4,096-entry array has the existing 256-byte stride (1 MiB); the shader's Model/Color/Material/NormalMatrix layout and draw limit are unchanged. Empty batches upload nothing. Callers without the scope keep the immediate path; `ObjectBatchUploadsEnabled=false` selects that reference in tests, and `ObjectUploadCount` counts driver calls. Change the reference toggle only between frames. Do not submit the command list inside the batch or restart a frame before disposing it.

`benchmark-gameplay-flashlight on|off [--individual-uploads]` measures the actual game host with scripted keyboard/relative-mouse movement, fixed physics, HUD, launcher UI settings and VSync/presentation. Unlike the render-only command, it can expose physics catch-up and the host's 100 ms delta clamp. Normal desktop Release testing reduced 762 main-scene driver updates to one and reached the same 60 FPS average with the torch on/off. Debug remains substantially more expensive in physics. See `Game/ENGINE_UPGRADE_VALIDATION.md` for conditions and limits; no shader quality or movement settings were changed.

The world renders to RGBA16F HDR targets, then applies exposure, Reinhard tone mapping and linear-to-sRGB presentation. UI is drawn afterwards and does not receive scene exposure. Basic primitives remain unlit, but convert authored sRGB colours to linear before presentation.

Textured rendering accepts up to 32 nearby `WorldPointLight` entries, including spotlight direction/cone data. `LevelLighting` applies the same saved controls in game and editor. Colour textures, normals, metallic/roughness maps, derivative tangent-space normals, opt-in direct PBR, synthetic specular and one selected authored room capture are supported. glTF normal-scale overrides, automatic room selection and box projection remain pending.

`WorldShadows` supports up to two spotlights and one six-face point light in eight 1024-square depth slices with 3-by-3 PCF. Lights opt into shadows; existing lights default to unshadowed. Priority and distance decide active lights. The gameplay flashlight uses high priority. Excess lights still illuminate without shadows. Up to 2,048 nearby casters are retained; overflow is counted, not a crash. Hidden crate parts are excluded.

Per-face conservative sphere/frustum tests now reject irrelevant casters. Each depth slice compares its light matrix and visible geometry/transform list against the prior frame, reusing unchanged depth. Hidden-part changes are captured as visible-part lists, not live mask references. Light movement/range/cone, caster transforms/removal/model replacement and changing light-slot assignments invalidate depth as needed. Colour/intensity/flicker do not invalidate depth. Disabled/inactive slices clear cached references. This is not main-view culling or a static/dynamic split atlas; any changed included caster redraws that face.

Diagnostics: `ShadowPassCount` is active faces, `ShadowRenderedPassCount` is refreshed faces, `ShadowCacheHits` is reused faces, `ShadowDrawCount` is submitted depth draws, and `ShadowCulledCasters` counts rejected caster/face pairs. `ShadowCachingEnabled` and `ShadowCullingEnabled` provide reference paths for regression tests; both default on. Cached maps still have world-shader sampling cost.

Moving-light optimization: when depth must be redrawn, caster matrices now use one bulk upload from a retained CPU array, padded to the existing 256-byte resource-set offsets. Multiple faces share the upload; fully cached frames upload no object matrices. This retains the existing caster count/ordering and quality, with a bounded 512 KiB CPU staging array, rather than issuing one driver update per caster. `ShadowBatchUploadsEnabled=false` provides the old reference for tests. `ShadowObjectUploadCount` reports driver calls, not the number of matrices in the batch. Opt-in `MeasureShadowCpuTime` exposes `ShadowCpuMilliseconds`; it is normally off.

`benchmark-flashlight` uses the actual low-light six-room level at approximately 1080p, comparing stationary/moving camera routes, off/unshadowed/shadowed lighting and reference/batched uploads. CPU render recording and shadow recording are separated from D3D11 timestamped submitted GPU work. The tool waits for query completion; that synchronous profiling behaviour is not used by the game. It excludes gameplay updates, physics, HUD and presentation, so the output is not playable FPS. See the validation report for measured costs and observed timing variability.

The lighting buffer at b2 contains a float4 header, 32 four-float4 light records and the appended ShadingSettings float4. b3 contains eight shadow matrices. The object buffer at b1 contains Model, Color, Material, the inverse-transpose normal matrix, Emission and five packed probe float4s; its aligned upload stride remains 256 bytes. Texture slots t0/t1/t2/t3 are base colour/material/normal/emission; shadow maps use t4 and lightmaps t5. Keep C# and HLSL layouts aligned. Shadow camera/object uniforms have independent storage so passes cannot overwrite each other's transforms.

Light groups, switches, flicker and persistent on/off overrides are provided by the shared level-lighting/gameplay layers; see `Game/LIGHTING_GUIDE.md`. Static indirect/probes are opt-in as described above. Limitations: no reflection probes, directional shadows, alpha-tested shadow materials, bloom or volumetrics. Shadow receivers are textured models; unlit blockout primitives can cast shadows but do not receive lighting. Light poses use editor hierarchy transforms; animated runtime light attachments remain pending. Point-light editor helpers are not runtime renderables/shadow casters.

To rebuild shaders with an installed Windows SDK, run its x64 `fxc.exe` from the project root:

```powershell
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T vs_5_0 /E VSMain /Fo Engine.Render/Shaders/TexturedModelVS.cso Engine.Render/Shaders/TexturedModel.hlsl
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T ps_5_0 /E PSMain /Fo Engine.Render/Shaders/TexturedModelPS.cso Engine.Render/Shaders/TexturedModel.hlsl
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T ps_5_0 /E PSMain /Fo Engine.Render/Shaders/BasicPS.cso Engine.Render/Shaders/Basic.hlsl
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T ps_5_0 /E PSMain /Fo Engine.Render/Shaders/PresentPS.cso Engine.Render/Shaders/PresentPS.hlsl
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T vs_5_0 /E VSMain /Fo Engine.Render/Shaders/ShadowVS.cso Engine.Render/Shaders/Shadow.hlsl
```

Shaders were compiled with SDK 10.0.26100.0. A .NET build does not compile HLSL automatically.

## Verification

`validate-model-cache` compares all current GLBs with their cached geometry/materials/textures, including named crate parts, and checks malformed/stale/missing caches and missing/corrupt texture dependencies in fresh processes. `benchmark-models cooked|glb` isolates CPU asset preparation. `benchmark-level-ready cooked|glb` measures harness initialization through a fully populated GPU readback, excluding OS process startup and actual first-playable input. See the validation report for separate results and methodology.

`Tools/LevelAuthoring` commands `validate-engine-upgrades`, `validate-rendering` and `validate-level-rendering` cover cache checksums/fallbacks, mip colour space, resource sharing, real GPU shadows/exposure and six-room textured game/F2 output. GPU captures are written under the tool output's `qa` directory. The level fixture verifies all requested models become ready and the flashlight changes the image. This is not an interactive standalone-editor playtest.

`validate-lighting-state` checks groups, prefab IDs, flicker, repeatable gameplay interactions and save compatibility. `validate-rendering` additionally compares cached/culled output with reference paths, including 24 moving-door/light frames and in-place hidden-part changes. Its D3D11 timestamp benchmark is isolated to submitted shadow commands plus target clears, without world shading/presentation; synchronous query polling belongs only to the test tool, never the game loop. See the validation report for numbers and limits.

See [the engine technology roadmap](../Game/ENGINE_TECHNOLOGY_ROADMAP.md) for measured results and remaining work. Preserve editor/game transform parity, collision alignment, the inventory visibility repair and ready-before-replacement behaviour.

# Engine Technology Roadmap

Updated: 2026-10-06. Scope: loading, memory, horror lighting, physics and native/managed architecture. This document separates implemented foundations from remaining work. The earlier inventory visibility repair in [PERSISTENCE_AND_INVENTORY.md](PERSISTENCE_AND_INVENTORY.md) is preserved.

## Recommendation

Implementation authorized and first pass delivered on 2026-10-06: compressed texture cooking and shared GPU resources, controllable/shadowed lighting, and measured physics improvements. C# gameplay/editor code, source GLBs, authored layouts and inventory behaviour remain in place. No C++ rewrite or physics-solver migration was made.

Keep C# gameplay, editor, interactions, inventory and RE:Dox persistence. Improve the asset pipeline and measure CPU/GPU work before choosing any C++ migration. A native core is possible, but moving the same expensive work to another language is not a substitute for caching, streaming, efficient collision algorithms or appropriate GPU formats.

For this horror game, prioritize predictable frame times, controlled light and shadow, reliable doors/physics, and assets ready before the player can see them. Do not start with a full engine rewrite or ray tracing.

## Approved Performance Baseline (2026-10-06)

The smoother flashlight playtest is user-confirmed and is now the default baseline, not an optional test-only optimization:

- Keep main-scene and shadow object uploads batched in production. Individual-update paths exist only for regression/reference measurements.
- All three game batch launchers build/run Release. HS2Editor Play Selected Level also launches Release, preserving its save-then-play selected-level workflow. Visual Studio retains Debug for debugging; choose Release there when assessing playtest performance. Editor/importer app launch configurations are otherwise unchanged.
- Preserve the current textured materials, mesh/collider alignment, shadow resolution/filtering, movement settings and ready-before-replacement rule. The flashlight test keeps 0.025 ambient fill and visible red/green indicators; other levels keep their authored lighting.
- Changes to rendering, lighting, loading or physics must rerun relevant image/physics checks and full-host moving flashlight on/off comparisons. Aim for the 16.7 ms total frame budget; record mean/P95, configuration, resolution and presentation conditions. The measured local 60 FPS baseline is not a universal hardware or full-level guarantee.
- Include doors, gravity-held objects, crate debris and inventory transitions in regression stress routes. The first combined route is now covered; a render-only benchmark cannot certify gameplay responsiveness.

## Next Implementation Order

1. **Main-view visibility culling and expanded stress tests: implemented.** Conservative transformed primitive/model-part bounds reject offscreen draws, including rotated/non-uniform/negative scales. Shadow casters remain independent; weapon/UI rendering is protected. Game/F2 and moving-door image references pass. Full-host stress coverage includes shutters, six broken crates, 120 peak fragments, gravity holding and inventory transitions. This is frustum culling, not behind-wall occlusion.
2. **Room dependency manifests, preloading and visual streaming: first pass implemented.** Automatic XZ zones prefetch neighbouring shared model/debris/replacement dependencies. Two preparation workers, budgeted GPU uploads, soft byte budgets, retention and fence-safe eviction/reload are active. Gameplay entities/colliders remain resident, preserving IDs, inventory, pickups, puzzles, doors, light overrides and held objects. Toolbar > Asset Streaming saves per-level settings. This does not yet unload logical rooms/physics or use door portals. True cold/warm first-playable comparisons remain outstanding; see [STREAMING_GUIDE.md](STREAMING_GUIDE.md).
3. **Cooked collision acceleration.** Cache validated static mesh collision/BVH data alongside cooked visual assets to reduce repeated startup work. Preserve authored transforms, contact/ray results and a source fallback; measure the gain rather than assuming it.
4. **Horror-lighting polish.** Add baked indirect lighting/light probes and emissive materials for fixtures, while retaining dynamic flashlight/door shadows and switch-controlled lighting. Evaluate shadow quality/budget under the expanded stress test before adding volumetrics.

Items 1 and 2 are delivered within the scope above; items 3 and 4 are next. Release walking captures remain at 60 FPS with zero streaming-wait frames; main draws average 368 versus 762 and world-render CPU time 2.12 versus 3.72 ms in the reference run. The six-room route still needs all 32 shared models; its residency is not reduced. A forced distant-area fixture verifies actual eviction/reload separately. A C++ rewrite or physics-solver replacement is not currently justified by these measurements.

## Implemented Foundations

1. Assets/loading: BC7 colour/material maps, BC5 normals, full mip chains, LZ4 disk mip payloads, source-hash/version/checksum validation, atomic cache writes, shared CPU preparation/GPU texture ownership and worker-side image preparation. Game/editor submit model parts under a soft 2 ms upload budget. Queued object replacements retain their original visuals/colliders until ready; failures retain the original. The importer has a Compression tab and the root has `CookAssets.bat`. All 376 current GLBs were cooked without errors; 168 unique texture caches were created.
2. Lighting: linear/sRGB handling, RGBA16F HDR world buffers, exposure/tone mapping, normal maps, spotlights, budgeted spot/point shadow maps and a gameplay F-key flashlight. Level-wide lighting controls and per-light enabled/shadow/cone controls are editable and saved. Existing lights remain unshadowed unless opted in. Game/editor use the same material and light settings.
3. Physics: per-mesh BVH acceleration for contacts and rays, deterministic candidate order, reusable candidate buffers and bounded-profile query counters. The existing C# solver and authored collider transforms are unchanged. Randomized comparisons, collider-bound checks and level traversal/settling checks guard correctness.

These foundations now support the visual-streaming first pass above, not full logical/physics room streaming, baked GI or a new physics engine. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md) for reproducible measurements, GPU captures and test limitations, [Engine.Render](../Engine.Render/README.md) for runtime details, and [Engine.AssetImporter](../Engine.AssetImporter/README.md) for cooking instructions.

## Lighting Follow-Up Completed

The next milestone adds editor-authored light groups, reusable `LightSwitch` interactions with optional puzzle prerequisites, explicit ID targets, per-light flicker and saved enabled-state overrides. Prefab placement/apply remaps internal light IDs; revert keeps existing IDs. Per-face conservative shadow culling and unchanged-depth caching are enabled in both game and editor. Moving doors/lights and hidden crate parts invalidate affected maps.

See [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md) for authoring. Regression coverage includes production save loading, prefab links, GPU image comparisons, six-room camera samples and a timestamped synthetic shadow benchmark. These complete the switch/state/cache items below, not baked GI, streaming or representative end-to-end profiling.

Moving-flashlight playtest follow-up: `sixRoomFlashlightTest.json` now has faint 0.025 ambient fill, with scene/directional lights still off and red/green puzzle indicators preserved. Profiling found hundreds of per-caster driver updates on moving shadow redraws. A retained padded buffer now uploads them in one batch, preserving shadow quality and movement speed. Paired real-level camera-route measurements and old/new image checks are documented in the validation report; this is not yet a complete gameplay/debris/input-latency profile.

Full-host follow-up found the remaining main-scene per-object upload bottleneck and Debug physics catch-up cost. Game/F2 and standalone editor now batch main object uniforms too (762 updates to one in the flashlight test), retaining transforms/materials and a bounded 1 MiB staging array. The flashlight test launcher builds/runs Release. Under normal desktop VSync, scripted walking/turning measured 60 FPS with the light on and off, versus 41.8 FPS for the Release individual-upload reference; movement distance was effectively unchanged. These are short local gameplay samples, not a heavy-debris/full-level guarantee. Reproduction, earlier timing variability and the timestep-clamp finding are documented in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

## Cooked Model Milestone Completed

The next loading milestone is implemented: versioned, source-hash/checksum-validated, lossless LZ4 caches for the existing GLB loader's geometry/material output and named part identities. These reference shared cooked textures. `CookAssets.bat` and the importer's Compression tab now cook both. All 376 current GLBs passed source/cache comparisons, including corruption/staleness and texture-dependency fallback/repair checks. Source art, level transforms and gameplay state are unchanged.

Warm serial CPU preparation of 28 six-room models measured a median 286.22 ms using GLB parsing versus 187.20 ms cached, but harness start-to-complete-GPU-readback improved only from 1,577.65 ms to 1,555.51 ms. Source reads/hashing, GPU upload and other startup work remain. These are separate measurements, not a claim of 35% faster first-playable startup. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

The subsequent visual-streaming milestone implements dependency manifests and staged prefetch/residency with persistent IDs and ready-before-replacement guarantees; it is separate from model cooking. Cooked collision/BVH data, full logical room streaming and true cold-start profiles remain pending. Model cooking itself adds no animation system.

## Current Source Audit

| Area | Current implementation | Consequence |
| --- | --- | --- |
| Structured data | `Engine.Core/Serialisation/StructuredData.cs` uses RE:Dox and validated DOX level caches; authored JSON remains authoritative. | Helps data decoding, not image decoding, uploads or physics construction. Inventory reads live memory when opened. |
| Models | `CookedModelCache` reads/hashes the source GLB and loads a validated lossless LZ4 snapshot of imported geometry/materials, or falls back to `GlbModelLoader`. Worker loading and resident path-based model caching remain. | Skips parsing on cache hits while preserving exact geometry/part IDs. Source I/O, GPU uploads and collision BVH construction remain. |
| Textures | `TextureCooker.LoadForRendering` resolves model-cache texture dependencies or prepares source images on fallback. `ModelTextureCache` owns shared reference-counted GPU resources. | Avoids repeated texture resources and runtime image decoding for cooked content. Source GLBs are still read/hashed; residency remains model-level, not per-mip. |
| Visibility/residency | Main-view bounds culling, automatic XZ dependency zones, neighbour prefetch and fence-safe unused-model eviction. | Reduces offscreen draws and can release distant render assets. Logical state/colliders stay resident; visible/near/shadow/held/debris owners override the soft budget. |
| Texture format | BC7 colour/material and BC5 normals with full mip chains; RGBA fallback for missing caches/unsupported formats. | Reduces GPU payload and distant shimmer. Extra cooked files can increase total deployment size while source GLBs are also shipped. |
| Lighting | HDR, normal maps, 32 point/spot lights, two spot plus one point shadow budget, per-face culling/caching, groups/switches/flicker and saved enabled overrides. | Supports controllable horror lighting. Baked indirect light, directional shadows, emission and volumetrics remain pending. |
| Physics | Custom C# solver with collider bounds/support pruning and per-mesh BVH queries. | Reduces triangle search work without changing collider shapes or solver behaviour. Native/mature solver evaluation remains conditional on measured bottlenecks. |
| Native boundary | RmlUi already runs behind `Native/HS2RmlUiBridge` with a C ABI consumed from C#. | The project already has an example of a managed/native split. |

Historical asset-ready startup was around six seconds in the October 2 test; faster serialization did not produce a demonstrated total-startup win. See [REDOX_EVALUATION.md](REDOX_EVALUATION.md). New warm asset-stage and frame captures are in [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md); they are not cold-start measurements or a promise about a 100-times-larger level.

## Compression Choices

Compression has different jobs: reducing disk reads, reducing CPU decoding work, and reducing GPU memory/bandwidth. One algorithm does not cover all three.

| Data | Candidate | Intended use and trade-off |
| --- | --- | --- |
| PC colour textures | BC7 with offline-generated mips | Implemented for colour/material images, with GPU format checks and fallback. |
| Normal maps | BC5 | Implemented with two stored channels, reconstructed Z and matching shader support. |
| HDR environment/light textures | BC6H | A future HDR asset option, not a fix for the current LDR shader. |
| Cross-platform textures | KTX2/Basis Universal | Portable storage with transcoding to supported GPU formats. Transcoding has a cost; direct cooked PC formats are simpler for the present Windows target. |
| Meshes | meshoptimizer | Optimize vertex/index access and compress buffers; add compatible loader support before exporting compressed assets. Preserve named crate parts, material assignments and collision alignment. |
| Asset-pack chunks | Zstandard or LZ4 | LZ4 implemented per cooked texture mip and imported model payload. General asset packs, optimized mesh buffers and Zstandard comparison are not implemented. |
| Later Windows I/O work | DirectStorage / GDeflate | Consider after measuring I/O/decompression bottlenecks. GPU decompression requires appropriate graphics/backend integration; it is not a drop-in C# speed switch. |

A 4096x4096 RGBA8 texture occupies 64 MiB for its base level; BC7 occupies 16 MiB. A full mip chain adds approximately one third to either total. This is an illustrative storage calculation, not a measured loading-time improvement or a promise of four-times-faster gameplay.

References: [Microsoft BC formats](https://learn.microsoft.com/en-us/windows/desktop/direct3d11/texture-block-compression-in-direct3d-11), [Khronos KTX](https://www.khronos.org/ktx/), [meshoptimizer](https://github.com/zeux/meshoptimizer), [Zstandard](https://github.com/facebook/zstd), [LZ4](https://github.com/lz4/lz4), [Microsoft DirectStorage](https://github.com/microsoft/DirectStorage).

## Remaining Work And Direction

The numbered stages below remain the longer-term plan. Texture/model cooking, texture sharing, budgeted uploads, initial shadowed HDR lighting and mesh-query acceleration are implemented as described above; broader acceptance gates are not all complete.

### 1. Measure A Repeatable Baseline

Record cold and warm startup separately, including file reads, GLB parsing, image decoding, mesh/collider construction, upload submission, GPU completion and first fully rendered playable frame. Capture CPU allocations, RAM/VRAM, frame-time percentiles and stalls while opening doors, inventory and producing debris. Use the same Release build, route, resolution and machine settings. Report real cold-cache methodology rather than calling the first loop iteration cold.

Preserve a small visual/physics reference level alongside `sixRoomTest.json`. Include textured doorframes, moving doors, breakable crates and gravity-gun pickups. A 60 FPS goal means a 16.7 ms total frame budget, not 16.7 ms available independently to every subsystem.

### 2. Cook Assets Before Play

Implemented: versioned GPU-ready texture mips, lossless imported-model buffers/materials with texture dependency keys, and derived spatial-zone dependency manifests. Remaining: vertex/index optimization, cooked collision acceleration and packaged/portal-aware dependency metadata. Keep source GLB/FBX files and editable level JSON. Hash source content plus import settings/tool version, rebuild changed or invalid assets and retain the source compatibility path. A model-loader semantics change must bump the model cache version.

Use sRGB formats for colour textures and linear formats for material data. Test colour-space changes against editor and game reference captures. Preserve node/part identities, transform conventions and authored collision shapes; compression must not reopen the previous wall/door alignment regressions.

### 3. Share Resources And Bound Upload Work

Introduce image/material caching independent of the model cache, with explicit ownership and lifetime management. Separate worker-side reading/decompression/decoding from bounded render-thread upload batches. Keep frequently used UI fonts, thumbnails and weapon resources resident. Generate UI thumbnails during import rather than loading full world models to open inventory.

Represent readiness explicitly: requested, CPU-ready, GPU-ready and failed. Never destroy or hide an existing object until its replacement's required rendering resources are ready. If preparation fails, retain the current valid object and report the error. This preserves the established crate/replacement rule.

### 4. Stream Rooms And Dependencies

The visual-resource first pass is implemented as documented in [STREAMING_GUIDE.md](STREAMING_GUIDE.md). The following requirements continue to apply to future full logical/physics room streaming and portal-aware prefetch; those extensions are not implemented yet.

Use room/zone-sized chunks with a shared asset pool. Prefetch adjacent rooms before the player opens their doors; retain a small neighbouring set to avoid repeated load/unload stalls. Budget residency by bytes and dependencies, not just object count. Visibility culling and streaming are separate concerns: an invisible room may still need collision, puzzle state, sound or preloaded assets.

Keep object IDs and persistent gameplay deltas independent of whether the room is resident. Save unlocked doors, solved circuits and collected items without requiring every mesh to stay loaded. Unloading a room must not reset puzzles, duplicate pickups or drop a gravity-held object unexpectedly.

### 5. Build Horror Lighting In Stages

Establish correct colour spaces, HDR render targets, tone mapping and controlled exposure first. Then implement shadowed spotlights for flashlights and focused fixtures, followed by a budgeted set of shadowed point lights. Check that walls block light and moving doors cast/update shadows. Add normal maps so the existing masonry/metal assets read properly under grazing light.

Completed: light groups, repeatable switches, authored flicker, saved enabled states and shadow caching/frustum culling. Keep editor preview and runtime lighting consistent while separating authored defaults from gameplay overrides.

Next combine baked static indirect lighting or probes with selective dynamic lights. Do not bake a movable door's closed shadow into a permanent wall/floor texture. Volumetrics and more advanced reflections can follow after these foundations meet frame budgets. Main-camera frustum culling and the first combined gameplay/debris CPU profile are complete; full-game GPU timestamps and longer stress/soak profiles remain.

### 6. Profile Physics Before Choosing A Replacement

Measure broad-phase candidate counts, triangle tests, solver work, sleeping bodies and debris bursts. Keep static architecture separate from dynamic objects, precompute mesh acceleration structures and avoid rebuilding static shapes during normal play. Confirm gravity-gun holding, character movement and hinged-door contacts remain stable.

If a mature solver is warranted, compare [BEPUphysics v2](https://github.com/bepu/bepuphysics2) (C#) and [Jolt](https://github.com/jrouwe/JoltPhysics) (C++) on the same scenes. Benchmark total integration cost and gameplay correctness, not only isolated solver throughput. Existing character/gravity-gun behaviour and serialization require explicit adapters and migration tests either way.

## C# And C++ Boundary

A suitable eventual split would keep gameplay, editor tools, content authoring and persistence in C#, with only measured heavy services in C++ where justified: physics, asset decoding or specialized render/backend work. C# can also drive native compression/physics libraries without migrating the rest of the engine. C is useful as a stable interoperability interface; it does not require writing the implementation itself in C.

Use a small versioned C ABI, opaque handles and batched arrays/commands. Specify ownership, disposal, threading and error reporting. Avoid thousands of calls per object per frame, shared mutable managed/native state, or exceptions crossing the boundary. Prefer generated interop and safe handle ownership where appropriate. [Microsoft native interoperability guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices).

Keep RE:Dox in the managed data layer even if native services are added. GPU shaders already run on the GPU; moving their C# orchestration to C++ does not automatically speed the shader. Address excessive allocation, duplicate work and poor data access in the existing code before committing to a rewrite.

## Acceptance Gates

- Inventory shade/grids remain visible and interactive in native and fallback rendering; no file read on every open.
- Authored transforms, hierarchy IDs, named destruction pieces and visible mesh/collider alignment remain unchanged.
- Save/restore survives room streaming without lost or duplicated pickups and puzzle state.
- Asset replacement remains ready-before-destroy, including preparation failure.
- Editor and game show matching materials and lighting; compression artifacts are checked close up and at a distance.
- Loading-time and frame-time improvements are measured separately. Smaller files, faster JSON reads and fewer allocations alone are not proof of faster first playable frames.

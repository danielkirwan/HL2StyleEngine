**Open-source C++ engine research for HL2StyleEngine**

Implementation follow-up (2026-10-27): the research-inspired [loading/package passes](LOADING_PACKAGE_GUIDE.md) are implemented in C#, following atmosphere acceptance. Immutable dependency metadata removes redundant model and lighting-fingerprint GLB reads; normal authoring validation and fallback remain. The original model-only milestone improved local warm first playable 2.8%; the subsequent lighting pass reduces worker/I/O cost but does not demonstrate an additional total-startup gain. No-build probes support later genuine post-restart samples; remaining startup work and mip residency remain next. This is not a new external-engine benchmark or native migration.

Implementation follow-up (2026-10-26): remaining research-derived systems are now explicitly staged in the [approved roadmap backlog](ENGINE_TECHNOLOGY_ROADMAP.md#approved-research-backlog-2026-10-26), with additional horror-game audio, AI and effects clearly separated from source-derived work. [Optional atmosphere first pass](ATMOSPHERE_GUIDE.md) follows room visibility and render submission; loading instrumentation/packaging is recommended next. The research below remains its dated source review, not a new external-engine benchmark or a claim that the whole backlog has shipped.

Researched: 22 October 2026. Target: atmospheric first-person environments inspired by Half-Life 2, BioShock, and Resident Evil Requiem, while retaining C# for gameplay and the runtime programming model.

This is a source and documentation review. The engines were not built or benchmarked against this project. Rankings describe usefulness for learning and applicability to this codebase; they do not establish which engine is fastest. Public development branches and rolling documentation can differ from released SDKs. The snapshot table at the end records the source revisions used for the reading routes.

**Recommendation**

Start with **Wicked Engine, Godot, ezEngine, and RBDOOM-3-BFG**. They answer different questions: how to build modern rendering, how to expose a C++ engine to C#, how to organize an efficient engine, and how to make room-based horror environments render efficiently. Add **Lumix** for a smaller architecture reference, **O3DE/Atom** for larger renderer systems, and **rbfx** for readable batching and binding examples. **Filament** is an especially useful supplementary renderer for upgrading this project's materials.

Keep the existing managed runtime while studying these systems. Adopt one technique at a time. A later native renderer or physics subsystem is feasible, but the present evidence does not justify a wholesale language migration.

**What is already in this project**

These observations come from current source, with project notes used for context. Some notes lag the implementation: light probes are present in code even where the README/roadmap still call them pending.

| Area | Current evidence | Research implication |
| --- | --- | --- |
| Runtime | .NET 10 C# projects; [EngineHost](../Engine.Runtime/Hosting/EngineHost.cs) creates Veldrid with Direct3D 11. | C# is already the engine/runtime language, not merely a thin script layer. |
| Frame structure | [IGameModule](../Engine.Runtime/Hosting/IGameModule.cs) has Initialize, Update, FixedUpdate and disposal; the host invokes world/overlay rendering separately. | Preserve the useful lifecycle while improving subsystem boundaries. |
| Graphics coupling | [IWorldRenderer](../Engine.Runtime/Hosting/IWorldRenderer.cs) takes the concrete managed Renderer; EngineContext exposes it; Renderer exposes Veldrid objects. | A native renderer needs a new extraction/submission boundary. It cannot replace a DLL transparently today. |
| Frame presentation | [Renderer](../Engine.Render/Render/Renderer.cs) uses HDR targets and 4x MSAA; [PresentPS](../Engine.Render/Shaders/PresentPS.hlsl) uses exposure, Reinhard mapping and sRGB output. | HDR and basic antialiasing already exist. Study colour grading and material response before treating them as missing foundations. |
| Materials | [TexturedModel.hlsl](../Engine.Render/Shaders/TexturedModel.hlsl) loads metallic/roughness and normal maps, but its directional specular is a power-based approximation; point/spot lighting adds diffuse light only. | Full direct-light PBR and specular environment lighting are concrete visual opportunities. Reading PBR textures does not itself supply a full PBR lighting model. |
| Indirect lighting | Static lightmap path plus [LightProbeVolume/LightProbeSampler](../Engine.Editor/Editor/Lighting/LightProbeVolume.cs), [LevelBakeSession.SampleProbe](../Engine.Editor/Editor/Lighting/LevelBakeSession.cs), and [ProbeIrradiance](../Engine.Render/Render/ProbeIrradiance.cs). | Extend and validate the existing six-direction diffuse probes. Reflection probes would supply a different, specular contribution. |
| Lights and shadows | [BasicWorldRenderer](../Engine.Render/Render/BasicWorldRenderer.cs) selects up to 32 lights globally; the pixel shader loops over that list. [WorldShadows](../Engine.Render/Render/WorldShadows.cs) budgets eight depth slices, caches unchanged faces and batches uploads. | Study spatial light lists and shadow allocation independently. More supported lights do not automatically mean affordable shadowed lights. |
| Visibility and streaming | Frustum checks, [RoomAssetManifest](Streaming/RoomAssetManifest.cs), zone prefetch, shared resources and frame-fenced retirement. [Streaming guide](STREAMING_GUIDE.md) distinguishes visual residency from logical/physics residency. | Occlusion/portal visibility and room dependencies are useful extensions. Invisible objects can still matter to shadows, audio, physics and puzzles. |
| Asset preparation | [CookedModelCache](../Engine.Render/Render/Model/CookedModelCache.cs), texture cooking, shared GPU textures and budgeted uploads; source GLBs are still read/hashed for cache validation. | Research manifests, packaged assets and bounded upload work as separate stages. C++ alone would not remove source I/O or GPU upload cost. |
| Physics and native code | C# mesh BVH/collision caches; [HS2RmlUiBridge](../Native/HS2RmlUiBridge/README.md) already exposes a C ABI to managed code. | Use the bridge as a local interop example. Evaluate a mature native solver only against measured needs and interaction behaviour. |

No new performance measurements or visual validation were run for this research.

**Comparison**

All seven primary entries have C++ engine code and free open-source core licences. Licences in this table describe the engine core; third-party libraries, optional integrations and example/game assets can have separate terms.

| Engine | Core licence | Best subject to study | C# position | Fit for this project |
| --- | --- | --- | --- | --- |
| [Wicked Engine](https://github.com/turanszkij/WickedEngine) | [MIT](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/LICENSE.txt) | Modern materials, tiled lighting, effects, compact component storage and jobs | Documented paths emphasize C++ and Lua; a C# integration would be separate work | Strongest first modern-graphics reference |
| [Godot](https://github.com/godotengine/godot) | [MIT](https://github.com/godotengine/godot/blob/5d1cd20f9117e4721fce626454e2323eb9462d0a/LICENSE.txt) | Clustered forward rendering, renderer abstraction and managed/native integration | Official .NET/C# support | Strongest C++ plus C# architecture reference |
| [ezEngine](https://github.com/ezEngine/ezEngine) | [MIT](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/LICENSE.md) | Render extraction, resource management, occlusion, tasks and reflection probes | Study/port the systems; this is not a ready replacement for this project's C# runtime | Particularly useful for engine organization |
| [Lumix](https://github.com/nem0/LumixEngine) | [MIT](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/LICENSE) | A comparatively compact renderer, draw streams, culling and resources | A managed API for this project would need to be designed | Good reference for tracing a whole subsystem |
| [O3DE / Atom](https://github.com/o3de/o3de) | [Apache 2.0 or MIT](https://github.com/o3de/o3de/blob/2462b90ee389939336931a14e234fc4e3b4cd534/LICENSE.txt) | Feature processors, draw packets, passes, frame graphs and asset systems | Official gameplay scripting is Lua/Script Canvas; C# integration would be additional work | Valuable advanced reference; high reading/integration cost |
| [rbfx / Rebel Fork](https://github.com/rbfx/rbfx) | [MIT](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/LICENSE) | Scene processing, batching, instancing, shadow allocation and bindings | Optional experimental bindings; maintainers explicitly say C# is not first class | Useful code reference, weaker managed-runtime foundation |
| [RBDOOM-3-BFG](https://github.com/RobertBeckebans/RBDOOM-3-BFG) | [GPLv3](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/LICENSE.md), with [component exceptions](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/LICENSE_EXCEPTIONS.md) | Room/portal visibility, horror lighting, irradiance grids and reflections | No ready C# runtime path established here | Excellent architectural study; code reuse needs GPL compatibility |

**1. Wicked Engine: study modern visuals and the cost of producing a frame**

Its [feature list](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/features.txt) includes PBR, tiled forward lighting, HDR, shadow mapping, volumetric scattering, lightmap baking, multithreaded rendering and optional ray-tracing effects. The documented graphics backends are Direct3D 12, Vulkan and Metal.

Its data-oriented component managers and job interface are useful even if all your gameplay stays managed. Work can be processed in groups over component data, with synchronization at the point the results are needed. Its job API distinguishes priorities, including a streaming queue. These are mechanisms to examine, not evidence of an automatic speed multiplier.

Read these in order:

1. [wiRenderPath3D.h](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/WickedEngine/wiRenderPath3D.h) and [wiRenderPath3D.cpp](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/WickedEngine/wiRenderPath3D.cpp): frame resources and ordering of rendering/effects.
2. [wiRenderer.cpp](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/WickedEngine/wiRenderer.cpp): locate lighting, visibility, shadow and material work; follow associated shaders.
3. [wiECS.h](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/WickedEngine/wiECS.h) and [wiJobSystem.h](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/WickedEngine/wiJobSystem.h): component storage, grouped dispatch and waits.

For your engine, study local-light material response and controlled effects first. The advanced API/resource assumptions mean its renderer cannot simply be pasted into the existing D3D11/Veldrid path. Its documented C++/Lua workflow also does not supply your desired managed runtime automatically. Sources: [project documentation](https://github.com/turanszkij/WickedEngine/blob/79243b535c226b1612449335d632f225e2303258/README.md) and [official site](https://wickedengine.net/).

**2. Godot: study the strongest managed/native example**

Godot demonstrates that a native C++ engine and C# gameplay are a workable combination. Its [C# documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html) describes modern .NET integration and the cost of repeated native property access.

Forward+ divides the camera frustum into spatial clusters and associates relevant lights with those regions. Shading can then avoid evaluating every scene light for every pixel. The renderer sits above a graphics abstraction. Different rendering methods trade features and resource costs rather than forcing one configuration onto every device. See the [internal rendering architecture](https://docs.godotengine.org/en/stable/engine_details/architecture/internal_rendering_architecture.html).

Source route:

1. [render_forward_clustered.cpp](https://github.com/godotengine/godot/blob/5d1cd20f9117e4721fce626454e2323eb9462d0a/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp): renderer orchestration.
2. [cluster_builder_rd.h](https://github.com/godotengine/godot/blob/5d1cd20f9117e4721fce626454e2323eb9462d0a/servers/rendering/renderer_rd/cluster_builder_rd.h) and its corresponding .cpp: cluster data and construction.
3. [modules/mono](https://github.com/godotengine/godot/tree/5d1cd20f9117e4721fce626454e2323eb9462d0a/modules/mono), especially [runtime_interop.cpp](https://github.com/godotengine/godot/blob/5d1cd20f9117e4721fce626454e2323eb9462d0a/modules/mono/glue/runtime_interop.cpp) and [GodotSharp](https://github.com/godotengine/godot/tree/5d1cd20f9117e4721fce626454e2323eb9462d0a/modules/mono/glue/GodotSharp): native and managed sides of the boundary.

Transfer the separation of responsibilities and the lesson about call frequency. Keep managed gameplay operations convenient, but batch large changes to native subsystems. Learning from Godot does not require adopting its node model or migrating this project to Godot.

**3. ezEngine: study how to keep engine systems organized**

ezEngine is a strong reference for extraction, render-data caching, resource ownership and task scheduling. Its [RenderWorld interface](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/RenderWorld/RenderWorld.h) explicitly separates extraction from rendering and describes double-buffered data when multithreading is enabled.

Its [occlusion implementation documentation](https://ezengine.net/pages/docs/performance/occlusion-culling.html) describes rasterizing simplified occluders in software and testing object bounds against that result. This could be valuable for corridors and closed rooms. Camera visibility still differs from a shadow light's visibility.

Read:

1. [RenderWorld.h](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/RenderWorld/RenderWorld.h) and [render-world implementation directory](https://github.com/ezEngine/ezEngine/tree/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/RenderWorld/Implementation).
2. [RasterizerView.h](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/Rasterizer/RasterizerView.h): occluder submission and visibility queries.
3. [ClusteredDataExtractor.cpp](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/Lights/Implementation/ClusteredDataExtractor.cpp) and [ReflectionProbeMapping.h](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/RendererCore/Lights/Implementation/ReflectionProbeMapping.h): spatial lighting data and budgeted probe mapping.
4. [ResourceManager.h](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/Core/ResourceManager/ResourceManager.h) and [TaskSystem.h](https://github.com/ezEngine/ezEngine/blob/08bae46f631efdec027910fb3e0688372d692b7e/Code/Engine/Foundation/Threading/TaskSystem.h).

For this project, the best lesson is making immutable rendering data from gameplay state, with explicit invalidation when doors, materials or transforms change. Its [modular design](https://github.com/ezEngine/ezEngine#design-philosophy) is also worth studying. Verify current platform support against the chosen checkout: the README and FAQ have differing levels of detail about Vulkan progress.

**4. Lumix: study a smaller system end to end**

Lumix offers a comparatively compact way to trace the interaction between the scene renderer, pipeline, GPU abstraction and resource manager. Its current pipeline interface exposes a G-buffer, local shadow atlas access, culling/buckets and hooks around the light and tone-mapping stages. Avoid assuming that older tutorials describing a scripted pipeline match today's code.

Read:

1. [pipeline.h](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/renderer/pipeline.h) and [pipeline.cpp](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/renderer/pipeline.cpp): how frame work is assembled.
2. [renderer.h](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/renderer/renderer.h) and [draw_stream.cpp](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/renderer/draw_stream.cpp): renderer extension points and command representation.
3. [culling_system.cpp](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/renderer/culling_system.cpp) and [resource_manager.cpp](https://github.com/nem0/LumixEngine/blob/55094198b3c6750a395c15e5f22a93993ab47cca/src/engine/resource_manager.cpp): spatial filtering and asset lifetime.

Its [postprocess tutorial](https://nem0.github.io/LumixEngine/renderer/postprocess.html) shows explicit hooks before/after tone mapping. That distinction matters to your existing HDR-world-then-UI flow. Study a small effect through the whole pipeline before attempting a new renderer. The suitability/readability assessment here is my judgment; no claim is made that Lumix runs your workload faster.

**5. O3DE / Atom: study larger renderer architecture selectively**

Atom receives simulation data through feature processors, builds draw packets, filters work into views/draw lists, and executes passes through an RHI. Its [frame-rendering guide](https://www.docs.o3de.org/docs/atom-guide/dev-guide/frame-rendering/) explains the flow. A mesh's depth, shadow and main draws can share organizational structure without forcing gameplay code to understand each pass.

Its [pass system](https://www.docs.o3de.org/docs/atom-guide/dev-guide/passes/pass-system/) separates pass code from configurable data. This is useful once a renderer has many dependencies. The larger framework also has a substantial integration and comprehension cost for a project of this size.

Read [FeatureProcessor.cpp](https://github.com/o3de/o3de/blob/2462b90ee389939336931a14e234fc4e3b4cd534/Gems/Atom/RPI/Code/Source/RPI.Public/FeatureProcessor.cpp) alongside the documentation, then [RenderPipeline.cpp](https://github.com/o3de/o3de/blob/2462b90ee389939336931a14e234fc4e3b4cd534/Gems/Atom/RPI/Code/Source/RPI.Public/RenderPipeline.cpp) and [FrameScheduler.cpp](https://github.com/o3de/o3de/blob/2462b90ee389939336931a14e234fc4e3b4cd534/Gems/Atom/RHI/Code/Source/RHI/FrameScheduler.cpp). Follow declarations and related files when the implementation entry point is small.

Transfer the principle of renderer-owned data and explicit resource dependencies. A modest extraction API can capture much of the organizational benefit without reproducing Atom's entire framework. O3DE's [official scripting overview](https://www.docs.o3de.org/docs/user-guide/scripting/) documents Lua and Script Canvas, so keeping your C# runtime while adopting O3DE wholesale would involve additional integration work.

**6. rbfx: study batching and bindings, with realistic expectations**

The Rebel Fork is a C++ framework derived from Urho3D. Its [README](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/README.md) describes optional experimental C# bindings, says C# is not first class, and explicitly does not target cutting-edge AAA graphics. Those limits make it a useful teaching reference rather than the leading recommendation for your visual target.

Read [DefaultRenderPipeline.h](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/Source/Urho3D/RenderPipeline/DefaultRenderPipeline.h), [BatchRenderer.cpp](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/Source/Urho3D/RenderPipeline/BatchRenderer.cpp) and [ShadowMapAllocator.cpp](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/Source/Urho3D/RenderPipeline/ShadowMapAllocator.cpp). Trace draw preparation, material/pipeline grouping, instancing and shadow allocation.

For interop, inspect [CSharp](https://github.com/rbfx/rbfx/tree/156d8d0917d4bde1f8751d746ee96ca7aa741813/Source/Urho3D/CSharp) and [Native/Object.cpp](https://github.com/rbfx/rbfx/blob/156d8d0917d4bde1f8751d746ee96ca7aa741813/Source/Urho3D/CSharp/Native/Object.cpp). Binding a renderer is only part of the work: event callbacks and managed/native object lifetimes also need rules.

For your existing batched uploads, this is a good next-level study of reducing repeated draw/state work. Do not equate one uniform-buffer upload with one draw call; those are separate costs.

**7. RBDOOM-3-BFG: study rooms, visibility and atmosphere**

This modernized Doom 3 BFG engine is unusually relevant to enclosed horror environments. Its [renderer overview](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/README.md) describes PBR, HDR, shadow maps, irradiance volumes, environment probes and software occlusion alongside the original room/portal structure.

Read [RenderWorld_portals.cpp](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/neo/renderer/RenderWorld_portals.cpp), [RenderWorld_lightgrid.cpp](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/neo/renderer/RenderWorld_lightgrid.cpp), [RenderWorld_envprobes.cpp](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/neo/renderer/RenderWorld_envprobes.cpp) and [tr_frontend_masked_occlusion_culling.cpp](https://github.com/RobertBeckebans/RBDOOM-3-BFG/blob/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5/neo/renderer/tr_frontend_masked_occlusion_culling.cpp).

Portals are openings connecting areas. Visibility traversal narrows what can be seen through those openings; it is a different problem from deciding which assets should stay loaded. That distinction fits your doors, room prefetch and persistent puzzles. Keep the two systems connected through conservative dependencies, without making a hidden room lose gameplay state.

Its irradiance representation is not identical to your six-axis probe data, so compare encoding, placement, interpolation and wall leakage rather than copying one sampler in isolation. GPL code reuse has different requirements from MIT reuse, and commercial Doom game assets are separate from the engine source.

**Supplement: Filament for your material upgrade**

[Filament](https://github.com/google/filament) is a C++ rendering engine under Apache 2.0, not a full gameplay/editor replacement. Its [PBR reference](https://google.github.io/filament/main/filament.html) covers microfacet specular shading, Fresnel, roughness, image-based lighting, exposure and clustered lighting.

The concrete code entry point is [surface_brdf.fs](https://github.com/google/filament/blob/main/shaders/src/surface_brdf.fs), followed by [surface_light_punctual.fs](https://github.com/google/filament/blob/main/shaders/src/surface_light_punctual.fs) and [surface_light_indirect.fs](https://github.com/google/filament/blob/main/shaders/src/surface_light_indirect.fs). Treat those rolling links as documentation references and pin a revision before adapting code.

For BioShock-like metalwork and wet surfaces, I would study this alongside Wicked before advanced ray tracing. The visual benefit is an inference from the material gap in your shader, not a measured comparison.

**Keeping C# while learning from C++**

There are two practical paths:

- Keep the current engine managed and implement the studied algorithms in C#/HLSL. This retains your runtime, editor and asset flow.
- Move selected subsystems behind a stable native interface after measuring a reason to do so. C# can remain responsible for game rules, scripts, puzzles, inventory, persistence and editor behaviour.

The second path needs an explicit ownership contract. A useful design is managed gameplay producing batches of transform/material/light changes, then a renderer retaining its own compact scene data and recording GPU work. Stable gameplay IDs map to renderer handles; native pointers should not become save-file identities.

Illustrative C ABI sketch, **proposed, not implemented**:

```c
#include <stdint.h>

typedef struct hs2_render_update {
    uint64_t instance;
    uint64_t material;
    float model[16];
} hs2_render_update;

/* Export/calling-convention annotations are platform-specific.
   Copy input updates before returning; retain no caller memory. */
int32_t hs2_apply_render_updates(
    uint32_t abi_version,
    const hs2_render_update* updates,
    uint32_t count);
```

A managed renderer API could accept ReadOnlySpan data and marshal or pin it only for the duration of submission. Define matrix convention, alignment, handle validity, errors, thread ownership and destruction rules explicitly. Fence resource destruction against GPU completion. Do not assume a pinned managed array remains safe for asynchronous native retention after the call.

Your [native UI bridge](../Native/HS2RmlUiBridge/README.md) already shows the need for explicit data lifetimes. Godot offers a fuller binding reference. The current EngineContext/IWorldRenderer surfaces would need refactoring to hide Veldrid if a native backend were selected; the existing IGameModule lifecycle can remain.

**Suggested implementation/research order**

These are recommendations for future work, not changes made during this review.

| Priority | Research question / possible experiment | Best references | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Add physically based specular response to point/spot lights and evaluate roughness/metalness consistently. | Filament, Wicked | Fixed material scene with roughness/metalness variants and grazing flashlight angles; editor/game parity; CPU/GPU timings. |
| 2 | Add room reflection captures/IBL while extending the existing diffuse probe system. | ezEngine, RBDOOM, Godot | Moving props and wet/metal surfaces across rooms; reflection/probe leakage checks; resident bytes and update cost. |
| 3 | Introduce controlled bloom and colour grading; evaluate fog as a separate budgeted effect. | Wicked, Lumix | HDR ordering, preserved UI exposure, dark-room readability and GPU cost. Existing MSAA remains part of the comparison. |
| 4 | Test portal/occlusion visibility and use room connectivity for prefetch. | RBDOOM, ezEngine | Door transitions and fast turns without missing geometry/shadows; lower submitted work where rooms actually occlude one another. |
| 5 | Separate gameplay state from a renderer-owned representation; consider draw grouping/instancing. | ezEngine, Atom, rbfx | Fewer repeated state/draw costs; correct doors, hidden crate parts, transforms and resource retirement. |
| 6 | Test tiled/clustered light assignment when the present light list becomes a demonstrated constraint. | Godot, Wicked, ezEngine | Scene with representative overlapping lights; bounded list overflow and stable selection; include cluster-building cost. |
| 7 | Improve packaged asset metadata and streaming granularity where profiles show startup/residency costs. | ezEngine/Lumix resource systems, Atom image systems | Separate cold/warm first-playable time, I/O, decode, upload, peak memory and backtracking stalls. |
| 8 | Evaluate jobs or a native physics subsystem against specific CPU/behaviour needs. | Wicked jobs, ezEngine tasks and modular physics integrations | P95 frame times, allocations, contact stability, gravity-held props, doors and debris under the same route. |

This ordering prioritizes likely value for the target genre, not a claim that every listed feature is necessary. Clustered lighting and sophisticated frame graphs can add overhead to a small scene. Start with the simpler path until representative measurements justify the complexity.

Use the existing flashlight/stress/profile tools as the baseline. Compare the same Release build settings, camera route, resolution, assets and presentation. Track CPU/GPU time and frame-time tails, not just VSync-capped average FPS. Preserve the current ready-before-replacement and independent shadow-visibility behaviour.

**Other names you may encounter**

Flax is close to the desired C++/C# model, but its [licensing page](https://flaxengine.com/licensing/) and [FAQ](https://flaxengine.com/faq/) describe a custom EULA and redistribution restrictions. It belongs in a separate source-available comparison, not the permissive open-source shortlist.

Valve's [Source SDK licence](https://github.com/ValveSoftware/source-sdk-2013/blob/master/LICENSE) grants a constrained SDK/mod use case. The public SDK is not a permissively licensed release of the entire Source engine. It is useful for approved SDK study, but should not be treated as interchangeable with MIT engine code.

For either case, do not infer broad reuse permission from the presence of a public GitHub repository. Similarly, an open-source engine licence does not grant rights to the assets from the commercial games that inspired your visuals.

**Source snapshots**

The source routes above use these recorded revisions, rather than relying on future branch contents. These are research snapshots, not recommendations to adopt unreleased development builds.

| Repository | Branch inspected | Recorded revision |
| --- | --- | --- |
| turanszkij/WickedEngine | master | [79243b535c22](https://github.com/turanszkij/WickedEngine/commit/79243b535c226b1612449335d632f225e2303258) |
| godotengine/godot | master | [5d1cd20f9117](https://github.com/godotengine/godot/commit/5d1cd20f9117e4721fce626454e2323eb9462d0a) |
| o3de/o3de | development | [2462b90ee389](https://github.com/o3de/o3de/commit/2462b90ee389939336931a14e234fc4e3b4cd534) |
| ezEngine/ezEngine | dev | [08bae46f631e](https://github.com/ezEngine/ezEngine/commit/08bae46f631efdec027910fb3e0688372d692b7e) |
| nem0/LumixEngine | master | [55094198b3c6](https://github.com/nem0/LumixEngine/commit/55094198b3c6750a395c15e5f22a93993ab47cca) |
| rbfx/rbfx | master | [156d8d0917d4](https://github.com/rbfx/rbfx/commit/156d8d0917d4bde1f8751d746ee96ca7aa741813) |
| RobertBeckebans/RBDOOM-3-BFG | master | [ea29c006e84f](https://github.com/RobertBeckebans/RBDOOM-3-BFG/commit/ea29c006e84fedcc0e7c173c383a087ce0a8c0d5) |

**First study task**

Follow one material from imported metallic/roughness textures through a point/spot light and a reflection probe in Filament/Wicked, then compare that flow with TexturedModel.hlsl. Write down the additional inputs, preprocessing, shader work and resource lifetime each part requires. This produces a concrete next design while leaving C# gameplay intact.


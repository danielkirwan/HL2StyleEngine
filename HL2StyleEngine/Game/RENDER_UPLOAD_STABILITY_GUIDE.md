# Render Upload Stability And Destination Preparation

Follow-up 2026-11-04: nonblocking GPU intervals, fixed-physics tick/substage tracing and Release/Debug graphics-validation policy are now available. [Current investigation and refreshed acceptance](FRAME_PACING_GUIDE.md). The historical timings/failures below remain intact; later passes do not establish the root cause of every prior hitch.

Updated 2026-11-02. C# remains the runtime. No lighting/shader/material, physics, source-level or save-format changes in this pass. Adaptive texture streaming remains an explicit trial; the shared buffer-upload fix also applies to normal Game/F2/HS2Editor rendering.

## Diagnosis And Implementation

Opt-in frame traces now include `RenderStages` and per-frame `RenderMilliseconds` (version 2, preserving the old stage fields). The breakdown covers lighting, reflections, model planning/uploads, texture planning/pump, preparation, shadows, drawing, batched object/instance uploads, resource retirement, command submission and presentation. `ShadowObjectUpload` is a nested detail of `Shadows`; do not add both to obtain frame totals. These are **host-side wall timings**, including driver blocking, not GPU timestamps. Disabled scopes do not allocate or read the clock. Frame capture remains bounded to the explicitly requested limit; no per-frame trace files are written during normal play.

The instrumented old-path full-detail control (`qa/gameplay-soak-20261102-130856-517-native`) failed: 19.848ms gameplay mean / 33.809ms P95, 1,329 intervals >33.3ms and 124 >100ms. Across its measured render frames, object uploads averaged 2.233ms, P95 9.266ms, maximum 147.055ms, with 234 calls >33.3ms. Texture pumping had no >33.3ms calls (maximum 5.917ms). This locates a concrete bottleneck without blaming texture compression or reducing quality.

The existing object and instance buffers are dynamic, but only their active prefixes were uploaded. The [upstream Veldrid D3D11 implementation](https://github.com/veldrid/veldrid/blob/master/src/Veldrid/D3D11/D3D11GraphicsDevice.cs) routes partial dynamic updates through staging copies and whole-buffer updates through mapping. That supports the staging-reuse explanation for the observed blocking; the package is the project's existing opticfluorine 4.9.6 fork, not a newly installed upstream build.

The batched object, instance and shadow-object uploads now send the complete existing managed arrays. Buffer sizes, shader layouts, active draw ranges, geometry and texture data are unchanged. Maximum copied data is 1 MiB of object constants, 576 KiB of instance data when instancing is used, and 512 KiB of shadow-object data when a shadow layer needs updating. Inactive slots are not drawn. No new ring buffers, unbounded native allocations, GPU-idle waits or reduced lighting settings are introduced. `FullDynamicBufferUploads=false` preserves the previous paths for explicit same-binary QA controls; normal rendering defaults to whole-buffer updates. The unbatched per-object reference paths are unchanged.

An intermediate object/instance-only fix (`qa/gameplay-soak-20261102-132613-633-native`) reduced object-upload maximum **147.055 -> 1.425ms**, P95 **9.266 -> 0.270ms**, with zero >33.3ms upload calls. The full run still failed (16.845/18.695ms gameplay mean/P95; 76 >33.3ms, one >100ms). The remaining 103.551ms stall was inside `Shadows`, which used the same partial dynamic-buffer pattern. The final fix therefore includes the shadow-object batch, with a separate nested timing and uncached old/new shadow-image comparisons. This intermediate failure remains recorded; it is not a final acceptance pass.

## Destination Readiness

The package-backed texture trial now provides:

- `PrepareTextureDestination(position, radius=32)`: snapshot model dependencies intersecting the requested radius, retain/preload those models and pin their textures at full detail. The current camera/player is not moved.
- `TextureDestinationPending`, `TextureDestinationReady`, `TextureDestinationError`, `TextureDestinationModelCount`: explicit status. Readiness requires loaded render models and mip 0 on every active owner of their textures. A failed model/texture is not silently treated as ready.
- `CancelTextureDestination()`: release the temporary pins and clear readiness. A newer request replaces the old request. Level revisions, level changes, editor entry or disabling the trial invalidate the request.

Keep the request alive through the first destination frame, then cancel it. Callers remain responsible for collision, lighting, spawn safety and transitions; this is **not whole-level or arbitrary-view readiness**. Dependencies are bounded by the requested 1-128 metre radius and the current entity snapshot. Moving/new objects outside that snapshot or an unrestricted long view may require another request. GPU texture preparation is asynchronous; this API does not promise instantaneous travel.

`LaunchTextureStreamingLevelTest.bat` retains F6 comparison and F2 full-detail authoring. **F7**, or the trial panel's **Prepare distant room** command, prepares the next test destination, moves only when ready, and releases the pins after reveal. The pending command can be cancelled in the panel. Ordinary level launchers do not enable adaptive streaming or add this trial navigation.

## Functional Evidence

- **151** same-scene old/new upload image comparisons pass with **0/255** maximum difference: six rooms, eight view angles, flashlight off/on, five moving shutters, thresholds and held-object crossings. Entity IDs, authored scene data and draw counts are preserved. F2 outlines remain visible. Final report images: `qa/buffer-uploads-20261102-133818`, with shadow caching disabled in both paths so the comparison exercises shadow uploads too. The earlier `132501` run tested main/instance updates with shadow caching enabled.
- Three distant destination first reveals (rooms 3, 11, 8) match restored full-detail references exactly: mean/max **0/255**. Preparation in the final warm render-only sample took 231/139/16ms, not a cold-load benchmark or fixed latency guarantee. Source pose stays unchanged until reveal; cancellation, supersession, invalid input, disabled-trial rejection, injected model failure, editor entry and level revision checks pass. `qa/texture-destination-20261102-140252-551`; the earlier `132345-835` repeat also passed. Model/reflection arrival is held ready to isolate texture quality; lighting/physics readiness is not proven by these images.
- **25** texture binding/lifetime checks pass, with unchanged geometry and exact legacy/PBR full-detail restoration (`qa/texture-bindings-20261102-140234.json`). Scripted F2 deferral, full-detail editor entry/exit, two F6 toggles and restoration pass (`qa/texture-level-20261102-140043-814`). This workflow test is not a performance sample or physical-keyboard F7/button acceptance.
- All **15** historical Game/F2/HS2Editor image references pass within **1/255**; no reference baselines were overwritten. Disabled profiling scopes are allocation-free; attribution, reset and detached snapshots pass `validate-render-profile`.
- **48** deterministic texture-demand checks and **483** mip/cache checks across 70 textures pass again (`qa/texture-mips-20261102-141130`). Release solution and Release/Debug authoring builds pass with zero warnings/errors. No dependency, shader or asset recook is required for this change.

## Final Performance And Remaining Gate

Sequential Release / RTX 3060 / D3D11 / actual 1920x1009 host, uncontrolled warm filesystem cache. Both long modes use 18 cycles / 216 gameplay actions, 120 warmup + 21,600 measured frames and isolated saves, restoring full detail for comparable settled memory checkpoints. Gates and lighting settings were not relaxed; no forced collection was used.

| Final long mode | Gameplay mean / P95 | >33.3ms / >100ms | Settled private growth | Result |
| --- | --- | --- | --- | --- |
| Full-detail package | 16.604 / 18.046 ms | 0 / 0 | +64.36 MiB | Pass |
| Adaptive streaming package | 17.495 / 18.810 ms | 423 / 5 | +32.08 MiB | Frame gate failed |

Reports: `qa/gameplay-soak-20261102-134101-848-native` and `134825-508-native`. Both complete all cycles/actions with zero source texture decodes/model parses or package fallbacks. The streaming run makes 232 replacements and reaches 70,103,264 resident texture bytes. Its memory samples are within the original 128 MiB guard, but validation stops at frame failure: it is **not an overall pass** or proof of leak freedom.

The final streaming trace has no >33.3ms calls in object uploads (maximum **1.181ms**), shadow uploads (maximum **3.311ms**), total shadows (**10.858ms**) or texture pumping (**5.155ms**). The largest remaining calls are in **Present**, reaching **106.211ms**; sampled worst frames have no GC pause. This isolates where the host waits, **not whether the root cause is GPU work, driver/presentation pacing or external contention**. Do not attribute it to texture decoding, claim it is external, or disable synchronization/shadow quality without further evidence. GPU/presentation isolation and another matched long acceptance run are the next stability work.

The final short adaptive route passes 3,600 measured wall intervals at **16.672ms mean / 17.203ms P95**, zero >33.3ms or >100ms, 68 replacements, zero waits/fallbacks and complete restoration (`qa/texture-level-20261102-140358-809`). Its following full-detail control averages **16.694/17.250ms**, but **fails** with two >33.3ms intervals, none >100ms (`qa/texture-level-20261102-140558-054`). One sampled frame spends 36.821ms in Physics, another 27.321ms in Present; neither has a GC pause and uploads remain small. Thus remaining hitch risk is **not exclusive to adaptive streaming**, and a long full-detail pass is not blanket frame-pacing acceptance. Keep both failed and passed runs; investigate presentation and physics spikes without changing solver behaviour to mask them.

Normal launchers remain full detail, and adaptive streaming remains an explicit experiment. These are approximately-60-FPS observations, not a locked minimum 60 FPS or cold-start improvement. The first room-3 prepared image was visually inspected: models/textures render and the image matches its full-detail reference; this does not change the atmosphere fixture's existing material/lighting art or constitute a manual F7 playthrough.

## Reproduce

Build `Tools/LevelAuthoring/LevelAuthoring.csproj` in Release, then run its DLL sequentially:

```text
validate-render-profile
check-buffer-upload-pixels
check-texture-destination
check-texture-bindings
check-texture-editor
validate-pbr-legacy
soak-gameplay --native-ui --long --texture-reference --partial-object-uploads
soak-gameplay --native-ui --long --texture-reference
soak-gameplay --native-ui --long --texture-streaming
```

Long controls retain the existing frame/memory limits and isolated QA saves. Full-detail checkpoints allow comparable memory samples. Do not count synchronous image readbacks as performance samples, force collection to pass a guard, or infer minimum 60 FPS from a mean near 60. Historical failures remain in [the validation report](ENGINE_UPGRADE_VALIDATION.md) and [the texture trial guide](TEXTURE_STREAMING_TRIAL_GUIDE.md).

Spatial audio is the following feature milestone, not part of this rendering change. Use the installed tools and the separate-project source inventory in [AUDIO_WORKFLOW.md](AUDIO_WORKFLOW.md); no other project's files were modified or imported here.

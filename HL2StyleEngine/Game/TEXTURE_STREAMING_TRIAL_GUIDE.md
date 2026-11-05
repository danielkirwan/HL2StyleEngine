# Texture-Only Streaming Trial

Frame-pacing follow-up (2026-11-04): new GPU/physics/thread CPU tracing and validation-off Release controls narrow the remaining investigation. Initial refreshed long streaming runs pass, but a subsequent full-detail long control fails with a severe CPU-side stall; this is not production rollout. No scheduler, texture-quality or physics-policy change. [Current reports and repeatable diagnostic BAT](FRAME_PACING_GUIDE.md).

Render/destination follow-up (2026-11-02): whole dynamic-buffer uploads remove the measured object/shadow update stalls, with 151 exact old/new images. The final full-detail 18-cycle control passes; the streaming control still fails frame acceptance, now with large stalls measured mainly in presentation. Streaming remains opt-in. Explicit preparation now covers the tested distant-room first reveals; F7 in this launcher waits for model/texture readiness before moving. [Latest evidence, limitations and reproduction](RENDER_UPLOAD_STABILITY_GUIDE.md). Earlier failures below remain historical evidence and are not erased by later passes.

Transition follow-up (2026-11-02): the opt-in scheduler now retains finer detail for 1.5 seconds before a stable demotion, prioritizes important promotions, and requests full detail for connected-room prefetch. CPU scheduling, GPU ownership, adjacent-room quality and F2/F6 checks pass. New long controls stay within the measured memory guard but **fail frame-time acceptance in both modes**; normal launchers still use full detail. [Audio workflow inventory for the following milestone](AUDIO_WORKFLOW.md).

Follow-up 2026-11-02: measured lightmap/reflection cache allocations are reduced without changing mip demand, texture quality or cache formats. Two final full-detail 18-cycle reload runs pass unchanged memory/frame gates; streaming-enabled long controls and transition-quality acceptance remain separate. [Current memory investigation, validation and remaining rollout gates](LIGHTING_MEMORY_GUIDE.md). The original failures and trial results below are retained as historical evidence.

Implemented 2026-11-01. This is an opt-in real-level experiment, not production rollout. C# remains the runtime. Normal Game, F2, HS2Editor, existing launchers, source levels, shaders, lighting, collision, save formats and RE:Dox behaviour are unchanged.

## Try It

Run `LaunchTextureStreamingLevelTest.bat` from the repository root. It builds Release and prepares the existing immutable `atmosphereTest.json` snapshot, using the project's current models. It does not overwrite a source level. Saves go into a separate authoring `qa/texture-level-<timestamp>/Saves` folder.

- Normal movement, flashlight, inventory and gravity gun remain available.
- F6 switches between room-aware texture streaming and full-detail reference; the trial panel also has a Full-detail reference checkbox when the cursor is available. Allow the preparation indicator to settle before comparing.
- F7, or Prepare distant room in the panel, prepares the next distant-room destination without moving immediately. It moves only after its model/texture dependencies are ready. The panel can cancel a pending request; entering F2 also cancels it. This tests texture readiness, not arbitrary whole-level loading or collision/lighting readiness.
- The trial panel reports actual resident texture payload, planned payload and completed replacements. The initial target is 48 MiB; quality protection can exceed that target.
- F2 first requests full detail. If restoration is still running it leaves gameplay visible and asks for another F2 press. Once ready, F2 enters the existing editor. The trial does not present coarse textures as the authoring reference.
- Existing `LaunchTextureMipTest.bat` remains the earlier model-replacement gallery. The new launcher tests texture-only replacement inside actual gameplay.

Package preparation warms file caches and is excluded from runtime measurements. No new importer step, source asset conversion or cache-format change is required.

## Ownership And Quality

`TextureMipResidency` prepares one texture mip tail on a worker and updates every active legacy/PBR material that shares that texture. All replacement resource sets are prepared before publication. Vertex/index buffers, model transforms and colliders are never rebuilt for a mip change. Old material sets and their texture references remain alive until the GPU frame fence completes. New work waits for prior retirement, limiting overlapping replacements.

The existing BC7/BC5/LZ4 texture cache provides checksummed mip payloads. `AssetPackage.LoadTextureMipTail` verifies declared unchanged model ownership and reads only the texture tail; a stale/corrupt request retains the currently displayed texture and reports an error. It does not silently parse a whole GLB to complete this experimental request. Ordinary full-model package loading keeps its existing strict-source fallback.

Shared demand uses the most detailed consumer, not the last model visited. Metadata is counted once per texture identity. Projected bounds and UV extent influence demand, with both size hysteresis and 1.5-second time-based retention before committing a stable coarser target. Returning to finer demand or changing the coarse target resets that timer. Promotions are immediate requests, serviced ahead of demotions in importance order; asynchronous preparation/upload can still take time. Nearby objects, held objects, viewmodels and unclassified owners stay full detail. Connected-room prefetch requests mip 0; visible/prefetched quality ceilings cannot be degraded just to meet a budget. Planned payload includes retained detail, so the soft budget can be exceeded honestly. The trial respects the existing room graph but never removes physics or gameplay entities.

The target is **not a hard RAM/VRAM cap**. Temporary old/new texture overlap, source CPU mip arrays still retained by loaded models, geometry, lightmaps/reflections, native driver resources and unsupported-BC RGBA fallback are separate costs. The descriptor estimate includes previously registered textures even if their model was evicted, so it can be conservative. The existing upload budget is soft: one indivisible texture upload can exceed it.

## Memory Investigation

The six-room native-inventory diagnostic now supports `soak-gameplay --native-ui --puzzles --long --memory-trace`: 18 cycles / 21,600 measured rendered frames. Profiling permits up to 36,000 explicitly requested frames; normal gameplay is unaffected. Per-cycle reports include GC allocation, committed/fragmented heap bytes, old entity/mesh weak-reference counts and resident texture/model payload.

The first attempt stopped at the former 10,000-frame harness cap and is retained as incomplete: `qa/gameplay-soak-20261101-144720-085-native`. After correcting the cap, `qa/gameplay-soak-20261101-145722-514-native` completes all 18 cycles and 216 actions. Gameplay intervals average **16.661 ms**, P95 **17.802 ms**, with one >33.3 ms and none >100 ms. Loading intervals remain separate in the report.

**The memory gate still fails:** process-private memory grows **146.32 MiB**, above the unchanged 128 MiB limit. Resident model-plus-texture payload stays exactly **113,422,478 bytes**, texture payload **106,394,384 bytes** / 81 textures. Managed bytes at the settled samples decrease, while fragmented heap bytes increase. This points toward allocation/heap/native-memory follow-up rather than proving accumulation of GPU models, but is not a complete root-cause diagnosis.

After timing and acceptance checks only, an optional diagnostic full collection finds **0 of 13,773 old entities** and **0 of 90 old collider meshes** alive. Approximately 223 MiB of fragmented managed heap remains reported. This collection is not used during gameplay, does not turn the failed gate into a pass, does not compact the LOH explicitly, and does not prove leak freedom. The workload intentionally adds/drops items over successive cycles; live current-world entities are not all expected to have constant counts.

## Reproduce

Build Release `Tools/LevelAuthoring/LevelAuthoring.csproj`, then run its DLL sequentially:

```text
validate-texture-demand
check-texture-transitions
check-texture-destination
check-texture-bindings
check-texture-editor
soak-texture-level --reference
soak-texture-level
validate-texture-mips
check-texture-mip-images
validate-pbr-legacy
soak-gameplay --native-ui --puzzles --long --memory-trace
soak-gameplay --native-ui --long --texture-reference
soak-gameplay --native-ui --long --texture-streaming
```

Texture-binding checks cover shared/late owners, eight promotion/demotion cycles, unchanged geometry buffers, byte-exact legacy/PBR full restoration, corrupt payload rejection, read failure retention/retry, superseded successes/failures and zero owned payload after retirement. The real-level route uses actual gameplay with scripted room teleports, walking/looking, flashlight toggles and gravity hold/release, followed by full-detail restoration. It is not a walking-only playthrough or cold-loading benchmark.

The two new long modes use the same immutable atmosphere package, native UI, 18 save/load/reload cycles, 21,600 measured frames and isolated saves. Streaming is enabled during the active portion of each cycle, then both modes restore full detail for comparable settled checkpoints. Existing frame and memory gates remain unchanged; no diagnostic collection is used to pass them. This is distinct from the older six-room `--puzzles` control.

## Transition Acceptance (2026-11-02)

- 48 deterministic scheduling checks pass, including rapid turn/door demand oscillation, immediate quality pins, revised-target timing and retained-payload accounting.
- 25 GPU binding/lifetime checks pass, including a two-independent-texture priority test. Full crate payload is **136,656 -> 2,256 bytes** with unchanged geometry and exact legacy/PBR restoration (`qa/texture-bindings-20261102-122853.json`). Binding tests disable the timer to isolate ownership; the separate deterministic checks exercise the production delay.
- Twelve first-frame/early/settled image comparisons across connected room transitions **0 -> 1 -> 2 -> 3 -> 7** all match full detail exactly: mean/max difference **0/255**. The test spends 2.5 seconds preparing each doorway approach, then crosses four metres; it does not promise instant loading from arbitrary destinations. Render-only 1280x720, torch on, model/reflection arrival held ready to isolate texture quality; synchronous readbacks are not performance samples. Full/minimum texture payload **82,626,208 / 72,992,416 bytes**, 38 replacements, zero package fallbacks. Report/images: `qa/texture-transitions-20261102-122458-039`; a first-frame room-3 capture was visually inspected.
- An earlier unannounced 66m teleport exposed a genuine limitation: room-3 first-frame mean error **4.908/255**, **12.846%** of pixels differing by more than 12. Early/settled detail recovered. The failed report/images remain at `qa/texture-transitions-20261102-121853-512`. Connected-room full-detail prefetch improves ordinary traversal but is **not** acceptance of unprefetched teleports; those require explicit destination readiness/prefetch before reveal.
- A one-cycle native streaming smoke test passes at **16.662 ms mean / 17.934 ms P95**, zero >33.3ms or >100ms gameplay intervals, zero package fallbacks and successful restoration (`qa/gameplay-soak-20261102-122307-861-native`). One settled checkpoint is not a memory-growth test.
- F2/F6 workflow passes with the production retention timer: coarse F2 deferral, full-detail editor entry/exit, two comparison toggles and restored full detail (`qa/texture-level-20261102-124942-439`). This is not a performance acceptance run. The 483 existing mip/cache checks pass again across 70 textures (`qa/texture-mips-20261102-125033`).

### Matched Performance Controls

Release, RTX 3060 / D3D11, actual 1920x1009 host, sequential processes, uncontrolled warm filesystem cache. All values below are measured wall intervals, including presentation, not GPU timestamps. Gates were not relaxed and failures are retained.

| Mode / workload | Gameplay mean / P95 | >33.3ms / >100ms | Settled private growth | Result |
| --- | --- | --- | --- | --- |
| Full-detail package, 18 cycles | 17.303 / 18.419 ms | 240 / 32 | +112.21 MiB | Frame gate failed |
| Streaming package, 18 cycles | 17.659 / 20.180 ms | 424 / 17 | +104.83 MiB | Frame gate failed |
| Full-detail route, 3,600 intervals | 16.661 / 17.265 ms | 0 / 0 | Not a long memory test | Pass |
| Streaming route, 3,600 intervals | 16.707 / 17.593 ms | 4 / 0 | Not a long memory test | Hitch gate failed |

Long reports: `qa/gameplay-soak-20261102-122916-966-native` and `123748-528-native`. Both complete 18 cycles / 216 actions, zero package fallbacks, no texture errors, and full-detail settled checkpoints without forced collection. Model-plus-texture payload changes by -204,848 bytes in each run; the sampled memory growth is below 128 MiB, but the validation stops at the earlier frame failure, so neither whole run is a pass. Streaming makes 252 replacements and reaches 70,103,264 texture bytes during active gameplay; both modes finish with the same 82,489,568 texture bytes after normal crate/world changes. This is not proof of leak freedom or constant total VRAM.

Short reports: `qa/texture-level-20261102-124546-962` and `124717-602`. Streaming makes 42 replacements, minimum texture payload 72,992,416 bytes and peak 82,648,080 bytes including overlap, with zero streaming waits/fallbacks and successful final restoration. The earlier route's 405 replacements/minimum 52,020,896 bytes are historical: this policy favors prefetch quality and retention over that larger payload reduction. The current sample is not a controlled proof of a particular transaction-speed improvement.

Sampled long-control stalls are mostly inside host `World` rendering, not save serialization or GC pauses. The short streaming trace also has stalls at frames 313/316, before reduced-detail streaming starts at frame 420, plus two later stalls. That prevents attributing all failures to mip uploads, but does not prove an external cause or clear streaming of performance risk. Next: finer render-stage/fence/upload timings and reproducible matched controls, followed by long manual playtesting. Do not claim minimum 60 FPS from an average near 60.

Release solution and Release/Debug authoring builds pass with zero warnings/errors. No shader, lighting, physics, source-level, save-format or normal-launcher changes were made in this transition pass.

## Validation Results

Release, local RTX 3060 / D3D11, sequential desktop tests. Host route render dimensions are 1920x1009 on this desktop, not a claimed 1920x1080 capture.

- 24 texture-binding assertions pass: crate texture payload **136,656 -> 2,256 bytes**, unchanged vertex/index buffers, exact legacy/PBR full restoration and complete owned-resource release. Final focused report: `qa/texture-bindings-20261101-151153.json`.
- Existing 483 mip/cache checks pass across 70 textures, and the previous whole-model GPU mip tests still pass with exact restoration, retries, supersession and zero payload after retirement.
- Initial reference route: **16.661 ms mean / 17.827 ms P95**, zero >33.3 ms; streaming: **16.661 / 17.811 ms**, zero >33.3 ms. Each measures 3,600 frames. Zero streaming waits or package fallbacks; both complete gravity hold/release and walking. Reports: `qa/texture-level-20261101-150506-760` and `150633-473`.
- Initial streaming route completes 391 texture transactions. Full texture payload **82,626,208 bytes (78.8 MiB)**, minimum **52,020,896 (49.6 MiB)**, peak **82,975,760** including replacement overlap. The minimum is one route sample, not a constant usage or whole-VRAM saving. Room changes/camera turns still cause repeated promotions/demotions; this is not a final transition-quality policy.
- Final scheduler repeat passes all 3,600 samples at **16.661 ms mean / 16.936 ms P95**, zero >33.3 ms, zero streaming waits/fallbacks, 405 replacements, complete full-detail restoration and identical full/minimum/peak texture payload. Report: `qa/texture-level-20261101-152112-455`. A final repeat is not evidence that every frame on every machine will meet 16.67 ms.
- `check-texture-editor` passes coarse-F2 deferral, full-detail F2 entry/exit and two F6 snapshot toggles with restored detail. Report: `qa/texture-level-20261101-151933-961`. This is a workflow test, not a frame-time acceptance sample.
- The actual BAT built and displayed the textured atmosphere level and reduced-residency panel. Desktop keyboard/close injection did not reach the SDL window reliably; do not count that as manual F6/F2 acceptance. The isolated test process was stopped after verifying its exact command line. The scripted workflow above verifies the game-input path separately; physical-keyboard/checkbox playtesting remains useful.
- All 15 historical Game/F2/HS2Editor image references pass within 1/255; no baselines were replaced. All 54,497 geometry/contact checks and five puzzle/door-route checks pass. The 392 RE:Dox persistence/inventory checks pass outside the restricted sandbox; its earlier restricted attempt failed to create its temporary DOX cache and is not counted as a pass.
- Release solution and Release/Debug authoring builds pass with zero warnings/errors. See the memory section for the retained failing long run; these passes do not supersede it.

## Remaining Acceptance

Keep normal launchers full detail. The later [render-upload pass](RENDER_UPLOAD_STABILITY_GUIDE.md) fixes the measured object/shadow upload blocking, but the long streaming run still fails in presentation. Before production adoption: isolate remaining presentation/GPU pacing with reproducible matched controls, without relaxing gates; continue native/driver retention checks and longer manual/standalone-editor playtests. Adjacent-room and explicitly prepared distant-room images, plus scripted F2/F6 checks, pass. Unannounced teleports are still not guaranteed sharp; callers must use readiness before reveal and choose dependencies/radius appropriate to their view. Physical F7/button playtesting and model/lighting/collision transitions beyond the texture-only fixture remain open. Projected-size/UV demand is conservative, not a per-triangle screen-density guarantee. Do not claim a locked minimum 60 FPS or a startup speedup from texture payload savings. Spatial audio remains the following separate horror-game milestone; use the documented installed tools and separate-project source inventory in [AUDIO_WORKFLOW.md](AUDIO_WORKFLOW.md).

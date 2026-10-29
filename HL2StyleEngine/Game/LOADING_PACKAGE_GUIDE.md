# Loading Diagnostics And Asset Packages

Current inventory follow-up (2026-10-27): bounded native/fallback UI preparation during existing loading frames fixes the measured first-open cases, with separate populated native tests and seven passing source/package trials. This moves some work earlier; it does not prove faster total startup. [Latest measurements and limits](INVENTORY_PERFORMANCE_GUIDE.md). The [earlier crate/hitch investigation](HITCH_STABILITY_GUIDE.md) retains its failures. Package formats and strict-source/fallback contracts below are unchanged.

First pass: 2026-10-27, following user acceptance of the atmosphere controls. C# stays. Original level files, textures, lighting, physics, saves and ordinary launch selections are unchanged.

Follow-up: packaged lighting dependency validation and no-build probes are now implemented. The reduced I/O/worker cost is measured below; **a further total-startup improvement is not demonstrated**. The original model-package results are retained as historical evidence.

## Playtest

Run **`LaunchLoadingTest.bat`** at the repository root. It builds Release, prepares a dependency-only snapshot of the current `Game/Content/Levels/atmosphereTest.json`, then opens that twelve-room level using prepared assets. This should look the same: it changes loading, not the artwork. Movement, F flashlight, gravity holding, destruction, inventory and F2 retain their controls.

The console prints the exact generated folder and report paths under `Game/bin/Release/net10.0/LoadingTestPackages/<content-id>/`. `last-package-load.json` records the launch. Run `LaunchLoadingTest.bat --source-assets` for the matched strict-source reference and `last-source-load.json`. Ordinary launchers do not enable package mode.

`LaunchLoadingTest.bat --source-lighting` retains packaged models but uses strict source lighting fingerprints, matching the previous package loader. It writes `last-source-lighting-load.json`. This separates the new lighting change from the earlier model-loading change.

Continue authoring **the original level in Game/Content/Levels**, not the generated snapshot. Package preparation refreshes generated files from the original; F2 edits to the snapshot may be replaced. Snapshot GLBs are read-only as an accidental-edit guard. HS2Editor and new F2 model requests while editing continue through strict source validation; already loaded resources retain their existing lifecycle.

Offline package preparation measured about 1.9-2.2 seconds with existing cooked caches, separate from build time and the runtime speedup. Uncooked imports can take much longer. Changed source content creates a new generated directory; older snapshots are retained, not automatically deleted. Existing `bin` ignores keep them out of Git.

## Contract

- Version-1 `assets.json` records the level hash, model-cache version, relative model paths, source SHA-256 identities, source file size/time and texture dependency keys. Dependencies cover scene models, replacements/debris, weapon/player models and current spawned pickup models.
- This fixture includes 33 models, 70 unique cooked textures and six matching reflection files, not all 388 project GLBs. About 110.5 MiB of source GLBs remain for fallback and existing lighting/collision validation. UI, shaders and executables still come from the app build: this is not a standalone shipping archive.
- Healthy package model preparation uses the precomputed identity and existing lossless LZ4 model caches, BC7/BC5 textures and mip chains without re-reading GLBs. Existing checksums, version/count/payload limits remain enforced; texture dependencies must be declared.
- Runtime reflection/diffuse validation now reuses only declared immutable model identities whose source size/time still match. Scene data, placement, dependency names/order and lighting-version keys are unchanged. Shader bytes remain fully hashed; lighting payloads retain their own integrity/version/key validation. Unknown or changed models use source hashing. No new manifest/cache format or lighting recook is required.
- Offline capture/baking, HS2Editor and new/reloaded F2 lighting validation remain strict-source. Entering F2 does not discard already loaded lighting merely to change validation policy; normal resource lifetime and ready-before-replacement are preserved. Collision source hashing is instrumented, not bypassed.
- Each package validates a shared texture from disk on first use, then reuses a weak reference while its size/time remain unchanged. It does not pin all decoded mip data forever or trust another package's in-memory texture copy.
- Missing, corrupt, unsupported or changed cache entries fall back to the source loader. Invalid manifests fall back when an explicit existing level was supplied; otherwise startup fails clearly. Missing required source models produce a failed-load report rather than a false first-playable success.
- Manifest paths are relative/contained; duplicate entries, invalid hashes/keys, excessive metadata and junctions/symlinks are rejected. This is **not signed or tamper-proof packaging**. Same-size/same-time edits can evade metadata checks in immutable mode: rebuild after edits. The normal authoring loader still hashes source content and detects that case.
- RE:Dox/JSON persistence and ready-before-replacement stay intact. No C++ migration, shader/transform/normal changes, new compression format, mip residency or logical/physics room streaming is included.

## Reports

Game arguments: `--load-report <file>`, optional `--load-probe` to exit on completion/failure, `--asset-package <manifest>`, `--source-lighting` for strict lighting with packaged models, and `--source-assets` for the full reference path. Supply an absolute `--level` for explicit manifest fallback. Ordinary launches without a report collect no launch telemetry.

Milestones distinguish managed entry, host/device readiness, initialized game, first returned Present, required GPU-ready models, lighting readiness and first playable presentation. The endpoint requires current required models, configured lighting/requested lightmap bindings, an inactive loading overlay, unblocked gameplay, no modal/editor state, zero failed required models and a subsequent settled render. Missing required models report `required-assets-failed`; unresolved readiness times out at 60 seconds. Initialization failure and early close are distinct outcomes.

Process-relative time uses OS process start plus a monotonic managed stopwatch. Present return is not physical scanout or a GPU-idle fence; no routine GPU wait was added. The existing 0.85-second loading-overlay policy remains in the endpoint. Speculative neighbours and alternate lighting banks are not all required at spawn.

Stage totals cover level decoding, world/physics creation, worker queues, model source reads/hashing, CPU preparation and render-thread upload submission attempts. The follow-up adds reflection snapshot/key/queue/payload/submission stages, diffuse snapshot/key/payload/atlas-submission stages and collision source-hash timing. `*-dependency-read-hash` bytes identify actual source hashing; `*-dependency-identity` counts metadata hits. Shader hashing is included in reflection dependency bytes. Collision/diffuse stages are absent when the level does not request that work.

These nested/concurrent totals are **not additive wall time**. Upload timing is CPU submission, not GPU execution. Only scopes completed by the endpoint are counted. Counters do not cover every OS read, UI file or compressed payload; zero model-source bytes therefore does not mean zero disk access. Residency bytes are not total RAM/VRAM.

## Lighting Dependency Follow-Up

2026-10-27, Release RTX 3060/D3D11, eight sequential fresh game processes: discard one warmup per mode, retain three each. Both modes use the same packaged models and the same level. Only lighting validation changes. OS files were warmed by preparation; no cold-disk claim. Reports: `LoadingTestPackages/F2054EEB70EE2390C40E6239/qa/20261027-094110/`.

| Metric | Package models + strict lighting | Package models + identity lighting |
| --- | ---: | ---: |
| First playable samples, ms | 3142.05, 3058.12, 3055.34 | 3063.61, 3200.65, 3156.34 |
| Median first playable, ms | 3058.12 | 3156.34 |
| Median lighting-ready milestone, ms | 1944.77 | 1754.72 |
| Median reflection-key work, summed worker ms | 315.84 | 17.56 |
| Reflection source-hash bytes/run | 227,165,928 | 98,412 |
| Reused model identity calls/run | 0 | 66 |

About 216.5 MiB of repeated model reads is avoided; the remaining 98,412 bytes are shader inputs. Key work falls about 94%, and lighting becomes ready about 190 ms earlier. **Total first playable is about 98 ms slower in this small sample, not a demonstrated speedup.** Keep this result; do not compare the discarded 3542.06/3143.80 ms warmups as a cold improvement. Snapshot preparation still costs roughly 335 ms summed across workers. Host setup, model availability, render/upload scheduling and the existing overlay policy remain in the endpoint; this test does not establish which causes the total variance. No overlay delay, physics step, shader or quality limit was reduced to improve the number.

Checks pass: 38 dependency/key/invalidation/corruption/cancellation checks including real reflection and diffuse/probe caches; 16 actual-game strict/package image pairs (six room views, torch on/off, F2 and a real probe-baked level), maximum 1/255 channel difference. F2 reloads reuse no immutable identities. Repeated 56 package checks, real-game malformed-manifest/missing-required probes, 400 collision contacts/rays plus five real static mesh colliders, 55 atmosphere GPU checks and all 15 historical Game/F2/HS2Editor images pass. Original textures, lighting files and baseline images were not replaced.

Full-host follow-up, actual 1920x1009 client, 120 warmup +300 measured frames:

| Workload | Mean/P95 frame interval, ms | Frames >33.3 ms |
| --- | ---: | ---: |
| Packaged torch-on stress | 16.718/17.087 | 1 (34.501 ms) |
| Packaged torch-off stress | 16.664/17.689 | 0 |
| Packaged torch-on traversal | 16.667/17.061 | 0 |
| Ordinary strict-source torch-on stress | 16.666/17.063 | 0 |

All unchanged gates pass: mean <=17 ms, P95 <=21 ms, at most one >33.3 ms, none >100 ms or streaming waits. Package runs have 30 model hits/zero fallbacks. Stress covers 120 crate fragments, gravity holding and inventory. This is approximately 60 FPS, not a locked minimum or long-soak guarantee.

## No-Build Probe

1. Run `PrepareLoadingProbe.bat` once. It builds Release and prepares the current atmosphere snapshot without launching the game. This warms OS files, so do it **before**, not after, a prospective cold test.
2. Run `RunLoadingProbe.bat` to measure and auto-close the prepared game. It does no build, cook or asset copy. Unique reports go to the prepared package's `probes/` folder.
3. `RunLoadingProbe.bat -Mode SourceLighting` tests the previous package-lighting behaviour; `-Mode Source` tests full strict source. Default mode is `Package`.
4. For a first run after a manual Windows restart, add `-ColdCandidate`. The report explicitly labels this **user-declared and not independently verified**. This tooling does not restart Windows or flush its file caches. Use separate restarts for comparison modes; sequential runs are not equivalent cold samples. Background Windows activity can still warm files.

Re-run preparation after source edits. Do not edit generated snapshots. All three probe modes were smoke-tested without restart: 3000.5/3008.1/2997.1 ms for Package/SourceLighting/Source, respectively; these single uncontrolled-cache launches are launcher validation, not another A/B performance claim. The exact prepare BAT succeeds outside the sandbox (the first sandbox restore returned failure without compiler diagnostics); no-build BATs run successfully under the existing PowerShell policy. Build/project checks are recorded in the validation report.

## Original Model-Package Results

Release, local RTX 3060/D3D11 desktop, VSync; eight sequential fresh game processes, standard 1280x720 requested window. One initial run per mode discarded to warm generated caches, then three per mode in alternating order. **OS file cache was warmed by packaging; no true cold-disk benchmark was performed.**

| Metric | Strict source | Package |
| --- | ---: | ---: |
| First-playable samples, ms | 3137.01, 3099.38, 3154.77 | 3049.48, 3005.11, 3088.47 |
| Median first playable, ms | 3137.01 | 3049.48 |
| Median model CPU preparation, ms, summed workers | 294.31 | 208.63 |
| Model-preparation GLB bytes per run | 107,447,128 | 0 |
| Required/resident models at completion | 30/30 | 30/30 |
| Package hits/fallbacks | not applicable | 30/0 |

About **88 ms / 2.8% earlier first playable**, and **29% less model-preparation CPU**. Device/runtime setup, lighting validation, uploads and overlay policy remain significant. Three samples are local evidence, not a general guarantee. Discarded source/package runs were 3647.43/3160.51 ms; do not compare them as a cold-load gain. Detailed reports: generated package `qa/20261027-084037/`.

An earlier implementation revalidated textures per part: median model CPU was worse, about 389 vs 292 ms, despite avoiding GLB reads. This was corrected before delivery. Earlier reports remain in `qa/20261027-083406/`; no integrity or performance gate was relaxed.

Full-host checks: 1920x1009 actual client pixels, 120 warmup + 300 measured frames, real gameplay/HUD/physics and accepted atmosphere.

| Packaged workload | Mean/P95, ms | Frames >33.3 ms | Streaming waits |
| --- | ---: | ---: | ---: |
| Torch-on destruction/hold/inventory | 16.783/19.217 | 1 (43.268 ms) | 0 |
| Torch-off matching stress | 16.668/18.859 | 0 | 0 |
| Torch-on room traversal | 16.668/18.984 | 0 | 0 |
| Normal strict-source torch-on stress reference | 16.664/18.555 | 0 | 0 |

All pass unchanged gates: mean <=17 ms, P95 <=21 ms, at most one >33.3 ms, none >100 ms and no streaming waits. Stress includes six crates and 120 peak fragments. All report 30 package hits/zero fallbacks. Approximately 60 FPS, **not a locked minimum**; retain the one hitch and longer-soak caveat. Traversal exercises room/reflection changes, not model eviction or lower memory in this shared-asset fixture.

56 package checks pass: all-model bit-exact geometry/material/mip comparisons, relocation, metadata validation, missing/truncated/corrupt/stale dependencies, undeclared textures, reference mode, fresh-process missing-texture fallback and telemetry serialization. Two real-game failure probes pass, restoring their temporarily missing generated model. The 388-model/661-part strict-source suite passes including same-size/same-time edits and cache repair; its first sandbox run hit access denied on temporary atomic replacement, and the normal-desktop repeat passed. All 55 atmosphere GPU checks and 15 historical Game/F2/HS2Editor image references pass (<=1/255).

Release solution and Debug/Release authoring builds pass with zero warnings/errors. The exact BAT builds, packages, starts and exits successfully under a 60-frame automatic-exit smoke harness: 16.62/18.66 ms mean/P95, not an additional gameplay gate. It reports first playable at 3107.4 ms. Git whitespace validation passes. No original source models, baseline images, save files or lighting shaders were replaced.

## Reproduce

Build `Tools/LevelAuthoring/LevelAuthoring.csproj -c Release`; run its DLL with these commands, sequentially:

```text
validate-loading-package
validate-model-cache
benchmark-loading
benchmark-loading --lighting-only
validate-lighting-dependencies
check-lighting-dependency-images
check-lighting-dependency-images --baked
validate-loading-failures
validate-collision-cache
benchmark-packaged-gameplay --stress
benchmark-packaged-gameplay --stress --torch-off
benchmark-packaged-gameplay --route
check-atmosphere-pixels
validate-pbr-legacy
```

Follow-up (2026-10-27): host window/device/renderer/UI stages, startup update/physics/render/submission phases and up to 256 raw/clamped frame records now expose remaining startup work. No simulation, overlay duration or endpoint policy changed. An isolated mip-tail gallery is also implemented; ordinary package `Load` remains full-resolution. [Current measurements, failures and limits](TEXTURE_MIP_GUIDE.md).

Follow-up (2026-10-28): `Frames[].HostMilliseconds` now records same-frame host-stage deltas, while `RawMilliseconds` retains the preceding frame interval. Physics subscopes expose body stepping, collider construction, dynamic pairs and player movement; `startup-save-serialization` tracks one-time preparation moved from first saving into loading. Nested totals are not additive. [New startup measurements and gameplay acceptance](GAMEPLAY_STABILITY_GUIDE.md). Timing collection remains opt-in; the existing readiness endpoint, simulation clamp and loading-overlay duration are unchanged.

Physics follow-up (2026-10-28): exact geometry reuse and local support reuse reduce repeated settling work; [same-binary startup comparisons and behaviour tests](PHYSICS_STARTUP_GUIDE.md) are recorded separately from the earlier results above. Next: genuine post-restart candidates, manual acceptance and texture-granularity/room-prefetched detail before production integration. The prepared package reuses existing model metadata for lighting; signed archives and precomputed lighting keys are not implemented. Keep clustered lighting, broader streaming and native work profile-led; do not enable immutable trust for editable source assets.

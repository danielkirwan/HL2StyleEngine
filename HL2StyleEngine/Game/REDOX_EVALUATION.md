# REDox Save/Load Evaluation

Historical evaluation date: 2026-10-02. At that time REDox was tested in isolation, not adopted. Production integration was subsequently implemented on 2026-10-05; see [PERSISTENCE_AND_INVENTORY.md](PERSISTENCE_AND_INVENTORY.md) for current targets, caches, save compatibility and verification. References below to "current" describe the October 2 baseline, not the present engine.

## Summary

REDox can encode the engine's existing level and save DTOs, using its System.Text.Json compatibility settings. Binary DOX is promising for generated runtime data. It does not replace GLB loading, texture decoding/upload, physics construction, filesystem operations or save-game state collection. A faster serializer is not by itself a fix for level-startup time or frame rate.

The engine currently targets .NET 8. The tested REDox source requires .NET 10, and its System.Text.Json adapter is marked preview. A .NET 10 migration would therefore be a separate prerequisite, not a package-only change. Both isolated game builds compiled with zero warnings/errors, but that is not approval to upgrade the whole engine/editor toolchain without regression testing. Sources: [REDox](https://github.com/CAPCOM-TD-OSS/REDox), [core project](https://github.com/CAPCOM-TD-OSS/REDox/blob/e64ee501c18356a7812d68eab0a83b915444d2fd/src/REDox/REDox.csproj), [compatibility project](https://github.com/CAPCOM-TD-OSS/REDox/blob/e64ee501c18356a7812d68eab0a83b915444d2fd/src/REDox.Serialization.SystemTextJson/REDox.Serialization.SystemTextJson.csproj).

## Current Engine Path

- `Engine.Editor/Editor/LevelIO.cs` reads text and uses System.Text.Json to create `LevelFile`. It accepts comments and trailing commas, repairs a null entity list and migrates legacy `Boxes` into entities. Saving writes indented JSON directly to the destination.
- `LevelEditorController.LoadOrCreate` and `Reload` each rebuild runtime entities twice. That existing behavior was deliberately preserved in this comparison. Optimizing it needs separate correctness tests.
- `Game/HL2GameModule.cs` collects gameplay state into `PrototypeSaveData`: inventory/storage, collected items, doors, puzzles, broken objects, weapon magazines/reserves, player stats/pose and save metadata. This is separate from the authored level. Its save DTO has no explicit schema-version field yet.
- Other JSON use includes prefabs, registered-script configuration strings, undo snapshots and project settings. Replacing `LevelIO` alone does not replace those serializers.
- Runtime construction, mesh-collider preparation, asynchronous GLB loading, texture creation and rendering follow the level-data read. These remain the same in the experimental builds.

## Measured Results

Primary test level: `sixRoomTest.json`, 758 entities, 1,187,344 source bytes. Data-only timings below include file read and typed deserialization with warm OS/JIT/serializer caches. They exclude assets, entity creation and physics.

| Reader | Runtime | Median data load | p95 batch mean | Allocated per read |
| --- | --- | ---: | ---: | ---: |
| Current System.Text.Json text path | .NET 8.0.19 | 7.35 ms | 8.13 ms | 7.54 MB |
| System.Text.Json UTF-8 control | .NET 8.0.19 | 6.19 ms | 6.33 ms | 2.76 MB |
| Current System.Text.Json text path | .NET 10.0.12 | 6.86 ms | 8.60 ms | 6.14 MB |
| System.Text.Json UTF-8 control | .NET 10.0.12 | 5.30 ms | 5.48 ms | 2.55 MB |
| REDox JSON | .NET 10.0.12 | 4.52 ms | 4.65 ms | 2.08 MB |
| REDox JSON, parallel enabled | .NET 10.0.12 | 4.49 ms | 4.71 ms | 2.08 MB |
| REDox binary DOX | .NET 10.0.12 | 2.00 ms | 2.08 ms | 1.73 MB |

DOX reduced warmed-up data-loading time by about 73% versus the current .NET 8 text path (approximately 3.7x throughput), but only saved about 5.35 ms per read. On the same .NET 10 runtime it was about 3.4x faster than the text path and 2.7x faster than the UTF-8 control. The cheaper UTF-8-only .NET 8 change saved about 1.16 ms and roughly 63% of allocations without REDox.

### Actual Game Startup

Five fresh-process launches per variant, in rotated order, all at 1920x1009. Every run reached 32 loaded model-cache entries with zero failures. The ready metric includes the existing loading-overlay delay; ranges show the five observed runs, not confidence intervals.

| Variant | First presented frame, median | Assets-ready frame, median | Ready min-max | Scene data read during startup, median |
| --- | ---: | ---: | ---: | ---: |
| Current path, .NET 8 | 2.62 s | **6.03 s** | 5.99-6.14 s | 28.67 ms |
| Same path, .NET 10 control | 2.32 s | **6.32 s** | 6.25-6.39 s | 26.50 ms |
| REDox JSON, .NET 10 | 2.82 s | **6.21 s** | 6.16-6.29 s | 26.56 ms |
| REDox DOX, .NET 10 | 3.24 s | **6.31 s** | 6.19-6.51 s | 26.94 ms |

There is **no demonstrated end-to-end startup win over the current engine**. The .NET 10 JSON variant was about 0.11 s faster than its .NET 10 control in these runs, but DOX was essentially unchanged; first-frame timings also varied substantially. Do not attribute these differences to serialization alone or extrapolate them to other machines. Fresh-process initialization removes most of the warmed-up parser advantage. The measured scene-data read is under 0.5% of the ready time; window/device initialization, world/asset work, presentation and loading delay dominate the remainder. Their individual costs need separate profiling.

### Saving And Size

For the six-room DTO, median serialization alone was 4.37 ms with current .NET 8 JSON, 3.71 ms with .NET 10 JSON, 2.93 ms with REDox JSON and 1.34 ms with DOX. Including buffered file writing gave 6.74, 6.50, 6.19 and 5.45 ms respectively. These are not durable save timings.

The small representative gameplay save took about 0.04-0.05 ms to read and 0.13-0.15 ms to serialize/write in the .NET 10 tests. At this size, versioning, recovery and correct state restoration matter much more than parser speed.

The level's DOX payload was 837,224 bytes: 29.5% smaller than the existing indented JSON. Equivalent compact JSON was only 684,863 bytes, so DOX was about 22% larger than compact JSON. DOX is not compression, and size benefits depend on the data. The small save also became larger in DOX.

Raw data: [current-runtime measurements](../Tools/REDoxBenchmark/results/micro-net8.json), [REDox/control measurements](../Tools/REDoxBenchmark/results/micro-net10.json), and individual `boot-*.json` files in the same folder. These files contain samples, parser checks, runtimes, source hashes and per-startup metrics.

## Correctness And Compatibility

All five authored level files, the existing `DoorFrame&Door` prefab, parent/vector/script fixtures, legacy box data, and a populated gameplay-save fixture passed equivalent read and round-trip checks. JSON written by REDox was also readable by the existing System.Text.Json DTO reader. Comparison used the fully serialized DTO trees, not just entity counts. This verifies current typed data, not preservation of unknown future JSON fields or comments.

The fixture uses the actual private `PrototypeSaveData` type, not an approximate replacement. It populates inventory/storage slots and rotations, weapon ownership/magazine/reserve, solved puzzles, collected items, opened doors, broken-object replacement paths, health/suit, camera/player pose and metadata. No real player save files were modified, and no end-to-end user save/load playthrough is claimed.

Important contract details:

- `LevelEntityDef.ParentId` is serialized as `Parent`. Preserve the existing attributes through the compatibility adapter or explicit mappings; do not assume REDox defaults are equivalent.
- `SerVec3` and `SerVec4` are custom readonly structs with constructor attributes. Their non-default values passed the contract checks.
- Registered-script type names and their JSON configuration strings remain data. Do not replace the script registry with arbitrary CLR type deserialization.
- Legacy `Boxes` conversion, entity-ID fixups and prefab-reference remapping must remain in their current layers, independent of codec choice.
- Existing JSON accepts comments; the tested REDox JSON reader rejected them. Both readers accepted trailing commas and rejected the truncated JSON fixture. REDox has a separate JSON5 path, but it was not evaluated here.
- The compatibility adapter source lists unsupported settings, including type-info resolvers and required-constructor/nullability options. Passing the current fixtures is not proof of complete System.Text.Json compatibility. See [adapter source](https://github.com/CAPCOM-TD-OSS/REDox/blob/e64ee501c18356a7812d68eab0a83b915444d2fd/src/REDox.Serialization.SystemTextJson/SystemTextJsonSerializerSettings.cs).
- Parallel deserialization was enabled as a separate test using the library's default thresholds. It did not provide a material benefit for this level. No claim is made that the current nested-list shape used all workers.

## Recommended Integration

1. Keep editor-authored levels, prefabs and script configuration as JSON. Existing source control diffs, inspector workflows and migration behavior should continue to work.
2. First evaluate the small .NET 8 improvement of reading UTF-8 bytes with the existing serializer. This avoids the large UTF-16 text allocation without introducing a runtime/library migration. It is measured here, not applied to production.
3. If larger data sets justify REDox, migrate the shared engine, game and editor projects to .NET 10 in a dedicated change. Test renderer/native UI/physics/importer boundaries, launchers, published builds and existing save files. Do not try to reference a net10 library directly from a net8 application.
4. Add a small codec boundary around typed level/save DTOs. Use `CAPCOM.REDox` plus the compatible attribute settings used in the harness, with a pinned version. Keep System.Text.Json as the existing authoring/legacy reader. A separate .NET 10 converter alone is insufficient if the .NET 8 game must deserialize DOX itself.
5. During an explicit build/export step, generate a DOX runtime copy of each validated level. Use a manifest/header containing format version, game schema version, level identity, source-content hash and codec/build version. Retain asset IDs/relative paths; do not embed live physics objects, GPU resources or duplicate GLBs/textures in the save payload.
6. At runtime, use the binary copy only when its manifest matches the source/build. Reject stale, missing or unsupported data and safely load the JSON source where available. Rebuild caches during authoring/export, not unexpectedly during timed startup. The experimental filename-only cache is not a production implementation.
7. Treat player saves separately. Introduce an explicit save-schema version and migrations before changing the wire format. Continue reading legacy JSON; test load-old/save-new/reload-new equivalence. Write to a temporary file in the same directory, flush/close as required, validate it, then atomically replace the destination while retaining a backup. Do not overwrite the only good save on failure.
8. Bound file size, nesting and collection counts; validate IDs/values after decoding; use only known DTO types. Test truncated/corrupt data, unsupported versions, missing assets, duplicate IDs, stale caches and interrupted writes. A checksum detects accidental damage, not malicious tampering.
9. Profile complete startup again before enabling the cache by default. Keep UI responsive while loading, but create GPU resources and publish the new world on their required threads. Preserve the engine's warm-before-replace rule: keep the old scene/object valid until its replacement is ready.

This is a proposal, not a completed integration. For this level, startup profiling and asset/runtime-world work are higher priorities than changing the save format. Binary serialization is more compelling if future levels or large persistent world states make parsing a substantial cost.

## Method And Reproduction

Engine source baseline: `1447a8b6138f3190fba95be5e1bc7ea5091190d7`. REDox source pinned to `e64ee501c18356a7812d68eab0a83b915444d2fd`; built from the actual repository, not a simulated serializer. License: Apache-2.0; preserve applicable third-party notices when distributing it.

Machine: AMD Ryzen 9 3900X, 24 logical processors, Windows build 26200, x64. Release builds used .NET 8.0.19 and .NET 10.0.12. A local SDK 10.0.401 was downloaded from Microsoft, SHA512-verified, and extracted into the ignored benchmark workspace; production targets and launchers were not changed.

Data measurements use actual source levels and their SHA256 hashes. Five warmups precede 25 batches of five operations; the reported median and p95 are per-operation batch means. File reads use warm OS caches. Save-write measurements use 15 buffered writes, not fsync/durable-storage latency. Allocations are process-wide GC allocations including workers, not peak memory. Fixed codec order, tiered compilation and uncontrolled background activity limit precision, especially for tiny save files.

Startup measures `sixRoomTest.json` in five fresh processes per variant, rotating variant order. No disk/driver cache flushing is performed; this is not a cold-boot disk benchmark. Clock starts before window/device creation, after process/CLR entry. First-frame and asset-ready measurements include the real game initialization and loading-overlay delay; ready requires all requested model-cache entries uploaded after a presented frame. No GPU timestamp or exhaustive visual/playthrough check is claimed. Native RmlUi presentation is disabled consistently; the existing fallback UI/render paths are unchanged. The engine maximizes its requested 1280x720 window; actual framebuffer size is recorded per run.

Only the experimental `LevelIO` and codec switch differ between startup variants. Practice-template validation still uses JSON; the selected scene uses the chosen codec. DOX cooking is excluded, as it would happen before shipping/loading. The shared benchmark adapter uses the non-generic Type-based deserialize overload; production uses the equivalent generic DTO overload. Separate outputs prevent replacing the normal game binaries. Existing source levels were hash-checked after the runs.

See [harness instructions](../Tools/REDoxBenchmark/README.md) for the exact source pin, SDK URL/hash, commands, limitations and raw result paths. No production rendering, collisions, gameplay logic, editor layouts, level files or player saves were changed by this evaluation.

# REDox Evaluation Harness

This is an isolated experiment, not an engine dependency or a replacement save system. See [the evaluation](../../Game/REDOX_EVALUATION.md) for results and the integration recommendation.

## What It Runs

- Uses the actual engine level, prefab and save DTO types, including the private gameplay save type via reflection.
- Checks all five authored levels and the existing prefab for equivalent reads and round trips. Synthetic fixtures cover parents, vectors, scripts, legacy box data, and populated gameplay-save fields.
- Measures in-memory deserialization, file read plus deserialization, serialization, and buffered file writing. Raw samples and correctness checks are in `results/micro-*.json`.
- Runs the actual `HL2GameModule` in a fresh game process for startup tests, with only `LevelIO` replaced in the experimental build. All rendering, assets, physics, UI and level construction code stays unchanged.
- Times first presented frame and first presented frame with all requested model-cache entries uploaded and the production loading delay elapsed. Reports failed models and actual framebuffer dimensions.
- Compares .NET 8 System.Text.Json, .NET 10 System.Text.Json, .NET 10 REDox JSON and .NET 10 REDox DOX. The warm data tests also include UTF-8 System.Text.Json and REDox parallel-deserialization enabled.

`Build.ps1` generates project files referencing the original sources under ignored `.work/`. It does not retarget or edit the production projects, solution or launchers. Build artifacts, downloaded sources and SDK remain under `.work/`; raw results are retained outside it. No player save files are read or written. Startup uses an explicit source level path and generated runtime UI stays in the experimental build output.

## Reproduce

Tested on Windows x64 with PowerShell 7, SDK 9.0.304 for the .NET 8 build, and a portable SDK 10.0.401 for .NET 10. The exact runtimes were 8.0.19 and 10.0.12. Existing engine/NuGet build prerequisites are required. The benchmark is not part of the solution; do not build its project directly.

From the repository workspace root, obtain the pinned source:

```powershell
New-Item -ItemType Directory -Force Tools/REDoxBenchmark/.work
git clone https://github.com/CAPCOM-TD-OSS/REDox.git Tools/REDoxBenchmark/.work/REDox
git -C Tools/REDoxBenchmark/.work/REDox checkout --detach e64ee501c18356a7812d68eab0a83b915444d2fd
```

Download the portable Windows x64 SDK ZIP from Microsoft's official endpoint and extract it into `Tools/REDoxBenchmark/.work/dotnet`:

```text
https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip
SHA512: 24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430
```

The evaluated workspace already has the source and verified SDK in those locations. No global SDK installation is needed. On another machine, verify the downloaded file with `Get-FileHash -Algorithm SHA512` before extraction.

```powershell
./Tools/REDoxBenchmark/Build.ps1 -Framework net8.0
./Tools/REDoxBenchmark/Build.ps1 -Framework net10.0
./Tools/REDoxBenchmark/Run.ps1 -StartupRepeats 5
```

The run opens and closes 20 game windows, one at a time, with variant order rotated each round. Leave them alone while measuring and close other game/editor instances beforehand. It overwrites the recorded result filenames. Microbenchmarks run first to generate `.work/cache/sixRoomTest.dox`. Cache generation is intentionally outside the startup measurement, representing a cooked/prebuilt asset.

For a single startup run after cache generation:

```powershell
dotnet Tools/REDoxBenchmark/.work/build-net8.0/bin/REDoxBenchmark/release/REDoxBenchmark.dll --boot "$PWD" "$PWD/Tools/REDoxBenchmark/results/manual-net8.json" stj
./Tools/REDoxBenchmark/.work/dotnet/dotnet.exe Tools/REDoxBenchmark/.work/build-net10.0/bin/REDoxBenchmark/release/REDoxBenchmark.dll --boot "$PWD" "$PWD/Tools/REDoxBenchmark/results/manual-dox.json" redox-dox
```

## Limits

These are scoped application measurements, not a BenchmarkDotNet certification or a manual playthrough. Data tests use five warmups, then 25 batches of five operations; reported p95 is a percentile of batch means, not individual loads. Writes use 15 single-operation samples and are buffered, not durability timings. Serializer metadata, JIT and OS caches are warm for the data tests. Fixed codec order and background activity can influence small measurements.

Startup tests use fresh processes with unflushed OS/driver caches. Their clock starts before window/device creation but after CLR/process entry. VSync and the game's loading-overlay delay are included. The engine maximizes its window, so the requested 1280x720 becomes 1920x1009 on this desktop; actual dimensions are recorded. Model readiness is checked on the update after a rendered/presented frame, not with GPU timestamp queries. FrameProfile's one post-warmup frame only terminates the process; it is not an FPS benchmark.

DOX input is prebuilt. Its cache here has no production invalidation/header validation, and the adapter uses filenames as cache keys. Do not ship that cache lookup. The codec uses System.Text.Json-compatible REDox settings, not its default naming/attribute behavior. Legacy box data round trips are checked, but no complete legacy save migration, error-recovery, unknown-field retention, concurrent edits, controller playthrough or corrupted-DOX fuzzing is claimed.

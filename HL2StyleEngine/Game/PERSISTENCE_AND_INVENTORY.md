# Persistence And Inventory

Implemented: 2026-10-05. This supersedes the adoption recommendation in the October 2 evaluation; that report remains a historical benchmark.

## Runtime And Launching

The engine, game, editor and importer now target .NET 10. Package versions are pinned in `Directory.Packages.props`: `CAPCOM.REDox` 1.0.0 and `CAPCOM.REDox.Serialization.SystemTextJson` 1.0.0-preview. The compatibility adapter is still preview, so contract regression tests are required before upgrading it. The core library is Apache-2.0 licensed. [Official RE:Dox repository](https://github.com/CAPCOM-TD-OSS/REDox).

The root launchers use `Tools/UseDotnet.bat`, preferring the project-local `.dotnet/dotnet.exe`. HS2Editor uses the same local executable when launching Play or the importer. The local SDK is available on this machine; a fresh checkout can run `Tools/SetupDotnet.ps1` to fetch the official Windows x64 SDK 10.0.401 ZIP, verify SHA512 and extract it locally. It does not install a system SDK. `global.json` requires the .NET 10 SDK family.

Visual Studio needs a compatible .NET 10 SDK and Visual Studio 2026 (18.0+) for supported .NET 10 targeting. The portable CLI does not automatically configure Visual Studio. CLI builds have been tested; Visual Studio builds have not. [Microsoft compatibility guidance](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs).

## Data Ownership

- `Engine.Core/Serialisation/StructuredData.cs` owns the shared codec and durable-file write boundary.
- Level and prefab source files remain editable JSON. Existing filenames, paths, parent IDs, transforms and script parameter contracts remain intact.
- Level loading prefers a validated binary DOX cache at `Content/Levels/.hs2cache/<level>.json.dox`. JSON is always authoritative. Source contents, DTO assembly identity and serializer version contribute to the cache fingerprint; timestamp-only checks are not used.
- The cache header has a format version plus source and payload SHA256 checksums. Missing, stale, truncated, corrupt or inaccessible caches fall back to JSON and are regenerated when writable. The first uncached load also pays the cache creation cost. These caches can be removed without losing authored data; never edit them instead of the JSON.
- Saves and prefab/level writes use RE:Dox JSON. A same-directory temporary file is flushed before replacing the destination. Existing contents are retained as `<filename>.json.bak` before replacement. Backups are not auto-loaded: inspect them before manually restoring a damaged source/save.
- Legacy JSON with UTF-8 BOM, comments or trailing commas remains readable. Comments use the System.Text.Json compatibility fallback; rewriting a typed document does not preserve comments or unknown future fields.
- Player saves use `SaveVersion = 1`, accepting older unversioned saves as version 0. Unsupported future versions and invalid inventory loads are rejected before clearing live inventory. Save filenames and slot locations are unchanged.
- Script/undo/project-settings JSON has not been indiscriminately migrated. These small internal contracts continue to use their existing serializers.

RE:Dox handles structured data here, not GLB parsing, texture upload, physics construction or world streaming. Inventory opening reads the live in-memory container, not a file on every opening. A 100-times-larger world still needs asset caching, culling and streaming; a serializer does not make rendering it 100 times faster.

## Light-State Save Extension (2026-10-06)

Version-1 saves now optionally contain `LightStates`, a list of `EntityId`/`Enabled` overrides. Older saves without it (or with null) use authored light defaults; no version bump is needed for this additive field. Overrides are separate from level/prefab `LightEnabled` and never get written back as authored defaults. The loader checks the saved level filename against the active one before restoring light states. Same-level runtime rebuilds retain them; level changes and progress reset clear them. Existing level-load behavior is unchanged: this field does not itself load another level.

Unknown entity IDs are retained in the override list for later residency work, but room streaming and campaign-wide cross-level light state are not implemented. Existing play-time persistence also restores gameplay flicker phase. `validate-lighting-state` exercises the production save reader/loader using temporary files, including a legacy save, without modifying player save slots. See [LIGHTING_GUIDE.md](LIGHTING_GUIDE.md).

## Inventory Behaviour

The inventory takes its visual direction from the supplied [JasozzGames reference video](https://x.com/JasozzGames/status/2106467936353366083/video/1): continuous fine-lined grids over the darkened world, item thumbnails, compact stack counts and selection/placement outlines. It retains this game's existing main capacity rather than copying the video's exact grid dimensions.

- Main inventory remains 8 columns by 4 rows. A separate 8-by-4 temporary overflow grid appears alongside it on wide screens and below it on smaller screens.
- Matching pickups fill existing stacks first, respecting each item's maximum. A full main inventory can receive items in overflow and opens inventory for sorting. If neither grid can fit the complete pickup, collection fails without consuming part of it.
- Drag items between grids, rotate supported footprints, merge matching stacks or use the existing item action menu to split a chosen quantity. Partial merges leave the remainder in the source stack. Multi-cell items cannot straddle the main/overflow boundary.
- Keyboard/controller navigation follows the actual visual grid arrangement. Existing Combine, Use, Examine, Split and Discard actions remain. Damaged Cable + Spare Wire still produces one Repaired Cable.
- Closing inventory returns every remaining overflow stack to the world near the player's feet, with its item ID and quantity intact. Spawning succeeds before the stack is removed. These are collectible world pickups, not a permanent second backpack. A failed spawn keeps the inventory available with an error.
- Runtime-spawned pickups are included in saves, with stable unique names, quantities and positions. A loaded save containing overflow returns it to the world because inventory starts closed. Repeated restoration replaces the previous runtime drop set rather than duplicating it.
- Storage transfers only use the permanent main grid. They do not silently deposit items into an overflow section hidden by the storage screen.
- Weapons and ammunition stay owned by the weapon system, outside the inventory. Crafting ammo still updates reserves and displays the created amount.

Native RmlUi and ImGui fallback share `Engine.UI/InventoryLayout.cs`. Native styling is scoped in `Game/Content/UI/Inventory/grid.rcss`; fallback rendering is in `InventoryPreviewRenderer.cs`. ImGui thumbnails use `ImGuiImageCache`, independent of world materials. Cable thumbnails are generated from project GLBs using the optional authoring tool. The weapon HUD, level geometry, GLB transforms and lighting shaders were not changed by this pass.

## Verification

### Native Visibility Repair (2026-10-06)

The initial refresh had two rendering defects despite passing input checks: RCSS alpha values used 0..1 instead of 0..255, and the overlay's percentage size collapsed to its content bounds. Corrected alpha values and an explicit viewport-sized block restore the shade, cell backgrounds and borders. This is not a user configuration, data-loading or missing-asset issue. Rebuild/relaunch normally; no save deletion or reimport is needed.

The new `validate-inventory-visuals` check reads the actual native render-command geometry and verifies full-screen shade coverage, all 64 cell fills/borders and item background opacity at 1300x775, 1920x1080 and 800x600. A live native window was also inspected, including item selection/description. The earlier visual inspection did not catch these defects; hit testing alone is not evidence that the UI is visible. Inventory storage and input behaviour have not been redesigned by this repair.

October 6 verification: Debug and Release solution builds passed with zero warnings/errors, all three native visibility cases passed, all 11 input checks and 321 persistence/inventory checks passed, and the six-room layout/physics/progression regression passed. No authored levels, prefabs or player saves were changed by this repair. No new full-startup benchmark was performed.

### Commands

From the project root, using the local SDK:

```powershell
.\.dotnet\dotnet.exe build HL2StyleEngine.sln
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-redox-inventory
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-inventory-input
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-inventory-visuals
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-six-room-test
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll validate-collider-bounds
```

The persistence check compares all five actual levels and current prefabs, including full DTO trees, transforms, aliases, JSON/DOX round trips, cache invalidation/corruption, backup writes and legacy saves. Container checks cover duplicate single-stack items, splits/partial merges, failed-load rollback, overflow/world restoration and responsive bounds/navigation. Input checks exercise real native document hit testing plus CPU ImGui frames, mouse drags, Combine, keyboard/controller selection and ammo crafting.

Optional `preview-inventory` opens an isolated real-game inventory fixture with existing assets, without editing authored levels or player saves. Add `--native` for RmlUi or `--small` for the compact layout. Native wide and fallback compact layouts were visually inspected. Automated desktop key injection did not reach SDL, and live drag automation did not complete a move reliably; those checks do not replace a human mouse/controller playtest. Deterministic drag/input regression tests pass for both renderers.

Data-read timings printed by `validate-redox-inventory` include file reads, decoding and cache validation, using 51 warmed samples. They exclude assets and scene construction. Debug measurements vary substantially with host load/JIT; use Release and the historical isolated benchmark for comparisons. No new end-to-end startup-speed claim is made by this implementation. See [REDOX_EVALUATION.md](REDOX_EVALUATION.md) for the earlier full-startup measurements.

Recorded Release run on 2026-10-05 for `sixRoomTest.json`: System.Text.Json UTF-8 read 10.840 ms, RE:Dox JSON 4.804 ms, validated DOX cache 3.736 ms (51-sample medians, warm caches). These are same-run local comparisons, not directly comparable to the older harness's timing methodology and build. Debug and Release solution builds completed with zero warnings/errors. The automated inventory/input suites, all six-room geometry/progression checks and 1,000 randomized collider-bound cases passed. Authored level/prefab files were unchanged.

Recommended playtest: fill the main grid, collect another item, move/split/merge it in overflow, close inventory, recollect the dropped stack, save at a typewriter and reload. Also repair the Medical cable using mouse and controller input. Existing level and player-save files have not been mass-rewritten or deleted.

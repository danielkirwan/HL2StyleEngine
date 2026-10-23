# Cooked Collision Data

Implemented 2026-10-21. Static mesh colliders now load validated LZ4-compressed triangles and BVH nodes instead of rebuilding them on every level load. C# physics, collision skin, model fitting, triangle order, ray/contact tie-breaking and gameplay remain unchanged. This is not a native solver migration.

## Automatic Behaviour

On a cache miss the game uses the original CPU model geometry, applies the existing bounds-fit/world transform, constructs the original BVH and atomically writes a disposable `.hs2collision` file under `Game/Content/.hs2cache/collision`. Later launches use it automatically. Missing, stale, corrupt, truncated or inaccessible caches fall back to the existing construction path. Cache write failures never remove a collider or fail the level load. The first uncached run includes construction and cache-write costs.

Keys include the source GLB SHA256, effective world position/size/quaternion, collision skin, cache version and importer/game assembly identities. Source files are hashed once per model per runtime-world rebuild, not once per instance. Saving/reloading edited transforms or changing source content invalidates the relevant placement. Builds can invalidate caches conservatively even when only unrelated game code changes. Source art changes still require the usual level/game reload; this is not live GLB hot reload.

Payloads carry a version, source/placement key and checksum. Reads bound lengths and validate triangle indices, finite geometry, tree topology/depth, complete leaf coverage and conservative parent/child/triangle bounds before using cached query pruning. No unsafe binary struct reinterpretation or quantization is involved.

Because the current solver stores world-space triangles, caches are placement-specific, not one universal collider per model. This preserves non-uniform scaling and rotation exactly without introducing inverse-transform contact changes. Moving mesh-door poses explicitly bypass the disk cache and use the existing construction path; swinging a door does not generate a file per frame. Primitive colliders do not need this cache. Future shared local-space collision instances are separate work.

Game build/publish copies collision caches alongside the other cooked assets. JSON and GLBs remain authoritative. Old files can accumulate after authoring changes; they are disposable, not save data. There is no automatic disk-cache pruning. Runtime visual streaming does not evict logical colliders or these live physics objects.

## Optional Pre-Cooking And Testing

No manual cook is required to play. To prepare a level after the final game build:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll cook-level-collisions Game/Content/Levels/sixRoomTest.json
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-collision-cache
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-collision-load source
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-collision-load cached
```

The level cook opens a bounded native initialization window, prepares the chosen level's mesh colliders, then exits without playing or saving progress. It is separate from Asset Importer > Compression, which cooks models/textures without knowing level placements. A subsequent packaging build copies the prepared files; a changed game/importer assembly may require cooking again. `HS2_COLLISION_CACHE=0` before launching provides the original construction reference and disables cache reads/writes.

Diagnostics: `HL2GameModule.CollisionPreparationMilliseconds`, `CollisionCacheHits` and `CollisionMeshesBuilt`; `MeshCollisionCache.Hits`, `Misses`, `Writes`, `WriteFailures` and `LastWriteError`. A missing cache is expected. The restricted test environment denied atomic file replacement, so successful cache-write tests use normal desktop permissions; unwritable fallback is also tested deliberately.

## Recorded Results

On 2026-10-21, three fresh-process Release pairs with warm filesystem caches measured six-room collision preparation at a median **31.020 ms source versus 13.939 ms cached**. All five static mesh colliders were cache hits with no rebuilds. Median host/game initialization was **1067.538 versus 1054.602 ms**. Initialization excludes fully uploaded rendering and first-playable input, so this is a modest startup improvement, not twice-as-fast whole-level loading.

A separate synthetic 20,000-triangle case measured a seven-sample median **49.690 ms BVH construction versus 3.950 ms validated cache read**, with a 400,969-byte compressed file. A final Release rerun measured **23.306 versus 5.373 ms**; these short samples vary with JIT/host conditions and are not a stable throughput estimate or whole-game multiplier. Real gains depend on collider complexity; the six-room level uses mostly primitive colliders.

Tests compare exact geometry, bounds, skin, 400 contacts/rays, transformed placements, production six-room colliders and failure handling. Standard level/physics/rendering/flashlight/streaming tests remain required when changing cache or transform rules. See [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md).

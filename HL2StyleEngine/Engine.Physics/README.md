# Physics

## Mesh Query Acceleration (2026-10-06)

`MeshCollisionMesh` builds a bounding-volume hierarchy once from its immutable triangles. Sphere, box and capsule mesh contacts, plus mesh raycasts, query nearby triangles through this tree instead of scanning every triangle. Triangle order remains unchanged in the mesh and returned candidate indices are sorted into the original order to preserve contact/ray tie-breaking. Collision skin expands contact queries conservatively. Existing solver, gravity, friction, sleeping and collider transforms are unchanged.

The BVH is currently built at load time, not serialized in a cooked collision cache. Static architecture still uses triangle meshes; dynamic objects retain the existing supported primitive shapes. This is not a replacement physics engine or a new dynamic triangle-mesh solver.

The cooked-model pipeline supplies the same CPU vertex/index arrays to shared model loading, avoiding GLB parsing on a valid cache hit. It does not serialize world-space collision transforms or the BVH. Those are still constructed by the existing collision path; imported part/triangle ordering remains unchanged.

`MeshCollisionMesh.AccelerationEnabled` permits test-only A/B comparisons. `CollectStatistics` adds query, bounds-test and candidate counts; `HS2_PROFILE_FRAMES` enables reporting for a bounded game capture. Statistics and the A/B toggle assume the current single simulation thread and are not thread-safe profiling APIs.

`Tools/LevelAuthoring` > `validate-engine-upgrades` compares 400 randomized contacts and 400 rays with acceleration on/off. A 20,000-triangle synthetic case measured 2,000 AABB queries at 732.22 ms scanning versus 4.83 ms with the BVH in the initial Debug run. Both returned eight candidates per query; bounds checks fell from 40,000,000 to 212,000. This is an isolated query result, not a whole-game speed multiplier.

Also run `validate-collider-bounds` and `validate-six-room-test` before changing bounds, transforms or contact logic. See `Game/ENGINE_TECHNOLOGY_ROADMAP.md` for the broader performance results. A mature solver evaluation remains conditional on measured needs; no C++ migration was required for this pass.

# Physics Geometry Reuse And Startup

Implemented 2026-10-28, following the save/load stability pass. C# remains the
runtime. This reduces repeated work without changing the solver or the scene.

## Playtest

Run `LaunchAtmosphereTest.bat` as before. `LaunchSixRoomShadowTest.bat` exercises
the puzzle doors; `LaunchLoadingTest.bat` runs the prepared atmosphere snapshot.
No reimport, rebake, save conversion or editor setting is needed.

Test stacked crates, gravity-gun holding/release, moving doors and walking along
rotated mesh architecture. Lighting, texture quality, collision shapes, gravity,
friction, damping, fixed timestep and loading readiness rules are unchanged.

## Implementation

- `WorldColliderCache` retains one geometry value per entity collider state and
  one per physics state. These are separate because a solver body center can
  differ from the entity transform during contact resolution.
- Exact shape, center, dimensions and original quaternion keys guard reuse.
  The key uses the input quaternion, not its normalized result. Every change
  builds through the original factory, including corrections within a substep.
- Immutable triangle meshes use object identity. A moved/rebuilt mesh is a new
  instance and cannot reuse the old geometry. Disabled colliders clear their
  entry; replacing/removing a level releases its owning entities. No global
  entity registry or unbounded geometry dictionary is added.
- Rotation integration passes its already-calculated support state to the
  torque helper, before any pose/contact mutation. Support/contact results are
  **not** cached across bodies, corrections or steps. Pair order is unchanged.
- The one-entry fields use bounded per-entity storage, not per-frame heap
  allocations. `ReuseEnabled` and optional counters are diagnostic controls,
  defaulting to reuse on/statistics off. Like the current solver they assume one
  simulation thread; they are not a parallel-physics API.

Opt-in startup reports now include `startup-physics-support`,
`startup-physics-rotation` and `startup-physics-main-thread-jit`. These are nested
within physics; never add them together as wall time. JIT measures only current
thread compilation, not background tiered compilation. Normal unprofiled play
does not allocate timing scopes.

## Behaviour Evidence

Before changing physics, fixed-1/60 replays were captured and reproduced in
fresh processes for both atmosphere and six-room shadow levels. Each runs 600
actual game physics steps, hashing player/entity positions, rotations, velocities,
held state, contacts/support manifolds and world collider bounds every frame.
Fixtures include a moving platform, stacked boxes, sphere/capsule, held/released
prop, teleport/rotation, resizing, shape replacement and solved puzzle state.
All **1,200 optimized frames match their pre-change hashes exactly**.

References under `Tools/LevelAuthoring/bin/Release/net10.0/qa/`:

- `physics-replay-20261028-111003-380/atmosphereTest.json`
- `physics-replay-20261028-111902-477/sixRoomShadowTest.json`
- Post-change reports: `physics-replay-20261028-112200-237` and `112207-056`.
- Final-build repeats: `physics-replay-20261028-114137-834` and `114144-398`,
  again all 1,200 hashes matching.

The first harness attempted two graphics hosts in one process and stalled after
the first level. Those test processes were stopped; the harness now accepts one
level per process. Original atmosphere references were retained, not replaced to
make tests pass. Replays are deterministic physics checks, not GPU/FPS tests or
random-loot/debris coverage; real-game soaks cover destruction separately.

**54,497 focused assertions** compare cached/original geometry and floor contacts
over 1,000 randomized poses and all three primitive shapes, including repeated
non-normalized quaternions, changed position/dimensions/rotation, shape changes,
same-bounds mesh replacement, clear, bypass and re-enable.

## Startup Measurement

Release .NET 10, RTX 3060 / D3D11, 1280x720 requested host. Same binary/package,
eight sequential fresh processes in alternating ABBA order; first sample of each
mode discarded. Both modes include local support reuse. Rebuild bypasses only
the geometry cache. Package preparation (3.48 seconds) is outside runtime timing.
OS files are warmed, not a cold-disk test. Full readiness and catch-up remain.

Reports: `qa/physics-startup-20261028-112230/` in the authoring output.

| Mode | First playable samples ms | Median ms | Median startup physics ms |
| --- | --- | --- | --- |
| Always rebuild geometry | 3129.5, 3189.3, 3235.3 | 3189.3 | 653.8 |
| Reuse unchanged geometry | 2844.7, 3212.8, 2900.7 | 2900.7 | 388.0 |

Median endpoint improves **288.6 ms / 9.0%**, measured physics **40.7%** in this
small sample. Retain the slower 3212.8 ms reuse trial; this is not a promised
speedup on every launch. Startup step counts differ with actual scheduling and
catch-up. Optimized samples make 351,392-372,024 geometry requests but only
1,298-1,316 builds, avoiding about **99.6%** of those constructions. Main-thread
JIT is still about 55-64 ms; no JIT elimination is claimed.

Final ordinary Game source/package smoke benchmark also reaches first playable
in all eight fresh processes. After one discarded warmup per mode, source
samples are 2895.9/2942.7/2980.9 ms (median **2942.7**); package samples are
2918.3/2897.2/2901.8 ms (median **2901.8**). Reports live under
`Game/bin/Release/net10.0/LoadingTestPackages/F2054EEB70EE2390C40E6239/qa/20261028-113930/`.
This compares two optimized loading modes, not optimized versus old physics.

The initial unoptimized diagnostic (`qa/physics-before-reuse.json`) recorded
739.6 ms physics, 273.2 ms nested support and 54.7 ms main-thread JIT. It motivates
the change but is not a controlled total-startup comparison.

## Gameplay Acceptance

Initial final-build runs each cover six cycles, 7,200 rendered measurement frames
after 120 warmup, 72 actions, real native/fallback UI, torch switching,
gravity-held props, crate debris, isolated save/load and F5 reload. The door
route additionally asserts door motion and uses static mesh architecture.
Release RTX 3060 / D3D11, actual client 1920x1009, VSync. Gameplay intervals and
explicit loading intervals remain separate; limits have not been relaxed.

| Route | Gameplay mean / P95 ms | >33.3 ms intervals | Private growth MiB | Result / report suffix |
| --- | --- | --- | --- | --- |
| Native atmosphere | 16.666 / 17.162 | 0 | +57.72 | Pass / `112348-096-native` |
| Fallback atmosphere | 16.659 / 17.117 | 1 (34.146 ms) | +4.23 | Pass / `112629-331-fallback` |
| Native six-room doors | 16.669 / 17.172 | 0 | +162.67 | **Memory guard failed** / `112916-188-native` |
| Six-room control, geometry rebuild | 16.666 / 17.223 | 1 (34.015 ms) | +26.77 | Pass / `113258-686-native` |
| Six-room optimized repeat | 16.661 / 17.199 | 0 | +76.23 | Pass / `113534-496-native` |

All directories begin `qa/gameplay-soak-20261028-`. Model-payload growth is zero
in all five. The first six-room run completes every behaviour/frame check, but its
private growth exceeds the unchanged 128 MiB guard. Its managed bytes fall from
297.5 to 239.8 MB while heap/private reservations vary; these numbers alone do
not establish the cause or prove absence of a leak. Preserve the failed report.
The fallback hitch has 4.74 ms physics, 22.44 ms submit/present and no measured GC
pause; that does not prove a GPU or driver cause. This is not hitch-free gameplay.

The rebuild control and unchanged optimized repeat both pass all original gates.
No forced GC, reduced workload, larger memory allowance or ignored loading data
was used to obtain a pass. This does **not** resolve the earlier memory outlier:
keep longer memory acceptance open before broader streaming rollout. Both modes
still own the bounded geometry fields; the control bypasses geometry reuse, not
the fields themselves. Do not treat it as a full pre-change memory baseline.

Across these five runs, 36,000 rendered measurement frames and 360 scripted
actions were completed. First real saves take 10.34-11.74 ms. Loading-frame
maxima range from 64.87 to 309.88 ms and remain visible in reports, separate from
gameplay gates. The optimized door repeat's maximum gameplay interval is
26.82 ms; it does not imply minimum 60 FPS.

Existing regression suites also pass: 1,000 collider-bound cases, 400 mesh
contacts/rays and five actual static mesh placements, BVH/linear query parity,
all five puzzle chains/open-close collision routes, 24 startup/save checks,
670 hitch assertions, 392 inventory/persistence checks and native/fallback
Combine/overflow/input. All 15 historical Game/F2/HS2Editor image references
remain within 1/255. No reference images, source levels or assets were replaced.

Release solution and Debug/Release authoring builds pass with zero warnings or
errors. Git whitespace validation passes. An earlier tool rebuild was blocked
by the two stalled replay test processes; after stopping only those identified
processes, all rebuilds completed. No runtime process or user data was replaced.

## Reproduce

Build Release first, then run checks sequentially on the normal desktop:

```powershell
.\.dotnet\dotnet.exe build Tools/LevelAuthoring/LevelAuthoring.csproj -c Release
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-physics-geometry
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-physics-replay atmosphereTest Tools/LevelAuthoring/bin/Release/net10.0/qa/physics-replay-20261028-111003-380
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll validate-physics-replay sixRoomShadowTest Tools/LevelAuthoring/bin/Release/net10.0/qa/physics-replay-20261028-111902-477
.\.dotnet\dotnet.exe Tools/LevelAuthoring/bin/Release/net10.0/LevelAuthoring.dll benchmark-physics-startup
```

`capture-physics-reference <level>` records a new reference for a deliberate
baseline change. Do not overwrite the pre-change references during validation.
`RunGameplayStabilityTests.bat` includes focused geometry/bounds checks and the
native/fallback/door six-cycle soaks; generated saves stay in isolated QA folders.
For a diagnostic control only, append `--rebuild-geometry` to `soak-gameplay`;
normal launchers retain reuse. The soak summary records this comparison mode.

## Remaining Work

Investigate the retained memory-growth outlier with longer allocation/retention
traces before accepting broader streaming rollout. Manual long playtests and
genuine post-restart samples remain valuable; the
automated route cannot prove universal collision correctness, leak freedom or a
locked minimum of 60 FPS. The next feature milestone is texture-granularity
ownership and room/projected-size mip demand, gated in a separate real-level
trial. Normal Game/F2/HS2Editor still use full-detail textures. Spatial audio
follows separately. No new physics engine or native runtime rewrite is required.

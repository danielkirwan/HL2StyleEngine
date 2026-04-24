# Work Log

Last updated: 2026-04-24

This file is the running handover for active work, recent changes, and the next tasks.

## Current Focus

Current focus is collision and prop behavior fidelity for dynamic physics objects, especially boxes, spheres, and capsules.

The immediate goal is to make runtime collision and visible object behavior line up more closely so props no longer feel like disguised AABBs or settle into obviously wrong poses.

The next active stage has started: collision response is moving from single-normal heuristics toward contact-manifold-driven support, pivot, and spin behavior.

The current gameplay issues being worked first are:

- held objects should collide while being carried
- boxes should fall and settle less like scripted orientation correction
- overhanging boxes on moving platforms should be able to fall off when their center of mass is no longer supported
- thrown boxes should stop freezing in implausible corner-balanced poses
- stacked boxes should settle with more weight and less low-speed jitter or sliding on each other

## Current Status Snapshot

- dynamic prop runtime rotation work is in progress
- box, sphere, and capsule debug collider drawing is now shape-aware
- rotation-aware collider plumbing has been introduced for dynamic boxes and capsules
- world collision now has a first contact-manifold layer carrying contact points as well as normal and penetration
- dynamic bodies now retain strongest world-contact and best support-contact manifolds from the physics step
- box toppling and world-collision spin now prefer manifold contact data before falling back to support-AABB heuristics
- `Engine.Physics` has a passing build signal
- the wider `Game` project still does not have a clean full-build signal because of unrelated workspace issues
- the roadmap now includes a proper standalone level editor for building maps, while keeping the runtime in-scene editor for live playtest adjustments

## 2026-04-17

### Summary

- tuned box-stack stability so dynamic prop contacts now merge back into each body's retained contact/support state instead of only using world support manifolds
- added low-speed resting-contact damping for support-like dynamic-dynamic contacts so stacked props bleed tiny lateral slide instead of endlessly re-exciting
- suppressed repeated collision spin for low-speed vertical box-on-box resting contacts and biased separation to move the upper box more than a strongly supported lower box
- promoted near-flat centered box-on-box support manifolds into face-support patches so clean stacked drops are less likely to degrade into corner support
- added a conservative low-speed face-support settle assist for boxes so broad stable support can flatten out the last small resting tilt instead of freezing slightly crooked
- updated held-object dynamic contacts so the free body on the other side of the collision also retains the merged dynamic contact/support manifold
- increased the dynamic-dynamic solver loop from 2 to 3 iterations per substep for a more stable pile/stack settle
- added a `ContactManifold` type so collision can carry contact points instead of only normal plus penetration
- updated `ShapeCollision` to produce manifold data for box, sphere, and capsule pairs, including projected support patches for box-box contact and support-feature contacts for capsule-box contact
- updated `StaticCollision` to preserve both the strongest contact manifold and the best upward-facing support manifold during dynamic world resolution
- updated dynamic body step state so boxes, spheres, and capsules retain last contact and last support manifolds
- updated game-side angular response so box toppling prefers the support manifold, and collision spin uses manifold contact offsets before falling back to shape-normal heuristics
- updated dynamic-dynamic prop contact to retain the contact manifold and use its contact point for collision spin instead of the default fallback offset
- updated moving-platform support matching so dynamic bodies can identify a supporting platform from the last support manifold before falling back to the old center-over-AABB test
- updated held-object fixed update so held props resolve against static/kinematic world first, then use explicit manifold-based dynamic-body contacts to push and react against free props
- removed the held-object exclusion from collision spin so held props can pick up angular response from real contacts too
- consolidated runtime prop support lookups behind a shared support-state helper that prefers support manifolds and only falls back to surface AABB scans when contact data is unavailable
- adjusted player moving-platform carry timing so direct platform support is applied before `SourcePlayerMotor.Step(...)`, while the late carry remains for non-platform support bodies
- replaced the remaining player push and pickup ray interaction fallbacks with shape-aware checks so boxes use OBB ray hits, capsules use capsule ray hits, and player-vs-prop push/stand checks use actual shape contact instead of broad AABB overlap
- kept the older support-AABB path as a fallback where manifold support is not yet available
- added a stable face-support settle path for boxes so flat multi-point floor contacts damp angular motion instead of re-triggering floor jiggle after throws
- created a shape-aware `WorldCollider` representation that includes collider rotation
- added a `ShapeCollision` layer to resolve contacts using actual shape data rather than assuming everything is an AABB
- updated dynamic box and capsule stepping to pass runtime rotation into collision resolution
- updated runtime collider generation and dynamic-vs-dynamic contact to use the shape-aware collider path
- updated debug drawing so spheres and capsules display as spheres and capsules instead of box wireframes
- removed the visual-only capsule floor offset workaround after the requirement was clarified to be true rotated colliders, not presentation compensation
- updated held-object collision to resolve against dynamic bodies as well as the world
- changed support checks to use body center-of-mass projection rather than any-overlap support
- narrowed stable-pose settling so it only helps near-stable poses instead of steering large parts of a fall
- added a small topple assist for nearly-stationary supported boxes/capsules that are far from a stable pose
- replaced capsule/righting pose steering with gravity-like topple torque from the current support/contact patch
- fixed the inferred world-collision normal used for angular response so floor impacts no longer treat the floor normal backwards
- extended moving-platform carry support from box-only to box and capsule bodies
- biased box-box contact normal selection toward the more vertical axis in near-flat floor contacts
- started shifting the prop behavior toward VPhysics/Havok-style principles by using actual collision normals for angular response instead of guessed velocity-change normals
- removed the box pose-slerp cleanup so resting orientation is driven more by collision/support and less by authored stable targets
- reduced angular damping and increased gravity-topple torque so overbalanced boxes/capsules are more likely to keep falling instead of perching
- replaced box stability/topple logic based on whole-floor support AABBs with a support-polygon check built from the box's actual lowest support corners

### Why

- stacked boxes were still missing persistent support information when that support came from another dynamic prop rather than the world
- low-speed prop stacks were still relying too much on one-frame impulses and could keep slipping or lightly chattering even after the broader contact-manifold refactor
- dropping one box onto another could still make the top box continuously try to flip while the supported lower box drifted, because the solver was still treating settling support contacts too much like fresh impacts
- even after that, a clean dropped box could still end up on a corner because a sparse 2-point vertical manifold was being read as edge support instead of a broad face contact
- even with the promoted face-support patch, a box could still visually freeze a few degrees off-flat because the stable-support path only damped rotation and did not settle out the remaining tilt
- the next physics stage needs real contact points so support, toppling, and spin can come from contact data instead of guessed pivots and broad support AABBs
- the remaining corner-rest and capsule-support issues are now more about missing contact features than about basic collider shape mismatch
- flat thrown boxes were still jiggling on the floor because stable support contacts could keep feeding tiny topple/spin corrections instead of settling
- thrown props still needed manifold-driven prop-to-prop angular transfer so object impacts could feel more weighted and less like center-to-center impulses
- moving-platform carry was still using a separate support heuristic instead of the new support-contact data
- held objects were still using a special movement path that could collide, but did not fully participate in the same contact-driven push/reaction behavior as free dynamic props
- several runtime prop decisions were still reaching directly into `supportAabb`-based helpers instead of going through one consistent support-evaluation path
- the player was still only inheriting direct moving-platform motion after the motor step, which could reintroduce visible sliding on platforms
- player push/stand detection and pickup targeting were still using broad AABB assumptions even after the collider/runtime path had become shape-aware
- sphere and capsule collisions still looked and behaved like boxes
- debug visuals were misleading because they did not match the intended collider shapes
- capsules looked like they floated or stayed overly upright
- the project needs real collision fidelity progress, not temporary visual tricks, to get closer to the intended Half-Life 2 style physical feel
- carried props were not respecting dynamic-body collision
- platform support logic was too permissive, allowing unrealistic edge support
- rotation settling was visually overpowering gravity/toppling behavior
- some boxes could freeze in corner-balanced poses instead of falling onto a face
- capsules were still visibly trying to return to an upright or authored stable pose
- flat floor impacts could still inject incorrect angular cues because the inferred collision normal in the spin path was reversed
- capsules were not inheriting moving-platform motion because the platform carry path was still box-only
- the previous collision/settling path still relied too much on steering toward preferred orientations instead of contact-driven behavior
- box balancing decisions were still being made against the entire floor/platform support AABB instead of the actual corner/edge contact patch

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\WorldCollider.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ContactManifold.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ShapeCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\StaticCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\BoxBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\CapsuleBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\SphereBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Editor\Editor\LevelEditorController.cs`

### Validation

- `Engine.Physics` build succeeded
- full `Game` build is still blocked by unrelated existing workspace issues in `Engine.Editor`, so there is not yet a clean end-to-end build signal for this work

### Remaining Risks

- the new manifold layer is still approximate for several shape pairs and is not yet a full rigid-body contact manifold solver
- capsule support now carries support-feature points against boxes, but dynamic feel still needs in-game validation for side-rest and rolling cases
- moving-platform support and held-object behavior still use older support/collision heuristics in places and are not fully unified with the new manifold path yet
- rotated capsule versus box contact is still an approximation, not a full rigid-body manifold/contact solution
- some picking, support, or ray paths may still use AABB fallbacks or broadphase approximations
- gameplay feel still needs in-game validation, especially for capsules tipping, rolling, and settling
- held objects may now block against dynamic bodies without yet transferring satisfying push forces
- the new topple assist is still an approximation and may need tuning if it feels too weak or too directed
- gravity-topple behavior is still driven from simplified support patch estimation rather than a full contact manifold
- flat-floor box behavior may still need more tuning if the linear contact solver keeps choosing a marginally tilted response in edge cases
- validation is currently blocked by a local dotnet restore/build permission error in the temporary obj path, so this pass still needs in-game confirmation
- the new box support-polygon logic still assumes a horizontal support plane and should be validated on floors and moving platforms before extending it further

### Next

- validate box-on-floor, box-on-box, and edge-rest cases with the new support manifold path
- validate side-resting capsule contacts and moving-platform carry after the capsule support-feature manifold change
- replace more `supportAabb`-based runtime decisions with contact-manifold-derived support where possible
- validate the new manifold-driven dynamic-dynamic angular transfer on thrown box and box-stack impacts
- validate moving-platform carry/support using the new support-manifold matching path
- validate held-object contact behavior against walls, corners, dynamic props, and moving platforms
- validate the shared support-state path on free props, held props, and moving-platform-supported props so the remaining fallbacks can be reduced further
- validate pickup targeting on rotated boxes and capsules now that ray hits are shape-aware instead of AABB-based
- validate player push/stand behavior against rotated or tipped props now that the player interaction path is shape-aware
- test dropped boxes, edge contacts, and floor settling behavior
- test low-side capsule impacts and side-rest behavior
- audit remaining AABB fallbacks where exact rotated shape behavior matters
- improve support and settling logic if capsules or boxes still look unstable
- scope the standalone level editor as a first-class workflow instead of relying only on the runtime scene editor
- preserve the runtime in-scene editor as a tuning and playtest tool rather than replacing it
- test carrying an object into static geometry and into another dynamic body
- test a box hanging off a moving platform with its center over and then beyond the support area
- test thrown box-on-box impacts for remaining corner-freeze cases
- test a capsule tipped partly onto its side and confirm it now continues to fall instead of righting itself
- test a capsule resting on a moving platform and confirm it is carried with the platform
- test a flat box drop and confirm it no longer bounces up into a visible angled pose on clean floor contact
- test a thrown box-on-box impact and confirm the losing box now topples off corners instead of hanging in a diamond pose
- test a capsule on its side and confirm it no longer tries to stand upright after settling
- test single-corner and two-corner box rests and confirm `stable` now flips false when the COM projection is outside the actual support polygon

## Notes For Future Chats

When continuing work:

- read this file first
- then read `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- if you make meaningful progress, update this file before ending the session

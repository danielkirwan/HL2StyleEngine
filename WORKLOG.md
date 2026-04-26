# Work Log

Last updated: 2026-04-25

This file is the running handover for active work, recent changes, and the next tasks.

## Current Focus

Current focus is collision and prop behavior fidelity for dynamic physics objects, especially boxes, spheres, and capsules.

The immediate goal is to make runtime collision and visible object behavior line up more closely so props no longer feel like disguised AABBs or settle into obviously wrong poses.

The current tuning stage is stack and settle polish on top of the newer contact-manifold-driven support, pivot, and spin behavior.

The current gameplay issues being worked first are:

- held objects should collide while being carried
- boxes should fall and settle less like scripted orientation correction
- overhanging boxes on moving platforms should be able to fall off when their center of mass is no longer supported
- thrown boxes should stop freezing in implausible corner-balanced poses
- stacked boxes should settle with more weight and less low-speed jitter or sliding on each other
- clean box-on-box drops should settle flat instead of freezing slightly angled on broad support

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
- the longer-term game target remains a grounded horror game built on top of this HL2-style movement and interaction foundation

## 2026-04-25

### Summary

- updated the handover docs to reflect the current game target, the current box-stack polish stage, and the latest stack-settling changes
- tuned dynamic box-on-box stability so dynamic contacts now merge back into retained support state instead of acting like one-frame impacts only
- added low-speed resting-contact damping and suppressed repeated collision spin for settled vertical box-on-box contacts
- biased dynamic contact separation so a strongly supported lower box gets disturbed less than the upper box resting on it
- promoted near-flat centered box-on-box support manifolds into face-support patches when the raw manifold was too sparse
- added a conservative low-speed face-support settle assist so broad stable support can flatten the last small visible tilt instead of freezing slightly crooked
- extended low-speed stack recognition from box-box only to support-like box/capsule contacts
- reduced disturbance of already-supported lower props in stacks by biasing correction and damping tiny residual lower-body motion during resting support contacts
- added a small box-stack and pile test cluster to `room01.json` with centered, offset, and loose-pile box arrangements
- fixed a pickup regression where grabbing the top box from a settled stack could inject fast spin into both the held box and the supporting box
- replaced the bad sparse-support escape assist with support classification that treats tilted one-point/two-point box contacts as unstable instead of stable rest
- tightened held-object vertical support contacts so carried boxes slide off stacks without friction-spinning the supported box underneath
- damped strongly-supported lower bodies during box-on-box support impacts so stack landings transfer less bounce and angular energy into the lower prop
- backed out a destabilizing box-box contact rejection experiment after it caused scene-wide contact jitter
- updated game-mode prop rendering to draw dynamic boxes and capsules from the same `Physics.Rotation` quaternion used by collision
- constrained the box-on-box flat-settle promotion so it only treats real broad contact patches as face support
- made one-point and two-point box support contacts unstable even when the box is near a cube-stable orientation
- clamped box-box contact manifold points to the supporting box face so dynamic box support patches cannot extend outside the lower box
- prevented edge/corner-supported boxes from being considered stack rest poses and added a small gravity-style topple nudge for sparse lowest-corner support
- added a dynamic box top-face support path so a flat, already-supported lower box can behave like a floor/platform for boxes resting above it
- fixed a startup crash in bottom-corner support detection by capping awkward tilted support patches to the four lowest candidate corners
- locked carried props to the orientation they had at pickup and stopped held-object contact spin/integration from rotating the carried object while the camera moves

### Why

- the md handover files needed to reflect the current project direction and latest physics tuning so a new chat can onboard quickly
- the top box in a clean drop onto another box was still trying to flip, drift onto corners, or freeze a few degrees off-flat
- the collision foundation is now strong enough that the highest-value work is tuning supported stacks and piles rather than adding another large physics rewrite
- mixed box/capsule piles were not getting the same low-speed support treatment as box-on-box contacts
- settled lower props in multi-body stacks could still absorb too much correction or tiny velocity from objects resting above them
- the level needed repeatable in-scene fixtures for validating clean stack settling, off-balance tipping, and small pile behavior
- the held-object dynamic contact path was still treating settled support contacts like fresh impacts, applying mass-split correction and collision spin to both bodies
- a corner contact directly under the box center could produce zero support lever, leaving the topple torque with no axis and allowing the box to perch on a point after being dropped
- the first attempt at fixing that used a pose-directed escape torque, which made pickup/drop behavior worse by reintroducing artificial spin
- carried boxes should still collide, but vertical support-like held contacts should not behave like high-friction moving shelves or impact impulses
- boxes landing on other boxes were still converting vertical support impacts into spin, especially when the lower box already had strong support from the floor or stack below
- the screenshot showed a visible gap/floating rest, which points toward box-box contact response or manifold projection over-separating before visible geometry touches
- dynamic box colliders are built from `Physics.Rotation`, so the likely fault is the approximate contact manifold/response rather than rotation not being passed into colliders
- rejecting suspected bad box-box contacts made contacts flicker frame-to-frame, which caused whole-scene shaking
- visible props were still being drawn by round-tripping physics rotation through `Transform.RotationEulerDeg`, so boxes could appear to snap or rest on a corner even when collision was using a different quaternion
- thrown and pickup-dropped boxes could still land on another box with only a corner or edge touching, then get promoted into fake full-face support and settle there
- sparse support contacts under a box center are physically unstable, so they should keep falling instead of being accepted as rest just because the box is near a valid face orientation
- floor and moving-platform support already behave better because they are static/world surfaces with bounded support; dynamic box-box contacts were projecting support points onto a plane without clipping them back to the lower box face
- a perfectly centered edge/corner contact can become an unstable mathematical equilibrium in the simplified solver, so it needs an explicit tiny topple bias instead of being damped as settled stack contact
- tilted boxes on top of other boxes were bouncing between stack-settle and sparse-topple rules; using the lower box's stable top face as a floor-like support surface makes box-on-box rest follow the same support path as floor/platform rest
- some tilted box poses can put more than four OBB corners within the support tolerance, but the helper only had a four-corner output buffer
- pickup only cleared angular velocity once, so the held-object update could still reintroduce rotation through world collision spin, dynamic-body contact spin, or normal angular integration

### Files

- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ShapeCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\bin\Debug\net8.0\Content\Levels\room01.json`

### Validation

- `Engine.Physics` build succeeded
- `room01.json` parsed successfully with 29 entities
- full `Game` build succeeded
- `git diff --check` passed, with only the existing line-ending warning for `HL2GameModule.cs`
- `Game` compile check succeeded with `--no-dependencies` into a temporary output folder while the running game kept the normal output DLLs locked
- `Engine.Physics` build succeeded
- `Game` compile check succeeded after the sparse support/topple change
- `Game` compile check succeeded after adding dynamic box top-face support
- `Game` compile check succeeded after fixing the bottom-corner overflow
- `Game` compile check succeeded after locking held-object orientation during pickup carry
- normal full `Game` build was blocked in this pass by the running `Game` process holding output DLLs open
- in-game validation improved the clean box-on-box resting case, but pile stability and mixed-stack behavior still need more testing

### Next

- re-test picking up a tilted/stacked box and turning the camera left/right to confirm the held object keeps its pickup orientation
- re-test corner-rest cases with the render path now using the same quaternion as collision
- re-test throwing and pickup-dropping boxes onto other boxes to confirm sparse corner/edge contacts topple instead of auto-correcting into corner rest
- re-test tilted drops onto a flat lower box and confirm the upper box settles like it does on the floor/moving platform instead of looping around corners
- tune multi-box pile stability and mixed box/capsule stacks
- reduce disturbance of already-supported lower props in piles and stacks
- re-test picking up the top box from the centered stack and confirm the lower boxes stay quiet
- re-test dropping carried boxes from awkward rotations and confirm tilted sparse support is unstable without causing new spin
- validate that clean box-on-box drops settle flat while genuine off-balance drops can still tip naturally
- validate mixed box/capsule piles in-game, especially low-speed capsule contacts so rolling is not over-damped
- continue keeping the handover docs current as the physics tuning moves into pile/polish work

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

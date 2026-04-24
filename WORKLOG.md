# Work Log

Last updated: 2026-04-24

This file is the running handover for active work, recent changes, and the next tasks.

## Current Focus

Current focus is collision and prop behavior fidelity for dynamic physics objects, especially boxes, spheres, and capsules.

The immediate goal is to make runtime collision and visible object behavior line up more closely so props no longer feel like disguised AABBs or settle into obviously wrong poses.

The current gameplay issues being worked first are:

- held objects should collide while being carried
- boxes should fall and settle less like scripted orientation correction
- overhanging boxes on moving platforms should be able to fall off when their center of mass is no longer supported
- thrown boxes should stop freezing in implausible corner-balanced poses

## Current Status Snapshot

- dynamic prop runtime rotation work is in progress
- box, sphere, and capsule debug collider drawing is now shape-aware
- rotation-aware collider plumbing has been introduced for dynamic boxes and capsules
- `Engine.Physics` has a passing build signal
- the wider `Game` project still does not have a clean full-build signal because of unrelated workspace issues
- the roadmap now includes a proper standalone level editor for building maps, while keeping the runtime in-scene editor for live playtest adjustments

## 2026-04-17

### Summary

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
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\ShapeCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Collision\StaticCollision.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\BoxBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics\Dynamics\CapsuleBody.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game\HL2GameModule.cs`
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Editor\Editor\LevelEditorController.cs`

### Validation

- `Engine.Physics` build succeeded
- full `Game` build is still blocked by unrelated existing workspace issues, so there is not yet a clean end-to-end build signal for this work

### Remaining Risks

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

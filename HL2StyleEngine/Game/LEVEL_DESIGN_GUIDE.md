# Level Design Guide

This project is aiming for slower to medium paced horror/action exploration: closer to Half-Life 2 pacing and immersive environmental storytelling than arena shooter speed.

## Core Loop

A useful room-to-room loop:

1. Enter a space and read it visually.
2. Notice a goal, obstruction, locked route, sound, silhouette, or pickup.
3. Solve a small spatial, inventory, physics, or combat problem.
4. Gain a reward: progress, resources, story clue, shortcut, weapon ammo, health, or a new view into the next area.
5. Change the pressure level before repeating.

The player should often feel they are moving through a real place first, and a combat route second.

## Pacing Targets

For slower/medium horror action:

- Use 2 to 5 minutes of exploration, puzzle work, or tension between normal combat encounters.
- Use short threat beats between fights: distant sound, movement behind glass, flickering light, corpse placement, locked door rattle, or an object knocked over.
- Avoid long empty stretches unless they build a clear question in the player's mind.
- After a high-pressure fight, give the player 30 to 90 seconds to recover, loot, read the environment, and reorient.
- Do not make every new room a fight. It teaches the player to stop looking at the world.

A good rhythm is:

- Safe entry or observation space.
- Light puzzle or traversal problem.
- Small enemy pressure or scare.
- Reward room or shortcut.
- Bigger encounter or set piece.
- Quiet aftermath.

## Combat Encounter Types

Use encounters with clear intent instead of only placing enemies in rooms.

- Ambush: enemy starts hidden, but there should be a fair tell before impact.
- Holdout: player must survive while a door opens, lift moves, fuse powers up, or machinery starts.
- Funnel: enemies approach through one or two obvious routes, useful for teaching a weapon.
- Crossfire: enemies attack from two levels or angles, useful after the player understands the arena.
- Cleanup: one or two weaker enemies after a puzzle, mainly to make the player use the space.
- Environmental fight: explosive props, physics objects, movable cover, or traps are the interesting part.

For this engine, physics objects should matter. Let players throw crates, block doors, smash weakened objects, or use the gravity gun as a tool before it is only a weapon.

## Enemy Spacing

For early levels:

- First enemy after a new area: delay it long enough that the player studies the space.
- Standard combat gap: about 2 to 4 minutes.
- High tension but no combat: up to 5 minutes if sound, lighting, locked doors, or visible danger keeps pressure up.
- Back-to-back fights: use sparingly, usually after giving ammo or health.
- Boss or heavy enemy setup: foreshadow for at least one room before the fight.

Keep enemy count lower when the space is dark, narrow, or puzzle-heavy. The slower the player movement, the more each enemy matters.

## Puzzle Design

Useful puzzle types for this project:

- Key item gate: find a key, fuse, valve, emblem, code, battery, or tool.
- Physics gate: move, stack, break, throw, or hold objects to reach or open something.
- Power routing: switch lights, restore power, unlock a door, start an elevator.
- Observation puzzle: read environmental clues, numbers, arrows, blood trail, labels, or machinery state.
- Inventory combination: combine items to create ammo, repair parts, or unlock a route.
- Risk-reward puzzle: optional locked chest, health cache, or weapon upgrade behind extra danger.

Rules:

- The goal should be visible before the solution is complete.
- The player should understand what changed after each step.
- Use one new idea at a time, then combine ideas later.
- Avoid puzzles that only test whether the player found a tiny object.
- If an item is required, give it a memorable silhouette, color, light, or location.

## Horror Tone

Horror comes from uncertainty, contrast, and vulnerability, not constant darkness.

Useful tools:

- Let the player see partial information: shadows, blocked sightlines, frosted glass, mesh fences, windows, door gaps.
- Use sound before sight: hum, distant impact, dragging, breathing, radio static, broken machinery.
- Use light as information: safe pools of light, flickering hazards, emergency red, cold blue utility light, warm false safety.
- Keep some spaces quiet enough that a small sound matters.
- Place enemies where the architecture makes sense: vents, service corridors, flooded rooms, storage, broken doors.

Avoid making every texture dark. The player needs readable shapes and navigation landmarks.

## Bioshock / Half-Life 2 Influences To Use

Useful Bioshock-style ideas:

- Strong room identity: medical wing, flooded service hall, maintenance, theatre, lab, archive.
- Environmental storytelling through props, signage, locked rooms, and before/after damage.
- Optional side rooms with resources and story details.
- Audio/visual contrast: elegant design damaged by violence or decay.

Useful Half-Life 2-style ideas:

- Teach mechanics through the level instead of popups.
- Use physics as both puzzle and combat language.
- Make routes feel grounded: blocked roads, maintenance doors, lifts, vents, rooftops, basements.
- Break combat with traversal, NPC beats later, environmental hazards, and object play.
- Let the player preview danger from a safe angle before entering it.

## Resource Placement

Resources should create decisions.

- Put health after danger, or before a clearly signposted bigger danger.
- Put ammo near places where the matching weapon is useful.
- Use low ammo to make melee, gravity gun, and physics objects more attractive.
- Reward exploration with supplies, not mandatory progress every time.
- If the player is below 25 health, consider extra health opportunities, but avoid making the system obvious.

## Door And Key Structure

Good door usage:

- Locked door visible early, key found through a different route.
- Doorframe plus door prefab should support lock scripts, key requirements, open angle/direction, sounds, and light links later.
- A locked door should have a clear prompt or readable visual language.
- Shortcuts should often open from the far side to make the level fold back on itself.

## Building With Prefabs

Good early prefab candidates:

- Doorframe plus working door.
- Locked door variants by key type.
- Breakable crate with loot chance.
- Light fixture plus point light.
- Switch plus target light group.
- Pickup table or supply shelf.
- Small combat cover cluster.

Keep prefab roots clean. Put the root at the meaningful pivot or placement point, then make doors, lights, triggers, and detail meshes children.

## First Test Level Goals

A strong first showcase level could include:

1. Blockout corridor and finished model corridor comparison.
2. One locked door and one key item.
3. One doorframe plus door prefab.
4. One breakable crate with health or suit battery chance.
5. One physics puzzle using the gravity gun.
6. One light switch that changes the mood of a room.
7. One quiet scare before the first enemy.
8. One short fight, then a reward room.

The goal is not scale yet. The goal is proving the engine can support a repeatable design language.

## Current Basement Slice

`Content/Levels/basementLevel.json` is the current exploration-only basement test slice. It should be treated as a practical test bed for modular basement corridor pieces, explicit hinge doors, rolling doors, wire/cable inventory puzzles, gravity-gun retrieval, and crate/object play.

The intended first-pass flow is: use the gravity gun to pull a high key down, unlock the basement door, find three wire pickups, fit them into the electrical panel sockets, pull the now-powered lever, and watch the rolling door lift open. No enemies are placed yet; crates are included so the player can still test physics, crowbar hits, pistol shots, gravity-gun blasts, and throws.

Current basement tuning notes:

- Main corridor and room width is now 6m so the player, weapon viewmodel, gravity-gun props, and crates have more breathing room.
- The first door and rolling-door openings are built from separate left/right/header collision pieces. Avoid using one full wall or full frame mesh as the blocker unless its mesh collider has a true walkable opening.
- The rolling door no longer has a back wall immediately behind it. It opens into an extra final bay with visible light panels and a `PointLight` data entity for the future lighting pass.
- Imported basement architecture should use a neutral white material tint. Dark mood should come from real lights, ambient/fill settings, fog, post effects, and set dressing, not from permanently darkening the mesh `Color` field.
- For the prototype slice, floors/walls/ceilings render GLB meshes but use box colliders for stable movement. Split doorway blockers use `Color.W = 0` so they remain collision-only and do not cover the visible model. Dynamic pickups and crates use high friction, zero restitution, and spawn just above the floor to reduce idle bouncing.
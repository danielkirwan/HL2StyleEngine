# Project Overview

Last updated: 2026-04-25

## Purpose

This project is a custom C# game engine and game prototype built to capture the feel of a Half-Life 2 style experience.

The goal is not to copy Source exactly. The goal is to build a modern, understandable codebase that can deliver:

- grounded first-person movement
- believable physics interaction and prop behavior
- readable, purposeful level spaces
- an art direction that leans toward industrial, physical, Source-era realism
- clear separation between reusable engine systems and game-specific behavior

## Game Target

The current game target for this engine is a grounded horror game built on top of the Half-Life 2 inspired interaction and movement foundation.

The intended direction combines:

- Half-Life 2 style physical interaction and object handling
- horror lighting and atmosphere
- Resident Evil inspired structure for maps, inventory, and save-point style progression

The recommended scope is still to reach that through a vertical slice first, not by building every survival-horror system at once.

## What The Project Is Trying To Achieve

The north star is a playable game and engine foundation where movement, collision, props, and world interaction feel physical and authored rather than floaty or arcade-like.

The main target qualities are:

- strong movement feel inspired by Half-Life 2 and Source-era FPS games
- physics props that look convincing when pushed, thrown, dropped, or stacked
- a fast iteration workflow with clear engine/game separation
- tooling that can grow into a practical in-engine editor and content workflow
- visuals and world building that support the same grounded tone as the gameplay

## Editor Direction

The project now has two editor goals, not one:

- a proper standalone level editor that is practical for building levels from scratch
- an in-scene runtime editor that stays available for playtest-time adjustment and rapid feel tuning

The standalone editor should become the main environment for layout, object placement, and level construction. The in-scene editor should remain as a fast iteration tool for checking scale, gameplay flow, physics feel, and small live adjustments while the game is running.

## Current Stage

The project is in a foundation-plus-prototyping stage.

Core engine structure is in place, the runtime and editor exist, and the project is now spending more time on feel-critical systems such as movement, collision, dynamic props, and debug tooling. The current work is moving from broad placeholder behavior toward more faithful physical behavior.

## Solution Layout

- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Core`
  Shared math, timing, and low-level utilities.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Platform`
  Windowing and platform-facing services.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Input`
  Input systems and input-related abstractions.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Render`
  Rendering systems and graphics integration.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Runtime`
  Runtime host, main loop, and module lifecycle.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Physics`
  Collision, stepping, and simple physics bodies.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Engine.Editor`
  Editor systems and debug/editor-facing tooling.
- `C:\HS2StyleEngine\HL2StyleEngine\HL2StyleEngine\Game`
  Game-specific logic, gameplay state, and current prototype behavior.

## Where The Project Is Right Now

Implemented or substantially present:

- custom engine structure in C# on .NET 8
- separate engine and game projects
- runtime loop and editor/runtime split
- rendering and debug UI foundations
- first-person movement foundations
- fixed-step simulation
- basic world collision and simple dynamic body stepping
- dynamic prop runtime state for visual rotation and angular settling work
- debug collider display for box, sphere, and capsule shapes

Current active gameplay/physics focus:

- make dynamic prop behavior feel more physically plausible
- stop sphere and capsule collision from behaving like box placeholders
- support rotated colliders for dynamic boxes and capsules
- improve how props settle after impacts, especially boxes and capsules
- improve stacked-box and pile behavior so supported props settle flat instead of perching or jittering

## Active Workstream

The main active workstream is runtime prop rotation and collision fidelity.

Recent work in progress has moved collision handling away from a pure AABB mindset and toward shape-aware colliders:

- `WorldCollider` now carries shape data and rotation
- dynamic box and capsule stepping passes rotation into collision resolution
- a new `ShapeCollision` layer handles rotated shape tests
- the collision path has started retaining contact manifolds so support, spin, and toppling can use contact points instead of only a single normal
- runtime and editor debug drawing now show sphere and capsule colliders properly
- dynamic contact and runtime world collider construction use shape-aware colliders

This work is aimed at fixing several visible problems:

- capsules and spheres behaving like hidden boxes
- props settling into implausible orientations
- boxes popping into awkward floor rotations
- capsules not tipping or resting believably
- stacked boxes resting at angles, on corners, or with too much low-speed jitter

## Known Current Limitations

- collision for rotated capsules versus boxes is orientation-aware, but still approximate rather than a full rigid-body contact manifold solution
- some support, picking, and ray logic may still rely on AABB-style fallbacks or broadphase approximations
- there are existing unrelated build blockers in the wider workspace, so a clean full `Game` build is not currently the validation signal
- parts of the current prop rotation system are still visual/runtime approximations layered over simplified collision

## Immediate Next Steps

- validate multi-box pile stability and mixed box/capsule stack behavior
- keep tuning support retention and low-speed settle behavior for stacked props
- validate rotated box, sphere, and capsule collision behavior in gameplay scenarios
- extend the new contact-manifold path so support and angular response are driven more consistently by contact points
- remove remaining AABB fallback logic where exact rotated shape behavior matters
- improve support and settling logic for tipped capsules and resting boxes
- define and build a real standalone level editor workflow for creating levels more efficiently
- keep and improve the current in-scene play editor so runtime tweaking remains part of the workflow
- get a cleaner full-project build signal once unrelated workspace issues are addressed
- continue moving from placeholder physics behavior toward feel-driven gameplay behavior
- shape the first horror vertical slice once the interaction and physics foundation is stable enough to support it

## Reference Handover

Future chats should treat these files as the handover set:

- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md`
- `C:\HS2StyleEngine\HL2StyleEngine\PROJECT_GUIDELINES.md`
- `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`

When meaningful work is completed, update `WORKLOG.md` first, then refresh `PROJECT.md` if the project direction or current stage has changed.

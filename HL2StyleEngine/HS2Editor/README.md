# HS2Editor

Standalone first-pass project and level editor for HL2StyleEngine.

## Run

Use `LaunchEditor.bat` from the repository root, or run:

```powershell
dotnet run --project HS2Editor\HS2Editor.csproj
```

The editor loads `HS2Project.json`, creates missing content folders under `Game/Content`, and opens the configured startup level.

## Current Features

- Level create, load, save, duplicate, and rename.
- Scene panel with grid, selection, transform gizmo drawing, editor camera controls, and textured GLB scene rendering for placed models.
- Unity-style default window placement: Scene center, hierarchy/levels/project left, inspector/toolbar right, and content/prefab/UI/status panels around it. Panels remain dockable and manually rearrangeable through ImGui docking. Layout-version changes, missing saved layouts, and saved layouts with collapsed/tiny key panels now clear stale `imgui.ini` state and hold the default placement for several seconds so old collapsed/off-screen windows cannot leave the editor black. The scene render fallback also treats very small saved scene panels as invalid and renders full-window until the layout recovers.
- Content browser for models, animations, prefabs, and project files. The Models tab uses a table layout with an explicit Asset column and Assign column, plus a selected-model 3D shaded/wireframe preview pane for `.glb` model assets when the panel is wide enough. The Prefabs tab lists JSON prefabs from `Game/Content/Prefabs` with Place/Edit actions.
- Assign a `.glb` model from `Content/Models` to the selected entity, or drag a model from the content browser into inspector lists that accept model assets. Assigning/dropping a GLB onto a static rigid body now defaults it to `Shape = "Mesh"` so imported architecture can use triangle mesh collision. Use visual-only `Prop` entities for decoration and `RigidBody` entities for anything that should collide.
- Inspector interaction controls can add/edit locked doors, locked chests, puzzle slots, puzzle levers, and puzzle doors directly on the selected level entity. Locked doors expose hinge offset/open-angle tuning, and puzzle levers expose required solved-state ids. The data is saved inside the level JSON, not in a separate attached document.
- Save selected entity as a prefab JSON and place prefabs back into the level.
- Basic `.rml` and `.rcss` UI file creation/editing/saving with source preview.
- Launch the asset importer.
- Launch the game from the selected level via `--level`.

## Current Limitations

- UI preview is source/text based until native RmlUi visual preview is wired into the editor.
- Asset references are path-based; GUID/meta files are planned for a later asset database pass.
- Play mode launches a separate game process instead of embedded play-in-editor.
- Content Browser previews are still editor-side shaded/wireframe previews rather than full textured offscreen render targets, but the Scene view now loads GLB textures for placed models. Static GLB mesh colliders are supported, but dynamic mesh physics is not; moving objects should still use box/sphere/capsule colliders. Programmatic dock splitting is limited by the current ImGui.NET wrapper, so the first reset uses default window placement rather than generated dock nodes. If the saved ImGui layout records tiny/off-screen panels, startup now treats that file as broken and rebuilds the layout. Rich object palettes, terrain, navmesh, material editing, animation timeline editing, and C# script creation are later milestones.

## Prefab Editing Update

The standalone editor now has a dedicated Prefabs workflow. Prefabs live under `Game/Content/Prefabs` and can contain a full entity hierarchy rather than only one selected entity.

Use the Hierarchy panel to parent objects first, then select the intended root and use Prefabs > Create From Selection. For a door setup, the frame should be the root and the actual moving door should be a child. Place prefabs from the Prefabs panel or Content Browser > Prefabs; placement remaps ids, keeps the door attached to the frame, and gives interaction state ids a unique instance suffix.

The Prefabs panel and Content Browser Prefabs tab support placing, editing, applying, reverting, unpacking, and creating simple variants. Prefab Edit mode loads the prefab contents into an isolated workspace; use Save Prefab and Return To Level when finished.

## Door Prefab Notes

`DoorFrame&Door.json` is a prefab asset under `Game/Content/Prefabs`. It is placed through the Prefabs panel or the Content Browser `Prefabs` tab by clicking `Place`; it is not assigned like a model GLB.

The frame/root object should not have a locked-door interaction. The locked-door interaction belongs on the actual door child, such as `RustedKeyDoorRoom1`, so the frame remains solid architecture and only the door opens. If a full frame mesh is only there for looks, make it a visual-only `Prop` and use separate collider pieces or a static mesh-collider `RigidBody` for the parts that should block the player.

## Interaction Inspector

The Interaction section in the Inspector writes a `LevelInteractionDef` onto the selected entity in the current level file. Use this for locked doors/chests and puzzle slots instead of attaching a separate JSON document.

For `RustedKeyDoorRoom1`, select the actual door or blocker entity, click `Add Locked Door`, set `Required Item` to `RustedKey`, and give it a unique `State Id` such as `Door_Room1_RustedKey`.

Key pickups should grant the same item id. The current convention is a pickup name like `ItemKey_RustedKey`, which gives the player `RustedKey`. The runtime then compares the player item id against the locked entity's `RequiredItem` value.

Use `Targets` for puzzle slots and puzzle levers that open or change other named entities. Use `Required States` on puzzle levers when several slots must be solved before the lever can activate. Locked doors currently operate on the locked entity itself, so they do not need a target list.

## Interaction Inspector Fixes

The interaction inspector now uses vertical, full-width fields so labels remain visible in narrow inspector layouts. Locked doors and locked chests show a target summary instead of the full target editor because they act on the selected object itself. Puzzle slots show the editable target list, including add, remove, and clear-all controls.

Interaction changes mark the active level or prefab document dirty. The inspector shows a `Save Active Document` button while dirty, and the normal editor save flow still works: toolbar Save, File > Save Level, or Play Selected Level before launch.

## Save And Play Sync

The source level JSON under `Game/Content/Levels` is the canonical editable level file. Toolbar `Save`, File > `Save Level`, and `Play Selected Level` save the active level document before launch. The Project panel `Save Project` button now also saves the active level or prefab first, then writes `HS2Project.json`, so saving project settings does not leave scene edits unsaved.

When a source level is saved, HS2Editor also mirrors that JSON into any existing runtime output copies under `Game/bin/.../Content/Levels` and `HS2Editor/bin/.../Content/Levels`. This keeps direct game launches from reading stale copied content between builds. Editor-launched play still passes the exact source level path through `--level`.

## Scene Mesh Selection Visibility

Scene meshes are now easier to edit directly. GLB objects render as their mesh shape, selected meshes receive a yellow editor highlight, and child meshes under the selected root receive a softer blue highlight. Selection uses exact oriented-box picking for the editor draw volume, and transparent collision helpers are ignored unless `Show Colliders (OBB)` is enabled. This is intended for prefab-style assemblies such as `PracticeDoorFrameMesh` with a child door.

Collider/blockout boxes default to hidden for GLB scene editing and can be enabled from the toolbar with `Show Colliders (OBB)` when collision tuning or selection of invisible helpers is needed. Rigid-body fallback boxes now respect the entity `Color` value instead of forcing magenta, so primitive helper colliders can use `Color.W = 0` to stay collision-only. Do not rely on alpha to hide GLB renderers; collision-only helpers should have no `MeshPath`. For static GLB architecture that is rotated or heavily scaled to make the texture face correctly, use `Shape = "Mesh"` so the collider follows the model triangles instead of the fitted box. If scene meshes are hidden from the View menu, the Scene panel shows a warning.





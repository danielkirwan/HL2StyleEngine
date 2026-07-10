# HS2Editor

HS2Editor is the standalone project and level editor for the engine. It is separate from the runtime game and separate from the asset importer.

## Goal

HS2Editor should become the Unity-style project and level authoring app for the engine. It should let the project owner manage content folders, create and edit levels, place objects, attach scripts/components, assign imported models, manage prefabs, inspect UI files, and launch the game from the selected scene.

The first implementation reuses the existing engine renderer and ImGui editor tooling so the app can have a real 3D viewport, hierarchy, inspector, project browser, and content browser without duplicating runtime systems.

## Confirmed Direction

- App name: `HS2Editor`.
- App type: standalone executable, like `Engine.AssetImporter`.
- Rendering/UI tech: existing engine renderer plus ImGui.
- Play mode: first pass launches the existing game executable with the selected level path, rather than embedding full runtime play inside the editor viewport.
- Import integration: first pass launches the existing converter app from the editor.
- Launchers: root-level batch launchers exist for editor, asset importer, and game.
- Project file: `HS2Project.json` stores project folders, startup level, recent levels, importer settings, Blender path, and editor preferences.
- First pass asset references: path-based references are in use.
- Longer-term asset identity: add GUID/meta files once the content browser, prefab system, and asset moves/renames need stable references.

## Implemented First Pass

The first editor slice now lives in `HS2Editor` and has been added to `HL2StyleEngine.sln`.

Implemented:

- Standalone `HS2Editor` executable using `EngineHost`.
- Root launchers: `LaunchEditor.bat`, `LaunchAssetImporter.bat`, and `LaunchGame.bat`.
- Root project file: `HS2Project.json`.
- Project folder creation for `Game/Content/Levels`, `Models`, `Materials`, `Animations`, `Scripts`, `UI`, and `Prefabs`.
- Source-side starter levels under `Game/Content/Levels`: `interaction_test.json`, `interaction_test_blockout.json`, and `room01.json`.
- Level manager for creating, loading, saving, duplicating, and renaming level JSON files.
- Dock layout version `8` forces stale saved ImGui window layouts to reset.
- Dockspace-based editor workspace with a Unity-style default placement: Scene center, hierarchy/levels/project on the left, inspector/toolbar on the right, and content/prefab/UI/status panels around the scene. The current ImGui.NET wrapper does not expose dock-builder calls, so the editor seeds default window placement and still leaves panels dockable/manually rearrangeable. Layout-version changes, missing saved layouts, and saved layouts with collapsed/tiny key panels clear stale `imgui.ini` state and hold default placement long enough to recover from old collapsed/off-screen windows.
- Scene camera controls: RMB look plus WASD/QE fly movement while the Scene panel is hovered or focused.
- Scene selection and dragging through the existing `LevelEditorController` picking path. Picking rays now use the Scene panel rectangle instead of the whole application window, and the Scene panel is not treated as regular UI for mouse blocking. If the Scene panel bounds are missing or saved below a usable size during startup, world rendering falls back to the full app window rather than leaving a tiny black viewport.
- Existing hierarchy, inspector, and toolbar panels reused inside the standalone app.
- Inspector interaction authoring for locked doors, locked chests, puzzle slots, and puzzle doors. Interaction JSON is stored directly on the selected level entity, not as a separate attached document.
- Content browser for models, animations, and all content files. The Models tab uses a table layout with a visible asset count/path header, explicit Asset and Assign columns, and a selected-model material-colour shaded 3D preview pane for `.glb` assets when there is enough room.
- Assign selected model from `Content/Models` to the selected entity by writing a `Content/...` mesh path.
- Drag a `.glb` model from the Content Browser onto the Scene panel to create a selected static `RigidBody` at the drop point. The new entity uses the existing selection, drag, and gizmo movement path, defaults to a one-unit mesh collider/model fit for static GLB rigid bodies, and can be resized in the inspector. Scene placement avoids the Scene viewport ImGui payload target and instead places the tracked dragged asset on mouse release over the Scene. Assigned/dropped GLBs now render in the Scene view using textured engine render models. The same bounds-fit transform as the game renderer is used, and the collider/blockout box can be shown as an overlay so mesh orientation, texture direction, and collision volume can be tuned together.
- Drag/drop GLB asset payloads from the content browser into inspector model fields and model lists. Selected boxes, props, and rigid bodies accept dropped `.glb` paths into `MeshPath`, so blockout walls, floors, ceilings, and props can be assigned imported models without typing paths. Damageable object replacement and debris model lists still accept dragged `.glb` paths for workflows such as dragging `DamagedCrate*.glb` onto a crate replacement list.
- Prefab creation from the selected entity and prefab placement from JSON files under `Content/Prefabs`. Prefabs are now visible in the Content Browser `Prefabs` tab as well as the compact Prefabs panel.
- Basic UI manager for creating, opening, editing, and saving `.rml` and `.rcss` files.
- Asset importer launch from the editor.
- Play selected level by saving the current level, mirroring existing runtime output copies, and launching `Game` with `--level <current source level path>`.
- Game startup now accepts `--level` so editor-launched play can load the selected scene.



## Save And Runtime Level Sync

HS2Editor treats `Game/Content/Levels/*.json` as the source of truth for authored levels. Save actions now behave as follows:

- Toolbar `Save`, File > `Save Level`, and `Play Selected Level` save the active level document.
- Project panel `Save Project` saves the active level or prefab document first, then writes `HS2Project.json`.
- Level saves mirror the JSON into any existing `Game/bin/.../Content/Levels` and `HS2Editor/bin/.../Content/Levels` copies so direct launches do not keep stale level data.
- Editor play still launches the game with `--level` and the absolute source-level path.

The game now also reads `HS2Project.json` when launched without `--level`, using `StartupLevel` before falling back to the old `interaction_test.json` practice level.
## 2026-07-09 Mesh Collider Update

Locked-in editor/runtime collision direction from this pass:

- Static imported `.glb` rigid bodies can now use `Shape = "Mesh"` to collide against their actual triangle mesh instead of only a primitive box, sphere, or capsule.
- Dragging a `.glb` from the Content Browser into the Scene now creates a selected static `RigidBody` with `Shape = "Mesh"` by default. It can still be switched back to `Box`, `Sphere`, or `Capsule` in the inspector.
- Assigning a `.glb` to a selected static rigid body through the Assign button also switches it to `Shape = "Mesh"` and zero mass, which is the expected setup for walls, floors, ceilings, doorframes, and other level architecture.
- Mesh colliders use the same bounds-fit transform as runtime GLB rendering, so the visible model orientation/scale and collision triangles are generated from the same data. Runtime mesh colliders also use a small collision skin so thin architectural triangles, such as doorframe edges and wall surfaces, are not easy to step through at player movement speed.
- Mesh colliders are intentionally static-only in this first pass. If a mesh-shaped rigid body is made dynamic or kinematic, runtime falls back to box physics behavior. Moving doors, pickups, crates, and other interactive physics objects should remain separate primitive collider bodies for now.
- Doorframes should be authored as static mesh-collider rigid bodies so the player can walk through the real opening. The actual moving door should be a separate entity with its own primitive collider and movement/interaction script.
- Editor Scene drawing now uses the engine GLB renderer with textures for placed models; the collider overlay can remain visible for selection and size tuning, and runtime collision follows GLB triangles when the object shape is `Mesh`.

## 2026-07-08 Editor Updates

Locked-in editor updates from this pass:

- Content Browser model assignment works from the explicit Assign button without the asset row swallowing the click.
- Dragging a `.glb` from the Content Browser into the Scene uses tracked drag state and mouse release over the Scene view instead of a Scene ImGui drop target, reducing crash risk.
- Dropped GLB models create selected static `RigidBody` entities with `Shape = "Mesh"` so they can immediately be moved, resized, and used as mesh collision in play.
- Assigned/dropped GLB models now render in the editor Scene view using textured engine render models. This shows the real mesh shape, orientation, and material/texture direction while editing.
- The collider/blockout shape remains available as a lightweight overlay, so visible mesh placement and collision volume tuning can be compared in the same view.
- `View > Show Scene Meshes` can toggle Scene mesh drawing if a problem asset needs primitive-box fallback while editing.
- Dock layout version `8` resets stale/broken saved layouts and keeps the Unity-style panel placement alive long enough to recover from collapsed windows.
- Editor Scene meshes now use textured GLB rendering; the Content Browser thumbnail preview remains the lighter material-colour/wireframe path for now.

Current first-pass limitations:

- UI preview is currently source/text preview, not the final native RmlUi visual preview.
- Asset references are path-based; GUID/meta files are still future work.
- Play mode launches a separate game process and does not embed runtime play in the editor viewport yet.
- Content Browser model preview uses an editor-side material-colour shaded projection first; full textured offscreen thumbnail rendering is a later viewport/render-target milestone. The Scene view renders assigned GLBs through the engine renderer with textures enabled and keeps the collider/blockout shape available as an overlay.
- Rich object creation palettes, visual UI layout editing, lighting preview/tools, terrain, navmesh, material tools, and animation timelines are later milestones.

## First Milestone

The first usable version should include:

- Project browser and project file loading.
- Level list with New, Duplicate, Rename, Load, Save, and Save As.
- 3D viewport for level editing.
- Hierarchy panel for level entities.
- Inspector panel for entity/component data.
- Content browser rooted at the project content folders.
- Drag models from `Content/Models` into the Scene to create props, or assign/drop models onto selected entities.
- Basic object creation for boxes, props, rigid bodies, lights, triggers, player spawn, doors/chests, puzzle slots, damageable objects, and scripted objects.
- Script attachment for registered scripts only.
- Editable script JSON parameters in the inspector.
- Prefab creation and placement using JSON files under `Content/Prefabs`.
- Basic UI management with visible UI preview. Full visual UI authoring can grow in later passes, but the editor should be able to create/open/edit UI assets and show what they look like.
- Button to launch the existing asset importer.
- Button to launch the game from the selected scene.

## Project Layout Target

Use a Unity-like content structure:

- `Content/Levels`
- `Content/Models`
- `Content/Materials`
- `Content/Animations`
- `Content/Scripts`
- `Content/UI`
- `Content/Prefabs`

The editor creates missing folders when a project is opened or created.

## Project File

The project file is JSON and lives at the project root as `HS2Project.json`.

It tracks:

- Project name.
- Content root.
- Startup level.
- Recent levels.
- Recent assets.
- Blender executable path.
- Asset importer project path.
- Game project path.
- Editor layout/preferences.
- Future asset database settings.

## Asset Identity Plan

Path-based references are fine for the first pass because the current level and renderer already use paths such as `Content/Models/...`.

GUID references should be added later because they are better once assets can be renamed or moved. The expected future shape is one small metadata file per asset, or a central asset database, mapping stable GUIDs to paths and importer settings. Prefabs and levels can then reference GUIDs instead of fragile paths.

## Level Authoring

The editor builds on the current `LevelFile` and `LevelEntityDef` format while gradually moving editor-only logic out of `Game`.

Supported first-pass entities/components:

- Transform.
- Model renderer.
- Collider.
- Rigid body.
- Point light.
- Trigger volume.
- Damageable object.
- Break replacement/debris model lists.
- Interaction data for locked doors, locked chests, puzzle slots, puzzle levers, and puzzle doors.
- Player spawn.
- Registered script attachment with editable JSON.

## Interaction Authoring

Interaction data lives inside the selected object's `LevelEntityDef` in the level JSON. There is no separate JSON document to attach to an object for doors, chests, or puzzle slots.

In the Inspector, use the Interaction section to add one of the supported interaction types:

- `LockedDoor`
- `LockedChest`
- `PuzzleSlot`
- `PuzzleLever`
- `PuzzleDoor`

The editor writes a `LevelInteractionDef` object onto the selected entity with fields for kind, state id, required item, prompt text, success text, target entity names, required solved-state ids, reward items, and optional swing-door hinge data.

Locked-door workflow:

1. Select the actual door or door-blocking entity, not only the visual frame.
2. Click `Add Locked Door`.
3. Set `Required Item` to the inventory item id, for example `RustedKey`.
4. Set a unique `State Id`, for example `Door_Room1_RustedKey`.
5. Adjust the locked/success prompts if needed.
6. For real hinged doors, set `Hinge Local Offset` to the hinge edge in the door entity's local space and leave `Open Angle Deg` at 90 unless the door needs a different arc.

Key pickup workflow:

- A pickup named like `ItemKey_RustedKey` grants the `RustedKey` item id.
- The door opens when the runtime sees that the player has the required item id on the selected locked entity.
- For the current locked-door runtime, `Targets` are not needed; the locked entity itself is hidden/opened once unlocked.

Puzzle-slot workflow:

- Add `PuzzleSlot` to the slot/interactable entity.
- Set `Required Item`.
- Add one or more target entity names in `Targets`.
- Target entities should use `PuzzleDoor` or another runtime-supported target kind.

Puzzle-lever workflow:

- Add `PuzzleLever` to the lever/interactable entity.
- Add required solved-state ids in `Required States`, usually the `StateId` values from several puzzle slots.
- Add one or more target entity names in `Targets`; the runtime opens those targets only once every required state has been solved and the lever is pulled.

Rewards are stored on the interaction for future chest/puzzle reward workflows. The editor can author them now, but each runtime interaction kind must still explicitly consume those reward fields before they affect gameplay.
## Visual Props And Collision

`Prop` entities should be treated as visual-only scene dressing in runtime. If an imported model needs to block the player, weapon traces, gravity-gun targeting, or physics objects, author it as a `RigidBody` and choose the appropriate collider shape.

For modular architecture, prefer stable primitive colliders during early level blocking even when the visible object uses a GLB mesh. Use `Shape = "Mesh"` only when the opening or silhouette matters, such as a walkable frame or irregular static object. Dynamic objects should stay on box/sphere/capsule colliders until convex/dynamic mesh physics exists.

The current basement slice uses visible GLB meshes with box colliders for the main floors/walls/ceilings, split primitive blocker pieces for door and rolling-door frames, and separate visual-only props where a decorative mesh should not become one large solid wall. Collision-only helpers should use primitive shapes with no `MeshPath` and can set `Color.W = 0` so runtime skips only the primitive fallback visual while still using the collider data.


## Interaction Inspector UI Update

The inspector interaction authoring UI now uses full-width vertical controls so labels are not clipped in narrow inspector panels. `Targets` are editable for `PuzzleSlot` and `PuzzleLever` interactions, where they name the entities affected by the interaction. `LockedDoor` and `LockedChest` interactions do not need targets because runtime opens/hides the selected locked entity itself; if stale targets exist, the inspector shows a clear-unused-targets action.

Every interaction edit goes through the standard editor dirty/save path. While dirty, the interaction inspector shows `Save Active Document`, and changes persist when the current level or prefab is saved.
## Lighting Authoring Direction

The editor already has a `PointLight` entity type with colour, intensity, and range fields in the level data. The next lighting pass should make those entities drive the game renderer instead of relying on the current fixed global textured-model light.

Recommended authoring model:

- Light fixture models and actual light sources should be separate entities. A lamp, bulb, ceiling fixture, or wall sconce model is normal scene geometry; one or more `PointLight` entities provide illumination.
- The editor should allow light entities to be placed, selected, moved, coloured, ranged, and previewed in the Scene view with a visible light icon/gizmo and radius helper.
- Light fixtures can be parented/grouped with their light entities once parent workflows are stable, so moving a lamp can also move its light source.
- Light switches should not hard-code individual object references in code. They should target named light groups or explicit entity IDs from level data.
- A switch entity should expose an interaction component such as `ToggleLights`, with fields for target light group/entity IDs, starts-on state, one-shot vs reusable behavior, prompt text, and optional sound/event names.
- Light entities should store runtime state such as enabled/disabled, colour, intensity, range, falloff, and group name. Save data should persist any switch-controlled light state that the player changes.
- The editor should show enough of the final lighting to support level dressing, even if the first pass is simple forward point lights rather than baked/global illumination.

Short implementation path once code work starts:

1. Extend `LevelEntityDef`/inspector if needed with `Enabled`, `LightGroup`, and possibly `Falloff` fields for point lights.
2. Make `BasicWorldRenderer` accept a small fixed array of active point lights each frame, plus ambient light, and pass them to the textured model shader.
3. Replace the current single hard-coded textured-model light with ambient plus directional plus nearby point lights.
4. Add a registered switch/interactable script that toggles target point lights by group or entity ID.
5. Save/load changed light states so a switched-off room stays switched off.
6. Add editor Scene preview for point-light influence radius and on/off state.

## Prefabs

Prefabs are JSON files under `Content/Prefabs`.

The first prefab format reuses the same entity/component data used by levels. A prefab should be placeable into a level and should carry its transform, renderer/model path, collider, physics, damage settings, interaction data, and scripts.

Breakable crate should become an early prefab candidate now that the prefab browser exists.

## Script Attachment

The first pass only attaches scripts that are already registered with the runtime script registry.

The inspector should show:

- Script type.
- JSON parameter text.
- Add/remove script controls.
- Validation errors when JSON is malformed or script type is unknown.

Creating new C# script files from the editor is out of scope for the first pass.

## UI Editing Direction

The first UI editing pass manages UI files and currently shows source/text preview.

Current target:

- Browse `Content/UI`.
- Create/open/save `.rml` and `.rcss` files.
- Edit source text in the editor app.
- Move from source/text preview to the same RmlUi/preview path available to the engine.

Later target:

- Visual layout canvas.
- Selectable UI elements.
- Inspector for element properties/styles.
- Asset picker for fonts/images.
- Live preview against sample gameplay UI state.

## Asset Importer Integration

HS2Editor does not duplicate the converter in the first pass. It launches the existing `Engine.AssetImporter` app and stores useful project paths.

Useful launch behavior:

- Open importer from toolbar/menu.
- Preselect common destination folders such as `Content/Models`, `Content/Animations`, or `Content/Models/ViewModels` when possible.
- Store Blender path in the project file or shared settings.

## Play From Editor

The first play flow launches the existing game executable with the currently selected level path.

Expected flow:

1. Save current level.
2. Launch game with selected level argument.
3. Runtime loads that level instead of always loading `interaction_test.json`.
4. Return to editor manually when finished.

Embedding runtime play inside the editor viewport can be a later milestone.

## Launchers

Root-level batch launchers are present:

- `LaunchEditor.bat`
- `LaunchAssetImporter.bat`
- `LaunchGame.bat`

These run the relevant project from the repository root.

## Acceptance Criteria For First Usable Editor

- A user can launch `HS2Editor`.
- A project opens with the expected content folders.
- A level can be created, loaded, edited, saved, duplicated, and renamed.
- Objects can be selected in a 3D viewport.
- Entity transforms, model paths, collider/physics settings, damage settings, interaction settings, and scripts can be edited through the existing inspector.
- A GLB from the content browser can be assigned to an entity, dropped onto box/prop/rigid-body `MeshPath`, or dropped into the Scene to create a movable static rigid body with a box collider.
- A prefab can be created from an object and placed back into a level.
- UI files can be opened, edited, saved, and source-previewed.
- The asset importer can be launched from the editor.
- The selected level can be launched in the game.

## Out Of Scope For The First Pass

- Full visual UI authoring.
- Embedded runtime play mode inside the editor viewport.
- Creating new C# scripts from inside the editor.
- GUID-based asset database.
- Terrain tools.
- Navmesh tools.
- Animation timeline editing.
- Material graph editing.



## Prefab Workflow Update
Door prefab placement update:

- Prefabs are JSON assets under `Game/Content/Prefabs` and are placed with `Place`, not assigned as model meshes.
- Placed prefab instances remap entity ids and now also suffix interaction `StateId` values so each placed locked door has an independent lock state.
- Doorframe/root entities should not carry `LockedDoor`; the actual child door entity should own the interaction.

Prefab assets now use a `PrefabFile` JSON format under `Content/Prefabs` with a root entity id, optional base prefab path for variants, and an entity list. Legacy one-entity prefab JSONs are still loaded and wrapped into the new format.

The editor can now build a prefab from the selected hierarchy root and all of its children. The selected root becomes the prefab root, children keep their parent links, and the root position is normalized to zero so new instances can be placed cleanly.

Placed prefab instances remap every entity id, preserve parent-child links, and write prefab metadata onto each placed entity: asset path, instance id, and source entity id. This supports Apply, Revert, and Unpack operations.

Supported first-pass prefab operations:

- Create prefab from selected hierarchy.
- Create variant from a selected prefab instance or selected prefab asset.
- Place prefab instances from `Content/Prefabs`.
- Apply selected instance changes back to its prefab asset.
- Revert selected instance from its prefab asset while preserving root placement.
- Unpack selected instance into normal scene entities.
- Edit a prefab in an isolated prefab workspace, then save and return to the previous level.
- Browse prefabs in a dedicated Content Browser tab.

Variants currently store a base prefab path and a full copied entity set. Full live inheritance/merge behavior is a later milestone.

## Scene Mesh Selection Visibility

The standalone editor now treats assigned GLB meshes as the primary scene visual when `View > Show Scene Meshes` is enabled and loads their embedded GLB textures in the Scene view. The in-game editor view also tries to draw selected GLB meshes before using primitive debug boxes, so play-mode editing no longer shows large fitted boxes as the main visual for imported models. Collider boxes are no longer forced on for selected GLB entities in the standalone editor; they are shown only when the `Show Colliders (OBB)` toolbar toggle is enabled.

Selected GLB entities get an editor-only yellow solid highlight pass so dark imported materials do not make the mesh disappear against the scene. Children of the selected hierarchy root get a softer blue highlight, which makes grouped objects such as a doorframe root with a child door easier to line up as one assembly.





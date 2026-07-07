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
- Dockspace-based editor workspace with a Unity-style default placement: Scene center, hierarchy/levels/project on the left, inspector/toolbar on the right, and content/prefab/UI/status panels around the scene. The current ImGui.NET wrapper does not expose dock-builder calls, so the editor seeds default window placement and still leaves panels dockable/manually rearrangeable. Layout-version changes, missing saved layouts, and saved layouts with collapsed/tiny key panels clear stale `imgui.ini` state and hold default placement long enough to recover from old collapsed/off-screen windows.
- Scene camera controls: RMB look plus WASD/QE fly movement while the Scene panel is hovered or focused.
- Scene selection and dragging through the existing `LevelEditorController` picking path. Picking rays now use the Scene panel rectangle instead of the whole application window, and the Scene panel is not treated as regular UI for mouse blocking. If the Scene panel bounds are missing or saved below a usable size during startup, world rendering falls back to the full app window rather than leaving a tiny black viewport.
- Existing hierarchy, inspector, and toolbar panels reused inside the standalone app.
- Content browser for models, animations, and all content files. The Models tab uses a table layout with a visible asset count/path header, explicit Asset and Assign columns, and a selected-model shaded/wireframe 3D preview pane for `.glb` assets when there is enough room.
- Assign selected model from `Content/Models` to the selected entity by writing a `Content/...` mesh path.
- Drag/drop GLB asset payloads from the content browser into inspector model lists. Damageable object replacement and debris model lists now accept dragged `.glb` paths, which supports workflows such as dragging `DamagedCrate*.glb` onto a crate replacement list.
- Prefab creation from the selected entity and prefab placement from JSON files under `Content/Prefabs`.
- Basic UI manager for creating, opening, editing, and saving `.rml` and `.rcss` files.
- Asset importer launch from the editor.
- Play selected level by saving the current level and launching `Game` with `--level <current level path>`.
- Game startup now accepts `--level` so editor-launched play can load the selected scene.

Current first-pass limitations:

- UI preview is currently source/text preview, not the final native RmlUi visual preview.
- Asset references are path-based; GUID/meta files are still future work.
- Play mode launches a separate game process and does not embed runtime play in the editor viewport yet.
- Content Browser model preview uses an editor-side shaded/wireframe projection first; full textured offscreen preview rendering is a later viewport/render-target milestone.
- Rich object creation palettes, visual UI layout editing, terrain, navmesh, material tools, and animation timelines are later milestones.

## First Milestone

The first usable version should include:

- Project browser and project file loading.
- Level list with New, Duplicate, Rename, Load, Save, and Save As.
- 3D viewport for level editing.
- Hierarchy panel for level entities.
- Inspector panel for entity/component data.
- Content browser rooted at the project content folders.
- Drag or assign models from `Content/Models` onto entities.
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
- Interaction data for locked doors, locked chests, puzzle doors, and puzzle slots.
- Player spawn.
- Registered script attachment with editable JSON.

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
- A GLB from the content browser can be assigned to an entity.
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
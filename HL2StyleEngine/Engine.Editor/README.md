# HS2Editor Plan

HS2Editor is planned as a standalone editor application for the engine, separate from the runtime game and separate from the asset importer.

## Goal

HS2Editor should become the Unity-style project and level authoring app for the engine. It should let the project owner manage content folders, create and edit levels, place objects, attach scripts/components, assign imported models, manage prefabs, inspect UI files, and launch the game from the selected scene.

The first implementation should reuse the existing engine renderer and ImGui editor tooling so the app can have a real 3D viewport, hierarchy, inspector, project browser, and content browser.

## Confirmed Direction

- App name: `HS2Editor`.
- App type: standalone executable, like `Engine.AssetImporter`.
- Rendering/UI tech: existing engine renderer plus ImGui.
- Play mode: first pass launches the existing game executable with the selected level path, rather than embedding full runtime play inside the editor viewport.
- Import integration: first pass launches the existing converter app from the editor.
- Launchers: provide simple root-level batch launchers for editor, asset importer, and game.
- Project file: add a project JSON file to store project folders, startup level, recent levels, importer settings, Blender path, and editor preferences.
- First pass asset references: path-based references are acceptable.
- Longer-term asset identity: add GUID/meta files once the content browser, prefab system, and asset moves/renames need stable references.

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

The editor should create missing folders when a project is opened or created.

## Project File

The project file should be JSON and live at the project root. A likely name is `HS2Project.json`.

It should track:

- Project name.
- Content root.
- Startup level.
- Recent levels.
- Recent assets.
- Blender executable path.
- Asset importer executable path.
- Game executable path.
- Editor layout/preferences.
- Future asset database settings.

## Asset Identity Plan

Path-based references are fine for the first pass because the current level and renderer already use paths such as `Content/Models/...`.

GUID references should be added later because they are better once assets can be renamed or moved. The expected future shape is one small metadata file per asset, or a central asset database, mapping stable GUIDs to paths and importer settings. Prefabs and levels can then reference GUIDs instead of fragile paths.

## Level Authoring

The editor should build on the current `LevelFile` and `LevelEntityDef` format while gradually moving editor-only logic out of `Game`.

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

Prefabs should be JSON files under `Content/Prefabs`.

The first prefab format can reuse the same entity/component data used by levels. A prefab should be placeable into a level and should carry its transform, renderer/model path, collider, physics, damage settings, interaction data, and scripts.

Breakable crate should become an early prefab candidate once the prefab browser exists.

## Script Attachment

The first pass should only attach scripts that are already registered with the runtime script registry.

The inspector should show:

- Script type.
- JSON parameter text.
- Add/remove script controls.
- Validation errors when JSON is malformed or script type is unknown.

Creating new C# script files from the editor is out of scope for the first pass.

## UI Editing Direction

The first UI editing pass should manage UI files and show a visible preview.

Minimum target:

- Browse `Content/UI`.
- Create/open/save `.rml` and `.rcss` files.
- Preview UI using the same RmlUi/preview path available to the engine.
- Edit source text in the editor app.

Later target:

- Visual layout canvas.
- Selectable UI elements.
- Inspector for element properties/styles.
- Asset picker for fonts/images.
- Live preview against sample gameplay UI state.

## Asset Importer Integration

HS2Editor should not duplicate the converter in the first pass. It should launch the existing `Engine.AssetImporter` app and pass or remember useful project paths.

Useful launch behavior:

- Open importer from toolbar/menu.
- Preselect common destination folders such as `Content/Models`, `Content/Animations`, or `Content/Models/ViewModels` when possible.
- Store Blender path in the project file or shared settings.

## Play From Editor

The first play flow should launch the existing game executable with the currently selected level path.

Expected flow:

1. Save current level.
2. Launch game with selected level argument.
3. Runtime loads that level instead of always loading `interaction_test.json`.
4. Return to editor manually when finished.

Embedding runtime play inside the editor viewport can be a later milestone.

## Launchers

Add simple root-level batch launchers when implementation starts:

- `LaunchEditor.bat`
- `LaunchAssetImporter.bat`
- `LaunchGame.bat`

These should build or run the relevant project/executable with paths that work from the repository root.

## Acceptance Criteria For First Usable Editor

- A user can launch `HS2Editor`.
- A project opens with the expected content folders.
- A level can be created, loaded, edited, saved, duplicated, and renamed.
- Objects can be placed and selected in a 3D viewport.
- Entity transforms, model paths, collider/physics settings, damage settings, interaction settings, and scripts can be edited.
- A GLB from the content browser can be assigned to an entity.
- A prefab can be created from an object and placed back into a level.
- UI files can be opened, edited, saved, and previewed.
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
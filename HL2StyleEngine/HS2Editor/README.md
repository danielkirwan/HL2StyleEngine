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
- Scene panel with grid, selection, transform gizmo drawing, and editor camera controls.
- Unity-style default window placement: Scene center, hierarchy/levels/project left, inspector/toolbar right, and content/prefab/UI/status panels around it. Panels remain dockable and manually rearrangeable through ImGui docking. Layout-version changes, missing saved layouts, and saved layouts with collapsed/tiny key panels now clear stale `imgui.ini` state and hold the default placement for several seconds so old collapsed/off-screen windows cannot leave the editor black. The scene render fallback also treats very small saved scene panels as invalid and renders full-window until the layout recovers.
- Content browser for models, animations, and project files. The Models tab uses a table layout with an explicit Asset column and Assign column, plus a selected-model 3D shaded/wireframe preview pane for `.glb` model assets when the panel is wide enough.
- Assign a `.glb` model from `Content/Models` to the selected entity, or drag a model from the content browser into inspector lists that accept model assets.
- Save selected entity as a prefab JSON and place prefabs back into the level.
- Basic `.rml` and `.rcss` UI file creation/editing/saving with source preview.
- Launch the asset importer.
- Launch the game from the selected level via `--level`.

## Current Limitations

- UI preview is source/text based until native RmlUi visual preview is wired into the editor.
- Asset references are path-based; GUID/meta files are planned for a later asset database pass.
- Play mode launches a separate game process instead of embedded play-in-editor.
- Content Browser previews are currently editor-side shaded/wireframe previews, not full textured offscreen render targets. Programmatic dock splitting is limited by the current ImGui.NET wrapper, so the first reset uses default window placement rather than generated dock nodes. If the saved ImGui layout records tiny/off-screen panels, startup now treats that file as broken and rebuilds the layout. Rich object palettes, terrain, navmesh, material editing, animation timeline editing, and C# script creation are later milestones.
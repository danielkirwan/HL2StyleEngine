# HS2Editor

Lighting states (2026-10-25): open `sixRoomLightingStabilityTest.json`. Shared **Toolbar > Indirect Lighting > Lighting States** creates/selects explicit on/off authored defaults with undo; bake/capture operates on the selected state's cache identity. **Static Lighting > Reflection Only / Probe Lit** bounds detailed-prop diffuse costs; **Lighting > Point Shadow Selection Margin** controls hysteresis. Existing docking/textured scenes are preserved. [Workflow and important recooking/state limits](../Game/LIGHTING_STABILITY_GUIDE.md).

Fixture-light test (2026-10-25): open `sixRoomPointLightTest.json` for eight visible point-light fixtures with zero ambient/directional fill. The Content Browser's Prefabs tab includes `CeilingPointLight.prefab.json`: move its housing root and edit the point-light/glowing-tube children. Existing PBR level and editor behaviour are unchanged. [Placement, recapture and shadow limits](../Game/POINT_LIGHT_TEST_GUIDE.md).

Latest (2026-10-25): open sixRoomPbrTest.json for the separate playable PBR rollout. Toolbar > Room Reflections now has Stream Room Reflections, resident limit, prefetch distance, retention and Unload / Reload Rooms. Movement of the editor camera drives residency; selected in-progress captures are pinned. Existing texture rendering, docking and input conventions are unchanged. [Residency workflow and tests](../Game/REFLECTION_STREAMING_GUIDE.md).

Room transitions (2026-10-25): open `roomTransitionTest.json`. **Toolbar > Room Reflections > Automatic Room Reflections** selects captures by room and blends through authored doorway zones. The Inspector adds Doorway Neighbor/Offset/Size/Travel Axis, while existing bounds/priority now affect rendering. Turning automatic mode off retains the old manual preview. Shared F2/standalone authoring, undo/save/prefab links and scene rendering are validated; native mouse/docking acceptance remains a manual check. Workflow and limits: `Game/ROOM_TRANSITION_GUIDE.md`. The dated entries below describe earlier stages.

Authored room captures (2026-10-23): open `roomReflectionTest.json` and **Toolbar > Room Reflections** for Add Probe, Capture / Rebuild, Cancel, Reload, preview selection and bounds. Probe Inspector metadata supports undo/save/prefabs. Captures use fixed geometry/lighting only and private GPU state, never the active camera's visible subset. One selected capture currently previews globally; automatic transitions/projection remain next. The standalone editor and F2 use the same session. Native docking/mouse acceptance still needs a manual playtest. See `Game/ROOM_REFLECTION_GUIDE.md`; run `LaunchRoomReflectionTest.bat` for the ready-made game fixture.

PBR/reflection follow-up (2026-10-23): open `pbrMaterialTest.json`, then use **Toolbar > Lighting > Lighting Model** for Legacy/PBR, **Specular Environment** for Off/Synthetic Test Studio, and **Indirect Lighting > PBR Diagnostic** for temporary contribution views. Specular Indirect isolates the new GGX environment. Game/F2/HS2Editor share save/undo, texture resources and shading; old levels remain Legacy with no environment. This is not an actual room capture. Model-cache v3 remains supported. Existing docking, selection and startup preferences are unchanged. See `Game/PBR_MATERIAL_GUIDE.md` and `Game/REFLECTION_LIGHTING_GUIDE.md` for tests, limitations and the launcher.

Playtest standard (2026-10-06): Play Selected Level / Launch Game From Level saves the current level and launches Game in Release, regardless of the editor's own build configuration. It still uses that selected level, not a hardcoded test level. Debug remains available through Visual Studio. The importer launch and editor startup scene are unchanged.

Rendering performance follow-up (2026-10-06): the scene viewport batches object uniform uploads before frame submission, shared with Game/F2. This preserves textured models, inverse-transpose normals, selection outlines, gizmos and draw order; it does not change docking or authored transforms. Main-scene batching is covered by GPU reference comparisons in `Game/ENGINE_UPGRADE_VALIDATION.md`. Standalone mouse/docking interaction remains a manual check.

Standalone first-pass project and level editor for HL2StyleEngine.

## Run

Use `LaunchEditor.bat` from the repository root, or run:

```powershell
.\.dotnet\dotnet.exe run --project HS2Editor\HS2Editor.csproj
```

The editor loads `HS2Project.json`, creates missing content folders under `Game/Content`, and opens the configured startup level.

Since 2026-10-05, the toolchain targets .NET 10. Root launchers and editor Play/importer actions prefer the project-local SDK; `Tools/SetupDotnet.ps1` provisions it on fresh checkouts, or use a compatible installed SDK. Shared level/prefab persistence now uses RE:Dox while keeping editable JSON and `.json.bak` backups. Level `.hs2cache/*.dox` files are disposable and validated against source contents, so stale timestamps cannot override saved edits. Scene rendering, docking and model transforms are unchanged by this migration. See `Game/PERSISTENCE_AND_INVENTORY.md` for details and Visual Studio requirements.

Game's Visual Studio launch profiles and both game launchers explicitly open `sixRoomTest.json` for testing; this does not change the startup scene selected here. F2 inside Game is a smaller in-game editor with the shared Toolbar/Hierarchy/Inspector, not this full application. Its new View menu restores those panels or opens F3 debug/weapon tools. Content Browser, prefab browsing and UI management remain in HS2Editor. The September 8 six-room performance fixes change runtime lookups and physics bounds/support queries, not this application's docking or textured renderer. See `Game/SIX_ROOM_TEST.md` for measured results and remaining manual checks.

## Asset And Lighting Update (2026-10-06)

Probe/preview follow-up (2026-10-22): open `lightProbeTest.json` for the separate movable-object lighting test. **Toolbar > Indirect Lighting > Bake Light Probes** and **Probe Spacing (m)** control the optional automatic grid. **Lighting View** switches Full / Direct Only / Indirect Only; **Preview Light Probes** isolates the moving-object contribution while leaving the static atlas enabled. **Show Probe Markers** displays green usable/red rejected samples. Validity, valid/total probe count and CPU sampling cost are visible. These preview controls are temporary and do not dirty/rebake the scene. Invalid/stale data falls back to realtime lighting. Both HS2Editor and F2 use the same implementation; no docking changes. Workflow, hard limits and performance: [bake guide](../Game/BAKED_LIGHTING_GUIDE.md) and [validation](../Game/ENGINE_UPGRADE_VALIDATION.md).

Static indirect follow-up (2026-10-22): **Inspector > Static Lighting** opts in fixed architecture; **Fixed Light: Bake Indirect** opts in permanent point/spot lights. **Toolbar > Indirect Lighting** supplies enable/settings, save-and-bake, progress/cancel, errors/stale fallback, memory and a non-destructive preview toggle. Load `indirectLightingRoom.json` for the separate 17-instance baked test. Rendering uses the same linear HDR lightmaps in HS2Editor, F2 and Game, without replacing textures or source UVs. Eligible opt-in static objects preserve the exact authored quaternion for render/collision parity; unmarked legacy objects are unchanged. No layout/docking changes. Full workflow and restrictions: [baked lighting guide](../Game/BAKED_LIGHTING_GUIDE.md).

Emission follow-up (2026-10-22): select an assigned model and open **Inspector > Emission**. Intensity scales imported emission; **Override Imported Emission** enables colour and named model-part selection. **Linked Light** stores a stable point/spot-light ID and makes visible emission follow authored on/off/flicker in the editor and saved overrides in play. None leaves emission independent. Internal links survive prefab placement/apply/revert; shared model resources are not edited. Both HS2Editor and F2 use this same inspector and lighting calculation. Open `sixRoomEmissionTest.json` for the switched console/arcade example. See `Game/LIGHTING_GUIDE.md` for the full workflow and limitations; emission alone does not illuminate a room.

Scene models now use the same cooked/shared BC7/BC5 texture pipeline as Game, with mipmaps, normal maps and budgeted GPU uploads. Run `CookAssets.bat` or launch the importer and use its new Compression tab after converting assets; restart previews to pick up newly cooked textures. Existing model transforms, mesh fit, selection outlines and collision shapes are preserved. Missing/corrupt caches use an RGBA fallback.

Cooked-model follow-up: Compression > Cook models and textures also creates validated lossless LZ4 geometry/material caches. Scene loading uses these automatically, skipping GLB parsing on a hit while preserving imported coordinates, part names and texture data. Missing/stale/invalid caches use the original GLB loader. Source GLBs remain required. The lightweight Content Browser preview retains its existing preview loader. No docking, selection or authored level settings were changed. Restart the editor after cooking; see `Game/ENGINE_UPGRADE_VALIDATION.md` for measured CPU preparation versus full render-ready timings.

Toolbar > Lighting exposes scene Shadows, Exposure, Ambient and Directional controls. Select a PointLight to change Enabled, Cast Shadows, Spotlight and Cone Angle, as well as existing colour/intensity/range controls. A spotlight points along its local -Z axis. Save Level persists these settings. Scene Point Lights must be enabled for level-authored lights. Existing lights default to enabled but do not cast shadows until opted in. Shadow budgets are two spots and one point light; extra lights are unshadowed. A low ambient/directional setting is useful for horror-lighting tests, but is not forced onto existing levels.

Game's F key toggles a shadowed flashlight during gameplay. Editor F-to-focus is unchanged. PointLight Inspector now includes Light Group, Flicker Amount and Flicker Speed. Add Light Switch under a switch object's Interaction section, then choose a group and/or explicit light targets; optional Required States gate use. Save Level persists authoring, while player saves retain runtime on/off overrides. Both editors preview authored defaults and flicker. Internal prefab light targets remap per instance and revert retains surviving IDs. See `Game/LIGHTING_GUIDE.md` for the full workflow and group-versus-ID behavior.

Unchanged shadows are cached and each shadow face culls irrelevant objects. Baked indirect lighting/probes now have the opt-in workflow above; animated runtime light attachments remain future work. HDR/colour handling changes how lighting is displayed; inspect your scene and tune exposure rather than rotating a correctly aligned wall to compensate for light. See `Engine.Render/README.md` and `Game/ENGINE_TECHNOLOGY_ROADMAP.md`. Existing authored scenes and docking layouts were not rewritten.

## Visibility And Runtime Streaming (2026-10-06)

Scene rendering now shares conservative main-view primitive/model-part culling with Game/F2. Selected-object textures/outlines, authored transforms, vertex snapping and dock layouts are unchanged. The editor keeps its authoring resource cache; it does not unload logical scene objects.

Toolbar > Asset Streaming saves runtime settings with the selected level: enabled by default, 32 m spatial zones, one neighbour ring, 256 MiB soft budget and 15 seconds retention. Game prefetches zone dependencies and releases distant unused render resources without removing entities/colliders or resetting gameplay state. F2 authoring temporarily uses full residency. Play Selected Level continues to save and launch Release. See [the streaming guide](../Game/STREAMING_GUIDE.md); no asset recook or level conversion is required.

## Current Features

Collision-cache follow-up (2026-10-21): Play now uses the game's validated static mesh collision cache automatically. World position/size/rotation and source content are part of its identity, so edited walls/frames cannot reuse a collider for an old placement after reload. F2 runtime rebuilds refresh source fingerprints. Moving mesh-door poses bypass disk caching. Authoring transforms, visible meshes and collider types are unchanged; see `Game/COLLISION_CACHE_GUIDE.md`. No new editor controls are needed for this cache.

- `sixRoomTest.json` is available in the Levels panel as a larger six-room physics/exploration test, now with five shutter puzzles. See `Game/SIX_ROOM_TEST.md` for layout, solutions and the play-test checklist. `LaunchSixRoomTest.bat` plays it directly.
- Puzzle authoring additions (2026-09-08): Interaction > Add Pressure Plate, Add Puzzle Indicator, and Puzzle Door > Lift Height. Plates use a horizontal static box rigid body's top and yaw-rotated footprint, with editable minimum mass/settle time. Indicators use Required States on a primitive Prop. Filter `SRPuzzle_` in Hierarchy to find the authored puzzle objects. Required pickups remain dynamic/Can Pick Up; frames are static Mesh colliders and separate shutter leaves own the lifting interaction. No layout or renderer changes accompany this pass.
- Scene Point Lights in the Toolbar toggles saved level lighting (`UsePointLights`). Enabled levels render nearby PointLight colour, intensity, range, flicker, spot cones and opt-in shadows in both Scene and game. `sixRoomTest` enables it; older levels default to off. Groups and repeatable switches are available through the Inspector.

- Level create, load, save, duplicate, and rename.
- Scene panel with grid, selection, transform gizmo drawing, editor camera controls, textured GLB scene rendering for placed models, outline/corner selection markers, and first-pass vertex/corner snapping with `V`.
- Unity-style default window placement: Scene center, hierarchy/levels/project left, inspector/toolbar right, and content/prefab/UI/status panels around it. Panels remain dockable and manually rearrangeable through ImGui docking. Layout-version changes, missing saved layouts, and saved layouts with collapsed/tiny key panels now clear stale `imgui.ini` state and hold the default placement for several seconds so old collapsed/off-screen windows cannot leave the editor black. The scene render fallback also treats very small saved scene panels as invalid and renders full-window until the layout recovers.
- Content browser for models, animations, prefabs, and project files. The Models tab uses a table layout with an explicit Asset column and Assign column, plus a selected-model 3D shaded/wireframe preview pane for `.glb` model assets when the panel is wide enough. The Prefabs tab lists JSON prefabs from `Game/Content/Prefabs` with Place/Edit actions.
- Assign a `.glb` model from `Content/Models` to the selected entity, or drag a model from the content browser into inspector lists that accept model assets. Assigning/dropping a GLB onto a static rigid body now defaults it to `Shape = "Mesh"` so imported architecture can use triangle mesh collision. Use visual-only `Prop` entities for decoration and `RigidBody` entities for anything that should collide.
- Inspector interaction controls can add/edit locked doors, locked chests, puzzle slots, puzzle levers, and puzzle doors directly on the selected level entity. Locked doors expose hinge offset/open-angle tuning, and puzzle levers expose required solved-state ids. The data is saved inside the level JSON, not in a separate attached document.
- Save selected entity as a prefab JSON and place prefabs back into the level.
- Basic `.rml` and `.rcss` UI file creation/editing/saving with source preview.
- Launch the asset importer.
- Launch the game from the selected level via `--level`.

## Current Limitations

- During the 2026-09-08 test, the persisted editor layout still opened with overlapping/collapsed panels despite the earlier layout recovery work. This remains an editor usability issue; the new six-room level was verified through its direct game launcher and through editor-controller data checks.

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

When a source level is saved, HS2Editor also mirrors that JSON into any existing runtime output copies under `Game/bin/.../Content/Levels` and `HS2Editor/bin/.../Content/Levels`. This keeps direct game launches from reading stale copied content between builds. Editor-launched play passes the exact source level path through `--level` and now starts at its authored spawn with the prototype loadout, without automatically restoring another level's save state.

## Scene Mesh Selection Visibility

Scene meshes are now easier to edit directly. GLB objects render as their mesh shape, and selected meshes use a yellow wire/corner outline instead of a solid yellow fill so the texture and orientation remain visible while editing. Child meshes under the selected root receive a softer blue outline. Selection uses exact oriented-box picking for the editor draw volume, and transparent collision helpers are ignored unless `Show Colliders (OBB)` is enabled. This is intended for prefab-style assemblies such as `PracticeDoorFrameMesh` with a child door.

Collider/blockout boxes default to hidden for GLB scene editing and can be enabled from the toolbar with `Show Colliders (OBB)` when collision tuning or selection of invisible helpers is needed. Rigid-body fallback boxes now respect the entity `Color` value instead of forcing magenta, so primitive helper colliders can use `Color.W = 0` to stay collision-only. Do not rely on alpha to hide GLB renderers; collision-only helpers should have no `MeshPath`. For static GLB architecture that is rotated or heavily scaled to make the texture face correctly, use `Shape = "Mesh"` so the collider follows the model triangles instead of the fitted box. If scene meshes are hidden from the View menu, the Scene panel shows a warning.
## Vertex/Corner Snapping

Hold `V` in the Scene view to enter the first-pass snapping workflow. While holding `V`, click a corner on the selected object to choose the source corner, then click a corner on another object to move the selected object so those two corners align. This currently uses the editor object's oriented bounds corners rather than every imported mesh vertex, which is enough for lining up modular walls, floors, frames, and doors.

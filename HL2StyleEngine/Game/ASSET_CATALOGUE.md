# Game Asset Catalogue

Snapshot: 2026-09-08. This lists files in the source `Game/Content` tree, not generated copies in `bin` or external Filebase source assets. Presence here does not mean a model is placed in a level or has gameplay behaviour configured. Filenames are preserved exactly, including existing spelling quirks.

## Summary

- 376 GLB model files: 374 original imports in `Game/Content/Models/ViewModels`, plus two derived rolling-door parts in `Game/Content/Models/Puzzles`.
- 16 animation GLBs in `Game/Content/Animations`.
- 5 level JSON files and 1 prefab JSON file.
- 1 TTF font, 10 UI PNGs, and HTML/RML/RCSS UI files.
- `Game/Content/Materials` and `Game/Content/Scripts` currently contain no files. This does not imply that GLBs lack embedded materials or that registered C# gameplay scripts are absent elsewhere in the solution.

| Model group | Files |
| --- | ---: |
| Basement corridor kit | 127 |
| Psychiatric ward kit | 91 |
| Structural extras | 15 |
| Doors and intercoms | 28 |
| Derived rolling-door parts | 2 |
| Pipes | 14 |
| Equipment and props | 13 |
| Cables and wires | 48 |
| Keys and locks | 13 |
| Crates | 8 |
| Health and batteries | 11 |
| Weapons | 4 |
| Character | 1 |
| Unclassified | 1 |

## Import Reminder

1. Launch root-level `LaunchAssetImporter.bat` (HS2 Asset Importer), or launch the importer from HS2Editor.
2. Use the Models tab for models. Select the source asset folder containing the FBX and its texture folders. Selecting an `FBX` folder also searches its parent for textures.
3. Choose `Game/Content/Models/ViewModels`, or another folder under `Game/Content/Models`, as the destination. For a single file set an output name. For a pack enable Convert all FBX in source; it is enabled automatically when browsing to a folder containing multiple FBXs, and each FBX keeps its filename.
4. Blender exe normally auto-fills. The installed executable at this snapshot is `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`. Browse to it if needed. `HS2_BLENDER_EXE` is an optional environment variable, not a magic value to enter into the text field. Empty input still allows environment/PATH/standard installation-folder discovery.
5. Click Convert and check the log for texture matches, skipped FBXs and Ready outputs. Blender runs in the background, imports the FBX, reconstructs supported material nodes and exports GLB. Batch matching uses material names and FBX names; check ambiguous matches rather than assuming every texture was assigned perfectly.
6. Open the result in HS2Editor's Content Browser and place/assign it. Model import alone does not add collision, a door interaction, a pickup identity or weapon behaviour. Use Prop for non-colliding decoration, or a static RigidBody with Mesh shape for architectural mesh collision.

For animation FBXs, use the Animations tab and destination `Game/Content/Animations`. Export preserves supported armature/clip data, but runtime skeletal animation playback is not implemented yet. The imported soldier is also awaiting runtime skinning support; its presence is not a finished playable character integration. See `Engine.AssetImporter/README.md` for details.

## Models

### Derived Rolling-Door Parts (2)

In `Game/Content/Models/Puzzles`:

- `RollupDoor1_Frame.glb`
- `RollupDoor1_Leaf.glb`

These are separate mesh nodes extracted from the existing `Rollup Door 1.glb` for the six-room puzzles. The source GLB is unchanged. Embedded textures/materials and node transforms are retained. The frame stays fixed while the leaf lifts; these are not newly downloaded assets or animated/skinned door models.

### Basement corridor kit (127)

#### Set A (41)

- `Basement_Corridor_A_Broken_Wall_Fence_2m.glb`
- `Basement_Corridor_A_Broken_Wall_Fence_3m.glb`
- `Basement_Corridor_A_Broken_Wall_Fence_4m.glb`
- `Basement_Corridor_A_Ceiling_2x2m.glb`
- `Basement_Corridor_A_Ceiling_4x4m.glb`
- `Basement_Corridor_A_Ceiling_Arched_4x2m.glb`
- `Basement_Corridor_A_Ceiling_Arched_4x4m.glb`
- `Basement_Corridor_A_Ceiling_Arched_Corner.glb`
- `Basement_Corridor_A_Ceiling_Arched_Junction.glb`
- `Basement_Corridor_A_Ceiling_Arched_T_Junction.glb`
- `Basement_Corridor_A_Concrete_Coloumn.glb`
- `Basement_Corridor_A_Floor_2x2m.glb`
- `Basement_Corridor_A_Floor_4x2m_Vent.glb`
- `Basement_Corridor_A_Floor_4x4m_Vent.glb`
- `Basement_Corridor_A_Floor_4x4m.glb`
- `Basement_Corridor_A_Floor_Vent_Corner.glb`
- `Basement_Corridor_A_Floor_Vent_Grill_2m.glb`
- `Basement_Corridor_A_Floor_Vent_Grill_4m.glb`
- `Basement_Corridor_A_Floor_Vent_Grill_Corner.glb`
- `Basement_Corridor_A_Floor_Vent_Grill_Junction.glb`
- `Basement_Corridor_A_Floor_Vent_Grill_T_Junction.glb`
- `Basement_Corridor_A_Floor_Vent_Junction.glb`
- `Basement_Corridor_A_Floor_Vent_T_Junction.glb`
- `Basement_Corridor_A_Steel_Beam.glb`
- `Basement_Corridor_A_Steel_Coloumn.glb`
- `Basement_Corridor_A_Utility_Area.glb`
- `Basement_Corridor_A_Wall_2m_Door.glb`
- `Basement_Corridor_A_Wall_2m_Shaft.glb`
- `Basement_Corridor_A_Wall_2m.glb`
- `Basement_Corridor_A_Wall_4m.glb`
- `Basement_Corridor_A_Wall_Corner.glb`
- `Basement_Corridor_A_Wall_Double_sided_2m.glb`
- `Basement_Corridor_A_Wall_Double_Sided_4m.glb`
- `Basement_Corridor_A_Wall_Double_Sided_Broken_2m.glb`
- `Basement_Corridor_A_Wall_Double_Sided_Broken_3m.glb`
- `Basement_Corridor_A_Wall_Double_Sided_Broken_4m.glb`
- `Basement_Corridor_A_Wall_Shaft_Grill.glb`
- `Basement_Corridor_A_Wall_Top_2m.glb`
- `Basement_Corridor_A_Wall_Top_4m.glb`
- `Basement_Corridor_A_Wall_Top_Double_sided_2m.glb`
- `Basement_Corridor_A_Wall_Top_Double_Sided_4m.glb`

#### Set B (10)

- `Basement_Corridor_B_Ceiling_4x4m.glb`
- `Basement_Corridor_B_Floor_4x4m_Wood_Frame.glb`
- `Basement_Corridor_B_Floor_4x4m.glb`
- `Basement_Corridor_B_Floor_Hole_4x4m.glb`
- `Basement_Corridor_B_Wall_2m_Door.glb`
- `Basement_Corridor_B_Wall_2m.glb`
- `Basement_Corridor_B_Wall_4m.glb`
- `Basement_Corridor_B_Wall_Corner.glb`
- `Basement_Corridor_B_Wall_Double_sided_2m.glb`
- `Basement_Corridor_B_Wall_Double_Sided_4m.glb`

#### Set C (30)

- `Basement_Corridor_C_Ceiling_A_2x2m.glb`
- `Basement_Corridor_C_Ceiling_A_4x4m.glb`
- `Basement_Corridor_C_Ceiling_B_2x2m.glb`
- `Basement_Corridor_C_Ceiling_B_4x4m.glb`
- `Basement_Corridor_C_Flat_Wall_4x2m_Door.glb`
- `Basement_Corridor_C_Flat_Wall_4x2m.glb`
- `Basement_Corridor_C_Flat_Wall_4x4m_Door.glb`
- `Basement_Corridor_C_Flat_Wall_4x4m.glb`
- `Basement_Corridor_C_Flat_Wall_Corner.glb`
- `Basement_Corridor_C_Floor_2x2m.glb`
- `Basement_Corridor_C_Floor_4x4m.glb`
- `Basement_Corridor_C_Gate_2m.glb`
- `Basement_Corridor_C_Gate_4m.glb`
- `Basement_Corridor_C_Grating_Floor_2x2m.glb`
- `Basement_Corridor_C_Grating_Floor_4x4m.glb`
- `Basement_Corridor_C_Pillar_A.glb`
- `Basement_Corridor_C_Pillar_B.glb`
- `Basement_Corridor_C_Pillar_C.glb`
- `Basement_Corridor_C_Pillar_D.glb`
- `Basement_Corridor_C_Wall_2m_Cruved_Door.glb`
- `Basement_Corridor_C_Wall_2m_Cruved.glb`
- `Basement_Corridor_C_Wall_2m_Door.glb`
- `Basement_Corridor_C_Wall_2m_Shaft.glb`
- `Basement_Corridor_C_Wall_2m.glb`
- `Basement_Corridor_C_Wall_4m_Cruved_Door.glb`
- `Basement_Corridor_C_Wall_4m_Cruved.glb`
- `Basement_Corridor_C_Wall_4m_Door.glb`
- `Basement_Corridor_C_Wall_4m.glb`
- `Basement_Corridor_C_Wall_Corner.glb`
- `Basement_Corridor_C_Wall_Curveed_Corner.glb`

#### Set D (11)

- `Basement_Corridor_D_Ceiling_2x2m.glb`
- `Basement_Corridor_D_Ceiling_4x4m.glb`
- `Basement_Corridor_D_Floor_2x2m.glb`
- `Basement_Corridor_D_Floor_4x4m.glb`
- `Basement_Corridor_D_Pillar_A.glb`
- `Basement_Corridor_D_Pillar_B.glb`
- `Basement_Corridor_D_Wall_2m_Door.glb`
- `Basement_Corridor_D_Wall_2m.glb`
- `Basement_Corridor_D_Wall_4m_Door.glb`
- `Basement_Corridor_D_Wall_4m.glb`
- `Basement_Corridor_D_Wall_Corner.glb`

#### Set E (11)

- `Basement_Corridor_E_Ceiling_2x2m.glb`
- `Basement_Corridor_E_Ceiling_4x4m.glb`
- `Basement_Corridor_E_Floor_2x2m.glb`
- `Basement_Corridor_E_Floor_4x4m.glb`
- `Basement_Corridor_E_Pillar_A.glb`
- `Basement_Corridor_E_Pillar_B.glb`
- `Basement_Corridor_E_Wall_2m_Door.glb`
- `Basement_Corridor_E_Wall_2m.glb`
- `Basement_Corridor_E_Wall_4m_Door.glb`
- `Basement_Corridor_E_Wall_4m.glb`
- `Basement_Corridor_E_Wall_Corner.glb`

#### Set F (11)

- `Basement_Corridor_F_Ceiling_2x2m.glb`
- `Basement_Corridor_F_Ceiling_4x4m.glb`
- `Basement_Corridor_F_Floor_2x2m.glb`
- `Basement_Corridor_F_Floor_4x4m.glb`
- `Basement_Corridor_F_Pillar_A.glb`
- `Basement_Corridor_F_Pillar_B.glb`
- `Basement_Corridor_F_Wall_2m_Door.glb`
- `Basement_Corridor_F_Wall_2m.glb`
- `Basement_Corridor_F_Wall_4m_Door.glb`
- `Basement_Corridor_F_Wall_4m.glb`
- `Basement_Corridor_F_Wall_Corner.glb`

#### Set G (13)

- `Basement_Corridor_G_Ceiling_4x4m.glb`
- `Basement_Corridor_G_Floor_4x4m.glb`
- `Basement_Corridor_G_Pillar_A.glb`
- `Basement_Corridor_G_Pillar_B.glb`
- `Basement_Corridor_G_Wall_2m_Lower_Door.glb`
- `Basement_Corridor_G_Wall_2m_Middle_Door.glb`
- `Basement_Corridor_G_Wall_2m_Upper_Door.glb`
- `Basement_Corridor_G_Wall_2m.glb`
- `Basement_Corridor_G_Wall_4m_Lower_Door.glb`
- `Basement_Corridor_G_Wall_4m_Middle_Door.glb`
- `Basement_Corridor_G_Wall_4m_Upper_Door.glb`
- `Basement_Corridor_G_Wall_4m.glb`
- `Basement_Corridor_G_Wall_Corner.glb`


### Psychiatric ward kit (91)

#### Basement (18)

- `Psychiatric_Ward_Basement_Ceiling_4x4m.glb`
- `Psychiatric_Ward_Basement_Ceiling_4x4mPsychiatric_Ward_Basement_Ceiling_4x4mPsychiatric_Ward_Basement_Ceiling_4x4m.glb`
- `Psychiatric_Ward_Basement_Floor_2x2m.glb`
- `Psychiatric_Ward_Basement_Floor_4x4m.glb`
- `Psychiatric_Ward_Basement_Lower_Pillar.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_2m_Door.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_2m.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_4m_Door_A.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_4m_Door_B.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_4m.glb`
- `Psychiatric_Ward_Basement_Lower_Wall_Corner.glb`
- `Psychiatric_Ward_Basement_Upper_Pillar.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_2m_Door.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_2m.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_4m_Door_A.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_4m_Door_B.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_4m.glb`
- `Psychiatric_Ward_Basement_Upper_Wall_Corner.glb`

#### Guards Room Exterior (13)

- `Psychiatric_Ward_Guards_Room_Exterior_Ceiling_2x2m.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Ceiling_45_Degree.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Ceiling_4x4m.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Pillar_1.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Pillar_2.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_2m_Door.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_2m_Window.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_2m.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_45_Degree_Door.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_45_Degree_Window.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_45_Degree.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_4m.glb`
- `Psychiatric_Ward_Guards_Room_Exterior_Wall_Corner.glb`

#### Guards Room Interior (14)

- `Psychiatric_Ward_Guards_Room_Interior_Ceiling_2x2m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Ceiling_45_Degree.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Ceiling_4x4m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Floor_2x2m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Floor_45_Degree.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Floor_4x4m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_2m_Door.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_2m_Window.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_2m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_45_Degree_Door.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_45_Degree_Window.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_45_Degree.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_4m.glb`
- `Psychiatric_Ward_Guards_Room_Interior_Wall_Corner.glb`

#### Infirmary (45)

- `Psychiatric_Ward_Infirmary_Arched_Windows_A.glb`
- `Psychiatric_Ward_Infirmary_Arched_Windows_B.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_2x2m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_4x4m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Beam_2m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Beam_4m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Crowning_2m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Crowning_4m.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Crowning_Corner_A.glb`
- `Psychiatric_Ward_Infirmary_Ceiling_Crowning_Corner_B.glb`
- `Psychiatric_Ward_Infirmary_Floor_2x2m_A.glb`
- `Psychiatric_Ward_Infirmary_Floor_2x2m_B.glb`
- `Psychiatric_Ward_Infirmary_Floor_2x2m_C.glb`
- `Psychiatric_Ward_Infirmary_Floor_4x4m_A.glb`
- `Psychiatric_Ward_Infirmary_Floor_4x4m_B.glb`
- `Psychiatric_Ward_Infirmary_Floor_4x4m_C.glb`
- `Psychiatric_Ward_Infirmary_Floor_Patches_A.glb`
- `Psychiatric_Ward_Infirmary_Floor_Patches_B.glb`
- `Psychiatric_Ward_Infirmary_Pillar_A.glb`
- `Psychiatric_Ward_Infirmary_Pillar_B.glb`
- `Psychiatric_Ward_Infirmary_Pillar_C.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Door_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Door.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Window_A_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Window_A.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Window_B_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m_Window_B.glb`
- `Psychiatric_Ward_Infirmary_Wall_2m.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_A_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_A.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_B_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_B.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_C_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Door_C.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Window_Bare.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m_Window.glb`
- `Psychiatric_Ward_Infirmary_Wall_4m.glb`
- `Psychiatric_Ward_Infirmary_Wall_Corner.glb`
- `Psychiatric_Ward_Infirmary_Wall_Gate_A.glb`
- `Psychiatric_Ward_Infirmary_Wall_Gate_B.glb`
- `Psychiatric_Ward_Infirmary_Wall_Paper_1.glb`
- `Psychiatric_Ward_Infirmary_Wall_Paper_2.glb`
- `Psychiatric_Ward_Infirmary_Wall_Paper_3.glb`

#### Exterior (1)

- `Psychiatric_Ward_Exterior_Stairs.glb`


### Structural extras (15)

- `Beam_2m.glb`
- `Beam_4m.glb`
- `Beam_Corner.glb`
- `Drainage.glb`
- `Guard_Rail_2m.glb`
- `Guard_Rail_4m.glb`
- `Ladder.glb`
- `Pillar.glb`
- `Platform_2m_Opening_A.glb`
- `Platform_2m_Opening_B.glb`
- `Platform_2m.glb`
- `Platform_4m.glb`
- `Wall_Horozintal_Trim_2m.glb`
- `Wall_Horozintal_Trim_4m.glb`
- `Wall_Vertical_Trim.glb`

### Doors and intercoms (28)

- `door 6.glb`
- `Door_18_Blue.glb`
- `Door_18_Brown.glb`
- `Door_18_Silver.glb`
- `Door17.glb`
- `DoorIntercomPack04_01.glb`
- `DoorIntercomPack04_02.glb`
- `DoorIntercomPack04_03.glb`
- `Rollup Door 1.glb`
- `Rollup Door 10.glb`
- `Rollup Door 2.glb`
- `Rollup Door 3.glb`
- `Rollup Door 4.glb`
- `Rollup Door 5.glb`
- `Rollup Door 6.glb`
- `Rollup Door 7.glb`
- `Rollup Door 8.glb`
- `Rollup Door 9.glb`
- `Steel_Door_1_A.glb`
- `Steel_Door_1_B.glb`
- `Steel_Door_2_A.glb`
- `Steel_Door_2_B.glb`
- `Steel_Door_3_A.glb`
- `Steel_Door_3_B.glb`
- `Steel_Door_4_A.glb`
- `Steel_Door_4_B.glb`
- `Steel_Door_5.glb`
- `Wiremesh_Door.glb`

### Pipes (14)

- `Pipes_Corner.glb`
- `Pipes_Junction.glb`
- `Pipes_Straight_2m.glb`
- `Pipes_Straight_4m.glb`
- `Pipes_T_Junction.glb`
- `Pipes.glb`
- `Shaft_Pipes_A.glb`
- `Shaft_Pipes_B.glb`
- `Shaft_Pipes_C.glb`
- `Shaft_Pipes_D.glb`
- `Utility_Pipes_A.glb`
- `Utility_Pipes_B.glb`
- `Utility_Pipes_C.glb`
- `Utility_Pipes_D.glb`

### Equipment and props (13)

- `Arcade_Machine_1.glb`
- `Arcade_Machine_2.glb`
- `Arcade_Machine_3.glb`
- `Arcade_Machine_4.glb`
- `Console 1.glb`
- `Console 2.glb`
- `Electrical Panel.glb`
- `Laptop Closed.glb`
- `Laptop Opened.glb`
- `Laptop.glb`
- `Lever04.glb`
- `Lever05.glb`
- `RedTelephone.glb`

### Cables and wires (48)

- `Cables01_01.glb`
- `Cables01_02.glb`
- `Cables01_03.glb`
- `Cables01_04.glb`
- `Cables01_05.glb`
- `Cables01_06.glb`
- `Cables01_07.glb`
- `Cables01_08.glb`
- `Cables01_09.glb`
- `Cables01_10.glb`
- `Cables01_11.glb`
- `Cables01_12.glb`
- `Cables01_13.glb`
- `Cables01_Det01.glb`
- `ElectricalWires01_01.glb`
- `ElectricalWires01_02.glb`
- `ElectricalWires01_03.glb`
- `ElectricalWires01_04.glb`
- `ElectricalWires01_05.glb`
- `ElectricalWires01_06.glb`
- `ElectricalWires01_07.glb`
- `ElectricalWires01_08.glb`
- `ElectricalWires01_09.glb`
- `ElectricalWires01_10.glb`
- `ElectricalWires01_11.glb`
- `ElectricalWires01_12.glb`
- `ElectricalWires02_01.glb`
- `ElectricalWires02_02.glb`
- `ElectricalWires02_03.glb`
- `ElectricalWires02_04.glb`
- `ElectricalWires02_Curve01.glb`
- `ElectricalWires02_Curve02.glb`
- `ElectricalWires02_Support01.glb`
- `ElectricalWires02_Support02.glb`
- `ElectricalWires02_Support03.glb`
- `ElectricalWires03_01.glb`
- `ElectricalWires03_02.glb`
- `ElectricalWires03_03.glb`
- `ElectricalWires03_04.glb`
- `ElectricalWires03_05.glb`
- `ElectricalWires03_06.glb`
- `ElectricalWires03_07.glb`
- `ElectricalWires03_08.glb`
- `ElectricalWires03_09.glb`
- `ElectricalWires03_10.glb`
- `ElectricalWires03_11.glb`
- `ElectricalWires03_12.glb`
- `ElectricalWires03_13.glb`

### Keys and locks (13)

- `Bronze Key.glb`
- `Key01.glb`
- `Key02.glb`
- `Key03.glb`
- `Key04.glb`
- `Lock06.glb`
- `Lock07_Key01.glb`
- `Lock07_Key02.glb`
- `Lock07_Lock.glb`
- `Lock23.glb`
- `Lock27.glb`
- `Lockpick_03.glb`
- `Silver_Key.glb`

### Crates (8)

- `Breakable_Wooden_Crate.glb`
- `DamagedCrate02.glb`
- `DamagedCrate03.glb`
- `DamagedCrate04.glb`
- `DamagedCrate05.glb`
- `DamagedCrate06.glb`
- `DamagedCrate07.glb`
- `DamagedCrate08.glb`

### Health and batteries (11)

- `Battery03_01.glb`
- `Battery03_02.glb`
- `Battery03_03.glb`
- `Battery03_04.glb`
- `Battery03_05.glb`
- `Battery07.glb`
- `FirstAidKit01.glb`
- `FirstAidKit02.glb`
- `FirstAidKit03.glb`
- `FirstAidKit04.glb`
- `FirstAidKit05.glb`

### Weapons (4)

- `Crowbar.glb`
- `gravitygun.glb`
- `Scifi_Handgun_01.glb`
- `test_pistol.glb`

### Character (1)

- `Future_Soldier_02.glb`

### Unclassified (1)

The filename alone is not descriptive enough to identify this asset; inspect its preview before use.

- `low.glb`

## Animations (16)

- `firing rifle.glb`
- `hit reaction.glb`
- `reloading.glb`
- `rifle aiming idle.glb`
- `rifle jump.glb`
- `rifle run.glb`
- `run backwards.glb`
- `strafe (2).glb`
- `strafe left.glb`
- `strafe right.glb`
- `strafe.glb`
- `toss grenade.glb`
- `turn left.glb`
- `turning right 45 degrees.glb`
- `walking backwards.glb`
- `walking.glb`

## Levels (5)

- `basementLevel.json`
- `interaction_test_blockout.json`
- `interaction_test.json`
- `room01.json`
- `sixRoomTest.json`

## Prefabs (1)

- `DoorFrame&Door.json`

## UI Files

- `Game/Content/UI/Fonts/AurelDeco-Regular.ttf`
- `Game/Content/UI/Fonts/AurelDeco-Specimen.html`
- `Game/Content/UI/Icons/ArchiveKey.png`
- `Game/Content/UI/Icons/Bullets.png`
- `Game/Content/UI/Icons/Crank.png`
- `Game/Content/UI/Icons/Fuse.png`
- `Game/Content/UI/Icons/GunPowder.png`
- `Game/Content/UI/Icons/InkRibbon.png`
- `Game/Content/UI/Icons/MasterKey.png`
- `Game/Content/UI/Icons/RustedKey.png`
- `Game/Content/UI/Icons/Scrap.png`
- `Game/Content/UI/Icons/ServiceKey.png`
- `Game/Content/UI/Inventory/inventory.rcss`
- `Game/Content/UI/Inventory/inventory.rml`

## Filename Note

There is both a normal psychiatric-ward basement ceiling filename and a second filename containing the same stem repeated three times. This inventory does not establish whether their mesh contents are duplicates. Do not rename or delete either without checking level and prefab references.

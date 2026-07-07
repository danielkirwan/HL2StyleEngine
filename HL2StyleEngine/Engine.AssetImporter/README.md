# HS2 Asset Importer

Standalone Windows tool for converting source art FBX files into GLB files for the engine content folders.
Editor integration: the standalone `HS2Editor` app launches this importer rather than duplicating the converter in its first pass. `HS2Project.json` stores useful paths such as `blender.exe`, the importer project, the game project, and preferred content roots under `Game/Content`.

The UI now has two tabs:

- `Models`: static/world/viewmodel mesh import with material and texture matching.
- `Animations`: animation FBX import that preserves armatures, skins, actions, and clips where Blender exposes them during GLB export.

## Models Tab

Single model conversion:

1. Choose a source folder that contains an `.fbx` file and any textures/material files that belong with it.
2. Choose a destination folder, normally `Game/Content/Models/ViewModels` or another folder under `Game/Content/Models`.
3. Choose an output name such as `test_pistol`.
4. Browse to `blender.exe`, or set `HS2_BLENDER_EXE` to the Blender executable path.
5. Convert. The output is written as `<name>.glb`.

Batch model conversion:

1. Choose a source folder that contains multiple `.fbx` files.
2. Enable `Convert all FBX in source`.
3. Convert. Each FBX is exported as its own `<fbx-file-name>.glb` in the destination folder.

The model converter uses Blender in background mode. It searches the source folder for common texture names such as albedo/basecolor/diffuse, normal, emission, metallic, metalness, and roughness, rebuilds material nodes, then exports a binary GLB.

If the selected folder is named `FBX`, the converter searches the parent asset folder for textures so sibling folders such as `tex` or `Textures` are included.

## Animations Tab

Single animation conversion:

1. Choose a source folder that contains a Mixamo/Unity animation `.fbx` file.
2. Choose a destination folder, normally `Game/Content/Animations`.
3. Choose an output clip name such as `weapon_idle`, `pistol_walk`, or `crowbar_swing`.
4. Browse to `blender.exe`, or set `HS2_BLENDER_EXE` to the Blender executable path.
5. Convert. The output is written as `<name>.glb`.

Batch animation conversion:

1. Choose a source folder containing multiple animation `.fbx` files.
2. Enable `Convert all animation FBX in source`.
3. Convert. Each FBX is exported as its own `<fbx-file-name>.glb` in `Game/Content/Animations` or your selected animation folder.

The animation export path does not rebuild material nodes. It imports the FBX into Blender, keeps armatures/actions, gives generic imported actions the clip name, and exports GLB with supported animation/skinning options such as `export_animations`, `export_skins`, `export_all_actions`, and frame-range sampling when the installed Blender version exposes them.

## Material And Texture Matching

The model Blender export script rebuilds material nodes before exporting. For each imported material it uses the material name and FBX file name as matching hints. This supports folders where multiple FBX files share a texture root but require different material groups.

Example: the damaged crate set uses FBX files such as `DamagedCrate02.fbx` and `DamagedCrate05.fbx`, with material names/textures such as `DamagedCrates1to4` and `DamagedCrates5to7`. Batch conversion exports one GLB per crate while matching each material to the correct texture set. If an FBX contains an embedded material name that does not match the file-number pattern, the importer currently trusts the embedded material name; the checked `DamagedCrate08.glb` is valid and textured, but uses the source material name `DamagedCrates1to4`.

The conversion log prints the matched albedo, normal, metallic, and roughness paths for each material so bad matches can be spotted quickly.

## Command Line

Single model conversion:

```powershell
dotnet run --project Engine.AssetImporter -- --source "C:\Path\To\Asset" --destination "Game\Content\Models\ViewModels" --name test_pistol
```

Batch model conversion:

```powershell
dotnet run --project Engine.AssetImporter -- --source "C:\Path\To\Asset" --destination "Game\Content\Models\ViewModels" --all
```

Single animation conversion:

```powershell
dotnet run --project Engine.AssetImporter -- --source "C:\Path\To\Animation" --destination "Game\Content\Animations" --name pistol_idle --mode animation
```

Batch animation conversion:

```powershell
dotnet run --project Engine.AssetImporter -- --source "C:\Path\To\Animations" --destination "Game\Content\Animations" --all --animations
```


## Unsupported FBX Versions

Blender 5.x cannot import older FBX files such as version `6100`; it requires FBX `7100` or later. The importer now preflights FBX versions and, in batch mode, skips unsupported files while still completing valid conversions.

If a file is skipped with `FBX version 6100`, re-export it from Unity, Autodesk FBX Converter, Blender through an older compatible bridge, or the original DCC tool as a newer FBX before importing again. In the Basic Shooter Pack test, `X Bot.fbx` was the old unsupported file while the 16 animation clips converted successfully.

## Runtime Limitation

The game can load and draw GLB mesh geometry through the first-pass renderer path. Embedded base-color textures, imported normals, and metallic/roughness texture data are supported by the current model renderer.

Animation GLBs can now be produced by the importer and copied by `Game.csproj`, but the game runtime still does not consume glTF `skins` or `animations`. Runtime playback still needs skeleton loading, inverse bind matrices, joint weights, animation samplers/channels, an animator, and skinned mesh rendering.

Normal-map perturbation, emission, environment reflections, skeletal skinning, animation clips, and fuller PBR behavior are still pending.

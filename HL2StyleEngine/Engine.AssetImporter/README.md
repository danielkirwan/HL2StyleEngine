# HS2 Asset Importer

Standalone Windows tool for importing source art folders and converting FBX models into GLB files that the engine can load.

## Current Flow

1. Choose a source folder that contains an `.fbx` file and any textures/material files that belong with it.
2. Choose a destination folder, normally `Game/Content/Models/ViewModels`.
3. Choose an output name such as `test_pistol`.
4. Browse to `blender.exe`, or set `HS2_BLENDER_EXE` to the Blender executable path.
5. Convert. The output is written as `<name>.glb`.

The converter uses Blender in background mode. It searches the source folder for common texture names such as albedo/basecolor/diffuse, normal, emission, and metallic, then asks Blender to export a binary GLB.

If the selected folder is named `FBX`, the converter searches the parent asset folder for textures so sibling folders such as `tex` are included.

## Current Runtime Limitation

The game can load and draw GLB mesh geometry through the first-pass renderer path, but the runtime model shader is still color-only. Texture/PBR rendering is the next renderer slice.

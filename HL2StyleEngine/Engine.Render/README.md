# World Rendering

`BasicWorldRenderer` draws primitives and textured GLB model parts. `TexturedModel.hlsl` and its tracked `TexturedModelVS.cso` / `TexturedModelPS.cso` files must be updated together; the normal .NET build copies compiled shaders but does not compile HLSL.

## Point Lighting (2026-09-08)

Textured rendering accepts up to 32 nearby `WorldPointLight` entries through `UpdatePointLights`. `LevelLighting` in Engine.Editor submits enabled level lights for both HS2Editor and the game. `LevelFile.UsePointLights` defaults to false for existing levels; the editor toolbar can enable it. `sixRoomTest.json` enables lighting for its indoor test spaces.

The lighting constant buffer at b2 contains one float4 count header and 32 position/range plus colour/intensity pairs. The object buffer at b1 appends the inverse-transpose normal matrix after Model, Color and Material. Keep C# and HLSL layouts aligned. Basic primitive colours retain their existing unlit rendering.

Lighting uses the existing ambient/directional pass plus smooth-range point contributions. There are no point-light shadows, groups, switch state, emission maps or spotlights yet. Authored light positions follow editor hierarchy transforms; animated runtime light attachments are not implemented.

To rebuild shaders with an installed Windows SDK, run its x64 `fxc.exe` from the project root:

```powershell
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T vs_5_0 /E VSMain /Fo Engine.Render/Shaders/TexturedModelVS.cso Engine.Render/Shaders/TexturedModel.hlsl
& '<Windows SDK bin>/x64/fxc.exe' /nologo /T ps_5_0 /E PSMain /Fo Engine.Render/Shaders/TexturedModelPS.cso Engine.Render/Shaders/TexturedModel.hlsl
```

Both shaders were compiled with SDK 10.0.26100.0 for the initial lighting update.

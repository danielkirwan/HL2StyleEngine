# Lighting Authoring And Switches

Lighting stability follow-up (2026-10-25): `LaunchSixRoomLightingStabilityTest.bat` adds fourteen fixtures, zero ambient/directional, fixture-derived bounce/probes, resident room captures and a near-spawn E-operated power switch. Shared **Indirect Lighting > Lighting States** authors explicit on/off presets; these now supply matching caches instead of reusing stale glow. [State authoring, recooking, tests and limits](LIGHTING_STABILITY_GUIDE.md). Notes below describe the earlier invariant-only fixtures.

Latest (2026-10-25): `LaunchSixRoomPointLightTest.bat` opens a separate fixture-only six-room PBR copy: eight visible ceiling lights, zero ambient/directional fill, flashlight initially off, and six freshly cooked room reflections. `Content/Prefabs/CeilingPointLight.prefab.json` is available in the Prefabs tab. See [fixture-only workflow, tests and remaining lighting work](POINT_LIGHT_TEST_GUIDE.md). The entries below describe earlier milestones; authored captures, automatic room transitions and bounded residency are now implemented, while switched capture presets remain pending.

Direct PBR added 2026-10-22 as an explicit per-level option, not a global relighting. **Toolbar > Lighting > Lighting Model** selects Legacy/PBR; missing values stay Legacy. Original ranges, cones, intensities, switches, flicker and shadows are retained. Unit C (2026-10-23) adds **Specular Environment: Off / Synthetic Test Studio**; only `LaunchPbrMaterialTest.bat` opts in. It is a distant test rig, not actual room geometry or a switched room capture. Read [PBR_MATERIAL_GUIDE.md](PBR_MATERIAL_GUIDE.md) and [REFLECTION_LIGHTING_GUIDE.md](REFLECTION_LIGHTING_GUIDE.md) for diagnostics, rules, ~60 FPS local results and remaining room-capture work.

Implemented 2026-10-06; emissive materials, fixture links, opt-in static indirect baking and movable-object probes added 2026-10-22 in HS2Editor, the F2 editor and gameplay. Existing level files are not automatically relit or given switches. The bake/probe workflow and separate test launchers are in [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md).

For a ready-made low-light test, run root `LaunchSixRoomFlashlightTest.bat`. It opens the separate `sixRoomFlashlightTest.json`, with all scene lights removed, directional strength zero and faint ambient strength **0.025**. Press **F** to switch the initially-off flashlight on/off. Red/green puzzle indicators stay visible but do not illuminate surrounding walls. The original lit six-room test remains available via `LaunchSixRoomTest.bat`. Adjust Toolbar > Lighting > Ambient to tune visibility without adding powered fixtures. See [SIX_ROOM_TEST.md](SIX_ROOM_TEST.md).

## Make A Switched Room

1. Enable **Scene Point Lights** in the Toolbar. Use **Add Light** to place PointLight entities near your fixture models. The visible lamp model and its light source are separate objects.
2. Select each light. Set **Enabled** for its starting state, colour, intensity and range. Give the room's lights the same **Light Group**, for example `StoreRoom`. Group matching ignores case and surrounding spaces.
3. For directed lamps, enable **Spotlight** and set **Cone Angle**. The light points along its local -Z axis. Enable **Cast Shadows** for selected important lights; the global Shadows setting must also be on.
4. Place a switch model or box. Select it and choose **Interaction > Add Light Switch**. Enter/select `StoreRoom` in **Light Group**. Alternatively, use **Targets > Target Entity > Add Target** to choose individual lights. The picker shows names but saves stable entity IDs. Group and explicit targets are combined without duplicates. Targets can be removed individually or cleared.
5. Leave **Required States** empty for an ordinary switch. For a powered circuit, add the existing puzzle state IDs required before the switch can be used. This gates interaction, not automatic electrical shutoff when a prerequisite later becomes false.
6. Save Level and launch Play. Aim at the switch within the normal interaction range and press **E** or the controller's existing interact binding. If any target is on, all targets turn off; otherwise all turn on. The switch remains reusable. Blank Prompt/Success Message fields use automatic on/off text. Locked Prompt overrides the default power-unavailable message.

Visual-only Prop switches receive a non-solid interaction box sized to the prop. Use a static RigidBody with your chosen collider if the switch also needs solid collision. Existing colliders are retained. A light switch does not consume inventory items or use Rewards/one-shot solved-state logic.

Two switches targeting the same lights share their actual state. Lights with zero intensity or a disabled level-wide Scene Point Lights setting remain dark regardless of the switch. Unlit blockout primitives do not receive lighting, so use textured models to judge the result.

## Flicker And Preview

**Flicker Amount** defaults to zero. Values up to one dim a light by a deterministic, ID-seeded waveform; intensity never exceeds the authored value. **Flicker Speed (Hz)** controls the base waveform from 0.1 to 20; a second frequency adds variation. Separate fixtures do not all flicker in phase. Start with a small amount and slow speed for a subtle damaged fixture.

HS2Editor and F2 preview authored enabled states and animated flicker. Gameplay uses the same light calculation plus player-created on/off overrides. Gameplay flicker time pauses with gameplay menus and is restored with saved play time. The editor's preview clock is independent. Linked fixture emission now follows this same state and flicker. Switch animations and sound hooks remain pending.

## Emissive Fixtures (2026-10-22)

Run root `LaunchSixRoomEmissionTest.bat` for a separate copy of the dim six-room level. A console and arcade cabinet are ahead of spawn. Aim at the lever to their right and press **E** to turn both screen glow and their paired room light off/on. **F** still toggles the torch. Original lit/flashlight level files and their launchers are unchanged. Load `sixRoomEmissionTest.json` in HS2Editor to adjust the test.

1. Select a model in the scene and open **Inspector > Emission**. Imported GLB emissive colour/texture/strength work automatically. **Emission Intensity** multiplies the imported emission; default 1 preserves it and 0 disables it.
2. To add or recolour emission, enable **Override Imported Emission**, choose a linear **Emission Colour**, and adjust intensity (0-100). **Model Part** chooses a loaded named part or All Parts. The imported emissive texture remains the mask, so a screen texture does not make the whole housing glow. Without an emissive map the chosen part emits uniformly. Unmatched parts keep their imported emission, also scaled by intensity.
3. Choose **Linked Light** to bind this fixture to an authored point/spot light. The picker shows names but stores the stable light ID. Off, missing/deleted, zero-intensity/range or globally disabled scene lights suppress linked emission. Enabled fixtures follow that light's flicker, independently of the shadow-slot budget. Choose None for always-on emission.
4. Put a **LightSwitch** interaction on a separate switch and target that light/group using the existing workflow above. A model's emission link is not itself an interaction, and does not create a new light. Include the fixture and light in the same prefab hierarchy for independent repeatable instances.
5. Save Level. Settings round-trip through editable JSON, RE:Dox caches and prefabs. Internal emission-light IDs remap on placement/apply/revert; external links remain external. Runtime switch state remains separate from authored defaults. Cached/shared model resources are never modified by an individual fixture override.

Emission is added in linear HDR before exposure/tone mapping. It makes the surface visible, not the surrounding room: use a paired point/spot light for illumination. Static bounce/probes use a separate fixed-light bake and do not integrate emissive surfaces. Bloom, volumetrics and light attachment animation remain unimplemented. Primitive blockout objects retain their existing unlit appearance; these controls apply to assigned model parts. The shared material pipeline supports embedded GLB images and float UV channels; emissive UV channel selection and `KHR_texture_transform` are preserved. Existing broader glTF limitations (for example external images/custom samplers) are not expanded here. See the [glTF material specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#materials) and [emissive-strength extension](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_emissive_strength/README.md).

Model caches are now version 2. Old v1 model caches safely fall back to GLB parsing; rerun `CookAssets.bat` or Importer > Compression to generate v2 snapshots. Emissive images use the existing BC7/sRGB mip and LZ4 cache pipeline with shared GPU ownership. The current 376 model GLBs have been recooked. Light gizmos remain editor-only and no longer render as opaque boxes or cast shadows during play.

## Prefabs And Persistence

For a self-contained switch/fixture prefab, include both entities under its root and use explicit light targets. Internal light IDs are remapped when placing another instance and translated back to source IDs when applying. Revert preserves IDs of surviving source entities, so external references remain valid. External targets are left unchanged.

Group names are level-wide and deliberately are not renamed per prefab instance. Two prefab instances using `StoreRoom` will control the same group. Use explicit targets for independent instances, or edit the group names after placement.

Player saves contain optional `LightStates` entries with `EntityId` and `Enabled`, separate from authored `LightEnabled` defaults. Existing unversioned/version-1 saves without the field use authored defaults. Reloading runtime entities in the same level retains overrides; changing levels or resetting progress clears them. The save loader restores overrides only when its recorded level filename matches the active level. F2 and HS2Editor save authored defaults, not the player's switch state.

Override records can retain IDs not currently present in the entity list. This prepares for later streaming, but room unloading/reloading and multi-level campaign-state persistence are not implemented by this feature.

## Shadow Budget

The renderer considers up to 32 point/spot lights; at most two spots and one point light cast shadows simultaneously. Point shadows use six faces, so that is eight 1024-square depth slices total. The flashlight has high priority and takes a spot slot. Other lights still illuminate without shadows and can illuminate through walls.

Each face conservatively culls casters outside its frustum. Unchanged depth maps are reused. Moving/rotating a door, hiding crate parts, changing models, moving a light, changing its range/cone, or removing casters invalidates affected maps. Colour/intensity/flicker changes alone do not require new depth. Disabling lights releases their cached slots. This is shadow-pass culling, not room streaming or camera-frustum culling of the main scene.

Limits still include opaque shadow geometry, no directional shadows, no volumetrics and no animated runtime attachment of light entities. Static indirect/probes have the separate bounded workflow below. A reused depth map still costs lighting/PCF work in the world shader. The 2,048-caster bound remains; exceeding it can omit shadows.

## Remaining Lighting Requirements (Updated 2026-10-22)

Items 1-4 now have first-pass implementations; the limits below distinguish them from remaining design work. Keep the current torch, dynamic shadows, switch states, streaming and accepted PC performance as the reference.

1. **Bake/static metadata and workflow: first pass complete.** Inspector **Static Lighting** and **Fixed Light: Bake Indirect** opt in eligible architecture and permanent lights. Toolbar **Indirect Lighting** saves/bakes with progress, cancellation, errors, stale fallback and a non-destructive preview toggle. Direct light stays realtime; this is not a full realtime/baked/mixed mode system. Dependencies currently invalidate conservatively by whole-level content.
2. **Lightmap UVs and offline baking: first pass complete.** Separate padded per-triangle charts, one diffuse bounce, one bounded linear RGBA16F atlas, lossless LZ4 storage and HDR shader sampling are implemented. Source UVs stay unchanged. This is not optimized multi-room atlas packing, safe mip generation, multi-bounce GI or emissive-source baking. See [BAKED_LIGHTING_GUIDE.md](BAKED_LIGHTING_GUIDE.md).
3. **Light probes for movable objects: first pass complete.** Enable **Bake Light Probes** and choose grid spacing in Toolbar > Indirect Lighting. Six-direction diffuse samples are interpolated for moving textured models, with fixed-wall visibility rejection and cached stationary queries. Full/Direct Only/Indirect Only, **Preview Light Probes**, **Show Probe Markers** and validity/cost indicators support comparisons. `LaunchLightProbeTest.bat` has 196 probes and carry/break crates. One position per model, fixed occluders and level-resident data are current limits; adaptive/manual placement, richer large-object sampling and area streaming remain future work.
4. **Emissive materials and switch integration: first pass complete 2026-10-22.** Imported maps/colour/strength, per-instance editor controls, optional part overrides, light-ID links and matching flicker are implemented and tested. A glowing surface does not automatically illuminate neighbours. Bloom remains separate and unimplemented.
5. **Correct changing-light behaviour.** The torch and moving-door shadows remain realtime. Do not permanently bake a movable door's closed shadow. Switchable lights must not leave their baked indirect contribution glowing when switched off: initially keep them realtime-only, or explicitly implement and budget baked on/off scenarios. Flickering lights need the same deliberate treatment.
6. **Resource ownership and validation: bounded first pass complete.** Bake outputs have version/content hashes, stable entity IDs, strict validation, shared soft upload budgeting and fence-safe retirement. The single atlas remains resident for the level; unused per-instance buffers retire after 120 frames. It is additional to the model streaming budget. Real GPU/editor comparisons, a sealed-wall leak fixture and moving/stress profiles pass. Multi-room atlas streaming and longer soak testing remain.

Run `LaunchIndirectLightingTest.bat` for the static-only room or `LaunchLightProbeTest.bat` for movable-object bounce. Next: explicit switchable-light scenarios and larger-area/longer stress validation. Reflection probes and volumetrics follow only after these foundations meet the frame/memory budgets. A C++ rewrite is not a prerequisite for any of this work.

Verification and measured costs: [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md). Remaining engine stages: [ENGINE_TECHNOLOGY_ROADMAP.md](ENGINE_TECHNOLOGY_ROADMAP.md).

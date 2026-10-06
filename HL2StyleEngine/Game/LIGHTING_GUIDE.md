# Lighting Authoring And Switches

Implemented 2026-10-06 in HS2Editor, the F2 editor and gameplay. Existing level files are not automatically relit or given switches.

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

HS2Editor and F2 preview authored enabled states and animated flicker. Gameplay uses the same light calculation plus player-created on/off overrides. Gameplay flicker time pauses with gameplay menus and is restored with saved play time. The editor's preview clock is independent. Switching off a light does not change the visible bulb material: emissive maps, switch animations and sound hooks are not implemented yet.

## Prefabs And Persistence

For a self-contained switch/fixture prefab, include both entities under its root and use explicit light targets. Internal light IDs are remapped when placing another instance and translated back to source IDs when applying. Revert preserves IDs of surviving source entities, so external references remain valid. External targets are left unchanged.

Group names are level-wide and deliberately are not renamed per prefab instance. Two prefab instances using `StoreRoom` will control the same group. Use explicit targets for independent instances, or edit the group names after placement.

Player saves contain optional `LightStates` entries with `EntityId` and `Enabled`, separate from authored `LightEnabled` defaults. Existing unversioned/version-1 saves without the field use authored defaults. Reloading runtime entities in the same level retains overrides; changing levels or resetting progress clears them. The save loader restores overrides only when its recorded level filename matches the active level. F2 and HS2Editor save authored defaults, not the player's switch state.

Override records can retain IDs not currently present in the entity list. This prepares for later streaming, but room unloading/reloading and multi-level campaign-state persistence are not implemented by this feature.

## Shadow Budget

The renderer considers up to 32 point/spot lights; at most two spots and one point light cast shadows simultaneously. Point shadows use six faces, so that is eight 1024-square depth slices total. The flashlight has high priority and takes a spot slot. Other lights still illuminate without shadows and can illuminate through walls.

Each face conservatively culls casters outside its frustum. Unchanged depth maps are reused. Moving/rotating a door, hiding crate parts, changing models, moving a light, changing its range/cone, or removing casters invalidates affected maps. Colour/intensity/flicker changes alone do not require new depth. Disabling lights releases their cached slots. This is shadow-pass culling, not room streaming or camera-frustum culling of the main scene.

Limits still include opaque shadow geometry, no directional shadows, no baked indirect light/probes, no emission/volumetrics and no animated runtime attachment of light entities. A reused depth map still costs lighting/PCF work in the world shader. The 2,048-caster bound remains; exceeding it can omit shadows.

Verification and measured costs: [ENGINE_UPGRADE_VALIDATION.md](ENGINE_UPGRADE_VALIDATION.md). Remaining engine stages: [ENGINE_TECHNOLOGY_ROADMAP.md](ENGINE_TECHNOLOGY_ROADMAP.md).

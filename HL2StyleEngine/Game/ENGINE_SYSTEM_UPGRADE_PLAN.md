# Prioritised Engine Systems Upgrade Plan

Updated: 5 November 2026. Status: planning only. This is the implementation order for the systems identified in [Engine Systems Research](ENGINE_SYSTEMS_RESEARCH.md), under the current [Game Style Objective](GAME_STYLE_OBJECTIVE.md).

## Mandatory Starting Gate: Finish Lighting First

Current acceptance evidence (2026-11-04): [Lighting Acceptance Handover](LIGHTING_ACCEPTANCE_HANDOVER.md). **Gate L pending; Gate B pending; new system upgrades blocked.** The handover separates automated lighting checks, visual/native review and retained frame-pacing failures. Passing lighting images or an average near 60 FPS does not clear the baseline gate.

Memory follow-up (2026-11-05): [controlled investigation](RELOAD_MEMORY_INVESTIGATION.md)
reproduces the 128 MiB guard failure and narrows it toward temporary managed
allocation/heap fragmentation. This is diagnosis, not clearance of either gate.

**The existing lighting implementation must be finished and accepted before any work starts on the new engine system upgrades.**

That includes audio/backend experiments, shared-system refactors, new rule systems, editor extensions, navigation and AI prototypes. This document is planning; it does not start those milestones or declare the lighting work complete. Complete lighting through its existing implementation track. Do not add new lighting features to this upgrade plan.

Record completion against the current lighting requirements and their existing visual, authoring, cache and stability checks. Do not infer whole-system completion from one successful fixture or an older status entry. Resolve or explicitly account for outstanding acceptance items in the existing track before marking this gate complete.

| Gate | Required evidence | Current planning status |
| --- | --- | --- |
| L: Existing lighting complete | Required lighting behavior is finished; current fixtures and normal play are reviewed; remaining issues and acceptance are recorded in the lighting guides. | Not cleared by this planning task. |
| B: Existing baseline stable | Remaining frame-pacing investigation and the existing gameplay/memory acceptance gates are closed for the normal full-detail path. | Open in the current roadmap. |
| Start new systems | Gates L and B both complete; capture the accepted baseline and begin Priority 1 below. | Pending both gates. |

Use the current [lighting authoring guide](LIGHTING_GUIDE.md), [lighting-state stability](LIGHTING_STABILITY_GUIDE.md), [shadow stability](SHADOW_STABILITY_GUIDE.md), [floor correction](FLOOR_LIGHTING_GUIDE.md), [room transitions](ROOM_TRANSITION_GUIDE.md) and [lighting memory evidence](LIGHTING_MEMORY_GUIDE.md) to establish lighting completion. Existing checks cover different parts of the system; their historical pass records are not a new blanket acceptance claim.

Keep the [frame-pacing investigation](FRAME_PACING_GUIDE.md) separate and visible. Do not change visual quality, solver behavior, fixed timestep or acceptance limits to make it pass. Adaptive streaming can remain experimental; completing this plan's starting gate does not require promoting it.

## Constraints For Every Upgrade

Future content must follow the adopted [immersive-sim design philosophy](IMMERSIVE_SIM_DESIGN_PHILOSOPHY.md)
(5 November 2026): connected exploration and materially different objective
solutions, with consistent rules and recoverable failures. This is a design
requirement, not permission to bypass Gates L/B or start the proposed prototype.

- **C# remains the runtime structure:** entities, rules, AI decisions, editor data and persistence stay managed. A focused native audio/acoustics/navigation library may sit behind a small C ABI and C# service when demonstrated useful.
- **Lighting is frozen after Gate L:** preserve the accepted rendering behavior, settings, shaders, bake/reflection formats and reference scenes. These upgrades do not introduce lighting algorithms or lighting-driven stealth.
- **Existing physics is unchanged:** preserve the solver, collision geometry, mass/friction/restitution, gravity, fixed-step behavior and accepted manipulation. Read available results through game-side adapters. Do not modify `Engine.Physics` to support this backlog; defer any feedback feature that requires new solver/contact APIs.
- Add the minimum data and authoring needed for each playable result. Extend the current entity/script/prefab structures instead of beginning with an ECS, editor or engine rewrite.
- Persist new meaningful state in the milestone that introduces it. Validate references, lifetime and compatibility at each stage; do not postpone them all to final polish.
- Keep original levels, lighting references, game saves and the other project's audio sources intact. Use separate fixtures and isolated test saves.

## Implementation Order

The order below is sequential after both starting gates. Priority is based on dependencies and a demonstrable benefit to this engine, rather than the size or reputation of the reference engine.

| Priority | Implement | Why here | Learn from | Result required before moving on |
| --- | --- | --- | --- | --- |
| 1 | Minimal C# system contracts and validated asset definitions | Establish ownership for the first features without a broad refactor. | O3DE audio requests; existing local script/entity contracts | Old levels and saves still load; new definitions validate and have explicit lifetimes. |
| 2 | Runtime audio and cue authoring | Spatial playback is an existing next milestone and makes subsequent interactions tangible. | Godot AudioServer/AudioStreamPlayer3D/Randomizer; O3DE; evaluate SoLoud | Reliable positional cues, buses, variation, audition and cleanup. |
| 3 | Surface-based action and impact feedback | Gives immediate value to implemented physics without changing simulation. | ezEngine SurfaceResource; Arx GameSound | Material-specific footsteps, hits and supported impacts, with no contact spam. |
| 4 | Reusable interactions, alternative conditions and persistent consequences | Turns individual puzzle scripts into composable immersive-sim rules. | The Dark Mod Response; Arx ScriptEvent; OpenMW ContainerStore | One obstacle supports distinct solutions, consumes items correctly and survives save/load. |
| 5 | Targeted prefab/art authoring improvements and one room kit | Prove the BioShock-inspired visual language with the gameplay systems now available. | ezEngine prefab parameters/AssetCurator; O3DE prefab propagation | A reusable original room/machine/sign kit works in the current editor and renderer. |
| 6 | Room/door acoustics and semantic hearing propagation | Establish consistent sound paths before an NPC depends on them. | The Dark Mod SndProp; Godot routing; optional Steam Audio experiment | Door changes affect audio/hearing appropriately, independently of camera visibility or user volume. |
| 7 | One NPC with navigation and investigation | Integrate existing signals and rules in a bounded gameplay example. | OpenMW AiPackage; Recast/Detour; CRYENGINE spatial-query documentation | Patrol, investigate, search and return work without hidden knowledge of the player. |
| 8 | Complete and harden one playable slice | Assess the combined design before expanding the feature set. | OpenMW lifecycle/persistence; existing local stability tools | Original art, alternative solutions, sound and one NPC work together within accepted budgets. |

### Priority 1: Small C# Contracts, Not A Framework Rewrite

**Implement:** define sound cue IDs, emitter/voice handles, surface profile IDs, semantic noise events and versioned asset definitions only as needed for Priority 2 and 3. Keep noise events meaningful even if no audio device exists. Add reference validation and a bounded command/callback path for backend work. Document ownership and teardown.

**Local integration:** [Entity.cs](../Engine.Runtime/Entities/Entity.cs), [ScriptRegistry.cs](../Engine.Runtime/Entities/ScriptRegistry.cs), [IComponent.cs](../Engine.Runtime/Entities/Interfaces/IComponent.cs) and [LevelTypes.cs](../Engine.Editor/Editor/LevelTypes.cs). Put reusable runtime contracts in `Engine.Runtime`; game-specific cue/rule definitions belong in `Game`. Follow the existing [native bridge pattern](../Native/HS2RmlUiBridge/README.md) where a native boundary is warranted. New folder/module names are implementation choices, not existing components.

**Read first:** O3DE [AudioSystem.h](https://github.com/o3de/o3de/blob/19c096e51f638d7be9e231d551ca3f688af34bf2/Gems/AudioSystem/Code/Source/Engine/AudioSystem.h) and [AudioSystem.cpp](https://github.com/o3de/o3de/blob/19c096e51f638d7be9e231d551ca3f688af34bf2/Gems/AudioSystem/Code/Source/Engine/AudioSystem.cpp): `PushRequest`, `PushRequests`, `ExternalUpdate` and the distinct blocking path. Learn how thread ownership and completion are separated; implement a proportionate C# service.

**Acceptance:** missing references fail clearly; commands cannot grow unbounded; stale handles cannot affect a reused voice; callbacks cannot mutate gameplay from the audio thread. Existing entity/script behavior, levels and saves remain compatible. Avoid migrating unrelated code merely to use the new contracts.

### Priority 2: Playback, Mixing And Editor Audition

**Implement:** a C# `IAudioService`, listener/emitter updates, one-shot and looping cues, adjustable buses, weighted variants with repeat prevention, basic distance attenuation, voice/concurrency limits, pause/resume and level teardown. Add cue audition and missing-asset feedback using the existing editor. Treat music/ambience streaming separately from short positional effects.

Evaluate one backend with a small representative prototype before committing to a dependency. **SoLoud is a candidate**, not a selected or integrated package. Verify .NET 10/x64 binding, native deployment, output-device recovery, disposal and sample/stream ownership. Follow the installed [audio authoring workflow](AUDIO_WORKFLOW.md) for original test sounds.

**Read first:** Godot [AudioStreamPlayer3D](https://github.com/godotengine/godot/blob/fefec82d74b46f5e6cc31f373071058f80a5cbee/scene/3d/audio_stream_player_3d.cpp), [AudioServer](https://github.com/godotengine/godot/blob/fefec82d74b46f5e6cc31f373071058f80a5cbee/servers/audio/audio_server.cpp) and [AudioStreamRandomizer](https://github.com/godotengine/godot/blob/fefec82d74b46f5e6cc31f373071058f80a5cbee/scene/resources/audio/audio_stream_randomizer.cpp). For the candidate backend, inspect SoLoud [bus playback](https://github.com/jarikomppa/soloud/blob/e82fd32c1f62183922f08c14c814a02b58db1873/src/core/soloud_bus.cpp) and [C# wrapper generation](https://github.com/jarikomppa/soloud/blob/e82fd32c1f62183922f08c14c814a02b58db1873/scripts/gen_cs.py).

**Acceptance:** audible positional movement and attenuation; loops stop on destruction/unload; repeated loads do not leak voices or buffers; UI and dialogue remain controllable; device loss does not crash or block simulation. No asset loading, decoding or managed gameplay executes on the real-time audio callback. Measure callback duration/underruns, latency, voices and memory against the accepted baseline.

### Priority 3: Feedback Around Existing Physics

**Implement:** a small surface vocabulary and response lookup for footsteps, weapon hits, breaks and observable impacts. Many visual materials may share a behavioral sound profile. Select intensity/variants with thresholds and per-source cooldowns. Publish an independent semantic noise event for an accepted action.

Keep the adapter in game/runtime presentation code and consume only results already available. Do not guess a physical impulse from arbitrary frame displacement and present it as a measured contact. Start with reliable hit/break/footstep events; include collision strength only where the existing API supplies adequate information. Scrape/roll loops are deferred unless sustained contact and velocity are already observable without physics changes. Surface profiles in this phase do not change friction, restitution or mass.

**Read first:** ezEngine [SurfaceResource.cpp](https://github.com/ezEngine/ezEngine/blob/59ca5986e0cf07d94fc360e426e102a60ca60d81/Code/Engine/Core/Physics/Implementation/SurfaceResource.cpp): `FindInteraction` and `InteractWithSurface`. Arx [GameSound.cpp](https://github.com/arx/ArxLibertatis/blob/5b95e4c5ca9d583f1b11c085326979772645e0f3/src/scene/GameSound.cpp): `ARX_SOUND_PlayCollision` and its material maps. Keep AI noise independent of Arx's playback/camera early-outs.

**Acceptance:** a supported metal/wood/glass action has a recognizable response; resting props stay quiet; muted/stolen voices leave identical gameplay noise events; queue limits hold during destruction stress. Existing deterministic physics replay results match. If an effect needs a solver change, leave it deferred rather than expanding this phase.

### Priority 4: Composable Interactions With Persistent State

**Implement:** extend existing interactions with explicit `All`/`Any` conditions, a small typed action vocabulary and a bounded response queue. Initially support inventory possession, persistent state, existing machine/door state and mass-sensor results. Commit successful consumption/rewards/state together; reject failed attempts without side effects. Include cause tracing, cooldowns and protection against cyclic or duplicate actions.

**Local integration:** [LevelInteractionDef](../Engine.Editor/Editor/LevelTypes.cs), the existing requirement/interaction/save paths in [HL2GameModule.cs](HL2GameModule.cs), and [pressure plates](HL2GameModule.Puzzles.cs). Map old `RequiredStates` to `All` to preserve old levels. Reuse existing door and machine actions without changing the lighting implementation or physical behavior.

**Read first:** The Dark Mod [Response.cpp](https://github.com/stgatilov/darkmod_src/blob/c1ab71003d544dbedcbcfce7d2bfc4af7cf7a650/game/StimResponse/Response.cpp), particularly `CResponse::TriggerResponse`; Arx [ScriptEvent.cpp](https://github.com/arx/ArxLibertatis/blob/5b95e4c5ca9d583f1b11c085326979772645e0f3/src/script/ScriptEvent.cpp); OpenMW [ContainerStore persistence](https://github.com/OpenMW/openmw/blob/9666b295d1a4bd46e7a8aed5e305bffc0366734c/apps/openmw/mwworld/containerstore.cpp).

**Acceptance:** one service door opens by an item, a machine-state solution or an existing mass-sensor bypass. Each branch has a readable requirement and tradeoff. Failed actions consume nothing; repeating a completed action cannot duplicate rewards; save/load restores consequences; existing conjunctive puzzles still behave the same. Apply the [orthogonal-design guidance](ORTHOGONAL_GAME_DESIGN_RESEARCH.md) to justify each capability.

### Priority 5: Original Art Kit And Focused Authoring Tools

**Implement:** audit existing prefab apply/revert, reference remapping, asset import and material workflows before adding tools. Build one original architectural kit with coherent proportions, trims/materials, signage, story props and machine/door prefabs. Add only missing high-value parameters and validators, such as a cue reference, initial machine state or sign variant. Cue/rule editing needed in earlier priorities belongs with those milestones; this phase improves reuse.

**Read first:** ezEngine [PrefabReferenceComponent.cpp](https://github.com/ezEngine/ezEngine/blob/59ca5986e0cf07d94fc360e426e102a60ca60d81/Code/Engine/Core/Prefabs/Implementation/PrefabReferenceComponent.cpp), [AssetCurator.h](https://github.com/ezEngine/ezEngine/blob/59ca5986e0cf07d94fc360e426e102a60ca60d81/Code/Editor/EditorFramework/Assets/AssetCurator.h), and O3DE [PrefabSystemComponent.cpp](https://github.com/o3de/o3de/blob/19c096e51f638d7be9e231d551ca3f688af34bf2/Code/Framework/AzToolsFramework/AzToolsFramework/Prefab/PrefabSystemComponent.cpp). Apply selectively to the local [PrefabIO.cs](../Engine.Editor/Editor/PrefabIO.cs).

**Acceptance:** a room communicates its original purpose through composition, signs and props; variants remain coherent; duplicated prefabs remap internal entity/cue/rule references; undo/save/reload and template updates work. Use the accepted lighting pipeline as-is. New shaders, decals, automatic room generation and a large asset-pipeline rewrite require a demonstrated need and remain outside this milestone.

### Priority 6: Room Acoustics And Gameplay Hearing Paths

**Implement:** a separate acoustic graph keyed by existing room/portal IDs, with explicit door links and authored transmission loss. Support a bounded propagation query for semantic noise events and independently tuned player occlusion/filtering and room reverb. Use a test listener to inspect hearing before adding a full NPC. Smooth audible transitions without delaying gameplay events unpredictably.

The existing [RoomVisibilityGraph](../Engine.Editor/Editor/RoomVisibility.cs) is camera visibility only. Do not modify its purpose or use its visible-room list to decide which sounds or AI exist. Navigation, visual visibility and acoustics may reference the same IDs but require separate connectivity and policy.

**Read first:** The Dark Mod [SndProp.h](https://github.com/stgatilov/darkmod_src/blob/c1ab71003d544dbedcbcfce7d2bfc4af7cf7a650/game/SndProp.h) and [SndProp.cpp](https://github.com/stgatilov/darkmod_src/blob/c1ab71003d544dbedcbcfce7d2bfc4af7cf7a650/game/SndProp.cpp). If the simple model is inadequate, evaluate Steam Audio's [C API](https://github.com/ValveSoftware/steam-audio/blob/480dd64f513cc8a6437e7d5b9eb0d3f1d30c2fac/core/src/core/phonon.h) and [DirectSimulator](https://github.com/ValveSoftware/steam-audio/blob/480dd64f513cc8a6437e7d5b9eb0d3f1d30c2fac/core/src/core/direct_simulator.cpp) as a separately measured native acoustic backend. Acoustic reflection/reverb here concerns sound and does not change visual reflections.

**Acceptance:** doors affect the intended sound paths; opening a door changes the mix smoothly; camera turns, mute settings and voice limits do not change semantic hearing; graph errors are reported; loops and leaks stay bounded. Assign ownership of attenuation/filtering so two layers do not apply the same loss twice. Advanced acoustic simulation is optional, not a prerequisite for Priority 7.

### Priority 7: One NPC, Navigation And Investigation

**Implement:** establish a navigable route using level geometry through a separate navigation representation. Prototype patrol, investigate, search and return with explicit intent, memory and movement. Feed Priority 6's semantic hearing results into a remembered location/confidence rather than revealing the player's actual position. Bound path/query work and replan on relevant door changes. Fit movement to the existing collision/controller capabilities; defer behaviors requiring physics changes.

**Read first:** OpenMW [AiPackage](https://github.com/OpenMW/openmw/blob/9666b295d1a4bd46e7a8aed5e305bffc0366734c/apps/openmw/mwmechanics/aipackage.cpp), especially `pathTo`, route updates and door handling; Recast/Detour [module overview/examples](https://github.com/recastnavigation/recastnavigation/blob/9f4ce64458dfae86e1239c525ddc219c4e9e06f1/README.md) and [DetourNavMeshQuery.cpp](https://github.com/recastnavigation/recastnavigation/blob/9f4ce64458dfae86e1239c525ddc219c4e9e06f1/Detour/Source/DetourNavMeshQuery.cpp). CRYENGINE's [UQS documentation](https://www.cryengine.com/docs/static/engines/cryengine-5/categories/23756816/pages/29450474) supplies the idea of filtering and scoring candidate investigation points; its private current source is not a dependency.

**Acceptance:** a thrown object distracts the NPC; closed routes produce a fallback rather than endless retries; search expires; off-screen behavior remains valid; mute settings do not affect decisions. Save/load restores the relevant intent/location or a documented safe re-entry state. Debug output explains the cause of state changes. Combat, squads, lighting-dependent detection, advanced locomotion and a general AI director stay deferred.

### Priority 8: Finish One Playable Slice Before Expanding

Combine the original foyer/machine-room art kit, one multi-solution service door, physical props, material feedback, connected acoustics and one investigating NPC in a separate fixture. Give each solution a meaningful noise/resource/exposure consequence. Preserve a recovery route if optional props are moved or destroyed.

Add authored machine/ambience layers, readable action transients, mix transitions and captions/accessibility controls. Use the four sound references in the [style objective](GAME_STYLE_OBJECTIVE.md) as listening goals. Persist logical loop intent, not native voice pointers, and avoid replaying historical one-shots when loading. OpenMW's [SoundManager](https://github.com/OpenMW/openmw/blob/9666b295d1a4bd46e7a8aed5e305bffc0366734c/apps/openmw/mwsound/soundmanagerimp.cpp) is a useful lifecycle reference.

**Acceptance:** repeat alternative solutions, save/load, pause, destruction and level reload; inspect handle/resource release and long-session behavior; check captions and headphone/speaker mixes. Run the existing relevant [gameplay stability](GAMEPLAY_STABILITY_GUIDE.md), physics replay and lighting reference checks with their limits intact. Demonstrate acceptable CPU, memory and frame pacing before increasing room, sound or NPC counts.

## Performance And Completion Rules

Set new subsystem budgets from the accepted Gate B workload and target hardware before implementation acceptance. Record whole-frame changes as well as audio callback duration/underruns, active/virtual voices, command queue peaks, sample/stream memory, acoustic query work, pathfinding and AI update time. Do not substitute a good average for failed hitch/memory limits.

Each milestone delivers one separate fixture, reproducible checks and a short status note covering what works, measured cost and remaining limitations. Keep optional features disabled until their own acceptance passes. Re-run broader checks when a relevant change or regression justifies them. The current task updates documentation only and clears no gate.

## Reference Use And Deferred Work

The source links use the revisions verified in the research. Study C++ ownership, data and scheduling; implement appropriate C# systems. Preserve applicable licenses when using actual code. GPL examples are learning references for independent implementation, not code to translate without obligations. Source access alone does not authorize copying proprietary game assets.

O3DE, ezEngine and Godot are the practical production references; The Dark Mod and Arx are especially useful for systemic behavior. CRYENGINE remains a public-documentation architecture reference under its own terms; archived Lumberyard is historical context. See the [research comparison and license sources](ENGINE_SYSTEMS_RESEARCH.md#engine-comparison-and-access).

Defer a C++ runtime migration, wholesale ECS/editor replacement, a physics replacement, new lighting/rendering work, broad AI/combat, full world simulation and mandatory advanced acoustic middleware. Optional branching narrative with ink can follow a demonstrated story requirement after the slice; ordinary logs and captions do not need a narrative framework.

**Immediate next work remains finishing the current lighting implementation and its acceptance, followed by closing the existing baseline stability gate. Priority 1 is the first new-system task only after both are complete.**

# Game Style Objective

Updated: 5 November 2026. This is the current creative objective for the engine and its game. It updates the earlier reference mix without replacing implementation status in the technology roadmap.

Build a first-person game with BioShock-inspired architectural art direction, deliberate exploration and environmental storytelling, immersive-sim player choice, expressive sound, and the existing Half-Life 2-style physical interactions. Keep C# as the runtime and gameplay programming structure.

## The Five Pillars

| Pillar | Reference | Practical direction |
| --- | --- | --- |
| Art direction | BioShock | Strong architectural identity, Art Deco influence where appropriate, distinctive silhouettes, ornamental and industrial contrasts, coherent materials, signage, and props that communicate a place's purpose and history. Develop an original setting and assets around these principles. |
| Atmosphere, lighting and visual horror | Resident Evil Requiem; Dead Space remake | Retain these as the atmosphere references. This work is already underway and is excluded from the current external-engine research pass. |
| Gameplay | Prey; Dishonored; System Shock; Deus Ex | Consistent world rules, readable affordances, alternative approaches to selected obstacles, consequences that persist, and interactions between physics, inventory, machines and AI. Teach individual rules before combining them. |
| Sound | Dead Space remake; Half-Life 2; Doom Eternal; The Last of Us Part II | Give places a recognizable acoustic identity; make physical actions convincing and informative; preserve clear threat, dialogue and interaction cues; use responsive layering and controlled intensity. The interpretation of each influence is detailed in the systems research. |
| Physics | Half-Life 2-style interactions already implemented | Preserve carrying, gravity-gun manipulation, throwing, impacts, breakables and mass-based puzzles as a gameplay foundation. Add meaningful feedback and world responses around the existing implementation. |

## What This Means For Levels

As of 5 November 2026, [the immersive-sim design philosophy](IMMERSIVE_SIM_DESIGN_PHILOSOPHY.md)
is an adopted authoring requirement: connected explorable spaces, objectives
with different viable methods, transferable rules, meaningful tradeoffs and
recovery. This applies the user's supplied research; it is not shipped-system status.

- Design spaces with a believable original function, a recognizable landmark, useful wayfinding and a traceable history.
- Use a reusable architectural kit and a limited material language. Give rooms distinct compositions and prop arrangements within that language.
- For selected major obstacles, support approaches using different capabilities: an item, a machine state, a physical solution or a route around the obstacle. Each must have a clear cost or consequence.
- Let the same physical object participate in more than one system: weight, obstruction, impact sound, distraction and damage where supported.
- Make important state changes legible through object motion, readable prompts, animation, signage and sound. Preserve accessibility options for information communicated through sound.
- Keep slower to medium-paced exploration, spatial reasoning and short action encounters. Use the sound references to shape feedback and intensity within that pacing.
- Preserve essential progression when optional physical objects are moved or destroyed, and restore meaningful consequences correctly after saving and loading.

These are design targets. They do not imply that AI perception, spatial audio or a general interaction-rule system has already shipped.

## Engine And Research Boundaries

**Execution prerequisite: finish and accept the current lighting implementation before starting any new engine system upgrade, including prototypes or preparatory system refactors.** The [prioritised systems upgrade plan](ENGINE_SYSTEM_UPGRADE_PLAN.md) also retains the existing baseline stability gate. This objective and its research are planning documents, not permission to start those milestones early.

C# owns entities, gameplay state, rules, AI decisions, editor-facing data and persistence. Study C++ engines for their system boundaries, data layouts, authoring tools and scheduling. Use a native library behind a small stable C interface only when its benefit is demonstrated in this project.

The current research covers art/content workflows, immersive-sim systems, audio and feedback around existing physics. New lighting, reflections, visual atmosphere and horror-rendering work remain in their existing implementation track. The current frame-pacing investigation also retains its acceptance gates; research does not approve a new dependency or a performance rollout.

## Related Documents

- [Prioritised systems upgrade plan and mandatory lighting gate](ENGINE_SYSTEM_UPGRADE_PLAN.md)
- [Source-code research and proposed development sequence](ENGINE_SYSTEMS_RESEARCH.md)
- [Level design guide](LEVEL_DESIGN_GUIDE.md)
- [Orthogonal game design research](ORTHOGONAL_GAME_DESIGN_RESEARCH.md)
- [Current implementation and performance roadmap](ENGINE_TECHNOLOGY_ROADMAP.md)
- [Installed audio tools and asset provenance](AUDIO_WORKFLOW.md)

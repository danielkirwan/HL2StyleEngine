# Immersive-Sim Design Philosophy

Adopted 5 November 2026 at the user's request. This guides future level and
gameplay design alongside [the style objective](GAME_STYLE_OBJECTIVE.md) and
[orthogonal design](ORTHOGONAL_GAME_DESIGN_RESEARCH.md). It does not clear Gate L
or Gate B or authorize new systems before their acceptance.

## Source And Interpretation

Read: [Immersive Sims and Player Agency, 5 November 2026](<C:/Users/danie/.codex/visualizations/2026/11/05/01a1ac00-234b-7c52-8c4d-6cd5e36bbf06/research/output/pdf/Immersive_Sims_Game_Design_Research.pdf>).
The supplied 27-page report compares thirteen games/versions. Its qualitative,
English-language, selected evidence is not a representative player survey.
Especially relevant: rules/choice (pp. 3-5), tradeoffs (p. 19), priorities
(p. 20), the proposed depot (p. 21) and playtesting (pp. 22-23). The following
is our project application, not a promise to implement the report's prototype
verbatim or an independent verification of all its external references.

## Player Promise

Build a slower/medium-paced first-person horror game where players can explore
a believable place freely within readable boundaries, form their own plans,
and complete important objectives in materially different ways. A compact,
connected level can satisfy this promise; a large open world is not required.
Atmosphere supports agency but does not replace reliable interaction.

## Level Requirements

- Author objectives as outcomes, not compulsory action sequences. Define what
  must become true, then allow supported world rules to produce that state.
- For each major obstacle, plan at least two viable approaches with different
  costs, risks or information needs. This is a project review target, not a
  quota of three labelled stealth/combat/hacking lanes. Combine approaches.
- Give spaces plausible functions, recognizable landmarks, visible connections,
  useful optional areas and return routes. Use loops, vertical links and
  unlockable shortcuts where they improve navigation. Not every room needs a
  loop and freedom does not mean every entrance is immediately accessible.
- Distinguish spatial alternatives from systemic solutions and narrative
  decisions. Another corridor alone is not proof of systemic depth.
- Let existing tools transfer between situations. Carrying, gravity retrieval,
  throwing, breakables, doors and mass sensors are the starting vocabulary.
  Do not promise climbable stacks, noise-sensitive guards or lighting-sensitive
  security until their actual behaviour has been implemented and tested.
- Make methods differ in time, resources, exposure, access, preparation or
  persistent consequences. Avoid one method being universally best.
- Accept unexpected solutions that obey consistent rules. Fix broken rules or
  progression exploits deliberately; do not block a valid plan simply because
  it bypasses an intended puzzle sequence.
- Provide recovery if a prop is moved, a consumable is spent or an attempt
  fails. Essential progression must not depend on an unprotected disposable
  object. Preserve at least one understandable remaining approach.
- Reward observation with actionable information. Keep clues recoverable through
  the world and, when supported, an appropriate journal or transcript. Optional
  guidance should help locate evidence rather than solve every problem.
- Make revisits worthwhile through new access, changed states or useful new
  understanding. Shorten empty return trips; avoid repetitive inventory upkeep.
- Keep materials, interaction feedback and controls readable in horror lighting.
  A failed input or unreliable placement is not an interesting failed plan.
- Persist meaningful changes through save/load: doors, collected items, machine
  states and consequences. Finish the central objective with a clear payoff.

## Authoring Review Card

| Field | Record before building the encounter |
| --- | --- |
| Objective | Observable completion state, independent of method |
| Place | Original room function, landmark and spatial connections |
| Opportunities | Supported routes, tools and information visible before commitment |
| Approaches | At least two meaningful methods and one possible hybrid |
| Tradeoffs | Different resource/time/risk/consequence costs |
| Rules | Behaviours shared with other objects and explicit exceptions |
| Recovery | What remains possible after failure, destruction or lost resources |
| Persistence | States that must survive save/load and revisits |
| Dependencies | Implemented, unverified, or deferred engine capabilities |

Example for a future service-access objective: an existing key opens the main
door; an existing powered mechanism or mass sensor opens another supported
access route. Gravity manipulation can retrieve a useful object or help operate
the mechanism. A maintenance passage may reconnect beyond the gate. These are
authoring possibilities, not changes to the current six-room puzzle layout.
General `Any` conditions, AI hearing and security remain roadmap work.

## Playtest Acceptance

Observe without explaining the solution list. Record what the player predicts,
which alternatives they notice, how they combine tools, the actual result and
their next choice after failure. Include newcomers as well as experienced
immersive-sim players; small tests find usability problems, not market shares.

A useful slice demonstrates different self-discovered successful plans,
understandable feedback and recovery. If everyone chooses one method, inspect
visibility, cost and reliability before adding more content. Keep a log of valid
unexpected solutions. Test the same objective after save/load and with optional
props moved or destroyed. Maintain the existing performance and physics gates.

## Implementation Boundary

This is design guidance only. Keep C# and existing physics. Finish lighting
acceptance and baseline memory/frame-pacing work first, then follow
[the prioritised systems plan](ENGINE_SYSTEM_UPGRADE_PLAN.md). Do not introduce
AI, audio, general interactions or editor prototypes under this documentation
update. The existing six-room fixture remains a regression test, not proof that
the full player promise has already shipped.

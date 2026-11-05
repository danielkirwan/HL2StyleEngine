**Orthogonal game design: research and application to HL2StyleEngine**

Researched: 4 November 2026. Scope: Harvey Smith's 2003 GDC material, Matthew Frederick's architecture lessons 45 and 46, Steve Lee's linked video, the supplied screenshot, and current engine source. Status: research and proposed design work; no gameplay or editor implementation is included.

Source review expanded on 4 November after checking the user's supplied 48-slide PDF against the accompanying pasted slide text and notes.

**Main recommendation**

Develop a small vocabulary of dependable player actions and world responses, then compose spaces that make their relationships matter. For this engine, begin with moving physical mass, changing powered states, damaging fragile objects and reading the environment. Preserve C# as the runtime and build on the existing entity, script, physics and persistence structures.

A useful review question is: what different plan does this element make possible, and how will a player recognize that possibility? A new asset, stat increase or elaborate room is valuable when it changes the experience meaningfully. Orthogonality is one lens for assessing this, not a requirement that all content be unrelated.

**What was actually reviewed**

- [GDC Vault: Orthogonal Unit Design](https://www.gdcvault.com/play/1022697/Orthogonal-Unit), Harvey Smith, GDC 2003, verifies the session identity.
- [Smith's own OUD page](https://www.witchboy.net/articles/articlesoud/) links his original [2003 presentation](https://www.witchboy.net/wp-content/uploads/2009/03/gdc03_oud.ppt). I read the slide material and notes through the [Google Slides copy linked in Lee's video](https://docs.google.com/presentation/d/1Pb3TkArQNolQNUCEAJ6fz622BPEthdGZ37uXSWj1XCs/htmlpresent). This is a review of the slides, not a claim to have listened to the original GDC audio.
- [Smith: Distinct Function in Game Units](https://www.witchboy.net/articles/distinct-function-in-game-units/) supplies his related written explanation.
- [101 Things I Learned in Architecture School](https://mitpress.mit.edu/9780262062664/101-things-i-learned-in-architecture-school/), Matthew Frederick, MIT Press, 2007. Lesson numbering/text was checked against a [reproduction of the book](https://studylib.net/doc/26029216/101-things-i-learned-in-architecture-school), around the printed folios 91-93, and the supplied illustration. The government-hosted PDF found in search repeatedly failed to load.
- [Steve Lee: Every game designer should understand Orthogonal Design](https://www.youtube.com/watch?v=hW5s5Wb5vo4), published 26 August 2024. I reviewed the exported English captions across the video, its chapter structure, a key frame and Lee's pinned clarifications. This was caption and visual review rather than continuous real-time viewing.
- [User-supplied slide PDF](<C:/Users/danie/Desktop/harvery smith OUD.pdf>): all 48 pages were inspected as rendered slides, with closer review of slides 18, 34, 36 and 37. The PDF is image-based and has no extractable text layer.
- [User-supplied pasted text](<C:/Users/danie/.codex/attachments/9d311fe6-050e-4693-9976-6f5b86f651d9/Pasted text.txt>): 48 numbered sections corresponding to the slides, including extensive speaker notes, repeated passages and replacement characters in some punctuation. Section 1 explicitly distinguishes the notes from the planned speech. Treat this as slide text plus supporting notes, not a verified word-for-word recording transcript. The PDF resolves wording/structure where the pasted text is unclear.

The following application, examples, component suggestions and implementation sequence are my synthesis for this project. They are not quotations or prescriptions from the source authors.

**1. Smith: differentiate functional roles**

Smith distinguishes primary roles from variations in statistics. His archer/riverboat comparison asks whether adding more of one unit could supply the other unit's missing function. He also stresses legible consequences and consistent behaviour. Later slides explicitly allow hybrids and numerical variants when context or clarity calls for them. See slides 7-10, 14-20, 28-37 and 43 in the linked deck.

The supplied files make the following design tests explicit. Slide references below refer to the supplied 48-page PDF and the matching numbered pasted sections.

**A. Apply the complete three-part test (slides 18-20)**

1. Do the two elements have different primary roles?
2. Can one address a tactical limitation of the other?
3. Would simply adding more of the first element still leave that limitation unresolved?

For a proposed tool set, identify the missing capability, not merely a different damage number. Under declared game rules, additional damage may still leave the player unable to retrieve an intact object. Test this against the actual mechanics: if shooting also pushes or breaks objects, acknowledge that overlap rather than asserting perfect independence.

Slide 14's notes clarify that an axis represents a function such as water crossing, rather than necessarily representing an entire unit. In engine authoring, therefore, describe capabilities and constraints separately from prefab names or visual identities.

**B. Require all four design qualities (slide 34)**

Distinct functions need defined roles, understandable feedback and dependable behaviour. For a plate, that means explaining whether it measures settled mass, what threshold it uses, how it signals activation and whether its linked mechanism is momentary or latched. Designer diagnostics help implement this, but the player also needs cues in the game world.

**C. Test second-order consequences (slides 35-36)**

The Populous example combines priests that convert idle warriors with warriors that resist conversion while fighting. Converted defenders engage the next attackers, changing whether those attackers can subsequently be converted. A seemingly self-sustaining defensive arrangement therefore creates a reason for active intervention.

The application is to test sequences as well as isolated pairs of objects. Moving a prop can change a sensor, geometry and the usefulness of a subsequent action. A combination earns its place when an ordinary rule changes a later decision without a separate one-off script for the whole sequence. Such interactions must still remain learnable and recoverable.

**D. Preserve useful shared behaviour (slides 37-41)**

Smith's Deus Ex bots share capabilities such as responding to alarms and being destructible, while healing and recharging/repairing remain different primary roles. That supports a shared C# interaction foundation with specialized responses. It does not call for a separate engine system for each enemy or prop, or for splitting every minor variation into a unique mechanic.

**E. Judge differences by the decisions they change (slides 37-38)**

Keep a variation when it meaningfully affects a plan, outcome or contextual expectation. Merge distinctions that impose learning and authoring costs without adding useful decisions. Numerical tuning and hybrid roles remain valid. This is a review criterion for playtesting, not an automatic score that proves a design is enjoyable.

**Proposed authoring review card**

| Field | Question for the designer |
| --- | --- |
| Primary role | What can the player accomplish with this element? |
| Limitation | What can it not accomplish under the declared rules? |
| Complement | Which other element addresses that limitation? |
| Observable state | How does the player recognize what it can currently do? |
| Consistent response | What happens for the same valid action in another room? |
| Combination | What subsequent decision changes when two rules interact? |
| Cost and recovery | What is spent or committed, and can a failed attempt block progress? |
| Reason to retain | Does this distinction justify its learning and production cost? |

This card is proposed project guidance. It is my application of the supplied material, not a form prescribed by Smith.

His written article adds a practical communication lesson: players must be able to discover an enemy's distinctive behaviour and have actions that respond to it. A hidden distinction that never changes a player's decisions contributes little. [Author's article](https://www.witchboy.net/articles/distinct-function-in-game-units/).

For your game, a better starting comparison is a pistol's primary damage role and a gravity gun's primary manipulation role. Their abilities can overlap; the important question is where one enables a plan that additional damage cannot. The same analysis applies to barriers, tools, pickups and environmental mechanisms.

**2. Frederick: understanding complexity, then organizing it**

Lesson 45 distinguishes an immediate, limited understanding; awareness of many factors without organizing them; and an experienced ability to recognize useful patterns. Lesson 46 applies that clarity to composition: a few deliberate relationships can support varied spatial experience. These describe design thinking; using them as stages of player learning is an adaptation.

Your screenshot is the lesson-46 comparison: three elements make three spaces, twelve separate elements make twelve spaces, and three interacting elements make twelve spaces. The number twelve is an illustration, not a formula for the number of solutions generated by three mechanics.

Applied to level design, ask whether one courtyard can support observation, traversal, a power problem, a changed return journey and an optional reward through its relationships. Avoid assuming that removing detail alone creates depth. The arrangement must create useful distinctions and understandable opportunities.

**3. Lee: the connection to game systems**

Lee's chapter examples span Doom enemies (2:11), Dishonored abilities (4:03), informed simplicity (4:59), Left 4 Dead (6:43), Tactical Breach Wizards (8:02) and non-combat Alba actions (9:33). He connects a compact set of distinctive functions to a broader range of situations. His pinned correction stresses separate functional axes, permits overlap and notes that removing a capability can differentiate a unit. [Video and creator's clarification](https://www.youtube.com/watch?v=hW5s5Wb5vo4).

For this project, that suggests comparing how players acquire information, manipulate matter, gain access and change a space. It does not imply that every existing weapon needs replacing or that more abilities automatically improve a level.

**4. What the engine already supports**

These observations use current source on 4 November, not only the earlier graphics research.

| Foundation | Evidence | Design opportunity |
| --- | --- | --- |
| Manipulating physical props | [WeaponDefinitions](Weapons/WeaponDefinitions.cs), [WeaponSystem](Weapons/WeaponSystem.cs), [WeaponHost](HL2GameModule.WeaponHost.cs) | Use mass, distance, access and prop placement to create manipulation problems. Current weapon effects overlap, which is compatible with role differentiation. |
| Mass-sensitive plates | [PressurePlateSensor](Puzzles/PressurePlateSensor.cs), [puzzle integration](HL2GameModule.Puzzles.cs) | Settled supporting dynamic bodies contribute mass. This is already closer to a general rule than requiring one specially named crate. Held bodies are excluded. |
| Interaction data | [LevelInteractionDef](../Engine.Editor/Editor/LevelTypes.cs) | Reusable kinds, targets, item requirements, costs and required states support authored mechanisms. |
| State-gated puzzles | [AreInteractionRequiredStatesComplete](HL2GameModule.cs) | The current RequiredStates list behaves as ALL conditions. More than one valid solution needs explicit ANY/alternative-path support or separate authored routes. |
| Switched lighting | [lighting integration](HL2GameModule.Lighting.cs), [LevelLightState](../Engine.Editor/Editor/LevelLightState.cs) | Power-related actions already change visible illumination and support shared state. This is not automatically a general electrical simulation. |
| Destruction and replacement | [fracture code](HL2GameModule.Fracture.cs) and weapon damage forwarding | Reuse breakable obstacles and consequences while retaining part identities and replacement readiness. |
| Persistent puzzle/item/door state | [persistence guide](PERSISTENCE_AND_INVENTORY.md), [six-room fixture](SIX_ROOM_TEST.md) | Return journeys can reflect player decisions; required-item protection already matters to progress. |
| Room/portal metadata | [RoomVisibility](../Engine.Editor/Editor/RoomVisibility.cs) | Current room/portal data is a useful source of spatial context. The graph explicitly manages camera visibility, not entity ownership, physics, AI, hearing or navigation. |
| Managed components | [ScriptRegistry](../Engine.Runtime/Entities/ScriptRegistry.cs), [IComponent](../Engine.Runtime/Entities/Interfaces/IComponent.cs) | Add focused C# rules using existing entities/components, without an ECS rewrite. |

The reviewed six-room fixture documents no enemies. I found no general enemy-perception/hearing implementation in the searched Game/Engine.Runtime sources. Enemy and noise-response suggestions below are proposed additions, not demonstrated features.

**5. A compact gameplay vocabulary**

Choose primary roles, constraints and feedback before expanding the asset list.

| Action/system | Distinct contribution | Constraint or tradeoff | Combinations to prototype |
| --- | --- | --- | --- |
| Move mass | Reposition the physical world | Reach, mass, stability, access | Weight a plate, move solid cover, retrieve a prop, create a step |
| Change a powered state | Alter what a mechanism or fixture can do | Which circuit is available; any deliberately shared capacity | Change a shutter, light, lift or information source |
| Damage fragile structure | Permanently alter an obstacle | Resource cost, one-way changes, collateral effects where supported | Open a grate, release a restrained prop, expose a mechanism |
| Observe/illuminate | Gain information | View angle, occlusion, contrast | Read a clue, inspect a route, identify an object's properties |
| Inventory authorization | Satisfy an explicit access/resource rule | Availability, consumption, carried resources | Key an optional cache or repair a selected mechanism |

Several effects in the last column would need specific support and authoring even though their foundations exist. For example, a light switch changing a powered lift is a proposed connection, not current universal behaviour.

Do not make every object respond to every action. Make supported behaviours consistent, readable and appropriate to the fiction. A bolted prop may resist lifting; a damaged brace may be breakable. Communicate those properties before they become a critical decision.

A physical object may perform several roles at once if the geometry allows it. Avoid artificial restrictions that prevent a box from being both cover and plate weight merely to enforce a preferred solution.

**6. Apply the screenshot to the layout**

Think of three overlapping design layers in the same space:

- **Movement:** the public path, a service passage, a ledge and the return shortcut.
- **Information:** what can be seen from each approach, where the objective is previewed, and which changes are legible from the hub.
- **Manipulation/state:** where movable mass, breakable obstructions, switches and mechanisms influence those routes.

These are organizing lenses, not a claim that all three are mathematically independent. Their intersections are opportunities: an overlook can reveal a control panel, expose a safe prop-retrieval angle and later become an escape route.

Use these checks on a blockout:

1. Can the player preview a goal or consequence before committing?
2. Can one tool be useful in two contexts with the same underlying rule?
3. Does moving to a different elevation or angle change the useful plan?
4. Does solving a problem change an earlier space or create a return shortcut?
5. Are routes meaningfully different in cost or experience?
6. Is the next step legible without decorating every route with new symbols and exceptions?

The screenshot's lesson is about the relationships you compose. A larger room count or a higher prop count is not evidence that these questions have good answers.

**7. Proposed first prototype: a maintenance junction**

Create a separate future fixture, provisionally orthogonalDesignLab.json, rather than altering an accepted test level during research.

**Goal:** reach a visible maintenance console behind a locked service connection.

**Starting vocabulary:** gravity manipulation, damage to an authored fragile panel, and switched mechanism state. Use one movable crate, one plate, a powered shutter and one elevated service opening.

**Teaching sequence**

1. In a safe approach, let the player see a weighted plate operate a small barrier. Show a clear state change. Use any eligible settled mass rather than a named puzzle object.
2. Let the player operate a switch and see the connected fixture/mechanism change from the same position.
3. Provide a clearly fragile panel with a visible passage beyond it. Make its destructive behaviour consistent with earlier breakables.
4. Bring those already understood rules together at the junction.

**Candidate approaches, all proposed**

| Approach | Player plan | Different cost/experience | Required validation |
| --- | --- | --- | --- |
| Main shutter | Power the mechanism, leave mass on the plate, use the main opening | Leaves the prop committed; uses the direct route | Plate mass, power condition, door timing and return access |
| Service passage | Break the fragile service panel and take the narrower route | Uses damage/resources; longer or less convenient traversal | Collision opening, resource availability and no invisible blockers |
| Elevated access | Reposition the crate to reach the service opening and approach the console from above | Gives up its plate role; requires spatial reasoning | Reach/jump/clearance, stable prop support and no accidental trapping |

This is a design sketch, not a promise that all routes already work in the engine. Verify traversal and physics before treating them as solutions. Route lengths, visibility and resource costs should be tuned through playtests, not assumed from a plan drawing.

Later, add one pressure source to this same geometry. A new threat should change the relative value of the approaches rather than simply add more health to targets.

**Success:** a new player understands the reusable rules, can explain more than one viable plan and can recover after trying an imperfect one. An experienced player should be able to transfer the same rules to a new arrangement.

**8. Engine changes that support this approach**

**A. Make conditions explicit**

Add an optional typed condition structure supporting All and Any, then only add threshold conditions when an actual design needs them. Keep existing RequiredStates semantics as All for old levels.

Evaluate conditions without consuming items or changing the world. Execute the selected action's costs/effects only when that action succeeds. This prevents an alternative route from consuming another route's repair item.

Distinguish current conditions from saved progress: plate occupancy is usually momentary; an unlocked door or completed repair may be latched. The authoring UI should expose that choice explicitly.

**B. Connect capabilities and consequences**

Build a small rule layer around the existing entity fields/components. Useful concepts include physical properties, activator/sensor, receiver, damage response and access condition. Avoid a special implementation for every room.

Example dependency, proposed: settled mass changes PlateActive; PlateActive and PowerAvailable enable an actuator; the actuator changes a shutter; the shutter changes physical access. Send the same resulting state to indicators, prompts, saves and rendering where appropriate.

Physical sensing belongs in the simulation's fixed-step ordering. Queue and trace interaction commands and state changes at defined points. Use stable IDs for targets and persistence, and clear ownership for undo, prefab cloning and save/load.

**C. Give the editor visibility into the rules**

Start with small extensions to the current Inspector:

- A selected object's supported actions, constraints, primary role and feedback.
- Incoming conditions and outgoing target links.
- A live explanation for a false condition, such as insufficient settled mass or missing power state.
- The difference between a momentary condition and a latched completion.
- A trace of the last meaningful state changes.

A later graph view can visualize this data. First make the data and diagnostics useful. Do not build a large visual scripting system before one prototype shows a need.

**D. Add design validation**

Check missing/duplicate target IDs, unavailable required capabilities/items, impossible mass thresholds and destructive loss of critical resources. Show dependency cycles for review; a cycle may be intentional feedback and should not be rejected automatically.

Validate that declared alternative routes differ and actually work. Static graph checks can find some errors; dynamic physics, resource use and player comprehension still require fixture tests and human playtests. Do not label a level proven solvable merely because references are valid.

**E. Add perception only when the design needs it**

If darkness, noise or attention become gameplay, introduce explicit C# perception rules. A future NoiseEvent can describe source, position, kind and strength; an AI listener can respond according to range, occlusion and memory.

Gameplay visibility should use an authored, testable perception model rather than reading tone-mapped screen pixels. The main camera's render-culling result must not turn off a nearby enemy's simulation or stand in for what the enemy can see.

Specify which actions emit noise. A gunshot, thrown prop and footsteps should follow documented rules if they support distraction strategies. These additions need their own implementation and performance budget.

**9. Future enemy differentiation**

Begin only after the environment prototype works. Possible roles:

| Proposed role | Functional question it adds | Player response to test | New support needed |
| --- | --- | --- | --- |
| Watcher | Can it acquire line of sight? | Use geometry, light-state rules where implemented, or change elevation | Detection, visibility queries, alert feedback |
| Listener | What stimulus will it investigate? | Redirect attention with a supported noise source | Noise events, hearing/occlusion, investigation behaviour |
| Fixed-area attacker | Can the player cross its controlled area? | Reposition solid cover, use timing or another route | Attack behaviour, hit/projectile rules, readable timing |

These are role sketches, not three complete enemies or a required roster. Their interactions need tuning. Pure separation is not the objective, and differing numerical values remain useful inside a role.

A threatening combination should still permit informed action. For a slower horror game, spatial uncertainty can coexist with dependable rules and readable consequences.

**10. Connect visual upgrades to design**

The engine now contains PBR/reflection, light-state and room features beyond the initial October research. Their existence alone does not establish gameplay behaviour.

Use materials, controlled illumination and composition to communicate traversable surfaces, movable mass, fragile obstructions and mechanism states. Give important clues redundant shape, placement, sound or interaction feedback so colour alone is not their only signal.

If a mirror/reflection communicates a live threat or a moving door, its data must represent the current relevant state. A cached room reflection can support atmosphere; it should not be trusted as a live tactical information channel. Put critical feedback in a dependable source until live reflection accuracy is supported.

Keep atmosphere and readability in balance. An authored room can feel mysterious while still explaining what the player's actions changed.

**11. Recommended order and acceptance**

1. Write a role/capability matrix for the current tools, props, barriers and puzzle mechanisms. Flag overlaps for review, not automatic deletion.
2. Define the junction fixture and its intended costs, clues and recovery paths before adding new systems.
3. Prototype the supported interactions with current C# code; add only the condition/actuator behaviour required for the fixture.
4. Add live state explanations and targeted authoring validation.
5. Playtest the safe teaching sequence, then the combined situation, then a changed arrangement using the same vocabulary.
6. Test item consumption, mass changes, prop destruction, backtracking and save/reload. Preserve current required-item protections, replacement readiness and latched progress.
7. Measure per-tick work and use event-driven/indexed updates where needed. Avoid a new rule-versus-every-entity scan.
8. Add one new threat/perception rule only if playtests show what additional decision it should create.
9. Reuse the successful vocabulary in a second level before expanding the mechanic roster.

Questions for playtests: what did you expect to happen, what actually happened, what other plan did you consider, and why did you choose this one? Observe whether players transfer a learned rule without a room-specific explanation.

The immediate deliverable should be one small, legible C# gameplay fixture with genuinely usable alternatives. That is a better test of these ideas than increasing the engine's feature count.


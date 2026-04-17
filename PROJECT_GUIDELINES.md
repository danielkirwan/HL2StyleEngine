# Project Guidelines

Last updated: 2026-04-17

## Project Direction

This project should continue aiming at a game and engine experience inspired by Half-Life 2 in:

- gameplay feel
- movement quality
- physics interaction
- environmental readability
- grounded art direction

It should not drift toward a generic sandbox, a pure tech demo, or a style that loses the grounded Source-era influence.

## Core Principles

### 1. Feel First

When choosing between a technically clever system and a system that produces the right gameplay feel, prefer the one that better supports player feel, readability, and believable interaction.

### 2. Grounded Physics Over Flash

Physics should feel weighty, understandable, and stable. Objects should not behave like arcade props unless the design explicitly wants that.

### 3. Clear Engine/Game Separation

Reusable systems belong in engine projects. Game-specific rules, tuning, and authored behavior belong in the game project unless there is a clear engine-level reason to generalize them.

### 4. Visible Debuggability

Any system that is hard to tune should have usable debug views or instrumentation. If collision or movement behavior changes, debug overlays should stay honest.

### 5. Iteration-Friendly Architecture

Prefer straightforward, inspectable systems over overly abstract ones. The project benefits from code that is easy to reason about and easy to tune.

### 6. Authentic Style Target

Art and presentation decisions should support a grounded industrial sci-fi tone rather than a glossy or overly stylized look.

## Gameplay And Technical Priorities

Prioritize work in roughly this order when there is a tradeoff:

1. player movement feel
2. collision correctness and stability
3. believable physics props and interaction
4. level editing and debug tooling
5. content workflow and presentation polish

## Update Rules For Future Chats

When making meaningful changes:

1. update `C:\HS2StyleEngine\HL2StyleEngine\WORKLOG.md`
2. record what changed, why it changed, and what still needs validation
3. update `C:\HS2StyleEngine\HL2StyleEngine\PROJECT.md` if the overall project state or direction changed
4. keep entries factual and short enough that a new chat can onboard quickly

## Worklog Entry Template

Use this structure when adding a meaningful entry to `WORKLOG.md`:

```md
## YYYY-MM-DD

### Summary
- short description of the change

### Why
- the problem this was solving

### Files
- important files touched

### Validation
- what was tested or built

### Next
- the most likely next tasks
```

## Handover Expectations

Future handovers should answer these questions clearly:

- what the project is trying to become
- what area is actively being worked on
- what changed recently
- why those changes were made
- what is still broken, approximate, or unverified
- what should happen next

## Things To Avoid

- do not treat debug visuals as proof that runtime behavior is correct unless the debug path has been checked too
- do not hide approximation behind presentation-only fixes if the user is asking for real physical behavior
- do not overwrite unrelated local work in the repo
- do not let temporary tuning hacks become permanent direction without documenting them

# CAPCOM RE ENGINE Conference Notes

Checked: 2026-10-05. These notes distinguish the published programme from technologies actually integrated into this project.

## Latest Japan Event

CAPCOM Open Conference Professional RE:2026 took place in Tokyo on October 2; the Tokyo event ran October 2-3. Osaka's student event is scheduled for October 17-18. The professional programme lists 16 technical talks. Sources: [official event](https://www.capcom-games.com/coc2026/ja-jp/), [CAPCOM announcement](https://prtimes.jp/main/html/rd/p/000005941.000013450.html).

The programme describes REX as a broad engine improvement effort spanning runtime performance, development tools, efficiency and QA, with systems including RE:Dox and RE:Log. Other sessions cover real-time path tracing for Resident Evil Requiem/Pragmata, the RE:UI tools framework, RE:Assist knowledge assistance, audiovisual automated QA, VORTEXEL environmental effects, C++/C# development, animation and large-field authoring. These are verified programme topics, not a claim that complete implementations or conference recordings are publicly downloadable. [Official professional programme](https://www.capcom-games.com/coc2026/ja-jp/professional/), [REX session](https://www.capcom-games.com/coc2026/ja-jp/professional/#W1).

## What Is Available

RE:Dox is a public Apache-2.0 structured-data library with a token-based document representation and multiple formats. It is not the whole RE ENGINE, nor a drop-in renderer or asset-streaming system. This engine now uses its pinned .NET 10 packages for level caches and persistence; see [PERSISTENCE_AND_INVENTORY.md](PERSISTENCE_AND_INVENTORY.md). [Official source](https://github.com/CAPCOM-TD-OSS/REDox).

Availability of public packages/source for the other named conference technologies has not been established here. No licensing or access to CAPCOM's internal engine is implied.

## Priorities For This Engine

These are project recommendations, not CAPCOM's prescribed implementation:

1. Measure time to first frame and asset-ready state separately. Show parsing, entity construction, collider creation, mesh decode and texture upload timings so improvements target the actual bottleneck.
2. Keep editable JSON plus validated cooked data. Extend the cache pattern to expensive mesh/collider preprocessing only after transform, material and stale-cache tests exist. Preserve the warm-next-object-before-removing-current-object replacement rule.
3. Improve structured diagnostics: include level path, entity ID, asset path and subsystem in errors. A useful loading report and profiler are more immediately valuable than adding advanced rendering techniques to an unstable scene workflow.
4. Build repeatable QA routes for the six-room level: inventory combination, overflow recovery, door traversal, gravity-gun collection and collider alignment. These can catch regressions before visual playtesting.
5. Keep gameplay/UI state separate from presentation. Reusable sample-state previews in the editor would make inventory and HUD authoring easier without replacing the working RmlUi/ImGui stack solely because another engine uses RE:UI.
6. For larger levels, prioritize asset residency, spatial streaming and visibility culling. For visual quality, finish lighting/shadow correctness before considering path tracing. A slower exploration game benefits first from consistent materials, readable interactables and stable pacing.

The useful takeaway is the investment across tools, diagnostics and testing as well as graphics. Only RE:Dox adoption and the inventory work are implemented in this pass; the priorities above are future work.

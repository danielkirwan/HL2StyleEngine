# Lighting Acceptance Handover

Date: 2026-11-04; memory investigation updated 2026-11-05. Acceptance pass recorded with outstanding items; this document does not authorize new systems.

| Gate | Verdict | Reason |
| --- | --- | --- |
| L: Existing lighting | Pending | Fresh automated lighting checks and native editor lighting edit/save/reopen checks pass; continuous gameplay/F2 input acceptance remains outstanding. |
| B: Baseline stability | Pending | Diagnostic 18-cycle six-room repeat grows 154.34 MiB; a later six-cycle repeat without the new probes grows 134.27 MiB. Both exceed the unchanged 128 MiB guard. Historical severe full-detail stalls remain unexplained. |
| New system upgrades | Blocked | Both gates must be complete under [ENGINE_SYSTEM_UPGRADE_PLAN.md](ENGINE_SYSTEM_UPGRADE_PLAN.md). |

No audio, interaction, editor-feature, navigation, AI prototype or preparatory system refactor is included. C# remains the runtime. Solver, timestep, physical parameters, shaders, source levels and lighting-cache formats are unchanged in this acceptance pass.

## Scope And Completion Work

### Memory Investigation (2026-11-05)

Sequential fresh-process short and long native-no-reload, fallback-reload and
native-reload controls are complete. Native long reload again fails memory at
**154.34 MiB**, despite zero gameplay intervals over 33.3 ms and flat model/
texture payloads. Managed commitment grows 151.57 MiB and fragmentation 125.86
MiB; these overlapping counters are not additive. Separate intrusive early/late
heap captures retain stable level/scenario snapshot counts and expose the
bounded test recorder's contribution to live-object growth. Measured UI drawing
and lighting preparation allocate substantial temporary data. No production
optimization or forced gameplay GC is introduced.

The final normal-desktop six-cycle control disables the new probes and again
fails memory (**134.27 MiB**) while passing gameplay pacing (16.666/16.812 ms
mean/P95, zero >33.3 ms). Its reported managed commitment falls, so this is not
fully explained by the earlier GC correlation: native/driver/process ownership
remains open. A preceding restricted launch has slow presentation and is retained
separately, not used to claim performance acceptance or an engine regression.

[Full commands, configurations, report paths, findings and limits](RELOAD_MEMORY_INVESTIGATION.md).
The heap run intentionally interrupts timing and is not a performance pass.
Fresh gameplay/startup, historical physics replay and strict/package lighting
image checks pass with the new observer disabled. Source levels/shaders/physics
remain unchanged; no rebake, cache migration or authoring step is required.
This does not complete manual Game/F2 acceptance or explain historical stalls.

### Native Review Follow-Up

After the user made the desktop available, reopening the private HS2Editor copy
reproduced overlapping panels and a tiny Scene view. The ImGui backend creates
its own context and starts its first frame in its constructor; the engine had
configured docking and loaded its layout into a context that the backend then
replaced. The correction configures an owned replacement context before its
first frame, rebuilds the font binding, preserves the currently visible default
font, saves layout only to the explicit app-local path, and releases the owned
context on disposal. The dockspace's central node now passes through the world
view instead of obscuring it with an opaque background. This is an existing
layout/lifetime bug fix, not a new editor feature or lighting algorithm.

The first constructor-only test caught the original lost-docking flag but
missed an ImGui first-frame assertion in an intermediate correction. The final
`check-imgui-layout` regression actually renders frames across two host
lifetimes, verifies saved placement and docking, and restores the test app's
previous layout file. It is included in the Lighting acceptance phase.

Native observations in `lighting-editor-review-20261104-145614-888` now verify
textured scene visibility, lighting off, Undo back to on, Redo back to off,
Save (dirty flag cleared), and a closed/reopened editor retaining the saved
off state and panel placement. The Prefabs tab lists `CeilingPointLight` and
`DoorFrame&Door`; this is browsing coverage, not new prefab placement/apply
acceptance. Only the private copy's spawn Z changed from -12 to -4 so its
editor camera starts inside the hall. Source project/level hashes are unchanged.

Evidence: `native-layout-before.jpg`, `native-layout-on.jpg`,
`native-lights-off.jpg`, `native-undo-on.jpg`, `native-redo-off.jpg`,
`native-saved-off-prefabs.jpg`, `native-reopened-off.jpg`, `native-game-off.jpg`.
The private game's saved-off state shows dark rooms with emissive indicators
and the health/suit HUD. Short injected keyboard taps did not reliably activate
F/F2; native mouse commands worked with longer presses. Do not count these as
successful native flashlight/F2 input or a continuous walk/carry/throw session.
Those manual checks remain pending, separately from scripted coverage.

Fresh full lighting rerun: `lighting-acceptance-20261104-153819-538-Lighting`.
All commands pass, including the new rendered-layout check, all 15 historical
render comparisons within 1/255, and both 600-step exact physics replays.
New representative images inspected: `shadow-stability-20261104-153925/`
`floor-corner-0-torch.png` and `pressure-crate-distance-18.png`.

The first follow-up baseline attempt, `lighting-acceptance-20261104-154516-793-Baseline`,
is **invalid**, not a pass or a valid performance failure: the private game
failed to close and overlapped its first soak. Its `INVALID.md` retains this
fact. Only the verified owned test processes were stopped before a clean rerun.
No user app was terminated. Baseline results below retain the earlier evidence;
the clean follow-up result must be recorded separately before acceptance.

Clean follow-up `lighting-acceptance-20261104-154729-227-Baseline`: the exact
frame-pacing BAT passes both short controls. The gameplay BAT fails because
the six-room six-cycle route grows **142.51 MiB** (128 MiB guard), with zero
model-payload growth. All three routes finish 72 actions with no gameplay
interval over 33.3 ms; native/fallback atmosphere memory growth is
43.66/22.84 MiB. Their reports are `gameplay-soak-20261104-154736-016-native`,
`154941-907-fallback` and `155147-381-native`. The last is a memory failure,
not a gameplay or frame-timing pass for the whole BAT. Protected inputs match.

The fresh long full-detail control `gameplay-soak-20261104-155834-340-native`
passes 18 cycles/216 actions: gameplay mean/P95 **16.662/17.417 ms**, zero
intervals over 33.3/100 ms, private growth **73.18 MiB**. This pass does not
identify a correction for the historical stalls or clear the six-room memory
failure. No scheduling/wait trace or memory root-cause fix is claimed.

Final long-control folder: `lighting-acceptance-20261104-155722-597-LongControls`.
Five of six commands pass; the six-room memory route fails. All protected
source levels, lighting shaders, physics sources and fixed-timestep hashes
remain unchanged. Loading intervals are separated by the existing test;
reported gameplay statistics do not assert stall-free level loading.

| Fresh long report | Gameplay mean / P95 | Maximum / >33.3 ms | Private growth | Verdict |
| --- | --- | --- | --- | --- |
| Full detail: `gameplay-soak-20261104-155834-340-native` | 16.662 / 17.417 ms | 27.009 ms / 0 | +73.18 MiB | Pass |
| Streaming: `gameplay-soak-20261104-160442-442-native` | 16.661 / 17.418 ms | 27.725 ms / 0 | +52.72 MiB | Pass |
| Six-room: `gameplay-soak-20261104-161050-816-native` | 16.661 / 16.924 ms | 27.743 ms / 0 | **+153.75 MiB** | **Memory fail** |

All finish 18 cycles/216 actions. Streaming performs 226 replacements with
zero package fallbacks and returns to full detail. Full detail performs zero
replacements. Both package traces have all 21,600 GPU samples valid, with zero
pending/skipped/invalid queries; their offline `pacing-analysis.json` files
are retained. This repeat does not reproduce the historical severe stalls,
but it neither explains them nor demonstrates a stall-specific fix.

The six-room first/final settled samples have identical model/texture payloads
and no pending reflection CPU/capture allocation. Committed managed heap grows
76.50 MiB, fragmentation grows 62.98 MiB, while estimated managed bytes grow
3.45 MiB. These counters are not additive and do not account for every byte of
the 153.75 MiB private growth. After timing finishes, diagnostic collection
leaves zero of 13,757 tracked old entities and 90 old meshes alive. This points
the next investigation toward reload allocation/heap commitment and native
reservations, but does not prove a particular leak or establish leak freedom.

Additional automated verification: Debug authoring build passes with zero
warnings/errors; `check-imgui-layout` passes in Debug as well as Release.
Debug log: `lighting-acceptance-20261104-153819-538-Lighting/check-imgui-layout-debug.log`.
Command: `.dotnet/dotnet.exe Tools/LevelAuthoring/bin/Debug/net10.0/LevelAuthoring.dll check-imgui-layout`.
The two-frame layout regression is functional coverage, not a performance run.

The implemented lighting under review comprises PBR material/colour-space handling; direct point/spot/directional lighting; texture/scalar and linked emission; deterministic one-bounce diffuse lightmaps and dynamic-object probes; prefiltered room reflections and room transitions; explicit on/off lighting scenarios; a bounded multi-point shadow atlas and independent flashlight; the camera-relative flashlight mount; geometric receiver-plane shadow correction and the six-room normal-only floor variant. Existing optional bloom/grading/fog is checked without expanding it.

This pass fixed a **validation-tool defect**, not a lighting shader: Release now correctly omits D3D debug validation, but two GPU correctness suites assumed `ID3D11InfoQueue` always existed. `GraphicsValidationHost` explicitly opts those suites into a debug device and restores the diagnostic environment immediately after construction. Normal gameplay remains non-debug in Release and VSync is unchanged. The original failure is retained in the first acceptance log.

The user explicitly approved retaining the currently authored atmosphere look. Its fixture expectation now records fog density **0.1**, start **3.102 m**, height falloff **0.034** and maximum opacity **0.744**. The original numerical test preset remains separate. No source JSON or image reference was rewritten to obtain the pass. The initial preset-mismatch failure is retained.

`Tools/LevelAuthoring/RunLightingAcceptance.ps1` invokes existing checks sequentially, retaining commands, exit codes, logs and protected-input SHA-256 snapshots. `PrepareLightingEditorReview.ps1` prepares an isolated native editor review with private documents/layout and shared model inputs; it does not modify the user's editor project.

## Automated Evidence

All QA paths below are relative to `Tools/LevelAuthoring/bin/Release/net10.0/qa/` in the workspace. Logs include complete commands and reported capture directories. Performance workloads run sequentially with no concurrent builds from this acceptance task. Desktop/OS cache conditions are uncontrolled; these are not cold-load measurements.

| Evidence | Result |
| --- | --- |
| `lighting-acceptance-20261104-141737-367-Lighting` | Release build and 27 of 28 initial check commands pass. Multi-shadow check fails before rendering on the missing debug interface; corrected separately. Protected inputs unchanged. |
| `lighting-acceptance-20261104-142246-278-ValidationDebug` | Debug-interface correction passes multi-shadow, PBR shadow, legacy render and 55 atmosphere GPU checks. Historical atmosphere fixture preset mismatch is retained; subsequently reconciled with user approval. Protected inputs unchanged. |
| `lighting-acceptance-20261104-142531-606-Baseline` | Both requested BATs executed. Gameplay BAT passes; frame-pacing BAT fails the full-detail control and stops before streaming. Protected inputs unchanged. |
| `lighting-acceptance-20261104-143535-033-LongControls` | Approved atmosphere validation passes 24 checks; short streaming control passes. All three long controls fail frame gates. Six-room settled memory also exceeds its guard. All protected inputs unchanged. |
| `lighting-acceptance-20261104-150128-811-ValidationDebug` | Final Release build: zero warnings/errors. All five targeted shadow/legacy/atmosphere commands pass, including 24 data and 55 GPU atmosphere checks. Protected inputs unchanged. Earlier failures remain retained. |
| `lighting-acceptance-20261104-153819-538-Lighting` | Fresh build plus 31 lighting/layout/regression commands pass. Protected inputs unchanged; additional Debug layout log also passes. |
| `lighting-acceptance-20261104-154729-227-Baseline` | Clean repeat: gameplay BAT fails six-room memory; frame-pacing BAT passes. Protected inputs unchanged. |
| `lighting-acceptance-20261104-155722-597-LongControls` | Full-detail/streaming long controls pass; six-room long control fails memory. Protected inputs unchanged. |

Detailed lighting coverage:

- 15 material, 126 PBR HDR and 41 emission checks: normals/UVs, colour spaces, finite values, roughness, direct lights, texture/scalar emission, links, persistence and prefab isolation.
- 282 indirect-bake checks and 50-probe coverage: one-bounce occlusion, directional/trilinear probes, thin-wall visibility, stale/cancel fallback, save failure and actual GPU sampling.
- 98 reflection, 50 room-capture, 106 transition and 953 reflection-streaming checks: validity/invalidation, roughness, fallback, room selection and bounded ownership. Poll-dependent streaming assertion counts are not feature counts.
- 797 lighting scenario/cache/undo checks; actual on/off/on, idle, saved override restoration and cancellation images match within 1/255. Diffuse-only energy remains visible, and the off state does not reuse the on-state atlas.
- 69 shadow-fixture checks; 64 point/spot contact views; nine crate approach/retreat views and eight flashlight orbits match uncached/unculled references. All 14 fixture owners remain represented in this test. The corrected multi-shadow suite covers 98 logical faces in 26 physical layers and dirty-neighbour/quality changes.
- Flashlight mount: 20 camera poses, continuous wall retraction, close-up crate/railing views and three held-crate poses. These deterministic poses are not a continuous manual carrying session.
- Floor: 4/8/12 m controlled views retain material detail, with **zero measured false self-shadow pixels**; corrected material has 8/2/4 dark pixels in the clearly lit masks, compared with 616/532/452 for the original normal map. Actual corner captures also reviewed.
- 39 cache reader/ownership checks and 38 lighting dependency checks cover concurrent reads, stale/missing/corrupt/truncated data, invalid radiance, cancellation, fallback and retained-result isolation. No cache validity guard is bypassed.
- Three strict/package diffuse-lighting image pairs match exactly. Both app outputs pass 62 packaged shader/model/cache checks each.
- Three prepared distant-room reveals and twelve adjacent-room first/early/settled image comparisons match full detail exactly. F2 restores full-detail editing and F6 restores the comparison snapshot. This does not promote experimental texture streaming.
- All 15 historical Game/F2/HS2Editor images remain within 1/255; reference images are unchanged. Both saved 600-step physics replays match state/contact/bounds hashes exactly (`physics-replay-20261104-142117-944`, `142122-041`).

## Earlier Baseline Results

Normal-desktop Release, RTX 3060/D3D11, actual 1920x1009 client, normal VSync and full quality. Loading intervals remain separate; original acceptance limits are unchanged.

| Workload/report | Gameplay mean / P95 | >33.3 ms / >100 ms | Private growth | Verdict |
| --- | --- | --- | --- | --- |
| Native UI, six cycles: `gameplay-soak-20261104-142540-476-native` | 16.667 / 17.166 ms | 0 / 0 | +74.16 MiB | Pass |
| Fallback UI, six cycles: `gameplay-soak-20261104-142747-056-fallback` | 16.676 / 17.220 ms | 1 / 0 | +25.03 MiB | Pass |
| Native puzzle level, six cycles: `gameplay-soak-20261104-142952-940-native` | 16.673 / 17.111 ms | 1 / 0 | -15.48 MiB | Pass |
| Full-detail frame-pacing control: `texture-level-20261104-143207-735` | 16.679 / 17.337 ms | 2 / 0 | Not this test's metric | **Fail** |
| Short streaming control: `texture-level-20261104-143545-330` | 16.661 / 17.158 ms | 0 / 0 | Not this test's metric | Pass |
| Full-detail package, 18 cycles: `gameplay-soak-20261104-143650-840-native` | 16.702 / 17.127 ms | 13 / 1 | +32.20 MiB | **Fail** |
| Streaming package, 18 cycles: `gameplay-soak-20261104-144300-366-native` | 16.679 / 17.472 ms | 11 / 0 | +64.45 MiB | **Fail** |
| Native six-room puzzle/memory, 18 cycles: `gameplay-soak-20261104-144909-753-native` | 16.675 / 17.703 ms | 5 / 0 | +140.38 MiB | **Fail** |

Each six-cycle route completes 72 scripted actions, using isolated saves. The gameplay BAT also passes 54,497 exact physics geometry/contact checks, 1,000 transformed-bound cases and 24 gameplay/startup checks. Approximately 60 FPS averages are not minimum-FPS guarantees.

Each long route completes 18 cycles, 216 actions and 21,600 measured frames. Package runs have zero fallbacks; full detail has zero replacements, while streaming has 234 and restores full detail. These behavioral results do not override failed frame gates. Both package memory samples are below the existing 128 MiB private/8 MiB model growth limits. The six-room +140.38 MiB private sample is **over the limit**, even though the tool throws on clustered hitches before reaching its memory assertion; it must not be counted as a memory pass. Model growth is zero there. Post-run diagnostic collection leaves zero of 13,796 tracked old entities and 90 old meshes alive, but does not explain the private-memory growth or prove leak freedom. No collection was forced during gameplay.

Latest severe full-detail event: **270.911 ms wall interval**, **270.839 ms profiled host work** at frame 20600, with 132.454 ms physics wall time across three ticks, 25,490,020 physics execution cycles, a 5.125 ms GPU command interval and no GC pause. The window was unfocused in this sample; the earlier 119.779 ms failure was focused, so focus is not a sufficient diagnosis. All 21,600 measured GPU results are valid, with zero pending/skipped/invalid samples. `pacing-analysis.json` beside this run's `frames.json` retains the breakdown. The streaming run also has its analysis retained.

The [frame-pacing investigation](FRAME_PACING_GUIDE.md) remains open. The evidence supports investigating scheduling/waits without identifying a particular competing process, driver or engine wait. No priority/affinity changes, solver changes, dropped ticks, quality reductions or relaxed gates are used. Other desktop applications were open at native-review time; these were not controlled or closed, and this is not evidence that they caused the measured stalls.

## Visual And Native Review

**Inspected generated images, not a manual playthrough:**

- [Flashlight floor corner](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-stability-20261104-141837/floor-corner-0-torch.png) and [second angle](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-stability-20261104-141837/floor-corner-2-torch.png): textured floor/walls remain visible inside the beam without the previous wide black bands; areas outside the beam stay dark.
- [Distant crates/fixtures](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-stability-20261104-141837/pressure-crate-distance-18.png): fixture-lit hall and distant props remain present; automated approach/retreat covers the intermediate views.
- [Held-crate flashlight view](../Tools/LevelAuthoring/bin/Release/net10.0/qa/flashlight-mount-20261104-141857/held-0.65.png): textured held geometry remains illuminated; near-eye shadows are largely concealed behind the object as expected.
- [Indirect-only contribution](../Tools/LevelAuthoring/bin/Release/net10.0/qa/lighting-switch-20261104-141826/bounce-only.png): low but nonzero bounced light, without added global ambient fill.
- [Standalone scene renderer](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-views-20261104-141940/sixRoomShadowTest-hs2editor.png), [game](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-views-20261104-141940/sixRoomShadowTest-game.png) and [F2](../Tools/LevelAuthoring/bin/Release/net10.0/qa/shadow-views-20261104-141940/sixRoomShadowTest-f2.png) were inspected: textured architecture, fixtures and shadows render. F2 includes its authoring markers/blue helper surface; these views are not expected to be identical to gameplay. Scene-render captures do not validate native dock layout/input.
- [Fixtures on](../Tools/LevelAuthoring/bin/Release/net10.0/qa/lighting-switch-20261104-141826/on.png) and [fixtures off](../Tools/LevelAuthoring/bin/Release/net10.0/qa/lighting-switch-20261104-141826/off.png): the off image is dark apart from independent emissive indicators, without visible stale room illumination.

Native editor/game interaction observations are kept separate from the automated checks. A full continuous human walk/orbit/carry/throw/break review and controller input acceptance are not implied by deterministic screenshots or scripted actions.

**Initial native HS2Editor review (historical):** isolated review directory `lighting-editor-review-20261104-145614-888`; initial report `review-summary.json`. The actual app opened with Hierarchy/Levels/Project to the left, Scene in the middle, Inspector/Toolbar to the right and Content Browser below. A scene click selected the textured wall with a yellow outline rather than a filled yellow object. The initial camera is outside the hall facing its dark exterior wall; this is not a white-material failure. Desktop focus changed during subsequent input, so automation stopped without acting on other apps. The later native follow-up above supersedes this initial review for lighting controls, undo/redo/save/reopen and layout. Existing automated data/prefab tests still do not substitute for continuous native gameplay or full prefab authoring checks.

The private copy initially lacked the `runtimes` native-dependency folder and failed to locate `cimgui`; copying the existing dependencies corrected that QA setup. The preparation script now includes them. Source project/level hashes remained identical, and no source editor layout file was created. Only the isolated review process was closed; the user's other apps were left alone.

## Preserved Limits And Authoring

- Up to 32 active direct lights, 16 selected shadowed points plus two spots; finite pool handoffs remain possible above the configured budget. The six-room test uses all 14 point owners, 512-square packed point faces, 1024-square spots and a 104 MiB atlas. This is not unlimited lights, area-light penumbrae or ray tracing.
- Baked indirect light is a fixed-scene one-bounce approximation. Moving doors/props do not rebake it; fixed room reflections are not realtime mirrors. Unsupported mixed lighting scenarios use dark fallback, not a stale bright state.
- At most four authored scenarios; two reflection banks may remain warm. Alternate diffuse first-use preparation is not universal prefetch, and cold/missing data can remain dark until compatible data is ready.
- Texture streaming stays opt-in. Normal play retains full detail. Prepared transitions cover their declared dependencies, not arbitrary world readiness or geometry/physics streaming.
- Cache-reader scratch reuse is bounded but is not a cap on total process memory or proof of leak freedom. No forced gameplay GC is added.

**No rebake, cache migration, import or save reset is required for this pass.** It changes validation tooling/expectations and corrects the existing ImGui context/layout lifecycle, not lighting inputs. Rebuild the editor/game to use the UI correction; do not reset a working user layout. Existing authoring rules remain: changing fixed geometry/material/light inputs requires matching diffuse/reflection regeneration; a changed shadow shader invalidates its dependent captures. Build after legitimate cooking to refresh outputs; never recook merely to hide a failed comparison.

## Reproduce And Remaining Acceptance

From the workspace, run sequentially:

```powershell
& .\Tools\LevelAuthoring\RunLightingAcceptance.ps1 -Phase Lighting
& .\Tools\LevelAuthoring\RunLightingAcceptance.ps1 -Phase ValidationDebug
& .\Tools\LevelAuthoring\RunLightingAcceptance.ps1 -Phase Baseline
& .\Tools\LevelAuthoring\RunLightingAcceptance.ps1 -Phase LongControls
```

`Baseline` invokes the exact requested BATs, using EOF only to return from their final pause. A failed BAT is not bypassed or labelled a pass. Separate follow-up commands can collect the evidence skipped by its early exit. `LongControls` runs the approved authored-atmosphere check, the short streaming control and three existing long routes (full-detail package, streaming package, native six-room puzzle/memory).

Offline analysis after all performance runs:

```powershell
& .\.dotnet\dotnet.exe .\Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll analyze-frame-pacing .\Tools\LevelAuthoring\bin\Release\net10.0\qa\gameplay-soak-20261104-143650-840-native\frames.json
& .\.dotnet\dotnet.exe .\Tools\LevelAuthoring\bin\Release\net10.0\LevelAuthoring.dll analyze-frame-pacing .\Tools\LevelAuthoring\bin\Release\net10.0\qa\gameplay-soak-20261104-144300-366-native\frames.json
```

Remaining acceptance, in order:

1. Complete uninterrupted native Game/F2 input and continuous close/distant/grazing, carry/throw/break, doorway and repeated fixture/torch checks. Complete dock/resize and prefab-instance reference authoring checks. Standalone lighting edit/undo/redo/save/reopen is now observed passing; do not repeat it as if it were still untested. Keep normal full detail separate from streaming.
2. Investigate the reproducible six-room reload private-memory growth using allocation/heap and native-reservation evidence. Preserve the 128 MiB/8 MiB guards and normal GC policy; do not use forced gameplay collection to manufacture a pass.
3. Correlate a bounded OS scheduling/wait trace with a renewed severe full-detail stall. The clean repeat is good evidence, not a demonstrated fix for prior failures. Repeat unchanged BATs/long controls after any correction, without changing physics or quality.
4. Record explicit acceptance before freezing the lighting baseline and clearing either gate. New system work remains blocked.

Do not freeze an accepted lighting baseline or begin new systems while either gate remains pending. After acceptance, preserve the recorded shader/source hashes, reference images, runtime settings, cache validation/fallback rules and physics replay results for subsequent upgrades.

Only after both gates clear, start Priority 1 of the systems plan (small C#
contracts and validated asset definitions), then Priority 2 (spatial audio).
Neither implementation nor preparatory refactoring is part of this handover.

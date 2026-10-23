# Unit C Reflection References

Reviewed 2026-10-23 at 1280x720 on D3D11/RTX 3060. Game, F2 and standalone scene renderers use PBR plus the synthetic studio. These are separate from the untouched Legacy (`PbrStage0`) and reflection-off (`PbrDirect`) references.

The initial unit C captures were reviewed, then deliberately updated after a geometric back-face energy guard: rear-facing surfaces must not gain specular environment light. Checking perturbed normal-map normals instead caused grazing speckles and was rejected. The final guard uses the interpolated geometric hemisphere and retains stable normal-mapped reflections. Final captures were visually inspected in all three views before acceptance. F2's existing spawn helper covers the foreground at this camera.

Run `validate-reflection-reference`; maximum accepted RGB difference is 1/255. Do not regenerate references merely to hide a regression. This is automated scene-render validation, not a manual editor/input playtest.

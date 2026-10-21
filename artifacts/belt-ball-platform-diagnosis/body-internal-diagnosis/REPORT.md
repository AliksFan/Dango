# Case 10 body internal integration diagnosis

Scope: diagnostic copies only, no tracked production changes. No windows, keyboard/mouse, or original application processes used. All diagnostic native processes exited.

Frozen input: ../native-repro/cone-top-run/case10-failure.bin. Format begins room_world (16640 bytes), safe belt_body (11176), attempt belt_body (11176); the remaining captured collection fields are not used by this replay. The harness creates valid local handles, restores the frozen world/body with its newly created body_handle, clears room fault, and executes the original dt=.0166666f with zero controls. This exactly replays accepted tick790 -> refusal -705/-605 before room contact work.

## Confirmed first failure

- dango_belt.c belt_integrate, solver site5: after 40 iterations the endpoint normalized gradient residual is 1.7806007823071528 > unchanged 2e-7.
- Substep0 / stage2; room geometry work0. This is separate from collection PGS and from the independently diagnosed case11 certificate and managed rebase249 wall refusal.
- Baseline uses the original 40 solve iterations /20 line-search trials. Early target 2e-9, hard residual2e-7, material and continuous shape certificates all retain their original semantics.
- Exact baseline replay: replay.log and final-baseline-analysis.log. Iteration21 rejects the first full BFGS direction with shape CROSS; the original retry resets to GN and sets acceleration_blocked permanently. GN continues with short descent steps and correctly refuses at the hard residual gate.

## State and derivative evidence

The frozen accepted shape is geometrically certified but highly bent:
- Minimum sampled curve derivative .018157526488928544 (material threshold radius*.02, radius approximately .7111).
- Elastic terms: stretch2.6908256222, bending45047.4714966843, radial1.5560955649, area.7652275468, guard.2415918524.
- A single bending row5 contributes45045.8069356746, more than99.99% of bending energy. This is sample2 /theta=2*(2*pi/64).
- Failed endpoint bending22126.5283935087, objective31580.4963590615, worst coefficient31 analytic gradient -12307.610082983, normalized1.78060078230715.
- Central finite differences at1e-6/1e-7/1e-8 approach this gradient: relative errors .0018594 /1.8591e-5 /1.7707e-7. This supports a true unresolved objective gradient, not a residual reporting artifact.

## Bounded diagnostic variants (all still refused)

These are experiments in copied source, not proposed production fixes, and none establishes a solved full step.

| Variant | Final residual | Evidence |
|---|---:|---|
| Original | 1.78060078230715 | final-baseline-analysis.log |
| Translate x origin to curve center=0, otherwise original | 1.78060078030233 | shift-origin.log |
| Reallow BFGS after a rejected proposal | .127147657870804 | retry-bfgs.log |
| Earlier BFGS activation | .290601004834162 | early-bfgs.log |
| Backtrack the rejected BFGS direction instead of immediate GN reset | .204195078922714 | backtrack-bfgs.log |
| Earlier activation plus BFGS backtracking | .558094468108291 | early-backtrack-bfgs.log |
| Diagnostic finite-difference full Hessian, use only if SPD else GN | .0205801342790122 | full-hessian.log |
| Same, finite difference1e-8 | .0206335700775409 | full-hessian-1e8.log |
| Diagnostic SPD-modified numerical Hessian metric | .169777536422491 | modified-hessian.log |

The x-origin experiment excludes a simple world-x~6 cancellation explanation for this frozen failure. It does not prove universal translation invariance. Full Hessian is indefinite until iteration35 along the GN trajectory, supporting a genuinely nonconvex/stiff local objective; a mere additional BFGS retry is insufficient. No experiment relaxed a production acceptance threshold, changed the objective/forces, or accepted an uphill/uncertified trial. The numerical Hessian experiments change direction-computation cost and are not runtime acceptance evidence.

## Conclusion and limits

Confirmed: original integrator fails to converge accurately on this highly bent accepted state under its current bounded direction/backtracking policy; the refusal is real and occurs before collection contact. Not established: a legal stationary solution reachable under every current constraint within40/20, or a minimal sufficient production fix. These experiments do not prove that the same objective has no legal solution. No source integration acceptance, full long-path acceptance, or main merge is justified.

## Reviewable next designs (separate authorization)

1. Prefer a bounded integrator design: handle dominant bending geometric curvature and coordinate conditioning explicitly, using a safeguarded descent metric under the same objective, continuous certificate, total cost budget, and hard residual gate. Freeze this state as an independent red regression. Benchmark against original trajectories and all existing gate/failure negatives before choosing implementation.
2. Review the preceding contact-position projection separately: it admits shape-valid states with very large localized bending energy. An elastic-aware correction could avoid feeding the stiff state into the next integrator, but changes physical behavior and needs a design; do not silently clip curvature, relax shape gates, or add hidden damping.
3. A bounded frame rollback/substep refinement is an alternative recovery design. It changes configured step/trajectory semantics and cost, so requires its own explicit total-work contract and validation rather than increasing40/20 or retrying indefinitely.

Files: inspect-body.c, src/ copied/instrumented native sources, CMakeLists.txt, build/, and named logs. Reproduce original instrumented replay with build/Release/inspect_body.exe <frozen-bin> 0. Variant IDs1,2,3,4,5,6,8 select the documented isolated diagnostic changes. Optional fourth argument translates x. All outputs remain under the ignored diagnosis directory.

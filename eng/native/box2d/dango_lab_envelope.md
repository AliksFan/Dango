# Isolated analytic envelope ground candidate

Build option `DANGO_PHYSICS_LAB_ENVELOPE`, output target `dango_physics_lab_envelope`.
The ordinary lab and production targets do not define this option. No material, mass,
input, range-cache, force, damping, timestep, speed cap, shape tolerance or 384-sweep
limit is changed. Only this target disables the floor shape mask (walls retain their
original filters) and replaces the discrete floor constraint rows.

For each node `q_i = p_i + radius * normalize(p_i-core)`. Each adjacent quadratic
B-spline segment has start `(q_prev+q_i)/2`, control `q_i`, end `(q_i+q_next)/2`.
Every segment's endpoint and interior derivative-zero roots are evaluated analytically
for world Y extrema. The global minimum's true normal and fixed-material-point tangent
Jacobians include radial extrapolation's core derivative. They distribute position and
velocity corrections by inverse mass. Position correction never changes velocity.
Normal impulses are upward only. Tangential impulses oppose virtual surface slip and
spend a cumulative absolute Coulomb budget per solve, rather than replenishing the
budget each sweep. The point and Jacobian are rebuilt each sweep. Shape rows, topology,
area and bounded-failure behavior remain active.

`grounded` is the analytic envelope gap <= existing 0.035 world proximity; ground
contact count is 0 or 1 from the analytic gap <= the existing geometric tolerance.
It is explicitly a geometric envelope contact, not a Box2D impulse receipt. The floor
Box2D contact count is recorded separately and must be zero. Candidate scope is one
body on a flat floor. Multi-body world creation, static platform creation/prepare,
ball addition and furniture collection admission are rejected. The legacy `dango_collision_create` entry point is unconditionally rejected by this
candidate, including count 1, so it cannot manufacture a capacity-2 handle and bypass
world admission. Platform and collection admission also reject unconditionally under
the candidate compile guard; the UI uses world creation with capacity 1.

The C1 curve and scalar minimum are continuous. With multiple separated equal minima,
the deterministic first segment can change contact identity and Jacobian. The candidate
does not claim global contact identity continuity or a multipoint contact patch. The
tangent derivative holds the selected material coordinate t fixed: differentiating
argmin t would confuse moving the selected geometric point with material surface slip.

## ABI and diagnostics

The existing lab ABI/version 1 and controls9/StepLab remain. Additional exports:

- `int dango_physics_lab_ground_model(void)` returns 1 for this candidate.
- `int dango_physics_world_get_lab_ground_stats(void*, int id, float* out, int capacity)`
  requires capacity >= 32, returns 0 on success, -410 invalid buffer, -412 unknown ID.

The 32 floats below are distinct from the old 16 lab drive stats. Impulses, energy
phase sums and corrections accumulate within the most recent StepLab. Points, speeds
and extrema are the most recent observation. Energies are translational KE for all 17
bodies; normal/friction before/after sums cover the same impulse applications. External
impulse residuals compare the distributed generalized impulses with the advertised
world-space reaction (not finite-difference whole-frame momentum, which also includes
gravity, Box2D joints, damping and user forces).

| Index | Meaning |
|---|---|
| 0,1,2 | Exact minimum point X, Y, and Y-ground gap |
| 3 | Sum of upward normal impulses |
| 4 | Sum of absolute tangential impulses |
| 5 | Configured coefficient of friction |
| 6 | Sum of L2 norms over all 17 position corrections per floor projection |
| 7 | Maximum single-body floor position correction |
| 8 | Total correcting shape/floor sweeps |
| 9,10 | Grounded proximity and geometric contact count (0/1) |
| 11,12 | Summed total KE before/after normal impulse phases |
| 13,14 | Summed total KE before/after friction impulse phases |
| 15,16,17 | Distributed external impulse residual X, Y, torque about world origin |
| 18 | Maximum per-body net position correction in a shape-only sweep |
| 19 | Minimum gap observed before correction during the frame |
| 20 | Maximum remaining penetration after a successful solve |
| 21,22 | Last normal virtual speed before/after the normal impulse |
| 23,24 | Last tangential virtual speed before/after the friction impulse |
| 25,26 | Last minimum segment index and material t |
| 27 | Real Box2D floor contact count (must be zero) |
| 28 | Analytic maximum curve Y |
| 29 | Sum of KE changes from shape-only sweeps, excluding floor phases |
| 30 | Constraint solver fault flag |
| 31 | Schema version 1 |

The isolated library includes a contract-test-only collapse export under
`DANGO_LAB_ENVELOPE_CONTRACT_TESTS`; it is used to verify a real world fault remains
latched. The tests also cover dense-reference geometry, segment seams, cyclic renaming,
Jacobian finite differences, force/torque distribution, no-support/zero-friction,
Coulomb budget/dissipation, position/velocity separation, airborne bitwise legacy-lab
equivalence, actual rest/walk/stop/jump worlds and unsupported scene rejection.
Pass the historical lab DLL path as the test executable's first argument to additionally
compare the newly rebuilt default lab with that historical DLL for 360 ground frames.

These contracts do not establish reduced motion jitter, improved shape preservation,
production support or visual acceptance. The first default-material Core A/B experiment
found no jitter improvement; its failed improvement metrics are retained by the lab.

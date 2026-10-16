# Continuous belt calibration ABI v1

This is an additive, isolated experiment interface. It does not change the
original 164-byte config, version 1, create/step/read ABI, mass, controls, or
default trajectory. The v1 diagnostics build is retained at
`artifacts/physics-lab/belt-calibration-native`; the optional original-ground-jump
correction builds separately at `artifacts/physics-lab/belt-calibration-v2-native`;
preserve `belt-native` as the pre-calibration binary. No product DLL is replaced.

`dango_belt_calibration_schema()` returns immutable UTF-8 JSON with ordered
parameter and diagnostic names, counts, defaults, units, and unsupported legacy
config fields. `create_calibrated(config,x,y,short_bones,params,11,out)` accepts
finite nonnegative parameters at most 10000 and leaves `out` untouched on error.
`read_diagnostics(handle,output,count)` requires capacity >=48. The original
create chooses the same defaults, but callers need not load any new exports.

## Parameters

| Index | Name | Default | Unit and exact meaning |
| --- | --- | --- | --- |
| 0 | radialStiffnessScale | 1 | Multiply 16 material-to-core radial spring k |
| 1 | radialDampingScale | 1 | Multiply those radial c |
| 2 | beltStretchScale | 1 | Multiply continuous strain stretch k only |
| 3 | beltBendScale | 1 | Multiply continuous curvature bend k only |
| 4 | areaScale | 1 | Multiply one-sided compression area k |
| 5 | shortBoneStiffnessScale | 1 | Multiply optional 16 short-bone k |
| 6 | shortBoneDampingScale | 1 | Multiply optional short-bone c |
| 7 | shapeDampingScale | 1 | Multiply continuous stretch and bend c only |
| 8 | overallDamping | 0 | 1/s; multiply every generalized velocity by `1/(1+h*d)` before implicit integration |
| 9 | groundedBrake | 0 | m/s²; remove COM vx clamped to `[-a*h,a*h]` by the same vx translation on belt and core |
| 10 | groundedHorizontalDamping | 0 | 1/s; after COM braking multiply every horizontal generalized velocity by `exp(-d*h)` |

Stiffness and damping multipliers are independent. Changing a k multiplier does
not silently change its c. Short-bone multipliers have no effect with short
bones disabled. No parameter changes actuation, jump impulse, cooldown, control
ramp, force distribution, or collision support semantics.

Brake/damping indexes 9/10 activate only if the exact continuous minimum is
within 0.001m of the floor and every explicit control is inactive (horizontal,
downward, Space, drag, rise, passive-spin toggle). An accepted jump also prevents
the release brake. There is no position lock or visual smoothing.

The legacy adapter `dango_material_contact_adapter.h:134-147` removes a shared
COM vx delta capped by `35*h`, then (unless the no-internal-horizontal-damping
variant is compiled) multiplies **all** vx by `exp(-60*h)`. Thus settings 35/60
recover those two velocity operations in the continuous coordinate basis. It is
not merely relative damping: a residual COM vx also decays. The adapter's
passive-spin operation, constraint projection, and solver order are separate;
these parameters do not claim complete old-solver equivalence.

The old bridge applies `linear_damping` to core and node Box2D bodies at creation
(`dango_box2d_bridge.c:2609,2648`). The belt's legacy config field remains unused;
index 8 is the explicit new calibration capability. Legacy radial and perimeter
hard limits likewise remain unsupported, as the schema states.

## V2 correction: actual original ground Space rule

The initial v1 path audit incorrectly attributed old-half Space to the isolated
bridge's core-only `jump_impulse` branch. That branch exists at
`dango_box2d_bridge.c:2904-2911`, but **the actual old-half world does not execute
it by default**. The v1 baseline and its failing airborne gate are preserved;
they expose the mistaken port, rather than establish the original product rule.

The verified call chain is `LabBeltCalibration.cs:191-193` creating a
`DangoPhysicsWorld`, `DangoPhysicsWorld.Lab.cs:117,223` calling the exported
`dango_physics_world_step_lab`, `dango_collision_review.c:1557` calling
`collision_world_step_impl`, and line 1436 calling `dango_collision_step`.
Default `legacy_interaction_flags=0` comes from calloc at line 1237; therefore
line 1038 passes jump=0 into the old isolated bridge. Actual launch is lines
1048-1050 calling `collision_launch` (lines 512-552). A normal ground jump adds
7.7m/s to every body's current vy, using upward impulses `mass*7.7`, for all
16 nodes **and** the core. It preserves current vx and internal velocity
differences. It does not read `cfg.jump_impulse`. A separate multi-body support
case uses 6.0m/s minus existing relative core vy and applies opposite support
impulse; that multi-body case is outside this single-body belt experiment.

The old-half target definition (`CMakeLists.txt:436-449`) enables PHYSICS_LAB,
LAB_REGIONS, LAB_HALF and BODY_DRIVE_REVIEW, without ENVELOPE, ARCS or the
material contact adapter. Actual release braking is
`dango_collision_review.c:700-733`; its two velocity operations are the same
35m/s² COM brake and 60/s horizontal damping discussed above. The adapter was
a matching formula, not the old-half execution path.

V2 adds `dango_belt_set_original_ground_jump(handle,int enabled)`. The default
is 0 and keeps v1 trajectory comparison possible. Set 1 after create and before
the first successful step to use the verified original ground jump. The schema
declares this capability; there is no adjustable jump-strength parameter.
Invalid flags or changes after stepping return -600 without mutation.

The continuous equivalent of the 17 body's identical delta-v is to add the
same `(float)7.7` to only the Fourier translation vy and the core vy. Every
shape-mode velocity remains unchanged. Actual injected momentum is total mass
times that speed, and diagnostics record the actual whole-body KE injection,
requested impulse, accepted impulse and momentum change.

The old-half bounded-floor read path (`dango_collision_review.c:773-804,818`)
accepts ground support within 0.035m of node surface height. The corrected
single-body rule uses the continuous curve's true floor gap <=0.035m, together
with existing contact state. This is a continuous equivalent of that slack,
not a requirement that exact zero-gap contact must already exist. Old-half
increments cooldown by h before the substep-zero launch and resets it at
launch. Thus acceptance tests `cooldown+h>=threshold`, and an accepted frame
ends with cooldown `(substepCount-1)*h`. V2 reproduces this timing only in the
opted-in mode; mode 0 retains the pre-correction experiment's timing. Discrete
node floor height and continuous true floor height differ geometrically, so
near-threshold acceptance is not claimed bit-identical across the two models.
Accepted Space suppresses W/S vertical force for that frame as before.

## Diagnostic interpretation

All 48 diagnostics are reset each successful step. Read the schema for exact
indexes. Initial/final energy and momentum fields are frame endpoints. Work,
impulses, losses, and stage deltas sum over substeps. Maximum correction is a
maximum. A failed step rolls back the entire body and previous diagnostics.

KE is exactly `sum(m_j*v_j²/2)` in the analytic Fourier mass metric, including
the core; momentum is the continuous integral plus core. Elastic energy is the
actual sum `k*r(q)²/2`. Gravity potential is `g*(ringMass*q1+coreMass*q35)`.

* Index 8 is negative damping endpoint-force work, summed as
  `c*(r_new-r_old)/h * J_new dot (q_new-q_old)`. Positive means energy removed.
  Nonlinearity means it can differ from index 9 and is not assumed positive.
* Index 9 is `sum c*(r_new-r_old)²/h`, a nonnegative strain-metric dissipation.
  Old stats[33] is the **incremental objective potential**, exactly half this
  quantity. It must not be labeled physical total energy dissipated.
* Indexes 10/11 are actual KE changes from explicit overall damping and the
  combined release brake/horizontal damping. Negative means energy removed.
* Indexes 12/13 are sums of KE immediately before and after velocity contact;
  their difference is index 14. Contact impulses also provide actual delta P/L.
* Indexes 15/16 are actual elastic/gravity potential changes from position-only
  floor repair. Projection does not update velocity, but can change potential
  and angular momentum (index 19). It is not assumed energetically neutral.
* Indexes 20-26 separately report requested/accepted jump and actual core
  impulse, momentum before/after, delta Py, and KE injection.
* Index 27 is all applied force endpoint work, including gravity and drag;
  index 28 is implicit integration delta `(KE+elastic)` minus that work. It
  contains internal damping and numerical integration effects and must not be
  relabeled damping alone.
* Indexes 44-46 are implicit-integration linear/angular momentum changes minus
  the external force endpoint impulse/torque. They expose solver residuals and
  time-integration effects, not contact or explicit damping (separate stages).

The exact frame bookkeeping identity (apart from float readback rounding) is

`delta(KE+elastic+gravity) = d25+d10+d11+d27+d28+d14+d15+(d7-d6)`.

Do not add d16 again: the final-minus-initial gravity potential already includes
projection gravity work. The diagnostic-only energy evaluations do not change
solver acceptance or correct its state.

## Gates and limits

`dango_physics_lab_belt_calibration_tests [preserved-dll-path]` checks calibrated
FD gradients, independent parameter wiring, rigid objectivity, no-force
deformation from zero initial velocity, physical damping removal, exact Padé
decay for pure translation, 60Hz default ABI equivalence, failed-create atomicity,
real Space impulse, and the stage energy ledger. An optional preserved DLL
comparison checks all 644 readback floats bit-for-bit over a 60Hz control path.
V2 adds gates for the exact whole-body impulse, unchanged shape modes, complete
force-free curve liftoff, W/S arbitration, first-substep cooldown timing,
ground slack, and immutable mode after the first successful step.

An additional explicitly labeled diagnostic starts a slightly deformed free
rotation at 1.1rad/s with no gravity, contact, or physical damping. At 60Hz and
four substeps, 1s changes angular momentum from 0.124359976 to 0.123907915
(-0.36351%); at 120Hz and the same four substeps, it ends at 0.124141338
(-0.17581%). Linear momentum drift is below 1e-18. This distinguishes exact
internal-force objectivity from the existing first-order backward-Euler
integrator's angular-momentum and mechanical-energy drift. Arbitrary free
rotation is **not** certified as exactly conserving angular momentum. The
diagnostic exposes the limitation without silently changing the solver.

The unchanged original contract executable still contains a whole-curve
airborne gate. Both the preserved baseline executable and new default executable
fail it on the current walk/release/jump sequence, with peak gap approximately
`2.22e-16`. This is a recorded baseline failure, not a passed calibration gate.
Substep/time-step reduction is diagnostic only; production acceptance remains
the specified 60Hz and original substep count.

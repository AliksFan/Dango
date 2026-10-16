# Continuous deformable belt experiment

This isolated experiment replaces all sixteen peripheral Box2D bodies with one
continuous Cartesian Fourier belt. It does not change any existing DLL target.
It is a candidate under investigation, not an accepted replacement.

Correction recorded 2026-10-16: references below to the "original core impulse"
describe the preserved pre-correction experiment, not the actual old-half
world's Space implementation. The actual ground rule adds 7.7m/s to every
peripheral body's and the core's vy. The optional corrected mode and verified
call chain are documented in `dango_belt_calibration.md`; the default remains
the old experiment for reproducible comparisons.

## State, mass and material

`B(theta)=T+sum(A_n cos(n theta)+B_n sin(n theta)), n=1..8`.
The belt has 34 scalar coordinates plus the two core coordinates. Uniform mass
in reference theta gives exact constant diagonal masses: 16*node_mass for T,
half that for each Fourier coefficient, and core_mass for the core. No mass is
assigned to the sixteen virtual scaffold attachments. No Box2D world, collision
body, proxy disc or interpolated-node collision shape exists in this module.

The natural visible radius is the average natural radius of the former
quadratic spline, `(r+node_radius)*(cos(pi/16)+.75+.25*cos(pi/8))/2`.
Scaffold attachment i is `s*B(theta_i)+(1-s)*Core`, `s=r/R`. Its full Jacobian,
including the core reaction, is used. Radial spring k/c use the old two-body
reduced mass and `k=mu*omega^2`, `c=2*zeta*mu*omega`. Optional short scaffold
springs use node_mass/2, the same old rest chord and the same scaled Jacobian.

The belt additionally has continuous stretch and signed-curvature energy,
evaluated on 64 uniform material quadrature points. Short springs and continuous
stretch are both physical energy terms; their combination is not asserted to
be mechanically identical to the old perimeter. Area is analytic in Fourier
coefficients. Its one-sided compression energy maps circle area back to the
old natural polygon area before applying the old area coefficient. The inactive
branch has zero Jacobian, including incremental damping across the threshold.

Implicit Euler minimizes inertial potential plus elastic energy and incremental
shape damping. Gauss–Newton uses an SPD matrix, actual objective backtracking,
and an explicit residual gate. There is no added absolute-velocity damping.
The prescribed original timestep/substep count is retained. Position repairs
never write velocity.

## Continuous contacts and bounds

Extrema and region boundaries use two bounded tangent-half-angle charts. Each
trigonometric degree-8 equation becomes a degree-at-most-16 polynomial on
[-1,1]. Recursive derivative roots partition it into monotone intervals;
stationary endpoints include near-repeated/even roots. The test includes two
contact minima and their maximum inside one former 256-grid interval, plus an
independent 16384-point oracle. These are numerical tests, not an exact-arithmetic
proof for arbitrary degenerate input.

Position projection rebuilds full-curve extrema until the penetration gate is
met. Every active continuous minimum contributes a velocity contact row.
The velocity solver uses global accumulated Signorini normal multipliers and
non-associated Coulomb tangential multipliers, iterating all coupled rows to a
fixed-point residual. A previous associated friction-cone minimization was
rejected because it could create a free separating normal velocity from pure
tangential motion. Tests require no normal impulse for vn=0/vt!=0, dissipative
support for vn<0, the friction bound and global residual. Successful solves
cannot increase kinetic energy.

The flat ground is unbounded vertically. `set_bounds(left,right)` specifies an
allowed horizontal curve domain; leaving it returns failure and restores the
whole fixed frame. It does not add sidewalls or platforms. Speed uses the maximum
of core speed and a continuous upper bound: sampled belt speed plus a global
theta-derivative Lipschitz allowance. Exceeding maximum_speed rejects the frame;
there is no velocity clipping. minimum_area is checked in mapped old-area units.

## Controls and compatibility limits

Controls are exactly nine floats in the former lab layout. A/D apply the same
total force budget to the current continuous left/right world half. W/S apply
the same vertical budget to the upper world half. The material theta intervals
and their Fourier basis integrals are computed analytically. Sixteen point masks
are not used to distribute load. W retains the .15s rise/.20s release ramp;
S cancels that ramp immediately. Accepted Space applies the unchanged original
core impulse and cooldown, freezing/omitting W/S for the complete fixed frame.
Drag attaches to the continuous highest material point selected when drag starts.

Configuration layout is binary compatible: two int32 followed by 39 float32,
164 bytes. Fields retained only for layout but not used by the new material:
radial/perimeter hard length ratios, ground_deformation_strength, repulsion
parameters, core_exclusion_ratio, angle_strength, top_inward_force, restitution,
contact_hertz and linear_damping. The old grounded release brake is not applied.
No parameter equivalence or old walking/stopping behavior is claimed for these
unsupported mechanisms. With the optional shape guard disabled, self-intersection
prevention is not implemented: positive area and derivative/nondegeneracy gates
do not prove a simple curve. The opt-in guard below supplies a stricter shape domain.

## Optional shape guard

`dango_belt_set_shape_guard(handle,1)` opts in before the first successful step.
The default is off; config layout, original outputs and energy diagnostics are
unchanged. The guard DLL is built separately in `artifacts/physics-lab/belt-guard-native`.
`dango_belt_read_shape_guard(handle,output,24)` reports the independent attempt
schema documented in `dango_belt.h`. Only these attempt diagnostics survive a
rejected step; physical state and all original diagnostics roll back atomically.

For every theta and every interpolation time of an accepted update, the guard
requires `cross(B-Core,B') > 0.0001*R^2`, `|B-Core| > 0.04*R`, and winding one.
This keeps the core inside a star-shaped simple boundary. It allows flattening,
elongation and some nonconvex shapes, but excludes deep folds even when they
would be simple curves. A small disk around the core remains inside the body;
this is not a general thickness guarantee for every distant protrusion.

The certificate covers interpolation time analytically using quadratic minima,
then bounds theta intervals using Fourier derivative norms and Taylor remainder.
Finite interval bisection includes a floating-point outward allowance; failure
to certify is conservative refusal, not proof that the candidate is invalid.
It is not a 256-sample intersection test or an exact-arithmetic theorem prover.
Every Newton candidate, integrated path and ground repair is certified. A ground
shape repair that would violate the guard may translate the entire belt and core
upward instead; its gravitational potential change remains in the existing ledger.

One-sided soft penalties begin at clearance `0.18*R` and cross `0.12*R^2`.
The 6 Hz penalty uses paired core/belt gradients and the existing elastic energy
and work ledger. Its 64-point energy quadrature is separate from the continuous
safety certificate. Material sliders, total mass, jump and actuation are unchanged.
The exact per-frame certificate work cap is 131072 interval visits, with depth at
most 20. Existing Newton/contact limits still apply; this is not a wall-clock or
60 Hz guarantee. Return `-605` means guarded-frame refusal and `-606` work-budget
exhaustion; the caller pauses once and offers reset rather than retrying forever.

### Drag-convergence correction

Two actual 80/100/0 drag states exposed incomplete convergence within the
unchanged 40 Newton iterations. The original Gauss-Newton metric omitted the
positive scalar curvature of the divergent guard penalty. With `g=x-hard` and
`A=on-hard`, its residual is `r=A^2/g-2A+g`; `r''=2A^2/g^3`. The guard-only
Hessian weight now includes `k*r*r''` as well as `k*(r')^2`, implemented as a
multiplier `1+2*A^2/(A+g)^2` on that residual's Jacobian outer product. Energy,
gradient, forces and physical k/c remain unchanged. This is scalar curvature
completion, not the full geometric Hessian.

If all 40 iterations are used, the guarded path recomputes the actual current
endpoint residual before applying the original `2e-7` cap; the legacy path is
unchanged. If Armijo exhausts 20 attempts, the guard may retain the current
already-certified state only when its actual residual meets that same `2e-7`
cap. No rejected or uphill trial is committed. The full integrated path and all
subsequent material/contact/speed checks still run. Solver diagnostic slot 16
counts `residualValidatedLineSearchStops`; these must not be reported as reaching
the stricter normal `2e-9` target. This final rule replaces the first candidate's
`64*DBL_EPSILON` special case and does not use a separate incremental-energy test.

For slowly converging guard-only solves, a damped positive-definite BFGS metric
may accelerate the latter half of the same 40-iteration budget, when the current
Gauss-Newton convergence predicts missing the unchanged hard residual cap.
History resets each substep; energy and gradient are unchanged. If the direction
is unsuitable or rejected, the solver returns to Gauss-Newton within the same
20-candidate line-search budget. Slots 17/18 disclose BFGS updates/GN resets.

Local certificate depth/stack uncertainty (reason 8) is separate from the global
131072-visit budget (reason 3). Local refusal can backtrack or use the existing
rigid upward ground fallback; true global exhaustion still rejects the frame.
In guard mode both a deforming ground candidate and its rigid fallback must also
pass the original material residual domain and configured minimum area before
commit. The original `|B'| > 0.02*R` and all other thresholds remain unchanged;
star-shaped geometry alone does not imply this material-domain condition.

### Opt-in speed saturation

`dango_belt_set_speed_saturation(handle,1)` requires shape protection and must
be called before the first successful step. It defaults off. The F lab enables
it explicitly after the user approved limiting true overspeed and continuing.
The continuous speed certificate refines a coarse bound only near the cap, with
at most 16384 samples. All 36 generalized velocities are scaled by one positive
factor after contact, while positions stay unchanged; the original speed cap
is not raised. At most four inward corrections and re-certifications are used.
Unresolved/nonfinite results still reject and atomically roll back the frame.

This is dissipative and changes extreme-drag feedback and momentum; it is not a
momentum-conserving force. Existing energy diagnostic slot 18 records its true
kinetic-energy delta, and the separate 12-double speed diagnostic reports that
delta plus linear/angular momentum changes. Snapshot slot 176 records enablement.
Continuous bounds and finite work caps are not a stable 60 Hz guarantee.

Read-only `dango_belt_read_snapshot(...,200)` and
`dango_belt_read_solver_diagnostics(...,24)` expose numerical world state and
precise failure sites, as documented in the header. Failure diagnostics survive
physical rollback. No snapshot restoration is exported; fixture restoration is
private to the regression executable. The lab retains the pre-step snapshot,
exact float timestep and nine native control values in memory, writing them
only on explicit export or failure. No OS input, screen content or text is read.
The current corrected module is built in `artifacts/physics-lab/belt-guard-robust-native`;
the first solver candidate, original guard and diagnostic-only builds remain
available for comparison. Six actual numerical fixtures and bounded follow-on
drag/release sequences are kept in the private solver regression executable.

### Force-only walking outline

The optional `dango_belt_set_whale_drive(handle,1)` is default-off, requires shape
protection, and is set before the first successful step. The new lab candidate
keeps the original half-region horizontal force and adds a small traction field
on the continuous belt. Materials, collision, q/v and the energy objective are
not changed to impose a shape. The intended visual result is a mildly fuller,
raised front and narrower rear while walking, swapping on reversal; this intent
requires separate visual acceptance.

With bounding-box normalized x/y, signed gain u, q=sign(u)*x and a=abs(u),
the current rear-localized preview uses smoothstep-clamped weights
`rear=S((-q-.15)/.60)` and `front=S((q-.05)/.55)`. Its raw traction is
`Fx=-sign(u)*horizontal_force*a*.18*rear` and
`Fy=horizontal_force*a*((.40*front-4.0*rear)*y+.08*q*q*q)`.
The rear is squeezed and gently pulled back while the front has weaker support.
A 64-point material-angle integral removes mean force and the rigid rotational
component before Fourier mapping. The compensating projection can distribute
some force over the rest of the belt. Spatial weight boundaries are C1; input
reversal through zero is continuous and is not asserted to be time-C1.
The added traction has zero net force/torque to numerical precision; it can
still change ground/friction response. Existing drive speed gating does not
toggle this shape field: u follows input with bounded 0.15s rise / 0.20s release
rates. Reversal crosses zero in at most 0.30s. There is no periodic gait phase.

Air, drag, accepted jump, W/S or remaining W ramp suppress traction; cancel also
clears u immediately. Other suppressed states let u decay without applying it.
The exact original W/S forces, jump rule and all solver/safety limits remain.
The field's work is already a subset of the integrator's external work; its new
Whale16 diagnostic slot 8 is informational and must not be added to the energy
ledger again. Snapshot slots 177/178 record enablement and signed gain. Rejected
steps restore that state and Whale16 telemetry, except its attempt counter.

`dango_belt_read_whale_drive(handle,double[16])` and the full schema are in the
header. The current preview is built separately in `artifacts/physics-lab/belt-whale-tail-native`;
the earlier candidates and accepted robust module remain available. Only the
80/100/0 endpoint is used for this stronger-head/tail visual preview; the prior
eight-corner result belongs to the 0.64-inflate candidate, whose appearance the
user rejected as too subtle. The experiment checkbox takes
effect on reset and can compare the same F body with original driving forces.

### Active-force distribution display

`dango_belt_read_force_ledger(handle,double[1024])` is read-only. Header8 and
row6 are documented in the header. It stores the last successful frame's final
substep force evaluation, at the actual pre-integration world positions. Rejected
frames retain the previous successful ledger. Initial row count is zero, unused
slots are zero and insufficient capacity writes nothing.

Horizontal/W/S driving uses up to 16 exact material-bin resultants, preserving
the original analytic total force and torque. These are equivalent small-region
resultants, not sixteen physical point loads. Shape rows are the actual 64
quadrature weights after zero-force/torque projection and before Fourier mapping;
drag is its actual single evaluated point force. Gravity/contact are omitted.
The display reads these recorded rows directly with a common linear arrow scale;
it does not infer forces from the subsequently rendered shape. Toggling the
overlay or reading the ledger cannot advance or modify the physical state.

## ABI and diagnostics

See `dango_belt.h`. Version is 1. All calls return 0 on success; -600 means
invalid arguments/capacity, -601 unknown or expired handle, -602 unsupported
configuration/derived nonfinite or non-float-readable values, -603 numerical,
speed, area or bounds failure, -604 allocation/handle-capacity failure. Inputs
are validated before mutation. Numerical failure restores the complete frame.
The API is intended for serialized lab calls, not concurrent calls on one handle.

`read` returns 68 scaffold floats (core then sixteen attachments, each XY/VXY),
512 curve floats (256 actual continuous belt XY samples), and 64 stats floats.
Scaffold points are for display and inspection, never collision masses.

| Slots | Meaning |
|---|---|
|0..5|CoreXY, beltTXY, physical COMXY|
|6..13|total mass, signed area, AABB minXY/maxXY, foot gap, continuous speed upper bound including core|
|14..19|grounded, accepted jump event, cooldown elapsed, W ramp, applied control Fx/Fy|
|20..24|horizontal action XY, vertical action XY, control torque about COM|
|25..31|last contact row count, summed contact KE before/after, summed N/absT impulses, peak fixed-point residual, final penetration|
|32..36|final elastic energy, summed incremental damping potential, peak implicit residual in position units, peak per-coefficient repair, fault(0 on successful frame)|
|37..41|schema1, DOF36, peripheral collision bodies0, short-bone switch, belt mass|
|42..49|KE, total Px/Py, angular momentum about world origin, CoreVXY, beltTVXY|
|50..55|accepted small steps, peak pre-repair penetration, summed contact energy delta, peak friction violation, minimum derivative, minimum scaffold radial length|
|56..63|peak GN iterations, requested control Fx/Fy, internal net Fx/Fy/torque, natural visible R, K8|

Read is nondestructive. Failed calls report their return code, preserving the
previous successful stats, so stats[36] must not be used to detect rejected calls.

## Current acceptance evidence

Native Release builds with /W4 /WX. Test suite intentionally keeps a failing
whole-belt jump gate: original core impulse is accepted and core velocity rises,
but the complete contour did not leave the floor in the tested walk/release/jump
sequence. This is an unresolved behavior failure, not a passed Space acceptance.

After the corrected area mapping, 1800 frames at 120 Hz settled to approximately
width 1.69369018, height 1.07093823, speed upper bound .00230, foot gap -1.08e-9.
The old measured height was 1.12277572: roughly 4.6% lower. Natural-circle matching
therefore does not establish resting-shape matching. No material search or
parameter adjustment was used to hide this difference. Real-window behavior,
performance and user visual acceptance remain separate and pending.

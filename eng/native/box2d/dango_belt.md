# Continuous deformable belt experiment

This isolated experiment replaces all sixteen peripheral Box2D bodies with one
continuous Cartesian Fourier belt. It does not change any existing DLL target.
It is a candidate under investigation, not an accepted replacement.

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
unsupported mechanisms. Shape self-intersection prevention is not implemented;
positive area and derivative/nondegeneracy gates do not prove a simple curve.

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

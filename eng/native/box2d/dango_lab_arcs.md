# Isolated elastic-arc experiment

This is an unaccepted experiment. Build with `DANGO_PHYSICS_LAB_ARCS=ON`;
enable `DANGO_PHYSICS_LAB=ON` as well for the baseline comparison executable.
The targets are `dango_physics_lab_arcs`, `dango_physics_lab_half`, and
`dango_physics_lab_half_envelope`. None replaces the production DLL.

The sixteen existing physical nodes are generalized coordinates for sixteen
periodic quadratic B-spline spans. Their radially expanded controls are exactly
the controls returned to the current renderer. Stretch, curvature, compressed
area, contact extrema, and virtual-work force transfer use that same curve.
Three-point Gauss integration defines the material energy. Analytic derivatives
include the expansion's reaction on the core. Natural metric and curvature are
fixed from the regular reference ring, not fitted to the current shape.
Conservative forces are not clipped. Rayleigh damping uses the same objective
stretch/curvature rate gradients. Existing nodal masses remain lumped masses;
this is not a continuum-consistent finite-element mass matrix.

The existing internal radial springs, radial safety bounds, core exclusion,
and top-pressure parameter remain. Arc targets replace perimeter distance
springs, perimeter hard length bounds, raw/display hard turning-angle bounds,
ground spreading, convexity forces, angle barriers, and angle equalization.
Area pressure is the gradient of compressed spline-area energy. The raw-control
polygon's crossing/positive-area rejection remains a topology fault check;
it is not a proof against every possible spline self-intersection.

Each span has a persistent seam slot and an interior-minimum slot. The shared
seam belongs to one span. Flat spans therefore retain both endpoints via
neighboring seam slots. All qualifying rows, not only the global lowest point,
participate. Within each physical small step, positions are repaired first;
then final curve geometry is frozen for the velocity solve. Each contact row
solves its full 2-by-2 effective mass and Coulomb cone as one atomic convex QP,
using accumulated normal/tangent multipliers. Candidates are the interior,
both cone edges, zero, and the current feasible value. A change applies the
multiplier difference. Actual atomic contact KE must not increase. This avoids
the energy injection caused by separately shrinking a friction bound after
releasing its normal multiplier. Multipliers reset between physical small steps;
there is no cross-step material-point warm start or inherited friction budget.

Position repair does not modify velocities. It can change material potential;
that measured cost is reported separately and is not claimed to conserve total
energy. The reported repair potential excludes radial-spring energy and imposed
top pressure. Quadratic polynomial spans approximate a circle: the reference
radial ripple is about 0.01882 percent; their mean radius is also slightly below
the control radius. They are not exact rational circular arcs.

The three half-region modules export `region_fraction()=2`. Region mode 4 shares
the original total force by actual selected-node mass. A/D select left/right
halves of the displayed spline bounds; W/S select the upper half and apply up/
down force. Mode 5 is rejected. Modes 0..3 remain comparison controls. Only a
single body and horizontal floor-plane contacts are supported; platform, ball,
furniture, and multi-body APIs reject this module's unsupported scenes. The
arc-floor solver evaluates a mathematical horizontal plane, without finite
floor-edge contacts. This does not make the surrounding scene unbounded:
configured scene bounds and walls remain finite (for example, the comparison
scene's horizontal bounds at -256 and +256). Scene walls and the plane's
contact model are separate boundaries.

The authoritative ABI and statistics slots are documented in
`dango_physics_lab_api.h`. Ground schema 2 records actual coupled-contact KE;
there is no separate friction-energy substep. Arc slot 16 measures all position
repair's material-potential change. Arc slot 8 is final penetration, slot 23 the
pre-repair peak. The 32 row identities are stable within the small-step velocity
solve; between small steps the extremum's material parameter can move.

`dango_physics_lab_arc_tests` checks analytic material/area gradients against
central finite differences, natural equilibrium, rotation/translation/cyclic
invariance, force/torque closure, Rayleigh dissipation, dynamic local recovery,
flat multi-point contact, curve extrema, split position/velocity repair,
contact impulse/moment closure, Coulomb cone, exported schemas, and 1080-frame
world protocols with both the older and current window default materials.
`dango_physics_lab_half_tests` captures actual per-body half-region forces with
unequal masses and compares 600 frames of the core path bit-for-bit against
the unchanged lab target. These checks do not replace desktop/manual acceptance.

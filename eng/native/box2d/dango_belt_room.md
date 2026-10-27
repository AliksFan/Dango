# F7-0 independent belt room ABI 1

## Optional isolated muscle actuation (2026-10-27)

The existing frame layout, 9-control step ABI and default behavior are unchanged.
`dango_belt_room_step_muscles` adds single-step shake time `[0,2]` and strength
`[0,2]` (extended from `[0,1]` for isolated amplitude candidates). Existing input
values retain exactly the same force. Zero strength follows the ordinary path. Active shake rejects concurrent
horizontal, vertical, jump, drag and cancel controls. Inputs are validated before
writes and are never retained after a successful or refused step. The existing
fault rollback and prepared-scene barrier apply.

Eight material-attached contractile fibres exert equal/opposite central forces.
Activation phases differ; a smooth near-ground weight attenuates the entire pair,
preserving balance. Optional active follower-force iterations use the existing
integrator and guards, with a finite convergence limit. Discarded trials still
consume the original frame's shared guard work budget. Only the converged trial
and its actuator work survive. No point is pinned, no pose/velocity is overwritten and no
friction parameter changes. The consumer currently covers one default body on
flat ground, not furniture/platform support.

`dango_belt_room_read_muscle_diagnostics` returns 32 doubles (v1; offsets in the
header), including actual net force, torque, actuator work, individual tensions
and convergence diagnostics. Actuator work is also included in the existing
integrator force-work ledger. Default bodies do not perform follower iterations.
The managed consumer must explicitly reject a module missing this capability;
there is no fallback to top dragging or rendered contour movement.

Native tests: `dango_belt_muscle_tests`; button/managed integration:
`Dango.ActionLab --verify-shake <new-output-directory> [previous-room-module]`.

## Existing room contract

2026-10-21 W/S update: this room embeds the same balanced upper/lower deformation
control as [the product body](dango_belt_product.md). Both held channels cancel
only the vertical pair; horizontal input, drag and original jump gates remain.
Contact forces and elastic energy can still alter takeoff or flight indirectly;
there is no trajectory lock or additional COM correction. ABI, collection
transactions and contact budgets are unchanged; the rebuilt module hash changes.

Current collection extension (R3, 2026-10-20): capabilities255 adds an independent
`belt-room-furniture-r3-v1` interface, while retaining ABI1/frame4472 and the R1
base contact identity. See [R3 contract](../../../docs/planning/2026-10-20-belt-room-furniture-r3.md)
and verification section9. A collection frame is16968 bytes and contains the
same native publication's body, immutable geometry, up to30 platform/disk items,
their live states and last applied token. New geometry/velocity channels are
uniform room meters (72 room units/m), with no legacy p/q conversion.

Enable/probe/prepare/apply/cancel/read exports and DTO layouts are frozen in
`dango_belt_room_collection.h`. Prepare binds generation/token/revision/tick/
configuration revision; step and mutations return busy without a physics fault
while prepared. Layout application increments revision without advancing tick.
All four substeps, coupled contact, shared geometry budget and full-world
rollback belong to one room owner. Disks use gravity scale0.5; body gravity,
material and original drive are unchanged. Disk contact reports choose one
representative row coherently while impulse sums cover the tick. Rolling uses
only the current substep's floor friction budget and is included in joint KE.

R3 preserves the old failures below as history. Its new proof precision and
world-round scheduling fixes retain the original penetration/depth/budget and
iteration limits. Current DLL/source/evidence identity is recorded separately;
native contracts do not constitute visible-product or human acceptance.

Current follow-up: [F7-1R1 contract](../../../docs/planning/2026-10-20-belt-room-f7-r1.md)
supersedes the numerical/contact-policy choices below for the new candidate.
This document retains the original `c24fe1f` contract and FAIL evidence as history;
R1 results are recorded separately and do not retroactively pass these old gates.

R1 basic-platform automatic gates pass with contact identity
`belt-solid-aabb-r1-v1`, unchanged ABI1/capabilities15/frame4472. Current commands:
`--platform` runs nine standing/removal cases; `--replay-r1-dll <DLL>` runs the
two former failures against the actual R1 DLL. `--scenarios-default` still exits1
for unfinished edge/corner/jump behavior; it is not a product-admission gate
that has passed. See the linked R1 contract and verification section5 for scope.

Framework-connection extension (2026-10-20): ABI1/frame4472 and the R1 contact
identity remain unchanged. Capability 16 adds `dango_belt_room_set_bounds`, so
the new module advertises 31. It changes only the containment guard, not solid
wall contact. The operation validates the expected scene revision, finite
interval, minimum width, complete continuous body bounds and any platform
bounds before writing. Rejection changes no state or fault; identical bounds
are a revision-preserving no-op, and an accepted change increments scene
revision without advancing tick. Managed physics callers retain capability-15
compatibility; the desktop room adapter requires the new capability. Connection
evidence and current product limitations are in verification section7.

Static-play R2 adds optional solid-wall capability 32 (current capabilities63),
with the same ABI/frame/contact identity; see the
[R2 contract](../../../docs/planning/2026-10-20-belt-room-static-r2.md).
`set_solid_walls(handle,enabled,revision)` defaults to disabled and validates the
complete body/platform interval before changing mode and scene revision.
`read_frame.diagnostics[23]` reports mode even after a failed step. Enabled walls
use the real continuous x extrema and the existing shared contact solver;
wall rows never qualify as ground/platform support. The desktop adapter requires
this capability and enables it explicitly. R2 also repairs missing inner-face
tie candidates, unnecessary clearance pursuit, and the unverified final allowed
projection. Final continuous platform proofs and all R1 numeric gates remain.
Old jump60 is retained as untriggered; separate jump90 verifies actual landing.
Results are recorded in verification section8, without changing old FAIL records.

Status: contract written before implementation sampling, 2026-10-20. F7-1
technical candidate only; capability declarations describe available operations,
not passed acceptance. No dynamic balls, multiple bodies, sleep or product room
admission is provided by this increment.

## Identity, coordinates and binary layout

Independent target/file `dango_belt_room`; no Box2D link or protected DLL copy.
ABI 1, room identity `continuous-belt-room-v1`, body model `continuous-belt-v1`, tuning `C455-F-v1`, contact model
`belt-solid-aabb-v1`, capabilities 15: coherent snapshot 1, frozen body 2,
solid platform 4, atomic layout 8. Maximum one body and one platform. World
units are meters, seconds, kilograms; x right, y up. There is no presentation
scaling in native. Geometry and controls use this same coordinate system.

Header is authoritative. Natural x64 layout: input 48 bytes, platform 48 bytes,
frame 4472 bytes. No packing. Frame contains full original successful body
snapshot, scaffold, curve and stats plus immutable matching platform geometry.
Native generation is unique within one module load; managed owner epoch must
isolate frames across DLL unload/reload. Native supplies nonzero world generation, body ID 1, config revision 1, initial
tick/scene revision 0. Successful Step increments world tick once. Platform
create/move/remove increments scene revision once without manufacturing a tick.
Creation requires a caller-provided nonzero ID greater than every previously
accepted platform ID in this world. Moving
requires the current ID; removal requires that exact ID. Stale revision, wrong
ID, overlap or invalid geometry does not change any live state. Removed IDs
are never reused. No reconfiguration operation is exposed in F7-1.

All lifetime/mutation/read operations belong to one owner; callers serialize
module registry access. Handles use a room-only tagged token registry and must
not be sent to single-body functions. Step requires exactly one input with the
current body ID, zero reserved field and all nine original finite controls.
Fixed dt must equal `0.0166666f`; four substeps, selected F calibration, guard,
speed saturation, radial/rear drive and original jump remain enabled. Material
and force expressions are inherited. No-platform execution dispatches the exact
original ground solver and preserves every original successful channel.

`read_frame` requires at least the advertised byte size and writes nothing on
capacity/handle/argument failure. It returns the last successful complete body
and layout, plus a separate latched world fault and failed-attempt room
diagnostics. A failed step restores the complete private belt_body, world tick,
layout, IDs and support state; only separate attempt diagnostics/fault survive.
The failed world refuses subsequent steps and layout mutations until destroyed.
Invalid API arguments/revision/admission rejection do not latch a step fault.
All frame padding is zero. Revision/tick/ID overflow rejects without mutation.
Scene revision starts at zero. Tombstoned IDs cannot be resurrected in this
world; a new world has a different generation within the same module load.
Across reload, use managed (OwnerEpoch,Generation).

Errors: -700 argument, -701 handle, -702 config, -703 already faulted,
-704 capacity/allocation, -705 physical/admission rejection, -706 geometry work
budget, -707 bounded geometry uncertainty, -708 stale revision or identity.
Original material/solver errors are recorded separately in room diagnostics.

## Frozen static contact model and gates

Platform is a finite, axis-aligned **solid** rectangle, including top, sides,
bottom and corners. Width/height 0.1..16m, friction 0..1.5, each edge within
1024m; it must remain inside horizontal bounds and above the ground. No
ground-height substitution or single-sided crossing is permitted. Curve is the
actual Fourier boundary, not the 256 displayed samples or its convex hull.

For material theta, P=sum(Bj*qj). A static normal row has components
Jn[2j]=Bj*nx, Jn[2j+1]=Bj*ny (j=0..16), core entries zero. Tangent is
(-ny,nx). The inherited paired mass metric gives Knn=Ktt=sum(Bj^2/mj)
and Knt=0. Normal Signorini then tangential maximum dissipation uses the original
non-associated Coulomb iteration. Platform rows use min(body friction,platform friction); ground uses body friction. Both ground and platform rows participate in
the same velocity fixed point. No restitution is added. Only top support with
normal.y>=0.7, core.y>platform.top and core.vy<0.5 counts as grounded. Side and
bottom do not support. Existing grounded jump force remains unchanged.
F7-1 deliberately keeps original floor-proximity brake behavior: extension of
that brake to elevated support is a separate, currently unsupported behavior;
this limitation must remain visible in acceptance.

Position correction uses the local elastic metric of the existing fixed-step
objective: A=M+h^2 sum((k(1+scalar_curvature)+c/h) J_material^T J_material),
with h=.0166666f/4. Solve A z=Jn^T and use delta_q=z*(depth+2e-9)/(Jn z),
scaled to the existing per-coordinate .01 cap, including the core response.
This changes only the position correction metric; material, free integration,
velocity impulse metric and acceptance gates remain unchanged. Rebuild continuous
features after each correction. Active corner distance roots use complete interval isolation
of F=(P-C).P-prime: conservative distance/Voronoi/derivative bounds discard
no-root cells, monotonicity certifies unique bracketed roots, and unresolved
near-multiple roots fail at the frozen work/depth caps. Every correction passes
unchanged shape/material guards.
The local quadratic metric is not a global nonlinear-energy guarantee. Every
projection adds a 36-dimensional metric assembly and Cholesky solve, at most32
per substep/128 per tick; unchanged certificate/iteration caps do not imply
unchanged runtime cost. Nonfinite, non-SPD or nonpositive effective response
refuses without a mass-only projection. Position correction does not write speed,
and elastic/gravity correction work remains separately reported.
If a rigid common translation is attempted, all surfaces and swept geometry
must still pass; there is no unconditional upward fallback. Ground is solved
again after platform corrections. A failed shape/material check rejects.

Frozen numerical gates, chosen before runs:

- final penetration <=1e-7m; row activation 2e-6m;
- position correction <=32 iterations per substep; velocity <=300 iterations,
  target 2e-8m/s, final residual <=2e-6m/s;
- contact KE_after <= KE_before+1e-9*(1+KE_before);
- new geometry certificate <=262144 cells/frame, theta depth20/time depth16;
  independent of unchanged body guard budget131072/frame;
- each position iteration changes any generalized coordinate by at most 0.01m;
  at most32 iterations bound total correction. Every projection elastic/gravity
  change and positive projection work is reported independently, never hidden
  in contact KE; this position stabilization does not claim energy conservation.

256 material samples may locate candidate penetrations only. Passing requires
an independent conservative continuous certificate. For each theta/time cell,
P is affine in interpolation time. Coordinate ranges enclose both time endpoint
curves using center value, center derivative and harmonic second-derivative
Taylor bounds with outward roundoff. A cell separated from the rectangle
contracted by the 1e-7 penetration allowance is accepted; otherwise bisect.
An actual midpoint inside the contracted rectangle is a crossing witness;
unresolved depth/work caps return uncertainty/budget, never success. Endpoint
containment additionally checks the rectangle center with a polyline winding
whose homotopy is certified by the Fourier second-derivative chord error tube.
The center-to-each-entire-chord minimum distance must exceed that tube;
uncertain distance refuses. With no boundary inside the rectangle, center
winding rejects an enclosed rectangle. Body-contained-in-platform
is detected by boundary interior checks.

At each original substep save old q; integrate to a private free prediction,
solve candidate positions/velocities and certify old-q to final-q interpolation.
First-hit slab provenance of each material candidate prevents pushing a body
that traversed the entire solid to the far face. Unresolved first-hit/corner
ambiguity fails. This interpolation is the accepted path contract; no extra
physical substeps, material changes or altered fixed dt are introduced.

Diagnostics24: 0 schema1,1 attempt,2 return,3 original solver return,
4 geometry work,5 max theta depth,6 max time depth,7 certificate reason
(1 crossing,2 budget,3 uncertainty,4 containment),8 maximum penetration,
9 velocity residual,10 contact KE before sum,11 contact KE after sum,
12 projection elastic delta,13 projection gravity delta,
14 maximum coordinate correction,15 positive projection work,
16 position iterations,17 velocity iterations,18 support platform ID low32,
19 support platform ID high32 (exact integers; support revision is frame.scene_revision),20 normal impulse,21 absolute tangent impulse,
22 completed substeps,23 reserved0.

## Required evidence and stops

Nine configurations, 480 ticks each, all original read channels versus frozen
C455 and current product. Finite-platform standing, leaving edge, jumping,
side/bottom/corner contact, drag/release and support removal plus overlapping
layout, stale revision/identity, capacity-no-write and physical full rollback.
Disabled contact and corrupt rollback negative builds must fail actual gates.
Any failure remains an explicit F7-1 failure; no threshold increases or material
changes are permitted from observed outcomes. New core mathematics beyond this
contact model returns to design review. No UI/window or product admission is
established by these native checks.

## 2026-10-20 implementation evidence and technical stop

The ABI and numerical gates above were frozen before sampling. The independent
module builds with C17 /W4 /WX. Original-body export visibility is suppressed
only for embedding, so a room DLL cannot masquerade as the single-body product.
The default single-body target and no-platform expressions remain unchanged.

Both frozen C455 and current product comparisons pass 4320/4320 steps, including
all original successful channels. Native direct gates pass capacity-no-write,
invalid-input no-fault atomicity, stale revision/overlap/tombstone rejection,
continuous crossing/containment/budget refusal, full private-state rollback,
rotated circle corner activation at 1.8e-6m, exact support ID 2^53+1 and immediate
layout support-channel coherence. Disabled platform contact and deliberately
corrupted rollback negative executables both fail their actual assertions.

**F7-1 is FAIL; F7-2 is not admitted.** Platform standing/removal completes for
7/9 configurations. Size120/softness0/build0 has last successful world tick59,
then attempt60 returns -707. Size120/softness100/build0 has last successful
world tick75, then attempt76 returns -707. Both exhaust the frozen theta depth20
while geometry work remains below its cap. This is a bounded certificate
uncertainty, not proof of published penetration. Diagnostic8 is maximum
pre-projection candidate penetration, not final committed penetration.

Additional 54-case action matrix: side contact 8/9 and bottom contact 8/9 complete
with the intended contact observed; pressure 5/9 completes. Seven edge runs fail
physical gates and two do not leave the edge during 240 ticks (unverified).
Corner-approach and jump-onto-platform runs each fail all nine configurations
at certificate uncertainty before completing those behaviors. Cases that fail
standing before later input are not claimed as tested pressure/edge behavior.
No tolerance, depth, budget, substep, material or drive was changed to clear
these failures. No full room, dynamic object, product or human acceptance follows.

Raw logs are under `artifacts/belt-room-f7/native/`: `platform-first.log`,
`platform-second.log`, `platform-third.log`, `platform-final.log`,
`scenarios-final.log`, `contracts-evidence-final.log`,
`equivalence-final-C455.log`, `equivalence-final-product.log` and negative logs.
Failure JSON includes complete zero-input history, last-success snapshot200,
attempt q/v, final certificate from/to and unresolved theta/time cell; matching
private body binaries retain the last-safe native state. The deterministic
public setup is configuration above, spawn(0,1.5), bounds[-20,20], platform
{id1,x0,y0.4,width3,height0.2,friction0.72}, all-zero controls, fixed dt0.0166666.
Run `dango_belt_room_tests_0 --platform` to reproduce the frozen stop. Negative
variant output filenames are distinct from the normal variant failure evidence.

Horizontal bounds currently retain the original guard rejection; they are not
solid wall contact. The public walking-to-boundary case proves rollback only.
Actual-DLL failure replay uses --replay-dll with the independent room DLL and
asserts last successful ticks59/75, rejected attempts60/76, and unchanged safe
body/layout channels. Full frame identity and module/source hashes are bound
in the generated native evidence manifest.

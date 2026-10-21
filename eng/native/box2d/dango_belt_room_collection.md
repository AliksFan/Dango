# Belt room furniture R3

Optional extension to room ABI 1. The original 4472-byte frame, original contact
identity and single-platform operations remain available. Capability 64 enables
the collection contract and capability 128 enables its dynamic disks. Extension
identity is `belt-room-furniture-r3-v1`; extension ABI is 1. The header is the
binary-layout authority. Natural x64 sizes are definition112, ball88, contact104,
item416, frame16968, probe48 and token40. No Box2D world or legacy mesh participates.

All new geometry uses meters, seconds and kilograms, x right/y up. In particular,
the reused ball DTO's legacy q transform does **not** apply. Product geometry is
platform272/72 by21/72, top-center anchor, and disk radius66/72. Product disk mass
is .02, gravity scale .5, floor/body friction .45/.35 and restitution .25/.15.
Disk inertia is .5*m*r*r. Only the disks use the reduced gravity.

`enable_collection(handle,expected_revision)` requires the old single platform to
be absent. First enable advances layout revision once; repeated enable is a no-op.
Thereafter old single-platform operations refuse. There is one body and at most
30 total platforms/disks. Object IDs increase monotonically and are never reused;
the current reused ball ID DTO limits admitted object IDs to INT32_MAX.

Furniture operations use kind1 add, kind2 move and kind3 remove. Add target is zero;
other operations identify an existing instance and cannot change its kind. Probe
rejections are 1 invalid definition, 2 collection disabled, 3 revision mismatch,
4 operation/target, 6 room bounds, 7 overlap, 8 uncertain/faulted geometry, 9 capacity.
An API error is a negative room code. Admission tests both layout anchors and live
disk positions, and the actual continuous body. Allowed top is ground+6 meters.

Preparation binds generation, token ID, layout revision, successful tick and body
configuration revision. While prepared, Step, probe/prepare, bounds, wall mode and
single-platform mutations return busy -709 without faulting. Apply requires the
complete exact token and advances revision without advancing tick. Cancel checks
the full pending token, is idempotent after cancellation, and changes no published
state. `last_applied_token` lets the owner verify a durable save's physical result.
Application preserves other objects' kinematics, clears obsolete contact reports
and recomputes body support. Disk moves restart at the newly confirmed anchor.

The body and disks advance in the same four fixed .0166666f/4 substeps. Fourier
point Jacobians and disk effective masses share one velocity fixed point. Disk
contact includes restitution on initial impacts with closing speed >=.5, Coulomb
friction, spin and the original rolling-resistance budget measured from floor-only
rows in the current substep. Contact DTOs sum impulses over the successful tick;
geometry, identity and all before-velocity fields consistently describe the row
with the strongest normal impulse in that tick. Disk-pair reports use opposite
normals, actual other-object IDs and each disk's own before-speed.
Disks remain active; room body sleep uses the managed conservative ball veto.

Position rounds are bounded at32, with at most one body projection and one
projection per disk per round, each coordinate <=.01. A disk pair marks both
disks as moved. Body/static projections use the shared local elastic metric A
described in dango_belt_room.md; body/disk projections use diag(A,m_disk I),
including the coupled core response. Both sides commit after the original body
guards and position correction writes neither body's nor disk's speed. Disk-only
projections and the joint velocity impulse metric remain mass based. A complete
round without changes is required to pass. Candidate
root/sample location cannot certify geometry. Fourier/disk acceptance uses the
theta/time cell certificate plus containment; box bounds are supplemented by an
outward-rounded radial Taylor lower bound which retains tangency correlation.
The radial theta derivative is quadratic in affine time and its extrema are
analytic. Disk/rectangle and disk/disk sweeps use segment distances. Geometry work
is shared across the whole frame:262144 cells, theta20/time16; unchanged body guard
131072. Penetration5e-4, clearance1e-4, activation1e-3, velocity300 iterations,
target2e-8/final2e-6 and joint KE tolerance1e-9*(1+before) remain unchanged.
The KE gate includes the completed rolling update. Guard-only walls atomically
refuse escaped disks; solid mode supplies actual contact.

A failed step restores the entire private body and room including disk poses,
velocities, integration counts, contact caches, IDs, support and version fields.
Only the separate failed-attempt diagnostics/fault are published. No retry, clipping,
old-body collision fallback or partial success is provided.

Build with the existing CMake runtime and `-DDANGO_BELT_ONLY=ON`. Targets are
`dango_belt_room`, `dango_belt_room_collection_tests_0`, negative variants `_1`/`_2`,
and collection metadata fixtures `dango_belt_room_collection_bad_1` through `_4`.
Native tests support `--thirty`, `--long`, `--platform`, `--rollback`, and Windows
`--dll <module>`; the last path loads the real DLL and consumes coherent frames.
Evidence belongs under `artifacts/belt-room-f7-collection-r3/`. Old FAIL records
remain independent; these targeted technical checks do not establish UI acceptance.

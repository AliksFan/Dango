# Product continuous belt ABI 1 / hot configuration contract

Source: frozen `refs/archive/2026-10-19/continuous-belt-lab` at
`75621d31e8de2c7f85699bcded503d2088a45c0d`. The existing solver, guard,
basis cache, force mappings and successful-step channels are extracted without
mathematical changes. Additions are product identity and atomic reconfiguration.
The product target is `dango_belt_product`, with a distinct DLL filename. It does
not link Box2D or the PhysicsLab executable and never copies over `dango_box2d.dll`.

## Identity and ownership

The inherited `dango_belt_version()` remains 1. Product callers additionally
require `dango_belt_product_version()==1`, model `continuous-belt-v1`, tuning
`C455-F-v1`, and capabilities 127. Bits are snapshot 1, shape guard 2, speed
saturation 4, radial eight-point drive 8, rear down-pressure 16, hot configuration
32, and short bones 64. Matching only the inherited ABI integer is insufficient.

The managed public binding lives in `Dango.Physics`; constructor, Step, Read,
Reconfigure, bounds and Dispose belong to one thread. Dispose is idempotent.
The native source handle registry retains the original single-owner contract;
callers must serialize module lifetime/handle operations. The managed binding
serializes native calls across its independent owners through a shared gate.
This is a single-body
flat-ground candidate, not a concurrent multi-world/multibody API.

Production defaults are guard + speed saturation + radial/rear drive + original
ground jump, short bones, fixed dt **0.0166666**, four substeps, and F calibration:
radial stiffness 1.4, area 1.4, bend 4, all other shape/damping multipliers 1,
overall damping from config, grounded brake 35, horizontal damping 60. `ForConfiguration`
constructs this calibration; raw `Default` retains the inherited all-one elastic
calibration for explicit comparison. No source protection threshold is changed.

## Atomic Reconfigure

`dango_belt_reconfigure(handle, config, calibration11, 11)` takes a complete
configuration at an owner step boundary. Invalid arguments, finite/range errors,
material-domain, ground, bounds, speed or shape-certificate rejection return the
existing negative codes. **Every byte of the live body remains unchanged on
failure**, including config, calibration, q/v, mass, input history, guard/contact
state and telemetry. Managed Configuration/Calibration update only on success.
The temporary validation body is never the live handle and is released before
return; it performs no Step and is not a replacement/respawn.

Supported hot domain is 16 nodes, 4 substeps, unchanged ground height, and the
approved body-control mapping (size 80..120, softness/build 0..100). Broad native
config creation still follows the inherited ABI, but Reconfigure rejects a
ground or discretization change. Worker revisions, latest-request coalescing,
FIFO barriers, cancellation epochs and failure latching are caller contracts.
The native synchronous operation applies exactly the complete payload supplied.

If radius is unchanged, q and v are preserved bit for bit. Config-derived mass
and material/calibration values change together. Size changes use the actual
Fourier geometry:

1. `s = newRadius / oldRadius`, with radius/attachment scale recomputed by the
   unchanged native config formula. Nontranslation Fourier coefficients scale
   by s, and core/ring centers scale about the previous mass COM.
2. Translate both centers in y so that the exact curve minimum y is unchanged.
   This preserves the foot/gap on ground and in air; it intentionally does not
   preserve vertical COM position during a size edit.
3. Preserve previous whole-body COM velocity; scale shape-mode and center-relative
   velocities by s, then correct the two translation velocities for the new mass
   weights. New generalized masses follow the config (the body mapper uses
   constant 2-D density). Momentum/energy need not be conserved under user editing.
4. Keep cooldown, ramp, dragging flag, material drag coordinate, support flag,
   successful-step state and solver/guard tick. No artificial fall or time reset.
5. Validate the new complete material domain, actual ground/bounds, unchanged
   speed cap and full shape certificate. Reject rather than clip/project a
   candidate that cannot satisfy them. Recompute body stats and certificate
   minima/size-dependent guard hard limits before the single live-state commit.

Contact rows/Jacobians are local to each contact solve and rebuilt every substep;
there is no persistent contact Jacobian to carry across sizes. The basis cache is
q/config independent and unchanged. Last-successful-step energy/force/solver
telemetry remains historical until the next successful Step; Reconfigure itself
is not a physical tick. The worker must publish its first new-configuration frame
only after successful Step and bind it to complete config/model/tuning/revision.
Parameter editing work is reported separately from free-evolution energy ledgers.

## Build and checks

Use CMake 3.22+ and the existing C17 toolchain:

```powershell
cmake -S eng/native/box2d -B artifacts/belt-product/native -G "Visual Studio 17 2022" -A x64 -DDANGO_BELT_ONLY=ON
cmake --build artifacts/belt-product/native --config Release --target dango_belt_product dango_belt_product_tests dango_belt_tests dango_belt_calibration_tests dango_belt_radial_tests
```

`dango_belt_product_tests <frozen-C455-DLL>` compares eight corners plus default,
480 ticks each, including rest/walk/reverse/W/S/jump. Scaffold/curve/stats,
energy/guard, snapshot q/v/mass, solver/speed/radial/rear and force ledger must
match bitwise. Hot checks start grounded/airborne/dragging, perform repeated
80↔120 and 0↔100 edits, single-axis out-and-back sweeps, retain material-only
q/v, reject invalid/unsafe config atomically, and step after rejection.
`--negative-size` deliberately restores old geometry after a resize;
`--negative-rollback` deliberately corrupts config after rejection. Both must
exit nonzero at their respective geometry/full-state assertions. These mutations
exist only in the test executable, not in the DLL.

Managed focused checks use `DANGO_BELT_MODULE` and optionally
`DANGO_BELT_FROZEN_MODULE` with filter `FullyQualifiedName~DangoBeltWorldTests`.
Missing explicit native evidence marks these tests inconclusive. The frozen
lab DLL is explicitly rejected as a product module despite inherited ABI 1.

Observed inherited failure (2026-10-19): the standalone old `dango_belt_tests`
uncalibrated legacy-jump scenario fails its line-46 `airborne` assertion after
120 ticks (peak whole-curve gap `2.22044605e-16`). A separately compiled,
completely unchanged frozen source control reproduces the same trace/exit 1.
The product's selected F calibration + original-ground-jump scenario passes the
dedicated jump contract and the 4320-tick frozen-module comparison. This old
failure is retained; no jump force, solver or gate was changed to clear it.

This contract provides native/CLI evidence only. Worker/FIFO/cancel races,
presentation calibration, OS input latency, human appearance, sleep policy,
long-session stability and room/furniture/multibody behavior require separate
integration and acceptance evidence.

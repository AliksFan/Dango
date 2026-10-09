# Round-ball projection admission probe

This .NET 10 console consumer verifies A1/A2/A3 and the pure-data portion of I9.
It references only the existing managed `Dango.Physics` DTOs. It has no WPF,
native library loading, world, Step, clock, input capture, or window creation.
It is not the complete round-ball sample or a proof of native contact/dynamics.

Build only this project into a **new** evidence directory:

```powershell
dotnet build eng/tools/Dango.RoundBallAdmission/Dango.RoundBallAdmission.csproj -c Release --artifacts-path artifacts/furniture/round-ball-probe-r1/build
dotnet artifacts/furniture/round-ball-probe-r1/build/bin/Dango.RoundBallAdmission/release/Dango.RoundBallAdmission.dll --self-test --output artifacts/furniture/round-ball-probe-r1/self-test.json
```

Use another new run directory for later builds. The probe never overwrites JSON
output. Exit codes: 0 = all contract checks passed; 1 = behavioral check failed;
2 = invalid CLI/input/output. Without `--output`, the JSON report goes to stdout.

## Fixture input

Generate a complete, explicitly synthetic example and round-trip it:

```powershell
dotnet artifacts/furniture/round-ball-probe-r1/build/bin/Dango.RoundBallAdmission/release/Dango.RoundBallAdmission.dll --write-synthetic-example artifacts/furniture/round-ball-probe-r1/synthetic-input.json
dotnet artifacts/furniture/round-ball-probe-r1/build/bin/Dango.RoundBallAdmission/release/Dango.RoundBallAdmission.dll --frames artifacts/furniture/round-ball-probe-r1/synthetic-input.json --output artifacts/furniture/round-ball-probe-r1/synthetic-input-result.json
```

Input is camelCase JSON with these fields:

- `schema`: 1.
- `sourceKind`: `synthetic` or `capturedPublicFrame` (the latter describes the body).
- `description`: explicit source/boundary description.
- `sourceCommit`, `sourceModuleSha256`: null for synthetic input; full 40/64-character
  hexadecimal source values required for captured input. The probe reports metadata
  but does not open a DLL or verify these provenance claims.
- `frames`: 1..256 uniquely named entries. Each contains `name`, nonnegative
  `sceneRevision` and `successfulStep`, complete existing `DangoBodyState` as `body`,
  `ballSourceKind` and the separate probe-only `ball` contract.

`body` contains `entityId: { value: positiveInteger }`, `contour: [32 finite float
values]`, `coreX`, `coreY`, `signedArea`, `centroidX`, `centroidY`, `maximumSpeed`,
`groundContactCount`, `isGrounded`, `selectedDragNode`, and `pointCount: 16`.
Copy the complete public frame; it already includes native node-radius expansion.
Do not expand it again. The probe defensively copies contour storage before use.

`ballSourceKind` is `syntheticGeometry` or `capturedInternalBallState`; `ball`
contains `entityId`, `centerQX`, `centerQY`, `radiusQ`, `angleRadians`, `velocityQX`,
`velocityQY`, `angularVelocity`, and `isSleeping`. The center/radius/velocity use q,
angles use radians. This is an internal probe contract, not a new formal ball ABI.
An accepted old body frame may be paired with synthetic projection ball data;
describe that distinction explicitly rather than claiming native ball capture.

All public body and ball fields are required by the JSON reader. Unknown fields,
non-finite values, invalid IDs/versions, missing capture metadata, malformed
contours and invalid scales are rejected. JSON evidence includes the input hash.
The report also records each frame's scene/step versions, IDs, ball source kind
and input angle; it does not claim synthetic ball data is a native snapshot.

## Independent checks and limits

The matrix is `s=1/.5` × pixel-only DPI `1/1.25/1.5`. The frozen numeric A1 anchors
are `(416,198)/(368,219)`. A2 checks eight radii, center, diagonal positions,
unwrapped native angle to WPF sign, and position/angle independence. A3 uses
1040 samples per frame: an independent old 160×120 canvas midpoint/Bernstein
oracle is compared with the new q projector. Tolerance is `1e-10` for pure double
coordinates, including pixel conversion; this does not replace the 1 DIP visual gate.

Self-test fixtures are **test-only mathematical shapes**, including sub-.98
controls and normalization stress. They are not captured resting/down/drag frames
or evidence that these shapes are reachable by a stable native rig. Self-tests
also show the independent behavioral assertions rejecting an ellipse, equal
bounds with incorrect diagonal radii, repeated DPI conversion, missing T, and
incorrect angle sign. Defensive-copy and invalid-input checks can fail when the
corresponding implementation is broken.

I9 repeatedly changes display parameters/overlay requests and compares the whole
serialized immutable snapshot. Snapshot/projector fields are checked to exclude
a world, Step delegate, clock or native handle. A pure-data consumer cannot prove
the future WPF Draw/Resize implementation's Step count.

Every report marks baseline DLL verification, native no-ball I8 regression, and
real window acceptance as unverified. Captured frames allow A3 contract comparison
but do not compare old/new native trajectories. Actual system DPI, pixels/raster,
HWND focus/IME/keyboard/drag, native dynamics and manual acceptance remain separate.

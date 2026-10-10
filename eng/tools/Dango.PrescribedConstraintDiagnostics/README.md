# Prescribed-center constraint diagnostics (R0)

This Windows x64 isolated tool observes the original self solver, without changing
its order, 96-sweep limit, tolerance, parameters or prescribed center. It does not
fix the solver, implement recovery, or supply physics admission.

Run `verify-diagnostics.ps1 -Box2DSourceRoot <verified pinned source>` in PowerShell
7. It validates every dependency hash against the earlier frozen manifest, finds
CMake on PATH or through vswhere, and builds three standalone executables:
no observer, read-only observer, and intentionally mutating observer. The last
must exit 1. Use a fresh `-ArtifactRoot` to retain previous evidence.

Fixtures are translated from the frozen stack/push inputs and the zero-velocity
overlap counterexample. They start with the same separated Add anchors, then
deliver the exact ABI float values from ordinal 1. Each normal executable also
loads the unchanged frozen DLL and compares every output/trace bit. The two
normal executables separately emit raw 17-point positions and velocities through
project-owned rig IDs and public Box2D getters; their binary outputs must match.
Fault outputs, contact count and lastsafe preservation are checked explicitly.

The observer saves every local constraint state at entry, after each real sweep,
and at exit. Its before/after state comparison rejects mutation. Failure JSONL
contains the failed call's per-row residuals and raw violations. Kind order is
radial distance, perimeter distance, raw angle, display angle, floor. Phase 0 is
the direct self call, phase 1 a self call inside pair contact. Stage 0/1/2/3 means
entry/sweep/success/failure. These are local diagnostic identifiers.

The `.observations.bin` format is only the recorded Windows x64 same-build test
layout: 40 metadata bytes, 93 project-owned floats, four alignment bytes (416
bytes total). The state has 17 positions, 17 velocities, 17 inverse masses and
eight length/radius/ground fields. It is not a portable format, Box2D private
state or a checkpoint. `analyze-failure.ps1 -ArtifactRoot <results>` validates
the selected call and decodes its original 96-sweep failure; signed-gradient and
circle/floor calculations do not perform extra solver iterations or mutate a
world. Two-constraint geometric feasibility does not prove whole-shape feasibility.

Manifest, stdout, stderr, outputs and failures remain in ignored artifacts.
Protected DLLs and the original replay EXE are hash checked before and after.
Every native operation runs on the process owner thread, worlds are released,
allocated bytes return to zero and all children must exit. No UI, listener,
account, persistence, Linux startup or production installation is involved.

# Prescribed position projection R1 (isolated local feasibility)

Run `verify-local.ps1` with PowerShell 7. Dependency/CMake locations default to
the existing local build cache; they are never embedded in committed config.
The tool verifies all pinned dependency files and protected binaries, extracts
the original R0 Tick42/substep5/entity1/phase0 stage0 entry without float
roundtrips, and builds a standalone executable. No native world, UI, account,
listener or persistent service is created. The script does not modify production
code. After the local gate passed, the same candidate header was moved into the
native directory and included exclusively by the prescribed solver.

The original solver must reproduce the 96-sweep failure and complete 372-byte
exit state bitwise. The single candidate only changes positions: radial rows
consider circle/floor intersections; angle rows enumerate 16 active sets of
four linear inequalities. All original residuals, immutable core/configuration
and velocities are independently checked. Five actual-process negative controls
disable floor, radial or angle rows/gates, move the core, or return early.
Their result flags never bypass the external full-state oracle.

Each sweep emits original per-node five-family residuals and raw positions;
each process emits its final exact state, QPC wall duration including observation
I/O, counters and OS process CPU time (including startup; coarse resolution), and
exit code. Use a fresh artifact root after any source change. A local positive
failure ends this candidate before native integration or trajectory/UI checks;
running its negative controls after failure does not authorize a second candidate.
See the frozen R1 contract in `docs/planning/` for all conditional later gates.

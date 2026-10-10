# Isolated cross-process physics replay admission

This test executable reconstructs the frozen `ce68521` world from a bounded
same-build input journal. It does not implement production checkpoints,
durable input storage, online authorization, or cross-platform recovery.
Capture and Replay run as two actual OS processes with different recorded PIDs.

The executable includes the project's frozen bridge and collision sources solely
for read-only test instrumentation. The 16 node bodies and core body are located
through project-owned rig IDs, and their raw positions and velocities are read
through public Box2D getters. No Box2D private structure is read or saved; no
production DLL or export is changed. The pointer-free journal records the full
float configuration, actual managed dt bits `0x3C888865`, build identity,
successful topology operations, and controls actually delivered to successful
fixed steps. A delivered Jump input can produce an explicit rejected Jump result;
the tool never describes that branch as an applied jump.

Only adjacent identical step controls are run-length encoded. The journal always
starts with an empty world, without any intermediate pose checkpoint. Its exact
representation is intentionally limited to this Windows same-build experiment.
The separate oracle contains raw 17-point dynamics and semantic frames at the
recovery boundary and the next 120 steps. Replay never uses the oracle to initialize
or mutate a world; it only compares the resulting observation.

Run `verify-replay.ps1 -Box2DSourceRoot <verified pinned source>` in PowerShell 7.
The runner discovers CMake on PATH or through Visual Studio's `vswhere`; an
explicit `-CMakePath` is also supported. The build manifest contains the actual
native-source hashes, probe/CMake hashes, dependency-source digest, compiler and
Release settings. `build-fingerprint.txt` can be independently SHA256-hashed to
reproduce the build ID. Each child also reports that ID, and the runner checks
the executable hash before every launch.
It extracts the frozen project source into ignored artifacts, builds its own CMake
target, then runs five original cold-restore scenarios plus topology, boundary,
Reset, other-world interference, Stop, and two 600-second simulated histories.
`-BuildOnly` only builds. `-LongOnly` repeats the two history fixtures; use an
independent ArtifactRoot when retaining prior measurements.
`-NegativesOnly` resumes negative checks against all nine already-recorded normal
fixtures, requiring exactly the same executable hash and build ID.

Correctness, 65536-byte journal size, and the 5-second restoration budget are
separate results. The entropy history can be run in a clearly labeled oversized
diagnostic mode, which always fails admission. Timing starts before opening and
validating the journal and ends at the reconstructed final safe frame; future
comparison is timed separately. These are simulated histories on the current
Windows host, not a real 600-second observation or production hardware SLA.
The runner records concurrent-load control as false; an uncontended final timing
review must be recorded separately without stopping the user's other programs.
The runner saves its summary and returns exit 1 whenever any normal admission
gate is red, including an oversized diagnostic result.

Missing-Jump and lost-Stop journals are structurally valid and have recomputed
integrity values, so they must fail the actual dynamic oracle. Other variants
cover Add order, Tick, missing segment, version, build identity, dt and length.
Every child process must exit and return native allocated bytes to its initial
value. Native operations check their process's single owner thread. The journal
checksum is only an artifact corruption check and supplies
no online trust or authentication guarantee.

# Isolated Windows safe-spawn admission

This .NET 10 console is outside production startup. It references the existing
Physics wrapper and the pure recovery-contract fixture. It uses the frozen R1
DLL read-only and never builds a replacement DLL. Native node/fault inspection
is compiled into a separate executable using the unchanged production source,
the pinned Box2D source, and existing executable-only publication injection.

Build the console with `dotnet build -c Release` and build `native/` with CMake,
passing `BOX2D_SOURCE_DIR` for the existing pinned source. Use a new ignored build
directory. Run the root-owned `verify-safe-spawn.ps1` to retain actual processes,
normal and expected-failure evidence, hashes, exit codes and allocation counts.

CLI options are key/value pairs:

- `--dll <frozen-R1-DLL>` is required for every executing mode.
- `--mode suite` verifies one/two-member 600-step native safety, nine retained
  push/stack/airborne transitions with 120-step equality, native member removal,
  private candidate failure, publication race, owner affinity and native probe.
- `--mode planner` independently verifies exact tangent, invalid bounds, group
  separation and live capacity rejections without creating a world.
- `--mode containment` verifies actual native Tick0 raw frames that escape
  left/right/ground while their scaled semantic contour still appears inside.
  Raw `Core+Contour` already includes radius and uses the original `.005 p`
  Box2D linear slop, separately from display contact and support thresholds.
- `--probe <independent-native-probe-EXE>` is required for suite and fault producer.
- `--mode produce --metadata <record.json> --disposition lost|released|fault|unknown
  --steps 120` writes fictitious domain metadata only. The separate
  `<record.json>.disposition.json` is a trusted test fixture receipt, not a
  production authorization or durable fault implementation. Prefix is 1..10000.
  The source world is actually disposed and the producer exits. This proves
  process termination/owner release, not an unclean production server crash.
  Fault producer uses a single independent native source world: actual prefix,
  publication failure, last-safe tick/error inspection and owner disposal before
  recording domain metadata. It exports no physical pose or checkpoint.
- `--mode consume --metadata <record.json> --disposition <matching-receipt>`
  independently validates metadata and current fixture facts. Lost/released
  create and publish an entire new native group; fault/unknown block before Create.
- `--output <evidence.json>` preserves the evidence. `--mode verify --output
  <evidence.json>` independently checks all recorded assertions and exits 1 on red.
- `--mutation healthyReset|oldMove|oldJump|halfReady|skipSpawnOverlap|skipSpawnBounds|
  skipSpawnGround|oldEpoch|noReason|faultAutoRebirth` executes deliberately wrong
  operations and lets independent raw/semantic/planning assertions reject them.
  Core semantic mutations are forwarded to its separate fixture oracle.

Evidence Version 1 contains Checks, Pass, PID, source/DLL/native config hashes,
NativeCounts and Allocation. Recovery includes Blocked/Ready/Reason, prior/new
epoch, first healthy tick/entity IDs and measured record-validation-to-healthy
milliseconds. The timer includes strict codec, claim, plan, native Create/Enable/
Bounds/Add, Tick0 read, neutral step, whole-group gates and publication. Build,
process startup and the remaining 600-step trajectory are excluded. RawDigest
is the SHA256 of every first healthy native raw+authority field encoded with
exact float bits; it excludes epoch and
producer history. Original gravity, solver, parameters and ABI remain unchanged.

No Server, Protocol, UI, database, Linux or account integration is implied.
The latest facts, lease checks, CAS and fault disposition here are explicit
fictitious fixtures. Windows headless evidence cannot replace live windows,
multiclient behavior, durable facts or manual dual-machine acceptance.

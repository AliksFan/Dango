# P1 Linux isolated build and evidence gate

This entry implements the approved `docs/planning/2026-10-11-dual-player-p1-linux-plan.md`
container gate. It requires the integrated Linux binary profile (`--linux-build-record`)
and uses only Bash, Python's standard library, .NET SDK 10.0.300, CMake, GCC and binutils.

The host owner supplies a temporary `linux/amd64` container with network disabled,
2 CPUs, 3 GiB memory and 128 PIDs. Mount the filtered source at `/inputs/repo` and the
pinned official Box2D archive at `/inputs/box2d.zip`, both read-only; mount a new evidence
directory at `/evidence` read-write. Input acquisition and package installation happen
before this entry. Run:

```sh
bash /inputs/repo/eng/tools/Dango.SafeSpawnAdmission/linux/run-linux.sh \
  /inputs/repo /inputs/box2d.zip /evidence
```

The three paths are optional positional arguments with the defaults above. Existing
host input/log files in `/evidence` are preserved; `/evidence/run` must be new or empty.
All copied source and build output live under one owned `/tmp/dango-p1-linux.*`
workspace. The container owner removes the temporary container after retaining evidence.
This entry does not start Docker, install packages, connect a network, open a window or
modify production source, native ABI, solver configuration or Windows frozen hashes.

The entry checks archive and license bytes before extracting the official Box2D tree.
It builds the unchanged module and independent C probe in Release, with Linux PIC and
the system math library, then builds the existing `net10.0` console tool offline.
`linux-build-record.json` contains exactly the approved 12 version-1 fields. Actual
ELF64/x86-64 identities, 35 header exports, loader ABI, source/file hashes, Box2D tree,
compiler/SDK/environment and exact build commands are retained separately.
After building and again after verification, `check-box2d` recomputes the actual
extracted Box2D tree's hash and complete path/size/SHA inventory, checks the actual
license and fixed origin, and compares every entry with the extraction snapshot.
Both check receipts are required before the root manifest can pass.

The independent verifier checks:

- A real zero-create `profilecheck`, plus 12 invalid-record profile oracles.
- All 43 required normal checks and all 17 frozen mutations, with actual child exit 1
  and the specified red oracle; exception checks are rejected.
- Six sequential producer/consumer pairs: lost and released at 120/1200 steps, fault
  and unknown at 120. Receipt SHA/PID/epoch and parameter bytes identify each producer.
- New-epoch reason, whole-group Tick0/publication, current ready, 600 native steps,
  the original 65,536-byte/5-second budgets, fault error -422/last-safe tick, and zero
  create for blocked dispositions. All four Linux first-healthy bit digests agree.
- Actual child PID/exit, mode, mutation, check names and types, allocation/owner counts,
  CPU/wall time, and unchanged source, module, probe, record and managed dependencies.

Every managed child has a fixed 60-second timeout; a timeout kills its owned process
group and fails. Normal failures stop further expansion. `run/manifest.json` seals the
run files; the root `manifest.json` references that result and inventories the full
evidence tree (excluding itself). Failures retain reports/logs and a red `failure.json`.
Only successful actual execution marks `LinuxVerified`; formal service, database,
real window, multi-client and manual acceptance remain false.

Pure parser/evidence checks can run on Windows without native loading or containers:

```sh
python -B eng/tools/Dango.SafeSpawnAdmission/linux/test_verify_linux.py
```

Use `-B` for Python invocations during source verification, so bytecode does not become
an extra source file. SourceHash uses the exact four-directory ordinal path/bytes
contract and excludes only `bin`/`obj`, matching the managed tool.

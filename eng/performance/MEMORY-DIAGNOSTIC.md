# Restoration memory attribution

This separate mode of `Dango.PerformanceProbe` observes only its own fictional
host. It does not change product rendering, scheduling, hide/restore, or disposal.
It never requests GC, trims the working set, enumerates other applications, reads
window contents, or updates the daily installation/settings. Both probe and waiter
reject output under `%LOCALAPPDATA%/Dango` and `%LOCALAPPDATA%/Programs/Dango`.

Build against the same clean published candidate used for the other probes:

```powershell
dotnet publish eng/tools/Dango.PerformanceProbe/Dango.PerformanceProbe.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:RuntimeFrameworkVersion=10.0.11 `
  -p:DesktopPackagePath=<absolute-candidate-package> -o <new-probe-directory>
```

Before running, compare the three product assemblies, both native DLLs and
CLR/WPF runtime files with that candidate. Build does not run the desktop.
Run the two modes **serially**, with distinct new output directories:

```powershell
pwsh -NoProfile -File eng/performance/Invoke-MemoryDiagnostic.ps1 `
  -ProbePath <probe-directory>/Dango.PerformanceProbe.exe `
  -OutputDirectory <new-wallpaper-output> -Mode Wallpaper -StableSeconds 60
python eng/performance/analyze_memory_diagnostic.py <wallpaper-output> <new-wallpaper-analysis.json>

pwsh -NoProfile -File eng/performance/Invoke-MemoryDiagnostic.ps1 `
  -ProbePath <probe-directory>/Dango.PerformanceProbe.exe `
  -OutputDirectory <new-window-output> -Mode Window -StableSeconds 60
python eng/performance/analyze_memory_diagnostic.py <window-output> <new-window-analysis.json>
```

The equivalent direct arguments are `<new-output> 60 Wallpaper --memory-diagnostic`.
The second argument means **stable observation seconds per checkpoint** in this
mode. The original three-argument interface, formal `samples.jsonl` schema and
acceptance protocol are unchanged. Diagnostic mode writes `memory-samples.jsonl`
instead, plus a `memory-protocol.json`; never feed it to the formal gate analyzer.

Schedule: 60 seconds warmup, stable observation at restoration counts **0, 1, 5,
10 and 20**, 20 hidden/visible cycles of at least 3 seconds per state, then host
disposal and 30 seconds further observation. With 60-second checkpoints the
scheduled minimum is 510 seconds (8.5 minutes); one-second dispatcher sampling
and startup can extend it. With 180-second checkpoints it is 1,110 seconds
(18.5 minutes). Neither setting constitutes the formal 900/3,600-second protocol.
The default 60-second segments cannot establish the formal 180-second CPU gate.

`Invoke-MemoryDiagnostic.ps1` starts a hidden test process and records its actual
PID/exit code in `process-exit.json`. A completion file alone is insufficient.
The existing probe heartbeat fails only the test process after a 15-second UI
stall. A waiter timeout records the test PID but does not kill it; inspect that
exact process before further measurement. No real run is performed by tests.

## Field contract

Every JSONL row has `diagnosticSchema: 1`, `runKind: memory-diagnostic`, monotonic
elapsed seconds, phase and phase age, requested restoration count, actual visible/
running state, and an observation kind. `sample` is approximately once per second;
`phase-enter` and before/after hide, restore and disposal are synchronous boundary
observations. All objects obtained by reflection are temporary within that call.
The metrics are sequential API snapshots, not an atomic process census.

| Metric | Source and interpretation |
|---|---|
| `workingSetBytes`, `privateBytes` | Current test `Process.WorkingSet64` / `PrivateMemorySize64`; different from managed allocation or surviving heap |
| `cpuSeconds`, `cpuMachinePercent` | Own process `TotalProcessorTime`; delta / actual elapsed interval / logical processor count; first rate is null |
| `handles`, `threads`, `gdiObjects`, `userObjects` | Own process handle/thread counts and `GetGuiResources`; GUI API errors fail rather than produce invented zero counts |
| `managedBytesEstimate` | `GC.GetTotalMemory(false)`; approximate allocated managed memory, which may include garbage not yet collected |
| `lastCompletedGc` | `GC.GetGCMemoryInfo()` heap, fragmentation, committed bytes, generation, index, pinned/finalization metadata for the last completed collection; null before the first GC; not current live bytes |
| `collectionCounts`, `collectionDeltas` | `GC.CollectionCount(0..2)` and differences across adjacent observations; no GC is requested by this tool |
| `totalAllocatedBytes`, `allocatedDeltaBytes` | `GC.GetTotalAllocatedBytes(false)` and approximate process-wide allocation delta; includes sampler/serialization/history overhead |
| `allocationIntervalSeconds`, `allocationBytesPerSecond` | Time since the previous memory observation, including boundary observations; rate = allocation delta / that interval; all deltas/rates null in the first row |
| `frameTotals`, `schedulerEnabled` | Existing immutable scheduler counters and running state, without adding/changing scheduler instrumentation; diagnostic health context only |
| `captureElapsedMs` | Time spent collecting metrics/identities before constructing/serializing the returned record; excludes serialization and is not the full observer cost |

`resources` uses monotonic identity IDs from a `ConditionalWeakTable` whose values
hold only `WeakReference<object>`, strings and scalars. The weak-history list also
contains no strong target references. `Capture` and weak-target inspection are
not inlined, so temporary references do not escape into the visibility-changing
caller. Inspecting a weak target briefly makes it strong during that call; the
probe does not promise zero observer effect on concurrent collection timing.

Observed own-host resources include WorkerW's reflected native host and HwndSource,
each source's composition target/root visual, the known scene/overlay windows,
and any windows registered with this isolated WPF Application. `Handle` and
`PresentationSource.FromVisual` inspect existing objects; no `EnsureHandle`, Show,
layout update, resource event subscription or OS-wide window enumeration is added.
Handle validity checks confirm ownership by this test PID; HWND numbers may be
reused, so HWND alone is never treated as object identity.

`kind` is assigned when an identity is first observed; `roles` may overlap. Counts
are per distinct identity/kind: do not add roles as independent resources.
`observedIdentities` means first seen, not a complete creation event count.
`currentlyReferenced` means reachable from the inspected slots/source list at that
instant. `detachedObserved` records leaving those slots, not destruction.
`disposedObserved` requires an observed explicit disposed state (HwndSource or
native host); other types may report null. `weakAlive`/`collected` describe a short
weak reference, and `firstCollectedSeconds` is when collection was first observed,
not its precise GC timestamp. No inference replaces missing disposal evidence.

The pre-existing probe and product retain host/window objects and scheduler
references through shutdown. This tool adds no strong historical resource owners,
but it is **not** a whole-host collection test. The accumulating identity metadata,
reflection, counter capture and JSON serialization allocate and contribute to
reported resource usage. A detached object still alive, including after a natural
GC, cannot by itself establish an unintended retaining path or a leak.

## Offline interpretation and verification

The analyzer checks allocation/GC/CPU counter consistency, identity history/counts,
phase order, actual visible/hidden states, all five checkpoint durations, disposal
and a matching external process exit. `complete` means this diagnostic protocol
has complete recorded evidence; **`acceptanceEvaluated` is always false**.
Missing/failed evidence remains `incomplete`; malformed counters are `invalid`.

Each checkpoint reports first/last/peak values, its final half-window (capped at
30 seconds) duration-weighted mean, change from checkpoint 0, cumulative-allocation
delta divided by actual segment duration, GC collection differences, latest GC
metadata and weak resource counts. Compare the two hosts' checkpoint-relative
changes using identical candidate/runtime/durations. A plateau or continued rise
is descriptive evidence; do not automatically label either as a cache or leak.

GPU/DWM allocations, complete native heaps and retaining paths are not measured.
Never subtract the managed-heap metric from private bytes to claim an exact native
leak size. If these observations cannot distinguish cache residency, delayed
collection or resource retention, the root cause remains unlocated.

```powershell
python -m unittest discover -s eng/performance -p 'test_*.py'
```

Synthetic tests exercise incorrect rate denominators, regressing counters,
incorrect release counts/identity reuse, interrupted checkpoints, lifecycle and
exit failures, and separation from formal acceptance. They do not launch a desktop.

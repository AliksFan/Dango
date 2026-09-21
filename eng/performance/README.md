# Isolated desktop performance evidence

This is a verification harness, not a product entry point. It constructs the existing
Product `DangoApplicationHost` from an already published package with a separate,
fictional settings file. It does not activate the daily instance, alter the package,
capture the desktop, inject input, change native parameters, or add entities.

Build with an existing clean Release/self-contained package:

```powershell
dotnet publish eng/tools/Dango.PerformanceProbe/Dango.PerformanceProbe.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:DesktopPackagePath=<absolute-package-directory> -o <new-probe-directory>
# Compare SHA-256 of the three Dango assemblies, both native libraries,
# coreclr.dll, PresentationCore.dll and PresentationFramework.dll with the input.
# Use a NEW absolute output directory; the probe refuses an existing one.
Start-Process <probe-directory>/Dango.PerformanceProbe.exe `
  -ArgumentList @('<new-output-directory>', '3600', 'Wallpaper') -WindowStyle Hidden
python eng/performance/analyze_samples.py <output>/samples.jsonl <output>/analysis.json
python -m unittest discover -s eng/performance -p 'test_*.py'
```

PowerShell `Start-Process -ArgumentList` joins arguments; quote a path containing
spaces before passing it. The harness has no daily-instance mutex or settings
fallback. It records only its own process and host. Confirm existing user instances
read-only before a run and verify their identity/settings hashes afterwards.

The schedule is 60 seconds warmup, the requested wall-clock observation, 20
hide/show cycles of at least 3 seconds per state, 180 seconds hidden, 180 seconds
restored, then 30 seconds after host disposal. The process exits itself. Attach a
process waiter to record actual exit code and confirm no test PID remains; a
`complete.json` alone does not prove process exit. No system sleep prevention is
installed. A missing UI heartbeat for 15 seconds fails this test process only.

`samples.jsonl` records one sample per second. Capability detection is explicit:
legacy hosts expose `motionTimer` and produce schema 1; candidate hosts expose
`motionScheduler` and produce schema 2. Missing or incompatible candidate diagnostic
members fail the probe instead of silently falling back to the legacy timer.

- Schema 1 retains the original timer completion-to-completion update metric.
  It does not know whether each old callback successfully applied a frame.
- Schema 2 subscribes only to `CallbackCompleted`, reads the product's immutable
  diagnostic snapshot, and counts a frame only when `AppliedFrames` increases by
  exactly one. It uses the original `LastFinishedTimestamp` for intervals. Multiple
  successful fixed steps, duplicate rendering events, zero-step events, and refreshes
  are not extra successful frames. `motionTicks`/`tickTotal` are checked aliases of
  interval/cumulative applied frames, never aliases for successful physics steps.
- `frameCounters` contains each sample's deltas; `frameTotals` contains monotonic
  lifetime totals across both hosts. Fields distinguish callbacks, duplicates,
  successful steps, applied/refresh frames, overloads, long pauses, invalid-clock
  resets, faults, and the three unplanned dropped-time reasons plus
  `lifecycleDroppedTicks`. The latter records unadvanced time/remainder at normal
  Stop and unexecuted old-batch steps during a host switch or Stop/Start; expected
  hide/exit cancellation can increase it without causing a timing failure.
  Dropped/remainder values use
  TimeSpan ticks (10,000,000 per second). `schedulerEnabled` is the real scheduler
  state; the retained `motionTimerEnabled` alias has exactly the same value.
- `handlerElapsedMs` uses the original product entry/exit timestamps. Candidate
  `renderIntervalsMs` contains completion intervals for all accepted scheduler
  callbacks, including duplicates. Neither callback rate nor application update
  rate measures GPU presentation or CPU execution time.

The observer does not subscribe to `CompositionTarget.Rendering`, including while
hidden. Reflection, diagnostic snapshot conversion and sampling add overhead; CPU,
memory and waits include it. Report these as instrumented isolated-host measurements,
not as an uninstrumented daily-executable benchmark. The normal single-instance and
startup workflow remain outside this harness. The chart explicitly distinguishes
legacy timer completions from candidate applied frames.

For the separate restoration-memory attribution mode, see
[MEMORY-DIAGNOSTIC.md](MEMORY-DIAGNOSTIC.md). Its distinct output/protocol never
substitutes for the formal resource gates described here.

`mixed` seconds cross state boundaries and are not credited to a pure idle/active/
doze state. `active` means the product's own autonomous movement, eye activity or
light feedback. This run sends no user gestures. Sleep is identified by the real
controller, not by elapsed wall time. The original schema 1 product's one-step-per-
callback clock slows at less than 60 callbacks/second; the candidate's successful
step count is recorded separately. Raw intervals are never filtered to make the
FPS target pass.

The application update target remains **60 FPS**. The analyzer also accepts an
explicit active-state acceptance floor with `--min-application-fps` (finite
1..60 inclusive; default **60**, preserving previous decisions). The user-approved
floor for the new performance acceptance run is **59 FPS**:

```powershell
python eng/performance/analyze_samples.py <output>/samples.jsonl <output>/analysis-59.json `
  --expected-observe-seconds 900 --min-application-fps 59
```

For the 3,600-second run, use `--expected-observe-seconds 3600`. The Python API
exposes the same keyword-only `min_application_fps=60.0`. This selects a policy for
that analysis; it does not change the product, raw intervals or historical reports.
Write a new report filename when applying a different policy to retained evidence.

Reports record `acceptancePolicy.applicationFps` with the 60 FPS target, selected
minimum, inclusive `>=` comparison, active state, required 180 seconds and metric
source. Application interval summaries retain `fps`, `targetFps`, `fpsMinusTarget`
and `targetStatus`, and add `minimumAcceptedFps`, `fpsMinusMinimumAccepted` and
`acceptanceStatus`. Thus 59.4 FPS can be `below-target` while `meets-minimum` under
an explicitly selected 59 FPS floor. Other state/phase summaries are descriptive;
the existing overall FPS check still evaluates the aggregate active-state mean.
`meets-minimum` is a numeric comparison, not proof of sufficient state coverage
or valid complete evidence. Check `validGateSeconds` (active requires at least
180 seconds), `insufficientGates`, data validity and the overall result before
claiming acceptance; an empty `failedTargets` alone is insufficient.
Rendering callback rate is not substituted for actual application updates, and
neither is GPU-present FPS. CPU, peak working set, unplanned dropped time, faults,
continuous observation, state duration, hide/restore, release, exit and data
integrity requirements are unchanged. Selecting 59 FPS cannot excuse their failure.

`disposal.json` checks actual scheduler/timer, runtime, scene, tray and window
release, attachment entry count and router disposed state. `attachmentsReleased`
is only a null-reference test: retained references do not prove leaks. Old schema 1
artifacts without internal attachment observations remain partial; new schema 2
requires those observations. The old files are never rewritten or supplemented
with measurements from a different run. Actual process exit is verified separately.

Schema 2 analysis rejects missing, negative, noninteger, regressing or inconsistent
counters; a step count substituted for frame count cannot pass. It reports dropped
time and faults even when there is no runtime error string. The retained
`scheduler.droppedSeconds` reports overload/pause/fault losses;
`lifecycleDroppedSeconds` reports expected lifecycle cancellation separately, and
`totalDroppedSeconds` sums both. Unplanned drops or faults prevent an overall pass, without inventing a frequency or duration allowance. These
are diagnostic correctness failures, not new CPU, memory or FPS thresholds. Zero
observed unplanned drops is not an independent proof that elapsed-time accounting is correct;
controlled-clock product tests supply that separate evidence.

The analysis requires valid complete evidence before an overall success result.
Short or interrupted observations cannot represent 60 minutes. Synthetic unit
test fixtures test the evaluator only and never count as desktop evidence. Growth
slopes describe this run; no universal leak threshold is invented. GPU, system
sleep, other DPI/monitor/OS combinations and unavailable capacities remain separate.

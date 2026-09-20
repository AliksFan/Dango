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

`samples.jsonl` records one sample per second and all product timer callback
intervals since the preceding sample. Timer observers run **after** product tick
handlers, so the metric is completion-to-completion application update interval,
not time spent doing physics/rendering, a compositor callback, or GPU present FPS.
The observer does not subscribe to `CompositionTarget.Rendering`, including while
hidden. CPU/memory include observer overhead and the alternative process entry;
report them as instrumented isolated-host measurements, not as an uninstrumented
daily-executable benchmark. The normal single-instance/startup workflow is outside
this harness.

`mixed` seconds cross state boundaries and are not credited to a pure idle/active/
doze state. `active` means the product's own autonomous movement, eye activity or
light feedback. This run sends no user gestures. Sleep is identified by the real
controller, not by elapsed wall time. At less than 60 updates/second its fixed-step
clock advances slower than the wall clock. Raw intervals are never filtered to
make the FPS target pass.

`disposal.json` checks actual timer/runtime/scene/tray/window release. In schema 1,
`attachmentsReleased` tests whether the managed attachment-controller reference
was set to null. Current product code disposes that object and retains its empty
reference; `false` is therefore **not evidence of a resource leak**. Its internal
entry/router cleanup is source-verified only in this version. Process exit is
verified separately. Future probes should measure internal disposed state if a
separate attachment lifetime investigation is required.

The analysis requires valid complete evidence before an overall success result.
Short or interrupted observations cannot represent 60 minutes. Synthetic unit
test fixtures test the evaluator only and never count as desktop evidence. Growth
slopes describe this run; no universal leak threshold is invented. GPU, system
sleep, other DPI/monitor/OS combinations and unavailable capacities remain separate.

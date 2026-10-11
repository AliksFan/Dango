[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BoundedProcess.ps1')
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$executable = (Get-Command pwsh -ErrorAction Stop).Source
$results = [Collections.Generic.List[object]]::new()
$normal = Invoke-DangoBoundedProcess $executable @('-NoProfile','-NonInteractive','-Command',"[Console]::Out.Write('quoted value with spaces'); [Console]::Error.Write('stderr fixture'); exit 0") 5000
if ($normal.TimedOut -or $normal.ExitCode -ne 0 -or $normal.StdOut -cne 'quoted value with spaces' -or $normal.StdErr -cne 'stderr fixture') { throw 'Normal query/argument/output gate failed.' }
$results.Add([pscustomobject]@{Name='normal-output-and-arguments';Result=$normal;Pass=$true})
$failure = Invoke-DangoBoundedProcess $executable @('-NoProfile','-NonInteractive','-Command','exit 7') 5000
if ($failure.TimedOut -or $failure.ExitCode -ne 7) { throw 'Actual nonzero exit was lost.' }
$results.Add([pscustomobject]@{Name='actual-nonzero-exit';Result=$failure;Pass=$true})
$hung = Invoke-DangoBoundedProcess $executable @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 10') 250
if (-not $hung.TimedOut -or -not $hung.Exited -or $hung.WallMilliseconds -ge 3000 -or
    (Get-Process -Id $hung.ProcessId -ErrorAction SilentlyContinue)) { throw 'Hung query was not bounded and released.' }
$results.Add([pscustomobject]@{Name='hung-query-timeout-and-exit';Result=$hung;Pass=$true})
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'results.json') -Encoding utf8
$results | Select-Object Name,Pass

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Evaluate only the trusted runner's pure ownership decision. Never run its resource operations.
$taskRunner = Join-Path $PSScriptRoot 'invoke-backend-postgres-tests.ps1'
$taskTokens = $null
$taskErrors = $null
$taskAst = [System.Management.Automation.Language.Parser]::ParseFile($taskRunner, [ref]$taskTokens, [ref]$taskErrors)
if ($taskErrors.Count) { throw 'runner_parse_failed' }
$taskFunction = $taskAst.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-TaskOwnedPostgresInstance'
    }, $false)
if ($null -eq $taskFunction) { throw 'runner_ownership_gate_missing' }
. ([scriptblock]::Create($taskFunction.Extent.Text))

$taskStart = [DateTime]::UtcNow
$taskDirectory = 'C:\fictional-c2\pgdata'
$taskExecutable = 'C:\fictional-postgres18\postgres.exe'
$taskProcess = [pscustomobject]@{ ExecutablePath=$taskExecutable; CommandLine="postgres.exe -D `"$taskDirectory`""; CreationDate=$taskStart }
$taskChecks = @(
    @{ Name='exact'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=$taskProcess; Expected=$true },
    @{ Name='no-recorded-start-pid'; Pid=0; Current=4101; Directory=$taskDirectory; Process=$taskProcess; Expected=$false },
    @{ Name='different-pid'; Pid=4102; Current=4101; Directory=$taskDirectory; Process=$taskProcess; Expected=$false },
    @{ Name='directory-prefix'; Pid=4101; Current=4101; Directory="$taskDirectory-other"; Process=$taskProcess; Expected=$false },
    @{ Name='missing-directory-record'; Pid=4101; Current=4101; Directory=''; Process=$taskProcess; Expected=$false },
    @{ Name='other-program'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=[pscustomobject]@{ ExecutablePath='C:\other\postgres.exe'; CommandLine=$taskProcess.CommandLine; CreationDate=$taskStart }; Expected=$false },
    @{ Name='old-process'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=[pscustomobject]@{ ExecutablePath=$taskExecutable; CommandLine=$taskProcess.CommandLine; CreationDate=$taskStart.AddMinutes(-1) }; Expected=$false },
    @{ Name='missing-process'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=$null; Expected=$false },
    @{ Name='command-directory-prefix'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=[pscustomobject]@{ ExecutablePath=$taskExecutable; CommandLine="postgres.exe -D `"$taskDirectory-other`""; CreationDate=$taskStart }; Expected=$false },
    @{ Name='missing-command'; Pid=4101; Current=4101; Directory=$taskDirectory; Process=[pscustomobject]@{ ExecutablePath=$taskExecutable; CommandLine=$null; CreationDate=$taskStart }; Expected=$false }
)
$taskFailed = @()
foreach ($taskCheck in $taskChecks) {
    $taskActual = Test-TaskOwnedPostgresInstance -StartedPid $taskCheck.Pid -CurrentPid $taskCheck.Current `
        -ExpectedDataDirectory $taskDirectory -RecordedDataDirectory $taskCheck.Directory `
        -ExpectedExecutable $taskExecutable -CurrentProcess $taskCheck.Process -StartTime $taskStart
    if ($taskActual -ne $taskCheck.Expected) { $taskFailed += $taskCheck.Name }
}
if ($taskFailed.Count) { throw "runner_ownership_failed: $($taskFailed -join ', ')" }
Write-Output "Runner ownership: $($taskChecks.Count)/$($taskChecks.Count) passed; no live resource operations."

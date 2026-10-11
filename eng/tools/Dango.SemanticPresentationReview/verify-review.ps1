param([Parameter(Mandatory)][string]$Executable, [string]$EvidenceName = 'suite-r2')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if ($EvidenceName -notmatch '^[a-z0-9-]{1,48}$') { throw 'Invalid isolated evidence name.' }
$taskEvidence = Join-Path $taskRoot "artifacts/p1-windows-review/$EvidenceName"
if (Test-Path -LiteralPath $taskEvidence) { throw 'Evidence directory already exists; preserve it.' }
New-Item -ItemType Directory -Path $taskEvidence | Out-Null
. (Join-Path $taskRoot 'eng/tools/Dango.SafeSpawnAdmission/linux/BoundedProcess.ps1')
$taskRuns = [Collections.Generic.List[object]]::new()
$taskPlans = @(
    @{ Name='push'; Mutation='none'; Oracle=$null },
    @{ Name='stack'; Mutation='none'; Oracle=$null },
    @{ Name='withdraw'; Mutation='none'; Oracle=$null },
    @{ Name='push'; Mutation='wrong-origin'; Oracle='actual-authority-center' },
    @{ Name='push'; Mutation='shared-replica'; Oracle='replica-ownership' },
    @{ Name='push'; Mutation='fallback'; Oracle='actual-fallback' },
    @{ Name='withdraw'; Mutation='stale-visual'; Oracle='visual-membership' }
)
function Assert-Review($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
try {
    foreach ($taskPlan in $taskPlans) {
        $taskName = "$($taskPlan.Name)-$($taskPlan.Mutation)"
        $taskOut = Join-Path $taskEvidence $taskName
        $taskArguments = @("--verify-$($taskPlan.Name)",
            (Join-Path $taskRoot 'artifacts/p1-r1/native-build/Release/dango_physics_module.dll'),
            (Join-Path $taskRoot 'artifacts/p1-r1/semantic-r1'),
            (Join-Path $taskRoot 'src/Dango.Desktop/AppShell/ControlCenter/ProductHomeView.xaml'), $taskOut, $taskPlan.Mutation)
        $taskRun = Invoke-DangoBoundedProcess -Executable (Resolve-Path -LiteralPath $Executable).Path -Arguments $taskArguments -TimeoutMilliseconds 60000
        $taskRuns.Add([pscustomobject]@{Name=$taskName;ExpectedOracle=$taskPlan.Oracle;ProcessId=$taskRun.ProcessId;
            ExitCode=$taskRun.ExitCode;TimedOut=$taskRun.TimedOut;Exited=$taskRun.Exited;WallMilliseconds=$taskRun.WallMilliseconds})
        $taskRuns | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'processes.json')
        $taskRun.StdOut | Set-Content (Join-Path $taskEvidence "$taskName.stdout.log")
        $taskRun.StdErr | Set-Content (Join-Path $taskEvidence "$taskName.stderr.log")
        Assert-Review ($taskRun.Exited -and -not $taskRun.TimedOut) "$taskName did not finish within the unchanged 60-second deadline."
        $taskResult = Get-Content (Join-Path $taskOut 'results.json') -Raw | ConvertFrom-Json
        Assert-Review ($taskResult.Pid -eq $taskRun.ProcessId -and $taskResult.Mode -eq $taskArguments[0] -and $taskResult.Mutation -eq $taskPlan.Mutation) 'Process/result identity mismatch.'
        Assert-Review ($taskResult.allocationBefore -eq 0 -and $taskResult.allocationAfter -eq 0) 'Native allocation did not return to zero.'
        foreach ($taskProperty in $taskResult.beforeHashes.PSObject.Properties) {
            Assert-Review ($taskResult.afterHashes.($taskProperty.Name) -eq $taskProperty.Value) 'Input hash changed.'
        }
        if ($taskPlan.Mutation -eq 'none') {
            Assert-Review ($taskRun.ExitCode -eq 0 -and $taskResult.ExitCode -eq 0 -and $taskResult.Pass -and $null -eq $taskResult.Error -and @($taskResult.cases).Count -eq 3) 'Normal scene did not pass all three schedules.'
            $taskRates = @($taskResult.cases | ForEach-Object { "$($_.rateA)/$($_.rateB)" } | Sort-Object)
            Assert-Review (($taskRates -join ',') -eq '144/30,30/60,60/144') 'Missing or duplicated schedule.'
            foreach ($taskCase in $taskResult.cases) {
                Assert-Review ($taskCase.scenario -eq $taskPlan.Name -and $taskCase.CommonTimeChecks -gt 0 -and $taskCase.MaximumCommonCenterError -le 1e-5) 'Common-time gate failed.'
                foreach ($taskPane in @($taskCase.left,$taskCase.right)) {
                    Assert-Review ($taskPane.Steps -eq 600 -and $taskPane.nativeOrdinal -eq 600) 'Incomplete fixed-step trajectory.'
                    foreach ($taskMetric in @('MaximumCenterError','MaximumContourError','MaximumPenetration','MaximumSupportGap')) {
                        Assert-Review ([double]::IsFinite([double]$taskPane.$taskMetric)) 'Non-finite geometry metric.'
                    }
                    Assert-Review ($taskPane.MaximumCenterError -le 1e-5 -and $taskPane.MaximumContourError -le 1e-5 -and $taskPane.MaximumPenetration -le .031 -and $taskPane.MaximumSupportGap -le .008) 'Actual WPF geometry gate failed.'
                    if ($taskPlan.Name -ne 'push') { Assert-Review ($taskPane.SupportedSteps -gt 30) 'No actual supporting interval.' }
                    if ($taskPlan.Name -eq 'withdraw') { Assert-Review ($taskPane.RemovedSteps -eq 420) 'Withdrawal did not complete.' }
                }
            }
            Assert-Review (@(Get-ChildItem -LiteralPath $taskOut -Filter '*.png').Count -eq 9) 'Missing WPF layout images.'
        } else {
            Assert-Review ($taskRun.ExitCode -eq 1 -and $taskResult.ExitCode -eq 1 -and -not $taskResult.Pass -and $taskResult.Oracle -eq $taskPlan.Oracle) 'Mutation did not fail its specified oracle.'
        }
        Write-Output "$taskName : exit $($taskRun.ExitCode), completed"
    }
    @{Version=1;Pass=$true;NormalScenes=3;SchedulePairs=9;Mutations=4;ChildDeadlineMilliseconds=60000;Runs=$taskRuns} |
        ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'manifest.json')
} catch {
    @{Version=1;Pass=$false;Error=$_.ToString();Runs=$taskRuns} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskEvidence 'manifest.json')
    throw
}

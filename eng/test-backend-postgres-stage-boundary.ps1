[CmdletBinding()]
param([string]$RunnerPath = (Join-Path $PSScriptRoot 'invoke-backend-postgres-tests.ps1'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Parse the runner without invoking it: this check must never probe or start PostgreSQL.
$taskTokens = $null
$taskParseErrors = $null
$taskRunnerAst = [Management.Automation.Language.Parser]::ParseFile(
    [IO.Path]::GetFullPath($RunnerPath), [ref]$taskTokens, [ref]$taskParseErrors)
if ($taskParseErrors.Count -ne 0) { throw 'backend_stage_boundary_runner_parse_failed' }

function Get-TaskAssignment([string]$Name) {
    $taskAssignments = @($taskRunnerAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -eq $Name
    }, $true))
    if ($taskAssignments.Count -ne 1) { throw 'backend_stage_boundary_assignment_unavailable' }
    return $taskAssignments[0]
}

$taskChecks = @()
$taskEnvironmentAssignment = Get-TaskAssignment 'taskTestEnvironment'
$taskEnvironmentLiteral = $taskEnvironmentAssignment.Find({
    param($node)
    $node -is [Management.Automation.Language.HashtableAst]
}, $true)
if ($null -eq $taskEnvironmentLiteral -or @($taskEnvironmentAssignment.FindAll({
    param($node)
    $node -is [Management.Automation.Language.CommandAst] -or
        $node -is [Management.Automation.Language.MemberExpressionAst]
}, $true)).Count -ne 0) { throw 'backend_stage_boundary_environment_not_data_only' }

# Only the data-only assignment runs, using fictional strings. ProcessStartInfo stays in memory.
$taskEnvironmentProbe = & {
    $taskConnection = 'fictional-connection-never-opened'
    $taskDataDirectory = 'fictional-directory-never-accessed'
    . ([scriptblock]::Create($taskEnvironmentAssignment.Extent.Text))
    return $taskTestEnvironment
}
$taskChildInfo = [Diagnostics.ProcessStartInfo]::new()
$taskChildInfo.Environment['DANGO_BACKEND_PROFILE_TEST_APPROVED'] = '1'
foreach ($taskName in $taskEnvironmentProbe.Keys) {
    $taskChildInfo.Environment[$taskName] = [string]$taskEnvironmentProbe[$taskName]
}
$taskChecks += [pscustomobject]@{
    Name = 'InheritedProfileApprovalIsRemovedFromTheChildEnvironment'
    Passed = $taskChildInfo.Environment['DANGO_BACKEND_PROFILE_TEST_APPROVED'] -ceq '0'
}

$taskFilterAssignment = Get-TaskAssignment 'taskBaseAllowedFilter'
$taskFilterLiterals = @($taskFilterAssignment.FindAll({
    param($node)
    $node -is [Management.Automation.Language.StringConstantExpressionAst]
}, $true))
if ($taskFilterLiterals.Count -ne 1) { throw 'backend_stage_boundary_filter_not_literal' }
$taskFilter = $taskFilterLiterals[0].Value
$taskCombinedAssignment = $taskRunnerAst.Find({
    param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'taskTestFilter'
}, $true)
if ($null -eq $taskCombinedAssignment -or @($taskCombinedAssignment.FindAll({
    param($node)
    $node -is [Management.Automation.Language.CommandAst] -or
        $node -is [Management.Automation.Language.MemberExpressionAst]
}, $true)).Count -ne 0) { throw 'backend_stage_boundary_filter_not_data_only' }
$taskPositiveSelector = 'FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests'
$taskCombinedFilter = & {
    $taskBaseAllowedFilter = $taskFilter
    $TestFilter = $taskPositiveSelector
    $B2 = $false; $B3 = $false; $B4 = $false
    . ([scriptblock]::Create($taskCombinedAssignment.Extent.Text))
    return $taskTestFilter
}
# Execute only filter construction. It must AND the caller's positive selector with the
# mandatory exclusion, so requesting the profile class cannot revive it in an old stage.
$taskClauses = @($taskFilter -split '&')
$taskChecks += [pscustomobject]@{
    Name = 'OldStageBaselineExcludesProfileEvenWithAnExplicitPositiveSelector'
    Passed = $taskCombinedFilter -ceq ('(' + $taskFilter + ')&(' + $taskPositiveSelector + ')') -and
        @($taskClauses | Where-Object {
        $_ -match '^FullyQualifiedName!~(?:Dango\.Server\.Tests\.Accounts\.)?AccountProfilePostgresTests$'
    }).Count -eq 1
}

$taskChecks | Format-Table -AutoSize
$taskFailures = @($taskChecks | Where-Object { -not $_.Passed })
if ($taskFailures.Count -ne 0) {
    throw ('backend_stage_boundary_failed:' + $taskFailures.Count + '/' + $taskChecks.Count)
}
Write-Output ('backend_stage_boundary_passed:' + $taskChecks.Count + '/' + $taskChecks.Count)

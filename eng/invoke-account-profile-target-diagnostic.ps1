[CmdletBinding()]
param(
    [string]$RunName='run20261008b0',
    [string]$PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'),
    [string]$ApprovalManifest,
    [string]$ApprovalManifestSha256,
    [switch]$PrepareApprovalManifest,
    [switch]$ExecuteApprovedPlan
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force
$taskDiagPlan=Get-DangoProfileDiagnosticPlan $RunName $PostgresBinDirectory (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'profile_diagnostic_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskDiagInputs=if ($PrepareApprovalManifest) { New-DangoProfileDiagnosticApprovalManifest $taskDiagPlan $ApprovalManifest }
        else { Get-DangoProfileDiagnosticReviewInputs $taskDiagPlan }
    $taskDiagPlan | Add-Member ReviewInputs $taskDiagInputs
    $taskDiagPlan; return
}
Assert-DangoProfileApproval ([bool]$ExecuteApprovedPlan)
if (-not $IsWindows) { throw 'profile_run_windows_required' }
$taskDiagApproved=Assert-DangoProfileDiagnosticReviewInputs $taskDiagPlan $ApprovalManifest $ApprovalManifestSha256
# One durable reservation consumes this diagnostic, even if no query is reached.
$taskDiagReservation=New-DangoProfileDiagnosticReservation $taskDiagPlan
Write-DangoProfileJson (Join-Path $taskDiagPlan.EvidenceDirectory 'approved-inputs.json') $taskDiagApproved -CreateNew
$taskDiagTools=[Collections.Generic.List[object]]::new()
$taskDiagProof=$null; $taskDiagStartAttempt=$null; $taskDiagStartReturn=$null; $taskDiagStartTool=$null
$taskDiagControlExit=-1; $taskDiagStartupObservation=$null; $taskDiagPort=0
$taskDiagPassword=$null; $taskDiagQueryEnvironment=$null; $taskDiagFailure=$null
$taskDiagCleanup=$false; $taskDiagOutputCleanup=$false; $taskDiagFacts=$null; $taskDiagMatches=$null
$taskDiagQueryAttempted=$false; $taskDiagFactsSaved=$false
try {
    foreach ($taskDiagRelative in @('state.json','credential.dpapi')) {
        $taskDiagPath=Join-Path $taskDiagPlan.StateDirectory $taskDiagRelative
        Assert-DangoProfileRegularPath $taskDiagPath
        if (-not [IO.File]::Exists($taskDiagPath)) { throw 'profile_diagnostic_existing_state_required' }
    }
    Assert-DangoProfileRegularPath $taskDiagPlan.DataDirectory
    if (-not [IO.Directory]::Exists($taskDiagPlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskDiagPlan.DataDirectory 'postmaster.pid'))) { throw 'profile_run_existing_postmaster_rejected' }
    $taskDiagState=Get-Content -LiteralPath (Join-Path $taskDiagPlan.StateDirectory 'state.json') -Raw | ConvertFrom-Json
    if ($taskDiagState.Version -ne 1 -or $taskDiagState.RunName -cne $taskDiagPlan.RunName -or
        $taskDiagState.Database -cne $taskDiagPlan.Database -or $taskDiagState.Owner -cne $taskDiagPlan.Role -or
        -not (Test-DangoProfilePath $taskDiagState.DataDirectory $taskDiagPlan.DataDirectory) -or
        $taskDiagState.Port -isnot [long] -and $taskDiagState.Port -isnot [int] -or
        $taskDiagState.Port -lt 1 -or $taskDiagState.Port -gt 65535) { throw 'profile_diagnostic_state_rejected' }
    $taskDiagPort=[int]$taskDiagState.Port
    if (Test-DangoProfileListener $taskDiagPort) { throw 'profile_run_existing_listener_rejected' }
    $taskDiagPostgres=Join-Path $taskDiagPlan.PostgresBinDirectory 'postgres.exe'
    $taskDiagControl=Join-Path $taskDiagPlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskDiagPsql=Join-Path $taskDiagPlan.PostgresBinDirectory 'psql.exe'
    $taskDiagVersion=Invoke-DangoProfileProcess $taskDiagPostgres @('--version') $taskDiagPlan.Repository -Receipts $taskDiagTools
    if ($taskDiagVersion.ExitCode -ne 0 -or $taskDiagVersion.Output -notmatch '^postgres \(PostgreSQL\) 18\.') { throw 'profile_run_postgres18_required' }
    $taskDiagStatus=Invoke-DangoProfileProcess $taskDiagControl @('status','-D',$taskDiagPlan.DataDirectory) $taskDiagPlan.Repository -Receipts $taskDiagTools
    if ($taskDiagStatus.ExitCode -ne 3) { throw 'profile_run_existing_postgres_rejected' }
    $taskDiagPlain=[Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes((Join-Path $taskDiagPlan.StateDirectory 'credential.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $taskDiagPassword=[Text.Encoding]::UTF8.GetString($taskDiagPlain) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskDiagPlain) }
    $taskDiagDecoded=$null
    try {
        $taskDiagDecoded=[Convert]::FromBase64String($taskDiagPassword)
        if ($taskDiagDecoded.Length -ne 32 -or [Convert]::ToBase64String($taskDiagDecoded) -cne $taskDiagPassword) { throw 'profile_run_credential_invalid' }
    } catch { throw 'profile_run_credential_invalid' }
    finally { if ($null -ne $taskDiagDecoded) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskDiagDecoded) } }
    [void](Assert-DangoProfileDiagnosticReviewInputs $taskDiagPlan $ApprovalManifest $ApprovalManifestSha256)
    $taskDiagStartAttempt=[DateTime]::UtcNow
    $taskDiagStartIndex=$taskDiagTools.Count
    try {
        $taskDiagStarted=Invoke-DangoProfileProcess $taskDiagControl @('start','-D',$taskDiagPlan.DataDirectory,'-l',
            (Join-Path $taskDiagPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $taskDiagPort",'-w','-t','30') `
            $taskDiagPlan.Repository -TimeoutSeconds 40 -Receipts $taskDiagTools `
            -OutputContract PostgresStartIgnored -PostgresStartPlan $taskDiagPlan -PostgresStartPort $taskDiagPort
        $taskDiagControlExit=$taskDiagStarted.ExitCode
    } finally {
        $taskDiagStartReturn=[DateTime]::UtcNow
        if ($taskDiagTools.Count -gt $taskDiagStartIndex) {
            $taskDiagStartTool=$taskDiagTools[$taskDiagStartIndex]
            if ($taskDiagStartTool.ExitCodeRecorded) { $taskDiagControlExit=$taskDiagStartTool.ExitCode }
        }
    }
    $taskDiagProof=Get-DangoProfilePostgresProof $taskDiagPlan $taskDiagPort $taskDiagControlExit $taskDiagStartTool
    if (-not (Test-DangoProfilePostgresProof $taskDiagProof $taskDiagPostgres $taskDiagPlan.DataDirectory `
        $taskDiagPort $taskDiagStartAttempt $taskDiagStartReturn $taskDiagPlan)) { throw 'profile_run_start_ownership_unconfirmed' }
    if ($taskDiagControlExit -ne 0) { throw 'profile_run_postgres_start_failed' }
    [void](Assert-DangoProfileDiagnosticReviewInputs $taskDiagPlan $ApprovalManifest $ApprovalManifestSha256)
    Write-DangoProfileJson (Join-Path $taskDiagPlan.EvidenceDirectory 'query-attempt.json') ([pscustomobject]@{
        Version=1; OwnerId=$taskDiagReservation.OwnerId; ReadOnlyQuery=1; BusinessWriteAttempts=0; AtUtc=[DateTime]::UtcNow }) -CreateNew
    $taskDiagQueryAttempted=$true
    $taskDiagQueryEnvironment=@{PGPASSWORD=$taskDiagPassword;PGCLIENTENCODING='UTF8'}
    $taskDiagQuery=Invoke-DangoProfileProcess $taskDiagPsql @('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1',
        '-h','127.0.0.1','-p',[string]$taskDiagPort,'-U',$taskDiagPlan.Role,'-d',$taskDiagPlan.Database,
        '-c',(Get-DangoProfileDiagnosticSql)) $taskDiagPlan.Repository $taskDiagQueryEnvironment -Receipts $taskDiagTools
    if ($taskDiagQuery.ExitCode -ne 0) { throw 'profile_diagnostic_query_failed' }
    $taskDiagFacts=Assert-DangoProfileDiagnosticEvidence $taskDiagQuery.Output
    Write-DangoProfileJson (Join-Path $taskDiagPlan.EvidenceDirectory 'target-facts.json') $taskDiagFacts -CreateNew
    $taskDiagFactsSaved=$true
    # Mismatch is the diagnostic result; it never permits migration or business writes.
    $taskDiagMatches=Get-DangoProfileDiagnosticMatches $taskDiagPlan $taskDiagFacts
    Write-DangoProfileJson (Join-Path $taskDiagPlan.EvidenceDirectory 'target-matches.json') $taskDiagMatches -CreateNew
} catch {
    $taskDiagFailure=if ($_.Exception.Message -match '^profile_(?:run|diagnostic)_[a-z0-9_]+$') { $_.Exception.Message } else { 'profile_diagnostic_operation_failed' }
} finally {
    try {
        if ($null -ne $taskDiagStartAttempt -and $null -ne $taskDiagStartReturn) {
            $taskDiagStartupObservation=Observe-DangoProfileStartup $taskDiagPlan $taskDiagPort $taskDiagControlExit $taskDiagStartTool $taskDiagStartAttempt
            if ($taskDiagStartupObservation.Verified) { $taskDiagProof=$taskDiagStartupObservation.Proof }
        }
        if ($null -ne $taskDiagProof -and $null -ne $taskDiagStartupObservation -and $taskDiagStartupObservation.Verified) {
            $taskDiagCurrent=Get-DangoProfilePostgresProof $taskDiagPlan $taskDiagPort $taskDiagProof.ControlExitCode $taskDiagStartTool
            if ($taskDiagCurrent.Pid -eq $taskDiagProof.Pid -and $taskDiagCurrent.CreatedAt -eq $taskDiagProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $taskDiagCurrent $taskDiagPostgres $taskDiagPlan.DataDirectory `
                    $taskDiagPort $taskDiagStartAttempt $taskDiagStartupObservation.ObservedUntil $taskDiagPlan)) {
                $taskDiagStopped=Invoke-DangoProfileProcess $taskDiagControl @('stop','-D',$taskDiagPlan.DataDirectory,'-m','fast','-w','-t','30') `
                    $taskDiagPlan.Repository -TimeoutSeconds 40 -Receipts $taskDiagTools
                $taskDiagCleanup=$taskDiagStopped.ExitCode -eq 0 -and
                    -not [IO.File]::Exists((Join-Path $taskDiagPlan.DataDirectory 'postmaster.pid')) -and
                    -not (Test-DangoProfileListener $taskDiagPort) -and
                    $null -eq (Get-Process -Id $taskDiagProof.Pid -ErrorAction SilentlyContinue)
            }
        } elseif ($null -eq $taskDiagStartAttempt) { $taskDiagCleanup=$true }
    } catch { $taskDiagCleanup=$false }
    if (@($taskDiagTools | Where-Object { -not $_.CleanupConfirmed }).Count -ne 0) { $taskDiagCleanup=$false }
    $taskDiagOutputCleanup=@($taskDiagTools | Where-Object { -not $_.OutputResourcesCleanupConfirmed }).Count -eq 0
    $taskDiagPassword=$null; $taskDiagQueryEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskDiagPlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1; OwnerId=$taskDiagReservation.OwnerId; Stage=$taskDiagPlan.Stage; Failure=$taskDiagFailure;
        DiagnosticCompleted=($null -eq $taskDiagFailure -and $taskDiagFactsSaved -and $null -ne $taskDiagMatches);
        FactsSaved=$taskDiagFactsSaved; QueryAttempted=$taskDiagQueryAttempted;
        ReadOnlyQueriesAttempted=if ($taskDiagQueryAttempted) { 1 } else { 0 }; BusinessWriteAttempts=0;
        TargetMatches=$taskDiagMatches; PostgreSqlCleanupConfirmed=$taskDiagCleanup;
        ToolOutputResourcesCleanupConfirmed=$taskDiagOutputCleanup; Tools=$taskDiagTools.Count; ToolProcesses=@($taskDiagTools);
        RecordedPort=$taskDiagPort; PostgreSqlPid=if ($null -ne $taskDiagProof) { $taskDiagProof.Pid } else { 0 };
        StartupObservation=$taskDiagStartupObservation; RetryAllowed=$false;
        DataRetained=[IO.Directory]::Exists($taskDiagPlan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskDiagPlan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskDiagPlan.StateDirectory 'b1test-state.json'))) }) -CreateNew
}
if (-not $taskDiagCleanup) { throw 'profile_run_cleanup_unconfirmed_no_retry' }
if (-not $taskDiagOutputCleanup) { throw 'profile_run_output_cleanup_unconfirmed_no_retry' }
if ($taskDiagFailure) { throw $taskDiagFailure }
Write-Output 'profile_diagnostic_completed_data_retained'

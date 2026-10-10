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
$taskProfilePlan=Get-DangoProfilePlan $RunName $PostgresBinDirectory (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'profile_run_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskProfileInputs=if ($PrepareApprovalManifest) { New-DangoProfileApprovalManifest $taskProfilePlan $ApprovalManifest }
        else { Get-DangoProfileReviewInputs $taskProfilePlan }
    $taskProfilePlan | Add-Member ReviewInputs $taskProfileInputs
    $taskProfilePlan; return
}
Assert-DangoProfileApproval ([bool]$ExecuteApprovedPlan)
if (-not $IsWindows) { throw 'profile_run_windows_required' }
$taskProfileApprovedInputs=Assert-DangoProfileReviewInputs $taskProfilePlan $ApprovalManifest $ApprovalManifestSha256

# An exclusive, durable whole-suite reservation precedes credentials and process startup.
# Even a failure before PostgreSQL starts consumes this one run; this entry cannot reset it.
$taskProfileReservation=New-DangoProfileReservation $taskProfilePlan
Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'approved-inputs.json') $taskProfileApprovedInputs -CreateNew
$taskProfileTools=[Collections.Generic.List[object]]::new()
$taskProfileProof=$null; $taskProfileStartAttempt=$null; $taskProfileStartReturn=$null
$taskProfileStartTool=$null; $taskProfileStartControlExit=-1
$taskProfileFailure=$null; $taskProfileCleanup=$false; $taskProfilePassword=$null; $taskProfileAfter=$null
$taskProfileHashes=@{}; $taskProfileAudit=$null; $taskProfileResources=$null; $taskProfileScopedGraph=$null
$taskProfileTestsAttempted=$false; $taskProfileInputsFrozen=$false
$taskProfileFacts=$null
$taskProfileTrx=Join-Path $taskProfilePlan.EvidenceDirectory 'account-profile.trx'
try {
    foreach ($taskProfileFile in @('state.json','credential.dpapi','b1test-state.json','b1tests-keys')) {
        $taskProfilePath=Join-Path $taskProfilePlan.StateDirectory $taskProfileFile
        Assert-DangoProfileRegularPath $taskProfilePath
        if (-not (Test-Path -LiteralPath $taskProfilePath)) { throw 'profile_run_existing_state_required' }
    }
    Assert-DangoProfileRegularPath $taskProfilePlan.DataDirectory
    if (-not [IO.Directory]::Exists($taskProfilePlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskProfilePlan.DataDirectory 'postmaster.pid'))) { throw 'profile_run_existing_postmaster_rejected' }
    $taskProfileState=Get-Content -LiteralPath (Join-Path $taskProfilePlan.StateDirectory 'state.json') -Raw | ConvertFrom-Json
    $taskProfileEpoch=Get-Content -LiteralPath (Join-Path $taskProfilePlan.StateDirectory 'b1test-state.json') -Raw | ConvertFrom-Json
    $taskProfileFacts=Assert-DangoProfileState $taskProfilePlan $taskProfileState $taskProfileEpoch
    if (Test-DangoProfileListener $taskProfileFacts.Port) { throw 'profile_run_existing_listener_rejected' }
    $taskProfilePostgres=Join-Path $taskProfilePlan.PostgresBinDirectory 'postgres.exe'
    $taskProfileControl=Join-Path $taskProfilePlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskProfilePsql=Join-Path $taskProfilePlan.PostgresBinDirectory 'psql.exe'
    $taskProfileDotnet=(Get-Command dotnet -CommandType Application).Source
    $taskProfileServer=Join-Path $taskProfilePlan.Repository 'src/Dango.Server/bin/Release/net10.0/Dango.Server.dll'
    $taskProfileTestDll=Join-Path $taskProfilePlan.Repository 'tests/Dango.Server.Tests/bin/Release/net10.0/Dango.Server.Tests.dll'
    $taskProfileSource=Join-Path $taskProfilePlan.Repository 'tests/Dango.Server.Tests/Accounts/AccountProfilePostgresTests.cs'
    $taskProfileSupport=Join-Path $taskProfilePlan.Repository 'tests/Dango.Server.Tests/Accounts/ProfileTestSupport.cs'
    $taskProfileOwnerSource=Join-Path $taskProfilePlan.Repository 'tests/Dango.Server.Tests/Accounts/ProfileTestOwner.cs'
    $taskProfileMigrationSource=Join-Path $taskProfilePlan.Repository 'src/Dango.Server/Persistence/Migrations/20261010055529_AccountProfile.cs'
    foreach ($taskProfilePath in @($taskProfilePostgres,$taskProfileControl,$taskProfilePsql,$taskProfileDotnet,
        $taskProfileServer,$taskProfileTestDll,$taskProfileSource,$taskProfileSupport,$taskProfileOwnerSource,$taskProfileMigrationSource)) {
        Assert-DangoProfileRegularPath $taskProfilePath
        if (-not [IO.File]::Exists($taskProfilePath)) { throw 'profile_run_required_input_missing' }
        $taskProfileHashes[$taskProfilePath]=(Get-FileHash -LiteralPath $taskProfilePath -Algorithm SHA256).Hash
    }
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'input-hashes.json') $taskProfileHashes -CreateNew
    $taskProfileInputsFrozen=$true
    $taskProfileVersion=Invoke-DangoProfileProcess $taskProfilePostgres @('--version') $taskProfilePlan.Repository -Receipts $taskProfileTools
    if ($taskProfileVersion.ExitCode -ne 0 -or $taskProfileVersion.Output -notmatch '^postgres \(PostgreSQL\) 18\.') { throw 'profile_run_postgres18_required' }
    $taskProfileStatus=Invoke-DangoProfileProcess $taskProfileControl @('status','-D',$taskProfilePlan.DataDirectory) $taskProfilePlan.Repository -Receipts $taskProfileTools
    if ($taskProfileStatus.ExitCode -ne 3) { throw 'profile_run_existing_postgres_rejected' }
    $taskProfilePlain=[Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes((Join-Path $taskProfilePlan.StateDirectory 'credential.dpapi')),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $taskProfilePassword=[Text.Encoding]::UTF8.GetString($taskProfilePlain) }
    finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskProfilePlain) }
    if ([string]::IsNullOrWhiteSpace($taskProfilePassword)) { throw 'profile_run_credential_invalid' }
    try { if ([Convert]::FromBase64String($taskProfilePassword).Length -ne 32) { throw 'profile_run_credential_invalid' } }
    catch { throw 'profile_run_credential_invalid' }
    $taskProfileStartAttempt=[DateTime]::UtcNow
    $taskProfileStartIndex=$taskProfileTools.Count
    try {
        $taskProfileStarted=Invoke-DangoProfileProcess $taskProfileControl @('start','-D',$taskProfilePlan.DataDirectory,'-l',
            (Join-Path $taskProfilePlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskProfileFacts.Port)",'-w','-t','30') `
            $taskProfilePlan.Repository -TimeoutSeconds 40 -Receipts $taskProfileTools
        $taskProfileStartControlExit=$taskProfileStarted.ExitCode
    } finally {
        $taskProfileStartReturn=[DateTime]::UtcNow
        if ($taskProfileTools.Count -gt $taskProfileStartIndex) { $taskProfileStartTool=$taskProfileTools[$taskProfileStartIndex] }
    }
    $taskProfileProof=Get-DangoProfilePostgresProof $taskProfilePlan $taskProfileFacts.Port $taskProfileStartControlExit $taskProfileStartTool
    if (-not (Test-DangoProfilePostgresProof $taskProfileProof $taskProfilePostgres $taskProfilePlan.DataDirectory `
        $taskProfileFacts.Port $taskProfileStartAttempt $taskProfileStartReturn)) { throw 'profile_run_start_ownership_unconfirmed' }
    if ($taskProfileStartControlExit -ne 0) { throw 'profile_run_postgres_start_failed' }
    $taskProfileSql=Get-DangoProfileDatabaseSql
    $taskProfilePsqlArgs=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$taskProfileFacts.Port,
        '-U',$taskProfilePlan.Role,'-d',$taskProfilePlan.Database,'-c',$taskProfileSql)
    $taskProfileBeforeResult=Invoke-DangoProfileProcess $taskProfilePsql $taskProfilePsqlArgs $taskProfilePlan.Repository `
        @{PGPASSWORD=$taskProfilePassword} -Receipts $taskProfileTools
    if ($taskProfileBeforeResult.ExitCode -ne 0) { throw 'profile_run_database_probe_failed' }
    $taskProfileBefore=$taskProfileBeforeResult.Output | ConvertFrom-Json
    if ($taskProfileBefore.database -cne $taskProfilePlan.Database -or $taskProfileBefore.role -cne $taskProfilePlan.Role -or
        $taskProfileBefore.version -lt 180000 -or $taskProfileBefore.version -ge 190000 -or
        $taskProfileBefore.listenAddresses -cne '127.0.0.1' -or
        -not (Test-DangoProfilePath $taskProfileBefore.dataDirectory $taskProfilePlan.DataDirectory) -or
        (@($taskProfileBefore.migrations) -join ',') -cne ($taskProfilePlan.PriorMigrations -join ',')) { throw 'profile_run_database_target_rejected' }
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'before.json') $taskProfileBefore -CreateNew
    $taskProfileConnection="Host=127.0.0.1;Port=$($taskProfileFacts.Port);Database=dango_backend_b0;Username=dango_b0_owner;Password=$taskProfilePassword;Include Error Detail=false"
    $taskProfileMigrationEnv=@{ DANGO_DATABASE=$taskProfilePlan.Database; DANGO_CONNECTION_STRING=$taskProfileConnection;
        DANGO_KEYS_DIRECTORY=(Join-Path $taskProfilePlan.StateDirectory 'b1tests-keys');
        DANGO_ENDPOINT="https://127.0.0.1:$($taskProfileFacts.Port)/"; DANGO_RECOVERY_EPOCH=$taskProfileFacts.Epoch.ToString('D');
        DANGO_EXPECTED_DATA_DIRECTORY=$taskProfilePlan.DataDirectory; DANGO_EXPECTED_MIGRATION=$taskProfilePlan.Migration;
        DANGO_EXPECTED_MIGRATIONS='' }
    [void](Assert-DangoProfileReviewInputs $taskProfilePlan $ApprovalManifest $ApprovalManifestSha256)
    $taskProfileMigration=Invoke-DangoProfileProcess $taskProfileDotnet @($taskProfileServer,'--formal-migrate') `
        $taskProfilePlan.Repository $taskProfileMigrationEnv -TimeoutSeconds 60 -Receipts $taskProfileTools
    if ($taskProfileMigration.ExitCode -ne 0) { throw 'profile_run_exact_migration_failed' }
    $taskProfileMigratedResult=Invoke-DangoProfileProcess $taskProfilePsql $taskProfilePsqlArgs $taskProfilePlan.Repository `
        @{PGPASSWORD=$taskProfilePassword} -Receipts $taskProfileTools
    if ($taskProfileMigratedResult.ExitCode -ne 0) { throw 'profile_run_migration_evidence_failed' }
    $taskProfileMigrated=$taskProfileMigratedResult.Output | ConvertFrom-Json
    Assert-DangoProfileOldGraph $taskProfileBefore $taskProfileMigrated -RequireInitialProfiles
    foreach ($taskProfileProperty in $taskProfileBefore.counts.PSObject.Properties) {
        if ($taskProfileMigrated.counts.($taskProfileProperty.Name) -ne $taskProfileProperty.Value) { throw 'profile_run_migration_graph_changed' }
    }
    if ((@($taskProfileMigrated.migrations) -join ',') -cne ((@($taskProfilePlan.PriorMigrations)+$taskProfilePlan.Migration) -join ',')) {
        throw 'profile_run_migration_history_rejected'
    }
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'migration-evidence.json') $taskProfileMigrated -CreateNew
    foreach ($taskProfilePath in $taskProfileHashes.Keys) {
        if ((Get-FileHash -LiteralPath $taskProfilePath -Algorithm SHA256).Hash -cne $taskProfileHashes[$taskProfilePath]) {
            throw 'profile_run_frozen_input_changed'
        }
    }
    [void](Assert-DangoProfileReviewInputs $taskProfilePlan $ApprovalManifest $ApprovalManifestSha256)
    $taskProfileTestEnv=@{ DANGO_BACKEND_TEST_CONNECTION=$taskProfileConnection; DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskProfilePlan.DataDirectory;
        DANGO_BACKEND_B1_TEST_APPROVED='1'; DANGO_BACKEND_PROFILE_TEST_APPROVED='1'; DANGO_BACKEND_B3_TEST_APPROVED='0';
        DANGO_BACKEND_B4_TEST_APPROVED='0'; DANGO_BACKEND_B5A_TEST_APPROVED='0'; DANGO_BACKEND_B5B_TEST_APPROVED='0';
        DANGO_BACKEND_B5C_TEST_APPROVED='0'; DANGO_BACKEND_B1_RECOVERY_EPOCH=$taskProfileFacts.Epoch.ToString('D');
        DANGO_BACKEND_B1_KEY_DIRECTORY=(Join-Path $taskProfilePlan.StateDirectory 'b1tests-keys');
        DANGO_BACKEND_PROFILE_OWNER_PID=[string]$taskProfileReservation.Owner.OwnerPid;
        DANGO_BACKEND_PROFILE_OWNER_ID=$taskProfileReservation.OwnerId.ToString('D');
        DANGO_PROFILE_VERIFICATION_RUN_DIRECTORY=$taskProfilePlan.EvidenceDirectory }
    $taskProfileTestsAttempted=$true
    $taskProfileTestResult=Invoke-DangoProfileProcess $taskProfileDotnet @('vstest',$taskProfileTestDll,
        ('--TestCaseFilter:'+$taskProfilePlan.Filter),'--Logger:trx;LogFileName=account-profile.trx',
        ('--ResultsDirectory:'+$taskProfilePlan.EvidenceDirectory)) $taskProfilePlan.Repository $taskProfileTestEnv `
        -TimeoutSeconds 180 -KillOwnedTree -Receipts $taskProfileTools
    if ($taskProfileTestResult.ExitCode -ne 0) { throw 'profile_run_tests_failed_no_retry' }
    Assert-DangoProfileTrx $taskProfileTrx
    $taskProfileAudit=Assert-DangoProfileAttempts $taskProfilePlan $taskProfileReservation.OwnerId `
        @(Read-DangoProfileLedger (Join-Path $taskProfilePlan.EvidenceDirectory 'attempts.jsonl') 67)
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'attempt-summary.json') $taskProfileAudit -CreateNew
    $taskProfileResources=Assert-DangoProfileResources $taskProfilePlan $taskProfileReservation.OwnerId `
        @(Read-DangoProfileLedger (Join-Path $taskProfilePlan.EvidenceDirectory 'resources.jsonl') 28)
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'fixture-resource-summary.json') $taskProfileResources -CreateNew
    $taskProfileAfterResult=Invoke-DangoProfileProcess $taskProfilePsql $taskProfilePsqlArgs $taskProfilePlan.Repository `
        @{PGPASSWORD=$taskProfilePassword} -Receipts $taskProfileTools
    if ($taskProfileAfterResult.ExitCode -ne 0) { throw 'profile_run_database_evidence_failed' }
    $taskProfileAfter=$taskProfileAfterResult.Output | ConvertFrom-Json
    Assert-DangoProfileOldGraph $taskProfileBefore $taskProfileAfter -RequireInitialProfiles
    foreach ($taskProfileCounter in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')) {
        if ($taskProfileAfter.counts.$taskProfileCounter-$taskProfileBefore.counts.$taskProfileCounter -ne 9) { throw 'profile_run_graph_delta_rejected' }
    }
    if ($taskProfileAfter.counts.sessions-$taskProfileBefore.counts.sessions -ne 10 -or
        (@($taskProfileAfter.migrations) -join ',') -cne ((@($taskProfilePlan.PriorMigrations)+$taskProfilePlan.Migration) -join ',') -or
        @($taskProfileBefore.userIds | Where-Object { $_ -notin $taskProfileAfter.userIds }).Count -ne 0) {
        throw 'profile_run_session_or_history_delta_rejected'
    }
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'after.json') $taskProfileAfter -CreateNew
    $taskProfileNewUsers=@($taskProfileAfter.userIds | Where-Object { $_ -notin $taskProfileBefore.userIds })
    $taskProfileScopedSql=Get-DangoProfileScopedSql $taskProfileNewUsers $taskProfileFacts.Epoch $taskProfileReservation.OwnerId
    $taskProfileScopedArgs=@($taskProfilePsqlArgs[0..($taskProfilePsqlArgs.Count-2)])+$taskProfileScopedSql
    $taskProfileScopedResult=Invoke-DangoProfileProcess $taskProfilePsql $taskProfileScopedArgs $taskProfilePlan.Repository `
        @{PGPASSWORD=$taskProfilePassword} -Receipts $taskProfileTools
    if ($taskProfileScopedResult.ExitCode -ne 0) { throw 'profile_run_scoped_evidence_failed' }
    $taskProfileScopedGraph=$taskProfileScopedResult.Output | ConvertFrom-Json
    Assert-DangoProfileScopedGraph $taskProfileScopedGraph
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'scoped-graph-evidence.json') $taskProfileScopedGraph -CreateNew
} catch {
    # Stable codes only; arbitrary exceptions may contain connection details.
    $taskProfileFailure=if ($_.Exception.Message -match '^profile_run_[a-z0-9_]+$') { $_.Exception.Message } else { 'profile_run_operation_failed' }
} finally {
    try {
        if ($null -eq $taskProfileProof -and $null -ne $taskProfileStartAttempt -and
            [IO.File]::Exists((Join-Path $taskProfilePlan.DataDirectory 'postmaster.pid'))) {
            $taskProfileProof=Get-DangoProfilePostgresProof $taskProfilePlan $taskProfileFacts.Port $taskProfileStartControlExit $taskProfileStartTool
        }
        if ($null -ne $taskProfileProof -and $null -ne $taskProfileStartAttempt -and $null -ne $taskProfileStartReturn) {
            $taskProfileCurrent=Get-DangoProfilePostgresProof $taskProfilePlan $taskProfileFacts.Port $taskProfileProof.ControlExitCode $taskProfileStartTool
            if ($taskProfileCurrent.Pid -eq $taskProfileProof.Pid -and $taskProfileCurrent.CreatedAt -eq $taskProfileProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $taskProfileCurrent $taskProfilePostgres $taskProfilePlan.DataDirectory `
                    $taskProfileFacts.Port $taskProfileStartAttempt $taskProfileStartReturn)) {
                $taskProfileStopped=Invoke-DangoProfileProcess $taskProfileControl @('stop','-D',$taskProfilePlan.DataDirectory,'-m','fast','-w','-t','30') `
                    $taskProfilePlan.Repository -TimeoutSeconds 40 -Receipts $taskProfileTools
                $taskProfileCleanup=$taskProfileStopped.ExitCode -eq 0 -and
                    -not [IO.File]::Exists((Join-Path $taskProfilePlan.DataDirectory 'postmaster.pid')) -and
                    -not (Test-DangoProfileListener $taskProfileFacts.Port) -and
                    $null -eq (Get-Process -Id $taskProfileProof.Pid -ErrorAction SilentlyContinue)
            }
        } elseif ($null -eq $taskProfileStartAttempt -or
            (-not [IO.File]::Exists((Join-Path $taskProfilePlan.DataDirectory 'postmaster.pid')) -and
             -not (Test-DangoProfileListener $taskProfileFacts.Port))) { $taskProfileCleanup=$true }
    } catch { $taskProfileCleanup=$false }
    if (@($taskProfileTools | Where-Object { -not $_.CleanupConfirmed }).Count -ne 0) { $taskProfileCleanup=$false }
    $taskProfilePassword=$null; $taskProfileConnection=$null; $taskProfileMigrationEnv=$null; $taskProfileTestEnv=$null
    Write-DangoProfileJson (Join-Path $taskProfilePlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1; OwnerId=$taskProfileReservation.OwnerId; Stage=$taskProfilePlan.Stage; Failure=$taskProfileFailure;
        PostgreSqlCleanupConfirmed=$taskProfileCleanup; PostgreSqlPid=if ($null -ne $taskProfileProof) { $taskProfileProof.Pid } else { 0 };
        RecordedPort=if ($null -ne $taskProfileFacts) { $taskProfileFacts.Port } else { 0 };
        PostmasterPidFilePresent=[IO.File]::Exists((Join-Path $taskProfilePlan.DataDirectory 'postmaster.pid'));
        ListenerPresentOnRecordedPort=if ($null -ne $taskProfileFacts) { Test-DangoProfileListener $taskProfileFacts.Port } else { $null };
        ToolProcesses=@($taskProfileTools); InputsFrozen=$taskProfileInputsFrozen;
        TestsPassed=($null -eq $taskProfileFailure -and $null -ne $taskProfileScopedGraph -and $null -ne $taskProfileAudit -and $null -ne $taskProfileResources);
        AttemptEvidence=$taskProfileAudit;
        FixturesAttempted=$taskProfileTestsAttempted; FixtureResourceEvidence=$taskProfileResources;
        FixtureNativeCleanupConfirmed=($null -ne $taskProfileResources -and $taskProfileResources.NativeCleanupConfirmed);
        WholeSuiteReserved=$true; RetryAllowed=$false; DataRetained=[IO.Directory]::Exists($taskProfilePlan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskProfilePlan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskProfilePlan.StateDirectory 'b1test-state.json'))) }) -CreateNew
}
if (-not $taskProfileCleanup) { throw 'profile_run_cleanup_unconfirmed_no_retry' }
if ($taskProfileFailure) { throw $taskProfileFailure }
Write-Output 'profile_run_seven_tests_passed_data_retained'

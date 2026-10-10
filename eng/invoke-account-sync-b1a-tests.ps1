[CmdletBinding()]
param(
    [string]$ApprovalManifest,
    [string]$ApprovalManifestSha256,
    [switch]$PrepareApprovalManifest,
    [switch]$ExecuteApprovedPlan
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -Force
$taskSyncPlan=Get-DangoAccountSyncPlan (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'accountsync_run_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskSyncReview=if ($PrepareApprovalManifest) { New-DangoAccountSyncApproval $taskSyncPlan $ApprovalManifest }
        else { Get-DangoAccountSyncInputs $taskSyncPlan }
    [pscustomobject]@{Plan=$taskSyncPlan;ReviewInputs=$taskSyncReview};return
}
if (-not $IsWindows) { throw 'accountsync_run_windows_required' }
$taskSyncApproved=Assert-DangoAccountSyncApproval $taskSyncPlan $ApprovalManifest $ApprovalManifestSha256

function Read-DangoAccountSyncBoundedState {
    param([string]$Path)
    Assert-DangoProfileRegularPath $Path
    $stream=[IO.FileStream]::new($Path,'Open','Read','Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 8192) { throw 'accountsync_run_state_bound_rejected' }
        $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
        if ($stream.ReadByte() -ne -1) { throw 'accountsync_run_state_bound_rejected' }
        return [Text.UTF8Encoding]::new($false,$true).GetString($bytes)|ConvertFrom-Json -Depth 16
    }finally{$stream.Dispose()}
}

function Invoke-DangoAccountSyncTool {
    param([string]$Step,[string]$Executable,[string[]]$Arguments,[hashtable]$EnvironmentValues=@{},
        [int]$TimeoutSeconds=30,[switch]$KillOwnedTree,[string]$OutputContract='CompleteEof')
    # One immutable sequence is reserved, with a single final stop permitted after failure.
    # This wrapper cannot dispatch a retry, migration, second suite or extra diagnostic.
    $expected=@('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped','stop')
    if ($taskSyncTools.Count -ge $taskSyncPlan.MaximumToolProcesses -or
        ($Step -cne 'stop' -and $Step -cne $expected[$taskSyncTools.Count]) -or
        ($Step -ceq 'stop' -and $taskSyncStopAttempted)) { throw 'accountsync_run_tool_budget_rejected' }
    if ($Step -ceq 'stop') { $script:taskSyncStopAttempted=$true }
    $options=@{Executable=$Executable;Arguments=$Arguments;WorkingDirectory=$taskSyncPlan.Repository;
        EnvironmentValues=$EnvironmentValues;TimeoutSeconds=$TimeoutSeconds;KillOwnedTree=$KillOwnedTree;
        Receipts=$taskSyncTools;OutputContract=$OutputContract}
    if ($OutputContract -ceq 'AccountSyncStartIgnored') {
        $options.PostgresStartPlan=$taskSyncPlan;$options.PostgresStartPort=$taskSyncFacts.Port
    }elseif ($OutputContract -ceq 'AccountSyncEvidenceJson') {
        $options.PostgresEvidencePlan=$taskSyncPlan;$options.PostgresEvidencePort=$taskSyncFacts.Port
    }
    return Invoke-DangoProfileProcess @options
}

function Invoke-DangoAccountSyncSnapshot {
    param([ValidateSet('before','after')][string]$Phase)
    $sizeArgs=@($taskSyncPsqlArgs[0..($taskSyncPsqlArgs.Count-2)])+(Get-DangoAccountSyncDatabaseSql -MeasureOnly)
    $sizeResult=Invoke-DangoAccountSyncTool ($Phase+'-size') $taskSyncPsql $sizeArgs $taskSyncPsqlEnvironment
    if ($sizeResult.ExitCode -ne 0) { throw 'accountsync_run_database_size_probe_failed' }
    $sizeEvidence=$sizeResult.Output|ConvertFrom-Json -Depth 16
    [void](Assert-DangoProfileDatabaseEvidenceSize $sizeEvidence)
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory ($Phase+'-size.json')) $sizeEvidence -CreateNew
    $result=Invoke-DangoAccountSyncTool ($Phase+'-full') $taskSyncPsql $taskSyncPsqlArgs $taskSyncPsqlEnvironment `
        -OutputContract AccountSyncEvidenceJson
    if ($result.ExitCode -ne 0 -or -not $result.Receipt.OutputComplete) { throw 'accountsync_run_database_snapshot_failed' }
    # The reader already enforces independent character and UTF-8 byte limits and full EOF.
    # Preserve the entire original snapshot before parsing or comparing any graph.
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($result.Output+"`n")
    if ($result.Output.Length -gt 1048574 -or $bytes.Length -gt 1048576) { throw 'accountsync_run_database_snapshot_bound_rejected' }
    $path=Join-Path $taskSyncPlan.EvidenceDirectory ($Phase+'.json');Assert-DangoProfileRegularPath $path
    $stream=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    $evidence=$result.Output|ConvertFrom-Json -Depth 32
    Assert-DangoAccountSyncTarget $taskSyncPlan $evidence
    return $evidence
}

function Assert-DangoAccountSyncFrozenInputs {
    [void](Assert-DangoAccountSyncApproval $taskSyncPlan $ApprovalManifest $ApprovalManifestSha256)
    foreach($path in $taskSyncExtraHashes.Keys) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $taskSyncExtraHashes[$path]) {
            throw 'accountsync_run_frozen_input_changed'
        }
    }
}

function Test-DangoAccountSyncProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    try {
        $process=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
        try {return $process.HasExited -or $process.StartTime.ToUniversalTime() -ne (ConvertTo-DangoProfileUtc $CreatedAt)}
        finally{$process.Dispose()}
    }catch [ArgumentException]{return $true}
    catch{return $false}
}

function Wait-DangoAccountSyncProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 3000) {
        if (Test-DangoAccountSyncProcessEnded $ProcessIdValue $CreatedAt) {return $true}
        [Threading.Thread]::Sleep(100)
    }
    return Test-DangoAccountSyncProcessEnded $ProcessIdValue $CreatedAt
}

function Read-DangoAccountSyncLedgerRows {
    param([string]$Path,[int]$Count)
    $lines=@(Read-DangoProfileLedger $Path $Count)
    if ($lines.Count -ne $Count) {throw 'accountsync_run_ledger_count_rejected'}
    foreach($line in $lines) {
        if ($line.Length -lt 1 -or $line.Length -gt 2048 -or [Text.Encoding]::UTF8.GetByteCount($line) -gt 2048) {
            throw 'accountsync_run_ledger_record_bound_rejected'
        }
        $row=$line|ConvertFrom-Json -Depth 12
        if ($null -eq $row -or $row -isnot [System.Management.Automation.PSCustomObject]) {throw 'accountsync_run_ledger_record_rejected'}
        $row
    }
}

function Assert-DangoAccountSyncJourneyEvidence {
    param($Evidence,[Guid]$OwnerId,[Guid]$UserId)
    $fields=@('Version','OwnerId','Passed','Final','ConfigurationWrites','LoginWrites','ProfileWrites','LogoutWrites',
        'DesktopHosts','HttpsHosts','HttpRequests','DurationSeconds','UsesSystemTrust','UsesWindowsCredentials',
        'ActualFormalHost','ActualApplicationHost','HealthyNativeFrames','ExactChoiceArchives','ExactSyncArchives')
    $finalFields=@('UserId','PetId','Nickname','ProfileVersion','ConfigurationVersion','Sessions','Revoked','Attempts','MailState','DefaultRoomId')
    if (-not (Test-DangoAccountSyncObjectShape $Evidence $fields) -or
        -not (Test-DangoAccountSyncObjectShape $Evidence.Final $finalFields)) {throw 'accountsync_run_journey_shape_rejected'}
    $integers=@{Version=1;ConfigurationWrites=5;LoginWrites=4;ProfileWrites=2;LogoutWrites=4;DesktopHosts=3;
        HttpsHosts=1;ExactChoiceArchives=3;ExactSyncArchives=3}
    foreach($field in $integers.Keys) {
        if (-not (Test-DangoAccountSyncInteger $Evidence.$field $integers[$field])) {throw 'accountsync_run_journey_evidence_rejected'}
    }
    $booleans=@{Passed=$true;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;
        ActualApplicationHost=$true;HealthyNativeFrames=$true}
    foreach($field in $booleans.Keys) {
        if (-not (Test-DangoAccountSyncBoolean $Evidence.$field $booleans[$field])) {throw 'accountsync_run_journey_evidence_rejected'}
    }
    $finalIntegers=@{ProfileVersion=3;ConfigurationVersion=6;Sessions=4;Revoked=4;Attempts=0}
    foreach($field in $finalIntegers.Keys) {
        if (-not (Test-DangoAccountSyncInteger $Evidence.Final.$field $finalIntegers[$field])) {throw 'accountsync_run_journey_evidence_rejected'}
    }
    $parsedOwner=[Guid]::Empty;$parsedUser=[Guid]::Empty;$pet=[Guid]::Empty;$room=[Guid]::Empty
    if ($OwnerId -eq [Guid]::Empty -or $UserId -eq [Guid]::Empty -or
        $Evidence.OwnerId -isnot [string] -or -not [Guid]::TryParseExact($Evidence.OwnerId,'D',[ref]$parsedOwner) -or $parsedOwner -ne $OwnerId -or
        $Evidence.Final.UserId -isnot [string] -or -not [Guid]::TryParseExact($Evidence.Final.UserId,'D',[ref]$parsedUser) -or $parsedUser -ne $UserId -or
        $Evidence.Final.PetId -isnot [string] -or -not [Guid]::TryParseExact($Evidence.Final.PetId,'D',[ref]$pet) -or $pet -eq [Guid]::Empty -or
        $Evidence.Final.DefaultRoomId -isnot [string] -or -not [Guid]::TryParseExact($Evidence.Final.DefaultRoomId,'D',[ref]$room) -or $room -eq [Guid]::Empty -or
        $Evidence.Final.Nickname -isnot [string] -or $Evidence.Final.Nickname -cne '新同步团子' -or
        $Evidence.Final.MailState -isnot [string] -or $Evidence.Final.MailState -cne 'Pending' -or
        -not (Test-DangoAccountSyncIntegerType $Evidence.HttpRequests) -or $Evidence.HttpRequests -lt 1 -or $Evidence.HttpRequests -gt 512) {
        throw 'accountsync_run_journey_evidence_rejected'
    }
    $duration=$Evidence.DurationSeconds
    if (($duration -isnot [double] -and $duration -isnot [decimal] -and $duration -isnot [int] -and $duration -isnot [long]) -or
        [double]::IsNaN([double]$duration) -or [double]::IsInfinity([double]$duration) -or $duration -lt 0 -or $duration -ge 600) {
        throw 'accountsync_run_journey_evidence_rejected'
    }
}

# CreateNew directory, reservation and ledgers precede all target state, credential and tool reads.
# Any subsequent failure consumes this stage; retained evidence cannot be reset by this runner.
$taskSyncReservation=New-DangoAccountSyncReservation $taskSyncPlan
Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'approved-inputs.json') $taskSyncApproved -CreateNew
$taskSyncTools=[Collections.Generic.List[object]]::new();$taskSyncExtraHashes=@{}
$taskSyncFacts=$null;$taskSyncProof=$null;$taskSyncStartupObservation=$null
$taskSyncStartAttempt=$null;$taskSyncStartReturn=$null;$taskSyncStartTool=$null;$taskSyncStartControlExit=-1
$taskSyncStopAttempted=$false;$taskSyncFailure=$null;$taskSyncCleanup=$false;$taskSyncOutputCleanup=$false
$taskSyncPassword=$null;$taskSyncConnection=$null;$taskSyncTestEnvironment=$null;$taskSyncPsqlEnvironment=$null
$taskSyncAudit=$null;$taskSyncResources=$null;$taskSyncScopedGraph=$null;$taskSyncJourney=$null
$taskSyncTestsAttempted=$false;$taskSyncInputsFrozen=$false;$taskSyncBefore=$null;$taskSyncAfter=$null
$taskSyncTrx=Join-Path $taskSyncPlan.EvidenceDirectory $taskSyncPlan.TrxFileName
try {
    foreach($name in @('state.json','credential.dpapi','b1test-state.json','b1tests-keys')) {
        $path=Join-Path $taskSyncPlan.StateDirectory $name;Assert-DangoProfileRegularPath $path
        if (-not (Test-Path -LiteralPath $path)) { throw 'accountsync_run_existing_state_required' }
    }
    Assert-DangoProfileRegularPath $taskSyncPlan.DataDirectory
    if (-not [IO.Directory]::Exists($taskSyncPlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskSyncPlan.DataDirectory 'postmaster.pid'))) { throw 'accountsync_run_existing_postmaster_rejected' }
    $taskSyncState=Read-DangoAccountSyncBoundedState (Join-Path $taskSyncPlan.StateDirectory 'state.json')
    $taskSyncEpoch=Read-DangoAccountSyncBoundedState (Join-Path $taskSyncPlan.StateDirectory 'b1test-state.json')
    $taskSyncFacts=Assert-DangoProfileState $taskSyncPlan $taskSyncState $taskSyncEpoch
    if ($taskSyncFacts.Port -ne 63237) { throw 'accountsync_run_fixed_port_rejected' }
    if (Test-DangoProfileListener $taskSyncFacts.Port) { throw 'accountsync_run_existing_listener_rejected' }
    $taskSyncPostgres=Join-Path $taskSyncPlan.PostgresBinDirectory 'postgres.exe'
    $taskSyncControl=Join-Path $taskSyncPlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskSyncPsql=Join-Path $taskSyncPlan.PostgresBinDirectory 'psql.exe'
    $taskSyncDotnet=(Get-Command dotnet -CommandType Application).Source
    $taskSyncTestDll=Join-Path $taskSyncPlan.Repository ($taskSyncPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    $taskSyncNative=Join-Path $taskSyncPlan.Repository $taskSyncPlan.NativeModule
    foreach($path in @($taskSyncPostgres,$taskSyncControl,$taskSyncPsql,$taskSyncDotnet,$taskSyncTestDll,$taskSyncNative,
        (Join-Path $taskSyncPlan.StateDirectory 'state.json'),(Join-Path $taskSyncPlan.StateDirectory 'b1test-state.json'))) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { throw 'accountsync_run_required_input_missing' }
        $taskSyncExtraHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    if ($taskSyncExtraHashes[$taskSyncNative] -cne $taskSyncPlan.NativeSha256) { throw 'accountsync_run_native_candidate_changed' }
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'input-hashes.json') $taskSyncExtraHashes -CreateNew
    Assert-DangoAccountSyncFrozenInputs;$taskSyncInputsFrozen=$true
    $version=Invoke-DangoAccountSyncTool 'version' $taskSyncPostgres @('--version')
    if ($version.ExitCode -ne 0 -or $version.Output -cnotmatch '^postgres \(PostgreSQL\) 18\.') { throw 'accountsync_run_postgres18_required' }
    $status=Invoke-DangoAccountSyncTool 'status' $taskSyncControl @('status','-D',$taskSyncPlan.DataDirectory)
    if ($status.ExitCode -ne 3) { throw 'accountsync_run_existing_postgres_rejected' }
    $credentialPath=Join-Path $taskSyncPlan.StateDirectory 'credential.dpapi'
    $credentialStream=[IO.FileStream]::new($credentialPath,'Open','Read','Read');$cipher=$null;$plain=$null;$decoded=$null
    try {
        if ($credentialStream.Length -le 0 -or $credentialStream.Length -gt 8192) { throw 'accountsync_run_credential_invalid' }
        $cipher=[byte[]]::new([int]$credentialStream.Length);$credentialStream.ReadExactly($cipher)
        if ($credentialStream.ReadByte() -ne -1) { throw 'accountsync_run_credential_invalid' }
        $plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskSyncPassword=[Text.UTF8Encoding]::new($false,$true).GetString($plain)
        if ($taskSyncPassword -cnotmatch '^[A-Za-z0-9+/]{43}=$') { throw 'accountsync_run_credential_invalid' }
        $decoded=[Convert]::FromBase64String($taskSyncPassword)
        if ($decoded.Length -ne 32 -or [Convert]::ToBase64String($decoded) -cne $taskSyncPassword) { throw 'accountsync_run_credential_invalid' }
    }finally{
        $credentialStream.Dispose()
        foreach($bytes in @($cipher,$plain,$decoded)) {if ($null -ne $bytes) {[Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)}}
    }
    Assert-DangoAccountSyncFrozenInputs
    $taskSyncStartAttempt=[DateTime]::UtcNow;$startIndex=$taskSyncTools.Count
    try {
        $started=Invoke-DangoAccountSyncTool 'start' $taskSyncControl @('start','-D',$taskSyncPlan.DataDirectory,'-l',
            (Join-Path $taskSyncPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskSyncFacts.Port)",'-w','-t','30') `
            -TimeoutSeconds 40 -OutputContract AccountSyncStartIgnored
        $taskSyncStartControlExit=$started.ExitCode
    }finally{
        $taskSyncStartReturn=[DateTime]::UtcNow
        if ($taskSyncTools.Count -gt $startIndex) {
            $taskSyncStartTool=$taskSyncTools[$startIndex]
            if ($taskSyncStartTool.ExitCodeRecorded) {$taskSyncStartControlExit=$taskSyncStartTool.ExitCode}
        }
    }
    $taskSyncProof=Get-DangoProfilePostgresProof $taskSyncPlan $taskSyncFacts.Port $taskSyncStartControlExit $taskSyncStartTool
    if (-not (Test-DangoProfilePostgresProof $taskSyncProof $taskSyncPostgres $taskSyncPlan.DataDirectory $taskSyncFacts.Port `
        $taskSyncStartAttempt $taskSyncStartReturn $taskSyncPlan)) { throw 'accountsync_run_start_ownership_unconfirmed' }
    if ($taskSyncStartControlExit -ne 0) { throw 'accountsync_run_postgres_start_failed' }
    $taskSyncPsqlArgs=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$taskSyncFacts.Port,
        '-U',$taskSyncPlan.Role,'-d',$taskSyncPlan.Database,'-c',(Get-DangoAccountSyncDatabaseSql))
    $taskSyncPsqlEnvironment=@{PGPASSWORD=$taskSyncPassword;PGCLIENTENCODING='UTF8'}
    $taskSyncBefore=Invoke-DangoAccountSyncSnapshot 'before'
    $taskSyncConnection="Host=127.0.0.1;Port=63237;Database=dango_backend_b0;Username=dango_b0_owner;Password=$taskSyncPassword;Include Error Detail=false"
    $taskSyncTestEnvironment=@{DANGO_BACKEND_ACCOUNTSYNC_B1A2_APPROVED='1';
        DANGO_BACKEND_ACCOUNTSYNC_B1A2_OWNER_ID=$taskSyncReservation.OwnerId.ToString('D');
        DANGO_BACKEND_ACCOUNTSYNC_B1A2_OWNER_PID=[string]$taskSyncReservation.Owner.OwnerPid;
        DANGO_BACKEND_ACCOUNTSYNC_B1A2_RUN_DIRECTORY=$taskSyncPlan.EvidenceDirectory;
        DANGO_BACKEND_ACCOUNTSYNC_B1A2_OWNER_MANIFEST_SHA256=$taskSyncReservation.ManifestSha256;
        DANGO_BACKEND_TEST_CONNECTION=$taskSyncConnection;DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskSyncPlan.DataDirectory;
        DANGO_BACKEND_B1_KEY_DIRECTORY=(Join-Path $taskSyncPlan.StateDirectory 'b1tests-keys');
        DANGO_BACKEND_B1_RECOVERY_EPOCH=$taskSyncFacts.Epoch.ToString('D')}
    Assert-DangoAccountSyncFrozenInputs;$taskSyncTestsAttempted=$true
    $test=Invoke-DangoAccountSyncTool 'vstest' $taskSyncDotnet @('vstest',$taskSyncTestDll,('--TestCaseFilter:'+$taskSyncPlan.Filter),
        ('--Logger:trx;LogFileName='+$taskSyncPlan.TrxFileName),('--ResultsDirectory:'+$taskSyncPlan.EvidenceDirectory)) $taskSyncTestEnvironment `
        -TimeoutSeconds 300 -KillOwnedTree
    if ($test.ExitCode -ne 0) { throw 'accountsync_run_tests_failed_no_retry' }
    Assert-DangoAccountSyncTrx $taskSyncTrx
    $attemptRows=@(Read-DangoAccountSyncLedgerRows (Join-Path $taskSyncPlan.EvidenceDirectory 'attempts.jsonl') 19)
    $taskSyncAudit=Assert-DangoAccountSyncAttempts $taskSyncPlan $taskSyncReservation.OwnerId $attemptRows
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'attempt-summary.json') $taskSyncAudit -CreateNew
    $resourceRows=@(Read-DangoAccountSyncLedgerRows (Join-Path $taskSyncPlan.EvidenceDirectory 'resources.jsonl') 10)
    $taskSyncResources=Assert-DangoAccountSyncResources $taskSyncReservation.OwnerId $resourceRows
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'fixture-resource-summary.json') $taskSyncResources -CreateNew
    $taskSyncAfter=Invoke-DangoAccountSyncSnapshot 'after'
    $newUser=Assert-DangoAccountSyncOldGraph $taskSyncBefore $taskSyncAfter
    $scopedSql=Get-DangoAccountSyncScopedSql $newUser $taskSyncFacts.Epoch $taskSyncReservation.OwnerId
    $scopedArgs=@($taskSyncPsqlArgs[0..($taskSyncPsqlArgs.Count-2)])+$scopedSql
    $scoped=Invoke-DangoAccountSyncTool 'scoped' $taskSyncPsql $scopedArgs $taskSyncPsqlEnvironment
    if ($scoped.ExitCode -ne 0) { throw 'accountsync_run_scoped_evidence_failed' }
    $taskSyncScopedGraph=$scoped.Output|ConvertFrom-Json -Depth 16
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'scoped-graph-evidence.json') $taskSyncScopedGraph -CreateNew
    Assert-DangoAccountSyncScopedGraph $taskSyncScopedGraph
    $journeyPath=Join-Path $taskSyncPlan.EvidenceDirectory 'journey-result.json'
    $taskSyncJourney=Read-DangoAccountSyncBoundedState $journeyPath
    Assert-DangoAccountSyncJourneyEvidence $taskSyncJourney $taskSyncReservation.OwnerId $newUser
    Assert-DangoAccountSyncFrozenInputs
}catch{
    # Never emit arbitrary exception text: it can include credentials or a connection string.
    $taskSyncFailure=if ($_.Exception.Message -cmatch '^(accountsync|profile_run)_[a-z0-9_]+$') {$_.Exception.Message}
        else {'accountsync_run_operation_failed'}
}finally{
    try {
        if ($null -ne $taskSyncStartAttempt -and $null -ne $taskSyncStartReturn) {
            $taskSyncStartupObservation=Observe-DangoProfileStartup $taskSyncPlan $taskSyncFacts.Port `
                $taskSyncStartControlExit $taskSyncStartTool $taskSyncStartAttempt
            if ($taskSyncStartupObservation.Verified) {$taskSyncProof=$taskSyncStartupObservation.Proof}
        }
        if ($null -ne $taskSyncProof -and $null -ne $taskSyncStartupObservation -and $taskSyncStartupObservation.Verified) {
            $current=Get-DangoProfilePostgresProof $taskSyncPlan $taskSyncFacts.Port $taskSyncProof.ControlExitCode $taskSyncStartTool
            if ($current.Pid -eq $taskSyncProof.Pid -and $current.CreatedAt -eq $taskSyncProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $current $taskSyncPostgres $taskSyncPlan.DataDirectory $taskSyncFacts.Port `
                    $taskSyncStartAttempt $taskSyncStartupObservation.ObservedUntil $taskSyncPlan)) {
                $stopped=Invoke-DangoAccountSyncTool 'stop' $taskSyncControl @('stop','-D',$taskSyncPlan.DataDirectory,'-m','fast','-w','-t','30') -TimeoutSeconds 40
                $taskSyncCleanup=$stopped.ExitCode -eq 0 -and
                    -not [IO.File]::Exists((Join-Path $taskSyncPlan.DataDirectory 'postmaster.pid')) -and
                    -not (Test-DangoProfileListener $taskSyncFacts.Port) -and
                    (Wait-DangoAccountSyncProcessEnded $taskSyncProof.Pid $taskSyncProof.CreatedAt) -and
                    (Wait-DangoAccountSyncProcessEnded $taskSyncProof.ShellBridge.Pid $taskSyncProof.ShellBridge.CreatedAt)
            }
        }elseif($null -eq $taskSyncStartAttempt){$taskSyncCleanup=$true}
    }catch{$taskSyncCleanup=$false}
    if (@($taskSyncTools|Where-Object {-not $_.CleanupConfirmed -or -not $_.ProcessDisposeConfirmed}).Count -ne 0) {$taskSyncCleanup=$false}
    $taskSyncOutputCleanup=@($taskSyncTools|Where-Object {-not $_.OutputResourcesCleanupConfirmed}).Count -eq 0
    if ($null -eq $taskSyncFailure -and $taskSyncTools.Count -ne 10) {$taskSyncFailure='accountsync_run_tool_sequence_incomplete'}
    $taskSyncPassword=$null;$taskSyncConnection=$null;$taskSyncTestEnvironment=$null;$taskSyncPsqlEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskSyncPlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskSyncReservation.OwnerId;Stage=$taskSyncPlan.Stage;Failure=$taskSyncFailure;
        PostgreSqlCleanupConfirmed=$taskSyncCleanup;PostgreSqlPid=if($null -ne $taskSyncProof){$taskSyncProof.Pid}else{0};
        ShellBridgeCleanupConfirmed=($null -ne $taskSyncProof -and (Test-DangoAccountSyncProcessEnded $taskSyncProof.ShellBridge.Pid $taskSyncProof.ShellBridge.CreatedAt));
        ToolOutputResourcesCleanupConfirmed=$taskSyncOutputCleanup;RecordedPort=if($null -ne $taskSyncFacts){$taskSyncFacts.Port}else{0};
        PostmasterPidFilePresent=[IO.File]::Exists((Join-Path $taskSyncPlan.DataDirectory 'postmaster.pid'));
        ListenerPresentOnRecordedPort=if($null -ne $taskSyncFacts){Test-DangoProfileListener $taskSyncFacts.Port}else{$null};
        ToolProcesses=@($taskSyncTools);InputsFrozen=$taskSyncInputsFrozen;
        TestsPassed=($null -eq $taskSyncFailure -and $null -ne $taskSyncAudit -and $null -ne $taskSyncResources -and
            $null -ne $taskSyncScopedGraph -and $null -ne $taskSyncJourney);
        AttemptEvidence=$taskSyncAudit;FixturesAttempted=$taskSyncTestsAttempted;FixtureResourceEvidence=$taskSyncResources;
        FixtureNativeCleanupConfirmed=($null -ne $taskSyncResources -and $taskSyncResources.NativeCleanupConfirmed);
        FixtureUiaWorkersCleanupConfirmed=($null -ne $taskSyncResources -and $taskSyncResources.UiaWorkersCleanupConfirmed);
        StartupObservation=$taskSyncStartupObservation;MaximumHttpRequests=512;MaximumDurationMinutes=10;TestProcessTimeoutSeconds=300;
        WholeSuiteReserved=$true;RetryAllowed=$false;DataRetained=[IO.Directory]::Exists($taskSyncPlan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskSyncPlan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskSyncPlan.StateDirectory 'b1test-state.json')))}) -CreateNew
}
if (-not $taskSyncCleanup) {throw 'accountsync_run_cleanup_unconfirmed_no_retry'}
if (-not $taskSyncOutputCleanup) {throw 'accountsync_run_output_cleanup_unconfirmed_no_retry'}
if ($taskSyncFailure) {throw $taskSyncFailure}
Write-Output 'accountsync_run_one_journey_passed_data_retained'

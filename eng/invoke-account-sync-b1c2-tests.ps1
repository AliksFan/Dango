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
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncB1c2Postgres.psm1') -Force
$taskB1cPlan=Get-DangoAccountSyncB1c2Plan (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'accountsync_run_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskB1cReview=if ($PrepareApprovalManifest) { New-DangoAccountSyncB1c2Approval $taskB1cPlan $ApprovalManifest }
        else { Get-DangoAccountSyncB1c2Inputs $taskB1cPlan }
    [pscustomobject]@{Plan=$taskB1cPlan;ReviewInputs=$taskB1cReview};return
}
if (-not $IsWindows) { throw 'accountsync_run_windows_required' }
$taskB1cApproved=Assert-DangoAccountSyncB1c2Approval $taskB1cPlan $ApprovalManifest $ApprovalManifestSha256

function Read-DangoAccountSyncB1c2BoundedState {
    param([string]$Path)
    Assert-DangoProfileRegularPath $Path
    $stream=[IO.FileStream]::new($Path,'Open','Read','Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 8192) { throw 'accountsync_run_state_bound_rejected' }
        $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
        if ($stream.ReadByte() -ne -1) { throw 'accountsync_run_state_bound_rejected' }
        return ConvertFrom-DangoAccountSyncB1c2StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
    }finally{$stream.Dispose()}
}

function Invoke-DangoAccountSyncB1c2Tool {
    param([string]$Step,[string]$Executable,[string[]]$Arguments,[hashtable]$EnvironmentValues=@{},
        [int]$TimeoutSeconds=30,[switch]$KillOwnedTree,[string]$OutputContract='CompleteEof')
    # One immutable sequence is reserved, with a single final stop permitted after failure.
    # This wrapper cannot dispatch a retry, migration, second suite or extra diagnostic.
    $expected=@('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped','stop')
    $isStop=Test-DangoAccountSyncB1c2String $Step 'stop'
    if ($taskB1cToolAttempts -ge $taskB1cPlan.MaximumToolProcesses -or $taskB1cStopAttempted -or
        (-not $isStop -and ($taskB1cToolIndex -ge 9 -or -not(Test-DangoAccountSyncB1c2String $Step $expected[$taskB1cToolIndex]))) -or
        ($isStop -and ($taskB1cToolIndex -lt 3 -or -not $taskB1cStopOwnershipVerified))) { throw 'accountsync_b1c2_tool_budget_rejected' }
    Assert-DangoAccountSyncB1c2PathText $Executable
    $toolExecutable=$taskB1cControl;$toolArguments=@();$toolEnvironment=@{};$toolTimeout=30;$toolTree=$false;$toolContract='CompleteEof'
    switch -CaseSensitive ($Step) {
        version {$toolExecutable=$taskB1c2Postgres;$toolArguments=@('--version')}
        status {$toolArguments=@('status','-D',$taskB1cPlan.DataDirectory)}
        start {$toolTimeout=40;$toolContract='PostgresStartIgnored';$toolArguments=@('start','-D',$taskB1cPlan.DataDirectory,'-l',
            (Join-Path $taskB1cPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p 63237",'-w','-t','30')}
        {Test-DangoAccountSyncB1c2StringIn $_ @('before-size','after-size')} {$toolExecutable=$taskB1cPsql;$toolContract='AccountSyncB1c2SmallJson';$toolEnvironment=$taskB1cPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)}
        {Test-DangoAccountSyncB1c2StringIn $_ @('before-full','after-full')} {$toolExecutable=$taskB1cPsql;$toolContract='AccountSyncB1c2EvidenceJson';$toolEnvironment=$taskB1cPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncDatabaseSql)}
        vstest {$toolExecutable=$taskB1cDotnet;$toolTimeout=300;$toolTree=$true;$toolEnvironment=$taskB1cTestEnvironment;
            $toolArguments=@('vstest',$taskB1cTestDll,('--TestCaseFilter:'+$taskB1cPlan.Filter),('--Logger:trx;LogFileName='+$taskB1cPlan.TrxFileName),
                ('--ResultsDirectory:'+$taskB1cPlan.EvidenceDirectory))}
        scoped {$toolExecutable=$taskB1cPsql;$toolContract='AccountSyncB1c2SmallJson';$toolEnvironment=$taskB1cPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncB1c2ScopedSql $taskB1cScope)}
        stop {$toolTimeout=40;$toolArguments=@('stop','-D',$taskB1cPlan.DataDirectory,'-m','fast','-w','-t','30')}
    }
    if(-not(Test-DangoProfilePath $Executable $toolExecutable) -or -not(Test-DangoAccountSyncB1c2FixedValue $Arguments ([string[]]$toolArguments)) -or
        -not(Test-DangoAccountSyncB1c2FixedValue $EnvironmentValues $toolEnvironment) -or $TimeoutSeconds -ne $toolTimeout -or
        ([bool]$KillOwnedTree) -ne $toolTree -or -not(Test-DangoAccountSyncB1c2String $OutputContract $toolContract)){throw 'accountsync_b1c2_tool_contract_rejected'}
    if(-not $isStop){Assert-DangoAccountSyncB1c2FrozenInputs}
    $script:taskB1cToolAttempts++;$script:taskB1cToolIndex++
    if ($isStop) { $script:taskB1cStopAttempted=$true }
    $options=@{Executable=$Executable;Arguments=$Arguments;WorkingDirectory=$taskB1cPlan.Repository;
        EnvironmentValues=$EnvironmentValues;TimeoutSeconds=$TimeoutSeconds;KillOwnedTree=$KillOwnedTree;
        Receipts=$taskB1cTools;OutputContract=$OutputContract}
    if (Test-DangoAccountSyncB1c2String $OutputContract 'PostgresStartIgnored') {
        $options.PostgresStartPlan=$taskB1cPlan;$options.PostgresStartPort=$taskB1cFacts.Port
    }elseif (Test-DangoAccountSyncB1c2StringIn $OutputContract @('AccountSyncB1c2EvidenceJson','AccountSyncB1c2SmallJson')) {
        $options.PostgresEvidencePlan=$taskB1cPlan;$options.PostgresEvidencePort=$taskB1cFacts.Port
    }
    return Invoke-DangoProfileProcess @options
}

function Invoke-DangoAccountSyncB1c2Snapshot {
    param([ValidateSet('before','after')][string]$Phase)
    $sizeArgs=Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)
    $sizeResult=Invoke-DangoAccountSyncB1c2Tool ($Phase+'-size') $taskB1cPsql $sizeArgs $taskB1cPsqlEnvironment -OutputContract AccountSyncB1c2SmallJson
    if(-not $sizeResult.Receipt.OutputComplete){throw 'accountsync_b1c2_size_eof_unconfirmed'}
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan ($Phase+'-size.json') $sizeResult.RawOutput
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan ($Phase+'-size.stderr.txt') $sizeResult.RawError
    if ($sizeResult.ExitCode -ne 0) { throw 'accountsync_b1c2_database_size_probe_failed' }
    $sizeEvidence=ConvertFrom-DangoAccountSyncB1c2StrictJson $sizeResult.RawOutput
    [void](Assert-DangoProfileDatabaseEvidenceSize $sizeEvidence)
    $result=Invoke-DangoAccountSyncB1c2Tool ($Phase+'-full') $taskB1cPsql $taskB1cPsqlArgs $taskB1cPsqlEnvironment `
        -OutputContract AccountSyncB1c2EvidenceJson
    if (-not $result.Receipt.OutputComplete) { throw 'accountsync_b1c2_database_snapshot_eof_unconfirmed' }
    # The reader already enforces independent character and UTF-8 byte limits and full EOF.
    # Preserve the entire original snapshot before parsing or comparing any graph.
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan ($Phase+'.json') $result.RawOutput -MaximumBytes 1048576
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan ($Phase+'.stderr.txt') $result.RawError
    if($result.ExitCode -ne 0){throw 'accountsync_b1c2_database_snapshot_failed'}
    $evidence=ConvertFrom-DangoAccountSyncB1c2StrictJson $result.RawOutput -MaximumBytes 1048576
    Assert-DangoAccountSyncB1c2Target $taskB1cPlan $evidence
    return $evidence
}

function Assert-DangoAccountSyncB1c2FrozenInputs {
    [void](Assert-DangoAccountSyncB1c2Approval $taskB1cPlan $ApprovalManifest $ApprovalManifestSha256)
    foreach($path in $taskB1cExtraHashes.Keys) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path) -or -not(Test-DangoAccountSyncB1c2String ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) $taskB1cExtraHashes[$path])) {
            throw 'accountsync_run_frozen_input_changed'
        }
    }
}

function Test-DangoAccountSyncB1c2ProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    try {
        $process=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
        try {return $process.HasExited -or $process.StartTime.ToUniversalTime() -ne (ConvertTo-DangoProfileUtc $CreatedAt)}
        finally{$process.Dispose()}
    }catch [ArgumentException]{return $true}
    catch{return $false}
}

function Wait-DangoAccountSyncB1c2ProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 3000) {
        if (Test-DangoAccountSyncB1c2ProcessEnded $ProcessIdValue $CreatedAt) {return $true}
        [Threading.Thread]::Sleep(100)
    }
    return Test-DangoAccountSyncB1c2ProcessEnded $ProcessIdValue $CreatedAt
}

function Read-DangoAccountSyncB1c2LedgerRows {
    param([string]$Path,[int]$Count)
    $lines=@(Read-DangoProfileLedger $Path $Count)
    if ($lines.Count -ne $Count) {throw 'accountsync_run_ledger_count_rejected'}
    foreach($line in $lines) {
        if ($line.Length -lt 1 -or $line.Length -gt 2048 -or [Text.Encoding]::UTF8.GetByteCount($line) -gt 2048) {
            throw 'accountsync_run_ledger_record_bound_rejected'
        }
        $row=ConvertFrom-DangoAccountSyncB1c2StrictJson $line
        if ($null -eq $row -or $row -isnot [System.Management.Automation.PSCustomObject]) {throw 'accountsync_run_ledger_record_rejected'}
        $row
    }
}



# CreateNew directory, reservation and ledgers precede all original state and credential reads.
# Any subsequent failure consumes this stage; retained evidence cannot be reset by this runner.
$taskB1cReservation=New-DangoAccountSyncB1c2Reservation $taskB1cPlan
Write-DangoProfileJson (Join-Path $taskB1cPlan.EvidenceDirectory 'approved-inputs.json') $taskB1cApproved -CreateNew
$taskB1cTools=[Collections.Generic.List[object]]::new();$taskB1cExtraHashes=@{}
$taskB1cToolAttempts=0;$taskB1cToolIndex=0;$taskB1cStopOwnershipVerified=$false;$taskB1cNewUsers=@();$taskB1cScope=$null
$taskB1c2PostgresProcessCleanup=$false;$taskB1cShellBridgeCleanup=$false;$taskB1cToolCleanup=$false
$taskB1cPidFileAbsent=$false;$taskB1cListenerAbsent=$false
$taskB1cFacts=$null;$taskB1cProof=$null;$taskB1cStartupObservation=$null
$taskB1cStartAttempt=$null;$taskB1cStartReturn=$null;$taskB1cStartTool=$null;$taskB1cStartControlExit=-1
$taskB1cStopAttempted=$false;$taskB1cFailure=$null;$taskB1cCleanup=$false;$taskB1cOutputCleanup=$false
$taskB1cPassword=$null;$taskB1cConnection=$null;$taskB1cTestEnvironment=$null;$taskB1cPsqlEnvironment=$null
$taskB1cAudit=$null;$taskB1cResources=$null;$taskB1cScopedGraph=$null;$taskB1cJourney=$null
$taskB1cArchivesValidated=$false;$taskB1cFaultsValidated=$false;$taskB1cScopeValidated=$false;$taskB1cIntentsValidated=$false
$taskB1cTestsAttempted=$false;$taskB1cInputsFrozen=$false;$taskB1cBefore=$null;$taskB1cAfter=$null
$taskB1cTrx=Join-Path $taskB1cPlan.EvidenceDirectory $taskB1cPlan.TrxFileName
try {
    Assert-DangoAccountSyncClosurePrivatePath $taskB1cPlan.StateDirectory $true
    foreach($name in @('state.json','credential.dpapi','b1test-state.json','b1tests-keys')) {
        $path=Join-Path $taskB1cPlan.StateDirectory $name;Assert-DangoProfileRegularPath $path
        if (-not (Test-Path -LiteralPath $path)) { throw 'accountsync_run_existing_state_required' }
    }
    foreach($name in @('state.json','credential.dpapi','b1test-state.json')){Assert-DangoAccountSyncClosurePrivatePath (Join-Path $taskB1cPlan.StateDirectory $name)}
    Assert-DangoProfileRegularPath $taskB1cPlan.DataDirectory
    if (-not [IO.Directory]::Exists($taskB1cPlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskB1cPlan.DataDirectory 'postmaster.pid'))) { throw 'accountsync_run_existing_postmaster_rejected' }
    $taskB1cState=Read-DangoAccountSyncB1c2BoundedState (Join-Path $taskB1cPlan.StateDirectory 'state.json')
    $taskB1cEpoch=Read-DangoAccountSyncB1c2BoundedState (Join-Path $taskB1cPlan.StateDirectory 'b1test-state.json')
    $taskB1cFacts=Assert-DangoAccountSyncB1c2State $taskB1cPlan $taskB1cState $taskB1cEpoch
    if ($taskB1cFacts.Port -ne 63237) { throw 'accountsync_run_fixed_port_rejected' }
    if (Test-DangoProfileListener $taskB1cFacts.Port) { throw 'accountsync_run_existing_listener_rejected' }
    $taskB1c2Postgres=Join-Path $taskB1cPlan.PostgresBinDirectory 'postgres.exe'
    $taskB1cControl=Join-Path $taskB1cPlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskB1cPsql=Join-Path $taskB1cPlan.PostgresBinDirectory 'psql.exe'
    $taskB1cDotnet=(Get-Command dotnet -CommandType Application).Source
    $taskB1cTestDll=Join-Path $taskB1cPlan.Repository ($taskB1cPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    foreach($path in @($taskB1c2Postgres,$taskB1cControl,$taskB1cPsql,$taskB1cDotnet,$taskB1cTestDll,
        (Join-Path $taskB1cPlan.StateDirectory 'state.json'),(Join-Path $taskB1cPlan.StateDirectory 'b1test-state.json'))) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { throw 'accountsync_run_required_input_missing' }
        $taskB1cExtraHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    Write-DangoProfileJson (Join-Path $taskB1cPlan.EvidenceDirectory 'input-hashes.json') $taskB1cExtraHashes -CreateNew
    Assert-DangoAccountSyncB1c2FrozenInputs;$taskB1cInputsFrozen=$true
    $version=Invoke-DangoAccountSyncB1c2Tool 'version' $taskB1c2Postgres @('--version')
    if ($version.ExitCode -ne 0 -or $version.Output -cnotmatch '^postgres \(PostgreSQL\) 18\.') { throw 'accountsync_run_postgres18_required' }
    $status=Invoke-DangoAccountSyncB1c2Tool 'status' $taskB1cControl @('status','-D',$taskB1cPlan.DataDirectory)
    if ($status.ExitCode -ne 3) { throw 'accountsync_run_existing_postgres_rejected' }
    $credentialPath=Join-Path $taskB1cPlan.StateDirectory 'credential.dpapi'
    $credentialStream=[IO.FileStream]::new($credentialPath,'Open','Read','Read');$cipher=$null;$plain=$null;$decoded=$null
    try {
        if ($credentialStream.Length -le 0 -or $credentialStream.Length -gt 8192) { throw 'accountsync_run_credential_invalid' }
        $cipher=[byte[]]::new([int]$credentialStream.Length);$credentialStream.ReadExactly($cipher)
        if ($credentialStream.ReadByte() -ne -1) { throw 'accountsync_run_credential_invalid' }
        $plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskB1cPassword=[Text.UTF8Encoding]::new($false,$true).GetString($plain)
        if ($taskB1cPassword -cnotmatch '\A[A-Za-z0-9+/]{43}=\z') { throw 'accountsync_run_credential_invalid' }
        $decoded=[Convert]::FromBase64String($taskB1cPassword)
        if ($decoded.Length -ne 32 -or -not(Test-DangoAccountSyncB1c2String ([Convert]::ToBase64String($decoded)) $taskB1cPassword)) { throw 'accountsync_run_credential_invalid' }
    }finally{
        $credentialStream.Dispose()
        foreach($bytes in @($cipher,$plain,$decoded)) {if ($null -ne $bytes) {[Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)}}
    }
    Assert-DangoAccountSyncB1c2FrozenInputs
    $taskB1cStartAttempt=[DateTime]::UtcNow;$startIndex=$taskB1cTools.Count
    try {
        $started=Invoke-DangoAccountSyncB1c2Tool 'start' $taskB1cControl @('start','-D',$taskB1cPlan.DataDirectory,'-l',
            (Join-Path $taskB1cPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskB1cFacts.Port)",'-w','-t','30') `
            -TimeoutSeconds 40 -OutputContract PostgresStartIgnored
        $taskB1cStartControlExit=$started.ExitCode
    }finally{
        $taskB1cStartReturn=[DateTime]::UtcNow
        if ($taskB1cTools.Count -gt $startIndex) {
            $taskB1cStartTool=$taskB1cTools[$startIndex]
            if ($taskB1cStartTool.ExitCodeRecorded) {$taskB1cStartControlExit=$taskB1cStartTool.ExitCode}
        }
    }
    $taskB1cProof=Get-DangoProfilePostgresProof $taskB1cPlan $taskB1cFacts.Port $taskB1cStartControlExit $taskB1cStartTool
    if (-not (Test-DangoProfilePostgresProof $taskB1cProof $taskB1c2Postgres $taskB1cPlan.DataDirectory $taskB1cFacts.Port `
        $taskB1cStartAttempt $taskB1cStartReturn $taskB1cPlan)) { throw 'accountsync_run_start_ownership_unconfirmed' }
    if ($taskB1cStartControlExit -ne 0) { throw 'accountsync_run_postgres_start_failed' }
    $taskB1cPsqlArgs=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$taskB1cFacts.Port,
        '-U',$taskB1cPlan.Role,'-d',$taskB1cPlan.Database,'-c',(Get-DangoAccountSyncDatabaseSql))
    $taskB1cPsqlEnvironment=@{PGPASSWORD=$taskB1cPassword;PGCLIENTENCODING='UTF8'}
    $taskB1cBefore=Invoke-DangoAccountSyncB1c2Snapshot 'before'
    $taskB1cConnection="Host=127.0.0.1;Port=63237;Database=dango_backend_b0;Username=dango_b0_owner;Password=$taskB1cPassword;Include Error Detail=false"
    $taskB1cTestEnvironment=@{DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED='1';
        DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_ID=$taskB1cReservation.OwnerId.ToString('D');
        DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_PID=[string]$taskB1cReservation.Owner.OwnerPid;
        DANGO_BACKEND_ACCOUNTSYNC_B1C2_RUN_DIRECTORY=$taskB1cPlan.EvidenceDirectory;
        DANGO_BACKEND_ACCOUNTSYNC_B1C2_OWNER_MANIFEST_SHA256=$taskB1cReservation.ManifestSha256;
        DANGO_BACKEND_TEST_CONNECTION=$taskB1cConnection;DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskB1cPlan.DataDirectory;
        DANGO_BACKEND_B1_KEY_DIRECTORY=(Join-Path $taskB1cPlan.StateDirectory 'b1tests-keys');
        DANGO_BACKEND_B1_RECOVERY_EPOCH=$taskB1cFacts.Epoch.ToString('D')}
    Assert-DangoAccountSyncB1c2FrozenInputs;$taskB1cTestsAttempted=$true
    $test=Invoke-DangoAccountSyncB1c2Tool 'vstest' $taskB1cDotnet @('vstest',$taskB1cTestDll,('--TestCaseFilter:'+$taskB1cPlan.Filter),
        ('--Logger:trx;LogFileName='+$taskB1cPlan.TrxFileName),('--ResultsDirectory:'+$taskB1cPlan.EvidenceDirectory)) $taskB1cTestEnvironment `
        -TimeoutSeconds 300 -KillOwnedTree
    if ($test.ExitCode -ne 0) { throw 'accountsync_run_tests_failed_no_retry' }
    Assert-DangoAccountSyncB1c2Trx $taskB1cTrx
    $attemptRows=@(Read-DangoAccountSyncB1c2LedgerRows (Join-Path $taskB1cPlan.EvidenceDirectory 'attempts.jsonl') 21)
    $taskB1cAudit=Assert-DangoAccountSyncB1c2Attempts $taskB1cPlan $taskB1cReservation.OwnerId $attemptRows
    Write-DangoProfileJson (Join-Path $taskB1cPlan.EvidenceDirectory 'attempt-summary.json') $taskB1cAudit -CreateNew
    $resourceRows=@(Read-DangoAccountSyncB1c2LedgerRows (Join-Path $taskB1cPlan.EvidenceDirectory 'resources.jsonl') 8)
    $taskB1cResources=Assert-DangoAccountSyncB1c2Resources $taskB1cReservation.OwnerId $resourceRows
    Write-DangoProfileJson (Join-Path $taskB1cPlan.EvidenceDirectory 'fixture-resource-summary.json') $taskB1cResources -CreateNew
    $taskB1cAfter=Invoke-DangoAccountSyncB1c2Snapshot 'after'
    $taskB1cNewUsers=@(Assert-DangoAccountSyncB1c2OldGraph $taskB1cBefore $taskB1cAfter)
    $scopeBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskB1cPlan.EvidenceDirectory 'scoped-inputs-evidence.json') 131072
    $taskB1cScope=ConvertFrom-DangoAccountSyncB1c2StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($scopeBytes))
    Assert-DangoAccountSyncB1c2ScopeInputs $taskB1cScope $taskB1cReservation.OwnerId $taskB1cFacts.Epoch $taskB1cNewUsers
    $taskB1cScopeValidated=$true
    $journeyPath=Join-Path $taskB1cPlan.EvidenceDirectory 'journey-evidence.json'
    $journeyBytes=Read-DangoAccountSyncClosureBytes $journeyPath 131072
    $taskB1cJourney=ConvertFrom-DangoAccountSyncB1c2StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($journeyBytes))
    Assert-DangoAccountSyncB1c2Journey $taskB1cJourney $taskB1cScope
    $archiveBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskB1cPlan.EvidenceDirectory 'configuration-sync-evidence.json') 131072
    $archiveEvidence=ConvertFrom-DangoAccountSyncB1c2StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($archiveBytes))
    Assert-DangoAccountSyncB1c2Archives $archiveEvidence $taskB1cScope $resourceRows[0].Facts.Port;$taskB1cArchivesValidated=$true
    $faultBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskB1cPlan.EvidenceDirectory 'fault-evidence.json') 1048576
    $faultEvidence=ConvertFrom-DangoAccountSyncB1c2StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($faultBytes)) -MaximumBytes 1048576
    $intentRows=@(Read-DangoAccountSyncB1c2LedgerRows (Join-Path $taskB1cPlan.EvidenceDirectory 'logout-intents.jsonl') 2)
    Assert-DangoAccountSyncB1c2LogoutIntents $intentRows $taskB1cScope;$taskB1cIntentsValidated=$true
    Assert-DangoAccountSyncB1c2FaultEvidence $faultEvidence $taskB1cScope $intentRows $taskB1cJourney
    Assert-DangoAccountSyncB1c2AuxAgreement $archiveEvidence $faultEvidence;$taskB1cFaultsValidated=$true
    $scopedSql=Get-DangoAccountSyncB1c2ScopedSql $taskB1cScope
    $scopedArgs=@($taskB1cPsqlArgs[0..($taskB1cPsqlArgs.Count-2)])+$scopedSql
    $scoped=Invoke-DangoAccountSyncB1c2Tool 'scoped' $taskB1cPsql $scopedArgs $taskB1cPsqlEnvironment -OutputContract AccountSyncB1c2SmallJson
    if(-not $scoped.Receipt.OutputComplete){throw 'accountsync_b1c2_scoped_eof_unconfirmed'}
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan 'scoped-graph-evidence.json' $scoped.RawOutput
    Save-DangoAccountSyncB1c2RawEvidence $taskB1cPlan 'scoped.stderr.txt' $scoped.RawError
    if ($scoped.ExitCode -ne 0) { throw 'accountsync_run_scoped_evidence_failed' }
    $taskB1cScopedGraph=ConvertFrom-DangoAccountSyncB1c2StrictJson $scoped.RawOutput
    Assert-DangoAccountSyncB1c2ScopedGraph $taskB1cScopedGraph
    Assert-DangoAccountSyncB1c2FrozenInputs
}catch{
    # Never emit arbitrary exception text: it can include credentials or a connection string.
    $taskB1cFailure=if ($_.Exception.Message -cmatch '^(accountsync|profile_run)_[a-z0-9_]+$') {$_.Exception.Message}
        else {'accountsync_run_operation_failed'}
}finally{
    try {
        if ($null -ne $taskB1cStartAttempt -and $null -ne $taskB1cStartReturn) {
            $taskB1cStartupObservation=Observe-DangoProfileStartup $taskB1cPlan $taskB1cFacts.Port `
                $taskB1cStartControlExit $taskB1cStartTool $taskB1cStartAttempt
            if ($taskB1cStartupObservation.Verified) {$taskB1cProof=$taskB1cStartupObservation.Proof}
        }
        if ($null -ne $taskB1cProof -and $null -ne $taskB1cStartupObservation -and $taskB1cStartupObservation.Verified) {
            $current=Get-DangoProfilePostgresProof $taskB1cPlan $taskB1cFacts.Port $taskB1cProof.ControlExitCode $taskB1cStartTool
            if ($current.Pid -eq $taskB1cProof.Pid -and $current.CreatedAt -eq $taskB1cProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $current $taskB1c2Postgres $taskB1cPlan.DataDirectory $taskB1cFacts.Port `
                    $taskB1cStartAttempt $taskB1cStartupObservation.ObservedUntil $taskB1cPlan)) {
                $taskB1cStopOwnershipVerified=$true
                $stopped=Invoke-DangoAccountSyncB1c2Tool 'stop' $taskB1cControl @('stop','-D',$taskB1cPlan.DataDirectory,'-m','fast','-w','-t','30') -TimeoutSeconds 40
                if($stopped.ExitCode -ne 0 -and $null -eq $taskB1cFailure){$taskB1cFailure='accountsync_b1c2_stop_failed'}
                $taskB1c2PostgresProcessCleanup=Wait-DangoAccountSyncB1c2ProcessEnded $taskB1cProof.Pid $taskB1cProof.CreatedAt
                $taskB1cShellBridgeCleanup=Wait-DangoAccountSyncB1c2ProcessEnded $taskB1cProof.ShellBridge.Pid $taskB1cProof.ShellBridge.CreatedAt
            }
        }elseif($null -eq $taskB1cStartAttempt){$taskB1c2PostgresProcessCleanup=$true;$taskB1cShellBridgeCleanup=$true}
    }catch{$taskB1cCleanup=$false}
    $taskB1cToolCleanup=@($taskB1cTools|Where-Object {-not $_.CleanupConfirmed -or -not $_.ProcessDisposeConfirmed}).Count -eq 0
    $taskB1cOutputCleanup=@($taskB1cTools|Where-Object {-not $_.OutputResourcesCleanupConfirmed}).Count -eq 0
    $taskB1cPidFileAbsent=-not [IO.File]::Exists((Join-Path $taskB1cPlan.DataDirectory 'postmaster.pid'))
    $taskB1cListenerAbsent=-not(Test-DangoProfileListener $taskB1cPlan.DatabasePort)
    $taskB1cCleanup=$taskB1c2PostgresProcessCleanup -and $taskB1cShellBridgeCleanup -and $taskB1cToolCleanup -and
        $taskB1cOutputCleanup -and $taskB1cPidFileAbsent -and $taskB1cListenerAbsent
    if ($null -eq $taskB1cFailure -and ($taskB1cTools.Count -ne 10 -or $taskB1cToolAttempts -ne 10)) {$taskB1cFailure='accountsync_run_tool_sequence_incomplete'}
    $taskB1cPassword=$null;$taskB1cConnection=$null;$taskB1cTestEnvironment=$null;$taskB1cPsqlEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskB1cPlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskB1cReservation.OwnerId;Stage=$taskB1cPlan.Stage;Failure=$taskB1cFailure;
        PostgreSqlCleanupConfirmed=$taskB1cCleanup;PostgreSqlPid=if($null -ne $taskB1cProof){$taskB1cProof.Pid}else{0};
        PostgreSqlProcessCleanupConfirmed=$taskB1c2PostgresProcessCleanup;ShellBridgeCleanupConfirmed=$taskB1cShellBridgeCleanup;
        ToolProcessCleanupConfirmed=$taskB1cToolCleanup;ToolAttempts=$taskB1cToolAttempts;
        ToolOutputResourcesCleanupConfirmed=$taskB1cOutputCleanup;RecordedPort=if($null -ne $taskB1cFacts){$taskB1cFacts.Port}else{0};
        PostmasterPidFilePresent=[IO.File]::Exists((Join-Path $taskB1cPlan.DataDirectory 'postmaster.pid'));
        ListenerPresentOnRecordedPort=if($null -ne $taskB1cFacts){Test-DangoProfileListener $taskB1cFacts.Port}else{$null};
        ToolProcesses=@($taskB1cTools);InputsFrozen=$taskB1cInputsFrozen;
        TestsPassed=($taskB1cCleanup -and $null -eq $taskB1cFailure -and $null -ne $taskB1cAudit -and $null -ne $taskB1cResources -and
            $null -ne $taskB1cScopedGraph -and $null -ne $taskB1cJourney -and $taskB1cArchivesValidated -and $taskB1cFaultsValidated -and $taskB1cScopeValidated -and $taskB1cIntentsValidated);
        ArchivesValidated=$taskB1cArchivesValidated;FaultsValidated=$taskB1cFaultsValidated;
        ScopeValidated=$taskB1cScopeValidated;LogoutIntentsValidated=$taskB1cIntentsValidated;
        AttemptEvidence=$taskB1cAudit;FixturesAttempted=$taskB1cTestsAttempted;FixtureResourceEvidence=$taskB1cResources;
        FixtureNativeCleanupConfirmed=($null -ne $taskB1cResources -and $taskB1cResources.NativeCleanupConfirmed);
        StartupObservation=$taskB1cStartupObservation;MaximumHttpRequests=128;MinimumHttpStartIntervalMilliseconds=600;MaximumDurationMinutes=10;TestProcessTimeoutSeconds=300;
        WholeSuiteReserved=$true;RetryAllowed=$false;DataRetained=[IO.Directory]::Exists($taskB1cPlan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskB1cPlan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskB1cPlan.StateDirectory 'b1test-state.json')))}) -CreateNew
}
if (-not $taskB1cCleanup) {throw 'accountsync_run_cleanup_unconfirmed_no_retry'}
if (-not $taskB1cOutputCleanup) {throw 'accountsync_run_output_cleanup_unconfirmed_no_retry'}
if ($taskB1cFailure) {throw $taskB1cFailure}
Write-Output 'accountsync_run_one_journey_passed_data_retained'

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
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncB1bPostgres.psm1') -Force
$taskB1bPlan=Get-DangoAccountSyncB1bPlan (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'accountsync_run_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskB1bReview=if ($PrepareApprovalManifest) { New-DangoAccountSyncB1bApproval $taskB1bPlan $ApprovalManifest }
        else { Get-DangoAccountSyncB1bInputs $taskB1bPlan }
    [pscustomobject]@{Plan=$taskB1bPlan;ReviewInputs=$taskB1bReview};return
}
if (-not $IsWindows) { throw 'accountsync_run_windows_required' }
$taskB1bApproved=Assert-DangoAccountSyncB1bApproval $taskB1bPlan $ApprovalManifest $ApprovalManifestSha256

function Read-DangoAccountSyncB1bBoundedState {
    param([string]$Path)
    Assert-DangoProfileRegularPath $Path
    $stream=[IO.FileStream]::new($Path,'Open','Read','Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 8192) { throw 'accountsync_run_state_bound_rejected' }
        $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
        if ($stream.ReadByte() -ne -1) { throw 'accountsync_run_state_bound_rejected' }
        return ConvertFrom-DangoAccountSyncB1bStrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
    }finally{$stream.Dispose()}
}

function Invoke-DangoAccountSyncB1bTool {
    param([string]$Step,[string]$Executable,[string[]]$Arguments,[hashtable]$EnvironmentValues=@{},
        [int]$TimeoutSeconds=30,[switch]$KillOwnedTree,[string]$OutputContract='CompleteEof')
    # One immutable sequence is reserved, with a single final stop permitted after failure.
    # This wrapper cannot dispatch a retry, migration, second suite or extra diagnostic.
    $expected=@('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped','stop')
    $isStop=Test-DangoAccountSyncB1bString $Step 'stop'
    if ($taskB1bToolAttempts -ge $taskB1bPlan.MaximumToolProcesses -or $taskB1bStopAttempted -or
        (-not $isStop -and ($taskB1bToolIndex -ge 9 -or -not(Test-DangoAccountSyncB1bString $Step $expected[$taskB1bToolIndex]))) -or
        ($isStop -and ($taskB1bToolIndex -lt 3 -or -not $taskB1bStopOwnershipVerified))) { throw 'accountsync_b1b_tool_budget_rejected' }
    Assert-DangoAccountSyncB1bPathText $Executable
    $toolExecutable=$taskB1bControl;$toolArguments=@();$toolEnvironment=@{};$toolTimeout=30;$toolTree=$false;$toolContract='CompleteEof'
    switch -CaseSensitive ($Step) {
        version {$toolExecutable=$taskB1bPostgres;$toolArguments=@('--version')}
        status {$toolArguments=@('status','-D',$taskB1bPlan.DataDirectory)}
        start {$toolTimeout=40;$toolContract='PostgresStartIgnored';$toolArguments=@('start','-D',$taskB1bPlan.DataDirectory,'-l',
            (Join-Path $taskB1bPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p 63237",'-w','-t','30')}
        {Test-DangoAccountSyncB1bStringIn $_ @('before-size','after-size')} {$toolExecutable=$taskB1bPsql;$toolContract='AccountSyncB1bSmallJson';$toolEnvironment=$taskB1bPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)}
        {Test-DangoAccountSyncB1bStringIn $_ @('before-full','after-full')} {$toolExecutable=$taskB1bPsql;$toolContract='AccountSyncB1bEvidenceJson';$toolEnvironment=$taskB1bPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncDatabaseSql)}
        vstest {$toolExecutable=$taskB1bDotnet;$toolTimeout=300;$toolTree=$true;$toolEnvironment=$taskB1bTestEnvironment;
            $toolArguments=@('vstest',$taskB1bTestDll,('--TestCaseFilter:'+$taskB1bPlan.Filter),('--Logger:trx;LogFileName='+$taskB1bPlan.TrxFileName),
                ('--ResultsDirectory:'+$taskB1bPlan.EvidenceDirectory))}
        scoped {$toolExecutable=$taskB1bPsql;$toolContract='AccountSyncB1bSmallJson';$toolEnvironment=$taskB1bPsqlEnvironment;
            $toolArguments=Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncB1bScopedSql $taskB1bNewUser $taskB1bFacts.Epoch $taskB1bReservation.OwnerId `
                ([Guid]$taskB1bJourney.Final.PetId) ([Guid]$taskB1bJourney.Final.DefaultRoomId))}
        stop {$toolTimeout=40;$toolArguments=@('stop','-D',$taskB1bPlan.DataDirectory,'-m','fast','-w','-t','30')}
    }
    if(-not(Test-DangoProfilePath $Executable $toolExecutable) -or -not(Test-DangoAccountSyncB1bFixedValue $Arguments ([string[]]$toolArguments)) -or
        -not(Test-DangoAccountSyncB1bFixedValue $EnvironmentValues $toolEnvironment) -or $TimeoutSeconds -ne $toolTimeout -or
        ([bool]$KillOwnedTree) -ne $toolTree -or -not(Test-DangoAccountSyncB1bString $OutputContract $toolContract)){throw 'accountsync_b1b_tool_contract_rejected'}
    if(-not $isStop){Assert-DangoAccountSyncB1bFrozenInputs}
    $script:taskB1bToolAttempts++;$script:taskB1bToolIndex++
    if ($isStop) { $script:taskB1bStopAttempted=$true }
    $options=@{Executable=$Executable;Arguments=$Arguments;WorkingDirectory=$taskB1bPlan.Repository;
        EnvironmentValues=$EnvironmentValues;TimeoutSeconds=$TimeoutSeconds;KillOwnedTree=$KillOwnedTree;
        Receipts=$taskB1bTools;OutputContract=$OutputContract}
    if (Test-DangoAccountSyncB1bString $OutputContract 'PostgresStartIgnored') {
        $options.PostgresStartPlan=$taskB1bPlan;$options.PostgresStartPort=$taskB1bFacts.Port
    }elseif (Test-DangoAccountSyncB1bStringIn $OutputContract @('AccountSyncB1bEvidenceJson','AccountSyncB1bSmallJson')) {
        $options.PostgresEvidencePlan=$taskB1bPlan;$options.PostgresEvidencePort=$taskB1bFacts.Port
    }
    return Invoke-DangoProfileProcess @options
}

function Invoke-DangoAccountSyncB1bSnapshot {
    param([ValidateSet('before','after')][string]$Phase)
    $sizeArgs=Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)
    $sizeResult=Invoke-DangoAccountSyncB1bTool ($Phase+'-size') $taskB1bPsql $sizeArgs $taskB1bPsqlEnvironment -OutputContract AccountSyncB1bSmallJson
    if(-not $sizeResult.Receipt.OutputComplete){throw 'accountsync_b1b_size_eof_unconfirmed'}
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan ($Phase+'-size.json') $sizeResult.RawOutput
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan ($Phase+'-size.stderr.txt') $sizeResult.RawError
    if ($sizeResult.ExitCode -ne 0) { throw 'accountsync_b1b_database_size_probe_failed' }
    $sizeEvidence=ConvertFrom-DangoAccountSyncB1bStrictJson $sizeResult.RawOutput
    [void](Assert-DangoProfileDatabaseEvidenceSize $sizeEvidence)
    $result=Invoke-DangoAccountSyncB1bTool ($Phase+'-full') $taskB1bPsql $taskB1bPsqlArgs $taskB1bPsqlEnvironment `
        -OutputContract AccountSyncB1bEvidenceJson
    if (-not $result.Receipt.OutputComplete) { throw 'accountsync_b1b_database_snapshot_eof_unconfirmed' }
    # The reader already enforces independent character and UTF-8 byte limits and full EOF.
    # Preserve the entire original snapshot before parsing or comparing any graph.
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan ($Phase+'.json') $result.RawOutput -MaximumBytes 1048576
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan ($Phase+'.stderr.txt') $result.RawError
    if($result.ExitCode -ne 0){throw 'accountsync_b1b_database_snapshot_failed'}
    $evidence=ConvertFrom-DangoAccountSyncB1bStrictJson $result.RawOutput -MaximumBytes 1048576
    Assert-DangoAccountSyncB1bTarget $taskB1bPlan $evidence
    return $evidence
}

function Assert-DangoAccountSyncB1bFrozenInputs {
    [void](Assert-DangoAccountSyncB1bApproval $taskB1bPlan $ApprovalManifest $ApprovalManifestSha256)
    foreach($path in $taskB1bExtraHashes.Keys) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path) -or -not(Test-DangoAccountSyncB1bString ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) $taskB1bExtraHashes[$path])) {
            throw 'accountsync_run_frozen_input_changed'
        }
    }
}

function Test-DangoAccountSyncB1bProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    try {
        $process=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
        try {return $process.HasExited -or $process.StartTime.ToUniversalTime() -ne (ConvertTo-DangoProfileUtc $CreatedAt)}
        finally{$process.Dispose()}
    }catch [ArgumentException]{return $true}
    catch{return $false}
}

function Wait-DangoAccountSyncB1bProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 3000) {
        if (Test-DangoAccountSyncB1bProcessEnded $ProcessIdValue $CreatedAt) {return $true}
        [Threading.Thread]::Sleep(100)
    }
    return Test-DangoAccountSyncB1bProcessEnded $ProcessIdValue $CreatedAt
}

function Read-DangoAccountSyncB1bLedgerRows {
    param([string]$Path,[int]$Count)
    $lines=@(Read-DangoProfileLedger $Path $Count)
    if ($lines.Count -ne $Count) {throw 'accountsync_run_ledger_count_rejected'}
    foreach($line in $lines) {
        if ($line.Length -lt 1 -or $line.Length -gt 2048 -or [Text.Encoding]::UTF8.GetByteCount($line) -gt 2048) {
            throw 'accountsync_run_ledger_record_bound_rejected'
        }
        $row=ConvertFrom-DangoAccountSyncB1bStrictJson $line
        if ($null -eq $row -or $row -isnot [System.Management.Automation.PSCustomObject]) {throw 'accountsync_run_ledger_record_rejected'}
        $row
    }
}



# CreateNew directory, reservation and ledgers precede all original state and credential reads.
# Any subsequent failure consumes this stage; retained evidence cannot be reset by this runner.
$taskB1bReservation=New-DangoAccountSyncB1bReservation $taskB1bPlan
Write-DangoProfileJson (Join-Path $taskB1bPlan.EvidenceDirectory 'approved-inputs.json') $taskB1bApproved -CreateNew
$taskB1bTools=[Collections.Generic.List[object]]::new();$taskB1bExtraHashes=@{}
$taskB1bToolAttempts=0;$taskB1bToolIndex=0;$taskB1bStopOwnershipVerified=$false;$taskB1bNewUser=[Guid]::Empty
$taskB1bPostgresProcessCleanup=$false;$taskB1bShellBridgeCleanup=$false;$taskB1bToolCleanup=$false
$taskB1bPidFileAbsent=$false;$taskB1bListenerAbsent=$false
$taskB1bFacts=$null;$taskB1bProof=$null;$taskB1bStartupObservation=$null
$taskB1bStartAttempt=$null;$taskB1bStartReturn=$null;$taskB1bStartTool=$null;$taskB1bStartControlExit=-1
$taskB1bStopAttempted=$false;$taskB1bFailure=$null;$taskB1bCleanup=$false;$taskB1bOutputCleanup=$false
$taskB1bPassword=$null;$taskB1bConnection=$null;$taskB1bTestEnvironment=$null;$taskB1bPsqlEnvironment=$null
$taskB1bAudit=$null;$taskB1bResources=$null;$taskB1bScopedGraph=$null;$taskB1bJourney=$null
$taskB1bArchivesValidated=$false;$taskB1bFaultsValidated=$false
$taskB1bTestsAttempted=$false;$taskB1bInputsFrozen=$false;$taskB1bBefore=$null;$taskB1bAfter=$null
$taskB1bTrx=Join-Path $taskB1bPlan.EvidenceDirectory $taskB1bPlan.TrxFileName
try {
    Assert-DangoAccountSyncClosurePrivatePath $taskB1bPlan.StateDirectory $true
    foreach($name in @('state.json','credential.dpapi','b1test-state.json','b1tests-keys')) {
        $path=Join-Path $taskB1bPlan.StateDirectory $name;Assert-DangoProfileRegularPath $path
        if (-not (Test-Path -LiteralPath $path)) { throw 'accountsync_run_existing_state_required' }
    }
    foreach($name in @('state.json','credential.dpapi','b1test-state.json')){Assert-DangoAccountSyncClosurePrivatePath (Join-Path $taskB1bPlan.StateDirectory $name)}
    Assert-DangoProfileRegularPath $taskB1bPlan.DataDirectory
    if (-not [IO.Directory]::Exists($taskB1bPlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskB1bPlan.DataDirectory 'postmaster.pid'))) { throw 'accountsync_run_existing_postmaster_rejected' }
    $taskB1bState=Read-DangoAccountSyncB1bBoundedState (Join-Path $taskB1bPlan.StateDirectory 'state.json')
    $taskB1bEpoch=Read-DangoAccountSyncB1bBoundedState (Join-Path $taskB1bPlan.StateDirectory 'b1test-state.json')
    $taskB1bFacts=Assert-DangoAccountSyncB1bState $taskB1bPlan $taskB1bState $taskB1bEpoch
    if ($taskB1bFacts.Port -ne 63237) { throw 'accountsync_run_fixed_port_rejected' }
    if (Test-DangoProfileListener $taskB1bFacts.Port) { throw 'accountsync_run_existing_listener_rejected' }
    $taskB1bPostgres=Join-Path $taskB1bPlan.PostgresBinDirectory 'postgres.exe'
    $taskB1bControl=Join-Path $taskB1bPlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskB1bPsql=Join-Path $taskB1bPlan.PostgresBinDirectory 'psql.exe'
    $taskB1bDotnet=(Get-Command dotnet -CommandType Application).Source
    $taskB1bTestDll=Join-Path $taskB1bPlan.Repository ($taskB1bPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    foreach($path in @($taskB1bPostgres,$taskB1bControl,$taskB1bPsql,$taskB1bDotnet,$taskB1bTestDll,
        (Join-Path $taskB1bPlan.StateDirectory 'state.json'),(Join-Path $taskB1bPlan.StateDirectory 'b1test-state.json'))) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { throw 'accountsync_run_required_input_missing' }
        $taskB1bExtraHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    Write-DangoProfileJson (Join-Path $taskB1bPlan.EvidenceDirectory 'input-hashes.json') $taskB1bExtraHashes -CreateNew
    Assert-DangoAccountSyncB1bFrozenInputs;$taskB1bInputsFrozen=$true
    $version=Invoke-DangoAccountSyncB1bTool 'version' $taskB1bPostgres @('--version')
    if ($version.ExitCode -ne 0 -or $version.Output -cnotmatch '^postgres \(PostgreSQL\) 18\.') { throw 'accountsync_run_postgres18_required' }
    $status=Invoke-DangoAccountSyncB1bTool 'status' $taskB1bControl @('status','-D',$taskB1bPlan.DataDirectory)
    if ($status.ExitCode -ne 3) { throw 'accountsync_run_existing_postgres_rejected' }
    $credentialPath=Join-Path $taskB1bPlan.StateDirectory 'credential.dpapi'
    $credentialStream=[IO.FileStream]::new($credentialPath,'Open','Read','Read');$cipher=$null;$plain=$null;$decoded=$null
    try {
        if ($credentialStream.Length -le 0 -or $credentialStream.Length -gt 8192) { throw 'accountsync_run_credential_invalid' }
        $cipher=[byte[]]::new([int]$credentialStream.Length);$credentialStream.ReadExactly($cipher)
        if ($credentialStream.ReadByte() -ne -1) { throw 'accountsync_run_credential_invalid' }
        $plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskB1bPassword=[Text.UTF8Encoding]::new($false,$true).GetString($plain)
        if ($taskB1bPassword -cnotmatch '\A[A-Za-z0-9+/]{43}=\z') { throw 'accountsync_run_credential_invalid' }
        $decoded=[Convert]::FromBase64String($taskB1bPassword)
        if ($decoded.Length -ne 32 -or -not(Test-DangoAccountSyncB1bString ([Convert]::ToBase64String($decoded)) $taskB1bPassword)) { throw 'accountsync_run_credential_invalid' }
    }finally{
        $credentialStream.Dispose()
        foreach($bytes in @($cipher,$plain,$decoded)) {if ($null -ne $bytes) {[Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)}}
    }
    Assert-DangoAccountSyncB1bFrozenInputs
    $taskB1bStartAttempt=[DateTime]::UtcNow;$startIndex=$taskB1bTools.Count
    try {
        $started=Invoke-DangoAccountSyncB1bTool 'start' $taskB1bControl @('start','-D',$taskB1bPlan.DataDirectory,'-l',
            (Join-Path $taskB1bPlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskB1bFacts.Port)",'-w','-t','30') `
            -TimeoutSeconds 40 -OutputContract PostgresStartIgnored
        $taskB1bStartControlExit=$started.ExitCode
    }finally{
        $taskB1bStartReturn=[DateTime]::UtcNow
        if ($taskB1bTools.Count -gt $startIndex) {
            $taskB1bStartTool=$taskB1bTools[$startIndex]
            if ($taskB1bStartTool.ExitCodeRecorded) {$taskB1bStartControlExit=$taskB1bStartTool.ExitCode}
        }
    }
    $taskB1bProof=Get-DangoProfilePostgresProof $taskB1bPlan $taskB1bFacts.Port $taskB1bStartControlExit $taskB1bStartTool
    if (-not (Test-DangoProfilePostgresProof $taskB1bProof $taskB1bPostgres $taskB1bPlan.DataDirectory $taskB1bFacts.Port `
        $taskB1bStartAttempt $taskB1bStartReturn $taskB1bPlan)) { throw 'accountsync_run_start_ownership_unconfirmed' }
    if ($taskB1bStartControlExit -ne 0) { throw 'accountsync_run_postgres_start_failed' }
    $taskB1bPsqlArgs=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$taskB1bFacts.Port,
        '-U',$taskB1bPlan.Role,'-d',$taskB1bPlan.Database,'-c',(Get-DangoAccountSyncDatabaseSql))
    $taskB1bPsqlEnvironment=@{PGPASSWORD=$taskB1bPassword;PGCLIENTENCODING='UTF8'}
    $taskB1bBefore=Invoke-DangoAccountSyncB1bSnapshot 'before'
    $taskB1bConnection="Host=127.0.0.1;Port=63237;Database=dango_backend_b0;Username=dango_b0_owner;Password=$taskB1bPassword;Include Error Detail=false"
    $taskB1bTestEnvironment=@{DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED='1';
        DANGO_BACKEND_ACCOUNTSYNC_B1B1_OWNER_ID=$taskB1bReservation.OwnerId.ToString('D');
        DANGO_BACKEND_ACCOUNTSYNC_B1B1_OWNER_PID=[string]$taskB1bReservation.Owner.OwnerPid;
        DANGO_BACKEND_ACCOUNTSYNC_B1B1_RUN_DIRECTORY=$taskB1bPlan.EvidenceDirectory;
        DANGO_BACKEND_ACCOUNTSYNC_B1B1_OWNER_MANIFEST_SHA256=$taskB1bReservation.ManifestSha256;
        DANGO_BACKEND_TEST_CONNECTION=$taskB1bConnection;DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskB1bPlan.DataDirectory;
        DANGO_BACKEND_B1_KEY_DIRECTORY=(Join-Path $taskB1bPlan.StateDirectory 'b1tests-keys');
        DANGO_BACKEND_B1_RECOVERY_EPOCH=$taskB1bFacts.Epoch.ToString('D')}
    Assert-DangoAccountSyncB1bFrozenInputs;$taskB1bTestsAttempted=$true
    $test=Invoke-DangoAccountSyncB1bTool 'vstest' $taskB1bDotnet @('vstest',$taskB1bTestDll,('--TestCaseFilter:'+$taskB1bPlan.Filter),
        ('--Logger:trx;LogFileName='+$taskB1bPlan.TrxFileName),('--ResultsDirectory:'+$taskB1bPlan.EvidenceDirectory)) $taskB1bTestEnvironment `
        -TimeoutSeconds 300 -KillOwnedTree
    if ($test.ExitCode -ne 0) { throw 'accountsync_run_tests_failed_no_retry' }
    Assert-DangoAccountSyncB1bTrx $taskB1bTrx
    $attemptRows=@(Read-DangoAccountSyncB1bLedgerRows (Join-Path $taskB1bPlan.EvidenceDirectory 'attempts.jsonl') 17)
    $taskB1bAudit=Assert-DangoAccountSyncB1bAttempts $taskB1bPlan $taskB1bReservation.OwnerId $attemptRows
    Write-DangoProfileJson (Join-Path $taskB1bPlan.EvidenceDirectory 'attempt-summary.json') $taskB1bAudit -CreateNew
    $resourceRows=@(Read-DangoAccountSyncB1bLedgerRows (Join-Path $taskB1bPlan.EvidenceDirectory 'resources.jsonl') 6)
    $taskB1bResources=Assert-DangoAccountSyncB1bResources $taskB1bReservation.OwnerId $resourceRows
    Write-DangoProfileJson (Join-Path $taskB1bPlan.EvidenceDirectory 'fixture-resource-summary.json') $taskB1bResources -CreateNew
    $taskB1bAfter=Invoke-DangoAccountSyncB1bSnapshot 'after'
    $taskB1bNewUser=Assert-DangoAccountSyncB1bOldGraph $taskB1bBefore $taskB1bAfter
    $journeyPath=Join-Path $taskB1bPlan.EvidenceDirectory 'journey-evidence.json'
    $journeyBytes=Read-DangoAccountSyncClosureBytes $journeyPath 131072
    $taskB1bJourney=ConvertFrom-DangoAccountSyncB1bStrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($journeyBytes))
    Assert-DangoAccountSyncB1bJourney $taskB1bJourney $taskB1bReservation.OwnerId $taskB1bNewUser $taskB1bFacts.Epoch
    $archiveBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskB1bPlan.EvidenceDirectory 'configuration-sync-evidence.json') 131072
    $archiveEvidence=ConvertFrom-DangoAccountSyncB1bStrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($archiveBytes))
    Assert-DangoAccountSyncB1bArchives $archiveEvidence $taskB1bNewUser ([Guid]$taskB1bJourney.Final.PetId);$taskB1bArchivesValidated=$true
    $faultBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskB1bPlan.EvidenceDirectory 'fault-evidence.json') 131072
    $faultEvidence=ConvertFrom-DangoAccountSyncB1bStrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($faultBytes))
    Assert-DangoAccountSyncB1bFaultEvidence $faultEvidence $taskB1bReservation.OwnerId
    Assert-DangoAccountSyncB1bAuxAgreement $archiveEvidence $faultEvidence;$taskB1bFaultsValidated=$true
    $scopedSql=Get-DangoAccountSyncB1bScopedSql $taskB1bNewUser $taskB1bFacts.Epoch $taskB1bReservation.OwnerId `
        ([Guid]$taskB1bJourney.Final.PetId) ([Guid]$taskB1bJourney.Final.DefaultRoomId)
    $scopedArgs=@($taskB1bPsqlArgs[0..($taskB1bPsqlArgs.Count-2)])+$scopedSql
    $scoped=Invoke-DangoAccountSyncB1bTool 'scoped' $taskB1bPsql $scopedArgs $taskB1bPsqlEnvironment -OutputContract AccountSyncB1bSmallJson
    if(-not $scoped.Receipt.OutputComplete){throw 'accountsync_b1b_scoped_eof_unconfirmed'}
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan 'scoped-graph-evidence.json' $scoped.RawOutput
    Save-DangoAccountSyncB1bRawEvidence $taskB1bPlan 'scoped.stderr.txt' $scoped.RawError
    if ($scoped.ExitCode -ne 0) { throw 'accountsync_run_scoped_evidence_failed' }
    $taskB1bScopedGraph=ConvertFrom-DangoAccountSyncB1bStrictJson $scoped.RawOutput
    Assert-DangoAccountSyncB1bScopedGraph $taskB1bScopedGraph
    Assert-DangoAccountSyncB1bFrozenInputs
}catch{
    # Never emit arbitrary exception text: it can include credentials or a connection string.
    $taskB1bFailure=if ($_.Exception.Message -cmatch '^(accountsync|profile_run)_[a-z0-9_]+$') {$_.Exception.Message}
        else {'accountsync_run_operation_failed'}
}finally{
    try {
        if ($null -ne $taskB1bStartAttempt -and $null -ne $taskB1bStartReturn) {
            $taskB1bStartupObservation=Observe-DangoProfileStartup $taskB1bPlan $taskB1bFacts.Port `
                $taskB1bStartControlExit $taskB1bStartTool $taskB1bStartAttempt
            if ($taskB1bStartupObservation.Verified) {$taskB1bProof=$taskB1bStartupObservation.Proof}
        }
        if ($null -ne $taskB1bProof -and $null -ne $taskB1bStartupObservation -and $taskB1bStartupObservation.Verified) {
            $current=Get-DangoProfilePostgresProof $taskB1bPlan $taskB1bFacts.Port $taskB1bProof.ControlExitCode $taskB1bStartTool
            if ($current.Pid -eq $taskB1bProof.Pid -and $current.CreatedAt -eq $taskB1bProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $current $taskB1bPostgres $taskB1bPlan.DataDirectory $taskB1bFacts.Port `
                    $taskB1bStartAttempt $taskB1bStartupObservation.ObservedUntil $taskB1bPlan)) {
                $taskB1bStopOwnershipVerified=$true
                $stopped=Invoke-DangoAccountSyncB1bTool 'stop' $taskB1bControl @('stop','-D',$taskB1bPlan.DataDirectory,'-m','fast','-w','-t','30') -TimeoutSeconds 40
                if($stopped.ExitCode -ne 0 -and $null -eq $taskB1bFailure){$taskB1bFailure='accountsync_b1b_stop_failed'}
                $taskB1bPostgresProcessCleanup=Wait-DangoAccountSyncB1bProcessEnded $taskB1bProof.Pid $taskB1bProof.CreatedAt
                $taskB1bShellBridgeCleanup=Wait-DangoAccountSyncB1bProcessEnded $taskB1bProof.ShellBridge.Pid $taskB1bProof.ShellBridge.CreatedAt
            }
        }elseif($null -eq $taskB1bStartAttempt){$taskB1bPostgresProcessCleanup=$true;$taskB1bShellBridgeCleanup=$true}
    }catch{$taskB1bCleanup=$false}
    $taskB1bToolCleanup=@($taskB1bTools|Where-Object {-not $_.CleanupConfirmed -or -not $_.ProcessDisposeConfirmed}).Count -eq 0
    $taskB1bOutputCleanup=@($taskB1bTools|Where-Object {-not $_.OutputResourcesCleanupConfirmed}).Count -eq 0
    $taskB1bPidFileAbsent=-not [IO.File]::Exists((Join-Path $taskB1bPlan.DataDirectory 'postmaster.pid'))
    $taskB1bListenerAbsent=-not(Test-DangoProfileListener $taskB1bPlan.DatabasePort)
    $taskB1bCleanup=$taskB1bPostgresProcessCleanup -and $taskB1bShellBridgeCleanup -and $taskB1bToolCleanup -and
        $taskB1bOutputCleanup -and $taskB1bPidFileAbsent -and $taskB1bListenerAbsent
    if ($null -eq $taskB1bFailure -and ($taskB1bTools.Count -ne 10 -or $taskB1bToolAttempts -ne 10)) {$taskB1bFailure='accountsync_run_tool_sequence_incomplete'}
    $taskB1bPassword=$null;$taskB1bConnection=$null;$taskB1bTestEnvironment=$null;$taskB1bPsqlEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskB1bPlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskB1bReservation.OwnerId;Stage=$taskB1bPlan.Stage;Failure=$taskB1bFailure;
        PostgreSqlCleanupConfirmed=$taskB1bCleanup;PostgreSqlPid=if($null -ne $taskB1bProof){$taskB1bProof.Pid}else{0};
        PostgreSqlProcessCleanupConfirmed=$taskB1bPostgresProcessCleanup;ShellBridgeCleanupConfirmed=$taskB1bShellBridgeCleanup;
        ToolProcessCleanupConfirmed=$taskB1bToolCleanup;ToolAttempts=$taskB1bToolAttempts;
        ToolOutputResourcesCleanupConfirmed=$taskB1bOutputCleanup;RecordedPort=if($null -ne $taskB1bFacts){$taskB1bFacts.Port}else{0};
        PostmasterPidFilePresent=[IO.File]::Exists((Join-Path $taskB1bPlan.DataDirectory 'postmaster.pid'));
        ListenerPresentOnRecordedPort=if($null -ne $taskB1bFacts){Test-DangoProfileListener $taskB1bFacts.Port}else{$null};
        ToolProcesses=@($taskB1bTools);InputsFrozen=$taskB1bInputsFrozen;
        TestsPassed=($taskB1bCleanup -and $null -eq $taskB1bFailure -and $null -ne $taskB1bAudit -and $null -ne $taskB1bResources -and
            $null -ne $taskB1bScopedGraph -and $null -ne $taskB1bJourney -and $taskB1bArchivesValidated -and $taskB1bFaultsValidated);
        ArchivesValidated=$taskB1bArchivesValidated;FaultsValidated=$taskB1bFaultsValidated;
        AttemptEvidence=$taskB1bAudit;FixturesAttempted=$taskB1bTestsAttempted;FixtureResourceEvidence=$taskB1bResources;
        FixtureNativeCleanupConfirmed=($null -ne $taskB1bResources -and $taskB1bResources.NativeCleanupConfirmed);
        StartupObservation=$taskB1bStartupObservation;MaximumHttpRequests=128;MaximumDurationMinutes=10;TestProcessTimeoutSeconds=300;
        WholeSuiteReserved=$true;RetryAllowed=$false;DataRetained=[IO.Directory]::Exists($taskB1bPlan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskB1bPlan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskB1bPlan.StateDirectory 'b1test-state.json')))}) -CreateNew
}
if (-not $taskB1bCleanup) {throw 'accountsync_run_cleanup_unconfirmed_no_retry'}
if (-not $taskB1bOutputCleanup) {throw 'accountsync_run_output_cleanup_unconfirmed_no_retry'}
if ($taskB1bFailure) {throw $taskB1bFailure}
Write-Output 'accountsync_run_one_journey_passed_data_retained'

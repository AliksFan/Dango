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
Import-Module (Join-Path $PSScriptRoot 'Dango.PorchCrudC1Postgres.psm1') -Force
$taskPorchC1Plan=Get-DangoPorchCrudC1Plan (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) { throw 'porch_c1_run_prepare_execute_conflict' }
if (-not $ExecuteApprovedPlan) {
    $taskPorchC1Review=if ($PrepareApprovalManifest) { New-DangoPorchCrudC1Approval $taskPorchC1Plan $ApprovalManifest }
        else { Get-DangoPorchCrudC1Inputs $taskPorchC1Plan }
    [pscustomobject]@{Plan=$taskPorchC1Plan;ReviewInputs=$taskPorchC1Review};return
}
if (-not $IsWindows) { throw 'porch_c1_run_windows_required' }
$taskPorchC1Approved=Assert-DangoPorchCrudC1Approval $taskPorchC1Plan $ApprovalManifest $ApprovalManifestSha256

function Read-DangoPorchCrudC1BoundedState {
    param([string]$Path)
    Assert-DangoProfileRegularPath $Path
    $stream=[IO.FileStream]::new($Path,'Open','Read','Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 8192) { throw 'porch_c1_run_state_bound_rejected' }
        $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
        if ($stream.ReadByte() -ne -1) { throw 'porch_c1_run_state_bound_rejected' }
        return ConvertFrom-DangoPorchCrudC1StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
    }finally{$stream.Dispose()}
}

function Invoke-DangoPorchCrudC1Tool {
    param([string]$Step,[string]$Executable,[string[]]$Arguments,[hashtable]$EnvironmentValues=@{},
        [int]$TimeoutSeconds=30,[switch]$KillOwnedTree,[string]$OutputContract='CompleteEof')
    # One immutable sequence is reserved, with a single final stop permitted after failure.
    # This wrapper cannot dispatch a retry, migration, second suite or extra diagnostic.
    $expected=@('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped','stop')
    $isStop=Test-DangoPorchCrudC1String $Step 'stop'
    if ($taskPorchC1ToolAttempts -ge $taskPorchC1Plan.MaximumToolProcesses -or $taskPorchC1StopAttempted -or
        (-not $isStop -and ($taskPorchC1ToolIndex -ge 9 -or -not(Test-DangoPorchCrudC1String $Step $expected[$taskPorchC1ToolIndex]))) -or
        ($isStop -and ($taskPorchC1ToolIndex -lt 3 -or -not $taskPorchC1StopOwnershipVerified))) { throw 'porch_c1_tool_budget_rejected' }
    Assert-DangoPorchCrudC1PathText $Executable
    $toolExecutable=$taskPorchC1Control;$toolArguments=@();$toolEnvironment=@{};$toolTimeout=30;$toolTree=$false;$toolContract='CompleteEof'
    switch -CaseSensitive ($Step) {
        version {$toolExecutable=$taskPorchC1Postgres;$toolArguments=@('--version')}
        status {$toolArguments=@('status','-D',$taskPorchC1Plan.DataDirectory)}
        start {$toolTimeout=40;$toolContract='PostgresStartIgnored';$toolArguments=@('start','-D',$taskPorchC1Plan.DataDirectory,'-l',
            (Join-Path $taskPorchC1Plan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p 63237",'-w','-t','30')}
        {Test-DangoPorchCrudC1StringIn $_ @('before-size','after-size')} {$toolExecutable=$taskPorchC1Psql;$toolContract='PorchCrudC1SmallJson';$toolEnvironment=$taskPorchC1PsqlEnvironment;
            $toolArguments=Get-DangoPorchCrudC1PsqlArguments $taskPorchC1Plan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)}
        {Test-DangoPorchCrudC1StringIn $_ @('before-full','after-full')} {$toolExecutable=$taskPorchC1Psql;$toolContract='PorchCrudC1EvidenceJson';$toolEnvironment=$taskPorchC1PsqlEnvironment;
            $toolArguments=Get-DangoPorchCrudC1PsqlArguments $taskPorchC1Plan (Get-DangoAccountSyncDatabaseSql)}
        vstest {$toolExecutable=$taskPorchC1Dotnet;$toolTimeout=300;$toolTree=$true;$toolEnvironment=$taskPorchC1TestEnvironment;
            $toolArguments=@('vstest',$taskPorchC1TestDll,('--TestCaseFilter:'+$taskPorchC1Plan.Filter),('--Logger:trx;LogFileName='+$taskPorchC1Plan.TrxFileName),
                ('--ResultsDirectory:'+$taskPorchC1Plan.EvidenceDirectory))}
        scoped {$toolExecutable=$taskPorchC1Psql;$toolContract='PorchCrudC1SmallJson';$toolEnvironment=$taskPorchC1PsqlEnvironment;
            $toolArguments=Get-DangoPorchCrudC1PsqlArguments $taskPorchC1Plan (Get-DangoPorchCrudC1ScopedSql $taskPorchC1Scope)}
        stop {$toolTimeout=40;$toolArguments=@('stop','-D',$taskPorchC1Plan.DataDirectory,'-m','fast','-w','-t','30')}
    }
    if(-not(Test-DangoProfilePath $Executable $toolExecutable) -or -not(Test-DangoPorchCrudC1FixedValue $Arguments ([string[]]$toolArguments)) -or
        -not(Test-DangoPorchCrudC1FixedValue $EnvironmentValues $toolEnvironment) -or $TimeoutSeconds -ne $toolTimeout -or
        ([bool]$KillOwnedTree) -ne $toolTree -or -not(Test-DangoPorchCrudC1String $OutputContract $toolContract)){throw 'porch_c1_tool_contract_rejected'}
    if(-not $isStop){Assert-DangoPorchCrudC1FrozenInputs}
    $script:taskPorchC1ToolAttempts++;$script:taskPorchC1ToolIndex++
    if ($isStop) { $script:taskPorchC1StopAttempted=$true }
    $options=@{Executable=$Executable;Arguments=$Arguments;WorkingDirectory=$taskPorchC1Plan.Repository;
        EnvironmentValues=$EnvironmentValues;TimeoutSeconds=$TimeoutSeconds;KillOwnedTree=$KillOwnedTree;
        Receipts=$taskPorchC1Tools;OutputContract=$OutputContract}
    if (Test-DangoPorchCrudC1String $OutputContract 'PostgresStartIgnored') {
        $options.PostgresStartPlan=$taskPorchC1Plan;$options.PostgresStartPort=$taskPorchC1Facts.Port
    }elseif (Test-DangoPorchCrudC1StringIn $OutputContract @('PorchCrudC1EvidenceJson','PorchCrudC1SmallJson')) {
        $options.PostgresEvidencePlan=$taskPorchC1Plan;$options.PostgresEvidencePort=$taskPorchC1Facts.Port
    }
    return Invoke-DangoProfileProcess @options
}

function Invoke-DangoPorchCrudC1Snapshot {
    param([ValidateSet('before','after')][string]$Phase)
    $sizeArgs=Get-DangoPorchCrudC1PsqlArguments $taskPorchC1Plan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)
    $sizeResult=Invoke-DangoPorchCrudC1Tool ($Phase+'-size') $taskPorchC1Psql $sizeArgs $taskPorchC1PsqlEnvironment -OutputContract PorchCrudC1SmallJson
    if(-not $sizeResult.Receipt.OutputComplete){throw 'porch_c1_size_eof_unconfirmed'}
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan ($Phase+'-size.json') $sizeResult.RawOutput
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan ($Phase+'-size.stderr.txt') $sizeResult.RawError
    if ($sizeResult.ExitCode -ne 0) { throw 'porch_c1_database_size_probe_failed' }
    $sizeEvidence=ConvertFrom-DangoPorchCrudC1StrictJson $sizeResult.RawOutput
    [void](Assert-DangoProfileDatabaseEvidenceSize $sizeEvidence)
    $result=Invoke-DangoPorchCrudC1Tool ($Phase+'-full') $taskPorchC1Psql $taskPorchC1PsqlArgs $taskPorchC1PsqlEnvironment `
        -OutputContract PorchCrudC1EvidenceJson
    if (-not $result.Receipt.OutputComplete) { throw 'porch_c1_database_snapshot_eof_unconfirmed' }
    # The reader already enforces independent character and UTF-8 byte limits and full EOF.
    # Preserve the entire original snapshot before parsing or comparing any graph.
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan ($Phase+'.json') $result.RawOutput -MaximumBytes 1048576
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan ($Phase+'.stderr.txt') $result.RawError
    if($result.ExitCode -ne 0){throw 'porch_c1_database_snapshot_failed'}
    $evidence=ConvertFrom-DangoPorchCrudC1StrictJson $result.RawOutput -MaximumBytes 1048576
    Assert-DangoPorchCrudC1Target $taskPorchC1Plan $evidence
    return $evidence
}

function Assert-DangoPorchCrudC1FrozenInputs {
    [void](Assert-DangoPorchCrudC1Approval $taskPorchC1Plan $ApprovalManifest $ApprovalManifestSha256)
    foreach($path in $taskPorchC1ExtraHashes.Keys) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path) -or -not(Test-DangoPorchCrudC1String ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) $taskPorchC1ExtraHashes[$path])) {
            throw 'porch_c1_run_frozen_input_changed'
        }
    }
}

function Test-DangoPorchCrudC1ProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    try {
        $process=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
        try {return $process.HasExited -or $process.StartTime.ToUniversalTime() -ne (ConvertTo-DangoProfileUtc $CreatedAt)}
        finally{$process.Dispose()}
    }catch [ArgumentException]{return $true}
    catch{return $false}
}

function Wait-DangoPorchCrudC1ProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt 3000) {
        if (Test-DangoPorchCrudC1ProcessEnded $ProcessIdValue $CreatedAt) {return $true}
        [Threading.Thread]::Sleep(100)
    }
    return Test-DangoPorchCrudC1ProcessEnded $ProcessIdValue $CreatedAt
}

function Read-DangoPorchCrudC1LedgerRows {
    param([string]$Path,[int]$Count)
    $lines=@(Read-DangoProfileLedger $Path $Count)
    if ($lines.Count -ne $Count) {throw 'porch_c1_run_ledger_count_rejected'}
    foreach($line in $lines) {
        if ($line.Length -lt 1 -or $line.Length -gt 2048 -or [Text.Encoding]::UTF8.GetByteCount($line) -gt 2048) {
            throw 'porch_c1_run_ledger_record_bound_rejected'
        }
        $row=ConvertFrom-DangoPorchCrudC1StrictJson $line
        if ($null -eq $row -or $row -isnot [System.Management.Automation.PSCustomObject]) {throw 'porch_c1_run_ledger_record_rejected'}
        $row
    }
}



# CreateNew directory, reservation and ledgers precede all original state and credential reads.
# Any subsequent failure consumes this stage; retained evidence cannot be reset by this runner.
$taskPorchC1Reservation=New-DangoPorchCrudC1Reservation $taskPorchC1Plan
Write-DangoProfileJson (Join-Path $taskPorchC1Plan.EvidenceDirectory 'approved-inputs.json') $taskPorchC1Approved -CreateNew
$taskPorchC1Tools=[Collections.Generic.List[object]]::new();$taskPorchC1ExtraHashes=@{}
$taskPorchC1ToolAttempts=0;$taskPorchC1ToolIndex=0;$taskPorchC1StopOwnershipVerified=$false;$taskPorchC1NewUsers=@();$taskPorchC1Scope=$null
$taskPorchC1PostgresProcessCleanup=$false;$taskPorchC1ShellBridgeCleanup=$false;$taskPorchC1ToolCleanup=$false
$taskPorchC1PidFileAbsent=$false;$taskPorchC1ListenerAbsent=$false
$taskPorchC1Facts=$null;$taskPorchC1Proof=$null;$taskPorchC1StartupObservation=$null
$taskPorchC1StartAttempt=$null;$taskPorchC1StartReturn=$null;$taskPorchC1StartTool=$null;$taskPorchC1StartControlExit=-1
$taskPorchC1StopAttempted=$false;$taskPorchC1Failure=$null;$taskPorchC1Cleanup=$false;$taskPorchC1OutputCleanup=$false
$taskPorchC1Password=$null;$taskPorchC1Connection=$null;$taskPorchC1TestEnvironment=$null;$taskPorchC1PsqlEnvironment=$null
$taskPorchC1Audit=$null;$taskPorchC1Resources=$null;$taskPorchC1ScopedGraph=$null;$taskPorchC1Journey=$null
$taskPorchC1JourneyValidated=$false;$taskPorchC1ScopeValidated=$false
$taskPorchC1TestsAttempted=$false;$taskPorchC1InputsFrozen=$false;$taskPorchC1Before=$null;$taskPorchC1After=$null
$taskPorchC1Trx=Join-Path $taskPorchC1Plan.EvidenceDirectory $taskPorchC1Plan.TrxFileName
try {
    Assert-DangoAccountSyncClosurePrivatePath $taskPorchC1Plan.StateDirectory $true
    foreach($name in @('state.json','credential.dpapi','b1test-state.json','b1tests-keys')) {
        $path=Join-Path $taskPorchC1Plan.StateDirectory $name;Assert-DangoProfileRegularPath $path
        if (-not (Test-Path -LiteralPath $path)) { throw 'porch_c1_run_existing_state_required' }
    }
    foreach($name in @('state.json','credential.dpapi','b1test-state.json')){Assert-DangoAccountSyncClosurePrivatePath (Join-Path $taskPorchC1Plan.StateDirectory $name)}
    Assert-DangoProfileRegularPath $taskPorchC1Plan.DataDirectory
    if (-not [IO.Directory]::Exists($taskPorchC1Plan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskPorchC1Plan.DataDirectory 'postmaster.pid'))) { throw 'porch_c1_run_existing_postmaster_rejected' }
    $taskPorchC1State=Read-DangoPorchCrudC1BoundedState (Join-Path $taskPorchC1Plan.StateDirectory 'state.json')
    $taskPorchC1Epoch=Read-DangoPorchCrudC1BoundedState (Join-Path $taskPorchC1Plan.StateDirectory 'b1test-state.json')
    $taskPorchC1Facts=Assert-DangoPorchCrudC1State $taskPorchC1Plan $taskPorchC1State $taskPorchC1Epoch
    if ($taskPorchC1Facts.Port -ne 63237) { throw 'porch_c1_run_fixed_port_rejected' }
    if (Test-DangoProfileListener $taskPorchC1Facts.Port) { throw 'porch_c1_run_existing_listener_rejected' }
    $taskPorchC1Postgres=Join-Path $taskPorchC1Plan.PostgresBinDirectory 'postgres.exe'
    $taskPorchC1Control=Join-Path $taskPorchC1Plan.PostgresBinDirectory 'pg_ctl.exe'
    $taskPorchC1Psql=Join-Path $taskPorchC1Plan.PostgresBinDirectory 'psql.exe'
    $taskPorchC1Dotnet=(Get-Command dotnet -CommandType Application).Source
    $taskPorchC1TestDll=Join-Path $taskPorchC1Plan.Repository ($taskPorchC1Plan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    foreach($path in @($taskPorchC1Postgres,$taskPorchC1Control,$taskPorchC1Psql,$taskPorchC1Dotnet,$taskPorchC1TestDll,
        (Join-Path $taskPorchC1Plan.StateDirectory 'state.json'),(Join-Path $taskPorchC1Plan.StateDirectory 'b1test-state.json'))) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { throw 'porch_c1_run_required_input_missing' }
        $taskPorchC1ExtraHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    Write-DangoProfileJson (Join-Path $taskPorchC1Plan.EvidenceDirectory 'input-hashes.json') $taskPorchC1ExtraHashes -CreateNew
    Assert-DangoPorchCrudC1FrozenInputs;$taskPorchC1InputsFrozen=$true
    $version=Invoke-DangoPorchCrudC1Tool 'version' $taskPorchC1Postgres @('--version')
    if ($version.ExitCode -ne 0 -or $version.Output -cnotmatch '^postgres \(PostgreSQL\) 18\.') { throw 'porch_c1_run_postgres18_required' }
    $status=Invoke-DangoPorchCrudC1Tool 'status' $taskPorchC1Control @('status','-D',$taskPorchC1Plan.DataDirectory)
    if ($status.ExitCode -ne 3) { throw 'porch_c1_run_existing_postgres_rejected' }
    $credentialPath=Join-Path $taskPorchC1Plan.StateDirectory 'credential.dpapi'
    $credentialStream=[IO.FileStream]::new($credentialPath,'Open','Read','Read');$cipher=$null;$plain=$null;$decoded=$null
    try {
        if ($credentialStream.Length -le 0 -or $credentialStream.Length -gt 8192) { throw 'porch_c1_run_credential_invalid' }
        $cipher=[byte[]]::new([int]$credentialStream.Length);$credentialStream.ReadExactly($cipher)
        if ($credentialStream.ReadByte() -ne -1) { throw 'porch_c1_run_credential_invalid' }
        $plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskPorchC1Password=[Text.UTF8Encoding]::new($false,$true).GetString($plain)
        if ($taskPorchC1Password -cnotmatch '\A[A-Za-z0-9+/]{43}=\z') { throw 'porch_c1_run_credential_invalid' }
        $decoded=[Convert]::FromBase64String($taskPorchC1Password)
        if ($decoded.Length -ne 32 -or -not(Test-DangoPorchCrudC1String ([Convert]::ToBase64String($decoded)) $taskPorchC1Password)) { throw 'porch_c1_run_credential_invalid' }
    }finally{
        $credentialStream.Dispose()
        foreach($bytes in @($cipher,$plain,$decoded)) {if ($null -ne $bytes) {[Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)}}
    }
    Assert-DangoPorchCrudC1FrozenInputs
    $taskPorchC1StartAttempt=[DateTime]::UtcNow;$startIndex=$taskPorchC1Tools.Count
    try {
        $started=Invoke-DangoPorchCrudC1Tool 'start' $taskPorchC1Control @('start','-D',$taskPorchC1Plan.DataDirectory,'-l',
            (Join-Path $taskPorchC1Plan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskPorchC1Facts.Port)",'-w','-t','30') `
            -TimeoutSeconds 40 -OutputContract PostgresStartIgnored
        $taskPorchC1StartControlExit=$started.ExitCode
    }finally{
        $taskPorchC1StartReturn=[DateTime]::UtcNow
        if ($taskPorchC1Tools.Count -gt $startIndex) {
            $taskPorchC1StartTool=$taskPorchC1Tools[$startIndex]
            if ($taskPorchC1StartTool.ExitCodeRecorded) {$taskPorchC1StartControlExit=$taskPorchC1StartTool.ExitCode}
        }
    }
    $taskPorchC1Proof=Get-DangoProfilePostgresProof $taskPorchC1Plan $taskPorchC1Facts.Port $taskPorchC1StartControlExit $taskPorchC1StartTool
    if (-not (Test-DangoProfilePostgresProof $taskPorchC1Proof $taskPorchC1Postgres $taskPorchC1Plan.DataDirectory $taskPorchC1Facts.Port `
        $taskPorchC1StartAttempt $taskPorchC1StartReturn $taskPorchC1Plan)) { throw 'porch_c1_run_start_ownership_unconfirmed' }
    if ($taskPorchC1StartControlExit -ne 0) { throw 'porch_c1_run_postgres_start_failed' }
    $taskPorchC1PsqlArgs=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$taskPorchC1Facts.Port,
        '-U',$taskPorchC1Plan.Role,'-d',$taskPorchC1Plan.Database,'-c',(Get-DangoAccountSyncDatabaseSql))
    $taskPorchC1PsqlEnvironment=@{PGPASSWORD=$taskPorchC1Password;PGCLIENTENCODING='UTF8'}
    $taskPorchC1Before=Invoke-DangoPorchCrudC1Snapshot 'before'
    $taskPorchC1Connection="Host=127.0.0.1;Port=63237;Database=dango_backend_b0;Username=dango_b0_owner;Password=$taskPorchC1Password;Include Error Detail=false"
    $taskPorchC1TestEnvironment=@{DANGO_BACKEND_PORCHCRUD_C1_APPROVED='1';
        DANGO_BACKEND_PORCHCRUD_C1_OWNER_ID=$taskPorchC1Reservation.OwnerId.ToString('D');
        DANGO_BACKEND_PORCHCRUD_C1_OWNER_PID=[string]$taskPorchC1Reservation.Owner.OwnerPid;
        DANGO_BACKEND_PORCHCRUD_C1_RUN_DIRECTORY=$taskPorchC1Plan.EvidenceDirectory;
        DANGO_BACKEND_PORCHCRUD_C1_OWNER_MANIFEST_SHA256=$taskPorchC1Reservation.ManifestSha256;
        DANGO_BACKEND_TEST_CONNECTION=$taskPorchC1Connection;DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskPorchC1Plan.DataDirectory;
        DANGO_BACKEND_B1_KEY_DIRECTORY=(Join-Path $taskPorchC1Plan.StateDirectory 'b1tests-keys');
        DANGO_BACKEND_B1_RECOVERY_EPOCH=$taskPorchC1Facts.Epoch.ToString('D')}
    Assert-DangoPorchCrudC1FrozenInputs;$taskPorchC1TestsAttempted=$true
    $test=Invoke-DangoPorchCrudC1Tool 'vstest' $taskPorchC1Dotnet @('vstest',$taskPorchC1TestDll,('--TestCaseFilter:'+$taskPorchC1Plan.Filter),
        ('--Logger:trx;LogFileName='+$taskPorchC1Plan.TrxFileName),('--ResultsDirectory:'+$taskPorchC1Plan.EvidenceDirectory)) $taskPorchC1TestEnvironment `
        -TimeoutSeconds 300 -KillOwnedTree
    if ($test.ExitCode -ne 0) { throw 'porch_c1_run_tests_failed_no_retry' }
    Assert-DangoPorchCrudC1Trx $taskPorchC1Trx
    $attemptRows=@(Read-DangoPorchCrudC1LedgerRows (Join-Path $taskPorchC1Plan.EvidenceDirectory 'attempts.jsonl') 21)
    $taskPorchC1Audit=Assert-DangoPorchCrudC1Attempts $taskPorchC1Plan $taskPorchC1Reservation.OwnerId $attemptRows
    Write-DangoProfileJson (Join-Path $taskPorchC1Plan.EvidenceDirectory 'attempt-summary.json') $taskPorchC1Audit -CreateNew
    $resourceRows=@(Read-DangoPorchCrudC1LedgerRows (Join-Path $taskPorchC1Plan.EvidenceDirectory 'resources.jsonl') 6)
    $taskPorchC1Resources=Assert-DangoPorchCrudC1Resources $taskPorchC1Reservation.OwnerId $resourceRows
    Write-DangoProfileJson (Join-Path $taskPorchC1Plan.EvidenceDirectory 'fixture-resource-summary.json') $taskPorchC1Resources -CreateNew
    $taskPorchC1After=Invoke-DangoPorchCrudC1Snapshot 'after'
    $taskPorchC1NewUsers=@(Assert-DangoPorchCrudC1OldGraph $taskPorchC1Before $taskPorchC1After)
    $scopeBytes=Read-DangoAccountSyncClosureBytes (Join-Path $taskPorchC1Plan.EvidenceDirectory 'scoped-inputs-evidence.json') 131072
    $taskPorchC1Scope=ConvertFrom-DangoPorchCrudC1StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($scopeBytes))
    Assert-DangoPorchCrudC1ScopeInputs $taskPorchC1Scope $taskPorchC1Reservation.OwnerId $taskPorchC1Facts.Epoch $taskPorchC1NewUsers
    $taskPorchC1ScopeValidated=$true
    $journeyPath=Join-Path $taskPorchC1Plan.EvidenceDirectory 'journey-evidence.json'
    $journeyBytes=Read-DangoAccountSyncClosureBytes $journeyPath 131072
    $taskPorchC1Journey=ConvertFrom-DangoPorchCrudC1StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($journeyBytes))
    Assert-DangoPorchCrudC1Journey $taskPorchC1Journey $taskPorchC1Scope
    $taskPorchC1JourneyValidated=$true
    $scopedSql=Get-DangoPorchCrudC1ScopedSql $taskPorchC1Scope
    $scopedArgs=@($taskPorchC1PsqlArgs[0..($taskPorchC1PsqlArgs.Count-2)])+$scopedSql
    $scoped=Invoke-DangoPorchCrudC1Tool 'scoped' $taskPorchC1Psql $scopedArgs $taskPorchC1PsqlEnvironment -OutputContract PorchCrudC1SmallJson
    if(-not $scoped.Receipt.OutputComplete){throw 'porch_c1_scoped_eof_unconfirmed'}
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan 'scoped-graph-evidence.json' $scoped.RawOutput
    Save-DangoPorchCrudC1RawEvidence $taskPorchC1Plan 'scoped.stderr.txt' $scoped.RawError
    if ($scoped.ExitCode -ne 0) { throw 'porch_c1_run_scoped_evidence_failed' }
    $taskPorchC1ScopedGraph=ConvertFrom-DangoPorchCrudC1StrictJson $scoped.RawOutput
    Assert-DangoPorchCrudC1ScopedGraph $taskPorchC1ScopedGraph
    Assert-DangoPorchCrudC1FrozenInputs
}catch{
    # Never emit arbitrary exception text: it can include credentials or a connection string.
    $taskPorchC1Failure=if ($_.Exception.Message -cmatch '^(porch_c1|accountsync|profile_run)_[a-z0-9_]+$') {$_.Exception.Message}
        else {'porch_c1_run_operation_failed'}
}finally{
    try {
        if ($null -ne $taskPorchC1StartAttempt -and $null -ne $taskPorchC1StartReturn) {
            $taskPorchC1StartupObservation=Observe-DangoProfileStartup $taskPorchC1Plan $taskPorchC1Facts.Port `
                $taskPorchC1StartControlExit $taskPorchC1StartTool $taskPorchC1StartAttempt
            if ($taskPorchC1StartupObservation.Verified) {$taskPorchC1Proof=$taskPorchC1StartupObservation.Proof}
        }
        if ($null -ne $taskPorchC1Proof -and $null -ne $taskPorchC1StartupObservation -and $taskPorchC1StartupObservation.Verified) {
            $current=Get-DangoProfilePostgresProof $taskPorchC1Plan $taskPorchC1Facts.Port $taskPorchC1Proof.ControlExitCode $taskPorchC1StartTool
            if ($current.Pid -eq $taskPorchC1Proof.Pid -and $current.CreatedAt -eq $taskPorchC1Proof.CreatedAt -and
                (Test-DangoProfilePostgresProof $current $taskPorchC1Postgres $taskPorchC1Plan.DataDirectory $taskPorchC1Facts.Port `
                    $taskPorchC1StartAttempt $taskPorchC1StartupObservation.ObservedUntil $taskPorchC1Plan)) {
                $taskPorchC1StopOwnershipVerified=$true
                $stopped=Invoke-DangoPorchCrudC1Tool 'stop' $taskPorchC1Control @('stop','-D',$taskPorchC1Plan.DataDirectory,'-m','fast','-w','-t','30') -TimeoutSeconds 40
                if($stopped.ExitCode -ne 0 -and $null -eq $taskPorchC1Failure){$taskPorchC1Failure='porch_c1_stop_failed'}
                $taskPorchC1PostgresProcessCleanup=Wait-DangoPorchCrudC1ProcessEnded $taskPorchC1Proof.Pid $taskPorchC1Proof.CreatedAt
                $taskPorchC1ShellBridgeCleanup=Wait-DangoPorchCrudC1ProcessEnded $taskPorchC1Proof.ShellBridge.Pid $taskPorchC1Proof.ShellBridge.CreatedAt
            }
        }elseif($null -eq $taskPorchC1StartAttempt){$taskPorchC1PostgresProcessCleanup=$true;$taskPorchC1ShellBridgeCleanup=$true}
    }catch{$taskPorchC1Cleanup=$false}
    $taskPorchC1ToolCleanup=@($taskPorchC1Tools|Where-Object {-not $_.CleanupConfirmed -or -not $_.ProcessDisposeConfirmed}).Count -eq 0
    $taskPorchC1OutputCleanup=@($taskPorchC1Tools|Where-Object {-not $_.OutputResourcesCleanupConfirmed}).Count -eq 0
    $taskPorchC1PidFileAbsent=-not [IO.File]::Exists((Join-Path $taskPorchC1Plan.DataDirectory 'postmaster.pid'))
    $taskPorchC1ListenerAbsent=-not(Test-DangoProfileListener $taskPorchC1Plan.DatabasePort)
    $taskPorchC1Cleanup=$taskPorchC1PostgresProcessCleanup -and $taskPorchC1ShellBridgeCleanup -and $taskPorchC1ToolCleanup -and
        $taskPorchC1OutputCleanup -and $taskPorchC1PidFileAbsent -and $taskPorchC1ListenerAbsent
    if ($null -eq $taskPorchC1Failure -and ($taskPorchC1Tools.Count -ne 10 -or $taskPorchC1ToolAttempts -ne 10)) {$taskPorchC1Failure='porch_c1_run_tool_sequence_incomplete'}
    $taskPorchC1Password=$null;$taskPorchC1Connection=$null;$taskPorchC1TestEnvironment=$null;$taskPorchC1PsqlEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskPorchC1Plan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskPorchC1Reservation.OwnerId;Stage=$taskPorchC1Plan.Stage;Failure=$taskPorchC1Failure;
        PostgreSqlCleanupConfirmed=$taskPorchC1Cleanup;PostgreSqlPid=if($null -ne $taskPorchC1Proof){$taskPorchC1Proof.Pid}else{0};
        PostgreSqlProcessCleanupConfirmed=$taskPorchC1PostgresProcessCleanup;ShellBridgeCleanupConfirmed=$taskPorchC1ShellBridgeCleanup;
        ToolProcessCleanupConfirmed=$taskPorchC1ToolCleanup;ToolAttempts=$taskPorchC1ToolAttempts;
        ToolOutputResourcesCleanupConfirmed=$taskPorchC1OutputCleanup;RecordedPort=if($null -ne $taskPorchC1Facts){$taskPorchC1Facts.Port}else{0};
        PostmasterPidFilePresent=[IO.File]::Exists((Join-Path $taskPorchC1Plan.DataDirectory 'postmaster.pid'));
        ListenerPresentOnRecordedPort=if($null -ne $taskPorchC1Facts){Test-DangoProfileListener $taskPorchC1Facts.Port}else{$null};
        ToolProcesses=@($taskPorchC1Tools);InputsFrozen=$taskPorchC1InputsFrozen;
        TestsPassed=($taskPorchC1Cleanup -and $null -eq $taskPorchC1Failure -and $null -ne $taskPorchC1Audit -and $null -ne $taskPorchC1Resources -and
            $null -ne $taskPorchC1ScopedGraph -and $null -ne $taskPorchC1Journey -and $taskPorchC1JourneyValidated -and $taskPorchC1ScopeValidated);
        JourneyValidated=$taskPorchC1JourneyValidated;
        ScopeValidated=$taskPorchC1ScopeValidated;
        AttemptEvidence=$taskPorchC1Audit;FixturesAttempted=$taskPorchC1TestsAttempted;FixtureResourceEvidence=$taskPorchC1Resources;
        FixtureNativeCleanupConfirmed=($null -ne $taskPorchC1Resources -and $taskPorchC1Resources.NativeCleanupConfirmed);
        StartupObservation=$taskPorchC1StartupObservation;MaximumHttpRequests=128;MinimumHttpStartIntervalMilliseconds=600;MaximumDurationMinutes=10;TestProcessTimeoutSeconds=300;
        WholeSuiteReserved=$true;RetryAllowed=$false;DataRetained=[IO.Directory]::Exists($taskPorchC1Plan.DataDirectory);
        KeysAndEpochRetained=([IO.Directory]::Exists((Join-Path $taskPorchC1Plan.StateDirectory 'b1tests-keys')) -and
            [IO.File]::Exists((Join-Path $taskPorchC1Plan.StateDirectory 'b1test-state.json')))}) -CreateNew
}
if (-not $taskPorchC1Cleanup) {throw 'porch_c1_run_cleanup_unconfirmed_no_retry'}
if (-not $taskPorchC1OutputCleanup) {throw 'porch_c1_run_output_cleanup_unconfirmed_no_retry'}
if ($taskPorchC1Failure) {throw $taskPorchC1Failure}
Write-Output 'porch_c1_run_one_journey_passed_data_retained'

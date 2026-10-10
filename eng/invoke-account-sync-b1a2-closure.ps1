[CmdletBinding()]
param([string]$ApprovalManifest,[string]$ApprovalManifestSha256,
    [switch]$PrepareApprovalManifest,[switch]$ExecuteApprovedPlan)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -Force
$taskClosurePlan=Get-DangoAccountSyncClosurePlan (Split-Path -Parent $PSScriptRoot)
if ($PrepareApprovalManifest -and $ExecuteApprovedPlan) {throw 'accountsync_closure_prepare_execute_conflict'}
if (-not $ExecuteApprovedPlan) {
    $inputs=if($PrepareApprovalManifest){New-DangoAccountSyncClosureApproval $taskClosurePlan $ApprovalManifest}
        else{Get-DangoAccountSyncClosureInputs $taskClosurePlan}
    $taskClosurePlan|Add-Member ReviewInputs $inputs;$taskClosurePlan;return
}
if (-not $IsWindows) {throw 'accountsync_closure_windows_required'}
$taskClosureApproved=Assert-DangoAccountSyncClosureApproval $taskClosurePlan $ApprovalManifest $ApprovalManifestSha256

function Assert-DangoAccountSyncClosureFrozenInputs {
    [void](Assert-DangoAccountSyncClosureApproval $taskClosurePlan $ApprovalManifest $ApprovalManifestSha256)
    foreach($path in $taskClosureExtraHashes.Keys) {
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $taskClosureExtraHashes[$path]) {
            throw 'accountsync_closure_frozen_input_changed'
        }
    }
}

function Invoke-DangoAccountSyncClosureTool {
    param([string]$Step,[string]$Executable,[string[]]$Arguments,[hashtable]$EnvironmentValues=@{},
        [int]$TimeoutSeconds=30,[string]$OutputContract='CompleteEof',[switch]$KillOwnedTree,[switch]$PreserveRawOutput)
    $sequence=@('version','status','start','scoped','stop')
    if ($taskClosureToolAttempts -ge $taskClosurePlan.MaximumToolProcesses -or $taskClosureStopAttempted -or
        ($Step -cne 'stop' -and ($taskClosureToolIndex -ge 4 -or $Step -cne $sequence[$taskClosureToolIndex])) -or
        ($Step -ceq 'stop' -and ($taskClosureToolIndex -lt 3 -or -not $taskClosureStopOwnershipVerified))) {
        throw 'accountsync_closure_tool_order_rejected'
    }
    Assert-DangoAccountSyncClosureToolContract $taskClosurePlan $Step $Executable $Arguments $taskClosurePlan.Repository `
        $EnvironmentValues $TimeoutSeconds ([bool]$KillOwnedTree) $OutputContract ([bool]$PreserveRawOutput)
    # A failure can always spend the one owned stop; it cannot launch another query or tool.
    if ($Step -cne 'stop') {Assert-DangoAccountSyncClosureFrozenInputs}
    if ($Step -ceq 'scoped') {
        if ($taskClosureQueryAttempted) {throw 'accountsync_closure_query_already_consumed'}
        Write-DangoProfileJson (Join-Path $taskClosurePlan.EvidenceDirectory 'query-attempt.json') ([pscustomobject]@{
            Version=1;OwnerId=$taskClosureReservation.OwnerId;Stage=$taskClosurePlan.Stage;SourceOwnerId=$taskClosurePlan.SourceOwnerId;
            ReadOnlyQuery=1;BusinessWriteAttempts=0;AtUtc=[DateTime]::UtcNow;RetryAllowed=$false}) -CreateNew
        $script:taskClosureQueryAttempted=$true
    }
    if ($Step -ceq 'stop') {$script:taskClosureStopAttempted=$true}
    $script:taskClosureToolAttempts++;$script:taskClosureToolIndex++
    $options=@{Executable=$Executable;Arguments=$Arguments;WorkingDirectory=$taskClosurePlan.Repository;
        EnvironmentValues=$EnvironmentValues;TimeoutSeconds=$TimeoutSeconds;KillOwnedTree=$KillOwnedTree;
        Receipts=$taskClosureTools;OutputContract=$OutputContract}
    if ($Step -ceq 'start') {$options.PostgresStartPlan=$taskClosurePlan;$options.PostgresStartPort=$taskClosurePlan.DatabasePort}
    if($PreserveRawOutput){$options.PreserveRawOutput=$true;$options.PostgresEvidencePlan=$taskClosurePlan;$options.PostgresEvidencePort=$taskClosurePlan.DatabasePort}
    return Invoke-DangoProfileProcess @options
}

function Test-DangoAccountSyncClosureProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    try {
        $process=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
        try{return $process.HasExited -or $process.StartTime.ToUniversalTime() -ne (ConvertTo-DangoProfileUtc $CreatedAt)}
        finally{$process.Dispose()}
    }catch [ArgumentException]{return $true}
    catch{return $false}
}

function Wait-DangoAccountSyncClosureProcessEnded {
    param([int]$ProcessIdValue,$CreatedAt)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.ElapsedMilliseconds -lt 3000) {
        if(Test-DangoAccountSyncClosureProcessEnded $ProcessIdValue $CreatedAt){return $true}
        [Threading.Thread]::Sleep(100)
    }
    return Test-DangoAccountSyncClosureProcessEnded $ProcessIdValue $CreatedAt
}

# The durable directory and reservation precede every original state and credential read.
$taskClosureReservation=New-DangoAccountSyncClosureReservation $taskClosurePlan
Write-DangoProfileJson (Join-Path $taskClosurePlan.EvidenceDirectory 'approved-inputs.json') $taskClosureApproved -CreateNew
$taskClosureTools=[Collections.Generic.List[object]]::new();$taskClosureExtraHashes=@{}
$taskClosureToolAttempts=0;$taskClosureToolIndex=0;$taskClosureQueryAttempted=$false
$taskClosureStopAttempted=$false;$taskClosureStopOwnershipVerified=$false
$taskClosureFailure=$null;$taskClosureFacts=$null;$taskClosureFactsSaved=$false;$taskClosureInputsFrozen=$false
$taskClosurePassword=$null;$taskClosureQueryEnvironment=$null
$taskClosureProof=$null;$taskClosureStartAttempt=$null;$taskClosureStartReturn=$null;$taskClosureStartTool=$null
$taskClosureControlExit=-1;$taskClosureObservation=$null;$taskClosurePostgresCleanup=$false;$taskClosureCmdCleanup=$false
$taskClosureToolCleanup=$false;$taskClosureOutputCleanup=$false;$taskClosurePidFileAbsent=$false;$taskClosureListenerAbsent=$false
try {
    Assert-DangoAccountSyncClosurePrivatePath $taskClosurePlan.StateDirectory $true
    foreach($name in @('state.json','credential.dpapi')) {
        $path=Join-Path $taskClosurePlan.StateDirectory $name
        if(-not [IO.File]::Exists($path)){throw 'accountsync_closure_existing_state_required'}
        Assert-DangoAccountSyncClosurePrivatePath $path
    }
    Assert-DangoProfileRegularPath $taskClosurePlan.DataDirectory
    if(-not [IO.Directory]::Exists($taskClosurePlan.DataDirectory) -or
        [IO.File]::Exists((Join-Path $taskClosurePlan.DataDirectory 'postmaster.pid'))) {throw 'accountsync_closure_existing_postmaster_rejected'}
    $taskClosureStatePath=Join-Path $taskClosurePlan.StateDirectory 'state.json'
    $stateBytes=Read-DangoAccountSyncClosureBytes $taskClosureStatePath 8192
    [void](Assert-DangoAccountSyncClosureState $taskClosurePlan ([Text.UTF8Encoding]::new($false,$true).GetString($stateBytes)))
    if(Test-DangoProfileListener $taskClosurePlan.DatabasePort){throw 'accountsync_closure_existing_listener_rejected'}
    $taskClosurePostgres=Join-Path $taskClosurePlan.PostgresBinDirectory 'postgres.exe'
    $taskClosureControl=Join-Path $taskClosurePlan.PostgresBinDirectory 'pg_ctl.exe'
    $taskClosurePsql=Join-Path $taskClosurePlan.PostgresBinDirectory 'psql.exe'
    foreach($path in @($taskClosurePostgres,$taskClosureControl,$taskClosurePsql,$taskClosureStatePath)) {
        Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){throw 'accountsync_closure_required_input_missing'}
        $taskClosureExtraHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    Write-DangoProfileJson (Join-Path $taskClosurePlan.EvidenceDirectory 'input-hashes.json') $taskClosureExtraHashes -CreateNew
    Assert-DangoAccountSyncClosureFrozenInputs;$taskClosureInputsFrozen=$true
    $version=Invoke-DangoAccountSyncClosureTool 'version' $taskClosurePostgres @('--version')
    if($version.ExitCode -ne 0 -or $version.Output -cnotmatch '^postgres \(PostgreSQL\) 18\.') {throw 'accountsync_closure_postgres18_required'}
    $status=Invoke-DangoAccountSyncClosureTool 'status' $taskClosureControl @('status','-D',$taskClosurePlan.DataDirectory)
    if($status.ExitCode -ne 3){throw 'accountsync_closure_existing_postgres_rejected'}
    $cipher=$null;$plain=$null;$decoded=$null
    try {
        $cipher=Read-DangoAccountSyncClosureBytes (Join-Path $taskClosurePlan.StateDirectory 'credential.dpapi') 8192
        $plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $taskClosurePassword=[Text.UTF8Encoding]::new($false,$true).GetString($plain)
        if($taskClosurePassword -cnotmatch '^[A-Za-z0-9+/]{43}=$'){throw 'accountsync_closure_credential_invalid'}
        $decoded=[Convert]::FromBase64String($taskClosurePassword)
        if($decoded.Length -ne 32 -or [Convert]::ToBase64String($decoded) -cne $taskClosurePassword){throw 'accountsync_closure_credential_invalid'}
    }finally{foreach($bytes in @($cipher,$plain,$decoded)){if($null -ne $bytes){[Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)}}}
    $taskClosureStartAttempt=[DateTime]::UtcNow;$startIndex=$taskClosureTools.Count
    try {
        $started=Invoke-DangoAccountSyncClosureTool 'start' $taskClosureControl @('start','-D',$taskClosurePlan.DataDirectory,'-l',
            (Join-Path $taskClosurePlan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($taskClosurePlan.DatabasePort)",'-w','-t','30') `
            -TimeoutSeconds 40 -OutputContract PostgresStartIgnored
        $taskClosureControlExit=$started.ExitCode
    }finally {
        $taskClosureStartReturn=[DateTime]::UtcNow
        if($taskClosureTools.Count -gt $startIndex){$taskClosureStartTool=$taskClosureTools[$startIndex]
            if($taskClosureStartTool.ExitCodeRecorded){$taskClosureControlExit=$taskClosureStartTool.ExitCode}}
    }
    $taskClosureProof=Get-DangoProfilePostgresProof $taskClosurePlan $taskClosurePlan.DatabasePort $taskClosureControlExit $taskClosureStartTool
    if(-not(Test-DangoProfilePostgresProof $taskClosureProof $taskClosurePostgres $taskClosurePlan.DataDirectory $taskClosurePlan.DatabasePort `
        $taskClosureStartAttempt $taskClosureStartReturn $taskClosurePlan)){throw 'accountsync_closure_start_ownership_unconfirmed'}
    if($taskClosureControlExit -ne 0){throw 'accountsync_closure_postgres_start_failed'}
    $taskClosureQueryEnvironment=@{PGPASSWORD=$taskClosurePassword;PGCLIENTENCODING='UTF8'}
    $query=Invoke-DangoAccountSyncClosureTool 'scoped' $taskClosurePsql @('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1',
        '-h','127.0.0.1','-p',[string]$taskClosurePlan.DatabasePort,'-U',$taskClosurePlan.Role,'-d',$taskClosurePlan.Database,
        '-c',(Get-DangoAccountSyncClosureSql $taskClosurePlan)) $taskClosureQueryEnvironment -PreserveRawOutput
    if(-not $query.Receipt.OutputComplete -or -not $query.Receipt.OutputResourcesCleanupConfirmed){throw 'accountsync_closure_query_eof_unconfirmed'}
    # Retain full EOF-complete output before exit, JSON shape, target or business comparisons.
    Save-DangoAccountSyncClosureFacts $taskClosurePlan $query.RawOutput
    Save-DangoAccountSyncClosureFacts $taskClosurePlan $query.RawError -FileName 'query-stderr.txt'
    $taskClosureFactsSaved=$true
    if($query.ExitCode -ne 0){throw 'accountsync_closure_query_failed'}
    $taskClosureFacts=Assert-DangoAccountSyncClosureEvidence $taskClosurePlan $query.RawOutput
    Write-DangoProfileJson (Join-Path $taskClosurePlan.EvidenceDirectory 'validation-result.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskClosureReservation.OwnerId;TargetAndGraphMatch=$true;ReadOnlyQueries=1;BusinessWriteAttempts=0}) -CreateNew
    Assert-DangoAccountSyncClosureFrozenInputs
}catch{
    $taskClosureFailure=if($_.Exception.Message -cmatch '^(accountsync_closure|profile_run)_[a-z0-9_]+$'){$_.Exception.Message}
        else{'accountsync_closure_operation_failed'}
}finally{
    try {
        if($null -ne $taskClosureStartAttempt -and $null -ne $taskClosureStartReturn){
            $taskClosureObservation=Observe-DangoProfileStartup $taskClosurePlan $taskClosurePlan.DatabasePort `
                $taskClosureControlExit $taskClosureStartTool $taskClosureStartAttempt
            if($taskClosureObservation.Verified){$taskClosureProof=$taskClosureObservation.Proof}
        }
        if($null -ne $taskClosureProof -and $null -ne $taskClosureObservation -and $taskClosureObservation.Verified){
            $current=Get-DangoProfilePostgresProof $taskClosurePlan $taskClosurePlan.DatabasePort $taskClosureProof.ControlExitCode $taskClosureStartTool
            if($current.Pid -eq $taskClosureProof.Pid -and $current.CreatedAt -eq $taskClosureProof.CreatedAt -and
                (Test-DangoProfilePostgresProof $current $taskClosurePostgres $taskClosurePlan.DataDirectory $taskClosurePlan.DatabasePort `
                    $taskClosureStartAttempt $taskClosureObservation.ObservedUntil $taskClosurePlan)){
                $taskClosureStopOwnershipVerified=$true
                try {
                    $stopped=Invoke-DangoAccountSyncClosureTool 'stop' $taskClosureControl @('stop','-D',$taskClosurePlan.DataDirectory,'-m','fast','-w','-t','30') -TimeoutSeconds 40
                    if($stopped.ExitCode -ne 0 -and $null -eq $taskClosureFailure){$taskClosureFailure='accountsync_closure_stop_failed'}
                }catch{if($null -eq $taskClosureFailure){$taskClosureFailure='accountsync_closure_stop_failed'}}
                $taskClosurePostgresCleanup=Wait-DangoAccountSyncClosureProcessEnded $taskClosureProof.Pid $taskClosureProof.CreatedAt
                $taskClosureCmdCleanup=Wait-DangoAccountSyncClosureProcessEnded $taskClosureProof.ShellBridge.Pid $taskClosureProof.ShellBridge.CreatedAt
            }
        }elseif($null -eq $taskClosureStartAttempt){$taskClosurePostgresCleanup=$true;$taskClosureCmdCleanup=$true}
    }catch{$taskClosurePostgresCleanup=$false;$taskClosureCmdCleanup=$false}
    $taskClosureToolCleanup=@($taskClosureTools|Where-Object{-not $_.CleanupConfirmed -or -not $_.ProcessDisposeConfirmed}).Count -eq 0
    $taskClosureOutputCleanup=@($taskClosureTools|Where-Object{-not $_.OutputResourcesCleanupConfirmed}).Count -eq 0
    $taskClosurePidFileAbsent=-not [IO.File]::Exists((Join-Path $taskClosurePlan.DataDirectory 'postmaster.pid'))
    $taskClosureListenerAbsent=-not (Test-DangoProfileListener $taskClosurePlan.DatabasePort)
    if($null -eq $taskClosureFailure -and ($taskClosureToolAttempts -ne 5 -or $taskClosureTools.Count -ne 5)){
        $taskClosureFailure='accountsync_closure_tool_sequence_incomplete'
    }
    $taskClosurePassword=$null;$taskClosureQueryEnvironment=$null
    Write-DangoProfileJson (Join-Path $taskClosurePlan.EvidenceDirectory 'resource-receipt.json') ([pscustomobject]@{
        Version=1;OwnerId=$taskClosureReservation.OwnerId;Stage=$taskClosurePlan.Stage;Failure=$taskClosureFailure;
        ClosurePassed=($null -eq $taskClosureFailure -and $null -ne $taskClosureFacts -and $taskClosureFactsSaved -and
            $taskClosurePostgresCleanup -and $taskClosureCmdCleanup -and $taskClosureToolCleanup -and $taskClosureOutputCleanup -and
            $taskClosurePidFileAbsent -and $taskClosureListenerAbsent);
        FactsSaved=$taskClosureFactsSaved;QueryAttempted=$taskClosureQueryAttempted;
        ReadOnlyQueriesAttempted=if($taskClosureQueryAttempted){1}else{0};BusinessWriteAttempts=0;MigrationOperations=0;
        ToolAttempts=$taskClosureToolAttempts;ToolProcesses=@($taskClosureTools);InputsFrozen=$taskClosureInputsFrozen;
        PostgreSqlProcessCleanupConfirmed=$taskClosurePostgresCleanup;ShellBridgeCleanupConfirmed=$taskClosureCmdCleanup;
        ToolProcessCleanupConfirmed=$taskClosureToolCleanup;ToolOutputResourcesCleanupConfirmed=$taskClosureOutputCleanup;
        PostmasterPidFileAbsent=$taskClosurePidFileAbsent;ListenerAbsentOnRecordedPort=$taskClosureListenerAbsent;
        RecordedPort=$taskClosurePlan.DatabasePort;PostgreSqlPid=if($null -ne $taskClosureProof){$taskClosureProof.Pid}else{0};
        StartupObservation=$taskClosureObservation;StopAttempted=$taskClosureStopAttempted;RetryAllowed=$false;
        DataRetained=[IO.Directory]::Exists($taskClosurePlan.DataDirectory);Certificates=0;HttpsHosts=0;DesktopHosts=0}) -CreateNew
}
if(-not($taskClosurePostgresCleanup -and $taskClosureCmdCleanup -and $taskClosureToolCleanup -and $taskClosureOutputCleanup -and
    $taskClosurePidFileAbsent -and $taskClosureListenerAbsent)){throw 'accountsync_closure_cleanup_unconfirmed_no_retry'}
if($taskClosureFailure){throw $taskClosureFailure}
Write-Output 'accountsync_closure_one_readonly_query_passed_data_retained'

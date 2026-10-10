[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountSyncB1bPostgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-sync-b1b1-tests.ps1'))
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -Force -DisableNameChecking
$taskModule=Import-Module ([IO.Path]::GetFullPath($ModulePath)) -Force -PassThru -DisableNameChecking
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskSourceHashes=[ordered]@{};foreach($path in @([IO.Path]::GetFullPath($ModulePath),[IO.Path]::GetFullPath($RunnerPath),
    (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'),$PSCommandPath)){$taskSourceHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
$taskSandbox=Join-Path $taskRepository ('artifacts/account-sync-b1b1-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$checks=[Collections.Generic.List[object]]::new();$owner=[Guid]'dddddddd-dddd-dddd-dddd-dddddddddddd'
$user=[Guid]'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';$epoch=[Guid]'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
$pet=[Guid]'cccccccc-cccc-cccc-cccc-cccccccccccc';$room=[Guid]'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
$binaryOriginal=& $taskModule {(Get-Command Get-DangoAccountSyncB1bBinaryInputs).ScriptBlock}
& $taskModule {function script:Get-DangoAccountSyncB1bBinaryInputs {param($Plan)[pscustomobject]@{
    Hashes=[ordered]@{'postgres.exe'=('A'*64);'pg_ctl.exe'=('B'*64);'psql.exe'=('C'*64)};MissingInputs=@()}}}
function Check([string]$TestName,[scriptblock]$Body){$passed=$false;$failure=$null
    try{$passed=[bool](& $Body)}catch{$failure=$_.Exception.Message}
    $checks.Add([pscustomobject]@{Name=$TestName;Passed=$passed;Failure=$failure})}
function Reject([scriptblock]$Body,[string]$Code){try{[void](& $Body)}catch{
    if($_.Exception.Message.Contains($Code,[StringComparison]::Ordinal)){return $true};throw ('b1b_offline_wrong_code:'+ $_.Exception.Message)}
    throw 'b1b_offline_expected_rejection_missing'}
function Plan {Get-DangoAccountSyncB1bPlan $taskSandbox}
function Clone($Value){$Value|ConvertTo-Json -Depth 32 -Compress|ConvertFrom-Json -Depth 32}
function Journey {
    $names=@('CAS','LostAcceptedResponse','LostResponseThenNewerCommit','UnforwardedAttempt','HostRecovery');$attempts=@(2,1,2,2,1);$writes=@(1,1,2,1,1);$versions=@(2,3,5,6,7)
    $cases=@();for($i=0;$i -lt 5;$i++){$cases+=([pscustomobject]@{Case=$names[$i];Attempts=$attempts[$i];SuccessfulWrites=$writes[$i];FinalVersion=$versions[$i];Passed=$true})}
    [pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Passed=$true;Final=[pscustomobject]@{UserId=$user.ToString('D');PetId=$pet.ToString('D');
        Nickname='异常同步团子';ProfileVersion=2;ConfigurationVersion=7;Sessions=2;Revoked=2;Attempts=0;MailState='Pending';DefaultRoomId=$room.ToString('D');RecoveryEpoch=$epoch.ToString('D')};
        ConfigurationAttempts=8;ConfigurationSuccessfulWrites=6;LoginWrites=2;ProfileWrites=1;LogoutWrites=2;HttpsHosts=2;Certificates=1;HttpRequests=64;
        DurationSeconds=12.5;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;ActualApplicationHost=$false;ActualWpfFileAdapter=$true;Exact5Cases=$true;Cases=$cases}
}
function Attempts {$rows=@();$sequence=0;$plan=Plan
    foreach($operation in $plan.Budgets.Keys){for($used=1;$used -le $plan.Budgets[$operation];$used++){$sequence++
        $rows+=([pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Sequence=$sequence;Operation=$operation;Used=$used;AtUtc='2026-10-11T00:00:00Z'})}}
    return ,$rows}
function Resources {$phases=@('TLSCreated','HostStarted','HostDisposed','HostStarted','HostDisposed','TLSDisposed');$rows=@()
    for($i=0;$i -lt 6;$i++){$facts=switch -CaseSensitive ($phases[$i]){TLSCreated{[pscustomobject]@{Port=63123;CertificateSha256=('A'*64)}};
        HostStarted{[pscustomobject]@{Index=[int](($i+1)/2);Port=63123;WorkersDisabled=$true}};
        HostDisposed{[pscustomobject]@{Index=[int]($i/2);Stopped=$true;Disposed=$true;InFlightRequests=0;LifecycleTasksSettled=$true}};
        TLSDisposed{[pscustomobject]@{NativeKeyIdentityCaptured=$true;NativeCleanupConfirmed=$true;NativeProbeFailed=$false}}}
        $rows+=([pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Sequence=$i+1;Phase=$phases[$i];Facts=$facts})};return ,$rows}
function FaultEvidence {
    $patches=@();$devices=@('primary','secondary','primary','primary','secondary','primary','primary','primary')
    $versions=@(1,1,2,3,4,5,5,6);$eyes=@(111,114,118,125,128,137,137,139);$statuses=@(200,409,200,200,200,$null,200,200)
    $accepted=@(2,$null,3,4,5,$null,6,7);$digests=@('A','B','C','D','E','F','F','1')
    for($i=0;$i -lt 8;$i++){$patches+=([pscustomobject]@{Sequence=$i+1;Device=$devices[$i];ExpectedVersion=$versions[$i];EyeSize=$eyes[$i];
        ConfigurationSha256=($digests[$i]*64);Forwarded=($i -ne 5);ResponseStatus=$statuses[$i];AcceptedVersion=$accepted[$i]})}
    $events=@();$sequence=0
    foreach($entry in @(
        @('cas','armed',$null,$null,$null),@('cas','patch-forwarding','primary',$null,$null),@('cas','patch-forwarding','secondary',$null,$null),
        @('cas','connection-open-started',$null,$null,$null),@('cas','connection-open-started',$null,$null,$null),
        @('cas','before-locks-arrived',$null,301,$null),@('cas','before-locks-arrived',$null,302,$null),
        @('cas','before-locks-released',$null,302,$null),@('cas','before-locks-released',$null,301,$null),
        @('cas','patch-response','secondary',$null,409),@('cas','patch-response','primary',$null,200),
        @('drop-accepted','armed',$null,$null,$null),@('drop-accepted','accepted-response-captured','primary',$null,200),
        @('drop-accepted','accepted-response-discarded','primary',$null,$null),@('hold-accepted','armed',$null,$null,$null),
        @('hold-accepted','accepted-response-captured','primary',$null,200),@('hold-accepted','accepted-response-released','primary',$null,$null),
        @('hold-accepted','accepted-response-discarded','primary',$null,$null),@('drop-before-forward','armed',$null,$null,$null),
        @('drop-before-forward','reserved-patch-not-forwarded','primary',$null,$null))){$sequence++
        $events+=([pscustomobject]@{Sequence=$sequence;Phase=$entry[0];Event=$entry[1];Device=$entry[2];BackendPid=$entry[3];StatusCode=$entry[4]})}
    [pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Completed=$true;CoordinatorsStopped=$true;DispatcherOperationsSettled=$true;
        Controls=[pscustomobject]@{ActivePhase=$null;Failed=$false;Disposed=$false;ActiveOperations=0;PendingConnectionTasks=0;CasArrivals=2;CasCompleted=2;
            BackendPids=@(301,302);AcceptedDropHits=1;AcceptedHoldHits=1;BeforeForwardDropHits=1;Events=$events};PatchAttempts=$patches}
}
function Archives {
    $steps=@('cas-primary','cas-secondary','cas-loser-follow','lost-accepted-target-satisfied','lost-accepted-secondary-follow',
        'lost-accepted-superseded','newer-secondary-save','superseded-primary-follow','unforwarded-original-version-retry',
        'retried-secondary-follow','host-recovery-newest-save','host-recovery-secondary-follow')
    $devices=@('primary','secondary','secondary','primary','secondary','primary','secondary','primary','primary','secondary','primary','secondary')
    $versions=@(1,1,2,2,3,3,4,5,5,6,6,7);$observed=@(2,2,2,3,3,5,5,5,6,6,7,7)
    $choices=@(0,0,1,0,1,0,0,1,0,1,0,1);$outcomes=@(1,3,0,2,0,3,1,0,1,0,1,0)
    $locals=@('A','B','B','C','A','D','E','D','F','E','1','F');$clouds=@('0','0','A','A','C','C','D','E','E','F','F','1')
    $rows=@();for($i=0;$i -lt 12;$i++){$rows+=([pscustomobject]@{Device=$devices[$i];Step=$steps[$i];JournalId=[Guid]::NewGuid().ToString('D');
        LocalOriginalSha256=('2'*64);LocalConfigurationSha256=($locals[$i]*64);CloudConfigurationSha256=($clouds[$i]*64);
        ConfigurationVersion=$versions[$i];Choice=$choices[$i];Outcome=$outcomes[$i];ObservedCloudVersion=$observed[$i];
        FailureCode=$(if($outcomes[$i] -eq 3){'pet_configuration_conflict'}else{$null})})}
    [pscustomobject]@{Count=12;ExactOriginalsAndReceipts=$true;UserId=$user.ToString('D');PetId=$pet.ToString('D');Archives=$rows}
}
function FakeProcess {
    $fake=[pscustomobject]@{Id=40000;StartTime=[DateTime]::UtcNow;ExitTime=[DateTime]::UtcNow;HasExited=$true;ExitCode=1;
        StandardInput=[IO.StringWriter]::new();StandardOutput=[IO.StringReader]::new("  {}`r`n ");
        StandardError=[IO.StringReader]::new("ERROR: fictional_failure`r`n");Disposed=$false}
    $fake|Add-Member ScriptMethod Start {return $true}
    $fake|Add-Member ScriptMethod WaitForExit {param($Milliseconds)return $true}
    $fake|Add-Member ScriptMethod Kill {param($Tree)}
    $fake|Add-Member ScriptMethod Dispose {$this.Disposed=$true}
    return $fake
}
function RawHelperOptions([string]$Mode='AccountSyncB1bSmallJson',[string]$Kind='scoped'){
    $contract=EvidenceContract $Mode $Kind
    @{Executable=$contract.Executable;Arguments=$contract.Arguments;WorkingDirectory=$contract.WorkingDirectory;
        EnvironmentValues=$contract.EnvironmentValues;TimeoutSeconds=30;OutputContract=$Mode;
        PostgresEvidencePlan=$contract.Plan;PostgresEvidencePort=63237}
}
function Snapshot {
    $plan=Plan;$counts=[ordered]@{};$digests=[ordered]@{}
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
        'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')){
        $counts[$key]=1;$digests[$key]=@(('a'*64))}
    [pscustomobject]@{database=$plan.Database;role=$plan.Role;version=180001;dataDirectory=$plan.DataDirectory;listenAddresses='127.0.0.1';migrations=@($plan.PriorMigrations);
        userIds=@('11111111-1111-1111-1111-111111111111');profiles=@([pscustomobject]@{id='11111111-1111-1111-1111-111111111111';nicknameIsNull=$false;version=3});
        counts=[pscustomobject]$counts;digests=[pscustomobject]$digests}
}
function AfterSnapshot {$e=Snapshot
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')){$e.counts.$key=2;$e.digests.$key=@(('a'*64),('b'*64))}
    $e.counts.sessions=3;$e.digests.sessions=@(('a'*64),('b'*64),('c'*64));$e.userIds+=($user.ToString('D'))
    $e.profiles+=([pscustomobject]@{id=$user.ToString('D');nicknameIsNull=$false;version=2});return $e}
function EvidenceContract([string]$Mode='AccountSyncB1bSmallJson',[string]$Kind='scoped'){
    $plan=Plan;$sql=switch($Kind){full{Get-DangoAccountSyncDatabaseSql};size{Get-DangoAccountSyncDatabaseSql -MeasureOnly};scoped{Get-DangoAccountSyncB1bScopedSql $user $epoch $owner $pet $room}}
    @{Executable=(Join-Path $plan.PostgresBinDirectory 'psql.exe');Arguments=(Get-DangoAccountSyncB1bPsqlArguments $plan $sql);
        WorkingDirectory=$plan.Repository;Plan=$plan;Port=63237;EnvironmentValues=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};KillOwnedTree=$false;OutputContract=$Mode}
}
function Fixture([string]$Relative,[string]$Content='fictional source'){$path=Join-Path $taskSandbox $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path));[IO.File]::WriteAllText($path,$Content,[Text.UTF8Encoding]::new($false));return $path}
try {
    Check fixed_scope {$plan=Plan;[void](Assert-DangoAccountSyncB1bFixedPlan $plan);$plan.Stage -ceq 'AccountSyncB1b1' -and
        [IO.Path]::GetFileName($plan.EvidenceDirectory) -ceq 'run20261011accountsyncb1b1' -and $plan.ApprovalVariable -ceq 'DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED' -and
        $plan.DatabasePort -eq 63237 -and $plan.MaximumToolProcesses -eq 10 -and $plan.MaximumHttpRequests -eq 128 -and
        ($plan.Budgets.Values|Measure-Object -Sum).Sum -eq 17 -and $plan.DesktopHosts -eq 0 -and $plan.HttpsHosts -eq 2 -and $plan.Certificates -eq 1 -and -not $plan.RetryAllowed}
    Check shared_dispatch {(Get-DangoProfileFixedPlan (Plan)).Stage -ceq 'AccountSyncB1b1'}
    foreach($property in (Plan).PSObject.Properties){if($property.Name -ceq 'Repository'){continue};$field=$property.Name
        Check ('plan_changed_'+$field){$plan=Plan
            if($plan.$field -is [bool]){$plan.$field=-not $plan.$field}elseif($plan.$field -is [int]){$plan.$field++}
            elseif($plan.$field -is [Array]){$plan.$field[0]+='_bad'}elseif($plan.$field -is [Collections.IDictionary]){$plan.$field.Configuration++}else{$plan.$field+='_bad'}
            Reject {Assert-DangoAccountSyncB1bFixedPlan $plan} 'accountsync_b1b_fixed_plan_rejected'}}
    Check plan_extra {$plan=Plan;$plan|Add-Member Extra 1;Reject {Assert-DangoAccountSyncB1bFixedPlan $plan} 'accountsync_b1b_fixed_plan_rejected'}
    Check plan_missing {$plan=Plan;$plan.PSObject.Properties.Remove('DesktopHosts');Reject {Assert-DangoAccountSyncB1bFixedPlan $plan} 'accountsync_b1b_fixed_plan_rejected'}
    foreach($field in @('Stage','Database','Role','Filter','ApprovalVariable','TrxFileName')){Check ('plan_nul_'+$field){$plan=Plan;$plan.$field+=[char]0
        Reject {Assert-DangoAccountSyncB1bFixedPlan $plan} 'accountsync_b1b_fixed_plan_rejected'}}
    Check scoped_five_ids_ascii {$sql=Get-DangoAccountSyncB1bScopedSql $user $epoch $owner $pet $room
        $sql -cnotmatch '[^\x00-\x7F]' -and $sql.Contains("p.id='$pet'::uuid") -and $sql.Contains("r.id='$room'::uuid") -and
            $sql.Contains("nickname=U&'\5F02\5E38\540C\6B65\56E2\5B50'") -and $sql.Contains('profile_version=2') -and
            $sql.Contains('p.configuration_version=7') -and $sql.Contains('::numeric=139') -and @([regex]::Matches($sql,"'[0-9a-f-]{36}'::uuid")).Count -eq 5}
    foreach($field in @('user','epoch','owner','pet','room')){Check ('scoped_empty_'+$field){$ids=@($user,$epoch,$owner,$pet,$room)
        $index=@('user','epoch','owner','pet','room').IndexOf($field);$ids[$index]=[Guid]::Empty
        Reject {Get-DangoAccountSyncB1bScopedSql $ids[0] $ids[1] $ids[2] $ids[3] $ids[4]} 'scoped_ids_rejected'}}
    Check valid_journey {$e=Journey;Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch;$true}
    Check journey_real_duration_decimal_survives_strict_json {$e=ConvertFrom-DangoAccountSyncB1bStrictJson ((Journey)|ConvertTo-Json -Depth 16);Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch;$true}
    foreach($field in (Journey).PSObject.Properties.Name){if($field -cin @('Final','Cases')){continue}
        Check ('journey_changed_'+$field){$e=Journey
            if($field -ceq 'HttpRequests'){$e.$field=129}elseif($e.$field -is [bool]){$e.$field=-not $e.$field}elseif($e.$field -is [int]){$e.$field++}
            elseif($field -ceq 'DurationSeconds'){$e.$field=600}else{$e.$field+='_bad'}
            Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_rejected'}}
    foreach($field in (Journey).Final.PSObject.Properties.Name){Check ('journey_final_changed_'+$field){$e=Journey
        if($e.Final.$field -is [int]){$e.Final.$field++}else{$e.Final.$field+='_bad'}
        Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_rejected'}}
    foreach($type in @('integer','boolean','float','null','nan','infinity','negative')){Check ('journey_bad_type_'+$type){$e=Journey
        switch($type){integer{$e.ConfigurationAttempts='8'};boolean{$e.Exact5Cases='True'};float{$e.ConfigurationAttempts=8.0};null{$e.HttpRequests=$null};
            nan{$e.DurationSeconds=[double]::NaN};infinity{$e.DurationSeconds=[double]::PositiveInfinity};negative{$e.DurationSeconds=-1}}
        Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_rejected'}}
    foreach($at in 0..4){foreach($field in @('Case','Attempts','SuccessfulWrites','FinalVersion','Passed')){Check ('case_changed_'+$at+'_'+$field){$e=Journey
        if($field -ceq 'Passed'){$e.Cases[$at].Passed=$false}elseif($field -ceq 'Case'){$e.Cases[$at].Case='bad'}else{$e.Cases[$at].$field++}
        Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_case_rejected'}}}
    Check valid_attempts {(Assert-DangoAccountSyncB1bAttempts (Plan) $owner (Attempts)).Total -eq 17}
    foreach($at in 0..16){Check ('attempt_sequence_'+$at){$rows=Attempts;$rows[$at].Sequence=0;Reject {Assert-DangoAccountSyncB1bAttempts (Plan) $owner $rows} 'accountsync_b1b_attempt_identity_rejected'}
        Check ('attempt_used_'+$at){$rows=Attempts;$rows[$at].Used++;Reject {Assert-DangoAccountSyncB1bAttempts (Plan) $owner $rows} 'accountsync_b1b_attempt_budget_rejected'}}
    Check attempt_extra {Reject {Assert-DangoAccountSyncB1bAttempts (Plan) $owner (@(Attempts)+@(Attempts)[0])} 'accountsync_b1b_attempt_count_rejected'}
    Check attempt_old_owner {$rows=Attempts;$rows[0].OwnerId=[Guid]::NewGuid().ToString('D');Reject {Assert-DangoAccountSyncB1bAttempts (Plan) $owner $rows} 'accountsync_b1b_attempt_identity_rejected'}
    Check valid_resources {(Assert-DangoAccountSyncB1bResources $owner (Resources)).NativeCleanupConfirmed}
    foreach($at in 0..5){Check ('resource_order_'+$at){$rows=Resources;$rows[$at].Sequence=0;Reject {Assert-DangoAccountSyncB1bResources $owner $rows} 'accountsync_b1b_resource_order_rejected'}
        foreach($field in (Resources)[$at].Facts.PSObject.Properties.Name){Check ('resource_fact_'+$at+'_'+$field){$rows=Resources
            if($rows[$at].Facts.$field -is [bool]){$rows[$at].Facts.$field=-not $rows[$at].Facts.$field}elseif($rows[$at].Facts.$field -is [int]){$rows[$at].Facts.$field++}else{$rows[$at].Facts.$field='bad'}
            Reject {Assert-DangoAccountSyncB1bResources $owner $rows} 'accountsync_b1b_'}}}
    Check graph_valid_delta {[void](Assert-DangoAccountSyncB1bTarget (Plan) (Snapshot));(Assert-DangoAccountSyncB1bOldGraph (Snapshot) (AfterSnapshot)) -eq $user}
    foreach($key in (Snapshot).digests.PSObject.Properties.Name){Check ('old_row_changed_'+$key){$after=AfterSnapshot;$after.digests.$key[0]='f'*64
        Reject {Assert-DangoAccountSyncB1bOldGraph (Snapshot) $after} 'profile_run_old_graph_changed'}}
    foreach($key in @('accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts','audit','outbox','leases','codes','bans','claims','logins','tokens')){
        Check ('unapproved_new_row_'+$key){$after=AfterSnapshot;$after.counts.$key++;$after.digests.$key+=('b'*64)
            Reject {Assert-DangoAccountSyncB1bOldGraph (Snapshot) $after} 'profile_run_unapproved_graph_created'}}
    Check multiset_duplicate_preservation {$before=Snapshot;$after=AfterSnapshot;$before.counts.claims=2;$before.digests.claims=@(('a'*64),('a'*64));$after.counts.claims=2;$after.digests.claims=@(('a'*64),('b'*64))
        Reject {Assert-DangoAccountSyncB1bOldGraph $before $after} 'profile_run_old_graph_changed'}
    foreach($kind in @('full','size','scoped')){$mode=if($kind -ceq 'full'){'AccountSyncB1bEvidenceJson'}else{'AccountSyncB1bSmallJson'}
        Check ('evidence_contract_valid_'+$kind){$contract=EvidenceContract $mode $kind;Assert-DangoAccountSyncB1bEvidenceContract @contract;$true}
        foreach($index in 0..16){$at=$index;Check ('evidence_argv_'+$kind+'_'+$at){$contract=EvidenceContract $mode $kind;$contract.Arguments[$at]+='_bad'
            Reject {Assert-DangoAccountSyncB1bEvidenceContract @contract} 'accountsync_b1b_evidence_contract_rejected'}}}
    foreach($variant in @('old_stage','wrong_port','extra_env','latin1','arbitrary_sql','nonascii','nul','overlong','extra_argv')){Check ('evidence_variant_'+$variant){$contract=EvidenceContract
        switch($variant){old_stage{$contract.Plan=Get-DangoAccountSyncPlan $taskSandbox};wrong_port{$contract.Port=5432};extra_env{$contract.EnvironmentValues.EXTRA='1'};
            latin1{$contract.EnvironmentValues.PGCLIENTENCODING='LATIN1'};arbitrary_sql{$contract.Arguments[16]='SELECT 1;'};nonascii{$contract.Arguments[16]+='界'};
            nul{$contract.Arguments[16]+=[char]0};overlong{$contract.Arguments[16]+=(' '*20000)};extra_argv{$contract.Arguments+=@('-f','malicious.sql')}}
        Reject {Assert-DangoAccountSyncB1bEvidenceContract @contract} 'accountsync_b1b_'}}
    foreach($json in @('{"Count":1,"Count":1}','[]','null','{"nested":{"x":1,"x":1}}','{')){Check ('strict_json_'+$checks.Count){Reject {ConvertFrom-DangoAccountSyncB1bStrictJson $json} 'accountsync_b1b_json_rejected'}}
    Check archives_valid {Assert-DangoAccountSyncB1bArchives (Archives) $user $pet;$true}
    foreach($at in 0..11){foreach($field in (Archives).Archives[$at].PSObject.Properties.Name){Check ('archive_changed_'+$at+'_'+$field){$e=Archives
        if($e.Archives[$at].$field -is [int]){$e.Archives[$at].$field++}else{$e.Archives[$at].$field='bad'}
        Reject {Assert-DangoAccountSyncB1bArchives $e $user $pet} 'accountsync_b1b_archive_rejected'}}}
    Check archive_duplicate_journal {$e=Archives;$e.Archives[1].JournalId=$e.Archives[0].JournalId;Reject {Assert-DangoAccountSyncB1bArchives $e $user $pet} 'accountsync_b1b_archive_rejected'}
    Check archive_unordered_steps {$e=Archives;$e.Archives[0],$e.Archives[1]=$e.Archives[1],$e.Archives[0];Reject {Assert-DangoAccountSyncB1bArchives $e $user $pet} 'accountsync_b1b_archive_rejected'}
    Check faults_valid {Assert-DangoAccountSyncB1bFaultEvidence (FaultEvidence) $owner;$true}
    Check faults_cas_secondary_first {$e=FaultEvidence;$e.PatchAttempts[0],$e.PatchAttempts[1]=$e.PatchAttempts[1],$e.PatchAttempts[0];$e.PatchAttempts[0].Sequence=1;$e.PatchAttempts[1].Sequence=2
        Assert-DangoAccountSyncB1bFaultEvidence $e $owner;Assert-DangoAccountSyncB1bAuxAgreement (Archives) $e;$true}
    Check faults_cas_duplicate_device {$e=FaultEvidence;$e.PatchAttempts[1].Device='primary';$e.PatchAttempts[1].EyeSize=111
        Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_evidence_rejected'}
    foreach($at in 0..7){foreach($field in (FaultEvidence).PatchAttempts[$at].PSObject.Properties.Name){Check ('patch_changed_'+$at+'_'+$field){$e=FaultEvidence
        if($e.PatchAttempts[$at].$field -is [int]){$e.PatchAttempts[$at].$field++}elseif($e.PatchAttempts[$at].$field -is [bool]){$e.PatchAttempts[$at].$field=-not $e.PatchAttempts[$at].$field}else{$e.PatchAttempts[$at].$field='bad'}
        Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_evidence_rejected'}}}
    Check fault_retry_configuration_changed {$e=FaultEvidence;$e.PatchAttempts[6].ConfigurationSha256='9'*64
        Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_evidence_rejected'}
    Check aux_valid {Assert-DangoAccountSyncB1bAuxAgreement (Archives) (FaultEvidence);$true}
    foreach($at in 0..11){foreach($field in @('LocalConfigurationSha256','CloudConfigurationSha256')){Check ('aux_wrong_digest_'+$at+'_'+$field){$a=Archives;$a.Archives[$at].$field='9'*64
        Reject {Assert-DangoAccountSyncB1bAuxAgreement $a (FaultEvidence)} 'accountsync_b1b_aux_configuration_rejected'}}}
    Check fault_events_without_optional_responses {$e=FaultEvidence;$e.Controls.Events=@($e.Controls.Events|Where-Object{$_.Event -cne 'patch-response'})
        for($i=0;$i -lt $e.Controls.Events.Count;$i++){$e.Controls.Events[$i].Sequence=$i+1};Assert-DangoAccountSyncB1bFaultEvidence $e $owner;$true}
    foreach($at in 0..19){foreach($field in @('Device','BackendPid','StatusCode')){Check ('fault_event_attribution_'+$at+'_'+$field){$e=FaultEvidence
        $e.Controls.Events[$at].$field=if($field -ceq 'Device'){'unrelated'}elseif($field -ceq 'BackendPid'){999}else{400}
        Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_events_rejected'}}}
    Check fault_event_duplicate_backend {$e=FaultEvidence;$e.Controls.Events[6].BackendPid=301;Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_events_rejected'}
    Check fault_event_response_device_status_mismatch {$e=FaultEvidence;$e.Controls.Events[9].StatusCode=200;Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_events_rejected'}
    foreach($suffix in @([char]0,[char]0x00AD,[char]0x200B)){$suffixCode=[int]$suffix
        foreach($field in @('Stage','Database','Role','Filter','ApprovalVariable','TrxFileName')){Check ('protocol_plan_'+$suffixCode+'_'+$field){$plan=Plan;$plan.$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bFixedPlan $plan} 'accountsync_b1b_fixed_plan_rejected'}}
        Check ('protocol_shared_dispatch_'+$suffixCode){$plan=Plan;$plan.Stage+=$suffix;Reject {Get-DangoProfileFixedPlan $plan} 'profile_run_fixed_plan_rejected'}
        foreach($field in @('Nickname','MailState','UserId','PetId','RecoveryEpoch','DefaultRoomId')){Check ('protocol_journey_final_'+$suffixCode+'_'+$field){$e=Journey;$e.Final.$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_rejected'}}
        Check ('protocol_journey_owner_'+$suffixCode){$e=Journey;$e.OwnerId+=$suffix;Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_rejected'}
        Check ('protocol_case_'+$suffixCode){$e=Journey;$e.Cases[0].Case+=$suffix;Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_case_rejected'}
        foreach($field in @('Operation','OwnerId')){Check ('protocol_attempt_'+$suffixCode+'_'+$field){$e=Attempts;$e[0].$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bAttempts (Plan) $owner $e} 'accountsync_b1b_attempt_identity_rejected'}}
        foreach($field in @('Phase','OwnerId')){Check ('protocol_resource_'+$suffixCode+'_'+$field){$e=Resources;$e[0].$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bResources $owner $e} 'accountsync_b1b_resource_order_rejected'}}
        foreach($field in @('Device','Step','JournalId','FailureCode')){Check ('protocol_archive_'+$suffixCode+'_'+$field){$e=Archives;$e.Archives[1].$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bArchives $e $user $pet} 'accountsync_b1b_archive_rejected'}}
        foreach($field in @('Device','ConfigurationSha256')){Check ('protocol_patch_'+$suffixCode+'_'+$field){$e=FaultEvidence;$e.PatchAttempts[0].$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_evidence_rejected'}}
        foreach($field in @('Phase','Event','Device')){Check ('protocol_fault_event_'+$suffixCode+'_'+$field){$e=FaultEvidence;$e.Controls.Events[1].$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bFaultEvidence $e $owner} 'accountsync_b1b_fault_events_rejected'}}
        Check ('protocol_journey_key_'+$suffixCode){$e=Journey;$value=$e.OwnerId;$e.PSObject.Properties.Remove('OwnerId');$e|Add-Member ("OwnerId"+$suffix) $value
            Reject {Assert-DangoAccountSyncB1bJourney $e $owner $user $epoch} 'accountsync_b1b_journey_shape_rejected'}
        Check ('protocol_path_'+$suffixCode){Reject {Get-DangoAccountSyncB1bPlan ($taskSandbox+$suffix)} 'accountsync_b1b_path_text_rejected'}
        foreach($field in @('Executable','WorkingDirectory','OutputContract')){Check ('protocol_evidence_'+$suffixCode+'_'+$field){$c=EvidenceContract;$c[$field]+=$suffix
            Reject {Assert-DangoAccountSyncB1bEvidenceContract @c} 'accountsync_b1b_evidence_contract_rejected'}}
        foreach($field in @('database','role','listenAddresses')){Check ('protocol_target_'+$suffixCode+'_'+$field){$e=Snapshot;$e.$field+=$suffix
            Reject {Assert-DangoAccountSyncB1bTarget (Plan) $e} 'accountsync_b1b_target_rejected'}}
        Check ('protocol_target_migration_'+$suffixCode){$e=Snapshot;$e.migrations[0]+=$suffix;Reject {Assert-DangoAccountSyncB1bTarget (Plan) $e} 'accountsync_b1b_target_rejected'}
        Check ('protocol_target_profile_id_'+$suffixCode){$e=Snapshot;$e.profiles[0].id+=$suffix;Reject {Assert-DangoAccountSyncB1bTarget (Plan) $e} 'accountsync_b1b_target_rejected'}
    }
    $taskFakeIdentity={param($process,$expected)[pscustomobject]@{Verified=$true;ImagePath=$expected;NativeErrorCode=0;Pid=$process.Id;CreatedAt=$process.StartTime.ToUniversalTime()}}
    Check shared_default_trim_unchanged {$fake=FakeProcess;$receipts=[Collections.Generic.List[object]]::new()
        $result=Invoke-DangoProfileProcess 'fictional.exe' @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity
        $result.Output -ceq '{}' -and @($result.PSObject.Properties.Name) -cnotcontains 'RawOutput' -and $result.Receipt.OutputComplete -and $fake.Disposed}
    foreach($kind in @('full','size','scoped')){Check ('shared_raw_'+$kind+'_exact_crlf'){$fake=FakeProcess;$receipts=[Collections.Generic.List[object]]::new()
        $mode=if($kind -ceq 'full'){'AccountSyncB1bEvidenceJson'}else{'AccountSyncB1bSmallJson'};$options=RawHelperOptions $mode $kind
        $result=Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity
        [string]::Equals($result.RawOutput,"  {}`r`n ",[StringComparison]::Ordinal) -and
            [string]::Equals($result.RawError,"ERROR: fictional_failure`r`n",[StringComparison]::Ordinal) -and
            $result.ExitCode -eq 1 -and $result.Receipt.OutputComplete -and $result.Receipt.OutputResourcesCleanupConfirmed -and $fake.Disposed}}
    foreach($field in @('OutputContract','PostgresEvidencePort','PostgresEvidencePlan','Arguments','PreserveRawOutput','TimeoutSeconds','ExtraArguments','ExtraEnvironment')){Check ('shared_b1b_contract_'+$field){$options=RawHelperOptions
        switch($field){OutputContract{$options.OutputContract='CompleteEof'};PostgresEvidencePort{$options.PostgresEvidencePort=5432};
            PostgresEvidencePlan{$options.PostgresEvidencePlan=Get-DangoAccountSyncPlan $taskSandbox};Arguments{$options.Arguments=@('--version')};
            PreserveRawOutput{$options.PreserveRawOutput=$true};TimeoutSeconds{$options.TimeoutSeconds=31};ExtraArguments{$options.Arguments+=@('-f','bad.sql')};ExtraEnvironment{$options.EnvironmentValues.EXTRA='1'}}
        $called=[pscustomobject]@{Factories=0};$code=if($field -ceq 'PostgresEvidencePlan'){'fixed_plan_rejected'}else{'contract_rejected'}
        $rejected=Reject {Invoke-DangoProfileProcess @options -ProcessFactory {param($info)$called.Factories++;throw 'must_not_launch'}} $code
        $rejected -and $called.Factories -eq 0}}
    foreach($stream in @('stdout-small','stderr-small','stdout-full','stderr-full')){foreach($representation in @('chars','utf8')){Check ('shared_output_bound_'+$stream+'_'+$representation){
        $fake=FakeProcess;$receipts=[Collections.Generic.List[object]]::new();$kind=if($stream.EndsWith('full')){'full'}else{'scoped'}
        $mode=if($kind -ceq 'full'){'AccountSyncB1bEvidenceJson'}else{'AccountSyncB1bSmallJson'};$options=RawHelperOptions $mode $kind
        $limit=if($stream -ceq 'stdout-full'){1048576}else{131072};$inputText=if($representation -ceq 'chars'){'A'*($limit+1)}else{'界'*([int][Math]::Floor($limit/3)+1)}
        if($stream.StartsWith('stdout')){$fake.StandardOutput=[IO.StringReader]::new($inputText)}else{$fake.StandardError=[IO.StringReader]::new($inputText)}
        $rejected=Reject {Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity} 'profile_run_tool_output_bound_exceeded'
        $rejected -and -not $receipts[0].OutputComplete -and $fake.Disposed}}}
    foreach($stream in @('stdout-small','stderr-small','stdout-full','stderr-full')){Check ('shared_output_exact_limit_'+$stream){
        $fake=FakeProcess;$kind=if($stream.EndsWith('full')){'full'}else{'scoped'};$mode=if($kind -ceq 'full'){'AccountSyncB1bEvidenceJson'}else{'AccountSyncB1bSmallJson'}
        $options=RawHelperOptions $mode $kind;$limit=if($stream -ceq 'stdout-full'){1048576}else{131072};$inputText='A'*$limit
        if($stream.StartsWith('stdout')){$fake.StandardOutput=[IO.StringReader]::new($inputText)}else{$fake.StandardError=[IO.StringReader]::new($inputText)}
        $result=Invoke-DangoProfileProcess @options -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity
        $result.Receipt.OutputComplete -and $fake.Disposed -and
            $(if($stream.StartsWith('stdout')){[string]::Equals($result.RawOutput,$inputText,[StringComparison]::Ordinal)}else{[string]::Equals($result.RawError,$inputText,[StringComparison]::Ordinal)})}}
    foreach($stream in @('stdout','stderr')){Check ('shared_raw_requires_full_eof_'+$stream) {
        $server=[IO.Pipes.AnonymousPipeServerStream]::new([IO.Pipes.PipeDirection]::In,[IO.HandleInheritability]::None)
        $writer=[IO.Pipes.AnonymousPipeClientStream]::new([IO.Pipes.PipeDirection]::Out,$server.ClientSafePipeHandle)
        $reader=[IO.StreamReader]::new($server,[Text.Encoding]::UTF8);$fake=FakeProcess
        if($stream -ceq 'stdout'){$fake.StandardOutput=$reader}else{$fake.StandardError=$reader}
        $receipts=[Collections.Generic.List[object]]::new();$options=RawHelperOptions
        try{$rejected=Reject {Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity} 'profile_run_tool_output_incomplete'
            $rejected -and -not $receipts[0].OutputComplete -and -not $receipts[0].OutputResourcesCleanupConfirmed -and $fake.Disposed -and
                $(if($stream -ceq 'stdout'){$receipts[0].StandardOutputCloseSkippedPending}else{$receipts[0].StandardErrorCloseSkippedPending})}
        finally{$writer.Dispose();$server.DisposeLocalCopyOfClientHandle();$reader.Dispose();$server.Dispose()}}}
    $taskOriginalFixed=& $taskModule {(Get-Command Assert-DangoAccountSyncB1bFixedPlan).ScriptBlock}
    $taskWriterDirectory=Join-Path $taskSandbox 'raw-writer';[void][IO.Directory]::CreateDirectory($taskWriterDirectory)
    try{& $taskModule {param($directory) $script:taskOfflineWriterDirectory=$directory;function script:Assert-DangoAccountSyncB1bFixedPlan {param($Plan)
            $fixed=Get-DangoAccountSyncB1bPlan $Plan.Repository;$fixed.EvidenceDirectory=$script:taskOfflineWriterDirectory;return $fixed}} $taskWriterDirectory
        Check raw_writer_preserves_exact_bytes {Save-DangoAccountSyncB1bRawEvidence (Plan) 'before-size.json' "  {}`r`n "
            [string]::Equals([IO.File]::ReadAllText((Join-Path $taskWriterDirectory 'before-size.json')),"  {}`r`n ",[StringComparison]::Ordinal)}
        Check raw_writer_duplicate_rejected {$path=Join-Path $taskWriterDirectory 'before-size.json';$old=[IO.File]::ReadAllText($path)
            try{Save-DangoAccountSyncB1bRawEvidence (Plan) 'before-size.json' 'overwrite';throw 'b1b_offline_duplicate_not_rejected'}catch{
                if($_.Exception.Message -ceq 'b1b_offline_duplicate_not_rejected'){throw}}
            [string]::Equals($old,[IO.File]::ReadAllText($path),[StringComparison]::Ordinal)}
        Check raw_writer_utf8_bound {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) 'after-size.json' ('界'*43691)} 'accountsync_b1b_evidence_bound_rejected'}
        Check raw_writer_char_bound {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) 'scoped.stderr.txt' ('A'*131073)} 'accountsync_b1b_evidence_bound_rejected'}
        Check raw_writer_invalid_utf16 {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) 'after-size.stderr.txt' ([string][char]0xD800)} 'Unable to translate'}
        Check raw_writer_outside_filename {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) '../escape.json' '{}'} 'accountsync_b1b_evidence_path_rejected'}
        Check raw_writer_filename_newline {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) "before.json`n" '{}'} 'accountsync_b1b_evidence_path_rejected'}
        Check raw_writer_small_cannot_use_full_limit {Reject {Save-DangoAccountSyncB1bRawEvidence (Plan) 'before-size.json' '{}' 1048576} 'accountsync_b1b_evidence_bound_rejected'}
        Check raw_writer_full_exact_limit {Save-DangoAccountSyncB1bRawEvidence (Plan) 'before.json' ('A'*1048576) 1048576
            (Get-Item -LiteralPath (Join-Path $taskWriterDirectory 'before.json')).Length -eq 1048576}
    }finally{& $taskModule {param($original)Set-Item -LiteralPath Function:script:Assert-DangoAccountSyncB1bFixedPlan -Value $original} $taskOriginalFixed}
    $sourceInputs=Get-DangoAccountSyncB1bInputs (Get-DangoAccountSyncB1bPlan $taskRepository)
    foreach($relative in @($sourceInputs.Hashes.Keys)+@($sourceInputs.MissingInputs)){if(-not $relative.StartsWith('postgres-bin/')){[void](Fixture $relative)}}
    $approval=New-DangoAccountSyncB1bApproval (Plan) (Join-Path $taskSandbox 'artifacts/fictional-approved.json')
    Check approval_valid {[void](Assert-DangoAccountSyncB1bApproval (Plan) $approval.Path $approval.Sha256);$true}
    foreach($suffix in @([char]0,[char]0x00AD,[char]0x200B)){Check ('approval_path_control_'+[int]$suffix){
        Reject {Assert-DangoAccountSyncB1bApproval (Plan) ($approval.Path+$suffix) $approval.Sha256} 'accountsync_b1b_path_text_rejected'}}
    Check freeze_production_transport_and_dependencies {$inputs=Get-DangoAccountSyncB1bInputs (Plan)
        $inputs.PostgresTools.Count -eq 3 -and $inputs.Hashes.Contains('src/Dango.Client.Sync/Pets/PetConfigurationLiveSync.cs') -and
            $inputs.Hashes.Contains('src/Dango.Client.Sync/Pets/PetConfigurationHttpClient.cs') -and $inputs.Hashes.Contains('src/Dango.Client.Sync/Pets/PetConfigurationFileJournal.cs') -and
            $inputs.Hashes.Contains('docs/planning/2026-10-11-account-sync-b1b1-local-plan.md') -and
            $inputs.Hashes.Contains((Plan).CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')}
    foreach($field in @('Stage','Plan','PostgresTools','Hashes','MissingInputs')){Check ('approval_changed_'+$field){$changed=Clone $approval.Inputs
        switch($field){Stage{$changed.Stage='AccountSyncB1a2'};Plan{$changed.Plan.Budgets.Configuration=9};PostgresTools{$changed.PostgresTools.'psql.exe'='D'*64};
            Hashes{$changed.Hashes.'src/Dango.Client.Sync/Pets/PetConfigurationHttpClient.cs'='D'*64};MissingInputs{$changed.MissingInputs=@('bad')}}
        $path=Fixture ('artifacts/mutated-'+$field+'.json') ($changed|ConvertTo-Json -Depth 32);$digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Reject {Assert-DangoAccountSyncB1bApproval (Plan) $path $digest} 'accountsync_b1b_json_value_rejected'}}
    foreach($suffix in @([char]0,[char]0x00AD,[char]0x200B)){
        foreach($field in @('Stage','RunName')){Check ('approval_protocol_'+[int]$suffix+'_'+$field){$changed=Clone $approval.Inputs;$changed.$field+=$suffix
            $path=Fixture ('artifacts/mutated-protocol-'+[int]$suffix+'-'+$field+'.json') ($changed|ConvertTo-Json -Depth 32);$digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Reject {Assert-DangoAccountSyncB1bApproval (Plan) $path $digest} 'accountsync_b1b_json_value_rejected'}}
        Check ('approval_protocol_key_'+[int]$suffix){$changed=Clone $approval.Inputs;$value=$changed.Stage;$changed.PSObject.Properties.Remove('Stage');$changed|Add-Member ('Stage'+$suffix) $value
            $path=Fixture ('artifacts/mutated-protocol-key-'+[int]$suffix+'.json') ($changed|ConvertTo-Json -Depth 32);$digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Reject {Assert-DangoAccountSyncB1bApproval (Plan) $path $digest} 'accountsync_b1b_json_value_rejected'}
    }
    Check approval_changed_source {[void](Fixture 'src/Dango.Client.Sync/Pets/PetConfigurationFileJournal.cs' 'changed');Reject {Assert-DangoAccountSyncB1bApproval (Plan) $approval.Path $approval.Sha256} 'accountsync_b1b_json_value_rejected'}
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($RunnerPath),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'b1b_offline_runner_parse_failed'}
    foreach($function in $ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)){Invoke-Expression $function.Extent.Text}
    function Assert-DangoAccountSyncB1bFrozenInputs {}
    function Invoke-DangoProfileProcess {param($Executable,$Arguments,$WorkingDirectory,$EnvironmentValues,$TimeoutSeconds,$KillOwnedTree,$Receipts,$OutputContract,$PostgresStartPlan,$PostgresStartPort,$PostgresEvidencePlan,$PostgresEvidencePort)
        $record=[pscustomobject]@{Executable=$Executable;Arguments=$Arguments;OutputContract=$OutputContract;OutputComplete=$true}
        if(-not $omitReceipt){$Receipts.Add($record)};[pscustomobject]@{ExitCode=0;Receipt=$record;Output='fictional';RawOutput='fictional';RawError=''}}
    function ResetRunner {$script:taskB1bPlan=Plan;$script:taskB1bToolAttempts=0;$script:taskB1bToolIndex=0;$script:taskB1bStopAttempted=$false;$script:taskB1bStopOwnershipVerified=$false
        $script:taskB1bTools=[Collections.Generic.List[object]]::new();$script:taskB1bFacts=[pscustomobject]@{Port=63237;Epoch=$epoch};$script:taskB1bNewUser=$user
        $script:taskB1bReservation=[pscustomobject]@{OwnerId=$owner};$script:taskB1bJourney=Journey
        $script:taskB1bControl=Join-Path $taskB1bPlan.PostgresBinDirectory 'pg_ctl.exe';$script:taskB1bPostgres=Join-Path $taskB1bPlan.PostgresBinDirectory 'postgres.exe';
        $script:taskB1bPsql=Join-Path $taskB1bPlan.PostgresBinDirectory 'psql.exe';$script:taskB1bDotnet=Join-Path $taskSandbox 'fake-dotnet.exe'
        $script:taskB1bTestDll=Join-Path $taskB1bPlan.Repository ($taskB1bPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
        $script:taskB1bPsqlEnvironment=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};$script:taskB1bTestEnvironment=@{DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED='1'};$script:omitReceipt=$false}
    function RunStep([string]$Step){$exe=$taskB1bControl;$environment=@{};$timeout=30;$contract='CompleteEof';$tree=$false
        $arguments=switch -CaseSensitive ($Step){version{$exe=$taskB1bPostgres;@('--version')};status{@('status','-D',$taskB1bPlan.DataDirectory)};
            start{$timeout=40;$contract='PostgresStartIgnored';@('start','-D',$taskB1bPlan.DataDirectory,'-l',(Join-Path $taskB1bPlan.EvidenceDirectory 'postgresql.log'),'-o','-h 127.0.0.1 -p 63237','-w','-t','30')};
            {$_ -cin @('before-size','after-size')}{$exe=$taskB1bPsql;$environment=$taskB1bPsqlEnvironment;$contract='AccountSyncB1bSmallJson';Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)};
            {$_ -cin @('before-full','after-full')}{$exe=$taskB1bPsql;$environment=$taskB1bPsqlEnvironment;$contract='AccountSyncB1bEvidenceJson';Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncDatabaseSql)};
            vstest{$exe=$taskB1bDotnet;$environment=$taskB1bTestEnvironment;$timeout=300;$tree=$true;@('vstest',$taskB1bTestDll,('--TestCaseFilter:'+$taskB1bPlan.Filter),('--Logger:trx;LogFileName='+$taskB1bPlan.TrxFileName),('--ResultsDirectory:'+$taskB1bPlan.EvidenceDirectory))};
            scoped{$exe=$taskB1bPsql;$environment=$taskB1bPsqlEnvironment;$contract='AccountSyncB1bSmallJson';Get-DangoAccountSyncB1bPsqlArguments $taskB1bPlan (Get-DangoAccountSyncB1bScopedSql $user $epoch $owner $pet $room)};
            stop{$timeout=40;@('stop','-D',$taskB1bPlan.DataDirectory,'-m','fast','-w','-t','30')}}
        Invoke-DangoAccountSyncB1bTool $Step $exe ([string[]]$arguments) $environment -TimeoutSeconds $timeout -OutputContract $contract -KillOwnedTree:$tree|Out-Null}
    Check runner_wrong_first {ResetRunner;Reject {RunStep vstest} 'accountsync_b1b_tool_budget_rejected'}
    Check runner_exact_ten {ResetRunner;foreach($step in @('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped')){RunStep $step}
        $script:taskB1bStopOwnershipVerified=$true;RunStep stop;$taskB1bToolAttempts -eq 10 -and $taskB1bTools.Count -eq 10}
    Check runner_eleventh {Reject {RunStep stop} 'accountsync_b1b_tool_budget_rejected'}
    Check runner_stop_needs_ownership {ResetRunner;foreach($step in @('version','status','start')){RunStep $step};Reject {RunStep stop} 'accountsync_b1b_tool_budget_rejected'}
    Check runner_failure_stop_no_retry {$script:taskB1bStopOwnershipVerified=$true;RunStep stop;Reject {RunStep 'before-size'} 'accountsync_b1b_tool_budget_rejected'}
    Check runner_receipt_omission_budget {ResetRunner;$script:omitReceipt=$true;foreach($step in @('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped')){RunStep $step}
        $script:taskB1bStopOwnershipVerified=$true;RunStep stop;$taskB1bToolAttempts -eq 10 -and $taskB1bTools.Count -eq 0 -and (Reject {RunStep version} 'accountsync_b1b_tool_budget_rejected')}
    Check runner_extra_argv {ResetRunner;Reject {Invoke-DangoAccountSyncB1bTool version $taskB1bPostgres @('--version','extra')} 'accountsync_b1b_tool_contract_rejected'}
    Check runner_nul_argv {ResetRunner;Reject {Invoke-DangoAccountSyncB1bTool version $taskB1bPostgres @("--version$([char]0)")} 'accountsync_b1b_tool_contract_rejected'}
    foreach($suffix in @([char]0,[char]0x00AD,[char]0x200B)){Check ('runner_protocol_step_'+[int]$suffix){ResetRunner;Reject {RunStep ('version'+$suffix)} 'accountsync_b1b_tool_budget_rejected'}
        Check ('runner_protocol_contract_'+[int]$suffix){ResetRunner;Reject {Invoke-DangoAccountSyncB1bTool version $taskB1bPostgres @('--version') -OutputContract ('CompleteEof'+$suffix)} 'accountsync_b1b_tool_contract_rejected'}}
    $text=[IO.File]::ReadAllText([IO.Path]::GetFullPath($RunnerPath))
    Check runner_no_native_or_old_stage {$text -cnotmatch 'NativeModule|NativeSha256|DANGO_BACKEND_ACCOUNTSYNC_B1A|FixtureUiaWorkers' -and $text -cmatch 'DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED'}
    Check runner_reservation_before_original_reads {$text.IndexOf('$taskB1bReservation=New-DangoAccountSyncB1bReservation',[StringComparison]::Ordinal) -lt
        $text.IndexOf('$taskB1bState=Read-DangoAccountSyncB1bBoundedState',[StringComparison]::Ordinal)}
    Check runner_raw_before_validation {$text.IndexOf("Save-DangoAccountSyncB1bRawEvidence `$taskB1bPlan 'scoped-graph-evidence.json'",[StringComparison]::Ordinal) -lt
        $text.IndexOf('$taskB1bScopedGraph=ConvertFrom-DangoAccountSyncB1bStrictJson',[StringComparison]::Ordinal) -and $text.Contains('RawError') -and $text.Contains('OutputComplete')}
    Check runner_strict_aux_evidence {$text.Contains('Assert-DangoAccountSyncB1bArchives') -and $text.Contains('Assert-DangoAccountSyncB1bFaultEvidence') -and
        $text.Contains('TestsPassed=($taskB1bCleanup') -and $text.Contains('$taskB1bArchivesValidated -and $taskB1bFaultsValidated')}
}finally{& $taskModule {param($original)Set-Item -LiteralPath Function:script:Get-DangoAccountSyncB1bBinaryInputs -Value $original} $binaryOriginal}
$resultPath=Join-Path $taskSandbox 'offline-results.json';$failed=@($checks|Where-Object{-not $_.Passed})
[IO.File]::WriteAllText($resultPath,([pscustomobject]@{Version=1;Passed=$checks.Count-$failed.Count;Failed=$failed.Count;Checks=@($checks);
    SourceHashes=$taskSourceHashes;UsesRealBackendResources=$false}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
if($failed.Count){$failed|Format-Table -AutoSize;throw ('b1b_offline_failed:'+ $failed.Count)}
Write-Output ('b1b_offline_passed:'+ $checks.Count+';evidence='+$resultPath)

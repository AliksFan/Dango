[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.PorchCrudC1Postgres.psm1') -Force -DisableNameChecking
$taskRoot=Split-Path -Parent $PSScriptRoot;$taskPlan=Get-DangoPorchCrudC1Plan $taskRoot
$taskChecks=0
function Confirm-Rejected([scriptblock]$Action){$rejected=$false;try{& $Action}catch{$rejected=$true};if(-not $rejected){throw ('porch_c1_offline_negative_accepted_at_'+$script:taskChecks)};$script:taskChecks++}
function Copy-TestValue($Value){$Value|ConvertTo-Json -Depth 24 -Compress|ConvertFrom-Json -Depth 24}
$taskScope=[pscustomobject]@{Version=1;OwnerId=([Guid]::NewGuid().ToString('D'));RecoveryEpoch=([Guid]::NewGuid().ToString('D'));
    Accounts=@();RoomId=([Guid]::NewGuid().ToString('D'));RoomRequestIds=@(1..7|ForEach-Object{[Guid]::NewGuid().ToString('D')})}
foreach($actor in @('a','b')){$taskScope.Accounts+=[pscustomobject]@{Actor=$actor;UserId=[Guid]::NewGuid().ToString('D');PetId=[Guid]::NewGuid().ToString('D');
    DefaultRoomId=[Guid]::NewGuid().ToString('D');SessionId=[Guid]::NewGuid().ToString('D');Nickname=$(if($actor -ceq 'a'){'门廊主人'}else{'门廊访客'});ProfileVersion=2;ConfigurationVersion=1}}
Assert-DangoPorchCrudC1ScopeInputs $taskScope;$taskChecks++
$taskSql=Get-DangoPorchCrudC1ScopedSql $taskScope
$taskRoundtrip=ConvertFrom-DangoPorchCrudC1SqlIdentity $taskSql
if($taskSql -cne (Get-DangoPorchCrudC1ScopedSql $taskRoundtrip)){throw 'porch_c1_offline_sql_roundtrip_failed'};$taskChecks++
$taskOutput=Join-Path $taskRoot 'artifacts/porch-crud-c1-candidate/scoped-sql-review.json'
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $taskOutput))
Write-DangoProfileJson $taskOutput ([pscustomobject]@{Version=1;Scope=$taskScope;Sql=$taskSql})
foreach($change in @('budget','run','filter','extra')){
    $mutated=Copy-TestValue $taskPlan
    switch($change){budget{$mutated.Budgets.Create=2};run{$mutated.EvidenceDirectory+='-other'};filter{$mutated.Filter='TestCategory=PorchCrudC1'};extra{$mutated|Add-Member Extra $true}}
    Confirm-Rejected {Assert-DangoPorchCrudC1FixedPlan $mutated}
}
foreach($change in @('nickname','pet','session','request','extra')){
    $mutated=Copy-TestValue $taskScope
    switch($change){nickname{$mutated.Accounts[0].Nickname='其他昵称'};pet{$mutated.Accounts[1].PetId=$mutated.Accounts[0].PetId};
        session{$mutated.Accounts[1].SessionId=$mutated.Accounts[0].SessionId};request{$mutated.RoomRequestIds[1]=$mutated.RoomRequestIds[0]};extra{$mutated|Add-Member Extra $true}}
    Confirm-Rejected {Assert-DangoPorchCrudC1ScopeInputs $mutated}
}
$taskEnvironment=@{PGPASSWORD=[Convert]::ToBase64String([byte[]]::new(32));PGCLIENTENCODING='UTF8'}
$taskPsql=Join-Path $taskPlan.PostgresBinDirectory 'psql.exe'
Assert-DangoPorchCrudC1EvidenceContract $taskPsql (Get-DangoPorchCrudC1PsqlArguments $taskPlan $taskSql) $taskRoot $taskPlan 63237 $taskEnvironment $false PorchCrudC1SmallJson;$taskChecks++
foreach($changedSql in @($taskSql.Replace('BEGIN READ ONLY;','BEGIN;'),($taskSql+' DELETE FROM dango.users;'),
    $taskSql.Replace('status=0 AND version=4','status=0 AND version=3'),$taskSql.Replace('pr.normalized_email=u.normalized_email','true'),
    $taskSql.Replace('o.target_user_ids=ARRAY','o.target_user_id=ARRAY'))){
    Confirm-Rejected {Assert-DangoPorchCrudC1EvidenceContract $taskPsql (Get-DangoPorchCrudC1PsqlArguments $taskPlan $changedSql) $taskRoot $taskPlan 63237 $taskEnvironment $false PorchCrudC1SmallJson}
}
Confirm-Rejected {ConvertFrom-DangoPorchCrudC1StrictJson '{"a":1,"a":2}'}
$taskKeys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions','audit','outbox',
    'leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
$taskDelta=@{invitations=2;registrations=2;challenges=2;emailJobs=2;users=2;pets=2;rooms=3;memberships=3;petStates=2;sessions=2;audit=4;outbox=4}
$before=[pscustomobject]@{counts=[pscustomobject]@{};digests=[pscustomobject]@{};userIds=@([Guid]::NewGuid().ToString('D'))}
$after=[pscustomobject]@{counts=[pscustomobject]@{};digests=[pscustomobject]@{};userIds=@($before.userIds)+@($taskScope.Accounts.UserId)}
foreach($key in $taskKeys){$before.counts|Add-Member $key 2;$before.digests|Add-Member $key @(('a'*64),('a'*64))
    $count=if($taskDelta.ContainsKey($key)){$taskDelta[$key]}else{0};$after.counts|Add-Member $key (2+$count)
    $hashes=@(('a'*64),('a'*64));for($i=1;$i -le $count;$i++){$hashes+=('{0:x64}' -f $i)};$after.digests|Add-Member $key $hashes}
[void](Assert-DangoPorchCrudC1OldGraph $before $after);$taskChecks++
$mutated=Copy-TestValue $after;$mutated.digests.users[0]='b'*64;Confirm-Rejected {Assert-DangoPorchCrudC1OldGraph $before $mutated}
$mutated=Copy-TestValue $after;$mutated.counts.accessoryBlobs++;$mutated.digests.accessoryBlobs+=('b'*64);Confirm-Rejected {Assert-DangoPorchCrudC1OldGraph $before $mutated}
$mutated=Copy-TestValue $after;$mutated.counts.audit++;$mutated.digests.audit+=('b'*64);Confirm-Rejected {Assert-DangoPorchCrudC1OldGraph $before $mutated}
$taskGraph=[pscustomobject]@{completeGraphs=2;sessions=2;restoredRooms=1;ownerMemberships=1;allMemberships=1;occupancy=0;audit=4;allAudit=4;outbox=4;allOutbox=4;foreignReceipts=0}
Assert-DangoPorchCrudC1ScopedGraph $taskGraph;$taskChecks++
foreach($key in @($taskGraph.PSObject.Properties.Name)){$mutated=Copy-TestValue $taskGraph;$mutated.$key++;Confirm-Rejected {Assert-DangoPorchCrudC1ScopedGraph $mutated}}
$taskDesktopFacts=[pscustomobject]@{Index=1;Disposed=$true;HwndClosed=$true;UiaSettled=$true;PumpsSettled=$true;HttpSettled=$true;
    TimerStopped=$true;HostTimersStopped=$true;RuntimeStopped=$true;HomeDriverReleased=$true;CoordinatorsStopped=$true;
    AccountOperationsDrained=$true;RoomOperationsDrained=$true;SecretsDisposed=$true;CleanupPassed=$true;InternalDisposeTaskObservable=$false}
$taskResourceFacts=@([pscustomobject]@{Port=54321;CertificateSha256=('A'*64)},[pscustomobject]@{Index=1;Port=54321;WorkersDisabled=$true},
    [pscustomobject]@{Index=1;Attempt=$true},$taskDesktopFacts,
    [pscustomobject]@{Index=1;Stopped=$true;Disposed=$true;InFlightRequests=0;LifecycleTasksSettled=$true;ListenerAbsent=$true},
    [pscustomobject]@{NativeKeyIdentityCaptured=$true;NativeCleanupConfirmed=$true;NativeProbeFailed=$false})
$taskResourcePhases=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed')
$taskResources=@(0..5|ForEach-Object{[pscustomobject]@{Version=1;OwnerId=$taskScope.OwnerId;Sequence=($_+1);Phase=$taskResourcePhases[$_];Facts=$taskResourceFacts[$_]}})
[void](Assert-DangoPorchCrudC1Resources ([Guid]$taskScope.OwnerId) $taskResources);$taskChecks++
foreach($change in @('http','rooms','listener','key','index','duplicate')){
    $mutated=Copy-TestValue $taskResources
    switch($change){http{$mutated[4].Facts.InFlightRequests=1};rooms{$mutated[3].Facts.RoomOperationsDrained=$false};listener{$mutated[4].Facts.ListenerAbsent=$false};
        key{$mutated[5].Facts.NativeCleanupConfirmed=$false};index{$mutated[3].Facts.Index=2};duplicate{$mutated+=Copy-TestValue $mutated[3]}}
    Confirm-Rejected {Assert-DangoPorchCrudC1Resources ([Guid]$taskScope.OwnerId) $mutated}
}
$taskRows=[Collections.Generic.List[object]]::new()
function Add-TestRequest($Actor,$Method,$Path,$Operation,$Status,$Version=$null,$Expected=$null,$RequestId=$null){
    $n=$taskRows.Count+1;$idx=if($Actor -ceq 'a'){0}else{1}
    $taskRows.Add([pscustomobject]@{Sequence=$n;ClientId=($idx+3);Role=$(if($idx -eq 0){'desktop'}else{'outsider'});Actor=$Actor;Method=$Method;Path=$Path;
        SessionId=$taskScope.Accounts[$idx].SessionId;RequestId=$RequestId;ExpectedVersion=$Expected;SendBefore=($n*1000);SendAfter=($n*1000+1);
        Status=$Status;RoomVersion=$Version;Operation=$Operation;Terminal='RanToCompletion'})
}
foreach($actor in @('a','b')){foreach($op in @('Start','Complete','Login','Profile','Logout')){Add-TestRequest $actor 'POST' '/api/v1/auth/offline-oracle' $op 200}}
1..8|ForEach-Object{Add-TestRequest 'a' 'GET' '/api/v1/me/pet' $null 200}
$taskDetail="/api/v1/rooms/$($taskScope.RoomId)"
Add-TestRequest 'a' 'POST' '/api/v1/rooms' 'Create' 201 1 $null $taskScope.RoomRequestIds[0]
Add-TestRequest 'a' 'GET' $taskDetail $null 200 1
Add-TestRequest 'b' 'GET' $taskDetail $null 404
Add-TestRequest 'b' 'PATCH' $taskDetail 'Update' 404 $null 1 $taskScope.RoomRequestIds[1]
Add-TestRequest 'a' 'PATCH' $taskDetail 'Update' 200 2 1 $taskScope.RoomRequestIds[2]
Add-TestRequest 'a' 'GET' $taskDetail $null 200 2
Add-TestRequest 'b' 'POST' ($taskDetail+'/dissolve') 'Dissolve' 404 $null 2 $taskScope.RoomRequestIds[3]
Add-TestRequest 'a' 'POST' ($taskDetail+'/dissolve') 'Dissolve' 200 3 2 $taskScope.RoomRequestIds[4]
Add-TestRequest 'a' 'GET' $taskDetail $null 200 3
Add-TestRequest 'b' 'POST' ($taskDetail+'/restore') 'Restore' 404 $null 3 $taskScope.RoomRequestIds[5]
Add-TestRequest 'a' 'POST' ($taskDetail+'/restore') 'Restore' 200 4 3 $taskScope.RoomRequestIds[6]
Add-TestRequest 'a' 'GET' $taskDetail $null 200 4
Add-TestRequest 'a' 'GET' $taskDetail $null 200 4
$taskJourney=[pscustomobject]@{Version=1;OwnerId=$taskScope.OwnerId;Passed=$true;Actors=$taskScope.Accounts;RoomId=$taskScope.RoomId;
    DurationSeconds=1.5;HttpRequests=$taskRows.Count;ClientsCreated=5;DesktopTransports=2;StopwatchFrequency=1000;Requests=$taskRows.ToArray();
    Walks=@([pscustomobject]@{Target='porch-create';StartX=560;EndX=160;Key=65;SameRuntime=$true;NewHealthyFrame=$true},
    [pscustomobject]@{Target='porch-archive';StartX=160;EndX=1800;Key=68;SameRuntime=$true;NewHealthyFrame=$true},
    [pscustomobject]@{Target=('room-'+$taskScope.RoomId.Replace('-',''));StartX=1800;EndX=1000;Key=65;SameRuntime=$true;NewHealthyFrame=$true});
    CleanupPassed=$true;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;ActualApplicationHost=$true;HealthyNativeFrames=$true}
Assert-DangoPorchCrudC1Journey $taskJourney $taskScope;$taskChecks++
foreach($change in @('foreign','readback','request','pacing','walk','cleanup','extra','fault')){
    $mutated=Copy-TestValue $taskJourney
    switch($change){foreign{$mutated.Requests[21].Status=200};readback{$mutated.Requests[19].RoomVersion=2};request{$mutated.Requests[18].RequestId=[Guid]::NewGuid().ToString('D')};
        pacing{$mutated.Requests[10].SendBefore=$mutated.Requests[9].SendAfter+599};walk{$mutated.Walks[1].EndX=$mutated.Walks[1].StartX};
        cleanup{$mutated.CleanupPassed=$false};extra{$mutated|Add-Member Extra $true};fault{$mutated.Requests[2].Terminal='Faulted'}}
    Confirm-Rejected {Assert-DangoPorchCrudC1Journey $mutated $taskScope}
}
[pscustomobject]@{Offline=$true;Checks=$taskChecks;DatabaseOpened=$false;OriginalStateRead=$false;ScopeArtifact=$taskOutput}

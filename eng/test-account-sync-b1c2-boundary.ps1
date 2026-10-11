[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountSyncB1c2Postgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-sync-b1c2-tests.ps1'))
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncB1bPostgres.psm1') -Force -DisableNameChecking
$taskModule=Import-Module ([IO.Path]::GetFullPath($ModulePath)) -Force -PassThru -DisableNameChecking
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskSandbox=Join-Path $taskRepository ('artifacts/account-sync-b1c2-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$checks=[Collections.Generic.List[object]]::new();$owner=[Guid]'dddddddd-dddd-dddd-dddd-dddddddddddd';$epoch=[Guid]'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
$sourceHashes=[ordered]@{};foreach($path in @($ModulePath,$RunnerPath,$PSCommandPath,(Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'),
    (Join-Path $taskRepository 'tests/Dango.Server.Tests/Configuration/AccountSyncB1c2ScopedSqlMetadataTests.cs'))){$sourceHashes[[IO.Path]::GetFullPath($path)]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Check([string]$Name,[scriptblock]$Body){$passed=$false;$failure=$null;try{$passed=[bool](& $Body)}catch{$failure=$_.Exception.Message}
    $checks.Add([pscustomobject]@{Name=$Name;Passed=$passed;Failure=$failure})}
function Reject([scriptblock]$Body,[string]$Code='accountsync_b1c2_'){try{[void](& $Body)}catch{
    if($_.Exception.Message.Contains($Code,[StringComparison]::Ordinal)){return $true};throw ('b1c2_offline_wrong_code:'+ $_.Exception.Message)}
    throw 'b1c2_offline_expected_rejection_missing'}
function Clone($Value){$Value|ConvertTo-Json -Depth 32 -Compress|ConvertFrom-Json -Depth 32}
function Plan {Get-DangoAccountSyncB1c2Plan $taskSandbox}
function Scope {
    $accounts=@();foreach($actor in @('a','b')){$accounts+=[pscustomobject]@{Actor=$actor;UserId="${actor}1111111-1111-4111-8111-111111111111";
        PetId="${actor}2222222-2222-4222-8222-222222222222";DefaultRoomId="${actor}3333333-3333-4333-8333-333333333333";
        SessionIds=@("${actor}4444444-4444-4444-8444-444444444444","${actor}5555555-5555-4555-8555-555555555555");
        Nickname=$(if($actor -ceq 'a'){'旧账号团子'}else{'新账号团子'});ProfileVersion=2;ConfigurationVersion=$(if($actor -ceq 'a'){2}else{3});
        # Independent fixed oracle: do not construct this input through the getter being tested.
        Configuration=[pscustomobject][ordered]@{appearanceSchemaVersion=3;bodyColorHex=$(if($actor -ceq 'a'){'#FFE8C2'}else{'#9ECBEF'});
            eyeColorHex='#272321';eyeSizePercent=$(if($actor -ceq 'a'){111}else{130});eyeOvalityPercent=0;eyeHeightControlPercent=51;eyeSpacingControlPercent=24.5}}}
    [pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');RecoveryEpoch=$epoch.ToString('D');Accounts=$accounts}
}
function Attempts {$rows=@();$sequence=0;$plan=Plan
    foreach($operation in $plan.Budgets.Keys){for($used=1;$used -le $plan.Budgets[$operation];$used++){$sequence++
        $rows+=[pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Sequence=$sequence;Operation=$operation;Used=$used;AtUtc='2026-10-11T00:00:00Z'}}};return ,$rows}
function Resources {$phases=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed');$rows=@()
    for($i=0;$i -lt 8;$i++){$facts=switch -CaseSensitive ($phases[$i]){
        TLSCreated{[pscustomobject]@{Port=63123;CertificateSha256=('A'*64)}};HostStarted{[pscustomobject]@{Index=1;Port=63123;WorkersDisabled=$true}};
        DesktopStarted{[pscustomobject]@{Index=$(if($i -eq 2){1}else{2});Attempt=$true}};
        DesktopDisposed{[pscustomobject]@{Index=$(if($i -eq 3){1}else{2});Disposed=$true;HwndClosed=$true;UiaSettled=$true;PumpsSettled=$true;HttpSettled=$true;
            TimerStopped=$true;HostTimersStopped=$true;RuntimeStopped=$true;HomeDriverReleased=$true;CoordinatorsStopped=$true;AccountOperationsDrained=$true;
            SecretsDisposed=$true;CleanupPassed=$true;InternalDisposeTaskObservable=$false}};
        HostDisposed{[pscustomobject]@{Index=1;Stopped=$true;Disposed=$true;InFlightRequests=0;LifecycleTasksSettled=$true}};
        TLSDisposed{[pscustomobject]@{NativeKeyIdentityCaptured=$true;NativeCleanupConfirmed=$true;NativeProbeFailed=$false}}}
        $rows+=[pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Sequence=$i+1;Phase=$phases[$i];Facts=$facts}};return ,$rows}
function Intents {$scope=Scope;$rows=@();for($i=0;$i -lt 2;$i++){$rows+=[pscustomobject]@{Version=1;OwnerId=$scope.OwnerId;Sequence=$i+1;
    Case=@('late-get','late-patch')[$i];ClientId=@(3,6)[$i];UserId=$scope.Accounts[0].UserId;PetId=$scope.Accounts[0].PetId;SessionId=$scope.Accounts[0].SessionIds[$i];
    Intent='uia-logout';Forwarded=$false;AtUtc='2026-10-11T00:00:00Z'}};return ,$rows}
function Archives {$scope=Scope;$rows=@();$steps=@('late-get-choice','late-get-save','late-patch-unacknowledged','late-patch-choice','late-patch-save')
    for($i=0;$i -lt 5;$i++){$actor=if($i -eq 2){'a'}else{'b'};$account=$scope.Accounts[$(if($actor -ceq 'a'){0}else{1})]
        $rows+=[pscustomobject]@{Actor=$actor;Case=$(if($i -lt 2){'late-get'}else{'late-patch'});Step=$steps[$i];JournalId=[Guid]::NewGuid().ToString('D');
            Origin='https://127.0.0.1:63123/';UserId=$account.UserId;PetId=$account.PetId;LocalOriginalSha256=('A'*64);
            LocalConfigurationSha256=(Get-DangoAccountSyncB1c2ConfigurationSha256 @('a','b','a','a','b')[$i] @(118,125,111,118,130)[$i]);
            CloudConfigurationSha256=(Get-DangoAccountSyncB1c2ConfigurationSha256 @('b','b','a','b','b')[$i] @(103,103,103,125,125)[$i]);
            ConfigurationVersion=@(1,1,1,2,2)[$i];Choice=@(1,0,0,1,0)[$i];ReceiptPresent=($i -ne 2);
            Outcome=@(0,1,$null,0,1)[$i];ObservedCloudVersion=@(1,2,$null,2,3)[$i];FailureCode=$null}}
    [pscustomobject]@{Version=1;OwnerId=$scope.OwnerId;Count=5;ReceiptCount=4;ExactOriginalsAndReceipts=$true;Archives=$rows}}
function FaultEvidence([int]$CancelSegments=0) {
    $scope=Scope;$requests=[Collections.Generic.List[object]]::new();$phases=[Collections.Generic.List[object]]::new();$holds=@();$reads=@{}
    function AddRequest([int]$Client,[string]$Role,[string]$Actor,[string]$Method,[string]$Path,[int]$SessionIndex=-1,[long]$ResponseVersion=0,[long]$ExpectedVersion=0,[double]$EyeSize=0,[bool]$Previous=$false){
        $account=$scope.Accounts[$(if($Actor -ceq 'a'){0}else{1})];$ordinal=$null
        if($Path -ceq '/api/v1/me/pet'){$key=[string]$Client;$ordinal=if($reads.ContainsKey($key)){$reads[$key]+1}else{1};$reads[$key]=$ordinal}
        $requests.Add([pscustomobject]@{Sequence=$requests.Count+1;ClientId=$Client;Role=$Role;Actor=$Actor;Method=$Method;Path=$Path;
            UserId=$(if($Role -ceq 'registration'){$null}else{$account.UserId});PetId=$(if($Role -ceq 'registration'){$null}else{$account.PetId});
            SessionId=$(if($SessionIndex -ge 0){$account.SessionIds[$SessionIndex]}else{$null});PetReadOrdinal=$ordinal;
            ExpectedVersion=$(if($ExpectedVersion -gt 0){$ExpectedVersion}else{$null});ConfigurationSha256=$(if($ExpectedVersion -gt 0){Get-DangoAccountSyncB1c2ConfigurationSha256 $Actor $EyeSize}else{$null});
            PreviousPumpSettled=$Previous;Retired=$false;ResponseStatus=200;ResponseVersion=$(if($ResponseVersion -gt 0){$ResponseVersion}else{$null})})
        return $requests.Count}
    function AddPhase([string]$Phase,$Client=$null,$Request=$null){$phases.Add([pscustomobject]@{Sequence=$phases.Count+1;Phase=$Phase;ClientId=$Client;RequestSequence=$Request})}
    foreach($actor in @('a','b')){$client=if($actor -ceq 'a'){1}else{2};$reg=if($actor -ceq 'a'){'c1111111-1111-4111-8111-111111111111'}else{'c2222222-2222-4222-8222-222222222222'}
        [void](AddRequest $client registration $actor POST '/api/v1/auth/registrations');[void](AddRequest $client registration $actor POST "/api/v1/auth/registrations/$reg/complete")}
    for($i=0;$i -lt 2;$i++){$case=@('late-get','late-patch')[$i];$a=@(3,6)[$i];$b=@(4,7)[$i];$cleanup=@(5,8)[$i]
        AddPhase "${case}:start";[void](AddRequest $a desktop a POST '/api/v1/auth/login' $i)
        [void](AddRequest $a desktop a GET '/api/v1/me' $i);[void](AddRequest $a desktop a GET '/api/v1/me/pet' $i 1)
        if($i -eq 0){[void](AddRequest $a desktop a PATCH '/api/v1/me/profile' $i);$held=AddRequest $a desktop a GET '/api/v1/me/pet' $i 1}
        else{$held=AddRequest $a desktop a PATCH '/api/v1/me/pet/configuration' $i 2 1 111}
        AddPhase actual-response-captured $a $held;AddPhase main-retired $a
        [void](AddRequest $b desktop b POST '/api/v1/auth/login' $i);[void](AddRequest $b desktop b GET '/api/v1/me' $i)
        [void](AddRequest $b desktop b GET '/api/v1/me/pet' $i ($i+1));AddPhase "${case}:new-identity-awaiting-old-pump" $b
        AddPhase "${case}:old-response-released" $a;AddPhase "${case}:old-pump-settled" $a
        if($i -eq 0){[void](AddRequest $b desktop b PATCH '/api/v1/me/profile' $i)}
        $baseline=AddRequest $b desktop b GET '/api/v1/me/pet' $i ($i+1) 0 0 $true;AddPhase "${case}:new-baseline-verified" $b $baseline
        $save=AddRequest $b desktop b PATCH '/api/v1/me/pet/configuration' $i ($i+2) ($i+1) @(125,130)[$i] $true
        AddPhase "${case}:b-save-converged" $b $save
        if($i -lt $CancelSegments){[void](AddRequest $b desktop b GET '/api/v1/me/pet' $i 0 0 0 $true);$requests[$requests.Count-1].ResponseStatus=$null}
        [void](AddRequest $cleanup cleanup a GET '/api/v1/me' $i 0 0 0 $true);[void](AddRequest $cleanup cleanup a GET '/api/v1/me/pet' $i $(if($i -eq 0){1}else{2}) 0 0 $true)
        [void](AddRequest $cleanup cleanup a GET '/api/v1/auth/request-protection' $i 0 0 0 $true);[void](AddRequest $cleanup cleanup a POST '/api/v1/auth/logout' $i 0 0 0 $true)
        AddPhase "${case}:a-session-cleaned" $cleanup;[void](AddRequest $b desktop b POST '/api/v1/auth/logout' $i 0 0 0 $true);AddPhase "${case}:complete" $b
        $holds+=[pscustomobject]@{Case=$case;Method=@('GET','PATCH')[$i];UserId=$scope.Accounts[0].UserId;PetId=$scope.Accounts[0].PetId;
            SessionId=$scope.Accounts[0].SessionIds[$i];ResponseVersion=$i+1;CancellationObserved=$true;Released=$true;Settled=$true;CleanupRelease=$false;ActualSuccessfulResponse=$true}}
    $e=[pscustomobject]@{Version=2;OwnerId=$scope.OwnerId;Completed=$true;Holds=$holds;Requests=@($requests);Phases=@($phases);
        CoordinatorsStopped=$true;DispatcherOperationsSettled=$true;OriginalTasksSettled=$true;StopwatchFrequency=1000;
        LifecycleObserversSettled=$true;LifecycleEvents=@();LogoutWindows=@()}
    AddFaultLifecycle $e;return $e
}
function AddFaultLifecycle($Evidence){
    # Independent fictional trace; never read a saved B1c1 trace or start HTTP/database work.
    $events=[Collections.Generic.List[object]]::new();$windows=@();$tick=0;$lastSendAfter=-600;$opened=@{};$invoked=@{};$bound=@{}
    function AddEvent([string]$Kind,$Request=$null,$Operation=$null,$Coordinator=$null,$Window=$null,$Value=$null){
        if($Kind -ceq 'SendBefore'){$script:traceTick=[Math]::Max($script:traceTick,$script:traceAfter+600)}else{$script:traceTick++}
        $events.Add([pscustomobject]@{Sequence=$events.Count+1;Tick=[long]$script:traceTick;Kind=$Kind;RequestSequence=$Request;
            OperationId=$Operation;CoordinatorId=$Coordinator;WindowId=$Window;Value=$Value})
        if($Kind -ceq 'SendAfter'){$script:traceAfter=$script:traceTick}
    }
    $script:traceTick=0;$script:traceAfter=-600
    foreach($request in $Evidence.Requests){$window=$null;$coordinator=$null;$operation=$null
        $baselineSequence=@($Evidence.Phases|Where-Object {$_.Phase.EndsWith(':new-baseline-verified') -and $_.ClientId -eq $request.ClientId}|ForEach-Object RequestSequence)
        # The real B comparison baseline occurs before createSync; it has no decorator operation or coordinator.
        $beforeBDecorator=$request.Actor -ceq 'b' -and $baselineSequence.Count -eq 1 -and $request.Sequence -le $baselineSequence[0]
        if($request.Role -ceq 'desktop' -and $request.Path -cin @('/api/v1/me/pet','/api/v1/me/pet/configuration') -and -not $beforeBDecorator){
            $coordinator=$request.ClientId;$operation=$request.Sequence
            if(-not $bound.ContainsKey([string]$coordinator)){AddEvent CoordinatorBound -Coordinator $coordinator;$bound[[string]$coordinator]=$true}
            foreach($w in $windows){if($w.ClientId -eq $request.ClientId -and $opened.ContainsKey([string]$w.WindowId)){$window=$w.WindowId}}
            AddEvent OperationStarted -Operation $operation -Coordinator $coordinator -Window $window}
        AddEvent RequestRegistered $request.Sequence $operation $coordinator $window
        AddEvent SendBefore $request.Sequence $operation $coordinator $window;AddEvent SendAfter $request.Sequence $operation $coordinator $window
        if($null -eq $request.ResponseStatus){
            if(-not $invoked.ContainsKey([string]$window)){AddEvent UiaLogoutInvoked -Window $window;$invoked[[string]$window]=$true}
            AddEvent CallerCancelled $request.Sequence $operation $coordinator $window
            AddEvent CoordinatorStopped -Operation $operation -Coordinator $coordinator -Window $window -Value StoppedWithoutFailure
            AddEvent OperationCallerCancelled -Operation $operation -Coordinator $coordinator -Window $window
            AddEvent RequestCanceledTokenMatched $request.Sequence $operation $coordinator $window true
            AddEvent RequestTerminal $request.Sequence $operation $coordinator $window Canceled
            AddEvent OperationTerminal -Operation $operation -Coordinator $coordinator -Window $window -Value Canceled
        }else{
            AddEvent HeadersReceived $request.Sequence $operation $coordinator $window;AddEvent BodyValidated $request.Sequence $operation $coordinator $window
            if($request.Actor -ceq 'b' -and $request.Path -ceq '/api/v1/auth/logout'){
                $w=@($windows|Where-Object ClientId -EQ $request.ClientId)[0]
                if(-not $invoked.ContainsKey([string]$w.WindowId)){AddEvent UiaLogoutInvoked -Window $w.WindowId;$invoked[[string]$w.WindowId]=$true}
                AddEvent CoordinatorStopped -Coordinator $w.CoordinatorId -Window $w.WindowId -Value StoppedWithoutFailure}
            AddEvent RequestTerminal $request.Sequence $operation $coordinator $window RanToCompletion
            if($null -ne $operation){AddEvent OperationTerminal -Operation $operation -Coordinator $coordinator -Window $window -Value RanToCompletion}
        }
        foreach($phase in @($Evidence.Phases|Where-Object RequestSequence -EQ $request.Sequence)){AddEvent Phase -Value $phase.Phase}
        $savePhase=@($Evidence.Phases|Where-Object {$_.RequestSequence -eq $request.Sequence -and $_.Phase.EndsWith(':b-save-converged')})
        if($savePhase.Count -eq 1){$id=$windows.Count+1;$logout=@($Evidence.Requests|Where-Object {$_.ClientId -eq $request.ClientId -and $_.Path -ceq '/api/v1/auth/logout'})[0]
            $w=[pscustomobject]@{WindowId=$id;Case=@('late-get','late-patch')[$id-1];ClientId=$request.ClientId;UserId=$request.UserId;PetId=$request.PetId;
                SessionId=$request.SessionId;CoordinatorId=$coordinator;SaveRequestSequence=$request.Sequence;LogoutRequestSequence=$logout.Sequence;Verified=$true}
            $windows+=$w;AddEvent WindowOpened -Window $id;$opened[[string]$id]=$true}
        if($request.Actor -ceq 'b' -and $request.Path -ceq '/api/v1/auth/logout'){$w=@($windows|Where-Object ClientId -EQ $request.ClientId)[0]
            AddEvent UiaLogoutReturned -Window $w.WindowId;AddEvent WindowClosed -Window $w.WindowId;$opened.Remove([string]$w.WindowId)}
    }
    $Evidence.LifecycleEvents=@($events);$Evidence.LogoutWindows=$windows
}
function Journey {$scope=Scope;$actors=@();foreach($account in $scope.Accounts){$actor=Clone $account;$actor|Add-Member Sessions 2;$actor|Add-Member Revoked 2;
    $actor|Add-Member Attempts 0;$actor|Add-Member MailState 'Pending';$actor|Add-Member RecoveryEpoch $scope.RecoveryEpoch;$actors+=$actor}
    $cases=@();for($i=0;$i -lt 2;$i++){$cases+=[pscustomobject]@{Case=@('late-get','late-patch')[$i];Passed=$true;HeldMethod=@('GET','PATCH')[$i];HeldResponseVersion=$i+1;
        NewBaselineVersion=$i+1;CancellationObserved=$true;ReleaseObserved=$true;HeldResponseSettled=$true;OldPumpSettledBeforeNewBaseline=$true;
        OldMainRequestsAfterStop=0;CancelledLogoutIntent=$true;AOriginalHasReceipt=$false}}
    [pscustomobject]@{Version=1;OwnerId=$scope.OwnerId;Passed=$true;Actors=$actors;Cases=$cases;ConfigurationAttempts=3;ConfigurationSuccessfulWrites=3;
        LoginWrites=4;ProfileWrites=2;LogoutWrites=4;DesktopHosts=2;HttpsHosts=1;HttpRequests=(FaultEvidence).Requests.Count;UsesSystemTrust=$false;UsesWindowsCredentials=$false;
        ActualFormalHost=$true;ActualApplicationHost=$true;HealthyNativeFrames=$true;LocalLogoutIntents=2;DurationSeconds=12.5;DesktopTransports=4;ClientsCreated=8;CleanupPassed=$true}}
function Snapshot {$plan=Plan;$counts=[ordered]@{};$digests=[ordered]@{}
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions','audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')){$counts[$key]=1;$digests[$key]=@(('a'*64))}
    [pscustomobject]@{database=$plan.Database;role=$plan.Role;version=180001;dataDirectory=$plan.DataDirectory;listenAddresses='127.0.0.1';migrations=@($plan.PriorMigrations);
        userIds=@('11111111-1111-1111-1111-111111111111');profiles=@([pscustomobject]@{id='11111111-1111-1111-1111-111111111111';nicknameIsNull=$false;version=3});counts=[pscustomobject]$counts;digests=[pscustomobject]$digests}}
function AfterSnapshot {$e=Snapshot;$scope=Scope
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')){$e.counts.$key=3;$e.digests.$key=@(('a'*64),('b'*64),('c'*64))}
    $e.counts.sessions=5;$e.digests.sessions=@(('a'*64),('b'*64),('c'*64),('d'*64),('e'*64));$e.userIds+=@($scope.Accounts.UserId)
    foreach($account in $scope.Accounts){$e.profiles+=[pscustomobject]@{id=$account.UserId;nicknameIsNull=$false;version=2}};return $e}
function EvidenceContract([string]$Mode='AccountSyncB1c2SmallJson',[string]$Kind='scoped'){$plan=Plan;$sql=switch($Kind){full{Get-DangoAccountSyncDatabaseSql};size{Get-DangoAccountSyncDatabaseSql -MeasureOnly};scoped{Get-DangoAccountSyncB1c2ScopedSql (Scope)}}
    @{Executable=(Join-Path $plan.PostgresBinDirectory 'psql.exe');Arguments=(Get-DangoAccountSyncB1c2PsqlArguments $plan $sql);WorkingDirectory=$plan.Repository;
        Plan=$plan;Port=63237;EnvironmentValues=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};KillOwnedTree=$false;OutputContract=$Mode}}
function FakeProcess {$fake=[pscustomobject]@{Id=40000;StartTime=[DateTime]::UtcNow;ExitTime=[DateTime]::UtcNow;HasExited=$true;ExitCode=1;
    StandardInput=[IO.StringWriter]::new();StandardOutput=[IO.StringReader]::new("  {}`r`n ");StandardError=[IO.StringReader]::new("ERROR: fictional_failure`r`n");Disposed=$false}
    $fake|Add-Member ScriptMethod Start {$true};$fake|Add-Member ScriptMethod WaitForExit {param($Milliseconds)$true};$fake|Add-Member ScriptMethod Kill {param($Tree)};
    $fake|Add-Member ScriptMethod Dispose {$this.Disposed=$true};return $fake}
function RawOptions([string]$Kind='scoped'){$mode=if($Kind -ceq 'full'){'AccountSyncB1c2EvidenceJson'}else{'AccountSyncB1c2SmallJson'};$c=EvidenceContract $mode $Kind
    @{Executable=$c.Executable;Arguments=$c.Arguments;WorkingDirectory=$c.WorkingDirectory;EnvironmentValues=$c.EnvironmentValues;TimeoutSeconds=30;
        OutputContract=$mode;PostgresEvidencePlan=$c.Plan;PostgresEvidencePort=63237}}
function TrxXml {
    # Fixed fictional standard VSTest shape; does not consume or transform saved run evidence.
    return @'
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results><UnitTestResult executionId="33333333-3333-4333-8333-333333333333" testId="22222222-2222-4222-8222-222222222222" testName="FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence" outcome="Passed" /></Results>
  <TestDefinitions><UnitTest name="FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence" id="22222222-2222-4222-8222-222222222222">
    <Execution id="33333333-3333-4333-8333-333333333333" />
    <TestMethod adapterTypeName="executor://mstestadapter/v4" className="Dango.AccountSyncReview.Tests.AccountSyncB1c2JourneyTests" name="FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccountWithLifecycleEvidence" />
  </UnitTest></TestDefinitions>
  <TestEntries><TestEntry testId="22222222-2222-4222-8222-222222222222" executionId="33333333-3333-4333-8333-333333333333" /></TestEntries>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary>
</TestRun>
'@
}
function SaveOfflineTrx([string]$Text){$directory=Join-Path $taskSandbox ('trx-contract/'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($directory);$path=Join-Path $directory 'account-sync-b1c2.trx'
    [IO.File]::WriteAllText($path,$Text,[Text.UTF8Encoding]::new($false));return $path}

Check fixed_plan {$plan=Plan;[void](Assert-DangoAccountSyncB1c2FixedPlan $plan);(Get-DangoProfileFixedPlan $plan).Stage -ceq 'AccountSyncB1c2' -and
    $plan.DesktopHosts -eq 2 -and $plan.HttpsHosts -eq 1 -and $plan.Certificates -eq 1 -and ($plan.Budgets.Values|Measure-Object -Sum).Sum -eq 21 -and
    $plan.MaximumHttpRequests -eq 128 -and $plan.MinimumHttpStartIntervalMilliseconds -eq 600 -and $plan.ProcessTimeoutSeconds -eq 300 -and
    $plan.MaximumDurationMinutes -eq 10 -and $plan.MigrationOperations -eq 0 -and $plan.PriorMigrations.Count -eq 7 -and $plan.MaximumToolProcesses -eq 10 -and -not $plan.RetryAllowed}
Check trx_exact_new_method_class_and_associations {Assert-DangoAccountSyncB1c2Trx (SaveOfflineTrx (TrxXml));$true}
foreach($fault in @('old-name','old-class','method-name','definition-name','result-test-id','entry-test-id','definition-test-id',
    'result-execution-id','entry-execution-id','definition-execution-id','missing-definition','duplicate-definition','not-executed')){
    Check ('trx_reject_'+$fault){[xml]$document=TrxXml
        $result=$document.SelectSingleNode("//*[local-name()='UnitTestResult']")
        $definition=$document.SelectSingleNode("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")
        $entry=$document.SelectSingleNode("//*[local-name()='TestEntry']")
        $method=$definition.SelectSingleNode("./*[local-name()='TestMethod']");$execution=$definition.SelectSingleNode("./*[local-name()='Execution']")
        $other='44444444-4444-4444-8444-444444444444'
        switch($fault){
            old-name{$result.SetAttribute('testName','FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount')}
            old-class{$method.SetAttribute('className','Dango.AccountSyncReview.Tests.AccountSyncB1cJourneyTests')}
            method-name{$method.SetAttribute('name','FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount')}
            definition-name{$definition.SetAttribute('name','FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount')}
            result-test-id{$result.SetAttribute('testId',$other)};entry-test-id{$entry.SetAttribute('testId',$other)};definition-test-id{$definition.SetAttribute('id',$other)}
            result-execution-id{$result.SetAttribute('executionId',$other)};entry-execution-id{$entry.SetAttribute('executionId',$other)};definition-execution-id{$execution.SetAttribute('id',$other)}
            missing-definition{[void]$definition.ParentNode.RemoveChild($definition)}
            duplicate-definition{[void]$definition.ParentNode.AppendChild($definition.CloneNode($true))}
            not-executed{$result.SetAttribute('outcome','NotExecuted');$counters=$document.SelectSingleNode("//*[local-name()='Counters']")
                $counters.SetAttribute('executed','0');$counters.SetAttribute('passed','0')}
        }
        Reject {Assert-DangoAccountSyncB1c2Trx (SaveOfflineTrx $document.OuterXml)}
    }
}
foreach($property in (Plan).PSObject.Properties){if($property.Name -ceq 'Repository'){continue};$field=$property.Name
    Check ('plan_mutation_'+$field){$plan=Plan;if($plan.$field -is [bool]){$plan.$field=-not $plan.$field}elseif($plan.$field -is [int]){$plan.$field++}
        elseif($plan.$field -is [Array]){$plan.$field[0]+='_bad'}elseif($plan.$field -is [Collections.IDictionary]){$plan.$field.Configuration++}else{$plan.$field+='_bad'}
        Reject {Assert-DangoAccountSyncB1c2FixedPlan $plan}}}
Check fixed_scope {Assert-DangoAccountSyncB1c2ScopeInputs (Scope) $owner $epoch @((Scope).Accounts.UserId);$true}
foreach($field in @('Actor','UserId','PetId','DefaultRoomId','Nickname','ProfileVersion','ConfigurationVersion')){foreach($i in 0..1){Check ('scope_mutation_'+$i+'_'+$field){$e=Scope
    if($e.Accounts[$i].$field -is [int]){$e.Accounts[$i].$field++}else{$e.Accounts[$i].$field='bad'};Reject {Assert-DangoAccountSyncB1c2ScopeInputs $e}}}}
foreach($field in (Scope).Accounts[0].Configuration.PSObject.Properties.Name){Check ('configuration_mutation_'+$field){$e=Scope
    if($e.Accounts[0].Configuration.$field -is [string]){$e.Accounts[0].Configuration.$field='#FFFFFF'}else{$e.Accounts[0].Configuration.$field++};Reject {Assert-DangoAccountSyncB1c2ScopeInputs $e}}}
foreach($fault in @('shared-user','shared-pet','shared-room','shared-session','session-order','actor-order','new-user-crossed','extra','case','type')){Check ('scope_'+$fault){$e=Scope
    switch($fault){shared-user{$e.Accounts[1].UserId=$e.Accounts[0].UserId};shared-pet{$e.Accounts[1].PetId=$e.Accounts[0].PetId};shared-room{$e.Accounts[1].DefaultRoomId=$e.Accounts[0].DefaultRoomId};
        shared-session{$e.Accounts[1].SessionIds[0]=$e.Accounts[0].SessionIds[0]};session-order{[Array]::Reverse($e.Accounts[0].SessionIds)};actor-order{[Array]::Reverse($e.Accounts)};
        extra{$e|Add-Member Extra 1};case{$e.Accounts[0].Actor='A'};type{$e.Accounts[0].ProfileVersion=2.0};new-user-crossed{}}
    $users=if($fault -ceq 'new-user-crossed'){@($e.Accounts[0].UserId,[Guid]::NewGuid().ToString('D'))}else{@($e.Accounts.UserId)}
    Reject {Assert-DangoAccountSyncB1c2ScopeInputs $e $owner $epoch $users}}}
Check scoped_sql_ascii_twelve_ids {$sql=Get-DangoAccountSyncB1c2ScopedSql (Scope);$sql -cnotmatch '[^\x00-\x7F]' -and @([regex]::Matches($sql,"'[0-9a-f-]{36}'::uuid")).Count -eq 12 -and
    $sql.Contains('p.configuration=e.configuration') -and $sql.Contains('s.id IN (e.session_one,e.session_two)') -and $sql.Contains('json_object_agg')}
Check scoped_exact_nine_counts {$values=[pscustomobject]@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=2;validSessions=2;revokedSessions=2;exactSessions=2}
    Assert-DangoAccountSyncB1c2ScopedGraph ([pscustomobject]@{a=(Clone $values);b=(Clone $values)});$true}
foreach($actor in @('a','b')){foreach($field in @('users','validUsers','namedUsers','completeGraphs','pendingUnsentEmails','sessions','validSessions','revokedSessions','exactSessions')){
    Check ('scoped_count_'+$actor+'_'+$field){$values=[pscustomobject]@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=2;validSessions=2;revokedSessions=2;exactSessions=2}
        $e=[pscustomobject]@{a=(Clone $values);b=(Clone $values)};$e.$actor.$field++;Reject {Assert-DangoAccountSyncB1c2ScopedGraph $e}}}}
foreach($kind in @('full','size','scoped')){Check ('evidence_contract_'+$kind){$mode=if($kind -ceq 'full'){'AccountSyncB1c2EvidenceJson'}else{'AccountSyncB1c2SmallJson'};$c=EvidenceContract $mode $kind;Assert-DangoAccountSyncB1c2EvidenceContract @c;$true}}
foreach($fault in @('write','extra-select','or','unknown-column','session','config','nickname','readonly','cross-stage','port','extra-argument','extra-environment')){Check ('evidence_reject_'+$fault){$c=EvidenceContract
    switch($fault){write{$c.Arguments[16]+=' DELETE FROM dango.users;'};extra-select{$c.Arguments[16]='SELECT 1;'};or{$c.Arguments[16]=$c.Arguments[16].Replace('AND u.email=e.email','OR u.email=e.email')};
        unknown-column{$c.Arguments[16]=$c.Arguments[16].Replace('s.control_generation','s.unknown_column')};session{$c.Arguments[16]=$c.Arguments[16].Replace('e.session_one,e.session_two','e.session_one,e.session_one')};
        config{$c.Arguments[16]=$c.Arguments[16].Replace('#FFE8C2','#9ECBEF')};nickname{$c.Arguments[16]=$c.Arguments[16].Replace("U&'\65E7\8D26\53F7\56E2\5B50'","'old'")};
        readonly{$c.Arguments[16]=$c.Arguments[16].Replace('BEGIN READ ONLY;','BEGIN;')};cross-stage{$c.Plan=Get-DangoAccountSyncB1bPlan $taskSandbox};port{$c.Port=5432};
        extra-argument{$c.Arguments+=@('-f','bad.sql')};extra-environment{$c.EnvironmentValues.EXTRA='1'}};Reject {Assert-DangoAccountSyncB1c2EvidenceContract @c}}}
Check old_b1b_contract_unchanged {$old=Get-DangoAccountSyncB1bPlan $taskSandbox;$s=Scope
    $sql=Get-DangoAccountSyncB1bScopedSql ([Guid]$s.Accounts[0].UserId) $epoch $owner ([Guid]$s.Accounts[0].PetId) ([Guid]$s.Accounts[0].DefaultRoomId)
    $c=@{Executable=(Join-Path $old.PostgresBinDirectory 'psql.exe');Arguments=(Get-DangoAccountSyncB1bPsqlArguments $old $sql);WorkingDirectory=$old.Repository;Plan=$old;Port=63237;
        EnvironmentValues=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};KillOwnedTree=$false;OutputContract='AccountSyncB1bSmallJson'}
    Assert-DangoAccountSyncB1bEvidenceContract @c;(Get-DangoProfileFixedPlan $old).Stage -ceq 'AccountSyncB1b1'}
Check strict_json_duplicate {Reject {ConvertFrom-DangoAccountSyncB1c2StrictJson '{"Version":1,"Version":1}'} 'accountsync_b1c2_json_rejected'}
Check valid_attempts {(Assert-DangoAccountSyncB1c2Attempts (Plan) $owner (Attempts)).Total -eq 21}
foreach($i in 0..20){foreach($field in @('Sequence','Used')){Check ('attempt_mutation_'+$i+'_'+$field){$e=Attempts;$e[$i].$field++;Reject {Assert-DangoAccountSyncB1c2Attempts (Plan) $owner $e}}}}
Check attempt_unsent_intents_not_extra_budget {Reject {Assert-DangoAccountSyncB1c2Attempts (Plan) $owner (@(Attempts)+@(Attempts)[0])}}
Check valid_resources {(Assert-DangoAccountSyncB1c2Resources $owner (Resources)).DesktopCleanupConfirmed}
foreach($i in 0..7){foreach($field in (Resources)[$i].Facts.PSObject.Properties.Name){if($field -ceq 'Port'){continue};Check ('resource_mutation_'+$i+'_'+$field){$e=Resources
    if($e[$i].Facts.$field -is [bool]){$e[$i].Facts.$field=-not $e[$i].Facts.$field}elseif($e[$i].Facts.$field -is [int]){$e[$i].Facts.$field++}else{$e[$i].Facts.$field='bad'};Reject {Assert-DangoAccountSyncB1c2Resources $owner $e}}}}
Check valid_journey {Assert-DangoAccountSyncB1c2Journey (Journey) (Scope);$true}
foreach($i in 0..1){foreach($field in (Journey).Cases[$i].PSObject.Properties.Name){Check ('case_mutation_'+$i+'_'+$field){$e=Journey
    if($e.Cases[$i].$field -is [bool]){$e.Cases[$i].$field=-not $e.Cases[$i].$field}elseif($e.Cases[$i].$field -is [int]){$e.Cases[$i].$field++}else{$e.Cases[$i].$field='bad'};Reject {Assert-DangoAccountSyncB1c2Journey $e (Scope)}}}}
Check valid_intents {Assert-DangoAccountSyncB1c2LogoutIntents (Intents) (Scope);$true}
foreach($fault in @('forwarded','third','same-session','wrong-user','wrong-case','same-client')){Check ('intents_'+$fault){$e=Intents
    switch($fault){forwarded{$e[0].Forwarded=$true};third{$e+=@((Intents)[0])};same-session{$e[1].SessionId=$e[0].SessionId};wrong-user{$e[0].UserId=(Scope).Accounts[1].UserId};
        wrong-case{$e[0].Case='late-patch'};same-client{$e[1].ClientId=$e[0].ClientId}}
    if($fault -ceq 'same-client'){Reject {Assert-DangoAccountSyncB1c2FaultEvidence (FaultEvidence) (Scope) $e (Journey)}}else{Reject {Assert-DangoAccountSyncB1c2LogoutIntents $e (Scope)}}}}
Check valid_archives {Assert-DangoAccountSyncB1c2Archives (Archives) (Scope) 63123;$true}
foreach($i in 0..4){foreach($field in (Archives).Archives[$i].PSObject.Properties.Name){Check ('archive_mutation_'+$i+'_'+$field){$e=Archives
    if($e.Archives[$i].$field -is [bool]){$e.Archives[$i].$field=-not $e.Archives[$i].$field}elseif($e.Archives[$i].$field -is [int]){$e.Archives[$i].$field++}else{$e.Archives[$i].$field='bad'};
    Reject {Assert-DangoAccountSyncB1c2Archives $e (Scope) 63123}}}}
Check valid_fault_trace {Assert-DangoAccountSyncB1c2FaultEvidence (FaultEvidence) (Scope) (Intents) (Journey);Assert-DangoAccountSyncB1c2AuxAgreement (Archives) (FaultEvidence);$true}
foreach($count in @(1,2)){Check ('valid_confirmed_logout_cancellation_'+$count){$e=FaultEvidence $count;$j=Journey;$j.HttpRequests=$e.Requests.Count
    Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) $j
    @($e.Requests|Where-Object {$null -eq $_.ResponseStatus -and $null -eq $_.ResponseVersion}).Count -eq $count}}
Check valid_unstarted_confirmed_cancellation {$e=FaultEvidence 1;$cancel=@($e.Requests|Where-Object {$null -eq $_.ResponseStatus})[0]
    $e.LifecycleEvents=@($e.LifecycleEvents|Where-Object {-not($_.RequestSequence -eq $cancel.Sequence -and $_.Kind -cin @('SendBefore','SendAfter'))})
    $sequence=0;foreach($event in $e.LifecycleEvents){$event.Sequence=++$sequence};$j=Journey;$j.HttpRequests=$e.Requests.Count
    Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) $j;$null -eq $cancel.ResponseStatus -and $null -eq $cancel.ResponseVersion}
Check unstarted_with_headers_is_impossible {$e=FaultEvidence 1;$cancel=@($e.Requests|Where-Object {$null -eq $_.ResponseStatus})[0]
    $cancel.ResponseStatus=200;$before=@($e.LifecycleEvents|Where-Object {$_.RequestSequence -eq $cancel.Sequence -and $_.Kind -ceq 'SendBefore'})[0]
    $before.Kind='HeadersReceived';$e.LifecycleEvents=@($e.LifecycleEvents|Where-Object {-not($_.RequestSequence -eq $cancel.Sequence -and $_.Kind -ceq 'SendAfter')})
    $sequence=0;foreach($event in $e.LifecycleEvents){$event.Sequence=++$sequence};$j=Journey;$j.HttpRequests=$e.Requests.Count
    Reject {Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) $j}}
Check two_cancellations_in_one_segment_rejected {$e=FaultEvidence 1;$cancel=@($e.Requests|Where-Object {$null -eq $_.ResponseStatus})[0];$extra=Clone $cancel
    $extra.Sequence=$cancel.Sequence+1;$extra.PetReadOrdinal++;$requests=[Collections.ArrayList]::new();foreach($request in $e.Requests){[void]$requests.Add($request)}
    $requests.Insert($cancel.Sequence,$extra);for($i=$cancel.Sequence+1;$i -lt $requests.Count;$i++){$requests[$i].Sequence++}
    foreach($phase in $e.Phases){if($null -ne $phase.RequestSequence -and $phase.RequestSequence -gt $cancel.Sequence){$phase.RequestSequence++}}
    $e.Requests=@($requests);AddFaultLifecycle $e;$j=Journey;$j.HttpRequests=$e.Requests.Count
    Reject {Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) $j}}
foreach($fault in @('old-version','observer-undrained','null-success','headers-non2xx','missing-body','missing-send-after','frequency-zero','under-600ms',
    'wrong-task-terminal','wrong-operation-terminal','wrong-original-token','original-token-missing','wrong-coordinator-stop','failed-stop','fixture-race','bodydeadline-race',
    'timeout-race','wrong-window-client','wrong-window-session','window-unverified','missing-logout200','wrong-canceled-path','wrong-canceled-actor','unknown-coordinator','critical-request','duplicate-window','wrong-op-binding','callback-stop-before-invoke','window-coordinator-crossed','cancel-pump-not-settled','cancel-first-read','terminal-window-missing','operation-terminal-window-missing')){
    Check ('lifecycle_'+$fault){$e=FaultEvidence 1;$j=Journey;$j.HttpRequests=$e.Requests.Count
        $cancel=@($e.Requests|Where-Object {$null -eq $_.ResponseStatus})[0];$requestEvents=@($e.LifecycleEvents|Where-Object RequestSequence -EQ $cancel.Sequence)
        $opCaller=@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'OperationCallerCancelled'})[0]
        $mutate=@($requestEvents|Where-Object Kind -CEQ RequestTerminal)[0]
        switch($fault){
            old-version{$e.Version=1};observer-undrained{$e.LifecycleObserversSettled=$false};null-success{$mutate.Value='RanToCompletion'}
            headers-non2xx{$e.Requests[0].ResponseStatus=503}
            missing-body{(@($e.LifecycleEvents|Where-Object Kind -CEQ BodyValidated)[0]).Kind='CallerCancelled'}
            missing-send-after{(@($requestEvents|Where-Object Kind -CEQ SendAfter)[0]).Kind='CallerCancelled'}
            frequency-zero{$e.StopwatchFrequency=0};under-600ms{$e.StopwatchFrequency=1001}
            wrong-task-terminal{$mutate.Value='Faulted'}
            wrong-operation-terminal{(@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'OperationTerminal' -and $_.OperationId -eq $opCaller.OperationId})[0]).Value='RanToCompletion'}
            wrong-original-token{(@($requestEvents|Where-Object Kind -CEQ RequestCanceledTokenMatched)[0]).Value='false'}
            original-token-missing{$opCaller.Kind='OperationTerminal';$opCaller.Value='Canceled'}
            wrong-coordinator-stop{(@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'CoordinatorStopped' -and $_.OperationId -eq $opCaller.OperationId})[0]).CoordinatorId=99}
            failed-stop{(@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'CoordinatorStopped' -and $_.OperationId -eq $opCaller.OperationId})[0]).Value='StoppedWithFailure'}
            fixture-race{(@($requestEvents|Where-Object Kind -CEQ CallerCancelled)[0]).Kind='FixtureCancelled'}
            bodydeadline-race{(@($requestEvents|Where-Object Kind -CEQ CallerCancelled)[0]).Kind='BodyDeadlineCancelled'}
            timeout-race{$e.StopwatchFrequency=1}
            wrong-window-client{$e.LogoutWindows[0].ClientId=99};wrong-window-session{$e.LogoutWindows[0].SessionId=(Scope).Accounts[1].SessionIds[1]}
            window-unverified{$e.LogoutWindows[0].Verified=$false};missing-logout200{$e.Requests[$e.LogoutWindows[0].LogoutRequestSequence-1].ResponseStatus=$null}
            wrong-canceled-path{$cancel.Path='/api/v1/me/profile'};wrong-canceled-actor{$cancel.Actor='a'}
            unknown-coordinator{(@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'CoordinatorBound' -and $_.CoordinatorId -eq $opCaller.CoordinatorId})[0]).CoordinatorId=99}
            critical-request{$e.Phases[6].RequestSequence=$cancel.Sequence}
            duplicate-window{$e.LogoutWindows[1].WindowId=1}
            wrong-op-binding{$opCaller.CoordinatorId=99}
            callback-stop-before-invoke{$callbackStop=@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'CoordinatorStopped' -and $_.OperationId -eq $opCaller.OperationId})[0]
                $invoke=@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'UiaLogoutInvoked' -and $_.WindowId -eq 1})[0]
                $callbackStop.Kind='UiaLogoutInvoked';$callbackStop.OperationId=$null;$callbackStop.CoordinatorId=$null;$callbackStop.Value=$null
                $invoke.Kind='CoordinatorStopped';$invoke.OperationId=$opCaller.OperationId;$invoke.CoordinatorId=$opCaller.CoordinatorId;$invoke.Value='StoppedWithoutFailure'}
            window-coordinator-crossed{$e.LogoutWindows[0].CoordinatorId=99}
            cancel-pump-not-settled{$cancel.PreviousPumpSettled=$false}
            cancel-first-read{$cancel.PetReadOrdinal=1}
            terminal-window-missing{$mutate.WindowId=$null}
            operation-terminal-window-missing{(@($e.LifecycleEvents|Where-Object {$_.Kind -ceq 'OperationTerminal' -and $_.OperationId -eq $opCaller.OperationId})[0]).WindowId=$null}
        }
        Reject {Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) $j}
    }
}
Check old_stage_rejected {$p=Plan;$p.Stage='AccountSyncB1c1';Reject {Assert-DangoAccountSyncB1c2FixedPlan $p}}
Check old_grant_rejected {$p=Plan;$p.ApprovalVariable='DANGO_BACKEND_ACCOUNTSYNC_B1C1_APPROVED';Reject {Assert-DangoAccountSyncB1c2FixedPlan $p}}
foreach($i in 0..19){foreach($field in @('Sequence','Phase','ClientId','RequestSequence')){Check ('phase_mutation_'+$i+'_'+$field){$e=FaultEvidence
    if($field -ceq 'Phase'){$e.Phases[$i].Phase='bad'}else{$e.Phases[$i].$field=99};Reject {Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) (Journey)}}}}
foreach($fault in @('held-method','cleanup-release','unsettled','patch-hash','patch-version','crossed-user','late-old-main','wrong-cleanup-role','baseline-pump','baseline-ordinal','session','retired','duplicate-request','cleanup-extra-login')){Check ('trace_'+$fault){$e=FaultEvidence
    switch($fault){held-method{$e.Holds[0].Method='PATCH'};cleanup-release{$e.Holds[0].CleanupRelease=$true};unsettled{$e.OriginalTasksSettled=$false};
        patch-hash{($e.Requests|Where-Object {$_.Path -ceq '/api/v1/me/pet/configuration'}|Select-Object -First 1).ConfigurationSha256='F'*64};
        patch-version{($e.Requests|Where-Object {$_.Path -ceq '/api/v1/me/pet/configuration'}|Select-Object -First 1).ExpectedVersion=2};
        crossed-user{$e.Requests[4].UserId=(Scope).Accounts[1].UserId};late-old-main{$e.Requests[-1].ClientId=3};wrong-cleanup-role{($e.Requests|Where-Object {$_.Role -ceq 'cleanup'}|Select-Object -First 1).Role='desktop'};
        baseline-pump{$e.Requests[$e.Phases[6].RequestSequence-1].PreviousPumpSettled=$false};baseline-ordinal{$e.Requests[$e.Phases[6].RequestSequence-1].PetReadOrdinal=1};
        session{$e.Holds[0].SessionId=(Scope).Accounts[1].SessionIds[0]};retired{$e.Requests[0].Retired=$true};duplicate-request{$e.Requests[1].Sequence=1};
        cleanup-extra-login{($e.Requests|Where-Object {$_.Role -ceq 'cleanup'}|Select-Object -First 1).Path='/api/v1/auth/login'}}
    Reject {Assert-DangoAccountSyncB1c2FaultEvidence $e (Scope) (Intents) (Journey)}}}
Check graph_exact_two_users {Assert-DangoAccountSyncB1c2Target (Plan) (Snapshot);$new=@(Assert-DangoAccountSyncB1c2OldGraph (Snapshot) (AfterSnapshot));
    Assert-DangoAccountSyncB1c2ScopeInputs (Scope) $owner $epoch $new;$new.Count -eq 2}
foreach($key in (Snapshot).digests.PSObject.Properties.Name){Check ('old_multiset_mutation_'+$key){$after=AfterSnapshot;$after.digests.$key[0]='f'*64;Reject {Assert-DangoAccountSyncB1c2OldGraph (Snapshot) $after} 'profile_run_old_graph_changed'}}
Check old_multiset_duplicate_required {$before=Snapshot;$after=AfterSnapshot;$before.counts.audit=2;$before.digests.audit=@(('a'*64),('a'*64));$after.counts.audit=2;$after.digests.audit=@(('a'*64),('f'*64));Reject {Assert-DangoAccountSyncB1c2OldGraph $before $after} 'profile_run_old_graph_changed'}
Check protected_unapproved_delta {$after=AfterSnapshot;$after.counts.audit=2;$after.digests.audit=@(('a'*64),('b'*64));Reject {Assert-DangoAccountSyncB1c2OldGraph (Snapshot) $after} 'profile_run_unapproved_graph_created'}
$fakeIdentity={param($process,$expected)[pscustomobject]@{Verified=$true;ImagePath=$expected;NativeErrorCode=0;Pid=$process.Id;CreatedAt=$process.StartTime.ToUniversalTime()}}
foreach($kind in @('full','size','scoped')){Check ('shared_raw_'+$kind){$fake=FakeProcess;$options=RawOptions $kind
    $r=Invoke-DangoProfileProcess @options -ProcessFactory {param($info)$fake} -IdentityProbe $fakeIdentity
    $r.RawOutput -ceq "  {}`r`n " -and $r.RawError -ceq "ERROR: fictional_failure`r`n" -and $r.Receipt.OutputComplete -and $r.Receipt.OutputResourcesCleanupConfirmed -and $fake.Disposed}}
foreach($stream in @('stdout-small','stderr-small','stdout-full','stderr-full')){foreach($representation in @('chars','utf8')){Check ('output_bound_'+$stream+'_'+$representation){$fake=FakeProcess;$options=RawOptions $(if($stream.EndsWith('full')){'full'}else{'scoped'});
    $limit=if($stream -ceq 'stdout-full'){1048576}else{131072};$text=if($representation -ceq 'chars'){'A'*($limit+1)}else{'界'*([int][Math]::Floor($limit/3)+1)}
    if($stream.StartsWith('stdout')){$fake.StandardOutput=[IO.StringReader]::new($text)}else{$fake.StandardError=[IO.StringReader]::new($text)}
    $receipts=[Collections.Generic.List[object]]::new();$rejected=Reject {Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $fakeIdentity} 'profile_run_tool_output_bound_exceeded'
    $rejected -and -not $receipts[0].OutputComplete -and $fake.Disposed}}}
$originalFixed=& $taskModule {(Get-Command Assert-DangoAccountSyncB1c2FixedPlan).ScriptBlock};$writerDirectory=Join-Path $taskSandbox 'raw-writer';[void][IO.Directory]::CreateDirectory($writerDirectory)
try{& $taskModule {param($directory)$script:taskOfflineDirectory=$directory;function script:Assert-DangoAccountSyncB1c2FixedPlan {param($Plan)$fixed=Get-DangoAccountSyncB1c2Plan $Plan.Repository;$fixed.EvidenceDirectory=$script:taskOfflineDirectory;$fixed}} $writerDirectory
    Check raw_create_new_preserves_bytes {Save-DangoAccountSyncB1c2RawEvidence (Plan) 'before-size.json' "  {}`r`n ";[IO.File]::ReadAllText((Join-Path $writerDirectory 'before-size.json')) -ceq "  {}`r`n "}
    Check raw_duplicate_rejected {$old=[IO.File]::ReadAllText((Join-Path $writerDirectory 'before-size.json'));$caught=$false;try{Save-DangoAccountSyncB1c2RawEvidence (Plan) 'before-size.json' 'overwrite'}catch{$caught=$true};$caught -and [IO.File]::ReadAllText((Join-Path $writerDirectory 'before-size.json')) -ceq $old}
    Check raw_full_mib {Save-DangoAccountSyncB1c2RawEvidence (Plan) 'before.json' ('A'*1048576) 1048576;(Get-Item -LiteralPath (Join-Path $writerDirectory 'before.json')).Length -eq 1048576}
    Check raw_bound {Reject {Save-DangoAccountSyncB1c2RawEvidence (Plan) 'after.json' ('A'*1048577) 1048576}}
    Check raw_path {Reject {Save-DangoAccountSyncB1c2RawEvidence (Plan) '../escape.json' '{}'}}
}finally{& $taskModule {param($original)Set-Item -LiteralPath Function:script:Assert-DangoAccountSyncB1c2FixedPlan -Value $original} $originalFixed}
$runnerTokens=$null;$runnerErrors=$null;$runnerAst=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($RunnerPath),[ref]$runnerTokens,[ref]$runnerErrors)
Check runner_parser {@($runnerErrors).Count -eq 0}
foreach($name in @('Invoke-DangoAccountSyncB1c2Tool')){$function=$runnerAst.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$true);
    if($null -eq $function){throw 'b1c2_offline_runner_function_missing'};Invoke-Expression $function.Extent.Text}
function Assert-DangoAccountSyncB1c2FrozenInputs {}
function Invoke-DangoProfileProcess {param($Executable,$Arguments,$WorkingDirectory,$EnvironmentValues,$TimeoutSeconds,$KillOwnedTree,$Receipts,$OutputContract,$PostgresStartPlan,$PostgresStartPort,$PostgresEvidencePlan,$PostgresEvidencePort)
    $record=[pscustomobject]@{Executable=$Executable;Arguments=$Arguments;OutputContract=$OutputContract;OutputComplete=$true};$Receipts.Add($record);
    [pscustomobject]@{ExitCode=0;Receipt=$record;Output='fictional';RawOutput='fictional';RawError=''}}
function ResetRunner {$script:taskB1cPlan=Plan;$script:taskB1cToolAttempts=0;$script:taskB1cToolIndex=0;$script:taskB1cStopAttempted=$false;$script:taskB1cStopOwnershipVerified=$false;
    $script:taskB1cTools=[Collections.Generic.List[object]]::new();$script:taskB1cFacts=[pscustomobject]@{Port=63237;Epoch=$epoch};$script:taskB1cScope=Scope;
    $script:taskB1cControl=Join-Path $taskB1cPlan.PostgresBinDirectory 'pg_ctl.exe';$script:taskB1c2Postgres=Join-Path $taskB1cPlan.PostgresBinDirectory 'postgres.exe';
    $script:taskB1cPsql=Join-Path $taskB1cPlan.PostgresBinDirectory 'psql.exe';$script:taskB1cDotnet=Join-Path $taskSandbox 'fake-dotnet.exe';
    $script:taskB1cTestDll=Join-Path $taskB1cPlan.Repository ($taskB1cPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll');
    $script:taskB1cPsqlEnvironment=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};$script:taskB1cTestEnvironment=@{DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED='1'}}
function RunStep([string]$Step){$exe=$taskB1cControl;$environment=@{};$timeout=30;$contract='CompleteEof';$tree=$false
    $arguments=switch -CaseSensitive ($Step){version{$exe=$taskB1c2Postgres;@('--version')};status{@('status','-D',$taskB1cPlan.DataDirectory)};
        start{$timeout=40;$contract='PostgresStartIgnored';@('start','-D',$taskB1cPlan.DataDirectory,'-l',(Join-Path $taskB1cPlan.EvidenceDirectory 'postgresql.log'),'-o','-h 127.0.0.1 -p 63237','-w','-t','30')};
        {$_ -cin @('before-size','after-size')}{$exe=$taskB1cPsql;$environment=$taskB1cPsqlEnvironment;$contract='AccountSyncB1c2SmallJson';Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncDatabaseSql -MeasureOnly)};
        {$_ -cin @('before-full','after-full')}{$exe=$taskB1cPsql;$environment=$taskB1cPsqlEnvironment;$contract='AccountSyncB1c2EvidenceJson';Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncDatabaseSql)};
        vstest{$exe=$taskB1cDotnet;$environment=$taskB1cTestEnvironment;$timeout=300;$tree=$true;@('vstest',$taskB1cTestDll,('--TestCaseFilter:'+$taskB1cPlan.Filter),('--Logger:trx;LogFileName='+$taskB1cPlan.TrxFileName),('--ResultsDirectory:'+$taskB1cPlan.EvidenceDirectory))};
        scoped{$exe=$taskB1cPsql;$environment=$taskB1cPsqlEnvironment;$contract='AccountSyncB1c2SmallJson';Get-DangoAccountSyncB1c2PsqlArguments $taskB1cPlan (Get-DangoAccountSyncB1c2ScopedSql (Scope))};
        stop{$timeout=40;@('stop','-D',$taskB1cPlan.DataDirectory,'-m','fast','-w','-t','30')}}
    Invoke-DangoAccountSyncB1c2Tool $Step $exe ([string[]]$arguments) $environment -TimeoutSeconds $timeout -OutputContract $contract -KillOwnedTree:$tree|Out-Null}
Check runner_wrong_first {ResetRunner;Reject {RunStep vstest}}
Check runner_exact_ten {ResetRunner;foreach($step in @('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped')){RunStep $step};
    $script:taskB1cStopOwnershipVerified=$true;RunStep stop;$taskB1cToolAttempts -eq 10 -and $taskB1cTools.Count -eq 10}
Check runner_eleventh {Reject {RunStep stop}}
Check runner_stop_ownership {ResetRunner;foreach($step in @('version','status','start')){RunStep $step};Reject {RunStep stop}}
Check runner_failure_stop_no_retry {$script:taskB1cStopOwnershipVerified=$true;RunStep stop;Reject {RunStep before-size}}
Check runner_extra_argument {ResetRunner;Reject {Invoke-DangoAccountSyncB1c2Tool version $taskB1c2Postgres @('--version','extra')}}
$runnerText=[IO.File]::ReadAllText([IO.Path]::GetFullPath($RunnerPath))
Check runner_reserves_before_original_reads {$runnerText.IndexOf('$taskB1cReservation=New-DangoAccountSyncB1c2Reservation',[StringComparison]::Ordinal) -lt $runnerText.IndexOf('$taskB1cState=Read-DangoAccountSyncB1c2BoundedState',[StringComparison]::Ordinal)}
Check runner_requires_scope_intents {$runnerText.Contains('$taskB1cScopeValidated -and $taskB1cIntentsValidated') -and $runnerText.Contains("'logout-intents.jsonl') 2") -and $runnerText.Contains("'attempts.jsonl') 21") -and $runnerText.Contains("'resources.jsonl') 8")}
Check runner_only_new_stage_approval {$runnerText.Contains("DANGO_BACKEND_ACCOUNTSYNC_B1C2_APPROVED='1'") -and
    $runnerText -cnotmatch 'DANGO_BACKEND_(?:PROFILE|ACCOUNTSYNC_B1A|ACCOUNTSYNC_B1B|ACCOUNTSYNC_B1C1)' -and
    $runnerText.IndexOf('$taskB1cApproved=Assert-DangoAccountSyncB1c2Approval',[StringComparison]::Ordinal) -lt
    $runnerText.IndexOf('$taskB1cReservation=New-DangoAccountSyncB1c2Reservation',[StringComparison]::Ordinal)}
$sqlArtifact=Join-Path $taskSandbox 'actual-b1c2-scoped-sql.json';$scope=Scope
[IO.File]::WriteAllText($sqlArtifact,([pscustomobject]@{Version=1;Scope=$scope;Sql=(Get-DangoAccountSyncB1c2ScopedSql $scope)}|ConvertTo-Json -Depth 16 -Compress),[Text.UTF8Encoding]::new($false))
$resultPath=Join-Path $taskSandbox 'offline-results.json';$failed=@($checks|Where-Object {-not $_.Passed})
[IO.File]::WriteAllText($resultPath,([pscustomobject]@{Version=1;Passed=$checks.Count-$failed.Count;Failed=$failed.Count;Checks=@($checks);SourceHashes=$sourceHashes;
    SqlArtifact=$sqlArtifact;UsesRealBackendResources=$false}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
if($failed.Count){$failed|Format-Table -AutoSize;throw ('b1c2_offline_failed:'+ $failed.Count)}
Write-Output ('b1c2_offline_passed:'+ $checks.Count+';evidence='+$resultPath+';sql='+$sqlArtifact)

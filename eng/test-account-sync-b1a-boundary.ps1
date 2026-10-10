[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1'))

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force
Import-Module $ModulePath -Force

# Pure offline contract checks. Only a UUID directory beneath repository artifacts
# is created. No runner, reservation, database, socket, certificate, credential,
# state directory, key ring or installed executable is opened or invoked.
$taskRepo=Split-Path -Parent $PSScriptRoot
$taskSandbox=Join-Path $taskRepo ('artifacts/account-sync-b1a2-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$taskChecks=[Collections.Generic.List[object]]::new()
$taskOwner=[Guid]'86d87632-8216-4abd-86e9-93bfa9b58644'
$taskOldUser='61a82454-b7bc-40b9-a322-1cb5936cf5a2'
$taskNewUser='ca6d7ad8-4c04-417c-a729-c2de2d9663ad'
$taskKeys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
    'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
$taskTables=@('test_invitations','pending_registrations','email_challenges','email_jobs','users','pets','rooms','room_memberships',
    'pet_room_states','auth_sessions','audit_logs','domain_outbox','room_device_leases','public_room_credentials','room_bans',
    'user_claims','user_logins','user_tokens','accessory_blobs','accessory_asset_references','accessory_current_outfits','accessory_commit_receipts')
$taskAccessory=@('accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
$taskAdds=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')
$taskSeven=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle','20261009031410_RoomManagement',
    '20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity','20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
$taskFrozen=@('eng/Dango.AccountSyncPostgres.psm1','eng/Dango.AccountProfilePostgres.psm1','eng/invoke-account-sync-b1a-tests.ps1',
    'eng/test-account-sync-b1a-boundary.ps1','eng/test-account-sync-b1a-runner-boundary.ps1',
    'src/Dango.Desktop/Properties/AssemblyInfo.cs','src/Dango.Desktop/AppShell/DangoApplicationHost.cs',
    'src/Dango.Desktop/AppShell/DangoApplicationHost.Accounts.cs','src/Dango.Desktop/AppShell/DangoApplicationHost.AccountDependencies.cs',
    'src/Dango.Desktop/AppShell/DangoApplicationHost.Persistence.cs','eng/tools/Dango.AccountSyncReview.Tests/Dango.AccountSyncReview.Tests.csproj',
    'Directory.Build.props','global.json')

function Check([string]$TestName,[scriptblock]$Body) {
    $passed=$false;$failure=$null
    try {$passed=[bool](& $Body)} catch {$failure=$_.Exception.Message}
    $taskChecks.Add([pscustomobject]@{Name=$TestName;Passed=$passed;Failure=$failure})
}
function Rejected([scriptblock]$Body,[string]$Code) {
    try {[void](& $Body)} catch {
        if ([string]::IsNullOrEmpty($Code) -or $_.Exception.Message.Contains($Code,[StringComparison]::Ordinal)) {return $true}
        throw ('offline_rejection_code_mismatch: expected='+$Code+'; actual='+$_.Exception.Message)
    }
    throw 'offline_expected_rejection_was_not_thrown'
}
function Plan {Get-DangoAccountSyncPlan $taskSandbox}
function Clone($Value) {ConvertFrom-Json (ConvertTo-Json $Value -Depth 25 -Compress)}
function Graph {
    $counts=[ordered]@{};$digests=[ordered]@{}
    foreach($key in $taskKeys) {$counts[$key]=[int]1;$digests[$key]=[string[]]@(('a'*64))}
    [pscustomobject]@{counts=[pscustomobject]$counts;digests=[pscustomobject]$digests;userIds=@($taskOldUser);
        profiles=@([pscustomobject]@{id=$taskOldUser;nicknameIsNull=$false;version=3})}
}
function AfterGraph {
    $graph=Graph
    foreach($key in $taskAdds) {$graph.counts.$key=2;$graph.digests.$key=@(('a'*64),('b'*64))}
    $graph.counts.sessions=5;$graph.digests.sessions=@(('a'*64),('b'*64),('c'*64),('d'*64),('e'*64))
    $graph.userIds=@($taskOldUser,$taskNewUser)
    $graph.profiles+=([pscustomobject]@{id=$taskNewUser;nicknameIsNull=$false;version=3})
    return $graph
}
function Target {
    $graph=Graph;$plan=Plan
    $graph|Add-Member database $plan.Database
    $graph|Add-Member role $plan.Role
    $graph|Add-Member version ([int]180001)
    $graph|Add-Member listenAddresses '127.0.0.1'
    $graph|Add-Member dataDirectory $plan.DataDirectory
    $graph|Add-Member migrations @($taskSeven)
    return $graph
}
function Attempts {
    $rows=@();$sequence=0
    foreach($key in @('Invite','Start','Confirm','Complete','Login','Profile','Configuration','Logout')) {
        $count=@{Invite=1;Start=1;Confirm=1;Complete=1;Login=4;Profile=2;Configuration=5;Logout=4}[$key]
        for($used=1;$used -le $count;$used++) {$sequence++;$rows+=([pscustomobject]@{Version=1;OwnerId=$taskOwner.ToString('D');Sequence=$sequence;Operation=$key;Used=$used;AtUtc='2026-10-10T00:00:00+00:00'})}
    }
    return ,$rows
}
function Resources {
    $phases=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','DesktopStarted','DesktopDisposed','DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed')
    $rows=@()
    for($i=0;$i -lt $phases.Count;$i++) {
        $facts=switch($phases[$i]) {
            TLSCreated {[pscustomobject]@{Port=62123;CertificateSha256=('A'*64)}}
            HostStarted {[pscustomobject]@{Port=62123;WorkersDisabled=$true}}
            DesktopStarted {[pscustomobject]@{Index=([int][Math]::Floor(($i-2)/2)+1);Attempt=$true}}
            DesktopDisposed {[pscustomobject]@{Index=([int][Math]::Floor(($i-2)/2)+1);Disposed=$true;HwndClosed=$true;ConfigurationPumpDrained=$true;UiaWorkerSettled=$true}}
            HostDisposed {[pscustomobject]@{Stopped=$true;Disposed=$true;InFlightRequests=0}}
            TLSDisposed {[pscustomobject]@{NativeKeyIdentityCaptured=$true;NativeCleanupConfirmed=$true;NativeProbeFailed=$false}}
        }
        $rows+=([pscustomobject]@{Version=1;OwnerId=$taskOwner.ToString('D');Sequence=($i+1);Phase=$phases[$i];Facts=$facts})
    }
    return ,$rows
}
function StartContract {
    $plan=Plan;$port=$plan.DatabasePort
    @{Executable=(Join-Path $plan.PostgresBinDirectory 'pg_ctl.exe');Arguments=[string[]]@('start','-D',$plan.DataDirectory,'-l',
        (Join-Path $plan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $port",'-w','-t','30');WorkingDirectory=$plan.Repository;
        Plan=$plan;Port=$port;EnvironmentValues=@{};KillOwnedTree=$false}
}
function EvidenceContract {
    $plan=Plan;$port=$plan.DatabasePort
    @{Executable=(Join-Path $plan.PostgresBinDirectory 'psql.exe');Arguments=[string[]]@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1',
        '-h','127.0.0.1','-p',[string]$port,'-U',$plan.Role,'-d',$plan.Database,'-c',(Get-DangoAccountSyncDatabaseSql));
        WorkingDirectory=$plan.Repository;Plan=$plan;Port=$port;EnvironmentValues=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};KillOwnedTree=$false}
}
function WriteFixture([string]$Relative,[string]$Content='fictional-offline-source') {
    $path=Join-Path $taskSandbox $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path,$Content,[Text.UTF8Encoding]::new($false))
    return $path
}

Check 'FixedPlanHasExactB1a2ScopeAndSevenMigrations' {
    $plan=Plan;[void](Assert-DangoAccountSyncFixedPlan $plan)
    $plan.Stage -ceq 'AccountSyncB1a2' -and $plan.RunName -ceq 'run20261008b0' -and $plan.ExpectedTests -eq 1 -and
        [IO.Path]::GetFileName($plan.EvidenceDirectory) -ceq 'run20261010accountsyncb1a2' -and
        $plan.CandidateDirectory -ceq 'artifacts/account-sync-b1a2-candidate/bin/Dango.AccountSyncReview.Tests/release' -and
        $plan.ApprovalVariable -ceq 'DANGO_BACKEND_ACCOUNTSYNC_B1A2_APPROVED' -and $plan.TrxFileName -ceq 'account-sync-b1a2.trx' -and
        $plan.MaximumToolProcesses -eq 10 -and $plan.MaximumHttpRequests -eq 512 -and $plan.MaximumDurationMinutes -eq 10 -and
        $plan.HttpsHosts -eq 1 -and $plan.Certificates -eq 1 -and $plan.DesktopHosts -eq 3 -and
        $plan.MigrationOperations -eq 0 -and $plan.RoomOperations -eq 0 -and $plan.SystemTrustOperations -eq 0 -and
        $plan.WindowsCredentialOperations -eq 0 -and $plan.EmailDispatchOperations -eq 0 -and
        ($plan.PriorMigrations -join ',') -ceq ($taskSeven -join ',') -and
        ($plan.Budgets.Values|Measure-Object -Sum).Sum -eq 19
}
foreach($property in (Plan).PSObject.Properties) {
    if($property.Name -ceq 'Repository') {continue} # Repository is deliberately the fictional test root.
    $name=$property.Name
    Check ('FixedPlanRejectsChanged_'+$name) {
        $plan=Plan
        if($plan.$name -is [bool]) {$plan.$name=-not $plan.$name}
        elseif($plan.$name -is [int]) {$plan.$name++}
        elseif($plan.$name -is [Collections.IDictionary]) {$plan.$name['Login']++}
        elseif($plan.$name -is [Array]) {$plan.$name[0]+='fictional-change'}
        else {$plan.$name+='fictional-change'}
        Rejected {Assert-DangoAccountSyncFixedPlan $plan} 'accountsync_fixed_plan_rejected'
    }
}
Check 'FixedPlanRejectsExtraProperty' {$plan=Plan;$plan|Add-Member UnreviewedOperation 1;Rejected {Assert-DangoAccountSyncFixedPlan $plan} 'accountsync_fixed_plan_rejected'}
Check 'FixedPlanRejectsMissingProperty' {$plan=Plan;$plan.PSObject.Properties.Remove('RoomOperations');Rejected {Assert-DangoAccountSyncFixedPlan $plan} 'accountsync_fixed_plan_rejected'}
Check 'FixedPlanRejectsNumericStringBudget' {$plan=Plan;$plan.Budgets.Login='4';Rejected {Assert-DangoAccountSyncFixedPlan $plan} 'accountsync_fixed_plan_rejected'}
Check 'ImportedProfileHelperResolvesTheAccountSyncFixedPlan' {
    $fixed=Get-DangoProfileFixedPlan (Plan)
    [void](Assert-DangoAccountSyncFixedPlan $fixed)
    $fixed.Stage -ceq 'AccountSyncB1a2'
}
foreach($oldField in @('Stage','EvidenceDirectory','CandidateDirectory','ApprovalVariable','TrxFileName')) {
    Check ('FixedPlanRejectsConsumedB1a_'+$oldField) {
        $plan=Plan
        $plan.$oldField=switch($oldField) {
            Stage {'AccountSyncB1a'}
            EvidenceDirectory {Join-Path (Split-Path -Parent $plan.EvidenceDirectory) 'run20261010accountsyncb1a'}
            CandidateDirectory {'artifacts/account-sync-b1a-candidate/bin/Dango.AccountSyncReview.Tests/release'}
            ApprovalVariable {'DANGO_BACKEND_ACCOUNTSYNC_B1A_APPROVED'}
            TrxFileName {'account-sync-b1a.trx'}
        }
        Rejected {Assert-DangoAccountSyncFixedPlan $plan} 'accountsync_fixed_plan_rejected'
    }
}
Check 'ImportedProfileHelperRejectsConsumedB1aDispatch' {
    $plan=Plan;$plan.Stage='AccountSyncB1a'
    Rejected {Get-DangoProfileFixedPlan $plan} 'profile_run_fixed_plan_rejected'
}

Check 'ExactReadOnlySqlHashesAllTwentyTwoCompleteRowsIncludingUserNicknameAndVersion' {
    $sql=Get-DangoAccountSyncDatabaseSql
    $sql.StartsWith('BEGIN READ ONLY;',[StringComparison]::Ordinal) -and $sql.TrimEnd().EndsWith('COMMIT;',[StringComparison]::Ordinal) -and
        $sql -notmatch '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\b' -and
        @($taskTables|Where-Object {$sql -notmatch ('FROM dango\.'+$_+'\b')}).Count -eq 0 -and
        @($taskKeys|Where-Object {-not $sql.Contains(("'"+$_+"'"),[StringComparison]::Ordinal)}).Count -eq 0 -and
        $sql.Contains("convert_to(to_jsonb(t)::text,'UTF8')),'hex') AS hash FROM dango.users t",[StringComparison]::Ordinal) -and
        -not $sql.Contains("- 'nickname'",[StringComparison]::Ordinal) -and -not $sql.Contains("- 'profile_version'",[StringComparison]::Ordinal)
}
Check 'MeasureOnlySqlKeepsCompleteUserHashAndReadOnlyFraming' {
    $sql=Get-DangoAccountSyncDatabaseSql -MeasureOnly
    $sql.Contains("json_build_object('utf8Octets',octet_length(convert_to(",[StringComparison]::Ordinal) -and
        $sql.Contains("convert_to(to_jsonb(t)::text,'UTF8')),'hex') AS hash FROM dango.users t",[StringComparison]::Ordinal) -and
        $sql.StartsWith('BEGIN READ ONLY;',[StringComparison]::Ordinal) -and $sql.TrimEnd().EndsWith('COMMIT;',[StringComparison]::Ordinal) -and
        -not $sql.Contains("- 'nickname'",[StringComparison]::Ordinal) -and -not $sql.Contains("- 'profile_version'",[StringComparison]::Ordinal)
}
Check 'StartContractAcceptsOnlyTheReviewedLocalCommandWithoutStartingIt' {$c=StartContract;Assert-DangoAccountSyncStartContract @c;$true}
foreach($case in @('executable','directory','port_zero','port_large','port_other','listener','data','log','timeout','extra_argument','environment','kill')) {
    Check ('StartContractRejects_'+$case) {
        $c=StartContract
        switch($case) {
            executable {$c.Executable=Join-Path $taskSandbox 'fictional.exe'};directory {$c.WorkingDirectory=Join-Path $taskSandbox 'other'}
            port_zero {$c.Port=0};port_large {$c.Port=65536};listener {$c.Arguments[6]='-h 0.0.0.0 -p 62123'}
            port_other {$c.Port++;$c.Arguments[6]="-h 127.0.0.1 -p $($c.Port)"}
            data {$c.Arguments[2]=Join-Path $taskSandbox 'fictional-pgdata'};log {$c.Arguments[4]=Join-Path $taskSandbox 'other.log'}
            timeout {$c.Arguments[9]='300'};extra_argument {$c.Arguments+='restart'};environment {$c.EnvironmentValues.EXTRA='fictional'};kill {$c.KillOwnedTree=$true}
        }
        Rejected {Assert-DangoAccountSyncStartContract @c} 'accountsync_start_contract_rejected'
    }
}
Check 'EvidenceContractAcceptsOnlyExactReadOnlyQueryWithoutOpeningConnection' {$c=EvidenceContract;Assert-DangoAccountSyncEvidenceContract @c;$true}
foreach($case in @('executable','directory','port_zero','port_large','port_other','listener','role','database','write_sql','measure_sql','extra_argument','extra_environment','encoding','password','kill')) {
    Check ('EvidenceContractRejects_'+$case) {
        $c=EvidenceContract
        switch($case) {
            executable {$c.Executable=Join-Path $taskSandbox 'fictional.exe'};directory {$c.WorkingDirectory=Join-Path $taskSandbox 'other'}
            port_zero {$c.Port=0};port_large {$c.Port=65536};listener {$c.Arguments[8]='0.0.0.0'};role {$c.Arguments[12]='fictional_other_role'}
            port_other {$c.Port++;$c.Arguments[10]=[string]$c.Port}
            database {$c.Arguments[14]='fictional_other_database'};write_sql {$c.Arguments[16]='DELETE FROM fictional_only;'}
            measure_sql {$c.Arguments[16]=Get-DangoAccountSyncDatabaseSql -MeasureOnly};extra_argument {$c.Arguments+='--file=fictional.sql'}
            extra_environment {$c.EnvironmentValues.EXTRA='fictional'};encoding {$c.EnvironmentValues.PGCLIENTENCODING='LATIN1'}
            password {$c.EnvironmentValues.PGPASSWORD='fictional-invalid'};kill {$c.KillOwnedTree=$true}
        }
        Rejected {Assert-DangoAccountSyncEvidenceContract @c} 'accountsync_evidence_contract_rejected'
    }
}

# Resolve the real cross-module contracts, then stop at an in-memory sentinel
# replacing the native-type function. ProcessInfo and ProcessFactory are never
# reached, and no native imports, Win32 query, process or resource are created.
function ProbeProfileLaunchBoundary([hashtable]$Contract,[string]$Kind,[switch]$Tamper) {
    $profileModule=Get-Module Dango.AccountProfilePostgres
    if($null -eq $profileModule) {throw 'offline_profile_module_missing'}
    return & $profileModule {
        param($Contract,$Kind,$Tamper)
        $original=(Get-Command Get-DangoProfileNativeType).ScriptBlock
        function script:Get-DangoProfileNativeType {throw 'offline_native_launch_boundary_reached'}
        try {
            $parameters=@{Executable=$Contract.Executable;Arguments=$Contract.Arguments;WorkingDirectory=$Contract.WorkingDirectory;
                EnvironmentValues=$Contract.EnvironmentValues;TimeoutSeconds=1;ProcessFactory={throw 'offline_factory_must_never_run'}}
            if($Kind -ceq 'Start') {
                $parameters.OutputContract='AccountSyncStartIgnored';$parameters.PostgresStartPlan=$Contract.Plan;$parameters.PostgresStartPort=$Contract.Port
            } else {
                $parameters.OutputContract='AccountSyncEvidenceJson';$parameters.PostgresEvidencePlan=$Contract.Plan;$parameters.PostgresEvidencePort=$Contract.Port
            }
            if($Tamper) {$parameters.Arguments+=[string]'fictional-unreviewed-argument'}
            try {Invoke-DangoProfileProcess @parameters;return 'offline_unexpected_launch_return'} catch {return $_.Exception.Message}
        } finally {Set-Item -LiteralPath Function:script:Get-DangoProfileNativeType -Value $original}
    } $Contract $Kind ([bool]$Tamper)
}
Check 'ProfileProcessResolvesAccountSyncStartContractBeforeAnyNativeOrProcessAction' {
    (ProbeProfileLaunchBoundary (StartContract) Start) -ceq 'offline_native_launch_boundary_reached'
}
Check 'ProfileProcessResolvesAccountSyncEvidenceContractBeforeAnyNativeOrProcessAction' {
    (ProbeProfileLaunchBoundary (EvidenceContract) Evidence) -ceq 'offline_native_launch_boundary_reached'
}
Check 'ProfileProcessRejectsTamperedStartBeforeNativeBoundary' {
    (ProbeProfileLaunchBoundary (StartContract) Start -Tamper) -ceq 'accountsync_start_contract_rejected'
}
Check 'ProfileProcessRejectsTamperedEvidenceBeforeNativeBoundary' {
    (ProbeProfileLaunchBoundary (EvidenceContract) Evidence -Tamper) -ceq 'accountsync_evidence_contract_rejected'
}
Check 'ProfileProcessRejectsConsumedB1aStageBeforeNativeBoundary' {
    $contract=StartContract;$contract.Plan.Stage='AccountSyncB1a'
    (ProbeProfileLaunchBoundary $contract Start) -ceq 'accountsync_fixed_plan_rejected'
}
Check 'ProfileProcessRejectsConsumedB1aLogDirectoryBeforeNativeBoundary' {
    $contract=StartContract
    $contract.Arguments[4]=Join-Path (Join-Path (Split-Path -Parent $contract.Plan.EvidenceDirectory) 'run20261010accountsyncb1a') 'postgresql.log'
    (ProbeProfileLaunchBoundary $contract Start) -ceq 'accountsync_start_contract_rejected'
}

Check 'TargetAcceptsExactlySevenReviewedMigrationIds' {Assert-DangoAccountSyncTarget (Plan) (Target);$true}
foreach($case in @('six','eight','reordered','replaced','database','role','version_old','version_new','listener','data')) {
    Check ('TargetRejects_'+$case) {
        $graph=Target
        switch($case) {
            six {$graph.migrations=@($taskSeven[0..5])};eight {$graph.migrations+='20261011000000_FictionalFuture'}
            reordered {$graph.migrations=@($taskSeven[1],$taskSeven[0])+$taskSeven[2..6]};replaced {$graph.migrations[6]='20261010055529_FictionalOther'}
            database {$graph.database='fictional_other_database'};role {$graph.role='fictional_other_role'}
            version_old {$graph.version=179999};version_new {$graph.version=190000};listener {$graph.listenAddresses='*'}
            data {$graph.dataDirectory=Join-Path $taskSandbox 'fictional-pgdata'}
        }
        Rejected {Assert-DangoAccountSyncTarget (Plan) $graph} 'accountsync_target_rejected'
    }
}
Check 'OldGraphAcceptsExactlyOneAccountAndFourLoginSessions' {(Assert-DangoAccountSyncOldGraph (Graph) (AfterGraph)) -eq [Guid]$taskNewUser}
foreach($key in $taskKeys) {
    foreach($container in @('counts','digests')) {
        Check ('GraphRejectsMissing_'+$container+'_'+$key) {
            $before=Graph;$after=AfterGraph;$after.$container.PSObject.Properties.Remove($key)
            Rejected {Assert-DangoAccountSyncOldGraph $before $after} 'profile_run_protected_graph_shape_rejected'
        }
    }
    Check ('FullOldRowHashRejectsChange_'+$key) {
        $before=Graph;$after=AfterGraph;$after.digests.$key[0]='f'*64
        Rejected {Assert-DangoAccountSyncOldGraph $before $after} 'profile_run_old_graph_changed'
    }
}
Check 'CompleteOldUserDigestRejectsNicknameOrProfileVersionChange' {
    $before=Graph;$after=AfterGraph;$after.profiles[0].version=4;$after.digests.users[0]='f'*64
    Rejected {Assert-DangoAccountSyncOldGraph $before $after} 'profile_run_old_graph_changed'
}
foreach($key in $taskAccessory) {
    foreach($change in @('add','delete','change')) {
        Check ('AccessoryRowsReject_'+$key+'_'+$change) {
            $before=Graph;$after=AfterGraph
            switch($change) {
                add {$after.counts.$key=2;$after.digests.$key=@(('a'*64),('b'*64))}
                delete {$after.counts.$key=0;$after.digests.$key=@()}
                change {$after.digests.$key=@(('b'*64))}
            }
            $code=if($change -ceq 'add') {'profile_run_unapproved_graph_created'} else {'profile_run_old_graph_changed'}
            Rejected {Assert-DangoAccountSyncOldGraph $before $after} $code
        }
    }
}
foreach($key in $taskAdds) {
    Check ('OneAccountGraphRejectsSecondNewRow_'+$key) {
        $after=AfterGraph;$after.counts.$key=3;$after.digests.$key+=('c'*64)
        Rejected {Assert-DangoAccountSyncOldGraph (Graph) $after} 'accountsync_new_graph_delta_rejected'
    }
}
Check 'SessionDeltaRejectsThreeLogins' {$after=AfterGraph;$after.counts.sessions=4;$after.digests.sessions=$after.digests.sessions[0..3];Rejected {Assert-DangoAccountSyncOldGraph (Graph) $after} 'accountsync_session_delta_rejected'}
Check 'SessionDeltaRejectsFiveLogins' {$after=AfterGraph;$after.counts.sessions=6;$after.digests.sessions+=('f'*64);Rejected {Assert-DangoAccountSyncOldGraph (Graph) $after} 'accountsync_session_delta_rejected'}
Check 'IdentityDeltaRejectsNoNewUser' {$after=AfterGraph;$after.userIds=@($taskOldUser);Rejected {Assert-DangoAccountSyncOldGraph (Graph) $after} 'accountsync_identity_delta_rejected'}
Check 'IdentityDeltaRejectsTwoNewUsers' {$after=AfterGraph;$after.userIds+='bab71727-7c09-4874-b2bb-2c423a2398ca';Rejected {Assert-DangoAccountSyncOldGraph (Graph) $after} 'accountsync_identity_delta_rejected'}

Check 'AttemptLedgerAcceptsExactNineteenAndEveryOperationBudget' {
    $r=Assert-DangoAccountSyncAttempts (Plan) $taskOwner (Attempts)
    $r.Total -eq 19 -and $r.Operations.Count -eq 8 -and $r.Operations.Configuration -eq 5 -and $r.Operations.Login -eq 4 -and $r.Operations.Profile -eq 2
}
Check 'AttemptLedgerAcceptsActualJsonRoundTripTypesIncludingUtcTimestamp' {
    $rows=Clone (Attempts);$result=Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows
    $result.Total -eq 19
}
foreach($field in @('Version','OwnerId','Sequence','Operation','Used','AtUtc')) {
    Check ('AttemptLedgerRejectsMissingField_'+$field) {$rows=Attempts;$rows[0].PSObject.Properties.Remove($field);Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $null}
    Check ('AttemptLedgerRejectsFieldCase_'+$field) {
        $rows=Attempts;$value=$rows[0].$field;$rows[0].PSObject.Properties.Remove($field);$rows[0]|Add-Member ($field.ToLowerInvariant()) $value
        Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $null
    }
}
Check 'AttemptLedgerRejectsExtraField' {$rows=Attempts;$rows[0]|Add-Member FictionalExtra $true;Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $null}
Check 'AttemptLedgerRejectsInvalidTimestamp' {$rows=Attempts;$rows[0].AtUtc='fictional-invalid-timestamp';Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $null}
Check 'AttemptLedgerRejectsNumericTimestamp' {$rows=Attempts;$rows[0].AtUtc=1;Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $null}
foreach($case in @('eighteen','twenty','duplicate_sequence','unknown_operation','wrong_used','configuration_overflow','wrong_owner','wrong_version',
    'string_version','boolean_version','string_sequence','string_used','boolean_used','operation_case')) {
    Check ('AttemptLedgerRejects_'+$case) {
        $rows=Attempts;$code='accountsync_attempt_identity_rejected'
        switch($case) {
            eighteen {$rows=$rows[0..17];$code='accountsync_attempt_count_rejected'}
            twenty {$rows+=([pscustomobject]@{Version=1;OwnerId=$taskOwner;Sequence=20;Operation='Logout';Used=5});$code='accountsync_attempt_count_rejected'}
            duplicate_sequence {$rows[1].Sequence=1};unknown_operation {$rows[0].Operation='FictionalUnknown'}
            wrong_used {$rows[4].Used=2;$code='accountsync_attempt_budget_rejected'}
            configuration_overflow {$rows[15].Operation='Configuration';$rows[15].Used=6;$code='accountsync_attempt_budget_rejected'}
            wrong_owner {$rows[0].OwnerId='b81a7b41-f67d-48da-a399-ef889b293109'};wrong_version {$rows[0].Version=2}
            string_version {$rows[0].Version='1';$code=$null};boolean_version {$rows[0].Version=$true;$code=$null}
            string_sequence {$rows[0].Sequence='1';$code=$null};string_used {$rows[0].Used='1';$code=$null}
            boolean_used {$rows[0].Used=$true;$code=$null};operation_case {$rows[4].Operation='login';$code=$null}
        }
        Rejected {Assert-DangoAccountSyncAttempts (Plan) $taskOwner $rows} $code
    }
}
Check 'ResourceLedgerAcceptsExactTenAndAllCleanupFacts' {$r=Assert-DangoAccountSyncResources $taskOwner (Resources);$r.HttpsHosts -eq 1 -and $r.Certificates -eq 1 -and $r.DesktopHosts -eq 3 -and $r.NativeCleanupConfirmed -eq $true -and $r.UiaWorkersCleanupConfirmed -eq $true}
foreach($field in @('Version','OwnerId','Sequence','Phase','Facts')) {
    Check ('ResourceLedgerRejectsMissingField_'+$field) {$rows=Resources;$rows[0].PSObject.Properties.Remove($field);Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null}
    Check ('ResourceLedgerRejectsFieldCase_'+$field) {
        $rows=Resources;$value=$rows[0].$field;$rows[0].PSObject.Properties.Remove($field);$rows[0]|Add-Member ($field.ToLowerInvariant()) $value
        Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null
    }
}
Check 'ResourceLedgerRejectsExtraField' {$rows=Resources;$rows[0]|Add-Member FictionalExtra $true;Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null}
for($resourceIndex=0;$resourceIndex -lt 10;$resourceIndex++) {
    $baseline=Resources
    foreach($factField in @($baseline[$resourceIndex].Facts.PSObject.Properties.Name)) {
        Check ('ResourceLedgerRejectsMissingFact_'+$resourceIndex+'_'+$factField) {
            $rows=Resources;$rows[$resourceIndex].Facts.PSObject.Properties.Remove($factField)
            Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null
        }
        Check ('ResourceLedgerRejectsFactCase_'+$resourceIndex+'_'+$factField) {
            $rows=Resources;$value=$rows[$resourceIndex].Facts.$factField;$rows[$resourceIndex].Facts.PSObject.Properties.Remove($factField)
            $rows[$resourceIndex].Facts|Add-Member ($factField.ToLowerInvariant()) $value
            Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null
        }
    }
    Check ('ResourceLedgerRejectsExtraFact_'+$resourceIndex) {
        $rows=Resources;$rows[$resourceIndex].Facts|Add-Member FictionalExtra $true
        Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $null
    }
}
foreach($case in @('nine','eleven','duplicate_sequence','wrong_owner','wrong_phase','wrong_index','hwnd_open','not_disposed','pump_running',
    'uia_worker_1','uia_worker_2','uia_worker_3','string_uia_worker',
    'host_running','inflight','native_identity_missing','native_key_present','native_probe_failed','certificate_invalid','tls_port_zero','host_port_mismatch','workers_enabled','desktop_attempt_false',
    'string_version','string_sequence','string_port','string_index','string_hwnd','string_inflight','string_native_cleanup','string_native_probe')) {
    Check ('ResourceLedgerRejects_'+$case) {
        $rows=Resources;$code=$null
        switch($case) {
            nine {$rows=$rows[0..8];$code='accountsync_resource_count_rejected'}
            eleven {$rows+=Clone $rows[9];$code='accountsync_resource_count_rejected'}
            duplicate_sequence {$rows[1].Sequence=1;$code='accountsync_resource_order_rejected'}
            wrong_owner {$rows[0].OwnerId='b81a7b41-f67d-48da-a399-ef889b293109';$code='accountsync_resource_order_rejected'}
            wrong_phase {$rows[1].Phase='HostDisposed';$code='accountsync_resource_order_rejected'}
            wrong_index {$rows[2].Facts.Index=2;$code='accountsync_desktop_resource_rejected'}
            hwnd_open {$rows[3].Facts.HwndClosed=$false;$code='accountsync_desktop_cleanup_rejected'}
            not_disposed {$rows[5].Facts.Disposed=$false;$code='accountsync_desktop_cleanup_rejected'}
            pump_running {$rows[7].Facts.ConfigurationPumpDrained=$false;$code='accountsync_desktop_cleanup_rejected'}
            uia_worker_1 {$rows[3].Facts.UiaWorkerSettled=$false;$code='accountsync_desktop_cleanup_rejected'}
            uia_worker_2 {$rows[5].Facts.UiaWorkerSettled=$false;$code='accountsync_desktop_cleanup_rejected'}
            uia_worker_3 {$rows[7].Facts.UiaWorkerSettled=$false;$code='accountsync_desktop_cleanup_rejected'}
            string_uia_worker {$rows[3].Facts.UiaWorkerSettled='true';$code='accountsync_desktop_cleanup_rejected'}
            host_running {$rows[8].Facts.Stopped=$false;$code='accountsync_native_cleanup_rejected'}
            inflight {$rows[8].Facts.InFlightRequests=1;$code='accountsync_native_cleanup_rejected'}
            native_identity_missing {$rows[9].Facts.NativeKeyIdentityCaptured=$false;$code='accountsync_native_cleanup_rejected'}
            native_key_present {$rows[9].Facts.NativeCleanupConfirmed=$false;$code='accountsync_native_cleanup_rejected'}
            native_probe_failed {$rows[9].Facts.NativeProbeFailed=$true;$code='accountsync_native_cleanup_rejected'}
            certificate_invalid {$rows[0].Facts.CertificateSha256='fictional-invalid';$code='accountsync_https_resource_rejected'}
            tls_port_zero {$rows[0].Facts.Port=0;$code='accountsync_https_resource_rejected'}
            host_port_mismatch {$rows[1].Facts.Port=62124;$code='accountsync_https_resource_rejected'}
            workers_enabled {$rows[1].Facts.WorkersDisabled=$false;$code='accountsync_https_resource_rejected'}
            desktop_attempt_false {$rows[2].Facts.Attempt=$false;$code='accountsync_desktop_resource_rejected'}
            string_version {$rows[0].Version='1'};string_sequence {$rows[0].Sequence='1'}
            string_port {$rows[0].Facts.Port='62123';$rows[1].Facts.Port='62123'}
            string_index {$rows[2].Facts.Index='1'};string_hwnd {$rows[3].Facts.HwndClosed='true'}
            string_inflight {$rows[8].Facts.InFlightRequests='0'}
            string_native_cleanup {$rows[9].Facts.NativeCleanupConfirmed='true'}
            string_native_probe {$rows[9].Facts.NativeProbeFailed='false'}
        }
        Rejected {Assert-DangoAccountSyncResources $taskOwner $rows} $code
    }
}

$taskScopedExpected=[ordered]@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=4;validSessions=4;revokedSessions=4}
function Scoped {
    $values=[ordered]@{}
    foreach($key in $taskScopedExpected.Keys) {$values[$key]=$taskScopedExpected[$key]}
    return [pscustomobject]$values
}
Check 'ScopedGraphAcceptsOneCompleteNamedAccountAndFourRevokedSessions' {Assert-DangoAccountSyncScopedGraph (Scoped);$true}
foreach($key in $taskScopedExpected.Keys) {
    Check ('ScopedGraphRejectsWrongCount_'+$key) {$graph=Scoped;$graph.$key++;Rejected {Assert-DangoAccountSyncScopedGraph $graph} 'accountsync_scoped_graph_rejected'}
    Check ('ScopedGraphRejectsNumericString_'+$key) {$graph=Scoped;$graph.$key=[string]$graph.$key;Rejected {Assert-DangoAccountSyncScopedGraph $graph} $null}
    Check ('ScopedGraphRejectsMissing_'+$key) {$graph=Scoped;$graph.PSObject.Properties.Remove($key);Rejected {Assert-DangoAccountSyncScopedGraph $graph} 'accountsync_scoped_shape_rejected'}
}
Check 'ScopedGraphRejectsExtraKey' {$graph=Scoped;$graph|Add-Member FictionalExtra 0;Rejected {Assert-DangoAccountSyncScopedGraph $graph} 'accountsync_scoped_shape_rejected'}
Check 'ScopedGraphRejectsIncorrectKeyCase' {
    $graph=Scoped;$graph.PSObject.Properties.Remove('users');$graph|Add-Member Users 1
    Rejected {Assert-DangoAccountSyncScopedGraph $graph} $null
}
Check 'ScopedSqlIsReadOnlyAndRestrictsAllRowsToOneIdentityEpochAndOwner' {
    $sql=Get-DangoAccountSyncScopedSql ([Guid]$taskNewUser) ([Guid]'f6d9b122-7630-42c3-849f-a199f1dd2485') $taskOwner
    $sql.StartsWith('BEGIN READ ONLY;',[StringComparison]::Ordinal) -and $sql.TrimEnd().EndsWith('COMMIT;',[StringComparison]::Ordinal) -and
        $sql -notmatch '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\b' -and
        $sql.Contains("WHERE id='$taskNewUser'::uuid",[StringComparison]::Ordinal) -and
        $sql.Contains('accountsync-'+$taskOwner.ToString('N')+'@example.invalid',[StringComparison]::Ordinal) -and
        $sql.Contains("recovery_epoch='f6d9b122-7630-42c3-849f-a199f1dd2485'::uuid",[StringComparison]::Ordinal) -and
        $sql.Contains("nickname=U&'\65B0\540C\6B65\56E2\5B50' AND profile_version=3",[StringComparison]::Ordinal) -and
        $sql.Contains("p.configuration_version=6 AND (p.configuration::json->>'eyeSizePercent')::numeric=128",[StringComparison]::Ordinal)
}
Check 'ScopedSqlIsIdenticalUnderUtf8AndWindowsChineseCodePage' {
    $sql=Get-DangoAccountSyncScopedSql ([Guid]$taskNewUser) ([Guid]'f6d9b122-7630-42c3-849f-a199f1dd2485') $taskOwner
    Assert-DangoAccountSyncSqlTransportContract $sql
    [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes($sql)) -ceq
        [Convert]::ToHexString([Text.Encoding]::GetEncoding(936).GetBytes($sql))
}
Check 'ScopedSqlUnicodeEscapeDecodesToExactExpectedNickname' {
    $sql=Get-DangoAccountSyncScopedSql ([Guid]$taskNewUser) ([Guid]'f6d9b122-7630-42c3-849f-a199f1dd2485') $taskOwner
    $literal=[regex]::Match($sql,"nickname=U&'(?<value>(?:\\[0-9A-F]{4})+)' AND profile_version=3")
    if (-not $literal.Success) {return $false}
    $decoded=[regex]::Replace($literal.Groups['value'].Value,'\\([0-9A-F]{4})',{
        param($match)
        ([char][Convert]::ToInt32($match.Groups[1].Value,16)).ToString()
    })
    $decoded -ceq '新同步团子'
}
foreach($case in @('Empty','Whitespace','ChineseNickname','LatinNonAscii','Nul','TooLong')) {
    Check ('SqlTransportRejects_'+$case) {
        $sql=switch($case) {
            Empty {''};Whitespace {'   '};ChineseNickname {"SELECT '新同步团子';"}
            LatinNonAscii {"SELECT 'é';"};Nul {'SELECT '+[char]0+';'};TooLong {'a'*16385}
        }
        Rejected {Assert-DangoAccountSyncSqlTransportContract $sql} 'accountsync_sql_transport_rejected'
    }
}
Check 'SqlTransportAcceptsExactAsciiLengthBoundary' {
    Assert-DangoAccountSyncSqlTransportContract ('a'*16384)
    $true
}
Check 'OriginalChineseArgumentReproducesRecordedUtf8ErrorBytes' {
    $bytes=[Text.Encoding]::GetEncoding(936).GetBytes('新')
    $bytes.Length -eq 2 -and $bytes[0] -eq 0xD0 -and $bytes[1] -eq 0xC2 -and
        [Convert]::ToHexString([Text.Encoding]::UTF8.GetBytes('新')) -ceq 'E696B0'
}
foreach($empty in @('UserId','Epoch','OwnerId')) {
    Check ('ScopedSqlRejectsEmpty_'+$empty) {
        $ids=@{UserId=[Guid]$taskNewUser;Epoch=[Guid]'f6d9b122-7630-42c3-849f-a199f1dd2485';OwnerId=$taskOwner};$ids[$empty]=[Guid]::Empty
        Rejected {Get-DangoAccountSyncScopedSql @ids} 'accountsync_scoped_ids_rejected'
    }
}

$taskTestName='FullApplicationHostRegistersNamesComparesEveryLoginAndSynchronizesRealSavedAppearance'
$taskTrx='<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testName="'+$taskTestName+'" outcome="Passed" /></Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
Check 'TrxAcceptsExactlyOneNamedPassedResult' {$path=WriteFixture 'artifacts/trx-pass/account-sync-b1a2.trx' $taskTrx;Assert-DangoAccountSyncTrx $path;$true}
Check 'TrxRejectsConsumedB1aResultFileName' {
    $path=WriteFixture 'artifacts/account-sync-b1a.trx' $taskTrx
    Rejected {Assert-DangoAccountSyncTrx $path} 'accountsync_trx_stage_path_rejected'
}
foreach($case in @('zero','two','failed','skipped','wrong_name','counter_total','counter_executed','counter_passed','counter_failed','counter_skipped','missing_counters','dtd')) {
    Check ('TrxRejects_'+$case) {
        $xml=$taskTrx;$code='accountsync_trx_not_exactly_one_passed'
        switch($case) {
            zero {$xml=$xml.Replace('<UnitTestResult testName="'+$taskTestName+'" outcome="Passed" />','')}
            two {$xml=$xml.Replace('</Results>','<UnitTestResult testName="FictionalOther" outcome="Passed" /></Results>')}
            failed {$xml=$xml.Replace('outcome="Passed"','outcome="Failed"')};skipped {$xml=$xml.Replace('outcome="Passed"','outcome="NotExecuted"')}
            wrong_name {$xml=$xml.Replace($taskTestName,'FictionalOther')};counter_total {$xml=$xml.Replace('total="1"','total="2"')}
            counter_executed {$xml=$xml.Replace('executed="1"','executed="0"')};counter_passed {$xml=$xml.Replace('passed="1"','passed="0"')}
            counter_failed {$xml=$xml.Replace('failed="0"','failed="1"')};counter_skipped {$xml=$xml.Replace('notExecuted="0"','notExecuted="1"')}
            missing_counters {$xml=$xml.Replace('<Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" />','')}
            dtd {$xml='<!DOCTYPE TestRun [<!ENTITY fictional "offline">]>'+$xml;$code=$null}
        }
        $path=WriteFixture ('artifacts/trx-'+$case+'/account-sync-b1a2.trx') $xml
        Rejected {Assert-DangoAccountSyncTrx $path} $code
    }
}

# Synthetic frozen inputs and synthetic review JSON live only in the fictional
# repository, never in the real approved-manifest or deployment directories.
$taskPlan=Plan
[void][IO.Directory]::CreateDirectory((Join-Path $taskSandbox 'eng/tools/Dango.AccountSyncReview.Tests'))
foreach($relative in $taskFrozen+@($taskPlan.NativeModule,
    'eng/tools/Dango.AccountSyncReview.Tests/FictionalBudget.cs','eng/tools/Dango.AccountSyncReview.Tests/FictionalJourney.cs',
    ($taskPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll'),($taskPlan.CandidateDirectory+'/Dango.Desktop.dll'),
    ($taskPlan.CandidateDirectory+'/Dango.Server.dll'),($taskPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.runtimeconfig.json'),
    ($taskPlan.CandidateDirectory+'/nested/FictionalDependency.dll'),($taskPlan.CandidateDirectory+'/FictionalHost.exe'))) {[void](WriteFixture $relative)}
$taskInputs=Get-DangoAccountSyncInputs $taskPlan
foreach($relative in $taskInputs.MissingInputs) {[void](WriteFixture $relative)}
$taskInputs=Get-DangoAccountSyncInputs $taskPlan
$taskReviewPath=Join-Path $taskSandbox 'artifacts/fictional-review-inputs.json'
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $taskReviewPath))
$taskReview=New-DangoAccountSyncApproval $taskPlan $taskReviewPath
Check 'FrozenInputsContainAllEntrypointsHostInjectionNativeAndCandidateFiles' {
    $expected=$taskFrozen+@($taskPlan.NativeModule,'eng/tools/Dango.AccountSyncReview.Tests/FictionalBudget.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/FictionalJourney.cs',($taskPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll'),
        ($taskPlan.CandidateDirectory+'/Dango.Desktop.dll'),($taskPlan.CandidateDirectory+'/Dango.Server.dll'),
        ($taskPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.runtimeconfig.json'),($taskPlan.CandidateDirectory+'/nested/FictionalDependency.dll'),
        ($taskPlan.CandidateDirectory+'/FictionalHost.exe'))
    $taskInputs.MissingInputs.Count -eq 0 -and @($expected|Where-Object {-not $taskInputs.Hashes.Contains($_)}).Count -eq 0
}
Check 'FictionalFrozenManifestAcceptsUnchangedInputs' {[void](Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath $taskReview.Sha256);$true}
Check 'FrozenInputsDoNotIncludeAnExistingConsumedB1aCandidate' {
    $old='artifacts/account-sync-b1a-candidate/bin/Dango.AccountSyncReview.Tests/release/Dango.AccountSyncReview.Tests.dll'
    [void](WriteFixture $old 'fictional-consumed-b1a-candidate')
    $current=Get-DangoAccountSyncInputs $taskPlan
    -not $current.Hashes.Contains($old) -and
        $current.Hashes.Contains($taskPlan.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
}
Check 'FrozenManifestRejectsConsumedB1aOuterStageEvenWithItsOwnMatchingDigest' {
    $old=Clone $taskReview.Inputs;$old.Stage='AccountSyncB1a'
    $path=Join-Path $taskSandbox 'artifacts/fictional-consumed-b1a-stage.json'
    Write-DangoProfileJson $path $old -CreateNew
    $digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Rejected {Assert-DangoAccountSyncApproval $taskPlan $path $digest} 'accountsync_approval_invalid'
}
Check 'FrozenManifestRejectsConsumedB1aInnerPlanEvenWithItsOwnMatchingDigest' {
    $old=Clone $taskReview.Inputs;$old.Plan.Stage='AccountSyncB1a'
    $old.Plan.EvidenceDirectory=Join-Path (Split-Path -Parent $old.Plan.EvidenceDirectory) 'run20261010accountsyncb1a'
    $old.Plan.CandidateDirectory='artifacts/account-sync-b1a-candidate/bin/Dango.AccountSyncReview.Tests/release'
    $old.Plan.ApprovalVariable='DANGO_BACKEND_ACCOUNTSYNC_B1A_APPROVED';$old.Plan.TrxFileName='account-sync-b1a.trx'
    $path=Join-Path $taskSandbox 'artifacts/fictional-consumed-b1a-plan.json'
    Write-DangoProfileJson $path $old -CreateNew
    $digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Rejected {Assert-DangoAccountSyncApproval $taskPlan $path $digest} 'accountsync_approved_plan_changed'
}
foreach($relative in $taskInputs.Hashes.Keys) {
    Check ('FrozenSourceRejectsModification_'+$relative) {
        $path=Join-Path $taskSandbox $relative;$original=[IO.File]::ReadAllBytes($path)
        [IO.File]::AppendAllText($path,'fictional-offline-change')
        try {Rejected {Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath $taskReview.Sha256} 'accountsync_inputs_changed'}
        finally {[IO.File]::WriteAllBytes($path,$original)}
    }
}
Check 'FrozenManifestRejectsNewTestSource' {
    $path=WriteFixture 'eng/tools/Dango.AccountSyncReview.Tests/FictionalExtra.cs'
    try {Rejected {Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath $taskReview.Sha256} 'accountsync_inputs_changed'}
    finally {[IO.File]::Delete($path)}
}
Check 'FrozenManifestRejectsNewCandidateExecutable' {
    $path=WriteFixture ($taskPlan.CandidateDirectory+'/FictionalExtra.exe')
    try {Rejected {Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath $taskReview.Sha256} 'accountsync_inputs_changed'}
    finally {[IO.File]::Delete($path)}
}
Check 'FrozenManifestRejectsChangedReviewBytes' {
    $original=[IO.File]::ReadAllBytes($taskReviewPath);[IO.File]::AppendAllText($taskReviewPath,'fictional-change')
    try {Rejected {Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath $taskReview.Sha256} 'accountsync_approval_changed'}
    finally {[IO.File]::WriteAllBytes($taskReviewPath,$original)}
}
Check 'FrozenManifestRejectsNonShaDigest' {Rejected {Assert-DangoAccountSyncApproval $taskPlan $taskReviewPath 'fictional-invalid'} 'accountsync_approval_digest_required'}

$taskPassed=@($taskChecks|Where-Object Passed).Count
$taskResult=[pscustomobject]@{ExecutedDatabase=$false;RunnerEntryExecuted=$false;ReservationExecuted=$false;NetworkProbed=$false;
    SystemTrustOrCredentialsAccessed=$false;Sandbox=$taskSandbox;Passed=$taskPassed;Total=$taskChecks.Count;Checks=@($taskChecks)}
Write-DangoProfileJson (Join-Path $taskSandbox 'offline-results.json') $taskResult -CreateNew
$taskChecks|Where-Object {-not $_.Passed}|Select-Object Name,Failure|Format-Table -AutoSize
Write-Output ('accountsync_b1a2_offline_evidence:'+ (Join-Path $taskSandbox 'offline-results.json'))
if($taskPassed -ne $taskChecks.Count) {throw ('accountsync_b1a2_offline_failed:'+$taskPassed+'/'+$taskChecks.Count)}
Write-Output ('accountsync_b1a2_offline_passed:'+$taskPassed+'/'+$taskChecks.Count)

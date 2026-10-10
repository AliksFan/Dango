Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1')

function Get-DangoAccountSyncPlan {
    param([string]$Repository)
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    [pscustomobject]@{
        Version=1; Stage='AccountSyncB1a2'; RunName='run20261008b0'; Executed=$false
        Repository=[IO.Path]::GetFullPath($Repository); StateDirectory=$state; DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261010accountsyncb1a2')
        PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin')
        Database='dango_backend_b0'; Role='dango_b0_owner'; DatabasePort=63237; ExpectedTests=1; MaximumToolProcesses=10
        Filter='FullyQualifiedName=Dango.AccountSyncReview.Tests.AccountSyncB1aJourneyTests.FullApplicationHostRegistersNamesComparesEveryLoginAndSynchronizesRealSavedAppearance'
        CandidateDirectory='artifacts/account-sync-b1a2-candidate/bin/Dango.AccountSyncReview.Tests/release'
        ApprovalVariable='DANGO_BACKEND_ACCOUNTSYNC_B1A2_APPROVED'; TrxFileName='account-sync-b1a2.trx'
        NativeModule='artifacts/local-room/account-profile-sync-20261010/bin/Dango.Desktop/release/dango_physics_module.dll'
        NativeSha256='BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507'
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity',
            '20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
        Budgets=[ordered]@{Invite=1;Start=1;Confirm=1;Complete=1;Login=4;Profile=2;Configuration=5;Logout=4}
        MaximumHttpRequests=512; MaximumDurationMinutes=10; ProcessTimeoutSeconds=300; HttpsHosts=1; Certificates=1; DesktopHosts=3
        MigrationOperations=0; RoomOperations=0; SystemTrustOperations=0; WindowsCredentialOperations=0; EmailDispatchOperations=0
    }
}

function Assert-DangoAccountSyncFixedPlan {
    param($Plan)
    $fixed=Get-DangoAccountSyncPlan $Plan.Repository
    if (@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count) {throw 'accountsync_fixed_plan_rejected'}
    foreach($p in $fixed.PSObject.Properties) {
        $actual=$Plan.PSObject.Properties[$p.Name]
        if ($null -eq $actual -or $actual.Name -cne $p.Name -or -not(Test-DangoProfileFixedValue $actual.Value $p.Value)) {
            throw 'accountsync_fixed_plan_rejected'
        }
    }
    return $fixed
}

function Get-DangoAccountSyncInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan
    $files=@('eng/Dango.AccountSyncPostgres.psm1','eng/Dango.AccountProfilePostgres.psm1',
        'eng/invoke-account-sync-b1a-tests.ps1','eng/test-account-sync-b1a-boundary.ps1',
        'eng/test-account-sync-b1a-runner-boundary.ps1','src/Dango.Desktop/Properties/AssemblyInfo.cs',
        'src/Dango.Desktop/AppShell/DangoApplicationHost.cs','src/Dango.Desktop/AppShell/DangoApplicationHost.Accounts.cs',
        'src/Dango.Desktop/AppShell/DangoApplicationHost.AccountDependencies.cs','src/Dango.Desktop/AppShell/DangoApplicationHost.Persistence.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/Dango.AccountSyncReview.Tests.csproj','Directory.Build.props','global.json',$fixed.NativeModule)
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $fixed.Repository 'eng/tools/Dango.AccountSyncReview.Tests') -Filter '*.cs' -File)) {
        $files+=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
    }
    $candidate=Join-Path $fixed.Repository $fixed.CandidateDirectory
    Assert-DangoProfileRegularPath $candidate
    if ([IO.Directory]::Exists($candidate)) {
        foreach($file in @(Get-ChildItem -LiteralPath $candidate -Recurse -Force)) {
            Assert-DangoProfileRegularPath $file.FullName
            if (-not $file.PSIsContainer -and $file.Extension.ToLowerInvariant() -cin @('.dll','.json','.exe')) {
                $files+=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
            }
        }
    }
    $files+=($fixed.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    $files=@($files | Sort-Object -Unique)
    if ($files.Count -gt 1024) {throw 'accountsync_inputs_bound_rejected'}
    $hashes=[ordered]@{};$missing=@()
    foreach($relative in $files) {
        $path=Join-Path $fixed.Repository $relative; Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) {$missing+=$relative;continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    [pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;Plan=$fixed;Hashes=$hashes;MissingInputs=$missing}
}

function New-DangoAccountSyncApproval {
    param($Plan,[string]$Path)
    $path=Assert-DangoProfileManifestPath $Plan $Path
    $inputs=Get-DangoAccountSyncInputs $Plan
    if ($inputs.MissingInputs.Count -ne 0) {throw 'accountsync_inputs_missing'}
    Write-DangoProfileJson $path $inputs -CreateNew
    [pscustomobject]@{Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;Inputs=$inputs}
}

function Assert-DangoAccountSyncApproval {
    param($Plan,[string]$Path,[string]$Digest)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan
    if ($Digest -cnotmatch '^[A-F0-9]{64}$') {throw 'accountsync_approval_digest_required'}
    $path=Assert-DangoProfileManifestPath $fixed $Path
    $file=[IO.FileInfo]::new($path)
    if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 262144) {throw 'accountsync_approval_invalid'}
    $bytes=[IO.File]::ReadAllBytes($path)
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -cne $Digest) {throw 'accountsync_approval_changed'}
    $approved=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json
    if (-not(Test-DangoAccountSyncObjectShape $approved @('Version','Stage','RunName','Plan','Hashes','MissingInputs')) -or
        -not(Test-DangoAccountSyncInteger $approved.Version 1) -or
        $approved.Stage -isnot [string] -or $approved.Stage -cne $fixed.Stage -or
        $approved.RunName -isnot [string] -or $approved.RunName -cne $fixed.RunName -or
        $approved.MissingInputs -isnot [Array] -or @($approved.MissingInputs).Count -ne 0 -or
        $approved.Hashes -isnot [System.Management.Automation.PSCustomObject]) {throw 'accountsync_approval_invalid'}
    # JSON arrays/numeric representations are compared by canonical JSON; executable plans also undergo strict typed checks.
    $canonical=($fixed|ConvertTo-Json -Depth 20 -Compress)
    if (($approved.Plan|ConvertTo-Json -Depth 20 -Compress) -cne $canonical) {throw 'accountsync_approved_plan_changed'}
    $current=Get-DangoAccountSyncInputs $fixed
    if ($current.MissingInputs.Count -ne 0 -or @($approved.Hashes.PSObject.Properties).Count -ne $current.Hashes.Count) {throw 'accountsync_inputs_changed'}
    foreach($key in $current.Hashes.Keys) {
        $p=$approved.Hashes.PSObject.Properties[$key]
        if ($null -eq $p -or $p.Name -cne $key -or $p.Value -isnot [string] -or
            $p.Value -cnotmatch '^[A-F0-9]{64}$' -or $p.Value -cne $current.Hashes[$key]) {throw 'accountsync_inputs_changed'}
    }
    return [pscustomobject]@{Path=$path;Sha256=$Digest;Inputs=$current}
}

function New-DangoAccountSyncReservation {
    param($Plan)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan
    Assert-DangoProfileRegularPath $fixed.EvidenceDirectory;Get-DangoProfileNativeType
    if (-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)) {throw 'accountsync_already_reserved'}
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $fixed.EvidenceDirectory -AclObject $acl
    $ownerProcess=[Diagnostics.Process]::GetCurrentProcess();$id=[Guid]::NewGuid()
    try {$owner=[pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;OwnerId=$id;OwnerPid=$ownerProcess.Id;
        OwnerCreatedAt=$ownerProcess.StartTime.ToUniversalTime().ToString('O');OwnerExecutable=$ownerProcess.MainModule.FileName;
        RunDirectory=$fixed.EvidenceDirectory;ExpectedTests=1;Budgets=$fixed.Budgets}} finally {$ownerProcess.Dispose()}
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'reservation.json') ([pscustomobject]@{Plan=$fixed;OwnerId=$id;RetryAllowed=$false;WholeSuiteReserved=$true}) -CreateNew
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') $owner -CreateNew
    foreach($name in @('attempts.jsonl','resources.jsonl')) {
        $file=[IO.FileStream]::new((Join-Path $fixed.EvidenceDirectory $name),'CreateNew','Write','Read',4096,[IO.FileOptions]::WriteThrough)
        try {$file.Flush($true)}finally{$file.Dispose()}
    }
    return [pscustomobject]@{OwnerId=$id;Owner=$owner;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') -Algorithm SHA256).Hash}
}

function Get-DangoAccountSyncDatabaseSql {
    param([switch]$MeasureOnly)
    $sql=Get-DangoProfileDatabaseSql -MeasureOnly:$MeasureOnly
    $old="(to_jsonb(t) - 'nickname' - 'profile_version')"
    if (-not $sql.Contains($old,[StringComparison]::Ordinal)) {throw 'accountsync_complete_user_hash_contract_rejected'}
    return $sql.Replace($old,'to_jsonb(t)',[StringComparison]::Ordinal)
}

function Assert-DangoAccountSyncStartContract {
    param([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,$Plan,[int]$Port,[hashtable]$EnvironmentValues,[bool]$KillOwnedTree)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan
    $expected=@('start','-D',$fixed.DataDirectory,'-l',(Join-Path $fixed.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $Port",'-w','-t','30')
    if ($Port -ne $fixed.DatabasePort -or $EnvironmentValues.Count -ne 0 -or $KillOwnedTree -or
        -not(Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'pg_ctl.exe')) -or
        -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository) -or -not(Test-DangoProfileFixedValue $Arguments ([string[]]$expected))) {
        throw 'accountsync_start_contract_rejected'
    }
}

function Assert-DangoAccountSyncEvidenceContract {
    param([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,$Plan,[int]$Port,[hashtable]$EnvironmentValues,[bool]$KillOwnedTree)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan
    $expected=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$Port,'-U',$fixed.Role,'-d',$fixed.Database,'-c',(Get-DangoAccountSyncDatabaseSql))
    if ($Port -ne $fixed.DatabasePort -or $KillOwnedTree -or $EnvironmentValues.Count -ne 2 -or
        @($EnvironmentValues.Keys) -cnotcontains 'PGPASSWORD' -or @($EnvironmentValues.Keys) -cnotcontains 'PGCLIENTENCODING' -or
        $EnvironmentValues.PGCLIENTENCODING -cne 'UTF8' -or $EnvironmentValues.PGPASSWORD -cnotmatch '^[A-Za-z0-9+/]{43}=$' -or
        -not(Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'psql.exe')) -or
        -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository) -or -not(Test-DangoProfileFixedValue $Arguments ([string[]]$expected))) {
        throw 'accountsync_evidence_contract_rejected'
    }
}

function Assert-DangoAccountSyncTarget {
    param($Plan,$Evidence)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan;Assert-DangoProfileProtectedGraphShape $Evidence
    if ($Evidence.database -cne $fixed.Database -or $Evidence.role -cne $fixed.Role -or $Evidence.version -lt 180000 -or
        $Evidence.version -ge 190000 -or $Evidence.listenAddresses -cne '127.0.0.1' -or
        -not(Test-DangoProfilePath $Evidence.dataDirectory $fixed.DataDirectory) -or
        (@($Evidence.migrations)-join ',') -cne ($fixed.PriorMigrations -join ',')) {throw 'accountsync_target_rejected'}
}

function Assert-DangoAccountSyncOldGraph {
    param($Before,$After)
    Assert-DangoProfileProtectedGraphShape $Before;Assert-DangoProfileProtectedGraphShape $After
    Assert-DangoProfileOldGraph $Before $After
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')) {
        if ($After.counts.$key-$Before.counts.$key -ne 1) {throw 'accountsync_new_graph_delta_rejected'}
    }
    if ($After.counts.sessions-$Before.counts.sessions -ne 4) {throw 'accountsync_session_delta_rejected'}
    $new=@($After.userIds|Where-Object {$_ -cnotin $Before.userIds})
    if ($new.Count -ne 1) {throw 'accountsync_identity_delta_rejected'}
    return [Guid]$new[0]
}

function Assert-DangoAccountSyncTrx {
    param([string]$Path)
    Assert-DangoProfileRegularPath $Path
    if ([IO.Path]::GetFileName($Path) -cne 'account-sync-b1a2.trx') {throw 'accountsync_trx_stage_path_rejected'}
    $file=[IO.FileInfo]::new($Path);if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 1048576) {throw 'accountsync_trx_rejected'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing='Prohibit';$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings);$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    try {$xml.Load($reader)}finally{$reader.Dispose()}
    $results=@($xml.SelectNodes("//*[local-name()='UnitTestResult']"));$c=$xml.SelectSingleNode("//*[local-name()='Counters']")
    if ($results.Count -ne 1 -or $results[0].outcome -cne 'Passed' -or
        $results[0].testName -cne 'FullApplicationHostRegistersNamesComparesEveryLoginAndSynchronizesRealSavedAppearance' -or
        $null -eq $c -or $c.total -cne '1' -or $c.executed -cne '1' -or $c.passed -cne '1' -or $c.failed -cne '0' -or $c.notExecuted -cne '0') {
        throw 'accountsync_trx_not_exactly_one_passed'
    }
}

function Assert-DangoAccountSyncAttempts {
    param($Plan,[Guid]$OwnerId,$Rows)
    $fixed=Assert-DangoAccountSyncFixedPlan $Plan;$used=@{};$sequence=0
    if (@($Rows).Count -ne 19) {throw 'accountsync_attempt_count_rejected'}
    foreach($r in $Rows) {
        if (-not(Test-DangoAccountSyncObjectShape $r @('Version','OwnerId','Sequence','Operation','Used','AtUtc')) -or
            -not(Test-DangoAccountSyncTimestamp $r.AtUtc)) {throw 'accountsync_attempt_identity_rejected'}
        $sequence++;$p=$fixed.Budgets[[string]$r.Operation]
        if (-not(Test-DangoAccountSyncInteger $r.Version 1) -or [Guid]$r.OwnerId -ne $OwnerId -or
            -not(Test-DangoAccountSyncInteger $r.Sequence $sequence) -or
            $r.Operation -isnot [string] -or @($fixed.Budgets.Keys) -cnotcontains $r.Operation -or $null -eq $p) {throw 'accountsync_attempt_identity_rejected'}
        $count=if ($used.ContainsKey([string]$r.Operation)) {$used[[string]$r.Operation]+1}else{1}
        if ($count -gt $p -or -not(Test-DangoAccountSyncInteger $r.Used $count)) {throw 'accountsync_attempt_budget_rejected'}
        $used[[string]$r.Operation]=$count
    }
    foreach($key in $fixed.Budgets.Keys) {if ($used[$key] -ne $fixed.Budgets[$key]) {throw 'accountsync_attempt_budget_incomplete'}}
    return [pscustomobject]@{Total=19;Operations=$used}
}

function Assert-DangoAccountSyncResources {
    param([Guid]$OwnerId,$Rows)
    $expected=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','DesktopStarted','DesktopDisposed',
        'DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed')
    if (@($Rows).Count -ne 10) {throw 'accountsync_resource_count_rejected'}
    foreach($row in $Rows) {
        if (-not(Test-DangoAccountSyncObjectShape $row @('Version','OwnerId','Sequence','Phase','Facts'))) {throw 'accountsync_resource_order_rejected'}
        $fields=switch -CaseSensitive ($row.Phase) {
            'TLSCreated' {@('Port','CertificateSha256')}
            'HostStarted' {@('Port','WorkersDisabled')}
            'DesktopStarted' {@('Index','Attempt')}
            'DesktopDisposed' {@('Index','Disposed','HwndClosed','ConfigurationPumpDrained','UiaWorkerSettled')}
            'HostDisposed' {@('Stopped','Disposed','InFlightRequests')}
            'TLSDisposed' {@('NativeKeyIdentityCaptured','NativeCleanupConfirmed','NativeProbeFailed')}
            default {throw 'accountsync_resource_order_rejected'}
        }
        if (-not(Test-DangoAccountSyncObjectShape $row.Facts $fields)) {throw 'accountsync_resource_order_rejected'}
    }
    if (-not(Test-DangoAccountSyncIntegerType $Rows[0].Facts.Port) -or $Rows[0].Facts.Port -lt 1 -or $Rows[0].Facts.Port -gt 65535 -or
        $Rows[0].Facts.CertificateSha256 -isnot [string] -or
        $Rows[0].Facts.CertificateSha256 -cnotmatch '^[A-F0-9]{64}$' -or
        -not(Test-DangoAccountSyncInteger $Rows[1].Facts.Port $Rows[0].Facts.Port) -or
        -not(Test-DangoAccountSyncBoolean $Rows[1].Facts.WorkersDisabled $true)) {throw 'accountsync_https_resource_rejected'}
    for($i=0;$i -lt 10;$i++) {
        $r=$Rows[$i]
        if (-not(Test-DangoAccountSyncInteger $r.Version 1) -or [Guid]$r.OwnerId -ne $OwnerId -or
            -not(Test-DangoAccountSyncInteger $r.Sequence ($i+1)) -or $r.Phase -cne $expected[$i]) {throw 'accountsync_resource_order_rejected'}
        if ($r.Phase -cin @('DesktopStarted','DesktopDisposed') -and
            -not(Test-DangoAccountSyncInteger $r.Facts.Index ([int][Math]::Floor(($i-2)/2)+1))) {throw 'accountsync_desktop_resource_rejected'}
        if ($r.Phase -ceq 'DesktopStarted' -and -not(Test-DangoAccountSyncBoolean $r.Facts.Attempt $true)) {throw 'accountsync_desktop_resource_rejected'}
        if ($r.Phase -ceq 'DesktopDisposed' -and (-not(Test-DangoAccountSyncBoolean $r.Facts.Disposed $true) -or
            -not(Test-DangoAccountSyncBoolean $r.Facts.HwndClosed $true) -or
            -not(Test-DangoAccountSyncBoolean $r.Facts.ConfigurationPumpDrained $true) -or
            -not(Test-DangoAccountSyncBoolean $r.Facts.UiaWorkerSettled $true))) {throw 'accountsync_desktop_cleanup_rejected'}
    }
    if (-not(Test-DangoAccountSyncBoolean $Rows[8].Facts.Stopped $true) -or -not(Test-DangoAccountSyncBoolean $Rows[8].Facts.Disposed $true) -or
        -not(Test-DangoAccountSyncInteger $Rows[8].Facts.InFlightRequests 0) -or
        -not(Test-DangoAccountSyncBoolean $Rows[9].Facts.NativeKeyIdentityCaptured $true) -or
        -not(Test-DangoAccountSyncBoolean $Rows[9].Facts.NativeCleanupConfirmed $true) -or
        -not(Test-DangoAccountSyncBoolean $Rows[9].Facts.NativeProbeFailed $false)) {throw 'accountsync_native_cleanup_rejected'}
    return [pscustomobject]@{HttpsHosts=1;Certificates=1;DesktopHosts=3;NativeCleanupConfirmed=$true;UiaWorkersCleanupConfirmed=$true}
}

function Get-DangoAccountSyncScopedSql {
    param([Guid]$UserId,[Guid]$Epoch,[Guid]$OwnerId)
    if ($UserId -eq [Guid]::Empty -or $Epoch -eq [Guid]::Empty -or $OwnerId -eq [Guid]::Empty) {throw 'accountsync_scoped_ids_rejected'}
    $sql=@"
BEGIN READ ONLY;
WITH selected AS (SELECT * FROM dango.users WHERE id='$($UserId.ToString('D'))'::uuid)
SELECT json_build_object('users',(SELECT count(*) FROM selected),
 'validUsers',(SELECT count(*) FROM selected WHERE email='accountsync-$($OwnerId.ToString('N'))@example.invalid' AND email_confirmed AND disabled_at IS NULL AND NOT recovery_locked),
 'namedUsers',(SELECT count(*) FROM selected WHERE nickname=U&'\65B0\540C\6B65\56E2\5B50' AND profile_version=3),
 'completeGraphs',(SELECT count(*) FROM selected u
 JOIN dango.pets p ON p.user_id=u.id AND p.configuration_version=6 AND (p.configuration::json->>'eyeSizePercent')::numeric=128
 JOIN dango.rooms r ON r.owner_id=u.id AND r.kind=0 AND r.status=0 AND r.version=1 AND r.template_id='desktop-pet-default'
 JOIN dango.room_memberships m ON m.room_id=r.id AND m.user_id=u.id AND m.role=0
 JOIN dango.pet_room_states s ON s.pet_id=p.id AND s.room_id=r.id AND s.version=1 AND s.control_generation=0 AND s.control_lease_id IS NULL
 JOIN dango.pending_registrations pr ON pr.user_id=u.id AND pr.pet_id=p.id AND pr.room_id=r.id AND pr.state=2 AND pr.completed_at IS NOT NULL
 JOIN dango.test_invitations i ON i.id=pr.invitation_id AND i.user_id=u.id AND i.consumed_at IS NOT NULL AND i.revoked_at IS NULL
 JOIN dango.email_challenges c ON c.pending_registration_id=pr.id AND c.purpose=0 AND c.consumed_at IS NOT NULL AND c.revoked_at IS NULL
 JOIN dango.email_jobs e ON e.challenge_id=c.id
 WHERE pr.normalized_email=u.normalized_email AND i.normalized_email=u.normalized_email AND c.normalized_email=u.normalized_email
 AND pr.recovery_epoch='$($Epoch.ToString('D'))'::uuid AND i.recovery_epoch=pr.recovery_epoch AND c.recovery_epoch=pr.recovery_epoch),
 'pendingUnsentEmails',(SELECT count(*) FROM dango.email_jobs e JOIN dango.email_challenges c ON c.id=e.challenge_id
 JOIN dango.pending_registrations pr ON pr.id=c.pending_registration_id JOIN selected u ON u.id=pr.user_id
 WHERE e.state=0 AND e.attempts=0 AND e.accepted_at IS NULL AND e.lease_id IS NULL AND e.lease_until IS NULL AND e.failure_code IS NULL),
 'sessions',(SELECT count(*) FROM dango.auth_sessions s JOIN selected u ON u.id=s.user_id),
 'validSessions',(SELECT count(*) FROM dango.auth_sessions s JOIN selected u ON u.id=s.user_id
 WHERE s.security_stamp=u.security_stamp AND s.recovery_epoch='$($Epoch.ToString('D'))'::uuid),
 'revokedSessions',(SELECT count(*) FROM dango.auth_sessions s JOIN selected u ON u.id=s.user_id WHERE s.revoked_at IS NOT NULL));
COMMIT;
"@
    Assert-DangoAccountSyncSqlTransportContract $sql
    return $sql
}

function Assert-DangoAccountSyncSqlTransportContract {
    param([string]$Sql)
    # Windows psql's narrow argv can use ACP despite PGCLIENTENCODING=UTF8.
    # Keep command-line SQL ASCII; the database interprets the Unicode literal.
    if ([string]::IsNullOrWhiteSpace($Sql) -or $Sql.Length -gt 16384 -or $Sql -cmatch '[^\x00-\x7F]' -or
        $Sql.Contains([char]0)) {throw 'accountsync_sql_transport_rejected'}
}

function Assert-DangoAccountSyncScopedGraph {
    param($Evidence)
    $expected=@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=4;validSessions=4;revokedSessions=4}
    if (@($Evidence.PSObject.Properties).Count -ne $expected.Count) {throw 'accountsync_scoped_shape_rejected'}
    foreach($key in $expected.Keys) {
        if (@($Evidence.PSObject.Properties.Name) -cnotcontains $key -or
            -not(Test-DangoAccountSyncInteger $Evidence.$key $expected[$key])) {throw 'accountsync_scoped_graph_rejected'}
    }
}

function Test-DangoAccountSyncIntegerType {
    param($Value)
    return ($Value -is [int] -or $Value -is [long])
}
function Test-DangoAccountSyncInteger {
    param($Value,[long]$Expected)
    return (Test-DangoAccountSyncIntegerType $Value) -and $Value -eq $Expected
}
function Test-DangoAccountSyncBoolean {
    param($Value,[bool]$Expected)
    return $Value -is [bool] -and $Value -eq $Expected
}
function Test-DangoAccountSyncObjectShape {
    param($Value,[string[]]$Fields)
    if ($Value -isnot [System.Management.Automation.PSCustomObject] -or @($Value.PSObject.Properties).Count -ne $Fields.Count) {return $false}
    foreach($field in $Fields) {if (@($Value.PSObject.Properties.Name) -cnotcontains $field) {return $false}}
    return $true
}
function Test-DangoAccountSyncTimestamp {
    param($Value)
    if ($Value -is [DateTimeOffset] -or $Value -is [DateTime]) {return $true}
    if ($Value -isnot [string] -or $Value -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|\+00:00)$') {return $false}
    $parsed=[DateTimeOffset]::MinValue
    return [DateTimeOffset]::TryParse($Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsed)
}

Export-ModuleMember -Function *-DangoAccountSync*

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1')
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1')

function Get-DangoAccountSyncClosurePlan {
    param([string]$Repository)
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    [pscustomobject]@{
        Version=1;Stage='AccountSyncB1a2Closure';RunName='run20261008b0';Executed=$false
        Repository=[IO.Path]::GetFullPath($Repository);StateDirectory=$state;DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261010accountsyncb1a2closure')
        PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin')
        Database='dango_backend_b0';Role='dango_b0_owner';DatabasePort=63237
        SourceOwnerId='4fd0debb-576b-4410-a8da-f8f434a2e390';UserId='c0179320-d30f-421f-a43a-7fd83f72999b'
        PetId='aac9ef69-694d-49bf-940d-3fc0d5992cbd';DefaultRoomId='c317c4f6-07a6-4cf2-8bde-8ca5e9fc4d8f'
        RecoveryEpoch='67559361-c6b5-487d-b4da-948bb049000a'
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity',
            '20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
        MaximumToolProcesses=5;ReadOnlyQueries=1;BusinessWriteAttempts=0;MigrationOperations=0
        Certificates=0;HttpsHosts=0;DesktopHosts=0;EmailDispatchOperations=0;SystemTrustOperations=0
        WindowsCredentialOperations=0;ExpectedTests=0;RetryAllowed=$false
    }
}

function Assert-DangoAccountSyncClosureFixedPlan {
    param($Plan)
    try {
        $fixed=Get-DangoAccountSyncClosurePlan $Plan.Repository
        if (@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count) {throw 'rejected'}
        foreach($property in $fixed.PSObject.Properties) {
            $actual=$Plan.PSObject.Properties[$property.Name]
            if ($null -eq $actual -or $actual.Name -cne $property.Name -or
                -not(Test-DangoProfileFixedValue $actual.Value $property.Value)) {throw 'rejected'}
        }
        return $fixed
    }catch{throw 'accountsync_closure_fixed_plan_rejected'}
}

function Get-DangoAccountSyncClosureSql {
    param($Plan)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
    $sql=Get-DangoAccountSyncScopedSql ([Guid]$fixed.UserId) ([Guid]$fixed.RecoveryEpoch) ([Guid]$fixed.SourceOwnerId)
    $selected="SELECT json_build_object('users',"
    $pet='JOIN dango.pets p ON p.user_id=u.id'
    $room='JOIN dango.rooms r ON r.owner_id=u.id'
    foreach($anchor in @($selected,$pet,$room)) {
        if (@([regex]::Matches($sql,[regex]::Escape($anchor))).Count -ne 1) {throw 'accountsync_closure_sql_anchor_rejected'}
    }
    $metadata=@'
SELECT json_build_object('database',current_database(),'role',current_user,
 'version',current_setting('server_version_num')::integer,'listenAddresses',current_setting('listen_addresses'),
 'dataDirectory',current_setting('data_directory'),
 'migrations',(SELECT COALESCE(json_agg("MigrationId" ORDER BY "MigrationId"),'[]'::json) FROM dango.__ef_migrations_history),
 'users',
'@
    $sql=$sql.Replace($selected,$metadata,[StringComparison]::Ordinal).
        Replace($pet,($pet+" AND p.id='$($fixed.PetId)'::uuid"),[StringComparison]::Ordinal).
        Replace($room,($room+" AND r.id='$($fixed.DefaultRoomId)'::uuid"),[StringComparison]::Ordinal)
    Assert-DangoAccountSyncSqlTransportContract $sql
    if (-not $sql.StartsWith('BEGIN READ ONLY;',[StringComparison]::Ordinal) -or
        -not $sql.TrimEnd().EndsWith('COMMIT;',[StringComparison]::Ordinal) -or
        @([regex]::Matches($sql,'(?im)^\s*SELECT\b')).Count -ne 1 -or
        $sql -match '(?i)\b(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP|TRUNCATE|COPY|CALL|DO|pg_read_file|pg_read_binary_file|pg_sleep|lo_import)\b') {
        throw 'accountsync_closure_sql_readonly_rejected'
    }
    return $sql
}

function Get-DangoAccountSyncClosureInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
    $files=@('eng/Dango.AccountSyncClosurePostgres.psm1','eng/invoke-account-sync-b1a2-closure.ps1',
        'eng/test-account-sync-b1a2-closure-boundary.ps1','eng/Dango.AccountProfilePostgres.psm1',
        'eng/Dango.AccountSyncPostgres.psm1','src/Dango.Server/Persistence/DangoDbContext.cs',
        'src/Dango.Server/Persistence/AccountLifecycleModel.cs','src/Dango.Server/Persistence/DangoDatabaseOptions.cs',
        'src/Dango.Server/Persistence/Entities/AccountFoundation.cs','src/Dango.Server/Persistence/Entities/AccountLifecycle.cs',
        'src/Dango.Server/Persistence/Migrations/DangoDbContextModelSnapshot.cs',
        'tests/Dango.Server.Tests/Configuration/AccountSyncScopedSqlMetadataTests.cs',
        'docs/planning/2026-10-10-account-sync-b1a2-closure-plan.md','tests/Dango.Server.Tests/Dango.Server.Tests.csproj',
        'src/Dango.Server/Dango.Server.csproj','Directory.Build.props','global.json')
    foreach($directory in @('src/Dango.Server/Persistence')) {
        $root=Join-Path $fixed.Repository $directory;Assert-DangoProfileRegularPath $root
        if ([IO.Directory]::Exists($root)) {
            foreach($file in @(Get-ChildItem -LiteralPath $root -Filter '*.cs' -File -Recurse)) {
                $files+=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
            }
        }
    }
    $files=@($files|Sort-Object -Unique);$hashes=[ordered]@{};$missing=@()
    if ($files.Count -gt 128) {throw 'accountsync_closure_inputs_bound_rejected'}
    foreach($relative in $files) {
        $path=Join-Path $fixed.Repository $relative;Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) {$missing+=$relative;continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $binaries=Get-DangoAccountSyncClosureBinaryInputs $fixed
    $missing+=@($binaries.MissingInputs)
    $sqlBytes=[Text.Encoding]::ASCII.GetBytes((Get-DangoAccountSyncClosureSql $fixed))
    [pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;Plan=$fixed;Hashes=$hashes;MissingInputs=$missing
        PostgresTools=$binaries.Hashes;SqlSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($sqlBytes))}
}

function Get-DangoAccountSyncClosureBinaryInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan;$hashes=[ordered]@{};$missing=@()
    foreach($name in @('postgres.exe','pg_ctl.exe','psql.exe')) {
        $path=Join-Path $fixed.PostgresBinDirectory $name;Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+='postgres-bin/'+$name;continue}
        $hashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    [pscustomobject]@{Hashes=$hashes;MissingInputs=$missing}
}

# JsonDocument preserves duplicate keys and JSON token kinds; ConvertFrom-Json alone does not.
function Assert-DangoAccountSyncClosureJsonValue {
    param([Text.Json.JsonElement]$Element,$Expected)
    if ($Expected -is [bool]) {
        if ($Element.ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False) -or
            $Element.GetBoolean() -ne $Expected) {throw 'accountsync_closure_json_value_rejected'}
    }elseif($Expected -is [int] -or $Expected -is [long]) {
        $number=[long]0
        if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $Element.TryGetInt64([ref]$number) -or
            $number -ne $Expected) {throw 'accountsync_closure_json_value_rejected'}
    }elseif($Expected -is [string]) {
        if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::String -or $Element.GetString() -cne $Expected) {
            throw 'accountsync_closure_json_value_rejected'
        }
    }elseif($Expected -is [Array]) {
        if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $Element.GetArrayLength() -ne $Expected.Count) {
            throw 'accountsync_closure_json_value_rejected'
        }
        for($i=0;$i -lt $Expected.Count;$i++) {Assert-DangoAccountSyncClosureJsonValue $Element[$i] $Expected[$i]}
    }else {
        if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object) {throw 'accountsync_closure_json_value_rejected'}
        $expectedMap=[ordered]@{}
        if ($Expected -is [Collections.IDictionary]) {foreach($key in $Expected.Keys) {$expectedMap[$key]=$Expected[$key]}}
        elseif($Expected -is [System.Management.Automation.PSCustomObject]) {
            foreach($property in $Expected.PSObject.Properties) {$expectedMap[$property.Name]=$property.Value}
        }else{throw 'accountsync_closure_json_value_rejected'}
        $properties=@($Element.EnumerateObject());$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($properties.Count -ne $expectedMap.Count) {throw 'accountsync_closure_json_value_rejected'}
        foreach($property in $properties) {
            if (-not $seen.Add($property.Name) -or $property.Name -cnotin @($expectedMap.Keys)) {
                throw 'accountsync_closure_json_value_rejected'
            }
            Assert-DangoAccountSyncClosureJsonValue $property.Value $expectedMap[$property.Name]
        }
    }
}

function Read-DangoAccountSyncClosureBytes {
    param([string]$Path,[int]$MaximumBytes)
    Assert-DangoProfileRegularPath $Path
    $stream=[IO.FileStream]::new($Path,'Open','Read','Read')
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt $MaximumBytes) {throw 'accountsync_closure_file_bound_rejected'}
        $bytes=[byte[]]::new([int]$stream.Length);$stream.ReadExactly($bytes)
        if ($stream.ReadByte() -ne -1) {throw 'accountsync_closure_file_bound_rejected'}
        return ,$bytes
    }finally{$stream.Dispose()}
}

function New-DangoAccountSyncClosureApproval {
    param($Plan,[string]$Path)
    $physical=Assert-DangoProfileManifestPath $Plan $Path;$inputs=Get-DangoAccountSyncClosureInputs $Plan
    if ($inputs.MissingInputs.Count -ne 0) {throw 'accountsync_closure_inputs_missing'}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    [pscustomobject]@{Path=$physical;Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash;Inputs=$inputs}
}

function Assert-DangoAccountSyncClosureApproval {
    param($Plan,[string]$Path,[string]$Digest)
    $document=$null
    try {
        $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
        if ($Digest -cnotmatch '^[A-F0-9]{64}$') {throw 'accountsync_closure_approval_digest_required'}
        $physical=Assert-DangoProfileManifestPath $fixed $Path
        $bytes=Read-DangoAccountSyncClosureBytes $physical 65536
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -cne $Digest) {
            throw 'accountsync_closure_approval_changed'
        }
        $document=[Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
        $current=Get-DangoAccountSyncClosureInputs $fixed
        if ($current.MissingInputs.Count -ne 0) {throw 'accountsync_closure_inputs_missing'}
        Assert-DangoAccountSyncClosureJsonValue $document.RootElement $current
        [pscustomobject]@{Path=$physical;Sha256=$Digest;Inputs=$current}
    }catch{
        if ($_.Exception.Message -cmatch '^accountsync_closure_[a-z0-9_]+$') {throw}
        throw 'accountsync_closure_approval_invalid'
    }finally{if($null -ne $document){$document.Dispose()}}
}

function Test-DangoAccountSyncClosurePrivateAcl {
    param($Acl,[Security.Principal.SecurityIdentifier]$Sid,[bool]$RequireProtected)
    try {
        if (($RequireProtected -and -not $Acl.AreAccessRulesProtected) -or
            -not $Sid.Equals($Acl.GetOwner([Security.Principal.SecurityIdentifier]))) {return $false}
        $full=$false
        foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Deny) {return $false}
            if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow) {
                if (-not $rule.IdentityReference.Equals($Sid)) {return $false}
                if (($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq
                    [Security.AccessControl.FileSystemRights]::FullControl) {$full=$true}
            }
        }
        return $full
    }catch{return $false}
}

function Assert-DangoAccountSyncClosurePrivatePath {
    param([string]$Path,[bool]$RequireProtected=$false)
    Assert-DangoProfileRegularPath $Path
    if (-not (Test-Path -LiteralPath $Path)) {throw 'accountsync_closure_existing_path_required'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        if (-not(Test-DangoAccountSyncClosurePrivateAcl (Get-Acl -LiteralPath $Path) $identity.User $RequireProtected)) {
            throw 'accountsync_closure_private_acl_rejected'
        }
    }finally{$identity.Dispose()}
}

function New-DangoAccountSyncClosureReservation {
    param($Plan)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
    $parent=Split-Path -Parent $fixed.EvidenceDirectory
    Assert-DangoProfileRegularPath $parent;Assert-DangoProfileRegularPath $fixed.EvidenceDirectory
    if (-not [IO.Directory]::Exists($parent)) {throw 'accountsync_closure_existing_parent_required'}
    Get-DangoProfileNativeType
    if (-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)) {
        throw 'accountsync_closure_already_reserved'
    }
    # The new directory consumes the allowance before any original state or credential is read.
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $sid=$identity.User;$acl=[Security.AccessControl.DirectorySecurity]::new()
        $acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
        Set-Acl -LiteralPath $fixed.EvidenceDirectory -AclObject $acl
    }finally{$identity.Dispose()}
    Assert-DangoAccountSyncClosurePrivatePath $fixed.EvidenceDirectory $true
    $owner=[Guid]::NewGuid();$process=[Diagnostics.Process]::GetCurrentProcess()
    try {$manifest=[pscustomobject]@{Version=1;OwnerId=$owner;Stage=$fixed.Stage;RunName=$fixed.RunName;
        OwnerPid=$process.Id;OwnerCreatedAt=$process.StartTime.ToUniversalTime().ToString('O');
        OwnerExecutable=$process.MainModule.FileName;RunDirectory=$fixed.EvidenceDirectory}}
    finally{$process.Dispose()}
    $reservation=[pscustomobject]@{Version=1;OwnerId=$owner;Stage=$fixed.Stage;Plan=$fixed;
        WholeClosureReserved=$true;RetryAllowed=$false;ReadOnlyQueries=1;BusinessWriteAttempts=0}
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'reservation.json') $reservation -CreateNew
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') $manifest -CreateNew
    return $reservation
}

function Assert-DangoAccountSyncClosureState {
    param($Plan,[string]$Json)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan;$document=$null
    try {
        $expected=[pscustomobject]@{RunName=$fixed.RunName;Database=$fixed.Database;Owner=$fixed.Role;
            DataDirectory=$fixed.DataDirectory;Port=$fixed.DatabasePort;Version=1}
        $document=[Text.Json.JsonDocument]::Parse($Json)
        # Paths have a filesystem equality contract; every other value remains token-type strict.
        $path=$document.RootElement.GetProperty('DataDirectory')
        if ($path.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            -not(Test-DangoProfilePath $path.GetString() $fixed.DataDirectory)) {throw 'rejected'}
        $expected.DataDirectory=$path.GetString()
        Assert-DangoAccountSyncClosureJsonValue $document.RootElement $expected
        return [pscustomobject]@{Port=$fixed.DatabasePort}
    }catch{throw 'accountsync_closure_state_rejected'}
    finally{if($null -ne $document){$document.Dispose()}}
}

function Save-DangoAccountSyncClosureFacts {
    param($Plan,[string]$Output,[ValidateSet('target-facts.json','query-stderr.txt',IgnoreCase=$false)][string]$FileName='target-facts.json')
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($Output)
    if ($Output.Length -gt 131072 -or $bytes.Length -gt 131072) {throw 'accountsync_closure_facts_bound_rejected'}
    $path=Join-Path $fixed.EvidenceDirectory $FileName;Assert-DangoProfileRegularPath $path
    $stream=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
}

function Assert-DangoAccountSyncClosureEvidence {
    param($Plan,[string]$Json)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan;$document=$null
    try {
        if ([string]::IsNullOrWhiteSpace($Json) -or $Json.Length -gt 131072 -or
            [Text.Encoding]::UTF8.GetByteCount($Json) -gt 131072) {throw 'rejected'}
        $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=4
        $document=[Text.Json.JsonDocument]::Parse($Json,$options)
        $version=[int]0;$v=$document.RootElement.GetProperty('version')
        $path=$document.RootElement.GetProperty('dataDirectory')
        if ($v.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $v.TryGetInt32([ref]$version) -or
            $version -lt 180000 -or $version -ge 190000 -or $path.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            -not(Test-DangoProfilePath $path.GetString() $fixed.DataDirectory)) {throw 'rejected'}
        $expected=[pscustomobject]@{database=$fixed.Database;role=$fixed.Role;version=$version;listenAddresses='127.0.0.1';
            dataDirectory=$path.GetString();migrations=@($fixed.PriorMigrations);users=1;validUsers=1;namedUsers=1;
            completeGraphs=1;pendingUnsentEmails=1;sessions=4;validSessions=4;revokedSessions=4}
        Assert-DangoAccountSyncClosureJsonValue $document.RootElement $expected
        return $expected
    }catch{throw 'accountsync_closure_evidence_rejected'}
    finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoAccountSyncClosureToolContract {
    param($Plan,[string]$Step,[string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,
        [hashtable]$EnvironmentValues,[int]$TimeoutSeconds,[bool]$KillOwnedTree,[string]$OutputContract,[bool]$PreserveRawOutput=$false)
    $fixed=Assert-DangoAccountSyncClosureFixedPlan $Plan
    $expectedExecutable=Join-Path $fixed.PostgresBinDirectory 'pg_ctl.exe'
    $expectedEnvironment=0;$expectedTimeout=30;$expectedOutput='CompleteEof'
    $expectedArguments=switch -CaseSensitive ($Step) {
        version {$expectedExecutable=Join-Path $fixed.PostgresBinDirectory 'postgres.exe';@('--version')}
        status {@('status','-D',$fixed.DataDirectory)}
        start {$expectedTimeout=40;$expectedOutput='PostgresStartIgnored';@('start','-D',$fixed.DataDirectory,'-l',
            (Join-Path $fixed.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($fixed.DatabasePort)",'-w','-t','30')}
        scoped {$expectedEnvironment=2;$expectedExecutable=Join-Path $fixed.PostgresBinDirectory 'psql.exe';
            @('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$fixed.DatabasePort,
                '-U',$fixed.Role,'-d',$fixed.Database,'-c',(Get-DangoAccountSyncClosureSql $fixed))}
        stop {$expectedTimeout=40;@('stop','-D',$fixed.DataDirectory,'-m','fast','-w','-t','30')}
        default {throw 'accountsync_closure_tool_contract_rejected'}
    }
    if ($PreserveRawOutput -ne ($Step -ceq 'scoped') -or $KillOwnedTree -or $TimeoutSeconds -ne $expectedTimeout -or $OutputContract -cne $expectedOutput -or
        $EnvironmentValues.Count -ne $expectedEnvironment -or
        -not(Test-DangoProfilePath $Executable $expectedExecutable) -or
        -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository) -or
        -not(Test-DangoProfileFixedValue $Arguments ([string[]]$expectedArguments))) {throw 'accountsync_closure_tool_contract_rejected'}
    if ($Step -ceq 'scoped') {
        $decoded=$null
        try {
            if (@($EnvironmentValues.Keys) -cnotcontains 'PGPASSWORD' -or @($EnvironmentValues.Keys) -cnotcontains 'PGCLIENTENCODING' -or
                $EnvironmentValues.PGCLIENTENCODING -isnot [string] -or $EnvironmentValues.PGCLIENTENCODING -cne 'UTF8' -or
                $EnvironmentValues.PGPASSWORD -isnot [string] -or $EnvironmentValues.PGPASSWORD -cnotmatch '^[A-Za-z0-9+/]{43}=$') {throw 'rejected'}
            $decoded=[Convert]::FromBase64String($EnvironmentValues.PGPASSWORD)
            if ($decoded.Length -ne 32 -or [Convert]::ToBase64String($decoded) -cne $EnvironmentValues.PGPASSWORD) {throw 'rejected'}
        }catch{throw 'accountsync_closure_tool_contract_rejected'}
        finally{if($null -ne $decoded){[Security.Cryptography.CryptographicOperations]::ZeroMemory($decoded)}}
    }
}

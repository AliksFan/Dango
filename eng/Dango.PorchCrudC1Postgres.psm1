Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -DisableNameChecking

function Get-DangoPorchCrudC1Plan {
    param([string]$Repository)
    Assert-DangoPorchCrudC1PathText $Repository
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    [pscustomobject]@{Version=1;Stage='PorchCrudC1';RunName='run20261008b0';Executed=$false
        Repository=[IO.Path]::GetFullPath($Repository);StateDirectory=$state;DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261011porchcrudc1')
        PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin')
        Database='dango_backend_b0';Role='dango_b0_owner';DatabasePort=63237;ExpectedTests=1;MaximumToolProcesses=10
        Filter='FullyQualifiedName=Dango.AccountSyncReview.Tests.PorchCrudC1JourneyTests.NativePorchCrudPersistsAndForeignAccountCannotReadOrMutate'
        CandidateDirectory='artifacts/porch-crud-c1-candidate/bin/Dango.AccountSyncReview.Tests/release'
        ApprovalVariable='DANGO_BACKEND_PORCHCRUD_C1_APPROVED';TrxFileName='porch-crud-c1.trx'
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity',
            '20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
        Budgets=[ordered]@{Invite=2;Start=2;Confirm=2;Complete=2;Login=2;Profile=2;Logout=2;Create=1;Update=2;Dissolve=2;Restore=2}
        MaximumHttpRequests=128;MinimumHttpStartIntervalMilliseconds=600;MaximumDurationMinutes=10;ProcessTimeoutSeconds=300;HttpsHosts=1;Certificates=1;DesktopHosts=1
        MigrationOperations=0;RoomOperations=7;SystemTrustOperations=0;WindowsCredentialOperations=0;EmailDispatchOperations=0
        RetryAllowed=$false}
}

function Assert-DangoPorchCrudC1PathText {
    param([string]$Path)
    if([string]::IsNullOrWhiteSpace($Path)){throw 'porch_c1_path_text_rejected'}
    foreach($character in $Path.ToCharArray()){
        if([char]::IsControl($character) -or [char]::GetUnicodeCategory($character) -eq [Globalization.UnicodeCategory]::Format){
            throw 'porch_c1_path_text_rejected'}
    }
}

function Test-DangoPorchCrudC1String {
    param($Actual,[string]$Expected)
    return $Actual -is [string] -and [string]::Equals($Actual,$Expected,[StringComparison]::Ordinal)
}

function Test-DangoPorchCrudC1StringIn {
    param($Actual,[string[]]$Expected)
    foreach($value in $Expected){if(Test-DangoPorchCrudC1String $Actual $value){return $true}}
    return $false
}

function Test-DangoPorchCrudC1ObjectShape {
    param($Value,[string[]]$Fields)
    if($Value -isnot [System.Management.Automation.PSCustomObject] -or @($Value.PSObject.Properties).Count -ne $Fields.Count){return $false}
    foreach($field in $Fields){if(-not(Test-DangoPorchCrudC1StringIn $field @($Value.PSObject.Properties.Name))){return $false}}
    return $true
}

function Test-DangoPorchCrudC1FixedValue {
    param($Actual,$Expected)
    if($null -eq $Expected){return $null -eq $Actual}
    if($null -eq $Actual){return $false}
    if($Expected -is [string]){return Test-DangoPorchCrudC1String $Actual $Expected}
    if($Expected -is [Collections.IDictionary]){
        if($Actual -isnot [Collections.IDictionary] -or $Actual.Count -ne $Expected.Count){return $false}
        foreach($key in $Expected.Keys){
            $actualKey=@($Actual.Keys|Where-Object{[string]::Equals($_,$key,[StringComparison]::Ordinal)})
            if($actualKey.Count -ne 1 -or -not(Test-DangoPorchCrudC1FixedValue $Actual[$actualKey[0]] $Expected[$key])){return $false}
        };return $true
    }
    if($Expected -is [Array]){
        if($Actual -isnot [Array] -or $Actual.Count -ne $Expected.Count){return $false}
        for($i=0;$i -lt $Expected.Count;$i++){if(-not(Test-DangoPorchCrudC1FixedValue $Actual[$i] $Expected[$i])){return $false}}
        return $true
    }
    return $Actual.GetType() -eq $Expected.GetType() -and $Actual -ceq $Expected
}

function Assert-DangoPorchCrudC1FixedPlan {
    param($Plan)
    try {
        $fixed=Get-DangoPorchCrudC1Plan $Plan.Repository
        if(@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count){throw 'rejected'}
        foreach($property in $fixed.PSObject.Properties){$actual=$Plan.PSObject.Properties[$property.Name]
            if($null -eq $actual -or -not(Test-DangoPorchCrudC1String $actual.Name $property.Name) -or
                -not(Test-DangoPorchCrudC1FixedValue $actual.Value $property.Value)){throw 'rejected'}}
        return $fixed
    }catch{throw 'porch_c1_fixed_plan_rejected'}
}

function Get-DangoPorchCrudC1Inputs {
    param($Plan)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan
    $files=@('eng/Dango.PorchCrudC1Postgres.psm1','eng/invoke-porch-crud-c1-tests.ps1','eng/test-porch-crud-c1-boundary.ps1',
        'eng/Dango.AccountProfilePostgres.psm1','eng/Dango.AccountSyncPostgres.psm1','eng/Dango.AccountSyncClosurePostgres.psm1',
        'eng/Dango.AccountSyncB1c2Postgres.psm1','docs/planning/2026-10-11-porch-crud-c1-local-plan.md','Directory.Build.props','global.json',
        'tests/Dango.Server.Tests/Configuration/PorchCrudC1ScopedSqlMetadataTests.cs',
        'tests/Dango.Desktop.Tests/AppShell/RoomIntegrationBoundaryTests.cs',
        'tests/Dango.Desktop.Tests/AppShell/PorchAccountSceneTests.cs','tests/Dango.Desktop.Tests/AppShell/RoomPanelViewTests.cs',
        'artifacts/main-absorption/9db6fd423e364e488967e0696ecc05d1/native/Release/dango_physics_module.dll')
    foreach($relativeRoot in @('src','eng/tools/Dango.AccountSyncReview.Tests')){
        $root=Join-Path $fixed.Repository $relativeRoot;Assert-DangoProfileRegularPath $root
        if([IO.Directory]::Exists($root)){
            foreach($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)){
                $relative=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
                if($relative -match '/(?:obj|bin)/'){continue}
                if(Test-DangoPorchCrudC1StringIn ($file.Extension.ToLowerInvariant()) @('.cs','.csproj','.props','.targets','.xaml')){
                    Assert-DangoProfileRegularPath $file.FullName;$files+=$relative
                }
            }
        }
    }
    $candidate=Join-Path $fixed.Repository $fixed.CandidateDirectory;Assert-DangoProfileRegularPath $candidate
    if([IO.Directory]::Exists($candidate)){
        foreach($file in @(Get-ChildItem -LiteralPath $candidate -Recurse -File -Force)){
            Assert-DangoProfileRegularPath $file.FullName
            $files+=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
        }
    }
    $files+=($fixed.CandidateDirectory+'/Dango.AccountSyncReview.Tests.dll')
    $files=@($files|Sort-Object -Unique);$hashes=[ordered]@{};$missing=@()
    if($files.Count -gt 1024){throw 'porch_c1_inputs_bound_rejected'}
    foreach($relative in $files){$path=Join-Path $fixed.Repository $relative;Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+=$relative;continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    $native='artifacts/main-absorption/9db6fd423e364e488967e0696ecc05d1/native/Release/dango_physics_module.dll'
    if($hashes.Contains($native) -and -not(Test-DangoPorchCrudC1String $hashes[$native] 'B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E')){
        throw 'porch_c1_native_digest_rejected'}
    $tools=Get-DangoPorchCrudC1BinaryInputs $fixed;$missing+=@($tools.MissingInputs)
    [pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;Plan=$fixed;Hashes=$hashes;
        PostgresTools=$tools.Hashes;MissingInputs=$missing}
}

function Get-DangoPorchCrudC1BinaryInputs {
    param($Plan)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan;$hashes=[ordered]@{};$missing=@()
    foreach($name in @('postgres.exe','pg_ctl.exe','psql.exe')){$path=Join-Path $fixed.PostgresBinDirectory $name
        Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+='postgres-bin/'+$name;continue}
        $hashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    [pscustomobject]@{Hashes=$hashes;MissingInputs=$missing}
}

function New-DangoPorchCrudC1Approval {
    param($Plan,[string]$Path)
    Assert-DangoPorchCrudC1PathText $Path
    $physical=Assert-DangoProfileManifestPath $Plan $Path;$inputs=Get-DangoPorchCrudC1Inputs $Plan
    if($inputs.MissingInputs.Count -ne 0){throw 'porch_c1_inputs_missing'}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    [pscustomobject]@{Path=$physical;Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash;Inputs=$inputs}
}

function Assert-DangoPorchCrudC1Approval {
    param($Plan,[string]$Path,[string]$Digest)
    $document=$null
    try{
        $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan
        Assert-DangoPorchCrudC1PathText $Path
        if($Digest -cnotmatch '\A[A-F0-9]{64}\z'){throw 'porch_c1_approval_digest_required'}
        $physical=Assert-DangoProfileManifestPath $fixed $Path;$bytes=Read-DangoAccountSyncClosureBytes $physical 262144
        if(-not(Test-DangoPorchCrudC1String ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) $Digest)){
            throw 'porch_c1_approval_changed'}
        $document=[Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
        $current=Get-DangoPorchCrudC1Inputs $fixed
        if($current.MissingInputs.Count -ne 0){throw 'porch_c1_inputs_missing'}
        Assert-DangoPorchCrudC1JsonValue $document.RootElement $current
        return [pscustomobject]@{Path=$physical;Sha256=$Digest;Inputs=$current}
    }catch{
        if($_.Exception.Message -cmatch '^(accountsync_b1c|accountsync_closure|profile_run)_[a-z0-9_]+$'){throw}
        throw 'porch_c1_approval_invalid'
    }finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoPorchCrudC1JsonValue {
    param([Text.Json.JsonElement]$Element,$Expected)
    if($Expected -is [bool]){
        if($Element.ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False) -or
            $Element.GetBoolean() -ne $Expected){throw 'porch_c1_json_value_rejected'}
    }elseif($Expected -is [int] -or $Expected -is [long]){
        $number=[long]0
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $Element.TryGetInt64([ref]$number) -or
            $number -ne $Expected){throw 'porch_c1_json_value_rejected'}
    }elseif($Expected -is [string]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            -not(Test-DangoPorchCrudC1String $Element.GetString() $Expected)){throw 'porch_c1_json_value_rejected'}
    }elseif($Expected -is [Array]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $Element.GetArrayLength() -ne $Expected.Count){
            throw 'porch_c1_json_value_rejected'}
        for($i=0;$i -lt $Expected.Count;$i++){Assert-DangoPorchCrudC1JsonValue $Element[$i] $Expected[$i]}
    }else{
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object){throw 'porch_c1_json_value_rejected'}
        $expectedMap=[ordered]@{}
        if($Expected -is [Collections.IDictionary]){foreach($key in $Expected.Keys){$expectedMap[$key]=$Expected[$key]}}
        elseif($Expected -is [System.Management.Automation.PSCustomObject]){
            foreach($property in $Expected.PSObject.Properties){$expectedMap[$property.Name]=$property.Value}
        }else{throw 'porch_c1_json_value_rejected'}
        $properties=@($Element.EnumerateObject());$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if($properties.Count -ne $expectedMap.Count){throw 'porch_c1_json_value_rejected'}
        foreach($property in $properties){
            if(-not $seen.Add($property.Name) -or -not(Test-DangoPorchCrudC1StringIn $property.Name @($expectedMap.Keys))){
                throw 'porch_c1_json_value_rejected'}
            Assert-DangoPorchCrudC1JsonValue $property.Value $expectedMap[$property.Name]
        }
    }
}

function New-DangoPorchCrudC1Reservation {
    param($Plan)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan;$parent=Split-Path -Parent $fixed.EvidenceDirectory
    Assert-DangoProfileRegularPath $parent;Assert-DangoProfileRegularPath $fixed.EvidenceDirectory
    if(-not [IO.Directory]::Exists($parent)){throw 'porch_c1_existing_parent_required'}
    Get-DangoProfileNativeType
    if(-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)){throw 'porch_c1_already_reserved'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try{$sid=$identity.User;$acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
        Set-Acl -LiteralPath $fixed.EvidenceDirectory -AclObject $acl}finally{$identity.Dispose()}
    Assert-DangoAccountSyncClosurePrivatePath $fixed.EvidenceDirectory $true
    $owner=[Guid]::NewGuid();$process=[Diagnostics.Process]::GetCurrentProcess()
    try{$manifest=[pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;OwnerId=$owner;OwnerPid=$process.Id;
        OwnerCreatedAt=$process.StartTime.ToUniversalTime().ToString('O');OwnerExecutable=$process.MainModule.FileName;
        RunDirectory=$fixed.EvidenceDirectory;ExpectedTests=1;Budgets=$fixed.Budgets}}finally{$process.Dispose()}
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'reservation.json') ([pscustomobject]@{
        Plan=$fixed;OwnerId=$owner;RetryAllowed=$false;WholeSuiteReserved=$true}) -CreateNew
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') $manifest -CreateNew
    foreach($name in @('attempts.jsonl','resources.jsonl')){$stream=[IO.FileStream]::new((Join-Path $fixed.EvidenceDirectory $name),'CreateNew','Write','Read',4096,[IO.FileOptions]::WriteThrough)
        try{$stream.Flush($true)}finally{$stream.Dispose()}}
    [pscustomobject]@{OwnerId=$owner;Owner=$manifest;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') -Algorithm SHA256).Hash}
}

function Get-DangoPorchCrudC1PsqlArguments {
    param($Plan,[string]$Sql)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan
    [string[]]@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$fixed.DatabasePort,
        '-U',$fixed.Role,'-d',$fixed.Database,'-c',$Sql)
}

function Assert-DangoPorchCrudC1EvidenceContract {
    param([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,$Plan,[int]$Port,[hashtable]$EnvironmentValues,
        [bool]$KillOwnedTree,[string]$OutputContract)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan;$accepted=$false
    try{Assert-DangoPorchCrudC1PathText $Executable;Assert-DangoPorchCrudC1PathText $WorkingDirectory}
    catch{throw 'porch_c1_evidence_contract_rejected'}
    if($Arguments.Count -ne 17){throw 'porch_c1_evidence_contract_rejected'}
    try{Assert-DangoAccountSyncSqlTransportContract $Arguments[16]}catch{throw 'porch_c1_evidence_contract_rejected'}
    if(Test-DangoPorchCrudC1String $OutputContract 'PorchCrudC1EvidenceJson'){
        $accepted=Test-DangoPorchCrudC1FixedValue $Arguments (Get-DangoPorchCrudC1PsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql))
    }elseif(Test-DangoPorchCrudC1String $OutputContract 'PorchCrudC1SmallJson'){
        $accepted=Test-DangoPorchCrudC1FixedValue $Arguments (Get-DangoPorchCrudC1PsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql -MeasureOnly))
        if(-not $accepted -and $Arguments.Count -eq 17){
            $scope=ConvertFrom-DangoPorchCrudC1SqlIdentity $Arguments[16]
            $accepted=Test-DangoPorchCrudC1FixedValue $Arguments (Get-DangoPorchCrudC1PsqlArguments $fixed (Get-DangoPorchCrudC1ScopedSql $scope))
        }
    }
    $decoded=$null
    try{
        if(-not $accepted -or $Port -ne 63237 -or $KillOwnedTree -or $EnvironmentValues.Count -ne 2 -or
            -not(Test-DangoPorchCrudC1StringIn 'PGPASSWORD' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoPorchCrudC1StringIn 'PGCLIENTENCODING' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoPorchCrudC1String $EnvironmentValues.PGCLIENTENCODING 'UTF8') -or
            $EnvironmentValues.PGPASSWORD -isnot [string] -or $EnvironmentValues.PGPASSWORD -cnotmatch '\A[A-Za-z0-9+/]{43}=\z' -or
            -not(Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'psql.exe')) -or
            -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository)){throw 'rejected'}
        $decoded=[Convert]::FromBase64String($EnvironmentValues.PGPASSWORD)
        if($decoded.Length -ne 32 -or -not(Test-DangoPorchCrudC1String ([Convert]::ToBase64String($decoded)) $EnvironmentValues.PGPASSWORD)){throw 'rejected'}
    }catch{throw 'porch_c1_evidence_contract_rejected'}
    finally{if($null -ne $decoded){[Security.Cryptography.CryptographicOperations]::ZeroMemory($decoded)}}
}

function Save-DangoPorchCrudC1RawEvidence {
    param($Plan,[string]$FileName,[string]$Text,[int]$MaximumBytes=131072)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan
    if($FileName -cnotmatch '\A(before|after)(-size)?\.(json|stderr\.txt)\z' -and
        -not(Test-DangoPorchCrudC1StringIn $FileName @('scoped-graph-evidence.json','scoped.stderr.txt'))){throw 'porch_c1_evidence_path_rejected'}
    if($MaximumBytes -ne 131072 -and ($MaximumBytes -ne 1048576 -or
        -not(Test-DangoPorchCrudC1StringIn $FileName @('before.json','after.json')))){throw 'porch_c1_evidence_bound_rejected'}
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($Text)
    if($Text.Length -gt $MaximumBytes -or $bytes.Length -gt $MaximumBytes){throw 'porch_c1_evidence_bound_rejected'}
    $path=Join-Path $fixed.EvidenceDirectory $FileName;Assert-DangoProfileRegularPath $path
    $stream=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
}

function ConvertFrom-DangoPorchCrudC1StrictJson {
    param([string]$Json,[int]$MaximumBytes=131072)
    $document=$null
    try{
        if([string]::IsNullOrWhiteSpace($Json) -or $Json.Length -gt $MaximumBytes -or [Text.Encoding]::UTF8.GetByteCount($Json) -gt $MaximumBytes){throw 'rejected'}
        $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=32
        $document=[Text.Json.JsonDocument]::Parse($Json,$options)
        $value=$Json|ConvertFrom-Json -Depth 32
        if($value -isnot [System.Management.Automation.PSCustomObject]){throw 'rejected'}
        Assert-DangoPorchCrudC1JsonKeys $document.RootElement
        return $value
    }catch{throw 'porch_c1_json_rejected'}
    finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoPorchCrudC1JsonKeys {
    param([Text.Json.JsonElement]$Element)
    if($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object){
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($property in $Element.EnumerateObject()){
            if(-not $seen.Add($property.Name)){throw 'porch_c1_duplicate_json_rejected'}
            Assert-DangoPorchCrudC1JsonKeys $property.Value
        }
    }elseif($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array){
        foreach($child in $Element.EnumerateArray()){Assert-DangoPorchCrudC1JsonKeys $child}
    }
}

function Assert-DangoPorchCrudC1ProtectedGraphShape {
    param($Evidence)
    Assert-DangoProfileProtectedGraphShape $Evidence
    $keys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
        'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
    if(-not(Test-DangoPorchCrudC1ObjectShape $Evidence.counts $keys) -or
        -not(Test-DangoPorchCrudC1ObjectShape $Evidence.digests $keys)){throw 'porch_c1_graph_shape_rejected'}
    foreach($key in $keys){foreach($digest in $Evidence.digests.$key){
        if($digest -isnot [string] -or $digest -cnotmatch '\A[a-f0-9]{64}\z'){throw 'porch_c1_graph_shape_rejected'}}}
}

function Assert-DangoPorchCrudC1Target {
    param($Plan,$Evidence)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan;Assert-DangoPorchCrudC1ProtectedGraphShape $Evidence
    Assert-DangoPorchCrudC1PathText $Evidence.dataDirectory
    $fields=@('database','role','version','dataDirectory','listenAddresses','migrations','userIds','profiles','counts','digests')
    if(-not(Test-DangoPorchCrudC1ObjectShape $Evidence $fields) -or -not(Test-DangoPorchCrudC1String $Evidence.database $fixed.Database) -or
        -not(Test-DangoPorchCrudC1String $Evidence.role $fixed.Role) -or -not(Test-DangoAccountSyncIntegerType $Evidence.version) -or
        $Evidence.version -lt 180000 -or $Evidence.version -ge 190000 -or $Evidence.listenAddresses -isnot [string] -or
        -not(Test-DangoPorchCrudC1String $Evidence.listenAddresses '127.0.0.1') -or $Evidence.dataDirectory -isnot [string] -or
        -not(Test-DangoProfilePath $Evidence.dataDirectory $fixed.DataDirectory) -or $Evidence.migrations -isnot [Array] -or
        -not(Test-DangoPorchCrudC1FixedValue $Evidence.migrations $fixed.PriorMigrations) -or $Evidence.userIds -isnot [Array] -or
        $Evidence.profiles -isnot [Array] -or $Evidence.userIds.Count -ne $Evidence.counts.users -or
        $Evidence.profiles.Count -ne $Evidence.counts.users){throw 'porch_c1_target_rejected'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($id in $Evidence.userIds){$parsed=[Guid]::Empty
        if($id -isnot [string] -or -not [Guid]::TryParseExact($id,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
            -not(Test-DangoPorchCrudC1String $id $parsed.ToString('D')) -or
            -not $seen.Add($id)){throw 'porch_c1_target_rejected'}
        $profile=@($Evidence.profiles|Where-Object {Test-DangoPorchCrudC1String $_.id $id})
        if($profile.Count -ne 1 -or -not(Test-DangoPorchCrudC1ObjectShape $profile[0] @('id','nicknameIsNull','version')) -or
            $profile[0].nicknameIsNull -isnot [bool] -or -not(Test-DangoAccountSyncIntegerType $profile[0].version) -or
            $profile[0].version -lt 1){throw 'porch_c1_target_rejected'}
    }
}

function Assert-DangoPorchCrudC1State {
    param($Plan,$State,$EpochState)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan
    Assert-DangoPorchCrudC1PathText $State.DataDirectory
    if(-not(Test-DangoPorchCrudC1ObjectShape $State @('RunName','Database','Owner','DataDirectory','Port','Version')) -or
        -not(Test-DangoPorchCrudC1ObjectShape $EpochState @('Version','RecoveryEpoch')) -or
        -not(Test-DangoAccountSyncInteger $State.Version 1) -or -not(Test-DangoAccountSyncInteger $State.Port 63237) -or
        -not(Test-DangoAccountSyncInteger $EpochState.Version 1) -or -not(Test-DangoPorchCrudC1String $State.RunName $fixed.RunName) -or
        -not(Test-DangoPorchCrudC1String $State.Database $fixed.Database) -or -not(Test-DangoPorchCrudC1String $State.Owner $fixed.Role) -or
        $State.DataDirectory -isnot [string] -or -not(Test-DangoProfilePath $State.DataDirectory $fixed.DataDirectory)){
        throw 'porch_c1_state_rejected'}
    $epoch=[Guid]::Empty
    if($EpochState.RecoveryEpoch -isnot [string] -or -not [Guid]::TryParseExact($EpochState.RecoveryEpoch,'D',[ref]$epoch) -or
        -not(Test-DangoPorchCrudC1String $EpochState.RecoveryEpoch $epoch.ToString('D')) -or
        $epoch -eq [Guid]::Empty){throw 'porch_c1_state_rejected'}
    [pscustomobject]@{Port=63237;Epoch=$epoch}
}

function Assert-DangoPorchCrudC1Trx {
    param([string]$Path)
    Assert-DangoPorchCrudC1PathText $Path
    Assert-DangoProfileRegularPath $Path
    if(-not(Test-DangoPorchCrudC1String ([IO.Path]::GetFileName($Path)) 'porch-crud-c1.trx')){throw 'porch_c1_trx_path_rejected'}
    $file=[IO.FileInfo]::new($Path);if(-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 1048576){throw 'porch_c1_trx_rejected'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing='Prohibit';$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings);$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    try{$xml.Load($reader)}finally{$reader.Dispose()}
    $results=@($xml.SelectNodes("//*[local-name()='UnitTestResult']"));$c=$xml.SelectSingleNode("//*[local-name()='Counters']")
    $definitions=@($xml.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
    $entries=@($xml.SelectNodes("//*[local-name()='TestEntries']/*[local-name()='TestEntry']"))
    $name='NativePorchCrudPersistsAndForeignAccountCannotReadOrMutate'
    if($results.Count -ne 1 -or -not(Test-DangoPorchCrudC1String $results[0].outcome 'Passed') -or
        -not(Test-DangoPorchCrudC1String $results[0].testName $name) -or $null -eq $c -or
        $definitions.Count -ne 1 -or $entries.Count -ne 1 -or
        -not(Test-DangoPorchCrudC1String $c.total '1') -or -not(Test-DangoPorchCrudC1String $c.executed '1') -or
        -not(Test-DangoPorchCrudC1String $c.passed '1') -or -not(Test-DangoPorchCrudC1String $c.failed '0') -or
        -not(Test-DangoPorchCrudC1String $c.notExecuted '0')){
        throw 'porch_c1_trx_rejected'}
    $definition=$definitions[0];$entry=$entries[0]
    $methods=@($definition.SelectNodes("./*[local-name()='TestMethod']"));$executions=@($definition.SelectNodes("./*[local-name()='Execution']"))
    if($methods.Count -ne 1 -or $executions.Count -ne 1 -or
        -not(Test-DangoPorchCrudC1String ($definition.GetAttribute('name')) $name) -or
        -not(Test-DangoPorchCrudC1String ($methods[0].GetAttribute('name')) $name) -or
        -not(Test-DangoPorchCrudC1String ($methods[0].GetAttribute('className')) 'Dango.AccountSyncReview.Tests.PorchCrudC1JourneyTests')){
        throw 'porch_c1_trx_rejected'}
    $testId=$definition.GetAttribute('id');$executionId=$executions[0].GetAttribute('id')
    foreach($id in @($testId,$executionId)){$parsed=[Guid]::Empty
        if(-not [Guid]::TryParseExact($id,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty){throw 'porch_c1_trx_rejected'}}
    if(-not(Test-DangoPorchCrudC1String ($results[0].GetAttribute('testId')) $testId) -or
        -not(Test-DangoPorchCrudC1String ($entry.GetAttribute('testId')) $testId) -or
        -not(Test-DangoPorchCrudC1String ($results[0].GetAttribute('executionId')) $executionId) -or
        -not(Test-DangoPorchCrudC1String ($entry.GetAttribute('executionId')) $executionId)){throw 'porch_c1_trx_rejected'}
}

function Assert-DangoPorchCrudC1Attempts {
    param($Plan,[Guid]$OwnerId,$Rows)
    $fixed=Assert-DangoPorchCrudC1FixedPlan $Plan;$used=@{};$sequence=0
    if(@($Rows).Count -ne 21){throw 'porch_c1_attempt_count_rejected'}
    foreach($row in $Rows){$sequence++;$parsed=[Guid]::Empty
        if(-not(Test-DangoPorchCrudC1ObjectShape $row @('Version','OwnerId','Sequence','Operation','Used','AtUtc')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence $sequence) -or
            -not(Test-DangoPorchCrudC1String $row.OwnerId $OwnerId.ToString('D')) -or
            -not(Test-DangoPorchCrudC1StringIn $row.Operation @($fixed.Budgets.Keys)) -or
            -not(Test-DangoAccountSyncTimestamp $row.AtUtc)){throw 'porch_c1_attempt_identity_rejected'}
        $count=if($used.ContainsKey($row.Operation)){$used[$row.Operation]+1}else{1}
        if($count -gt $fixed.Budgets[$row.Operation] -or -not(Test-DangoAccountSyncInteger $row.Used $count)){
            throw 'porch_c1_attempt_budget_rejected'}
        $used[$row.Operation]=$count
    }
    foreach($key in $fixed.Budgets.Keys){if($used[$key] -ne $fixed.Budgets[$key]){throw 'porch_c1_attempt_budget_rejected'}}
    [pscustomobject]@{Total=21;Operations=$used}
}

function ConvertTo-DangoPorchCrudC1Guid {
    param($Value)
    $id=[Guid]::Empty
    if($Value -isnot [string] -or -not [Guid]::TryParseExact($Value,'D',[ref]$id) -or $id -eq [Guid]::Empty -or
        $Value -cne $id.ToString('D')){throw 'porch_c1_identity_rejected'}
    return $id
}

function Assert-DangoPorchCrudC1ScopeInputs {
    param($Scope,[Guid]$OwnerId=[Guid]::Empty,[Guid]$Epoch=[Guid]::Empty,$NewUserIds=$null)
    if(-not(Test-DangoPorchCrudC1ObjectShape $Scope @('Version','OwnerId','RecoveryEpoch','Accounts','RoomId','RoomRequestIds')) -or
        -not(Test-DangoAccountSyncInteger $Scope.Version 1) -or $Scope.Accounts -isnot [Array] -or $Scope.Accounts.Count -ne 2 -or
        $Scope.RoomRequestIds -isnot [Array] -or $Scope.RoomRequestIds.Count -ne 7){throw 'porch_c1_scope_rejected'}
    $parsedOwner=ConvertTo-DangoPorchCrudC1Guid $Scope.OwnerId;$parsedEpoch=ConvertTo-DangoPorchCrudC1Guid $Scope.RecoveryEpoch
    if(($OwnerId -ne [Guid]::Empty -and $parsedOwner -ne $OwnerId) -or ($Epoch -ne [Guid]::Empty -and $parsedEpoch -ne $Epoch)){throw 'porch_c1_scope_rejected'}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($value in @($Scope.RoomId)+@($Scope.RoomRequestIds)){[void](ConvertTo-DangoPorchCrudC1Guid $value);if(-not $ids.Add($value)){throw 'porch_c1_scope_rejected'}}
    for($i=0;$i -lt 2;$i++){
        $actor=$Scope.Accounts[$i];$letter=@('a','b')[$i];$nickname=@('门廊主人','门廊访客')[$i]
        if(-not(Test-DangoPorchCrudC1ObjectShape $actor @('Actor','UserId','PetId','DefaultRoomId','SessionId','Nickname','ProfileVersion','ConfigurationVersion')) -or
            $actor.Actor -cne $letter -or $actor.Nickname -cne $nickname -or -not(Test-DangoAccountSyncInteger $actor.ProfileVersion 2) -or
            -not(Test-DangoAccountSyncInteger $actor.ConfigurationVersion 1)){throw 'porch_c1_scope_rejected'}
        foreach($key in @('UserId','PetId','DefaultRoomId','SessionId')){[void](ConvertTo-DangoPorchCrudC1Guid $actor.$key);if(-not $ids.Add($actor.$key)){throw 'porch_c1_scope_rejected'}}
    }
    if($null -ne $NewUserIds -and (@($NewUserIds).Count -ne 2 -or
        @($NewUserIds|Where-Object{$_ -cnotin @($Scope.Accounts.UserId)}).Count -ne 0)){throw 'porch_c1_new_users_rejected'}
}

function Get-DangoPorchCrudC1ScopedSql {
    param($Scope)
    Assert-DangoPorchCrudC1ScopeInputs $Scope
    $identity=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Scope|ConvertTo-Json -Depth 12 -Compress)))
    $accounts=@()
    foreach($a in $Scope.Accounts){$nickname=if($a.Actor -ceq 'a'){"U&'\95E8\5ECA\4E3B\4EBA'"}else{"U&'\95E8\5ECA\8BBF\5BA2'"};$accounts+="('$($a.Actor)','$($a.UserId)'::uuid,'$($a.PetId)'::uuid,'$($a.DefaultRoomId)'::uuid,'$($a.SessionId)'::uuid,$nickname)"}
    $writes=@();$operations=@('created','updated','dissolved','restored');$indexes=@(0,2,4,6)
    for($i=0;$i -lt 4;$i++){$writes+="('$($Scope.RoomRequestIds[$indexes[$i]])'::uuid,$($i+1),'$($operations[$i])')"}
    $foreign=@(1,3,5)|ForEach-Object{"'$($Scope.RoomRequestIds[$_])'::uuid"}
    $a=$Scope.Accounts[0];$sample='{"appearanceSchemaVersion":3,"bodyColorHex":"#FFE8C2","eyeColorHex":"#272321","eyeSizePercent":103,"eyeOvalityPercent":0,"eyeHeightControlPercent":51,"eyeSpacingControlPercent":24.5}'
    $sql=@"
-- porch_c1_scope:$identity
BEGIN READ ONLY;
WITH expected(actor,user_id,pet_id,default_room_id,session_id,nickname) AS (VALUES $($accounts -join ',')),
writes(request_id,version,operation) AS (VALUES $($writes -join ','))
SELECT json_build_object(
'completeGraphs',(SELECT count(*) FROM expected e
 JOIN dango.users u ON u.id=e.user_id AND u.email_confirmed AND u.disabled_at IS NULL AND NOT u.recovery_locked
 AND u.email='porchc1-$($Scope.OwnerId.Replace('-',''))-'||e.actor||'@example.invalid' AND u.normalized_email=upper(u.email)
 AND u.nickname=e.nickname AND u.profile_version=2
 JOIN dango.pets p ON p.id=e.pet_id AND p.user_id=u.id AND p.configuration_version=1 AND p.configuration='$sample'::jsonb
 JOIN dango.rooms r ON r.id=e.default_room_id AND r.owner_id=u.id AND r.kind=0 AND r.status=0 AND r.version=1 AND r.capacity=2
 AND r.admission_generation=1 AND r.dissolved_at IS NULL AND r.recover_until IS NULL
 JOIN dango.room_memberships m ON m.room_id=r.id AND m.user_id=u.id AND m.role=0
 JOIN dango.pet_room_states ps ON ps.pet_id=p.id AND ps.room_id=r.id AND ps.version=1 AND ps.control_generation=0
 AND ps.control_lease_id IS NULL AND ps.position_x=0 AND ps.position_y=0 AND NOT ps.is_sleeping
 JOIN dango.pending_registrations pr ON pr.user_id=u.id AND pr.pet_id=p.id AND pr.room_id=r.id AND pr.state=2 AND pr.completed_at IS NOT NULL
 JOIN dango.test_invitations i ON i.id=pr.invitation_id AND i.user_id=u.id AND i.consumed_at IS NOT NULL AND i.revoked_at IS NULL
 JOIN dango.email_challenges c ON c.pending_registration_id=pr.id AND c.purpose=0 AND c.consumed_at IS NOT NULL AND c.revoked_at IS NULL AND c.generation=pr.generation
 JOIN dango.email_jobs j ON j.challenge_id=c.id AND j.state=0 AND j.attempts=0 AND j.accepted_at IS NULL AND j.lease_id IS NULL AND j.lease_until IS NULL AND j.failure_code IS NULL
 JOIN dango.auth_sessions s ON s.id=e.session_id AND s.user_id=u.id AND s.security_stamp=u.security_stamp AND s.revoked_at IS NOT NULL
 AND s.recovery_epoch='$($Scope.RecoveryEpoch)'::uuid
 WHERE pr.normalized_email=u.normalized_email AND i.normalized_email=u.normalized_email AND c.normalized_email=u.normalized_email
 AND pr.recovery_epoch=s.recovery_epoch AND i.recovery_epoch=s.recovery_epoch AND c.recovery_epoch=s.recovery_epoch),
'sessions',(SELECT count(*) FROM dango.auth_sessions WHERE user_id IN (SELECT user_id FROM expected)),
'restoredRooms',(SELECT count(*) FROM dango.rooms WHERE id='$($Scope.RoomId)'::uuid AND owner_id='$($a.UserId)'::uuid
 AND kind=1 AND status=0 AND version=4 AND admission_generation=2 AND name=U&'\95E8\5ECA\8336\5BA4' AND capacity=8
 AND template_id='ordinary-default' AND template_version=1 AND dissolved_at IS NULL AND recover_until IS NULL),
'ownerMemberships',(SELECT count(*) FROM dango.room_memberships WHERE room_id='$($Scope.RoomId)'::uuid AND user_id='$($a.UserId)'::uuid AND role=0),
'allMemberships',(SELECT count(*) FROM dango.room_memberships WHERE room_id='$($Scope.RoomId)'::uuid),
'occupancy',(SELECT count(*) FROM dango.pet_room_states WHERE room_id='$($Scope.RoomId)'::uuid),
'audit',(SELECT count(*) FROM dango.audit_logs x JOIN writes w ON x.request_id=w.request_id AND x.applied_version=w.version AND x.operation=w.operation
 WHERE x.room_id='$($Scope.RoomId)'::uuid AND x.actor_user_id='$($a.UserId)'::uuid AND x.request_hash ~ '^[a-fA-F0-9]{64}$'),
'allAudit',(SELECT count(*) FROM dango.audit_logs WHERE actor_user_id IN (SELECT user_id FROM expected)),
'outbox',(SELECT count(*) FROM dango.domain_outbox o JOIN dango.audit_logs x ON x.id=o.id JOIN writes w ON x.request_id=w.request_id
 WHERE o.room_id='$($Scope.RoomId)'::uuid AND x.room_id=o.room_id AND o.room_version=w.version AND x.applied_version=w.version
 AND o.event_type=w.operation AND x.operation=w.operation AND o.occurred_at=x.occurred_at AND o.published_at IS NULL AND o.target_user_ids=ARRAY['$($a.UserId)'::uuid]),
'allOutbox',(SELECT count(*) FROM dango.domain_outbox WHERE room_id='$($Scope.RoomId)'::uuid),
'foreignReceipts',(SELECT count(*) FROM dango.audit_logs WHERE actor_user_id='$($Scope.Accounts[1].UserId)'::uuid OR request_id IN ($($foreign -join ','))));
COMMIT;
"@
    Assert-DangoAccountSyncSqlTransportContract $sql;return $sql
}

function ConvertFrom-DangoPorchCrudC1SqlIdentity {
    param([string]$Sql)
    $match=[regex]::Match($Sql,'\A-- porch_c1_scope:([A-Za-z0-9+/=]{1,8192})\r?\n')
    if(-not $match.Success){throw 'porch_c1_sql_identity_rejected'}
    try{$bytes=[Convert]::FromBase64String($match.Groups[1].Value)
        $scope=ConvertFrom-DangoPorchCrudC1StrictJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
        Assert-DangoPorchCrudC1ScopeInputs $scope;return $scope
    }catch{throw 'porch_c1_sql_identity_rejected'}
}

function Assert-DangoPorchCrudC1ScopedGraph {
    param($Evidence)
    $expected=@{completeGraphs=2;sessions=2;restoredRooms=1;ownerMemberships=1;allMemberships=1;occupancy=0;
        audit=4;allAudit=4;outbox=4;allOutbox=4;foreignReceipts=0}
    if(-not(Test-DangoPorchCrudC1ObjectShape $Evidence @($expected.Keys))){throw 'porch_c1_scoped_graph_rejected'}
    foreach($key in $expected.Keys){if(-not(Test-DangoAccountSyncInteger $Evidence.$key $expected[$key])){throw 'porch_c1_scoped_graph_rejected'}}
}

function Assert-DangoPorchCrudC1OldGraph {
    param($Before,$After)
    Assert-DangoPorchCrudC1ProtectedGraphShape $Before;Assert-DangoPorchCrudC1ProtectedGraphShape $After
    $delta=@{invitations=2;registrations=2;challenges=2;emailJobs=2;users=2;pets=2;rooms=3;memberships=3;petStates=2;sessions=2;audit=4;outbox=4}
    foreach($key in @($Before.counts.PSObject.Properties.Name)){
        $remaining=[Collections.Generic.List[string]]::new();foreach($hash in $After.digests.$key){$remaining.Add($hash)}
        foreach($hash in $Before.digests.$key){if(-not $remaining.Remove($hash)){throw 'porch_c1_old_graph_changed'}}
        $wanted=if($delta.ContainsKey($key)){$delta[$key]}else{0}
        if($remaining.Count -ne $wanted -or $After.counts.$key-$Before.counts.$key -ne $wanted){throw 'porch_c1_graph_delta_rejected'}
    }
    $new=@($After.userIds|Where-Object{$_ -cnotin @($Before.userIds)})
    if($new.Count -ne 2 -or @($new|Sort-Object -Unique).Count -ne 2){throw 'porch_c1_identity_delta_rejected'}
    return $new
}

function Assert-DangoPorchCrudC1Resources {
    param([Guid]$OwnerId,$Rows)
    # Exactly one desktop lifecycle; no synthetic resource events are accepted.
    $phases=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed')
    if(@($Rows).Count -ne 6){throw 'porch_c1_resource_count_rejected'}
    for($i=0;$i -lt 6;$i++){
        if(-not(Test-DangoPorchCrudC1ObjectShape $Rows[$i] @('Version','OwnerId','Sequence','Phase','Facts')) -or
            -not(Test-DangoAccountSyncInteger $Rows[$i].Version 1) -or -not(Test-DangoAccountSyncInteger $Rows[$i].Sequence ($i+1)) -or
            $Rows[$i].OwnerId -cne $OwnerId.ToString('D') -or $Rows[$i].Phase -cne $phases[$i]){throw 'porch_c1_resource_order_rejected'}
    }
    $desktopBools=@('Disposed','HwndClosed','UiaSettled','PumpsSettled','HttpSettled','TimerStopped','HostTimersStopped','RuntimeStopped',
        'HomeDriverReleased','CoordinatorsStopped','AccountOperationsDrained','RoomOperationsDrained','SecretsDisposed','CleanupPassed')
    $expectedShapes=@(@('Port','CertificateSha256'),@('Index','Port','WorkersDisabled'),@('Index','Attempt'),
        (@('Index')+$desktopBools+@('InternalDisposeTaskObservable')),@('Index','Stopped','Disposed','InFlightRequests','LifecycleTasksSettled','ListenerAbsent'),
        @('NativeKeyIdentityCaptured','NativeCleanupConfirmed','NativeProbeFailed'))
    for($i=0;$i -lt 6;$i++){if(-not(Test-DangoPorchCrudC1ObjectShape $Rows[$i].Facts $expectedShapes[$i])){throw 'porch_c1_resource_shape_rejected'}}
    $tls=$Rows[0].Facts
    if(-not(Test-DangoAccountSyncIntegerType $tls.Port) -or $tls.Port -lt 1 -or $tls.Port -gt 65535 -or $tls.Port -eq 63237 -or
        $tls.CertificateSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or $Rows[1].Facts.Port -ne $tls.Port){throw 'porch_c1_resource_tls_rejected'}
    foreach($i in @(1,2,3,4)){if(-not(Test-DangoAccountSyncInteger $Rows[$i].Facts.Index 1)){throw 'porch_c1_resource_index_rejected'}}
    foreach($key in $desktopBools){if(-not(Test-DangoAccountSyncBoolean $Rows[3].Facts.$key $true)){throw 'porch_c1_desktop_cleanup_rejected'}}
    foreach($entry in @(@(1,'WorkersDisabled',$true),@(2,'Attempt',$true),@(3,'InternalDisposeTaskObservable',$false),
        @(4,'Stopped',$true),@(4,'Disposed',$true),@(4,'LifecycleTasksSettled',$true),@(4,'ListenerAbsent',$true),@(5,'NativeKeyIdentityCaptured',$true),
        @(5,'NativeCleanupConfirmed',$true),@(5,'NativeProbeFailed',$false))){if(-not(Test-DangoAccountSyncBoolean $Rows[$entry[0]].Facts.($entry[1]) $entry[2])){throw 'porch_c1_resource_cleanup_rejected'}}
    if(-not(Test-DangoAccountSyncInteger $Rows[4].Facts.InFlightRequests 0)){throw 'porch_c1_requests_inflight_rejected'}
    [pscustomobject]@{HttpsHosts=1;Certificates=1;DesktopHosts=1;NativeCleanupConfirmed=$true;DesktopCleanupConfirmed=$true}
}

function Assert-DangoPorchCrudC1Journey {
    param($Evidence,$Scope)
    Assert-DangoPorchCrudC1ScopeInputs $Scope
    $fields=@('Version','OwnerId','Passed','Actors','RoomId','DurationSeconds','HttpRequests','ClientsCreated','DesktopTransports',
        'StopwatchFrequency','Requests','Walks','CleanupPassed','UsesSystemTrust','UsesWindowsCredentials','ActualFormalHost','ActualApplicationHost','HealthyNativeFrames')
    if(-not(Test-DangoPorchCrudC1ObjectShape $Evidence $fields) -or $Evidence.OwnerId -cne $Scope.OwnerId -or $Evidence.RoomId -cne $Scope.RoomId -or
        -not(Test-DangoAccountSyncInteger $Evidence.Version 1) -or -not(Test-DangoAccountSyncBoolean $Evidence.Passed $true) -or
        $Evidence.DurationSeconds -isnot [double] -or $Evidence.DurationSeconds -le 0 -or $Evidence.DurationSeconds -ge 300 -or
        $Evidence.Requests -isnot [Array] -or $Evidence.Requests.Count -lt 25 -or $Evidence.Requests.Count -gt 128 -or
        -not(Test-DangoAccountSyncInteger $Evidence.HttpRequests $Evidence.Requests.Count) -or
        $Evidence.ClientsCreated -lt 4 -or $Evidence.ClientsCreated -gt 5 -or $Evidence.DesktopTransports -lt 1 -or $Evidence.DesktopTransports -gt 2 -or
        $Evidence.StopwatchFrequency -le 0){throw 'porch_c1_journey_rejected'}
    foreach($key in @('CleanupPassed','ActualFormalHost','ActualApplicationHost','HealthyNativeFrames')){if(-not(Test-DangoAccountSyncBoolean $Evidence.$key $true)){throw 'porch_c1_journey_rejected'}}
    foreach($key in @('UsesSystemTrust','UsesWindowsCredentials')){if(-not(Test-DangoAccountSyncBoolean $Evidence.$key $false)){throw 'porch_c1_journey_rejected'}}
    if(($Evidence.Actors|ConvertTo-Json -Depth 8 -Compress) -cne ($Scope.Accounts|ConvertTo-Json -Depth 8 -Compress)){throw 'porch_c1_actor_agreement_rejected'}
    if($Evidence.Walks -isnot [Array] -or $Evidence.Walks.Count -ne 3){throw 'porch_c1_walks_rejected'}
    for($i=0;$i -lt 3;$i++){$walk=$Evidence.Walks[$i];$target=@('porch-create','porch-archive',('room-'+$Scope.RoomId.Replace('-','')))[$i]
        if(-not(Test-DangoPorchCrudC1ObjectShape $walk @('Target','StartX','EndX','Key','SameRuntime','NewHealthyFrame')) -or $walk.Target -cne $target -or
            [Math]::Abs($walk.StartX-$walk.EndX) -le 50 -or -not(Test-DangoAccountSyncBoolean $walk.SameRuntime $true) -or
            -not(Test-DangoAccountSyncBoolean $walk.NewHealthyFrame $true)){throw 'porch_c1_walks_rejected'}}
    $sequence=0;$previous=$null
    foreach($row in $Evidence.Requests){$sequence++
        if(-not(Test-DangoPorchCrudC1ObjectShape $row @('Sequence','ClientId','Role','Actor','Method','Path','SessionId','RequestId','ExpectedVersion','SendBefore','SendAfter','Status','RoomVersion','Operation','Terminal')) -or
            -not(Test-DangoAccountSyncInteger $row.Sequence $sequence)){throw 'porch_c1_request_trace_rejected'}
        if($null -ne $row.SendBefore){if($null -eq $row.SendAfter -or $row.SendAfter -lt $row.SendBefore -or
            ($null -ne $previous -and ($row.SendBefore-$previous)/[double]$Evidence.StopwatchFrequency -lt .6)){throw 'porch_c1_pacing_rejected'};$previous=$row.SendAfter}
        elseif($null -ne $row.SendAfter){throw 'porch_c1_request_trace_rejected'}
        if($row.Terminal -ceq 'Canceled'){
            if($row.Method -cne 'GET' -or $row.Path -cne '/api/v1/me/pet' -or $row.Actor -cne 'a' -or $row.SessionId -cne $Scope.Accounts[0].SessionId -or $null -ne $row.Operation){throw 'porch_c1_cancel_rejected'}
        }elseif($row.Terminal -cne 'RanToCompletion' -or $null -eq $row.Status -or $null -eq $row.SendBefore){throw 'porch_c1_request_trace_rejected'}
    }
    $writes=@($Evidence.Requests|Where-Object{$_.Path.StartsWith('/api/v1/rooms',[StringComparison]::Ordinal) -and $_.Method -cne 'GET'})
    if($writes.Count -ne 7 -or @($Evidence.Requests|Where-Object{$null -ne $_.Operation}).Count -ne 17){throw 'porch_c1_room_writes_rejected'}
    for($i=0;$i -lt 7;$i++){$row=$writes[$i];$actor=@('a','b','a','b','a','b','a')[$i];$op=@('Create','Update','Update','Dissolve','Dissolve','Restore','Restore')[$i]
        $version=@(1,$null,2,$null,3,$null,4)[$i];$expected=@($null,1,1,2,2,3,3)[$i];$status=@(201,404,200,404,200,404,200)[$i]
        $session=$Scope.Accounts[$(if($actor -ceq 'a'){0}else{1})].SessionId
        $suffix=switch($op){Dissolve{'/dissolve'};Restore{'/restore'};default{''}}
        $path=if($i -eq 0){'/api/v1/rooms'}else{"/api/v1/rooms/$($Scope.RoomId)$suffix"}
        $method=if($op -ceq 'Update'){'PATCH'}else{'POST'}
        if($row.Actor -cne $actor -or $row.Operation -cne $op -or $row.RequestId -cne $Scope.RoomRequestIds[$i] -or $row.SessionId -cne $session -or
            $row.ExpectedVersion -cne $expected -or $row.Status -cne $status -or $row.RoomVersion -cne $version -or $row.Terminal -cne 'RanToCompletion' -or
            $row.Path -cne $path -or $row.Method -cne $method){throw 'porch_c1_room_writes_rejected'}
        if($actor -ceq 'a'){
            $read=@($Evidence.Requests|Where-Object{$_.Sequence -gt $row.Sequence -and $_.Method -ceq 'GET' -and $_.Actor -ceq 'a' -and $_.Path -ceq "/api/v1/rooms/$($Scope.RoomId)"}|Select-Object -First 1)
            if($read.Count -ne 1 -or $read[0].Status -ne 200 -or $read[0].RoomVersion -ne $version -or $read[0].Terminal -cne 'RanToCompletion'){throw 'porch_c1_readback_rejected'}
        }
    }
    if(@($Evidence.Requests|Where-Object{$_.Actor -ceq 'b' -and $_.Method -ceq 'GET' -and $_.Path -ceq "/api/v1/rooms/$($Scope.RoomId)" -and $_.Status -eq 404}).Count -ne 1 -or
        @($Evidence.Requests|Where-Object{$_.Actor -ceq 'a' -and $_.Method -ceq 'GET' -and $_.Path -ceq "/api/v1/rooms/$($Scope.RoomId)" -and $_.Status -eq 200 -and $_.RoomVersion -eq 4}).Count -ne 2){throw 'porch_c1_readback_rejected'}
}

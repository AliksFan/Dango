Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -DisableNameChecking

function Get-DangoAccountSyncB1cPlan {
    param([string]$Repository)
    Assert-DangoAccountSyncB1cPathText $Repository
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    [pscustomobject]@{Version=1;Stage='AccountSyncB1c1';RunName='run20261008b0';Executed=$false
        Repository=[IO.Path]::GetFullPath($Repository);StateDirectory=$state;DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261011accountsyncb1c1')
        PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin')
        Database='dango_backend_b0';Role='dango_b0_owner';DatabasePort=63237;ExpectedTests=1;MaximumToolProcesses=10
        Filter='FullyQualifiedName=Dango.AccountSyncReview.Tests.AccountSyncB1cJourneyTests.FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount'
        CandidateDirectory='artifacts/account-sync-b1c1-candidate/bin/Dango.AccountSyncReview.Tests/release'
        ApprovalVariable='DANGO_BACKEND_ACCOUNTSYNC_B1C1_APPROVED';TrxFileName='account-sync-b1c1.trx'
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity',
            '20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
        Budgets=[ordered]@{Invite=2;Start=2;Confirm=2;Complete=2;Login=4;Profile=2;Configuration=3;Logout=4}
        MaximumHttpRequests=128;MinimumHttpStartIntervalMilliseconds=600;MaximumDurationMinutes=10;ProcessTimeoutSeconds=300;HttpsHosts=1;Certificates=1;DesktopHosts=2
        ConfigurationSuccessfulWrites=3;FinalAConfigurationVersion=2;FinalBConfigurationVersion=3;FinalAEyeSizePercent=111;FinalBEyeSizePercent=130;FinalProfileVersion=2
        MigrationOperations=0;RoomOperations=0;SystemTrustOperations=0;WindowsCredentialOperations=0;EmailDispatchOperations=0
        RetryAllowed=$false}
}

function Assert-DangoAccountSyncB1cPathText {
    param([string]$Path)
    if([string]::IsNullOrWhiteSpace($Path)){throw 'accountsync_b1c_path_text_rejected'}
    foreach($character in $Path.ToCharArray()){
        if([char]::IsControl($character) -or [char]::GetUnicodeCategory($character) -eq [Globalization.UnicodeCategory]::Format){
            throw 'accountsync_b1c_path_text_rejected'}
    }
}

function Test-DangoAccountSyncB1cString {
    param($Actual,[string]$Expected)
    return $Actual -is [string] -and [string]::Equals($Actual,$Expected,[StringComparison]::Ordinal)
}

function Test-DangoAccountSyncB1cStringIn {
    param($Actual,[string[]]$Expected)
    foreach($value in $Expected){if(Test-DangoAccountSyncB1cString $Actual $value){return $true}}
    return $false
}

function Test-DangoAccountSyncB1cObjectShape {
    param($Value,[string[]]$Fields)
    if($Value -isnot [System.Management.Automation.PSCustomObject] -or @($Value.PSObject.Properties).Count -ne $Fields.Count){return $false}
    foreach($field in $Fields){if(-not(Test-DangoAccountSyncB1cStringIn $field @($Value.PSObject.Properties.Name))){return $false}}
    return $true
}

function Test-DangoAccountSyncB1cFixedValue {
    param($Actual,$Expected)
    if($null -eq $Expected){return $null -eq $Actual}
    if($null -eq $Actual){return $false}
    if($Expected -is [string]){return Test-DangoAccountSyncB1cString $Actual $Expected}
    if($Expected -is [Collections.IDictionary]){
        if($Actual -isnot [Collections.IDictionary] -or $Actual.Count -ne $Expected.Count){return $false}
        foreach($key in $Expected.Keys){
            $actualKey=@($Actual.Keys|Where-Object{[string]::Equals($_,$key,[StringComparison]::Ordinal)})
            if($actualKey.Count -ne 1 -or -not(Test-DangoAccountSyncB1cFixedValue $Actual[$actualKey[0]] $Expected[$key])){return $false}
        };return $true
    }
    if($Expected -is [Array]){
        if($Actual -isnot [Array] -or $Actual.Count -ne $Expected.Count){return $false}
        for($i=0;$i -lt $Expected.Count;$i++){if(-not(Test-DangoAccountSyncB1cFixedValue $Actual[$i] $Expected[$i])){return $false}}
        return $true
    }
    return $Actual.GetType() -eq $Expected.GetType() -and $Actual -ceq $Expected
}

function Assert-DangoAccountSyncB1cFixedPlan {
    param($Plan)
    try {
        $fixed=Get-DangoAccountSyncB1cPlan $Plan.Repository
        if(@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count){throw 'rejected'}
        foreach($property in $fixed.PSObject.Properties){$actual=$Plan.PSObject.Properties[$property.Name]
            if($null -eq $actual -or -not(Test-DangoAccountSyncB1cString $actual.Name $property.Name) -or
                -not(Test-DangoAccountSyncB1cFixedValue $actual.Value $property.Value)){throw 'rejected'}}
        return $fixed
    }catch{throw 'accountsync_b1c_fixed_plan_rejected'}
}

function Get-DangoAccountSyncB1cInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan
    $files=@('eng/Dango.AccountSyncB1cPostgres.psm1','eng/invoke-account-sync-b1c1-tests.ps1','eng/test-account-sync-b1c1-boundary.ps1',
        'eng/Dango.AccountProfilePostgres.psm1','eng/Dango.AccountSyncPostgres.psm1','eng/Dango.AccountSyncClosurePostgres.psm1',
        'docs/planning/2026-10-11-account-sync-b1c1-local-plan.md','Directory.Build.props','global.json',
        'eng/tools/Dango.AccountSyncReview.Tests/Dango.AccountSyncReview.Tests.csproj',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1cTestOwner.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1cAttemptBudget.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1cHttpsFixture.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1cJourneyTests.cs',
        'src/Dango.Client.Sync/Pets/PetConfigurationLiveSync.cs','src/Dango.Client.Sync/Pets/PetConfigurationHttpClient.cs',
        'src/Dango.Client.Sync/Pets/PetConfigurationFileJournal.cs','src/Dango.Client.Sync/Pets/PetConfigurationChoice.cs')
    $files+=@('tests/Dango.Server.Tests/Dango.Server.Tests.csproj',
        'tests/Dango.Server.Tests/Configuration/AccountSyncScopedSqlMetadataTests.cs',
        'tests/Dango.Server.Tests/Configuration/AccountSyncB1cScopedSqlMetadataTests.cs',
        'artifacts/main-absorption/9db6fd423e364e488967e0696ecc05d1/native/Release/dango_physics_module.dll')
    foreach($relativeRoot in @('src','eng/tools/Dango.AccountSyncReview.Tests')){
        $root=Join-Path $fixed.Repository $relativeRoot;Assert-DangoProfileRegularPath $root
        if([IO.Directory]::Exists($root)){
            foreach($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)){
                $relative=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
                if($relative -match '/(?:obj|bin)/'){continue}
                if(Test-DangoAccountSyncB1cStringIn ($file.Extension.ToLowerInvariant()) @('.cs','.csproj','.props','.targets')){
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
    if($files.Count -gt 1024){throw 'accountsync_b1c_inputs_bound_rejected'}
    foreach($relative in $files){$path=Join-Path $fixed.Repository $relative;Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+=$relative;continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    $native='artifacts/main-absorption/9db6fd423e364e488967e0696ecc05d1/native/Release/dango_physics_module.dll'
    if($hashes.Contains($native) -and -not(Test-DangoAccountSyncB1cString $hashes[$native] 'B77206757264A56E9022DF6D65AE4A85E0651A3911370B6D3FDF430DD064351E')){
        throw 'accountsync_b1c_native_digest_rejected'}
    $tools=Get-DangoAccountSyncB1cBinaryInputs $fixed;$missing+=@($tools.MissingInputs)
    [pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;Plan=$fixed;Hashes=$hashes;
        PostgresTools=$tools.Hashes;MissingInputs=$missing}
}

function Get-DangoAccountSyncB1cBinaryInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan;$hashes=[ordered]@{};$missing=@()
    foreach($name in @('postgres.exe','pg_ctl.exe','psql.exe')){$path=Join-Path $fixed.PostgresBinDirectory $name
        Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+='postgres-bin/'+$name;continue}
        $hashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    [pscustomobject]@{Hashes=$hashes;MissingInputs=$missing}
}

function New-DangoAccountSyncB1cApproval {
    param($Plan,[string]$Path)
    Assert-DangoAccountSyncB1cPathText $Path
    $physical=Assert-DangoProfileManifestPath $Plan $Path;$inputs=Get-DangoAccountSyncB1cInputs $Plan
    if($inputs.MissingInputs.Count -ne 0){throw 'accountsync_b1c_inputs_missing'}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    [pscustomobject]@{Path=$physical;Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash;Inputs=$inputs}
}

function Assert-DangoAccountSyncB1cApproval {
    param($Plan,[string]$Path,[string]$Digest)
    $document=$null
    try{
        $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan
        Assert-DangoAccountSyncB1cPathText $Path
        if($Digest -cnotmatch '\A[A-F0-9]{64}\z'){throw 'accountsync_b1c_approval_digest_required'}
        $physical=Assert-DangoProfileManifestPath $fixed $Path;$bytes=Read-DangoAccountSyncClosureBytes $physical 262144
        if(-not(Test-DangoAccountSyncB1cString ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) $Digest)){
            throw 'accountsync_b1c_approval_changed'}
        $document=[Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
        $current=Get-DangoAccountSyncB1cInputs $fixed
        if($current.MissingInputs.Count -ne 0){throw 'accountsync_b1c_inputs_missing'}
        Assert-DangoAccountSyncB1cJsonValue $document.RootElement $current
        return [pscustomobject]@{Path=$physical;Sha256=$Digest;Inputs=$current}
    }catch{
        if($_.Exception.Message -cmatch '^(accountsync_b1c|accountsync_closure|profile_run)_[a-z0-9_]+$'){throw}
        throw 'accountsync_b1c_approval_invalid'
    }finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoAccountSyncB1cJsonValue {
    param([Text.Json.JsonElement]$Element,$Expected)
    if($Expected -is [bool]){
        if($Element.ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False) -or
            $Element.GetBoolean() -ne $Expected){throw 'accountsync_b1c_json_value_rejected'}
    }elseif($Expected -is [int] -or $Expected -is [long]){
        $number=[long]0
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $Element.TryGetInt64([ref]$number) -or
            $number -ne $Expected){throw 'accountsync_b1c_json_value_rejected'}
    }elseif($Expected -is [string]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            -not(Test-DangoAccountSyncB1cString $Element.GetString() $Expected)){throw 'accountsync_b1c_json_value_rejected'}
    }elseif($Expected -is [Array]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $Element.GetArrayLength() -ne $Expected.Count){
            throw 'accountsync_b1c_json_value_rejected'}
        for($i=0;$i -lt $Expected.Count;$i++){Assert-DangoAccountSyncB1cJsonValue $Element[$i] $Expected[$i]}
    }else{
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object){throw 'accountsync_b1c_json_value_rejected'}
        $expectedMap=[ordered]@{}
        if($Expected -is [Collections.IDictionary]){foreach($key in $Expected.Keys){$expectedMap[$key]=$Expected[$key]}}
        elseif($Expected -is [System.Management.Automation.PSCustomObject]){
            foreach($property in $Expected.PSObject.Properties){$expectedMap[$property.Name]=$property.Value}
        }else{throw 'accountsync_b1c_json_value_rejected'}
        $properties=@($Element.EnumerateObject());$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if($properties.Count -ne $expectedMap.Count){throw 'accountsync_b1c_json_value_rejected'}
        foreach($property in $properties){
            if(-not $seen.Add($property.Name) -or -not(Test-DangoAccountSyncB1cStringIn $property.Name @($expectedMap.Keys))){
                throw 'accountsync_b1c_json_value_rejected'}
            Assert-DangoAccountSyncB1cJsonValue $property.Value $expectedMap[$property.Name]
        }
    }
}

function New-DangoAccountSyncB1cReservation {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan;$parent=Split-Path -Parent $fixed.EvidenceDirectory
    Assert-DangoProfileRegularPath $parent;Assert-DangoProfileRegularPath $fixed.EvidenceDirectory
    if(-not [IO.Directory]::Exists($parent)){throw 'accountsync_b1c_existing_parent_required'}
    Get-DangoProfileNativeType
    if(-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)){throw 'accountsync_b1c_already_reserved'}
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
    foreach($name in @('attempts.jsonl','resources.jsonl','logout-intents.jsonl')){$stream=[IO.FileStream]::new((Join-Path $fixed.EvidenceDirectory $name),'CreateNew','Write','Read',4096,[IO.FileOptions]::WriteThrough)
        try{$stream.Flush($true)}finally{$stream.Dispose()}}
    [pscustomobject]@{OwnerId=$owner;Owner=$manifest;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') -Algorithm SHA256).Hash}
}

function Get-DangoAccountSyncB1cPsqlArguments {
    param($Plan,[string]$Sql)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan
    [string[]]@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$fixed.DatabasePort,
        '-U',$fixed.Role,'-d',$fixed.Database,'-c',$Sql)
}

function Assert-DangoAccountSyncB1cEvidenceContract {
    param([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,$Plan,[int]$Port,[hashtable]$EnvironmentValues,
        [bool]$KillOwnedTree,[string]$OutputContract)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan;$accepted=$false
    try{Assert-DangoAccountSyncB1cPathText $Executable;Assert-DangoAccountSyncB1cPathText $WorkingDirectory}
    catch{throw 'accountsync_b1c_evidence_contract_rejected'}
    if($Arguments.Count -ne 17){throw 'accountsync_b1c_evidence_contract_rejected'}
    try{Assert-DangoAccountSyncSqlTransportContract $Arguments[16]}catch{throw 'accountsync_b1c_evidence_contract_rejected'}
    if(Test-DangoAccountSyncB1cString $OutputContract 'AccountSyncB1cEvidenceJson'){
        $accepted=Test-DangoAccountSyncB1cFixedValue $Arguments (Get-DangoAccountSyncB1cPsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql))
    }elseif(Test-DangoAccountSyncB1cString $OutputContract 'AccountSyncB1cSmallJson'){
        $accepted=Test-DangoAccountSyncB1cFixedValue $Arguments (Get-DangoAccountSyncB1cPsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql -MeasureOnly))
        if(-not $accepted -and $Arguments.Count -eq 17){
            $scope=ConvertFrom-DangoAccountSyncB1cSqlIdentity $Arguments[16]
            $accepted=Test-DangoAccountSyncB1cFixedValue $Arguments (Get-DangoAccountSyncB1cPsqlArguments $fixed (Get-DangoAccountSyncB1cScopedSql $scope))
        }
    }
    $decoded=$null
    try{
        if(-not $accepted -or $Port -ne 63237 -or $KillOwnedTree -or $EnvironmentValues.Count -ne 2 -or
            -not(Test-DangoAccountSyncB1cStringIn 'PGPASSWORD' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoAccountSyncB1cStringIn 'PGCLIENTENCODING' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoAccountSyncB1cString $EnvironmentValues.PGCLIENTENCODING 'UTF8') -or
            $EnvironmentValues.PGPASSWORD -isnot [string] -or $EnvironmentValues.PGPASSWORD -cnotmatch '\A[A-Za-z0-9+/]{43}=\z' -or
            -not(Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'psql.exe')) -or
            -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository)){throw 'rejected'}
        $decoded=[Convert]::FromBase64String($EnvironmentValues.PGPASSWORD)
        if($decoded.Length -ne 32 -or -not(Test-DangoAccountSyncB1cString ([Convert]::ToBase64String($decoded)) $EnvironmentValues.PGPASSWORD)){throw 'rejected'}
    }catch{throw 'accountsync_b1c_evidence_contract_rejected'}
    finally{if($null -ne $decoded){[Security.Cryptography.CryptographicOperations]::ZeroMemory($decoded)}}
}

function Save-DangoAccountSyncB1cRawEvidence {
    param($Plan,[string]$FileName,[string]$Text,[int]$MaximumBytes=131072)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan
    if($FileName -cnotmatch '\A(before|after)(-size)?\.(json|stderr\.txt)\z' -and
        -not(Test-DangoAccountSyncB1cStringIn $FileName @('scoped-graph-evidence.json','scoped.stderr.txt'))){throw 'accountsync_b1c_evidence_path_rejected'}
    if($MaximumBytes -ne 131072 -and ($MaximumBytes -ne 1048576 -or
        -not(Test-DangoAccountSyncB1cStringIn $FileName @('before.json','after.json')))){throw 'accountsync_b1c_evidence_bound_rejected'}
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($Text)
    if($Text.Length -gt $MaximumBytes -or $bytes.Length -gt $MaximumBytes){throw 'accountsync_b1c_evidence_bound_rejected'}
    $path=Join-Path $fixed.EvidenceDirectory $FileName;Assert-DangoProfileRegularPath $path
    $stream=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
}

function ConvertFrom-DangoAccountSyncB1cStrictJson {
    param([string]$Json,[int]$MaximumBytes=131072)
    $document=$null
    try{
        if([string]::IsNullOrWhiteSpace($Json) -or $Json.Length -gt $MaximumBytes -or [Text.Encoding]::UTF8.GetByteCount($Json) -gt $MaximumBytes){throw 'rejected'}
        $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=32
        $document=[Text.Json.JsonDocument]::Parse($Json,$options)
        $value=$Json|ConvertFrom-Json -Depth 32
        if($value -isnot [System.Management.Automation.PSCustomObject]){throw 'rejected'}
        Assert-DangoAccountSyncB1cJsonKeys $document.RootElement
        return $value
    }catch{throw 'accountsync_b1c_json_rejected'}
    finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoAccountSyncB1cJsonKeys {
    param([Text.Json.JsonElement]$Element)
    if($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object){
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($property in $Element.EnumerateObject()){
            if(-not $seen.Add($property.Name)){throw 'accountsync_b1c_duplicate_json_rejected'}
            Assert-DangoAccountSyncB1cJsonKeys $property.Value
        }
    }elseif($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array){
        foreach($child in $Element.EnumerateArray()){Assert-DangoAccountSyncB1cJsonKeys $child}
    }
}

function Assert-DangoAccountSyncB1cProtectedGraphShape {
    param($Evidence)
    Assert-DangoProfileProtectedGraphShape $Evidence
    $keys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
        'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence.counts $keys) -or
        -not(Test-DangoAccountSyncB1cObjectShape $Evidence.digests $keys)){throw 'accountsync_b1c_graph_shape_rejected'}
    foreach($key in $keys){foreach($digest in $Evidence.digests.$key){
        if($digest -isnot [string] -or $digest -cnotmatch '\A[a-f0-9]{64}\z'){throw 'accountsync_b1c_graph_shape_rejected'}}}
}

function Assert-DangoAccountSyncB1cTarget {
    param($Plan,$Evidence)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan;Assert-DangoAccountSyncB1cProtectedGraphShape $Evidence
    Assert-DangoAccountSyncB1cPathText $Evidence.dataDirectory
    $fields=@('database','role','version','dataDirectory','listenAddresses','migrations','userIds','profiles','counts','digests')
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence $fields) -or -not(Test-DangoAccountSyncB1cString $Evidence.database $fixed.Database) -or
        -not(Test-DangoAccountSyncB1cString $Evidence.role $fixed.Role) -or -not(Test-DangoAccountSyncIntegerType $Evidence.version) -or
        $Evidence.version -lt 180000 -or $Evidence.version -ge 190000 -or $Evidence.listenAddresses -isnot [string] -or
        -not(Test-DangoAccountSyncB1cString $Evidence.listenAddresses '127.0.0.1') -or $Evidence.dataDirectory -isnot [string] -or
        -not(Test-DangoProfilePath $Evidence.dataDirectory $fixed.DataDirectory) -or $Evidence.migrations -isnot [Array] -or
        -not(Test-DangoAccountSyncB1cFixedValue $Evidence.migrations $fixed.PriorMigrations) -or $Evidence.userIds -isnot [Array] -or
        $Evidence.profiles -isnot [Array] -or $Evidence.userIds.Count -ne $Evidence.counts.users -or
        $Evidence.profiles.Count -ne $Evidence.counts.users){throw 'accountsync_b1c_target_rejected'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($id in $Evidence.userIds){$parsed=[Guid]::Empty
        if($id -isnot [string] -or -not [Guid]::TryParseExact($id,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
            -not(Test-DangoAccountSyncB1cString $id $parsed.ToString('D')) -or
            -not $seen.Add($id)){throw 'accountsync_b1c_target_rejected'}
        $profile=@($Evidence.profiles|Where-Object {Test-DangoAccountSyncB1cString $_.id $id})
        if($profile.Count -ne 1 -or -not(Test-DangoAccountSyncB1cObjectShape $profile[0] @('id','nicknameIsNull','version')) -or
            $profile[0].nicknameIsNull -isnot [bool] -or -not(Test-DangoAccountSyncIntegerType $profile[0].version) -or
            $profile[0].version -lt 1){throw 'accountsync_b1c_target_rejected'}
    }
}

function Assert-DangoAccountSyncB1cState {
    param($Plan,$State,$EpochState)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan
    Assert-DangoAccountSyncB1cPathText $State.DataDirectory
    if(-not(Test-DangoAccountSyncB1cObjectShape $State @('RunName','Database','Owner','DataDirectory','Port','Version')) -or
        -not(Test-DangoAccountSyncB1cObjectShape $EpochState @('Version','RecoveryEpoch')) -or
        -not(Test-DangoAccountSyncInteger $State.Version 1) -or -not(Test-DangoAccountSyncInteger $State.Port 63237) -or
        -not(Test-DangoAccountSyncInteger $EpochState.Version 1) -or -not(Test-DangoAccountSyncB1cString $State.RunName $fixed.RunName) -or
        -not(Test-DangoAccountSyncB1cString $State.Database $fixed.Database) -or -not(Test-DangoAccountSyncB1cString $State.Owner $fixed.Role) -or
        $State.DataDirectory -isnot [string] -or -not(Test-DangoProfilePath $State.DataDirectory $fixed.DataDirectory)){
        throw 'accountsync_b1c_state_rejected'}
    $epoch=[Guid]::Empty
    if($EpochState.RecoveryEpoch -isnot [string] -or -not [Guid]::TryParseExact($EpochState.RecoveryEpoch,'D',[ref]$epoch) -or
        -not(Test-DangoAccountSyncB1cString $EpochState.RecoveryEpoch $epoch.ToString('D')) -or
        $epoch -eq [Guid]::Empty){throw 'accountsync_b1c_state_rejected'}
    [pscustomobject]@{Port=63237;Epoch=$epoch}
}

function Assert-DangoAccountSyncB1cTrx {
    param([string]$Path)
    Assert-DangoAccountSyncB1cPathText $Path
    Assert-DangoProfileRegularPath $Path
    if(-not(Test-DangoAccountSyncB1cString ([IO.Path]::GetFileName($Path)) 'account-sync-b1c1.trx')){throw 'accountsync_b1c_trx_path_rejected'}
    $file=[IO.FileInfo]::new($Path);if(-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 1048576){throw 'accountsync_b1c_trx_rejected'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing='Prohibit';$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings);$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    try{$xml.Load($reader)}finally{$reader.Dispose()}
    $results=@($xml.SelectNodes("//*[local-name()='UnitTestResult']"));$c=$xml.SelectSingleNode("//*[local-name()='Counters']")
    if($results.Count -ne 1 -or -not(Test-DangoAccountSyncB1cString $results[0].outcome 'Passed') -or
        -not(Test-DangoAccountSyncB1cString $results[0].testName 'FullDesktopLateGetAndCommittedPatchCannotContaminateTheNextAccount') -or $null -eq $c -or
        -not(Test-DangoAccountSyncB1cString $c.total '1') -or -not(Test-DangoAccountSyncB1cString $c.executed '1') -or
        -not(Test-DangoAccountSyncB1cString $c.passed '1') -or -not(Test-DangoAccountSyncB1cString $c.failed '0') -or
        -not(Test-DangoAccountSyncB1cString $c.notExecuted '0')){
        throw 'accountsync_b1c_trx_rejected'}
}

function Assert-DangoAccountSyncB1cAttempts {
    param($Plan,[Guid]$OwnerId,$Rows)
    $fixed=Assert-DangoAccountSyncB1cFixedPlan $Plan;$used=@{};$sequence=0
    if(@($Rows).Count -ne 21){throw 'accountsync_b1c_attempt_count_rejected'}
    foreach($row in $Rows){$sequence++;$parsed=[Guid]::Empty
        if(-not(Test-DangoAccountSyncB1cObjectShape $row @('Version','OwnerId','Sequence','Operation','Used','AtUtc')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence $sequence) -or
            -not(Test-DangoAccountSyncB1cString $row.OwnerId $OwnerId.ToString('D')) -or
            -not(Test-DangoAccountSyncB1cStringIn $row.Operation @($fixed.Budgets.Keys)) -or
            -not(Test-DangoAccountSyncTimestamp $row.AtUtc)){throw 'accountsync_b1c_attempt_identity_rejected'}
        $count=if($used.ContainsKey($row.Operation)){$used[$row.Operation]+1}else{1}
        if($count -gt $fixed.Budgets[$row.Operation] -or -not(Test-DangoAccountSyncInteger $row.Used $count)){
            throw 'accountsync_b1c_attempt_budget_rejected'}
        $used[$row.Operation]=$count
    }
    foreach($key in $fixed.Budgets.Keys){if($used[$key] -ne $fixed.Budgets[$key]){throw 'accountsync_b1c_attempt_budget_rejected'}}
    [pscustomobject]@{Total=21;Operations=$used}
}

# These are fixed product-contract samples, not values copied from an observed response.
function Get-DangoAccountSyncB1cConfiguration {
    param([ValidateSet('a','b')][string]$Actor,[switch]$Initial)
    [pscustomobject][ordered]@{appearanceSchemaVersion=3;bodyColorHex=$(if($Actor -ceq 'a'){'#FFE8C2'}else{'#9ECBEF'});
        eyeColorHex='#272321';eyeSizePercent=$(if($Initial){103}else{if($Actor -ceq 'a'){111}else{130}});
        eyeOvalityPercent=0;eyeHeightControlPercent=51;eyeSpacingControlPercent=24.5}
}

function Assert-DangoAccountSyncB1cConfiguration {
    param($Value,[string]$Actor)
    $expected=Get-DangoAccountSyncB1cConfiguration $Actor
    if(-not(Test-DangoAccountSyncB1cObjectShape $Value @($expected.PSObject.Properties.Name))){throw 'accountsync_b1c_configuration_shape_rejected'}
    foreach($property in $expected.PSObject.Properties){$actual=$Value.($property.Name)
        if($property.Value -is [string]){if(-not(Test-DangoAccountSyncB1cString $actual $property.Value)){throw 'accountsync_b1c_configuration_rejected'}}
        elseif($actual -isnot [int] -and $actual -isnot [long] -and $actual -isnot [double] -and $actual -isnot [decimal]){throw 'accountsync_b1c_configuration_rejected'}
        elseif([double]::IsNaN([double]$actual) -or [double]::IsInfinity([double]$actual) -or $actual -ne $property.Value){throw 'accountsync_b1c_configuration_rejected'}
    }
    if(-not(Test-DangoAccountSyncInteger $Value.appearanceSchemaVersion 3)){throw 'accountsync_b1c_configuration_rejected'}
}

function ConvertTo-DangoAccountSyncB1cGuid {
    param($Value)
    $parsed=[Guid]::Empty
    if($Value -isnot [string] -or -not [Guid]::TryParseExact($Value,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
        -not(Test-DangoAccountSyncB1cString $Value $parsed.ToString('D'))){throw 'accountsync_b1c_identity_rejected'}
    return $parsed
}

function Assert-DangoAccountSyncB1cScopeInputs {
    param($Evidence,[Guid]$OwnerId=[Guid]::Empty,[Guid]$Epoch=[Guid]::Empty,[string[]]$NewUserIds=@())
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence @('Version','OwnerId','RecoveryEpoch','Accounts')) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Version 1) -or $Evidence.Accounts -isnot [Array] -or $Evidence.Accounts.Count -ne 2){throw 'accountsync_b1c_scope_shape_rejected'}
    $owner=ConvertTo-DangoAccountSyncB1cGuid $Evidence.OwnerId;$epochValue=ConvertTo-DangoAccountSyncB1cGuid $Evidence.RecoveryEpoch
    if(($OwnerId -ne [Guid]::Empty -and $owner -ne $OwnerId) -or ($Epoch -ne [Guid]::Empty -and $epochValue -ne $Epoch)){throw 'accountsync_b1c_scope_identity_rejected'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for($i=0;$i -lt 2;$i++){$account=$Evidence.Accounts[$i];$actor=@('a','b')[$i]
        if(-not(Test-DangoAccountSyncB1cObjectShape $account @('Actor','UserId','PetId','DefaultRoomId','SessionIds','Nickname','ProfileVersion','ConfigurationVersion','Configuration')) -or
            -not(Test-DangoAccountSyncB1cString $account.Actor $actor) -or -not(Test-DangoAccountSyncInteger $account.ProfileVersion 2) -or
            -not(Test-DangoAccountSyncInteger $account.ConfigurationVersion ($i+2)) -or
            -not(Test-DangoAccountSyncB1cString $account.Nickname @('旧账号团子','新账号团子')[$i]) -or
            $account.SessionIds -isnot [Array] -or $account.SessionIds.Count -ne 2){throw 'accountsync_b1c_scope_account_rejected'}
        foreach($value in @($account.UserId,$account.PetId,$account.DefaultRoomId)+@($account.SessionIds)){
            [void](ConvertTo-DangoAccountSyncB1cGuid $value);if(-not $seen.Add($value)){throw 'accountsync_b1c_scope_duplicate_identity_rejected'}}
        if([string]::CompareOrdinal($account.SessionIds[0],$account.SessionIds[1]) -ge 0){throw 'accountsync_b1c_scope_session_order_rejected'}
        Assert-DangoAccountSyncB1cConfiguration $account.Configuration $actor
    }
    if($NewUserIds.Count -gt 0 -and ($NewUserIds.Count -ne 2 -or
        @($NewUserIds|Where-Object {$_ -cnotin @($Evidence.Accounts.UserId)}).Count -ne 0 -or
        @($NewUserIds|Sort-Object -Unique).Count -ne 2)){throw 'accountsync_b1c_scope_new_users_rejected'}
}

function Get-DangoAccountSyncB1cScopedSql {
    param($Scope)
    Assert-DangoAccountSyncB1cScopeInputs $Scope
    $owner=[Guid]$Scope.OwnerId;$epoch=$Scope.RecoveryEpoch;$rows=@()
    foreach($account in $Scope.Accounts){$actor=$account.Actor
        $configuration=Get-DangoAccountSyncB1cConfiguration $actor|ConvertTo-Json -Compress
        $nickname=if($actor -ceq 'a'){"U&'\65E7\8D26\53F7\56E2\5B50'"}else{"U&'\65B0\8D26\53F7\56E2\5B50'"}
        $rows+="('$actor','$($account.UserId)'::uuid,'$($account.PetId)'::uuid,'$($account.DefaultRoomId)'::uuid,'$($account.SessionIds[0])'::uuid,'$($account.SessionIds[1])'::uuid,'accountsyncb1c-$($owner.ToString('N'))-$actor@example.invalid',$nickname,2,$($account.ConfigurationVersion),'$configuration'::jsonb)"
    }
    $sql=@"
BEGIN READ ONLY;
WITH run(owner_id,epoch) AS (VALUES ('$($owner.ToString('D'))'::uuid,'$epoch'::uuid)),
expected(actor,user_id,pet_id,room_id,session_one,session_two,email,nickname,profile_version,configuration_version,configuration) AS (VALUES $($rows -join ',')),
facts AS (SELECT e.actor,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id) AS users,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id AND u.email=e.email AND u.normalized_email=upper(e.email) AND u.email_confirmed AND u.disabled_at IS NULL AND NOT u.recovery_locked) AS valid_users,
 (SELECT count(*) FROM dango.users u WHERE u.id=e.user_id AND u.nickname=e.nickname AND u.profile_version=e.profile_version) AS named_users,
 (SELECT count(*) FROM dango.users u
 JOIN dango.pets p ON p.user_id=u.id AND p.id=e.pet_id AND p.configuration_version=e.configuration_version AND p.configuration=e.configuration
 JOIN dango.rooms r ON r.owner_id=u.id AND r.id=e.room_id AND r.kind=0 AND r.status=0 AND r.version=1 AND r.capacity=2 AND r.template_id='desktop-pet-default' AND r.template_version=1 AND r.admission_generation=1
 JOIN dango.room_memberships m ON m.room_id=r.id AND m.user_id=u.id AND m.role=0
 JOIN dango.pet_room_states s ON s.pet_id=p.id AND s.room_id=r.id AND s.version=1 AND s.control_generation=0 AND s.control_lease_id IS NULL AND s.position_x=0 AND s.position_y=0 AND NOT s.is_sleeping
 JOIN dango.pending_registrations pr ON pr.user_id=u.id AND pr.pet_id=p.id AND pr.room_id=r.id AND pr.state=2 AND pr.completed_at IS NOT NULL
 JOIN dango.test_invitations i ON i.id=pr.invitation_id AND i.user_id=u.id AND i.consumed_at IS NOT NULL AND i.revoked_at IS NULL
 JOIN dango.email_challenges c ON c.pending_registration_id=pr.id AND c.purpose=0 AND c.consumed_at IS NOT NULL AND c.revoked_at IS NULL AND c.generation=pr.generation
 JOIN dango.email_jobs j ON j.challenge_id=c.id
 WHERE u.id=e.user_id AND pr.normalized_email=u.normalized_email AND i.normalized_email=u.normalized_email AND c.normalized_email=u.normalized_email
 AND pr.recovery_epoch=run.epoch AND i.recovery_epoch=run.epoch AND c.recovery_epoch=run.epoch) AS complete_graphs,
 (SELECT count(*) FROM dango.email_jobs j JOIN dango.email_challenges c ON c.id=j.challenge_id JOIN dango.pending_registrations pr ON pr.id=c.pending_registration_id
 WHERE pr.user_id=e.user_id AND pr.pet_id=e.pet_id AND pr.room_id=e.room_id AND pr.recovery_epoch=run.epoch
 AND j.state=0 AND j.attempts=0 AND j.accepted_at IS NULL AND j.lease_id IS NULL AND j.lease_until IS NULL AND j.failure_code IS NULL) AS pending_unsent_emails,
 (SELECT count(*) FROM dango.auth_sessions s WHERE s.user_id=e.user_id) AS sessions,
 (SELECT count(*) FROM dango.auth_sessions s JOIN dango.users u ON u.id=s.user_id WHERE s.user_id=e.user_id AND s.security_stamp=u.security_stamp AND s.recovery_epoch=run.epoch) AS valid_sessions,
 (SELECT count(*) FROM dango.auth_sessions s WHERE s.user_id=e.user_id AND s.revoked_at IS NOT NULL) AS revoked_sessions,
 (SELECT count(*) FROM dango.auth_sessions s JOIN dango.users u ON u.id=s.user_id WHERE s.user_id=e.user_id AND s.id IN (e.session_one,e.session_two)
 AND s.security_stamp=u.security_stamp AND s.recovery_epoch=run.epoch AND s.revoked_at IS NOT NULL) AS exact_sessions
 FROM expected e CROSS JOIN run)
SELECT json_object_agg(actor,json_build_object('users',users,'validUsers',valid_users,'namedUsers',named_users,'completeGraphs',complete_graphs,
 'pendingUnsentEmails',pending_unsent_emails,'sessions',sessions,'validSessions',valid_sessions,'revokedSessions',revoked_sessions,'exactSessions',exact_sessions)) FROM facts;
COMMIT;
"@
    Assert-DangoAccountSyncSqlTransportContract $sql;return $sql
}

# Reconstruct from the fixed VALUES identities, then compare the complete SQL/argv.
# It never authorizes an arbitrary SELECT under a known stage label.
function ConvertFrom-DangoAccountSyncB1cSqlIdentity {
    param([string]$Sql)
    $id="[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
    $run=[regex]::Match($Sql,"WITH run\(owner_id,epoch\) AS \(VALUES \('($id)'::uuid,'($id)'::uuid\)\),")
    if(-not $run.Success){throw 'accountsync_b1c_sql_identity_rejected'}
    $accounts=@()
    foreach($actor in @('a','b')){$match=[regex]::Match($Sql,"\('$actor','($id)'::uuid,'($id)'::uuid,'($id)'::uuid,'($id)'::uuid,'($id)'::uuid,")
        if(-not $match.Success){throw 'accountsync_b1c_sql_identity_rejected'}
        $accounts+=[pscustomobject]@{Actor=$actor;UserId=$match.Groups[1].Value;PetId=$match.Groups[2].Value;DefaultRoomId=$match.Groups[3].Value;
            SessionIds=@($match.Groups[4].Value,$match.Groups[5].Value);Nickname=$(if($actor -ceq 'a'){'旧账号团子'}else{'新账号团子'});
            ProfileVersion=2;ConfigurationVersion=$(if($actor -ceq 'a'){2}else{3});Configuration=(Get-DangoAccountSyncB1cConfiguration $actor)}
    }
    [pscustomobject]@{Version=1;OwnerId=$run.Groups[1].Value;RecoveryEpoch=$run.Groups[2].Value;Accounts=$accounts}
}

function Assert-DangoAccountSyncB1cScopedGraph {
    param($Evidence)
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence @('a','b'))){throw 'accountsync_b1c_scoped_shape_rejected'}
    $expected=@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=2;validSessions=2;revokedSessions=2;exactSessions=2}
    foreach($actor in @('a','b')){if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence.$actor @($expected.Keys))){throw 'accountsync_b1c_scoped_shape_rejected'}
        foreach($key in $expected.Keys){if(-not(Test-DangoAccountSyncInteger $Evidence.$actor.$key $expected[$key])){throw 'accountsync_b1c_scoped_graph_rejected'}}}
}

function Assert-DangoAccountSyncB1cOldGraph {
    param($Before,$After)
    Assert-DangoAccountSyncB1cProtectedGraphShape $Before;Assert-DangoAccountSyncB1cProtectedGraphShape $After;Assert-DangoProfileOldGraph $Before $After
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')){
        if($After.counts.$key-$Before.counts.$key -ne 2){throw 'accountsync_b1c_graph_delta_rejected'}}
    if($After.counts.sessions-$Before.counts.sessions -ne 4){throw 'accountsync_b1c_graph_delta_rejected'}
    $new=@($After.userIds|Where-Object {$_ -cnotin @($Before.userIds)})
    if($new.Count -ne 2 -or @($new|Sort-Object -Unique).Count -ne 2){throw 'accountsync_b1c_identity_delta_rejected'}
    return $new
}

function Assert-DangoAccountSyncB1cResources {
    param([Guid]$OwnerId,$Rows)
    $phases=@('TLSCreated','HostStarted','DesktopStarted','DesktopDisposed','DesktopStarted','DesktopDisposed','HostDisposed','TLSDisposed')
    if(@($Rows).Count -ne 8){throw 'accountsync_b1c_resource_count_rejected'}
    $desktopBools=@('Disposed','HwndClosed','UiaSettled','PumpsSettled','HttpSettled','TimerStopped','HostTimersStopped','RuntimeStopped',
        'HomeDriverReleased','CoordinatorsStopped','AccountOperationsDrained','SecretsDisposed','CleanupPassed')
    for($i=0;$i -lt 8;$i++){$row=$Rows[$i]
        if(-not(Test-DangoAccountSyncB1cObjectShape $row @('Version','OwnerId','Sequence','Phase','Facts')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence ($i+1)) -or
            -not(Test-DangoAccountSyncB1cString $row.OwnerId $OwnerId.ToString('D')) -or
            -not(Test-DangoAccountSyncB1cString $row.Phase $phases[$i])){throw 'accountsync_b1c_resource_order_rejected'}
        $fields=switch -CaseSensitive ($row.Phase){TLSCreated{@('Port','CertificateSha256')};HostStarted{@('Index','Port','WorkersDisabled')};
            DesktopStarted{@('Index','Attempt')};DesktopDisposed{@('Index')+$desktopBools+@('InternalDisposeTaskObservable')};
            HostDisposed{@('Index','Stopped','Disposed','InFlightRequests','LifecycleTasksSettled')};TLSDisposed{@('NativeKeyIdentityCaptured','NativeCleanupConfirmed','NativeProbeFailed')}}
        if(-not(Test-DangoAccountSyncB1cObjectShape $row.Facts $fields)){throw 'accountsync_b1c_resource_shape_rejected'}
    }
    $tls=$Rows[0].Facts
    if(-not(Test-DangoAccountSyncIntegerType $tls.Port) -or $tls.Port -lt 1 -or $tls.Port -gt 65535 -or
        $tls.CertificateSha256 -isnot [string] -or $tls.CertificateSha256 -cnotmatch '\A[A-F0-9]{64}\z'){throw 'accountsync_b1c_tls_resource_rejected'}
    $start=$Rows[1].Facts;$stop=$Rows[6].Facts
    if(-not(Test-DangoAccountSyncInteger $start.Index 1) -or -not(Test-DangoAccountSyncInteger $start.Port $tls.Port) -or
        -not(Test-DangoAccountSyncBoolean $start.WorkersDisabled $true) -or -not(Test-DangoAccountSyncInteger $stop.Index 1) -or
        -not(Test-DangoAccountSyncBoolean $stop.Stopped $true) -or -not(Test-DangoAccountSyncBoolean $stop.Disposed $true) -or
        -not(Test-DangoAccountSyncInteger $stop.InFlightRequests 0) -or -not(Test-DangoAccountSyncBoolean $stop.LifecycleTasksSettled $true)){throw 'accountsync_b1c_host_cleanup_rejected'}
    foreach($index in @(1,2)){$desktopStart=$Rows[$index*2].Facts;$desktopStop=$Rows[$index*2+1].Facts
        if(-not(Test-DangoAccountSyncInteger $desktopStart.Index $index) -or -not(Test-DangoAccountSyncBoolean $desktopStart.Attempt $true) -or
            -not(Test-DangoAccountSyncInteger $desktopStop.Index $index) -or
            -not(Test-DangoAccountSyncBoolean $desktopStop.InternalDisposeTaskObservable $false)){throw 'accountsync_b1c_desktop_cleanup_rejected'}
        foreach($key in $desktopBools){if(-not(Test-DangoAccountSyncBoolean $desktopStop.$key $true)){throw 'accountsync_b1c_desktop_cleanup_rejected'}}}
    $disposed=$Rows[7].Facts
    if(-not(Test-DangoAccountSyncBoolean $disposed.NativeKeyIdentityCaptured $true) -or
        -not(Test-DangoAccountSyncBoolean $disposed.NativeCleanupConfirmed $true) -or
        -not(Test-DangoAccountSyncBoolean $disposed.NativeProbeFailed $false)){throw 'accountsync_b1c_native_cleanup_rejected'}
    [pscustomobject]@{HttpsHosts=1;Certificates=1;DesktopHosts=2;NativeCleanupConfirmed=$true;DesktopCleanupConfirmed=$true}
}

function Assert-DangoAccountSyncB1cJourney {
    param($Evidence,$Scope)
    Assert-DangoAccountSyncB1cScopeInputs $Scope
    $fields=@('Version','OwnerId','Passed','Actors','Cases','ConfigurationAttempts','ConfigurationSuccessfulWrites','LoginWrites','ProfileWrites','LogoutWrites',
        'DesktopHosts','HttpsHosts','HttpRequests','UsesSystemTrust','UsesWindowsCredentials','ActualFormalHost','ActualApplicationHost','HealthyNativeFrames',
        'LocalLogoutIntents','DurationSeconds','DesktopTransports','ClientsCreated','CleanupPassed')
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence $fields) -or -not(Test-DangoAccountSyncB1cString $Evidence.OwnerId $Scope.OwnerId)){throw 'accountsync_b1c_journey_shape_rejected'}
    foreach($entry in @{Version=1;ConfigurationAttempts=3;ConfigurationSuccessfulWrites=3;LoginWrites=4;ProfileWrites=2;LogoutWrites=4;DesktopHosts=2;HttpsHosts=1;LocalLogoutIntents=2}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncInteger $Evidence.($entry.Key) $entry.Value)){throw 'accountsync_b1c_journey_rejected'}}
    foreach($entry in @{Passed=$true;CleanupPassed=$true;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;ActualApplicationHost=$true;HealthyNativeFrames=$true}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncBoolean $Evidence.($entry.Key) $entry.Value)){throw 'accountsync_b1c_journey_rejected'}}
    foreach($entry in @{HttpRequests=128;DesktopTransports=8;ClientsCreated=12}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncIntegerType $Evidence.($entry.Key)) -or $Evidence.($entry.Key) -lt 1 -or $Evidence.($entry.Key) -gt $entry.Value){throw 'accountsync_b1c_journey_rejected'}}
    $duration=$Evidence.DurationSeconds
    if(($duration -isnot [double] -and $duration -isnot [decimal] -and $duration -isnot [int] -and $duration -isnot [long]) -or
        [double]::IsNaN([double]$duration) -or [double]::IsInfinity([double]$duration) -or $duration -lt 0 -or $duration -ge 600){throw 'accountsync_b1c_journey_rejected'}
    if($Evidence.Actors -isnot [Array] -or $Evidence.Actors.Count -ne 2 -or $Evidence.Cases -isnot [Array] -or $Evidence.Cases.Count -ne 2){throw 'accountsync_b1c_journey_rejected'}
    for($i=0;$i -lt 2;$i++){$actor=$Evidence.Actors[$i];$expected=$Scope.Accounts[$i]
        $actorFields=@($expected.PSObject.Properties.Name)+@('Sessions','Revoked','Attempts','MailState','RecoveryEpoch')
        if(-not(Test-DangoAccountSyncB1cObjectShape $actor $actorFields)){throw 'accountsync_b1c_journey_actor_rejected'}
        foreach($key in @('Actor','UserId','PetId','DefaultRoomId','Nickname')){if(-not(Test-DangoAccountSyncB1cString $actor.$key $expected.$key)){throw 'accountsync_b1c_journey_actor_rejected'}}
        foreach($entry in @{ProfileVersion=2;ConfigurationVersion=($i+2);Sessions=2;Revoked=2;Attempts=0}.GetEnumerator()){
            if(-not(Test-DangoAccountSyncInteger $actor.($entry.Key) $entry.Value)){throw 'accountsync_b1c_journey_actor_rejected'}}
        if(-not(Test-DangoAccountSyncB1cFixedValue $actor.SessionIds $expected.SessionIds) -or
            -not(Test-DangoAccountSyncB1cString $actor.RecoveryEpoch $Scope.RecoveryEpoch) -or
            -not(Test-DangoAccountSyncB1cString $actor.MailState 'Pending')){throw 'accountsync_b1c_journey_actor_rejected'}
        Assert-DangoAccountSyncB1cConfiguration $actor.Configuration $expected.Actor
        $case=$Evidence.Cases[$i]
        $caseFields=@('Case','Passed','HeldMethod','HeldResponseVersion','NewBaselineVersion','CancellationObserved','ReleaseObserved','HeldResponseSettled',
            'OldPumpSettledBeforeNewBaseline','OldMainRequestsAfterStop','CancelledLogoutIntent','AOriginalHasReceipt')
        if(-not(Test-DangoAccountSyncB1cObjectShape $case $caseFields) -or -not(Test-DangoAccountSyncB1cString $case.Case @('late-get','late-patch')[$i]) -or
            -not(Test-DangoAccountSyncB1cString $case.HeldMethod @('GET','PATCH')[$i]) -or
            -not(Test-DangoAccountSyncInteger $case.HeldResponseVersion ($i+1)) -or -not(Test-DangoAccountSyncInteger $case.NewBaselineVersion ($i+1)) -or
            -not(Test-DangoAccountSyncInteger $case.OldMainRequestsAfterStop 0) -or -not(Test-DangoAccountSyncBoolean $case.AOriginalHasReceipt $false)){throw 'accountsync_b1c_journey_case_rejected'}
        foreach($key in @('Passed','CancellationObserved','ReleaseObserved','HeldResponseSettled','OldPumpSettledBeforeNewBaseline','CancelledLogoutIntent')){
            if(-not(Test-DangoAccountSyncBoolean $case.$key $true)){throw 'accountsync_b1c_journey_case_rejected'}}
    }
}

function Assert-DangoAccountSyncB1cLogoutIntents {
    param($Rows,$Scope)
    if(@($Rows).Count -ne 2){throw 'accountsync_b1c_logout_intent_count_rejected'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for($i=0;$i -lt 2;$i++){$row=$Rows[$i]
        if(-not(Test-DangoAccountSyncB1cObjectShape $row @('Version','OwnerId','Sequence','Case','ClientId','UserId','PetId','SessionId','Intent','Forwarded','AtUtc')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence ($i+1)) -or
            -not(Test-DangoAccountSyncB1cString $row.OwnerId $Scope.OwnerId) -or -not(Test-DangoAccountSyncB1cString $row.Case @('late-get','late-patch')[$i]) -or
            -not(Test-DangoAccountSyncIntegerType $row.ClientId) -or $row.ClientId -lt 1 -or $row.ClientId -gt 12 -or
            -not(Test-DangoAccountSyncB1cString $row.UserId $Scope.Accounts[0].UserId) -or -not(Test-DangoAccountSyncB1cString $row.PetId $Scope.Accounts[0].PetId) -or
            -not(Test-DangoAccountSyncB1cStringIn $row.SessionId $Scope.Accounts[0].SessionIds) -or -not $seen.Add($row.SessionId) -or
            -not(Test-DangoAccountSyncB1cString $row.Intent 'uia-logout') -or -not(Test-DangoAccountSyncBoolean $row.Forwarded $false) -or
            -not(Test-DangoAccountSyncTimestamp $row.AtUtc)){throw 'accountsync_b1c_logout_intent_rejected'}
    }
}

function Get-DangoAccountSyncB1cConfigurationSha256 {
    param([ValidateSet('a','b')][string]$Actor,[double]$EyeSize)
    if([double]::IsNaN($EyeSize) -or [double]::IsInfinity($EyeSize)){throw 'accountsync_b1c_configuration_digest_rejected'}
    $color=if($Actor -ceq 'a'){'#FFE8C2'}else{'#9ECBEF'}
    $size=if($EyeSize -eq 0){'0'}else{$EyeSize.ToString('R',[Globalization.CultureInfo]::InvariantCulture)}
    $canonical='{"appearanceSchemaVersion":3,"bodyColorHex":"'+$color+'","eyeColorHex":"#272321","eyeSizePercent":'+$size+',"eyeOvalityPercent":0,"eyeHeightControlPercent":51,"eyeSpacingControlPercent":24.5}'
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical)))
}

function Assert-DangoAccountSyncB1cArchives {
    param($Evidence,$Scope,[int]$HttpsPort)
    Assert-DangoAccountSyncB1cScopeInputs $Scope
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence @('Version','OwnerId','Count','ReceiptCount','ExactOriginalsAndReceipts','Archives')) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Version 1) -or -not(Test-DangoAccountSyncB1cString $Evidence.OwnerId $Scope.OwnerId) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Count 5) -or -not(Test-DangoAccountSyncInteger $Evidence.ReceiptCount 4) -or
        -not(Test-DangoAccountSyncBoolean $Evidence.ExactOriginalsAndReceipts $true) -or $Evidence.Archives -isnot [Array] -or
        $Evidence.Archives.Count -ne 5 -or $HttpsPort -lt 1 -or $HttpsPort -gt 65535){throw 'accountsync_b1c_archive_rejected'}
    $steps=@('late-get-choice','late-get-save','late-patch-unacknowledged','late-patch-choice','late-patch-save')
    $localActors=@('a','b','a','a','b');$localSizes=@(118,125,111,118,130);$cloudActors=@('b','b','a','b','b');$cloudSizes=@(103,103,103,125,125)
    $versions=@(1,1,1,2,2);$choices=@(1,0,0,1,0);$outcomes=@(0,1,$null,0,1);$observed=@(1,2,$null,2,3)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for($i=0;$i -lt 5;$i++){$row=$Evidence.Archives[$i];$actor=if($i -eq 2){'a'}else{'b'};$account=$Scope.Accounts[$(if($actor -ceq 'a'){0}else{1})]
        if(-not(Test-DangoAccountSyncB1cObjectShape $row @('Actor','Case','Step','JournalId','Origin','UserId','PetId','LocalOriginalSha256','LocalConfigurationSha256','CloudConfigurationSha256','ConfigurationVersion','Choice','ReceiptPresent','Outcome','ObservedCloudVersion','FailureCode')) -or
            -not(Test-DangoAccountSyncB1cString $row.Actor $actor) -or -not(Test-DangoAccountSyncB1cString $row.Case $(if($i -lt 2){'late-get'}else{'late-patch'})) -or
            -not(Test-DangoAccountSyncB1cString $row.Step $steps[$i]) -or -not(Test-DangoAccountSyncB1cString $row.Origin "https://127.0.0.1:$HttpsPort/") -or
            -not(Test-DangoAccountSyncB1cString $row.UserId $account.UserId) -or -not(Test-DangoAccountSyncB1cString $row.PetId $account.PetId) -or
            -not(Test-DangoAccountSyncInteger $row.ConfigurationVersion $versions[$i]) -or -not(Test-DangoAccountSyncInteger $row.Choice $choices[$i]) -or
            -not(Test-DangoAccountSyncBoolean $row.ReceiptPresent ($i -ne 2)) -or $null -ne $row.FailureCode -or
            $row.LocalOriginalSha256 -isnot [string] -or $row.LocalOriginalSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or
            -not(Test-DangoAccountSyncB1cString $row.LocalConfigurationSha256 (Get-DangoAccountSyncB1cConfigurationSha256 $localActors[$i] $localSizes[$i])) -or
            -not(Test-DangoAccountSyncB1cString $row.CloudConfigurationSha256 (Get-DangoAccountSyncB1cConfigurationSha256 $cloudActors[$i] $cloudSizes[$i]))){throw 'accountsync_b1c_archive_rejected'}
        [void](ConvertTo-DangoAccountSyncB1cGuid $row.JournalId)
        if(-not $seen.Add($row.JournalId)){throw 'accountsync_b1c_archive_rejected'}
        if($i -eq 2){if($null -ne $row.Outcome -or $null -ne $row.ObservedCloudVersion){throw 'accountsync_b1c_archive_rejected'}}
        elseif(-not(Test-DangoAccountSyncInteger $row.Outcome $outcomes[$i]) -or -not(Test-DangoAccountSyncInteger $row.ObservedCloudVersion $observed[$i])){throw 'accountsync_b1c_archive_rejected'}
    }
}

function Assert-DangoAccountSyncB1cAuxAgreement {
    param($Archives,$FaultEvidence)
    $patches=@($FaultEvidence.Requests|Where-Object {$_.Method -ceq 'PATCH' -and $_.Path -ceq '/api/v1/me/pet/configuration'})
    if($patches.Count -ne 3){throw 'accountsync_b1c_aux_evidence_disagreement'}
    # Request order is B125, A111, B130; archive order has A's unacknowledged original in the middle.
    for($i=0;$i -lt 3;$i++){$archive=$Archives.Archives[@(1,2,4)[$i]];$patch=$patches[$i]
        if(-not(Test-DangoAccountSyncB1cString $archive.Actor $patch.Actor) -or
            -not(Test-DangoAccountSyncB1cString $archive.UserId $patch.UserId) -or -not(Test-DangoAccountSyncB1cString $archive.PetId $patch.PetId) -or
            -not(Test-DangoAccountSyncB1cString $archive.LocalConfigurationSha256 $patch.ConfigurationSha256) -or
            $archive.ConfigurationVersion -ne $patch.ExpectedVersion){throw 'accountsync_b1c_aux_evidence_disagreement'}}
}

function Assert-DangoAccountSyncB1cFaultEvidence {
    param($Evidence,$Scope,$Intents,$Journey)
    Assert-DangoAccountSyncB1cScopeInputs $Scope;Assert-DangoAccountSyncB1cLogoutIntents $Intents $Scope
    if(-not(Test-DangoAccountSyncB1cObjectShape $Evidence @('Version','OwnerId','Completed','Holds','Requests','Phases','CoordinatorsStopped','DispatcherOperationsSettled','OriginalTasksSettled')) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Version 1) -or -not(Test-DangoAccountSyncB1cString $Evidence.OwnerId $Scope.OwnerId) -or
        $Evidence.Holds -isnot [Array] -or $Evidence.Holds.Count -ne 2 -or $Evidence.Requests -isnot [Array] -or
        $Evidence.Requests.Count -lt 1 -or $Evidence.Requests.Count -gt 128 -or $Evidence.Phases -isnot [Array] -or $Evidence.Phases.Count -ne 20){throw 'accountsync_b1c_fault_evidence_rejected'}
    foreach($key in @('Completed','CoordinatorsStopped','DispatcherOperationsSettled','OriginalTasksSettled')){
        if(-not(Test-DangoAccountSyncBoolean $Evidence.$key $true)){throw 'accountsync_b1c_fault_evidence_rejected'}}
    if($null -ne $Journey -and $Journey.HttpRequests -ne $Evidence.Requests.Count){throw 'accountsync_b1c_fault_evidence_rejected'}
    $requests=$Evidence.Requests;$sequence=0;$clientRoles=@{};$clientActors=@{};$clientSessions=@{};$petReads=@{}
    foreach($request in $requests){$sequence++
        if(-not(Test-DangoAccountSyncB1cObjectShape $request @('Sequence','ClientId','Role','Actor','Method','Path','UserId','PetId','SessionId','PetReadOrdinal','ExpectedVersion','ConfigurationSha256','PreviousPumpSettled','Retired','ResponseStatus','ResponseVersion')) -or
            -not(Test-DangoAccountSyncInteger $request.Sequence $sequence) -or -not(Test-DangoAccountSyncIntegerType $request.ClientId) -or
            $request.ClientId -lt 1 -or $request.ClientId -gt 12 -or -not(Test-DangoAccountSyncB1cStringIn $request.Role @('desktop','registration','cleanup')) -or
            -not(Test-DangoAccountSyncB1cStringIn $request.Method @('GET','POST','PATCH')) -or $request.Path -isnot [string] -or
            -not(Test-DangoAccountSyncBoolean $request.Retired $false) -or -not(Test-DangoAccountSyncIntegerType $request.ResponseStatus) -or
            $request.ResponseStatus -lt 200 -or $request.ResponseStatus -ge 300 -or
            ($null -ne $request.PreviousPumpSettled -and $request.PreviousPumpSettled -isnot [bool])){throw 'accountsync_b1c_fault_request_rejected'}
        $id=[string]$request.ClientId
        if($clientRoles.ContainsKey($id) -and -not(Test-DangoAccountSyncB1cString $request.Role $clientRoles[$id])){throw 'accountsync_b1c_fault_request_rejected'}
        $clientRoles[$id]=$request.Role
        if($null -eq $request.Actor){if($request.Role -cne 'desktop' -or $request.Method -cne 'GET' -or $request.Path -cne '/api/v1/auth/request-protection' -or
            $null -ne $request.UserId -or $null -ne $request.PetId -or $null -ne $request.SessionId -or $clientActors.ContainsKey($id)){throw 'accountsync_b1c_fault_request_rejected'}}
        else{
            if(-not(Test-DangoAccountSyncB1cStringIn $request.Actor @('a','b')) -or
                ($clientActors.ContainsKey($id) -and -not(Test-DangoAccountSyncB1cString $request.Actor $clientActors[$id]))){throw 'accountsync_b1c_fault_request_rejected'}
            $clientActors[$id]=$request.Actor;$account=$Scope.Accounts[$(if($request.Actor -ceq 'a'){0}else{1})]
            if($request.Role -ceq 'registration'){
                if($null -ne $request.SessionId -or ($null -ne $request.UserId -and -not(Test-DangoAccountSyncB1cString $request.UserId $account.UserId)) -or
                    ($null -ne $request.PetId -and -not(Test-DangoAccountSyncB1cString $request.PetId $account.PetId))){throw 'accountsync_b1c_fault_request_rejected'}}
            elseif(-not(Test-DangoAccountSyncB1cString $request.UserId $account.UserId) -or -not(Test-DangoAccountSyncB1cString $request.PetId $account.PetId)){
                throw 'accountsync_b1c_fault_request_rejected'}
            if($null -ne $request.SessionId){
                if(-not(Test-DangoAccountSyncB1cStringIn $request.SessionId $account.SessionIds) -or
                    ($clientSessions.ContainsKey($id) -and -not(Test-DangoAccountSyncB1cString $request.SessionId $clientSessions[$id]))){throw 'accountsync_b1c_fault_request_rejected'}
                $clientSessions[$id]=$request.SessionId}
            elseif($request.Role -ceq 'cleanup' -or $request.Path.StartsWith('/api/v1/me',[StringComparison]::Ordinal) -or $request.Path -ceq '/api/v1/auth/logout'){
                throw 'accountsync_b1c_fault_request_rejected'}
        }
        $allowed=($request.Method -ceq 'GET' -and $request.Path -cin @('/api/v1/auth/request-protection','/api/v1/me','/api/v1/me/profile','/api/v1/me/pet')) -or
            ($request.Method -ceq 'POST' -and $request.Path -cin @('/api/v1/auth/registrations','/api/v1/auth/login','/api/v1/auth/logout')) -or
            ($request.Method -ceq 'PATCH' -and $request.Path -cin @('/api/v1/me/profile','/api/v1/me/pet/configuration')) -or
            ($request.Role -ceq 'registration' -and (($request.Method -ceq 'GET' -and $request.Path -cmatch '\A/api/v1/auth/registrations/[0-9a-f-]{36}/status\z') -or
                ($request.Method -ceq 'POST' -and $request.Path -cmatch '\A/api/v1/auth/registrations/[0-9a-f-]{36}/complete\z')))
        if(-not $allowed){throw 'accountsync_b1c_fault_request_rejected'}
        if($request.Path -ceq '/api/v1/me/pet'){$ordinal=if($petReads.ContainsKey($id)){$petReads[$id]+1}else{1}
            if(-not(Test-DangoAccountSyncInteger $request.PetReadOrdinal $ordinal)){throw 'accountsync_b1c_fault_request_rejected'};$petReads[$id]=$ordinal}
        elseif($null -ne $request.PetReadOrdinal){throw 'accountsync_b1c_fault_request_rejected'}
        if($request.Method -ceq 'PATCH' -and $request.Path -ceq '/api/v1/me/pet/configuration'){
            if(-not(Test-DangoAccountSyncIntegerType $request.ExpectedVersion) -or $request.ExpectedVersion -lt 1 -or
                $request.ConfigurationSha256 -isnot [string] -or $request.ConfigurationSha256 -cnotmatch '\A[A-F0-9]{64}\z'){throw 'accountsync_b1c_fault_request_rejected'}}
        elseif($null -ne $request.ExpectedVersion -or $null -ne $request.ConfigurationSha256){throw 'accountsync_b1c_fault_request_rejected'}
        if($request.Path -cin @('/api/v1/me/pet','/api/v1/me/pet/configuration')){
            if(-not(Test-DangoAccountSyncIntegerType $request.ResponseVersion) -or $request.ResponseVersion -lt 1){throw 'accountsync_b1c_fault_request_rejected'}}
        elseif($null -ne $request.ResponseVersion){throw 'accountsync_b1c_fault_request_rejected'}
        if($request.Role -ceq 'registration' -and ($request.Path -cin @('/api/v1/auth/login','/api/v1/auth/logout') -or $request.Path.StartsWith('/api/v1/me',[StringComparison]::Ordinal))){throw 'accountsync_b1c_fault_request_rejected'}
        if($request.Role -ceq 'cleanup' -and ($request.Actor -cne 'a' -or $request.Method -ceq 'PATCH' -or $request.Path -cnotin @('/api/v1/auth/request-protection','/api/v1/me','/api/v1/me/pet','/api/v1/auth/logout'))){throw 'accountsync_b1c_fault_request_rejected'}
    }
    foreach($role in @('registration','cleanup')){if(@($clientRoles.Keys|Where-Object {$clientRoles[$_] -ceq $role}).Count -ne 2){throw 'accountsync_b1c_fault_clients_rejected'}}
    $desktopCount=@($clientRoles.Keys|Where-Object {$clientRoles[$_] -ceq 'desktop'}).Count
    if($desktopCount -lt 4 -or $desktopCount -gt 8 -or ($null -ne $Journey -and ($desktopCount -gt $Journey.DesktopTransports -or $clientRoles.Count -gt $Journey.ClientsCreated))){throw 'accountsync_b1c_fault_clients_rejected'}
    foreach($path in @('/api/v1/auth/registrations','/api/v1/auth/login','/api/v1/auth/logout','/api/v1/me/profile','/api/v1/me/pet/configuration')){
        $expectedCount=switch -CaseSensitive ($path){'/api/v1/auth/registrations'{2};'/api/v1/auth/login'{4};'/api/v1/auth/logout'{4};'/api/v1/me/profile'{2};'/api/v1/me/pet/configuration'{3}}
        if(@($requests|Where-Object {$_.Path -ceq $path -and $_.Method -cne 'GET'}).Count -ne $expectedCount){throw 'accountsync_b1c_fault_counts_rejected'}}
    if(@($requests|Where-Object {$_.Role -ceq 'registration' -and $_.Method -ceq 'POST' -and $_.Path.EndsWith('/complete',[StringComparison]::Ordinal)}).Count -ne 2){throw 'accountsync_b1c_fault_counts_rejected'}
    $logins=@($requests|Where-Object {$_.Path -ceq '/api/v1/auth/login'});$loginSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($login in $logins){if($login.Role -cne 'desktop' -or $null -eq $login.SessionId -or -not $loginSeen.Add($login.SessionId)){throw 'accountsync_b1c_fault_session_rejected'}}
    foreach($session in @($Scope.Accounts.SessionIds)){if(-not $loginSeen.Contains($session)){throw 'accountsync_b1c_fault_session_rejected'}}
    $logoutSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($logout in @($requests|Where-Object {$_.Path -ceq '/api/v1/auth/logout'})){
        if(-not $logoutSeen.Add($logout.SessionId) -or ($logout.Actor -ceq 'a' -and $logout.Role -cne 'cleanup') -or
            ($logout.Actor -ceq 'b' -and $logout.Role -cne 'desktop')){throw 'accountsync_b1c_fault_session_rejected'}}
    foreach($profileActor in @('a','b')){if(@($requests|Where-Object {$_.Method -ceq 'PATCH' -and $_.Path -ceq '/api/v1/me/profile' -and $_.Actor -ceq $profileActor -and $_.Role -ceq 'desktop'}).Count -ne 1){throw 'accountsync_b1c_fault_counts_rejected'}}
    $patches=@($requests|Where-Object {$_.Method -ceq 'PATCH' -and $_.Path -ceq '/api/v1/me/pet/configuration'})
    for($i=0;$i -lt 3;$i++){$patch=$patches[$i]
        if($patch.Role -cne 'desktop' -or -not(Test-DangoAccountSyncB1cString $patch.Actor @('b','a','b')[$i]) -or
            -not(Test-DangoAccountSyncInteger $patch.ExpectedVersion @(1,1,2)[$i]) -or -not(Test-DangoAccountSyncInteger $patch.ResponseStatus 200) -or
            -not(Test-DangoAccountSyncInteger $patch.ResponseVersion @(2,2,3)[$i]) -or
            -not(Test-DangoAccountSyncB1cString $patch.ConfigurationSha256 (Get-DangoAccountSyncB1cConfigurationSha256 @('b','a','b')[$i] @(125,111,130)[$i]))){throw 'accountsync_b1c_fault_patch_rejected'}}
    for($i=0;$i -lt 2;$i++){$case=@('late-get','late-patch')[$i];$hold=$Evidence.Holds[$i];$intent=$Intents[$i]
        if(-not(Test-DangoAccountSyncB1cObjectShape $hold @('Case','Method','UserId','PetId','SessionId','ResponseVersion','CancellationObserved','Released','Settled','CleanupRelease','ActualSuccessfulResponse')) -or
            -not(Test-DangoAccountSyncB1cString $hold.Case $case) -or -not(Test-DangoAccountSyncB1cString $hold.Method @('GET','PATCH')[$i]) -or
            -not(Test-DangoAccountSyncB1cString $hold.UserId $Scope.Accounts[0].UserId) -or -not(Test-DangoAccountSyncB1cString $hold.PetId $Scope.Accounts[0].PetId) -or
            -not(Test-DangoAccountSyncB1cString $hold.SessionId $intent.SessionId) -or -not(Test-DangoAccountSyncInteger $hold.ResponseVersion ($i+1)) -or
            -not(Test-DangoAccountSyncBoolean $hold.CleanupRelease $false)){throw 'accountsync_b1c_fault_hold_rejected'}
        foreach($key in @('CancellationObserved','Released','Settled','ActualSuccessfulResponse')){if(-not(Test-DangoAccountSyncBoolean $hold.$key $true)){throw 'accountsync_b1c_fault_hold_rejected'}}
        $rows=@($Evidence.Phases[($i*10)..($i*10+9)])
        $labels=@("${case}:start",'actual-response-captured','main-retired',"${case}:new-identity-awaiting-old-pump","${case}:old-response-released","${case}:old-pump-settled","${case}:new-baseline-verified","${case}:b-save-converged","${case}:a-session-cleaned","${case}:complete")
        for($p=0;$p -lt 10;$p++){$phase=$rows[$p]
            if(-not(Test-DangoAccountSyncB1cObjectShape $phase @('Sequence','Phase','ClientId','RequestSequence')) -or
                -not(Test-DangoAccountSyncInteger $phase.Sequence ($i*10+$p+1)) -or -not(Test-DangoAccountSyncB1cString $phase.Phase $labels[$p])){throw 'accountsync_b1c_fault_phase_rejected'}
            if($p -eq 0){if($null -ne $phase.ClientId -or $null -ne $phase.RequestSequence){throw 'accountsync_b1c_fault_phase_rejected'}}
            else{if(-not(Test-DangoAccountSyncIntegerType $phase.ClientId) -or -not $clientRoles.ContainsKey([string]$phase.ClientId)){throw 'accountsync_b1c_fault_phase_rejected'}
                if($p -cin @(1,6,7)){if(-not(Test-DangoAccountSyncIntegerType $phase.RequestSequence) -or $phase.RequestSequence -lt 1 -or $phase.RequestSequence -gt $requests.Count){throw 'accountsync_b1c_fault_phase_rejected'}}
                elseif($null -ne $phase.RequestSequence){throw 'accountsync_b1c_fault_phase_rejected'}}
        }
        $held=$requests[$rows[1].RequestSequence-1];$baseline=$requests[$rows[6].RequestSequence-1];$saved=$requests[$rows[7].RequestSequence-1]
        $aClient=$intent.ClientId;$bClient=$baseline.ClientId;$cleanupClient=$rows[8].ClientId
        if($held.ClientId -ne $aClient -or $held.Actor -cne 'a' -or $held.Role -cne 'desktop' -or $held.Method -cne $hold.Method -or
            -not(Test-DangoAccountSyncB1cString $held.SessionId $hold.SessionId) -or $held.ResponseVersion -ne $hold.ResponseVersion -or
            $held.Path -cne $(if($i -eq 0){'/api/v1/me/pet'}else{'/api/v1/me/pet/configuration'}) -or $held.ResponseStatus -ne 200 -or
            $baseline.Role -cne 'desktop' -or $baseline.Actor -cne 'b' -or $baseline.Method -cne 'GET' -or $baseline.Path -cne '/api/v1/me/pet' -or
            $baseline.ResponseVersion -ne ($i+1) -or $baseline.PetReadOrdinal -lt 2 -or -not(Test-DangoAccountSyncBoolean $baseline.PreviousPumpSettled $true) -or
            $baseline.Sequence -le $held.Sequence -or $saved.Sequence -le $baseline.Sequence -or $saved.ClientId -ne $bClient -or
            $saved.Sequence -ne $patches[@(0,2)[$i]].Sequence){throw 'accountsync_b1c_fault_trace_rejected'}
        foreach($p in @(1,2,4,5)){if($rows[$p].ClientId -ne $aClient){throw 'accountsync_b1c_fault_trace_rejected'}}
        foreach($p in @(3,6,7,9)){if($rows[$p].ClientId -ne $bClient){throw 'accountsync_b1c_fault_trace_rejected'}}
        if(@($requests|Where-Object {$_.ClientId -eq $aClient -and $_.Sequence -gt $held.Sequence}).Count -ne 0){throw 'accountsync_b1c_fault_old_main_request_rejected'}
        $cleanup=@($requests|Where-Object {$_.ClientId -eq $cleanupClient})
        if($cleanup.Count -ne 4 -or @($cleanup|Where-Object {$_.Role -cne 'cleanup' -or $_.Actor -cne 'a' -or $_.SessionId -cne $hold.SessionId -or $_.Sequence -le $saved.Sequence}).Count -ne 0 -or
            @($cleanup|Where-Object {$_.Method -ceq 'GET' -and $_.Path -ceq '/api/v1/me'}).Count -ne 1 -or
            @($cleanup|Where-Object {$_.Method -ceq 'GET' -and $_.Path -ceq '/api/v1/me/pet'}).Count -ne 1 -or
            @($cleanup|Where-Object {$_.Method -ceq 'GET' -and $_.Path -ceq '/api/v1/auth/request-protection'}).Count -ne 1 -or
            @($cleanup|Where-Object {$_.Method -ceq 'POST' -and $_.Path -ceq '/api/v1/auth/logout'}).Count -ne 1){throw 'accountsync_b1c_fault_cleanup_rejected'}
    }
}

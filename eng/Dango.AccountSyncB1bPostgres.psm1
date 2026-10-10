Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1') -DisableNameChecking

function Get-DangoAccountSyncB1bPlan {
    param([string]$Repository)
    Assert-DangoAccountSyncB1bPathText $Repository
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    [pscustomobject]@{Version=1;Stage='AccountSyncB1b1';RunName='run20261008b0';Executed=$false
        Repository=[IO.Path]::GetFullPath($Repository);StateDirectory=$state;DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261011accountsyncb1b1')
        PostgresBinDirectory=(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin')
        Database='dango_backend_b0';Role='dango_b0_owner';DatabasePort=63237;ExpectedTests=1;MaximumToolProcesses=10
        Filter='FullyQualifiedName=Dango.AccountSyncReview.Tests.AccountSyncB1bJourneyTests.RealHttpsCoordinatorsHandleConcurrencyLostResponsesAndHostRecovery'
        CandidateDirectory='artifacts/account-sync-b1b1-candidate/bin/Dango.AccountSyncReview.Tests/release'
        ApprovalVariable='DANGO_BACKEND_ACCOUNTSYNC_B1B1_APPROVED';TrxFileName='account-sync-b1b1.trx'
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity',
            '20261009124814_AccessoryAccountBackup','20261010055529_AccountProfile')
        Budgets=[ordered]@{Invite=1;Start=1;Confirm=1;Complete=1;Login=2;Profile=1;Configuration=8;Logout=2}
        MaximumHttpRequests=128;MaximumDurationMinutes=10;ProcessTimeoutSeconds=300;HttpsHosts=2;Certificates=1;DesktopHosts=0
        ConfigurationSuccessfulWrites=6;FinalConfigurationVersion=7;FinalEyeSizePercent=139;FinalProfileVersion=2
        MigrationOperations=0;RoomOperations=0;SystemTrustOperations=0;WindowsCredentialOperations=0;EmailDispatchOperations=0
        RetryAllowed=$false}
}

function Assert-DangoAccountSyncB1bPathText {
    param([string]$Path)
    if([string]::IsNullOrWhiteSpace($Path)){throw 'accountsync_b1b_path_text_rejected'}
    foreach($character in $Path.ToCharArray()){
        if([char]::IsControl($character) -or [char]::GetUnicodeCategory($character) -eq [Globalization.UnicodeCategory]::Format){
            throw 'accountsync_b1b_path_text_rejected'}
    }
}

function Test-DangoAccountSyncB1bString {
    param($Actual,[string]$Expected)
    return $Actual -is [string] -and [string]::Equals($Actual,$Expected,[StringComparison]::Ordinal)
}

function Test-DangoAccountSyncB1bStringIn {
    param($Actual,[string[]]$Expected)
    foreach($value in $Expected){if(Test-DangoAccountSyncB1bString $Actual $value){return $true}}
    return $false
}

function Test-DangoAccountSyncB1bObjectShape {
    param($Value,[string[]]$Fields)
    if($Value -isnot [System.Management.Automation.PSCustomObject] -or @($Value.PSObject.Properties).Count -ne $Fields.Count){return $false}
    foreach($field in $Fields){if(-not(Test-DangoAccountSyncB1bStringIn $field @($Value.PSObject.Properties.Name))){return $false}}
    return $true
}

function Test-DangoAccountSyncB1bFixedValue {
    param($Actual,$Expected)
    if($null -eq $Expected){return $null -eq $Actual}
    if($null -eq $Actual){return $false}
    if($Expected -is [string]){return Test-DangoAccountSyncB1bString $Actual $Expected}
    if($Expected -is [Collections.IDictionary]){
        if($Actual -isnot [Collections.IDictionary] -or $Actual.Count -ne $Expected.Count){return $false}
        foreach($key in $Expected.Keys){
            $actualKey=@($Actual.Keys|Where-Object{[string]::Equals($_,$key,[StringComparison]::Ordinal)})
            if($actualKey.Count -ne 1 -or -not(Test-DangoAccountSyncB1bFixedValue $Actual[$actualKey[0]] $Expected[$key])){return $false}
        };return $true
    }
    if($Expected -is [Array]){
        if($Actual -isnot [Array] -or $Actual.Count -ne $Expected.Count){return $false}
        for($i=0;$i -lt $Expected.Count;$i++){if(-not(Test-DangoAccountSyncB1bFixedValue $Actual[$i] $Expected[$i])){return $false}}
        return $true
    }
    return $Actual.GetType() -eq $Expected.GetType() -and $Actual -ceq $Expected
}

function Assert-DangoAccountSyncB1bFixedPlan {
    param($Plan)
    try {
        $fixed=Get-DangoAccountSyncB1bPlan $Plan.Repository
        if(@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count){throw 'rejected'}
        foreach($property in $fixed.PSObject.Properties){$actual=$Plan.PSObject.Properties[$property.Name]
            if($null -eq $actual -or -not(Test-DangoAccountSyncB1bString $actual.Name $property.Name) -or
                -not(Test-DangoAccountSyncB1bFixedValue $actual.Value $property.Value)){throw 'rejected'}}
        return $fixed
    }catch{throw 'accountsync_b1b_fixed_plan_rejected'}
}

function Get-DangoAccountSyncB1bInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan
    $files=@('eng/Dango.AccountSyncB1bPostgres.psm1','eng/invoke-account-sync-b1b1-tests.ps1','eng/test-account-sync-b1b1-boundary.ps1',
        'eng/Dango.AccountProfilePostgres.psm1','eng/Dango.AccountSyncPostgres.psm1','eng/Dango.AccountSyncClosurePostgres.psm1',
        'docs/planning/2026-10-11-account-sync-b1b1-local-plan.md','Directory.Build.props','global.json',
        'eng/tools/Dango.AccountSyncReview.Tests/Dango.AccountSyncReview.Tests.csproj',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1bTestOwner.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1bAttemptBudget.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1bHttpsFixture.cs',
        'eng/tools/Dango.AccountSyncReview.Tests/AccountSyncB1bJourneyTests.cs',
        'src/Dango.Client.Sync/Pets/PetConfigurationLiveSync.cs','src/Dango.Client.Sync/Pets/PetConfigurationHttpClient.cs',
        'src/Dango.Client.Sync/Pets/PetConfigurationFileJournal.cs','src/Dango.Client.Sync/Pets/PetConfigurationChoice.cs')
    $files+=@('tests/Dango.Server.Tests/Dango.Server.Tests.csproj',
        'tests/Dango.Server.Tests/Configuration/AccountSyncScopedSqlMetadataTests.cs',
        'tests/Dango.Server.Tests/Configuration/AccountSyncB1bScopedSqlMetadataTests.cs')
    foreach($relativeRoot in @('src','eng/tools/Dango.AccountSyncReview.Tests')){
        $root=Join-Path $fixed.Repository $relativeRoot;Assert-DangoProfileRegularPath $root
        if([IO.Directory]::Exists($root)){
            foreach($file in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)){
                $relative=[IO.Path]::GetRelativePath($fixed.Repository,$file.FullName).Replace('\','/')
                if($relative -match '/(?:obj|bin)/'){continue}
                if(Test-DangoAccountSyncB1bStringIn ($file.Extension.ToLowerInvariant()) @('.cs','.csproj','.props','.targets')){
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
    if($files.Count -gt 1024){throw 'accountsync_b1b_inputs_bound_rejected'}
    foreach($relative in $files){$path=Join-Path $fixed.Repository $relative;Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+=$relative;continue}
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    $tools=Get-DangoAccountSyncB1bBinaryInputs $fixed;$missing+=@($tools.MissingInputs)
    [pscustomobject]@{Version=1;Stage=$fixed.Stage;RunName=$fixed.RunName;Plan=$fixed;Hashes=$hashes;
        PostgresTools=$tools.Hashes;MissingInputs=$missing}
}

function Get-DangoAccountSyncB1bBinaryInputs {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan;$hashes=[ordered]@{};$missing=@()
    foreach($name in @('postgres.exe','pg_ctl.exe','psql.exe')){$path=Join-Path $fixed.PostgresBinDirectory $name
        Assert-DangoProfileRegularPath $path
        if(-not [IO.File]::Exists($path)){$missing+='postgres-bin/'+$name;continue}
        $hashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    [pscustomobject]@{Hashes=$hashes;MissingInputs=$missing}
}

function New-DangoAccountSyncB1bApproval {
    param($Plan,[string]$Path)
    Assert-DangoAccountSyncB1bPathText $Path
    $physical=Assert-DangoProfileManifestPath $Plan $Path;$inputs=Get-DangoAccountSyncB1bInputs $Plan
    if($inputs.MissingInputs.Count -ne 0){throw 'accountsync_b1b_inputs_missing'}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    [pscustomobject]@{Path=$physical;Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash;Inputs=$inputs}
}

function Assert-DangoAccountSyncB1bApproval {
    param($Plan,[string]$Path,[string]$Digest)
    $document=$null
    try{
        $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan
        Assert-DangoAccountSyncB1bPathText $Path
        if($Digest -cnotmatch '\A[A-F0-9]{64}\z'){throw 'accountsync_b1b_approval_digest_required'}
        $physical=Assert-DangoProfileManifestPath $fixed $Path;$bytes=Read-DangoAccountSyncClosureBytes $physical 262144
        if(-not(Test-DangoAccountSyncB1bString ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) $Digest)){
            throw 'accountsync_b1b_approval_changed'}
        $document=[Text.Json.JsonDocument]::Parse([Text.UTF8Encoding]::new($false,$true).GetString($bytes))
        $current=Get-DangoAccountSyncB1bInputs $fixed
        if($current.MissingInputs.Count -ne 0){throw 'accountsync_b1b_inputs_missing'}
        Assert-DangoAccountSyncB1bJsonValue $document.RootElement $current
        return [pscustomobject]@{Path=$physical;Sha256=$Digest;Inputs=$current}
    }catch{
        if($_.Exception.Message -cmatch '^(accountsync_b1b|accountsync_closure|profile_run)_[a-z0-9_]+$'){throw}
        throw 'accountsync_b1b_approval_invalid'
    }finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoAccountSyncB1bJsonValue {
    param([Text.Json.JsonElement]$Element,$Expected)
    if($Expected -is [bool]){
        if($Element.ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False) -or
            $Element.GetBoolean() -ne $Expected){throw 'accountsync_b1b_json_value_rejected'}
    }elseif($Expected -is [int] -or $Expected -is [long]){
        $number=[long]0
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $Element.TryGetInt64([ref]$number) -or
            $number -ne $Expected){throw 'accountsync_b1b_json_value_rejected'}
    }elseif($Expected -is [string]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            -not(Test-DangoAccountSyncB1bString $Element.GetString() $Expected)){throw 'accountsync_b1b_json_value_rejected'}
    }elseif($Expected -is [Array]){
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $Element.GetArrayLength() -ne $Expected.Count){
            throw 'accountsync_b1b_json_value_rejected'}
        for($i=0;$i -lt $Expected.Count;$i++){Assert-DangoAccountSyncB1bJsonValue $Element[$i] $Expected[$i]}
    }else{
        if($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object){throw 'accountsync_b1b_json_value_rejected'}
        $expectedMap=[ordered]@{}
        if($Expected -is [Collections.IDictionary]){foreach($key in $Expected.Keys){$expectedMap[$key]=$Expected[$key]}}
        elseif($Expected -is [System.Management.Automation.PSCustomObject]){
            foreach($property in $Expected.PSObject.Properties){$expectedMap[$property.Name]=$property.Value}
        }else{throw 'accountsync_b1b_json_value_rejected'}
        $properties=@($Element.EnumerateObject());$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if($properties.Count -ne $expectedMap.Count){throw 'accountsync_b1b_json_value_rejected'}
        foreach($property in $properties){
            if(-not $seen.Add($property.Name) -or -not(Test-DangoAccountSyncB1bStringIn $property.Name @($expectedMap.Keys))){
                throw 'accountsync_b1b_json_value_rejected'}
            Assert-DangoAccountSyncB1bJsonValue $property.Value $expectedMap[$property.Name]
        }
    }
}

function New-DangoAccountSyncB1bReservation {
    param($Plan)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan;$parent=Split-Path -Parent $fixed.EvidenceDirectory
    Assert-DangoProfileRegularPath $parent;Assert-DangoProfileRegularPath $fixed.EvidenceDirectory
    if(-not [IO.Directory]::Exists($parent)){throw 'accountsync_b1b_existing_parent_required'}
    Get-DangoProfileNativeType
    if(-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)){throw 'accountsync_b1b_already_reserved'}
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

function Get-DangoAccountSyncB1bScopedSql {
    param([Guid]$UserId,[Guid]$Epoch,[Guid]$OwnerId,[Guid]$PetId,[Guid]$DefaultRoomId)
    if($PetId -eq [Guid]::Empty -or $DefaultRoomId -eq [Guid]::Empty){throw 'accountsync_b1b_scoped_ids_rejected'}
    $sql=Get-DangoAccountSyncScopedSql $UserId $Epoch $OwnerId
    $replacements=[ordered]@{"email='accountsync-"="email='accountsyncb1b-";"nickname=U&'\65B0\540C\6B65\56E2\5B50'"="nickname=U&'\5F02\5E38\540C\6B65\56E2\5B50'";
        'profile_version=3'='profile_version=2';'p.configuration_version=6'='p.configuration_version=7';"::numeric=128"='::numeric=139';
        'JOIN dango.pets p ON p.user_id=u.id'="JOIN dango.pets p ON p.user_id=u.id AND p.id='$($PetId.ToString('D'))'::uuid";
        'JOIN dango.rooms r ON r.owner_id=u.id'="JOIN dango.rooms r ON r.owner_id=u.id AND r.id='$($DefaultRoomId.ToString('D'))'::uuid"}
    foreach($from in $replacements.Keys){if(@([regex]::Matches($sql,[regex]::Escape($from))).Count -ne 1){throw 'accountsync_b1b_scoped_anchor_rejected'}
        $sql=$sql.Replace($from,$replacements[$from],[StringComparison]::Ordinal)}
    Assert-DangoAccountSyncSqlTransportContract $sql;return $sql
}

function Get-DangoAccountSyncB1bPsqlArguments {
    param($Plan,[string]$Sql)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan
    [string[]]@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$fixed.DatabasePort,
        '-U',$fixed.Role,'-d',$fixed.Database,'-c',$Sql)
}

function Assert-DangoAccountSyncB1bEvidenceContract {
    param([string]$Executable,[string[]]$Arguments,[string]$WorkingDirectory,$Plan,[int]$Port,[hashtable]$EnvironmentValues,
        [bool]$KillOwnedTree,[string]$OutputContract)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan;$accepted=$false
    try{Assert-DangoAccountSyncB1bPathText $Executable;Assert-DangoAccountSyncB1bPathText $WorkingDirectory}
    catch{throw 'accountsync_b1b_evidence_contract_rejected'}
    if($Arguments.Count -ne 17){throw 'accountsync_b1b_evidence_contract_rejected'}
    try{Assert-DangoAccountSyncSqlTransportContract $Arguments[16]}catch{throw 'accountsync_b1b_evidence_contract_rejected'}
    if(Test-DangoAccountSyncB1bString $OutputContract 'AccountSyncB1bEvidenceJson'){
        $accepted=Test-DangoAccountSyncB1bFixedValue $Arguments (Get-DangoAccountSyncB1bPsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql))
    }elseif(Test-DangoAccountSyncB1bString $OutputContract 'AccountSyncB1bSmallJson'){
        $accepted=Test-DangoAccountSyncB1bFixedValue $Arguments (Get-DangoAccountSyncB1bPsqlArguments $fixed (Get-DangoAccountSyncDatabaseSql -MeasureOnly))
        if(-not $accepted -and $Arguments.Count -eq 17){
            $ids=[regex]::Matches($Arguments[16],"'(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})'::uuid")
            if($ids.Count -eq 5){
                $user=[Guid]$ids[0].Value.Substring(1,36);$pet=[Guid]$ids[1].Value.Substring(1,36)
                $room=[Guid]$ids[2].Value.Substring(1,36);$epoch=[Guid]$ids[3].Value.Substring(1,36)
                $mail=[regex]::Match($Arguments[16],"email='accountsyncb1b-([0-9a-f]{32})@example\.invalid'")
                if($mail.Success){$owner=[Guid]::ParseExact($mail.Groups[1].Value,'N')
                    $accepted=Test-DangoAccountSyncB1bFixedValue $Arguments (Get-DangoAccountSyncB1bPsqlArguments $fixed (Get-DangoAccountSyncB1bScopedSql $user $epoch $owner $pet $room))}
            }
        }
    }
    $decoded=$null
    try{
        if(-not $accepted -or $Port -ne 63237 -or $KillOwnedTree -or $EnvironmentValues.Count -ne 2 -or
            -not(Test-DangoAccountSyncB1bStringIn 'PGPASSWORD' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoAccountSyncB1bStringIn 'PGCLIENTENCODING' @($EnvironmentValues.Keys)) -or
            -not(Test-DangoAccountSyncB1bString $EnvironmentValues.PGCLIENTENCODING 'UTF8') -or
            $EnvironmentValues.PGPASSWORD -isnot [string] -or $EnvironmentValues.PGPASSWORD -cnotmatch '\A[A-Za-z0-9+/]{43}=\z' -or
            -not(Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'psql.exe')) -or
            -not(Test-DangoProfilePath $WorkingDirectory $fixed.Repository)){throw 'rejected'}
        $decoded=[Convert]::FromBase64String($EnvironmentValues.PGPASSWORD)
        if($decoded.Length -ne 32 -or -not(Test-DangoAccountSyncB1bString ([Convert]::ToBase64String($decoded)) $EnvironmentValues.PGPASSWORD)){throw 'rejected'}
    }catch{throw 'accountsync_b1b_evidence_contract_rejected'}
    finally{if($null -ne $decoded){[Security.Cryptography.CryptographicOperations]::ZeroMemory($decoded)}}
}

function Save-DangoAccountSyncB1bRawEvidence {
    param($Plan,[string]$FileName,[string]$Text,[int]$MaximumBytes=131072)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan
    if($FileName -cnotmatch '\A(before|after)(-size)?\.(json|stderr\.txt)\z' -and
        -not(Test-DangoAccountSyncB1bStringIn $FileName @('scoped-graph-evidence.json','scoped.stderr.txt'))){throw 'accountsync_b1b_evidence_path_rejected'}
    if($MaximumBytes -ne 131072 -and ($MaximumBytes -ne 1048576 -or
        -not(Test-DangoAccountSyncB1bStringIn $FileName @('before.json','after.json')))){throw 'accountsync_b1b_evidence_bound_rejected'}
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($Text)
    if($Text.Length -gt $MaximumBytes -or $bytes.Length -gt $MaximumBytes){throw 'accountsync_b1b_evidence_bound_rejected'}
    $path=Join-Path $fixed.EvidenceDirectory $FileName;Assert-DangoProfileRegularPath $path
    $stream=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
}

function ConvertFrom-DangoAccountSyncB1bStrictJson {
    param([string]$Json,[int]$MaximumBytes=131072)
    $document=$null
    try{
        if([string]::IsNullOrWhiteSpace($Json) -or $Json.Length -gt $MaximumBytes -or [Text.Encoding]::UTF8.GetByteCount($Json) -gt $MaximumBytes){throw 'rejected'}
        $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=32
        $document=[Text.Json.JsonDocument]::Parse($Json,$options)
        $value=$Json|ConvertFrom-Json -Depth 32
        if($value -isnot [System.Management.Automation.PSCustomObject]){throw 'rejected'}
        Assert-DangoAccountSyncB1bJsonKeys $document.RootElement
        return $value
    }catch{throw 'accountsync_b1b_json_rejected'}
    finally{if($null -ne $document){$document.Dispose()}}
}

function Assert-DangoAccountSyncB1bJsonKeys {
    param([Text.Json.JsonElement]$Element)
    if($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object){
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($property in $Element.EnumerateObject()){
            if(-not $seen.Add($property.Name)){throw 'accountsync_b1b_duplicate_json_rejected'}
            Assert-DangoAccountSyncB1bJsonKeys $property.Value
        }
    }elseif($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array){
        foreach($child in $Element.EnumerateArray()){Assert-DangoAccountSyncB1bJsonKeys $child}
    }
}

function Assert-DangoAccountSyncB1bProtectedGraphShape {
    param($Evidence)
    Assert-DangoProfileProtectedGraphShape $Evidence
    $keys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
        'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence.counts $keys) -or
        -not(Test-DangoAccountSyncB1bObjectShape $Evidence.digests $keys)){throw 'accountsync_b1b_graph_shape_rejected'}
    foreach($key in $keys){foreach($digest in $Evidence.digests.$key){
        if($digest -isnot [string] -or $digest -cnotmatch '\A[a-f0-9]{64}\z'){throw 'accountsync_b1b_graph_shape_rejected'}}}
}

function Assert-DangoAccountSyncB1bTarget {
    param($Plan,$Evidence)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan;Assert-DangoAccountSyncB1bProtectedGraphShape $Evidence
    Assert-DangoAccountSyncB1bPathText $Evidence.dataDirectory
    $fields=@('database','role','version','dataDirectory','listenAddresses','migrations','userIds','profiles','counts','digests')
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence $fields) -or -not(Test-DangoAccountSyncB1bString $Evidence.database $fixed.Database) -or
        -not(Test-DangoAccountSyncB1bString $Evidence.role $fixed.Role) -or -not(Test-DangoAccountSyncIntegerType $Evidence.version) -or
        $Evidence.version -lt 180000 -or $Evidence.version -ge 190000 -or $Evidence.listenAddresses -isnot [string] -or
        -not(Test-DangoAccountSyncB1bString $Evidence.listenAddresses '127.0.0.1') -or $Evidence.dataDirectory -isnot [string] -or
        -not(Test-DangoProfilePath $Evidence.dataDirectory $fixed.DataDirectory) -or $Evidence.migrations -isnot [Array] -or
        -not(Test-DangoAccountSyncB1bFixedValue $Evidence.migrations $fixed.PriorMigrations) -or $Evidence.userIds -isnot [Array] -or
        $Evidence.profiles -isnot [Array] -or $Evidence.userIds.Count -ne $Evidence.counts.users -or
        $Evidence.profiles.Count -ne $Evidence.counts.users){throw 'accountsync_b1b_target_rejected'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($id in $Evidence.userIds){$parsed=[Guid]::Empty
        if($id -isnot [string] -or -not [Guid]::TryParseExact($id,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
            -not(Test-DangoAccountSyncB1bString $id $parsed.ToString('D')) -or
            -not $seen.Add($id)){throw 'accountsync_b1b_target_rejected'}
        $profile=@($Evidence.profiles|Where-Object {Test-DangoAccountSyncB1bString $_.id $id})
        if($profile.Count -ne 1 -or -not(Test-DangoAccountSyncB1bObjectShape $profile[0] @('id','nicknameIsNull','version')) -or
            $profile[0].nicknameIsNull -isnot [bool] -or -not(Test-DangoAccountSyncIntegerType $profile[0].version) -or
            $profile[0].version -lt 1){throw 'accountsync_b1b_target_rejected'}
    }
}

function Assert-DangoAccountSyncB1bState {
    param($Plan,$State,$EpochState)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan
    Assert-DangoAccountSyncB1bPathText $State.DataDirectory
    if(-not(Test-DangoAccountSyncB1bObjectShape $State @('RunName','Database','Owner','DataDirectory','Port','Version')) -or
        -not(Test-DangoAccountSyncB1bObjectShape $EpochState @('Version','RecoveryEpoch')) -or
        -not(Test-DangoAccountSyncInteger $State.Version 1) -or -not(Test-DangoAccountSyncInteger $State.Port 63237) -or
        -not(Test-DangoAccountSyncInteger $EpochState.Version 1) -or -not(Test-DangoAccountSyncB1bString $State.RunName $fixed.RunName) -or
        -not(Test-DangoAccountSyncB1bString $State.Database $fixed.Database) -or -not(Test-DangoAccountSyncB1bString $State.Owner $fixed.Role) -or
        $State.DataDirectory -isnot [string] -or -not(Test-DangoProfilePath $State.DataDirectory $fixed.DataDirectory)){
        throw 'accountsync_b1b_state_rejected'}
    $epoch=[Guid]::Empty
    if($EpochState.RecoveryEpoch -isnot [string] -or -not [Guid]::TryParseExact($EpochState.RecoveryEpoch,'D',[ref]$epoch) -or
        -not(Test-DangoAccountSyncB1bString $EpochState.RecoveryEpoch $epoch.ToString('D')) -or
        $epoch -eq [Guid]::Empty){throw 'accountsync_b1b_state_rejected'}
    [pscustomobject]@{Port=63237;Epoch=$epoch}
}

function Assert-DangoAccountSyncB1bOldGraph {
    param($Before,$After)
    Assert-DangoAccountSyncB1bProtectedGraphShape $Before;Assert-DangoAccountSyncB1bProtectedGraphShape $After;Assert-DangoProfileOldGraph $Before $After
    foreach($key in @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates')){
        if($After.counts.$key-$Before.counts.$key -ne 1){throw 'accountsync_b1b_graph_delta_rejected'}}
    if($After.counts.sessions-$Before.counts.sessions -ne 2){throw 'accountsync_b1b_graph_delta_rejected'}
    $new=@($After.userIds|Where-Object {-not(Test-DangoAccountSyncB1bStringIn $_ $Before.userIds)})
    if($new.Count -ne 1){throw 'accountsync_b1b_identity_delta_rejected'}
    return [Guid]$new[0]
}

function Assert-DangoAccountSyncB1bScopedGraph {
    param($Evidence)
    $expected=@{users=1;validUsers=1;namedUsers=1;completeGraphs=1;pendingUnsentEmails=1;sessions=2;validSessions=2;revokedSessions=2}
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence @($expected.Keys))){throw 'accountsync_b1b_scoped_shape_rejected'}
    foreach($key in $expected.Keys){if(-not(Test-DangoAccountSyncInteger $Evidence.$key $expected[$key])){throw 'accountsync_b1b_scoped_graph_rejected'}}
}

function Assert-DangoAccountSyncB1bTrx {
    param([string]$Path)
    Assert-DangoAccountSyncB1bPathText $Path
    Assert-DangoProfileRegularPath $Path
    if(-not(Test-DangoAccountSyncB1bString ([IO.Path]::GetFileName($Path)) 'account-sync-b1b1.trx')){throw 'accountsync_b1b_trx_path_rejected'}
    $file=[IO.FileInfo]::new($Path);if(-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 1048576){throw 'accountsync_b1b_trx_rejected'}
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing='Prohibit';$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings);$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
    try{$xml.Load($reader)}finally{$reader.Dispose()}
    $results=@($xml.SelectNodes("//*[local-name()='UnitTestResult']"));$c=$xml.SelectSingleNode("//*[local-name()='Counters']")
    if($results.Count -ne 1 -or -not(Test-DangoAccountSyncB1bString $results[0].outcome 'Passed') -or
        -not(Test-DangoAccountSyncB1bString $results[0].testName 'RealHttpsCoordinatorsHandleConcurrencyLostResponsesAndHostRecovery') -or $null -eq $c -or
        -not(Test-DangoAccountSyncB1bString $c.total '1') -or -not(Test-DangoAccountSyncB1bString $c.executed '1') -or
        -not(Test-DangoAccountSyncB1bString $c.passed '1') -or -not(Test-DangoAccountSyncB1bString $c.failed '0') -or
        -not(Test-DangoAccountSyncB1bString $c.notExecuted '0')){
        throw 'accountsync_b1b_trx_rejected'}
}

function Assert-DangoAccountSyncB1bAttempts {
    param($Plan,[Guid]$OwnerId,$Rows)
    $fixed=Assert-DangoAccountSyncB1bFixedPlan $Plan;$used=@{};$sequence=0
    if(@($Rows).Count -ne 17){throw 'accountsync_b1b_attempt_count_rejected'}
    foreach($row in $Rows){$sequence++;$parsed=[Guid]::Empty
        if(-not(Test-DangoAccountSyncB1bObjectShape $row @('Version','OwnerId','Sequence','Operation','Used','AtUtc')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence $sequence) -or
            -not(Test-DangoAccountSyncB1bString $row.OwnerId $OwnerId.ToString('D')) -or
            -not(Test-DangoAccountSyncB1bStringIn $row.Operation @($fixed.Budgets.Keys)) -or
            -not(Test-DangoAccountSyncTimestamp $row.AtUtc)){throw 'accountsync_b1b_attempt_identity_rejected'}
        $count=if($used.ContainsKey($row.Operation)){$used[$row.Operation]+1}else{1}
        if($count -gt $fixed.Budgets[$row.Operation] -or -not(Test-DangoAccountSyncInteger $row.Used $count)){
            throw 'accountsync_b1b_attempt_budget_rejected'}
        $used[$row.Operation]=$count
    }
    foreach($key in $fixed.Budgets.Keys){if($used[$key] -ne $fixed.Budgets[$key]){throw 'accountsync_b1b_attempt_budget_rejected'}}
    [pscustomobject]@{Total=17;Operations=$used}
}

function Assert-DangoAccountSyncB1bResources {
    param([Guid]$OwnerId,$Rows)
    $phases=@('TLSCreated','HostStarted','HostDisposed','HostStarted','HostDisposed','TLSDisposed')
    if(@($Rows).Count -ne 6){throw 'accountsync_b1b_resource_count_rejected'}
    for($i=0;$i -lt 6;$i++){$row=$Rows[$i];$parsed=[Guid]::Empty
        if(-not(Test-DangoAccountSyncB1bObjectShape $row @('Version','OwnerId','Sequence','Phase','Facts')) -or
            -not(Test-DangoAccountSyncInteger $row.Version 1) -or -not(Test-DangoAccountSyncInteger $row.Sequence ($i+1)) -or
            -not(Test-DangoAccountSyncB1bString $row.OwnerId $OwnerId.ToString('D')) -or
            -not(Test-DangoAccountSyncB1bString $row.Phase $phases[$i])){throw 'accountsync_b1b_resource_order_rejected'}
        $fields=switch -CaseSensitive ($row.Phase){TLSCreated{@('Port','CertificateSha256')};HostStarted{@('Index','Port','WorkersDisabled')};
            HostDisposed{@('Index','Stopped','Disposed','InFlightRequests','LifecycleTasksSettled')};TLSDisposed{@('NativeKeyIdentityCaptured','NativeCleanupConfirmed','NativeProbeFailed')}}
        if(-not(Test-DangoAccountSyncB1bObjectShape $row.Facts $fields)){throw 'accountsync_b1b_resource_shape_rejected'}
    }
    $tls=$Rows[0].Facts
    if(-not(Test-DangoAccountSyncIntegerType $tls.Port) -or $tls.Port -lt 1 -or $tls.Port -gt 65535 -or
        $tls.CertificateSha256 -isnot [string] -or $tls.CertificateSha256 -cnotmatch '\A[A-F0-9]{64}\z'){throw 'accountsync_b1b_tls_resource_rejected'}
    foreach($index in @(1,2)){$start=$Rows[$index*2-1].Facts;$stop=$Rows[$index*2].Facts
        if(-not(Test-DangoAccountSyncInteger $start.Index $index) -or -not(Test-DangoAccountSyncInteger $start.Port $tls.Port) -or
            -not(Test-DangoAccountSyncBoolean $start.WorkersDisabled $true) -or -not(Test-DangoAccountSyncInteger $stop.Index $index) -or
            -not(Test-DangoAccountSyncBoolean $stop.Stopped $true) -or -not(Test-DangoAccountSyncBoolean $stop.Disposed $true) -or
            -not(Test-DangoAccountSyncInteger $stop.InFlightRequests 0) -or -not(Test-DangoAccountSyncBoolean $stop.LifecycleTasksSettled $true)){
            throw 'accountsync_b1b_host_cleanup_rejected'}}
    $disposed=$Rows[5].Facts
    if(-not(Test-DangoAccountSyncBoolean $disposed.NativeKeyIdentityCaptured $true) -or
        -not(Test-DangoAccountSyncBoolean $disposed.NativeCleanupConfirmed $true) -or
        -not(Test-DangoAccountSyncBoolean $disposed.NativeProbeFailed $false)){throw 'accountsync_b1b_native_cleanup_rejected'}
    [pscustomobject]@{HttpsHosts=2;Certificates=1;DesktopHosts=0;NativeCleanupConfirmed=$true;SameOriginConfirmed=$true}
}

function Assert-DangoAccountSyncB1bJourney {
    param($Evidence,[Guid]$OwnerId,[Guid]$UserId,[Guid]$Epoch)
    $fields=@('Version','OwnerId','Passed','Final','ConfigurationAttempts','ConfigurationSuccessfulWrites','LoginWrites','ProfileWrites','LogoutWrites',
        'HttpsHosts','Certificates','HttpRequests','DurationSeconds','UsesSystemTrust','UsesWindowsCredentials','ActualFormalHost','ActualApplicationHost',
        'ActualWpfFileAdapter','Exact5Cases','Cases')
    $finalFields=@('UserId','PetId','Nickname','ProfileVersion','ConfigurationVersion','Sessions','Revoked','Attempts','MailState','DefaultRoomId','RecoveryEpoch')
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence $fields) -or -not(Test-DangoAccountSyncB1bObjectShape $Evidence.Final $finalFields)){
        throw 'accountsync_b1b_journey_shape_rejected'}
    foreach($entry in @{Version=1;ConfigurationAttempts=8;ConfigurationSuccessfulWrites=6;LoginWrites=2;ProfileWrites=1;LogoutWrites=2;HttpsHosts=2;Certificates=1}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncInteger $Evidence.($entry.Key) $entry.Value)){throw 'accountsync_b1b_journey_rejected'}}
    foreach($entry in @{Passed=$true;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;ActualApplicationHost=$false;
        ActualWpfFileAdapter=$true;Exact5Cases=$true}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncBoolean $Evidence.($entry.Key) $entry.Value)){throw 'accountsync_b1b_journey_rejected'}}
    foreach($entry in @{ProfileVersion=2;ConfigurationVersion=7;Sessions=2;Revoked=2;Attempts=0}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncInteger $Evidence.Final.($entry.Key) $entry.Value)){throw 'accountsync_b1b_journey_rejected'}}
    foreach($entry in @{OwnerId=$OwnerId;UserId=$UserId;RecoveryEpoch=$Epoch;PetId=[Guid]::Empty;DefaultRoomId=[Guid]::Empty}.GetEnumerator()){
        $value=if(Test-DangoAccountSyncB1bString $entry.Key 'OwnerId'){$Evidence.OwnerId}else{$Evidence.Final.($entry.Key)};$parsed=[Guid]::Empty
        if($value -isnot [string] -or -not [Guid]::TryParseExact($value,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
            -not(Test-DangoAccountSyncB1bString $value $parsed.ToString('D')) -or
            ($entry.Value -ne [Guid]::Empty -and $parsed -ne $entry.Value)){throw 'accountsync_b1b_journey_rejected'}}
    if($OwnerId -eq [Guid]::Empty -or $UserId -eq [Guid]::Empty -or $Epoch -eq [Guid]::Empty -or
        -not(Test-DangoAccountSyncB1bString $Evidence.Final.Nickname '异常同步团子') -or
        -not(Test-DangoAccountSyncB1bString $Evidence.Final.MailState 'Pending') -or
        -not(Test-DangoAccountSyncIntegerType $Evidence.HttpRequests) -or $Evidence.HttpRequests -lt 1 -or $Evidence.HttpRequests -gt 128){
        throw 'accountsync_b1b_journey_rejected'}
    $duration=$Evidence.DurationSeconds
    if(($duration -isnot [double] -and $duration -isnot [decimal] -and $duration -isnot [int] -and $duration -isnot [long]) -or
        [double]::IsNaN([double]$duration) -or [double]::IsInfinity([double]$duration) -or $duration -lt 0 -or $duration -ge 600){throw 'accountsync_b1b_journey_rejected'}
    $names=@('CAS','LostAcceptedResponse','LostResponseThenNewerCommit','UnforwardedAttempt','HostRecovery')
    $attempts=@(2,1,2,2,1);$writes=@(1,1,2,1,1);$versions=@(2,3,5,6,7)
    if($Evidence.Cases -isnot [Array] -or $Evidence.Cases.Count -ne 5){throw 'accountsync_b1b_case_rejected'}
    for($i=0;$i -lt 5;$i++){$case=$Evidence.Cases[$i]
        if(-not(Test-DangoAccountSyncB1bObjectShape $case @('Case','Attempts','SuccessfulWrites','FinalVersion','Passed')) -or
            -not(Test-DangoAccountSyncB1bString $case.Case $names[$i]) -or -not(Test-DangoAccountSyncInteger $case.Attempts $attempts[$i]) -or
            -not(Test-DangoAccountSyncInteger $case.SuccessfulWrites $writes[$i]) -or -not(Test-DangoAccountSyncInteger $case.FinalVersion $versions[$i]) -or
            -not(Test-DangoAccountSyncBoolean $case.Passed $true)){throw 'accountsync_b1b_case_rejected'}}
}

function Assert-DangoAccountSyncB1bArchives {
    param($Evidence,[Guid]$UserId,[Guid]$PetId)
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence @('Count','ExactOriginalsAndReceipts','UserId','PetId','Archives')) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Count 12) -or -not(Test-DangoAccountSyncBoolean $Evidence.ExactOriginalsAndReceipts $true) -or
        -not(Test-DangoAccountSyncB1bString $Evidence.UserId $UserId.ToString('D')) -or
        -not(Test-DangoAccountSyncB1bString $Evidence.PetId $PetId.ToString('D')) -or $Evidence.Archives -isnot [Array] -or
        $Evidence.Archives.Count -ne 12){throw 'accountsync_b1b_archive_rejected'}
    $steps=@('cas-primary','cas-secondary','cas-loser-follow','lost-accepted-target-satisfied','lost-accepted-secondary-follow',
        'lost-accepted-superseded','newer-secondary-save','superseded-primary-follow','unforwarded-original-version-retry',
        'retried-secondary-follow','host-recovery-newest-save','host-recovery-secondary-follow')
    $devices=@('primary','secondary','either','primary','secondary','primary','secondary','primary','primary','secondary','primary','secondary')
    $versions=@(1,1,2,2,3,3,4,5,5,6,6,7);$observed=@(2,2,2,3,3,5,5,5,6,6,7,7)
    $choices=@(0,0,1,0,1,0,0,1,0,1,0,1);$outcomes=@(-1,-1,0,2,0,3,1,0,1,0,1,0)
    $journalIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for($i=0;$i -lt 12;$i++){$row=$Evidence.Archives[$i];$parsed=[Guid]::Empty
        if(-not(Test-DangoAccountSyncB1bObjectShape $row @('Device','Step','JournalId','LocalOriginalSha256','LocalConfigurationSha256','CloudConfigurationSha256','ConfigurationVersion','Choice','Outcome','ObservedCloudVersion','FailureCode')) -or
            -not(Test-DangoAccountSyncB1bStringIn $row.Device @('primary','secondary')) -or
            (-not(Test-DangoAccountSyncB1bString $devices[$i] 'either') -and -not(Test-DangoAccountSyncB1bString $row.Device $devices[$i])) -or
            -not(Test-DangoAccountSyncB1bString $row.Step $steps[$i]) -or
            $row.JournalId -isnot [string] -or -not [Guid]::TryParseExact($row.JournalId,'D',[ref]$parsed) -or $parsed -eq [Guid]::Empty -or
            -not(Test-DangoAccountSyncB1bString $row.JournalId $parsed.ToString('D')) -or
            -not $journalIds.Add($row.JournalId) -or $row.LocalOriginalSha256 -isnot [string] -or $row.LocalOriginalSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or
            $row.LocalConfigurationSha256 -isnot [string] -or $row.LocalConfigurationSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or
            $row.CloudConfigurationSha256 -isnot [string] -or $row.CloudConfigurationSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or
            -not(Test-DangoAccountSyncInteger $row.ConfigurationVersion $versions[$i]) -or -not(Test-DangoAccountSyncInteger $row.Choice $choices[$i]) -or
            -not(Test-DangoAccountSyncInteger $row.ObservedCloudVersion $observed[$i]) -or
            ($i -ge 2 -and -not(Test-DangoAccountSyncInteger $row.Outcome $outcomes[$i])) -or
            ($i -lt 2 -and (-not(Test-DangoAccountSyncIntegerType $row.Outcome) -or $row.Outcome -notin @(1,3)))){throw 'accountsync_b1b_archive_rejected'}
        if($row.Outcome -eq 3){if(-not(Test-DangoAccountSyncB1bString $row.FailureCode 'pet_configuration_conflict')){throw 'accountsync_b1b_archive_rejected'}}
        elseif($null -ne $row.FailureCode){throw 'accountsync_b1b_archive_rejected'}
    }
    if(($Evidence.Archives[0].Outcome+$Evidence.Archives[1].Outcome) -ne 4 -or
        -not(Test-DangoAccountSyncB1bString $Evidence.Archives[2].Device $(if($Evidence.Archives[0].Outcome -eq 3){'primary'}else{'secondary'}))){
        throw 'accountsync_b1b_archive_rejected'}
}

function Assert-DangoAccountSyncB1bFaultEvidence {
    param($Evidence,[Guid]$OwnerId)
    $controlFields=@('ActivePhase','Failed','Disposed','ActiveOperations','PendingConnectionTasks','CasArrivals','CasCompleted','BackendPids',
        'AcceptedDropHits','AcceptedHoldHits','BeforeForwardDropHits','Events')
    if(-not(Test-DangoAccountSyncB1bObjectShape $Evidence @('Version','OwnerId','Completed','Controls','PatchAttempts','CoordinatorsStopped','DispatcherOperationsSettled')) -or
        -not(Test-DangoAccountSyncInteger $Evidence.Version 1) -or -not(Test-DangoAccountSyncB1bString $Evidence.OwnerId $OwnerId.ToString('D')) -or
        -not(Test-DangoAccountSyncBoolean $Evidence.Completed $true) -or -not(Test-DangoAccountSyncBoolean $Evidence.CoordinatorsStopped $true) -or
        -not(Test-DangoAccountSyncBoolean $Evidence.DispatcherOperationsSettled $true) -or -not(Test-DangoAccountSyncB1bObjectShape $Evidence.Controls $controlFields)){
        throw 'accountsync_b1b_fault_evidence_rejected'}
    $controls=$Evidence.Controls
    if($null -ne $controls.ActivePhase -or -not(Test-DangoAccountSyncBoolean $controls.Failed $false) -or
        -not(Test-DangoAccountSyncBoolean $controls.Disposed $false)){throw 'accountsync_b1b_fault_evidence_rejected'}
    foreach($entry in @{ActiveOperations=0;PendingConnectionTasks=0;CasArrivals=2;CasCompleted=2;AcceptedDropHits=1;AcceptedHoldHits=1;BeforeForwardDropHits=1}.GetEnumerator()){
        if(-not(Test-DangoAccountSyncInteger $controls.($entry.Key) $entry.Value)){throw 'accountsync_b1b_fault_evidence_rejected'}}
    if($controls.BackendPids -isnot [Array] -or $controls.BackendPids.Count -ne 2 -or
        -not(Test-DangoAccountSyncIntegerType $controls.BackendPids[0]) -or -not(Test-DangoAccountSyncIntegerType $controls.BackendPids[1]) -or
        $controls.BackendPids[0] -le 0 -or $controls.BackendPids[1] -le 0 -or $controls.BackendPids[0] -eq $controls.BackendPids[1] -or
        $Evidence.PatchAttempts -isnot [Array] -or $Evidence.PatchAttempts.Count -ne 8){throw 'accountsync_b1b_fault_evidence_rejected'}
    $devices=@('either','either','primary','primary','secondary','primary','primary','primary')
    $versions=@(1,1,2,3,4,5,5,6);$eyes=@(111,114,118,125,128,137,137,139);$accepted=@(-1,-1,3,4,5,0,6,7)
    for($i=0;$i -lt 8;$i++){$patch=$Evidence.PatchAttempts[$i]
        if(-not(Test-DangoAccountSyncB1bObjectShape $patch @('Sequence','Device','ExpectedVersion','EyeSize','ConfigurationSha256','Forwarded','ResponseStatus','AcceptedVersion')) -or
            -not(Test-DangoAccountSyncInteger $patch.Sequence ($i+1)) -or $patch.Device -isnot [string] -or
            ($i -ge 2 -and -not(Test-DangoAccountSyncB1bString $patch.Device $devices[$i])) -or
            ($i -lt 2 -and -not(Test-DangoAccountSyncB1bStringIn $patch.Device @('primary','secondary'))) -or
            -not(Test-DangoAccountSyncInteger $patch.ExpectedVersion $versions[$i]) -or
            -not(Test-DangoAccountSyncInteger $patch.EyeSize $(if($i -lt 2){if(Test-DangoAccountSyncB1bString $patch.Device 'primary'){111}else{114}}else{$eyes[$i]})) -or
            $patch.ConfigurationSha256 -isnot [string] -or $patch.ConfigurationSha256 -cnotmatch '\A[A-F0-9]{64}\z' -or
            -not(Test-DangoAccountSyncBoolean $patch.Forwarded ($i -ne 5))){throw 'accountsync_b1b_fault_evidence_rejected'}
        if($i -lt 2){
            if(-not(Test-DangoAccountSyncIntegerType $patch.ResponseStatus) -or $patch.ResponseStatus -notin @(200,409) -or
                ($patch.ResponseStatus -eq 200 -and -not(Test-DangoAccountSyncInteger $patch.AcceptedVersion 2)) -or
                ($patch.ResponseStatus -eq 409 -and $null -ne $patch.AcceptedVersion)){throw 'accountsync_b1b_fault_evidence_rejected'}
        }elseif($i -eq 5){if($null -ne $patch.ResponseStatus -or $null -ne $patch.AcceptedVersion){throw 'accountsync_b1b_fault_evidence_rejected'}}
        elseif(-not(Test-DangoAccountSyncInteger $patch.ResponseStatus 200) -or -not(Test-DangoAccountSyncInteger $patch.AcceptedVersion $accepted[$i])){
            throw 'accountsync_b1b_fault_evidence_rejected'}
    }
    if((Test-DangoAccountSyncB1bString $Evidence.PatchAttempts[0].Device $Evidence.PatchAttempts[1].Device) -or
        $Evidence.PatchAttempts[0].ResponseStatus+$Evidence.PatchAttempts[1].ResponseStatus -ne 609 -or
        -not [string]::Equals($Evidence.PatchAttempts[5].ConfigurationSha256,$Evidence.PatchAttempts[6].ConfigurationSha256,[StringComparison]::Ordinal) -or
        @($Evidence.PatchAttempts.ConfigurationSha256|Sort-Object -Unique).Count -ne 7){throw 'accountsync_b1b_fault_evidence_rejected'}
    if($controls.Events -isnot [Array] -or $controls.Events.Count -lt 16 -or $controls.Events.Count -gt 64){throw 'accountsync_b1b_fault_events_rejected'}
    $counts=@{};$sequence=0;$phaseOrder=@('cas','drop-accepted','hold-accepted','drop-before-forward');$lastPhase=-1
    $forwarded=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $responded=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $arrived=[Collections.Generic.HashSet[int]]::new();$released=[Collections.Generic.HashSet[int]]::new()
    $allowed=@('cas/armed','cas/patch-forwarding','cas/connection-open-started','cas/before-locks-arrived','cas/before-locks-released','cas/patch-response',
        'drop-accepted/armed','drop-accepted/accepted-response-captured','drop-accepted/accepted-response-discarded',
        'hold-accepted/armed','hold-accepted/accepted-response-captured','hold-accepted/accepted-response-released','hold-accepted/accepted-response-discarded',
        'drop-before-forward/armed','drop-before-forward/reserved-patch-not-forwarded')
    foreach($event in $controls.Events){$sequence++
        if(-not(Test-DangoAccountSyncB1bObjectShape $event @('Sequence','Phase','Event','Device','BackendPid','StatusCode'))){throw 'accountsync_b1b_fault_events_rejected'}
        $key=$event.Phase+'/'+$event.Event
        if(-not(Test-DangoAccountSyncInteger $event.Sequence $sequence) -or $event.Phase -isnot [string] -or $event.Event -isnot [string] -or
            -not(Test-DangoAccountSyncB1bStringIn $key $allowed) -or
            ($null -ne $event.Device -and -not(Test-DangoAccountSyncB1bStringIn $event.Device @('primary','secondary'))) -or
            ($null -ne $event.BackendPid -and (-not(Test-DangoAccountSyncIntegerType $event.BackendPid) -or $event.BackendPid -notin $controls.BackendPids)) -or
            ($null -ne $event.StatusCode -and (-not(Test-DangoAccountSyncIntegerType $event.StatusCode) -or $event.StatusCode -notin @(200,409)))){
            throw 'accountsync_b1b_fault_events_rejected'}
        $phaseIndex=$phaseOrder.IndexOf($event.Phase)
        if($phaseIndex -lt $lastPhase -or $phaseIndex -gt $lastPhase+1 -or
            ($phaseIndex -gt $lastPhase -and -not(Test-DangoAccountSyncB1bString $event.Event 'armed'))){throw 'accountsync_b1b_fault_events_rejected'}
        $lastPhase=$phaseIndex
        if(Test-DangoAccountSyncB1bString $event.Event 'armed'){
            if($null -ne $event.Device -or $null -ne $event.BackendPid -or $null -ne $event.StatusCode){throw 'accountsync_b1b_fault_events_rejected'}
        }elseif(Test-DangoAccountSyncB1bString $event.Phase 'cas'){
            if(Test-DangoAccountSyncB1bString $event.Event 'connection-open-started'){
                if($null -ne $event.Device -or $null -ne $event.BackendPid -or $null -ne $event.StatusCode){throw 'accountsync_b1b_fault_events_rejected'}
            }elseif(Test-DangoAccountSyncB1bStringIn $event.Event @('before-locks-arrived','before-locks-released')){
                if($null -ne $event.Device -or $null -eq $event.BackendPid -or $null -ne $event.StatusCode){throw 'accountsync_b1b_fault_events_rejected'}
                if(Test-DangoAccountSyncB1bString $event.Event 'before-locks-arrived'){
                    if(-not $arrived.Add($event.BackendPid) -or $released.Count -ne 0){throw 'accountsync_b1b_fault_events_rejected'}
                }elseif($arrived.Count -ne 2 -or -not $arrived.Contains($event.BackendPid) -or -not $released.Add($event.BackendPid)){
                    throw 'accountsync_b1b_fault_events_rejected'}
            }else{
                if($null -eq $event.Device -or $null -ne $event.BackendPid){throw 'accountsync_b1b_fault_events_rejected'}
                if(Test-DangoAccountSyncB1bString $event.Event 'patch-forwarding'){
                    if($null -ne $event.StatusCode -or -not $forwarded.Add($event.Device)){throw 'accountsync_b1b_fault_events_rejected'}
                }else{
                    $patch=@($Evidence.PatchAttempts[0..1]|Where-Object{Test-DangoAccountSyncB1bString $_.Device $event.Device})[0]
                    if($null -eq $event.StatusCode -or $event.StatusCode -ne $patch.ResponseStatus -or
                        -not $forwarded.Contains($event.Device) -or -not $responded.Add($event.Device)){throw 'accountsync_b1b_fault_events_rejected'}
                }
            }
        }else{
            $captured=Test-DangoAccountSyncB1bString $event.Event 'accepted-response-captured'
            if(-not(Test-DangoAccountSyncB1bString $event.Device 'primary') -or $null -ne $event.BackendPid -or
                ($captured -and -not(Test-DangoAccountSyncInteger $event.StatusCode 200)) -or
                (-not $captured -and $null -ne $event.StatusCode)){throw 'accountsync_b1b_fault_events_rejected'}
        }
        $counts[$key]=if($counts.ContainsKey($key)){$counts[$key]+1}else{1}
    }
    $required=@{'cas/armed'=1;'cas/patch-forwarding'=2;'cas/connection-open-started'=2;'cas/before-locks-arrived'=2;'cas/before-locks-released'=2;
        'drop-accepted/armed'=1;'drop-accepted/accepted-response-captured'=1;'drop-accepted/accepted-response-discarded'=1;
        'hold-accepted/armed'=1;'hold-accepted/accepted-response-captured'=1;'hold-accepted/accepted-response-released'=1;'hold-accepted/accepted-response-discarded'=1;
        'drop-before-forward/armed'=1;'drop-before-forward/reserved-patch-not-forwarded'=1}
    foreach($key in $required.Keys){if(-not $counts.ContainsKey($key) -or $counts[$key] -ne $required[$key]){throw 'accountsync_b1b_fault_events_rejected'}}
    if($forwarded.Count -ne 2 -or $arrived.Count -ne 2 -or $released.Count -ne 2 -or $responded.Count -gt 2 -or
        $controls.Events.Count -ne 18+$responded.Count){throw 'accountsync_b1b_fault_events_rejected'}
    foreach($phaseName in @('drop-accepted','hold-accepted','drop-before-forward')){
        $phaseEvents=@($controls.Events|Where-Object{Test-DangoAccountSyncB1bString $_.Phase $phaseName}).Event
        $expectedEvents=switch -CaseSensitive ($phaseName){'drop-accepted'{@('armed','accepted-response-captured','accepted-response-discarded')};
            'hold-accepted'{@('armed','accepted-response-captured','accepted-response-released','accepted-response-discarded')};
            'drop-before-forward'{@('armed','reserved-patch-not-forwarded')}}
        if(-not(Test-DangoAccountSyncB1bFixedValue $phaseEvents $expectedEvents)){throw 'accountsync_b1b_fault_events_rejected'}
    }
}

function Assert-DangoAccountSyncB1bAuxAgreement {
    param($Archives,$FaultEvidence)
    $patches=$FaultEvidence.PatchAttempts;$rows=$Archives.Archives
    $primary=@($patches[0..1]|Where-Object{Test-DangoAccountSyncB1bString $_.Device 'primary'})[0]
    $secondary=@($patches[0..1]|Where-Object{Test-DangoAccountSyncB1bString $_.Device 'secondary'})[0]
    $winner=@($patches[0..1]|Where-Object{$_.ResponseStatus -eq 200})[0]
    $loser=if(Test-DangoAccountSyncB1bString $winner.Device 'primary'){$secondary}else{$primary}
    $cloudDigests=@{2=$winner.ConfigurationSha256;3=$patches[2].ConfigurationSha256;4=$patches[3].ConfigurationSha256;
        5=$patches[4].ConfigurationSha256;6=$patches[6].ConfigurationSha256;7=$patches[7].ConfigurationSha256}
    $localDigests=@($primary.ConfigurationSha256,$secondary.ConfigurationSha256,$loser.ConfigurationSha256,
        $patches[2].ConfigurationSha256,$winner.ConfigurationSha256,$patches[3].ConfigurationSha256,$patches[4].ConfigurationSha256,
        $patches[3].ConfigurationSha256,$patches[6].ConfigurationSha256,$patches[4].ConfigurationSha256,$patches[7].ConfigurationSha256,$patches[6].ConfigurationSha256)
    for($i=0;$i -lt 12;$i++){
        if(-not [string]::Equals($rows[$i].LocalConfigurationSha256,$localDigests[$i],[StringComparison]::Ordinal) -or
            ($rows[$i].ConfigurationVersion -ge 2 -and
                -not [string]::Equals($rows[$i].CloudConfigurationSha256,$cloudDigests[[int]$rows[$i].ConfigurationVersion],[StringComparison]::Ordinal))){
            throw 'accountsync_b1b_aux_configuration_rejected'}
    }
    if(-not [string]::Equals($rows[0].CloudConfigurationSha256,$rows[1].CloudConfigurationSha256,[StringComparison]::Ordinal) -or
        $rows[0].Outcome -ne $(if($primary.ResponseStatus -eq 200){1}else{3}) -or
        $rows[1].Outcome -ne $(if($secondary.ResponseStatus -eq 200){1}else{3})){
        throw 'accountsync_b1b_aux_configuration_rejected'}
}

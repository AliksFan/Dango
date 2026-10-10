Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DangoProfilePlan {
    param([string]$RunName, [string]$PostgresBinDirectory, [string]$Repository)
    if ($RunName -cne 'run20261008b0') { throw 'profile_run_target_rejected' }
    $expectedBin = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'
    if (-not (Test-DangoProfilePath $PostgresBinDirectory $expectedBin)) { throw 'profile_run_binary_target_rejected' }
    $state = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    return [pscustomobject]@{
        Version=1; Stage='AccountProfileA'; RunName=$RunName; Executed=$false
        StateDirectory=$state; DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261010profilea4')
        Database='dango_backend_b0'; Role='dango_b0_owner'; PostgresBinDirectory=$expectedBin
        Repository=[IO.Path]::GetFullPath($Repository)
        Migration='20261010055529_AccountProfile'
        Filter='FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests'
        ExpectedTests=7; AccountGraphs=9; LoginAttempts=10; ProfileAttempts=19; RevokeAttempts=2
        Budgets=[ordered]@{ Invite=9; Start=9; Confirm=9; Complete=9; Login=10; DirectProfileUpdate=11;
            HttpProfilePatch=8; SessionRevoke=1; HttpLogout=1 }
        CaseAttempts=[ordered]@{ concurrent_profile=7; duplicate_names=14; rollback_after_profile=6;
            rollback_before_commit=6; cross_session=13; expiry=6; https=15 }
        CaseBudgets=@{ concurrent_profile=@{ Invite=1; Start=1; Confirm=1; Complete=1; Login=1; DirectProfileUpdate=2 };
            duplicate_names=@{ Invite=2; Start=2; Confirm=2; Complete=2; Login=2; DirectProfileUpdate=4 };
            rollback_after_profile=@{ Invite=1; Start=1; Confirm=1; Complete=1; Login=1; DirectProfileUpdate=1 };
            rollback_before_commit=@{ Invite=1; Start=1; Confirm=1; Complete=1; Login=1; DirectProfileUpdate=1 };
            cross_session=@{ Invite=2; Start=2; Confirm=2; Complete=2; Login=2; DirectProfileUpdate=2; SessionRevoke=1 };
            expiry=@{ Invite=1; Start=1; Confirm=1; Complete=1; Login=1; DirectProfileUpdate=1 };
            https=@{ Invite=1; Start=1; Confirm=1; Complete=1; Login=2; HttpProfilePatch=8; HttpLogout=1 } }
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity')
        Writes='one exact forward migration and this seven-case class once; existing data retained; no retry, reset, SMTP or room lifecycle work'
    }
}

function Test-DangoProfilePath {
    param([string]$Actual, [string]$Expected)
    return [string]::Equals([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Actual)),
        [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Expected)), [StringComparison]::OrdinalIgnoreCase)
}

function Get-DangoProfileDiagnosticPlan {
    param([string]$RunName, [string]$PostgresBinDirectory, [string]$Repository)
    if ($RunName -cne 'run20261008b0') { throw 'profile_run_diagnostic_target_rejected' }
    $expectedBin=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'
    if (-not (Test-DangoProfilePath $PostgresBinDirectory $expectedBin)) { throw 'profile_run_binary_target_rejected' }
    $state=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango/BackendDevelopment/run20261008b0'
    return [pscustomobject]@{
        Version=1; Stage='AccountProfileTargetDiagnostic'; RunName=$RunName; Executed=$false
        StateDirectory=$state; DataDirectory=(Join-Path $state 'pgdata')
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261010profiled1')
        Database='dango_backend_b0'; Role='dango_b0_owner'; PostgresBinDirectory=$expectedBin
        Repository=[IO.Path]::GetFullPath($Repository)
        PriorMigrations=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle',
            '20261009031410_RoomManagement','20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity')
        ProfileMigration='20261010055529_AccountProfile'
        MaximumToolProcesses=5; ReadOnlyQueries=1; BusinessWriteAttempts=0; ExpectedTests=0
    }
}

function Assert-DangoProfileApproval {
    param([bool]$Approved)
    if (-not $Approved) { throw 'profile_run_explicit_approval_required' }
}

function Get-DangoProfileReviewInputs {
    param($Plan)
    $files=@('src/Dango.Server/bin/Release/net10.0/Dango.Server.dll',
        'tests/Dango.Server.Tests/bin/Release/net10.0/Dango.Server.Tests.dll',
        'tests/Dango.Server.Tests/Accounts/AccountProfilePostgresTests.cs',
        'tests/Dango.Server.Tests/Accounts/ProfileTestSupport.cs','tests/Dango.Server.Tests/Accounts/ProfileTestOwner.cs',
        'src/Dango.Server/Persistence/Migrations/20261010055529_AccountProfile.cs',
        'eng/invoke-account-profile-postgres-tests.ps1','eng/Dango.AccountProfilePostgres.psm1')
    foreach ($relativeRoot in @('src/Dango.Server/bin/Release/net10.0','tests/Dango.Server.Tests/bin/Release/net10.0')) {
        $root=Join-Path $Plan.Repository $relativeRoot
        Assert-DangoProfileRegularPath $root
        if (-not [IO.Directory]::Exists($root)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
            Assert-DangoProfileRegularPath $file.FullName
            if (-not $file.PSIsContainer -and $file.Extension.ToLowerInvariant() -cin @('.dll','.json','.exe')) {
                $files+=[IO.Path]::GetRelativePath($Plan.Repository,$file.FullName).Replace('\','/')
            }
        }
    }
    $files=@($files | Sort-Object -Unique)
    if ($files.Count -gt 1024) { throw 'profile_run_review_input_bound_rejected' }
    $hashes=[ordered]@{}; $missing=@()
    foreach ($relative in $files) {
        $path=Join-Path $Plan.Repository $relative
        Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { $missing+=$relative; continue }
        $hashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{ Version=1; Stage='AccountProfileA'; RunName='run20261008b0';
        Migration=$Plan.Migration; Filter=$Plan.Filter; ExpectedTests=7; Hashes=$hashes; MissingInputs=$missing }
}

function Assert-DangoProfileManifestPath {
    param($Plan, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'profile_run_approval_manifest_required' }
    $physical=[IO.Path]::GetFullPath($Path)
    $root=[IO.Path]::GetFullPath((Join-Path $Plan.Repository 'artifacts'))+[IO.Path]::DirectorySeparatorChar
    if (-not $physical.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw 'profile_run_manifest_path_rejected' }
    Assert-DangoProfileRegularPath $physical
    return $physical
}

function New-DangoProfileApprovalManifest {
    param($Plan, [string]$Path)
    $physical=Assert-DangoProfileManifestPath $Plan $Path
    $inputs=Get-DangoProfileReviewInputs $Plan
    if ($inputs.MissingInputs.Count -ne 0) { throw 'profile_run_review_inputs_missing' }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    return [pscustomobject]@{ Path=$physical; Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash; Inputs=$inputs }
}

function Assert-DangoProfileReviewInputs {
    param($Plan, [string]$Path, [string]$ApprovalManifestSha256)
    if ($ApprovalManifestSha256 -cnotmatch '^[A-F0-9]{64}$') { throw 'profile_run_approval_digest_required' }
    $physical=Assert-DangoProfileManifestPath $Plan $Path
    $file=[IO.FileInfo]::new($physical)
    if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 65536) { throw 'profile_run_approval_manifest_invalid' }
    # The approved digest comes from the user's reviewed request, not from this mutable file.
    $bytes=[IO.File]::ReadAllBytes($physical)
    $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    if ($digest -cne $ApprovalManifestSha256) { throw 'profile_run_approval_manifest_changed' }
    $approved=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    $current=Get-DangoProfileReviewInputs $Plan
    if ($approved.Version -ne 1 -or $approved.Stage -cne 'AccountProfileA' -or $approved.RunName -cne $Plan.RunName -or
        $approved.Migration -cne $Plan.Migration -or $approved.Filter -cne $Plan.Filter -or $approved.ExpectedTests -ne 7 -or
        @($approved.MissingInputs).Count -ne 0 -or $current.MissingInputs.Count -ne 0 -or
        @($approved.Hashes.PSObject.Properties).Count -ne $current.Hashes.Count) { throw 'profile_run_approval_manifest_invalid' }
    foreach ($relative in $current.Hashes.Keys) {
        $property=$approved.Hashes.PSObject.Properties[$relative]
        if ($null -eq $property -or $property.Value -cnotmatch '^[A-F0-9]{64}$' -or
            $property.Value -cne $current.Hashes[$relative]) { throw 'profile_run_approved_input_changed' }
    }
    return [pscustomobject]@{ Path=$physical; Sha256=$digest; Inputs=$current }
}

function Assert-DangoProfileRegularPath {
    param([string]$Path)
    for ($part = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path)); $null -ne $part; $part = $part.Parent) {
        if ($part.Exists -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'profile_run_reparse_rejected' }
    }
    if ([IO.File]::Exists($Path) -and ([IO.File]::GetAttributes($Path) -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'profile_run_reparse_rejected'
    }
}

function Assert-DangoProfileState {
    param($Plan, $State, $EpochState)
    $epoch=[Guid]::Empty
    if ($State.Version -ne 1 -or $State.RunName -cne $Plan.RunName -or $State.Database -cne $Plan.Database -or
        $State.Owner -cne $Plan.Role -or -not (Test-DangoProfilePath $State.DataDirectory $Plan.DataDirectory) -or
        $State.Port -isnot [long] -and $State.Port -isnot [int] -or $State.Port -lt 1 -or $State.Port -gt 65535 -or
        $EpochState.Version -ne 1 -or -not [Guid]::TryParse([string]$EpochState.RecoveryEpoch, [ref]$epoch) -or
        $epoch -eq [Guid]::Empty) { throw 'profile_run_state_rejected' }
    return [pscustomobject]@{ Port=[int]$State.Port; Epoch=$epoch }
}

function New-DangoProfileProcessInfo {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [hashtable]$EnvironmentValues=@{})
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$Executable; $info.WorkingDirectory=$WorkingDirectory
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.RedirectStandardInput=$true
    # Clear inherited stage grants and formal configuration before adding this exact child's values.
    foreach ($name in @($info.Environment.Keys)) {
        if ($name.StartsWith('DANGO_', [StringComparison]::OrdinalIgnoreCase) -or
            $name.StartsWith('PG', [StringComparison]::OrdinalIgnoreCase)) { [void]$info.Environment.Remove($name) }
    }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    foreach ($name in $EnvironmentValues.Keys) { $info.Environment[$name]=[string]$EnvironmentValues[$name] }
    if ($EnvironmentValues.ContainsKey('PGCLIENTENCODING') -and $EnvironmentValues['PGCLIENTENCODING'] -ceq 'UTF8') {
        $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false,$true)
        $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false,$true)
    }
    return $info
}

function ConvertTo-DangoProfileUtc {
    param($Value)
    if ($Value -is [DateTime]) {
        if ($Value.Kind -ne [DateTimeKind]::Utc) { throw 'profile_run_utc_time_required' }
        return $Value
    }
    if ($Value -is [DateTimeOffset]) {
        if ($Value.Offset -ne [TimeSpan]::Zero) { throw 'profile_run_utc_time_required' }
        return $Value.UtcDateTime
    }
    $parsed=[DateTimeOffset]::MinValue
    if ($Value -isnot [string] -or $Value -cnotmatch '(?:Z|\+00:00)$' -or
        -not [DateTimeOffset]::TryParse($Value,[Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None,[ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) {
        throw 'profile_run_utc_time_required'
    }
    return $parsed.UtcDateTime
}

function Get-DangoProfileProcessIdentity {
    param($Process, [string]$ExpectedExecutable)
    Get-DangoProfileNativeType
    $errorCode=0; $image=$null; $nativePid=0; $createdAt=[DateTime]::MinValue
    try {
        # Query the retained handle returned by Start(), not a reopened PID or module list.
        $image=[DangoProfileRunNativeV2]::QueryImage($Process.SafeHandle,[ref]$errorCode)
        if ($errorCode -eq 0) {
            $nativePid=[DangoProfileRunNativeV2]::IdentityPid($Process.SafeHandle,[ref]$createdAt,[ref]$errorCode)
        }
    } catch { $errorCode=if ($_.Exception -is [ComponentModel.Win32Exception]) { $_.Exception.NativeErrorCode } else { -1 } }
    $verified=$errorCode -eq 0 -and -not [string]::IsNullOrEmpty($image) -and $nativePid -gt 0 -and
        $createdAt.Kind -eq [DateTimeKind]::Utc -and (Test-DangoProfilePath $image $ExpectedExecutable)
    return [pscustomobject]@{ Verified=$verified; ImagePath=$image; NativeErrorCode=$errorCode; Pid=$nativePid; CreatedAt=$createdAt }
}

function Test-DangoProfileToolIdentity {
    param($Identity, [int]$PidValue, [DateTime]$CreatedAt, [string]$Executable)
    try {
        return $Identity.Verified -eq $true -and $Identity.NativeErrorCode -eq 0 -and $Identity.Pid -eq $PidValue -and
            (ConvertTo-DangoProfileUtc $Identity.CreatedAt) -eq $CreatedAt -and
            (Test-DangoProfilePath $Identity.ImagePath $Executable)
    } catch { return $false }
}

function Get-DangoProfileExitedTime {
    param($Process)
    try { return ConvertTo-DangoProfileUtc $Process.ExitTime.ToUniversalTime() }
    catch { return $null }
}

function Test-DangoProfileFixedValue {
    param($Actual, $Expected)
    if ($null -eq $Expected) { return $null -eq $Actual }
    if ($null -eq $Actual) { return $false }
    if ($Expected -is [Collections.IDictionary]) {
        if ($Actual -isnot [Collections.IDictionary] -or $Actual.Count -ne $Expected.Count) { return $false }
        foreach ($key in $Expected.Keys) {
            if (@($Actual.Keys) -cnotcontains $key -or -not (Test-DangoProfileFixedValue $Actual[$key] $Expected[$key])) { return $false }
        }
        return $true
    }
    if ($Expected -is [Array]) {
        if ($Actual -isnot [Array] -or $Actual.Count -ne $Expected.Count) { return $false }
        for ($index=0; $index -lt $Expected.Count; $index++) {
            if (-not (Test-DangoProfileFixedValue $Actual[$index] $Expected[$index])) { return $false }
        }
        return $true
    }
    return $Actual.GetType() -eq $Expected.GetType() -and $Actual -ceq $Expected
}

function Get-DangoProfileFixedPlan {
    param($Plan)
    try {
        if ($null -eq $Plan) { throw 'rejected' }
        $fixed=if ($Plan.Stage -ceq 'AccountProfileA') {
            Get-DangoProfilePlan 'run20261008b0' $Plan.PostgresBinDirectory $Plan.Repository
        } elseif ($Plan.Stage -ceq 'AccountProfileTargetDiagnostic') {
            Get-DangoProfileDiagnosticPlan 'run20261008b0' $Plan.PostgresBinDirectory $Plan.Repository
        } else { throw 'rejected' }
        if (@($Plan.PSObject.Properties).Count -ne @($fixed.PSObject.Properties).Count) { throw 'rejected' }
        foreach ($property in $fixed.PSObject.Properties) {
            $actual=$Plan.PSObject.Properties[$property.Name]
            if ($null -eq $actual -or $actual.Name -cne $property.Name -or
                -not (Test-DangoProfileFixedValue $actual.Value $property.Value)) { throw 'rejected' }
        }
        return $fixed
    } catch { throw 'profile_run_fixed_plan_rejected' }
}

function Assert-DangoProfileStartOutputContract {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, $Plan, [int]$Port,
        [hashtable]$EnvironmentValues, [bool]$KillOwnedTree)
    try {
        if ($Port -lt 1 -or $Port -gt 65535 -or $EnvironmentValues.Count -ne 0 -or $KillOwnedTree) { throw 'rejected' }
        $fixed=Get-DangoProfileFixedPlan $Plan
        if (-not (Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'pg_ctl.exe')) -or
            -not (Test-DangoProfilePath $WorkingDirectory $fixed.Repository)) { throw 'rejected' }
        $expected=@('start','-D',$fixed.DataDirectory,'-l',(Join-Path $fixed.EvidenceDirectory 'postgresql.log'),
            '-o',"-h 127.0.0.1 -p $Port",'-w','-t','30')
        if (-not (Test-DangoProfileFixedValue $Arguments ([string[]]$expected))) { throw 'rejected' }
    } catch { throw 'profile_run_start_output_contract_rejected' }
}

function Assert-DangoProfileEvidenceOutputContract {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, $Plan, [int]$Port,
        [hashtable]$EnvironmentValues, [bool]$KillOwnedTree)
    try {
        if ($Port -lt 1 -or $Port -gt 65535 -or $KillOwnedTree -or $EnvironmentValues.Count -ne 2 -or
            @($EnvironmentValues.Keys) -cnotcontains 'PGPASSWORD' -or @($EnvironmentValues.Keys) -cnotcontains 'PGCLIENTENCODING' -or
            $EnvironmentValues['PGCLIENTENCODING'] -isnot [string] -or $EnvironmentValues['PGCLIENTENCODING'] -cne 'UTF8' -or
            $EnvironmentValues['PGPASSWORD'] -isnot [string] -or $EnvironmentValues['PGPASSWORD'] -cnotmatch '^[A-Za-z0-9+/]{43}=$') { throw 'rejected' }
        $decoded=$null
        try {
            $decoded=[Convert]::FromBase64String($EnvironmentValues['PGPASSWORD'])
            if ($decoded.Length -ne 32 -or [Convert]::ToBase64String($decoded) -cne $EnvironmentValues['PGPASSWORD']) { throw 'rejected' }
        } finally { if ($null -ne $decoded) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($decoded) } }
        $fixed=Get-DangoProfileFixedPlan $Plan
        if ($fixed.Stage -cne 'AccountProfileA') { throw 'rejected' }
        if (-not (Test-DangoProfilePath $Executable (Join-Path $fixed.PostgresBinDirectory 'psql.exe')) -or
            -not (Test-DangoProfilePath $WorkingDirectory $fixed.Repository)) { throw 'rejected' }
        $expected=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$Port,
            '-U',$fixed.Role,'-d',$fixed.Database,'-c',(Get-DangoProfileDatabaseSql))
        if (-not (Test-DangoProfileFixedValue $Arguments ([string[]]$expected))) { throw 'rejected' }
    } catch { throw 'profile_run_evidence_output_contract_rejected' }
}

function Get-DangoProfileReadFailureCode {
    param($OutputTask, $ErrorTask)
    $overflow=$false
    foreach ($task in @($OutputTask,$ErrorTask)) {
        if ($null -eq $task) { continue }
        if ($task.IsCanceled) { return 'profile_run_tool_output_read_failed' }
        if ($task.IsFaulted) {
            $causes=@($task.Exception.Flatten().InnerExceptions)
            if ($causes.Count -ne 1 -or $causes[0] -isnot [DangoProfileOutputLimitExceptionV1]) { return 'profile_run_tool_output_read_failed' }
            $overflow=$true
        }
    }
    if ($overflow) { return 'profile_run_tool_output_bound_exceeded' }
    return $null
}

function Wait-DangoProfileReadCompletion {
    param($OutputTask, $ErrorTask)
    try {
        foreach ($task in @($OutputTask,$ErrorTask)) {
            if (-not $task.Wait(1000)) { throw 'profile_run_tool_output_incomplete' }
        }
        return $OutputTask.GetAwaiter().GetResult()
    } catch {
        $failure=Get-DangoProfileReadFailureCode $OutputTask $ErrorTask
        if ($null -ne $failure) { throw $failure }
        if ($_.Exception.Message -ceq 'profile_run_tool_output_incomplete') { throw 'profile_run_tool_output_incomplete' }
        throw 'profile_run_tool_output_read_failed'
    }
}

function Set-DangoProfileProcessExited {
    param($Process, $Receipt)
    # Capture actual exit evidence immediately, before output or cleanup can fail.
    $Receipt.Exited=$true
    $Receipt.ExitedAt=Get-DangoProfileExitedTime $Process
    $Receipt.ExitCode=$Process.ExitCode
    $Receipt.ExitCodeRecorded=$true
}

function Close-DangoProfileProcessOutput {
    param($OutputReader, $ErrorReader, $OutputTask, $ErrorTask, $PreviousClose)
    $facts=if ($null -ne $PreviousClose) { $PreviousClose } else {
        [pscustomobject]@{ StandardOutputTaskSettled=$false; StandardErrorTaskSettled=$false;
            StandardOutputClosed=$false; StandardErrorClosed=$false;
            StandardOutputCloseFailed=$false; StandardErrorCloseFailed=$false;
            StandardOutputCloseSkippedPending=$false; StandardErrorCloseSkippedPending=$false;
            OutputReaderTasksSettled=$false; OutputReadersClosed=$false; OutputStreamsClosed=$false;
            OutputResourcesCleanupConfirmed=$false }
    }
    $facts.StandardOutputTaskSettled=$null -eq $OutputTask -or $OutputTask.IsCompleted
    $facts.StandardErrorTaskSettled=$null -eq $ErrorTask -or $ErrorTask.IsCompleted
    $facts.StandardOutputCloseSkippedPending=-not $facts.StandardOutputTaskSettled
    $facts.StandardErrorCloseSkippedPending=-not $facts.StandardErrorTaskSettled
    # No new close task is created. Never call synchronous Dispose while a read
    # is pending; an exited process does not establish reader or pipe cleanup.
    if ($facts.StandardOutputTaskSettled -and $null -ne $OutputReader -and
        -not $facts.StandardOutputClosed -and -not $facts.StandardOutputCloseFailed) {
        try { $OutputReader.Dispose(); $facts.StandardOutputClosed=$true }
        catch { $facts.StandardOutputCloseFailed=$true }
    }
    if ($facts.StandardErrorTaskSettled -and $null -ne $ErrorReader -and
        -not $facts.StandardErrorClosed -and -not $facts.StandardErrorCloseFailed) {
        try { $ErrorReader.Dispose(); $facts.StandardErrorClosed=$true }
        catch { $facts.StandardErrorCloseFailed=$true }
    }
    # A failed StreamReader.Dispose can already mark the reader disposed before
    # BaseStream.Close fails. A second no-op Dispose is not proof of closure.
    $facts.OutputReaderTasksSettled=$facts.StandardOutputTaskSettled -and $facts.StandardErrorTaskSettled
    $facts.OutputReadersClosed=$facts.StandardOutputClosed -and $facts.StandardErrorClosed
    $facts.OutputStreamsClosed=$facts.OutputReadersClosed
    $facts.OutputResourcesCleanupConfirmed=$facts.OutputReaderTasksSettled -and $facts.OutputReadersClosed
    return $facts
}

function Set-DangoProfileOutputCloseFacts {
    param($Receipt, $Facts)
    foreach ($property in $Facts.PSObject.Properties) { $Receipt.($property.Name)=$property.Value }
}

function Set-DangoProfileReadCloseSnapshot {
    param($Receipt, $OutputTask, $ErrorTask)
    if ($Receipt.OutputCloseSnapshotTaken) { return }
    if ($null -ne $OutputTask) { $Receipt.StandardOutputTaskStateBeforeDispose=[string]$OutputTask.Status }
    if ($null -ne $ErrorTask) { $Receipt.StandardErrorTaskStateBeforeDispose=[string]$ErrorTask.Status }
    if ($null -ne $Receipt.StandardOutputCapture) { $Receipt.StandardOutputFailureBeforeDispose=$Receipt.StandardOutputCapture.FailureKind }
    if ($null -ne $Receipt.StandardErrorCapture) { $Receipt.StandardErrorFailureBeforeDispose=$Receipt.StandardErrorCapture.FailureKind }
    $Receipt.OutputCloseSnapshotTaken=$true
}

function Invoke-DangoProfileProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory,
        [hashtable]$EnvironmentValues=@{}, [ValidateRange(1,300)][int]$TimeoutSeconds=30,
        [switch]$KillOwnedTree,
        [Collections.Generic.List[object]]$Receipts,
        [scriptblock]$IdentityProbe={ param($process,$expected) Get-DangoProfileProcessIdentity $process $expected },
        [scriptblock]$ProcessFactory={ param($info) $process=[Diagnostics.Process]::new(); $process.StartInfo=$info; return $process },
        [ValidateSet('CompleteEof','PostgresStartIgnored','PostgresEvidenceJson',IgnoreCase=$false)][string]$OutputContract='CompleteEof',
        $PostgresStartPlan, [int]$PostgresStartPort=0,
        $PostgresEvidencePlan, [int]$PostgresEvidencePort=0)
    $ignoreStartOutput=$OutputContract -ceq 'PostgresStartIgnored'
    $evidenceOutput=$OutputContract -ceq 'PostgresEvidenceJson'
    if ($ignoreStartOutput) {
        if ($null -ne $PostgresEvidencePlan -or $PostgresEvidencePort -ne 0) { throw 'profile_run_start_output_contract_rejected' }
        Assert-DangoProfileStartOutputContract $Executable $Arguments $WorkingDirectory $PostgresStartPlan $PostgresStartPort `
            $EnvironmentValues ([bool]$KillOwnedTree)
    } elseif ($evidenceOutput) {
        if ($null -ne $PostgresStartPlan -or $PostgresStartPort -ne 0) { throw 'profile_run_evidence_output_contract_rejected' }
        Assert-DangoProfileEvidenceOutputContract $Executable $Arguments $WorkingDirectory $PostgresEvidencePlan $PostgresEvidencePort `
            $EnvironmentValues ([bool]$KillOwnedTree)
    } elseif ($null -ne $PostgresStartPlan -or $PostgresStartPort -ne 0 -or
        $null -ne $PostgresEvidencePlan -or $PostgresEvidencePort -ne 0) { throw 'profile_run_start_output_contract_rejected' }
    # Compile the Win32 query before launch; a short-lived tool can exit during Add-Type.
    Get-DangoProfileNativeType
    $info=New-DangoProfileProcessInfo $Executable $Arguments $WorkingDirectory $EnvironmentValues
    $process=& $ProcessFactory $info
    $receipt=[pscustomobject]@{ Capability=[Guid]::NewGuid(); Pid=0; CreatedAt=$null; Executable=$Executable; Arguments=@($Arguments);
        Started=$false; StartAttempted=$false; StartReturned=$false; Failure=$null;
        IdentityVerified=$false; ImageQueryError=0; InitialImageQueryError=$null; FinalImageQueryError=$null;
        Exited=$false; ExitedAt=$null; ExitCode=$null; ExitCodeRecorded=$false; TimedOut=$false; CleanupConfirmed=$false;
        OutputContract=$OutputContract; ReaderTasksStarted=0; OutputCaptured=$false; OutputComplete=$false;
        OutputDiscarded=$ignoreStartOutput; StandardOutputClosed=$false; StandardErrorClosed=$false; OutputStreamsClosed=$false;
        StandardOutputCapture=$null; StandardErrorCapture=$null; StandardOutputTaskState='NotStarted'; StandardErrorTaskState='NotStarted';
        StandardOutputTaskStateBeforeDispose='NotStarted'; StandardErrorTaskStateBeforeDispose='NotStarted';
        StandardOutputFailureBeforeDispose='None'; StandardErrorFailureBeforeDispose='None'; FinalReadFailureCode=$null;
        OutputCloseSnapshotTaken=$false; StandardOutputTaskSettled=$true; StandardErrorTaskSettled=$true;
        StandardOutputCloseFailed=$false; StandardErrorCloseFailed=$false;
        StandardOutputCloseSkippedPending=$false; StandardErrorCloseSkippedPending=$false;
        OutputReaderTasksSettled=$true; OutputReadersClosed=$false; OutputResourcesCreated=$false;
        OutputResourcesCleanupConfirmed=$true; ProcessDisposeConfirmed=$false }
    if ($null -ne $Receipts) { $Receipts.Add($receipt) }
    $timer=[Diagnostics.Stopwatch]::new(); $output=$null; $error=$null
    $outputReader=$null; $errorReader=$null; $outputClose=$null; $hadFailure=$false
    try {
        $receipt.StartAttempted=$true; $receipt.OutputResourcesCleanupConfirmed=$false
        $started=$process.Start(); $receipt.StartReturned=$true
        if (-not $started) { $receipt.OutputResourcesCleanupConfirmed=$true; throw 'profile_run_tool_start_failed' }
        $receipt.Started=$true; $receipt.OutputResourcesCreated=$true; $timer.Start()
        $receipt.Pid=$process.Id
        $receipt.CreatedAt=$process.StartTime.ToUniversalTime()
        $identity=& $IdentityProbe $process $Executable
        $receipt.InitialImageQueryError=$identity.NativeErrorCode
        $receipt.ImageQueryError=$identity.NativeErrorCode
        $receipt.IdentityVerified=Test-DangoProfileToolIdentity $identity $receipt.Pid $receipt.CreatedAt $Executable
        if (-not $receipt.IdentityVerified) { throw 'profile_run_tool_identity_unconfirmed' }
        try { $outputReader=$process.StandardOutput; $errorReader=$process.StandardError }
        catch { throw 'profile_run_tool_output_read_failed' }
        try { $process.StandardInput.Close() } catch { throw 'profile_run_tool_input_close_failed' }
        if (-not $ignoreStartOutput) {
            $stdoutLimit=if ($evidenceOutput) { 1048576 } else { 131072 }
            $stdoutByteLimit=if ($evidenceOutput) { 1048576 } else { 0 }
            $receipt.StandardOutputCapture=[DangoProfileOutputCaptureV1]::new($stdoutLimit,$stdoutByteLimit)
            $receipt.StandardErrorCapture=[DangoProfileOutputCaptureV1]::new(131072)
            try {
                $output=[DangoProfileRunNativeV2]::ReadBoundedAsync($outputReader,$stdoutLimit,$receipt.StandardOutputCapture)
                $receipt.ReaderTasksStarted++
                $error=[DangoProfileRunNativeV2]::ReadBoundedAsync($errorReader,131072,$receipt.StandardErrorCapture)
                $receipt.ReaderTasksStarted++
            } catch { throw 'profile_run_tool_output_read_failed' }
        }
        $exited=$process.WaitForExit(100)
        while (-not $exited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds -and
            ($ignoreStartOutput -or (-not $output.IsFaulted -and -not $error.IsFaulted -and -not $output.IsCanceled -and -not $error.IsCanceled))) {
            $exited=$process.WaitForExit(100)
        }
        if (-not $exited) {
            $readFailure=if (-not $ignoreStartOutput) { Get-DangoProfileReadFailureCode $output $error } else { $null }
            $receipt.TimedOut=$null -eq $readFailure
            if ($null -ne $readFailure) { throw $readFailure }
            throw 'profile_run_tool_timeout'
        }
        Set-DangoProfileProcessExited $process $receipt
        $receipt.CleanupConfirmed=$true
        if ($ignoreStartOutput) {
            Set-DangoProfileReadCloseSnapshot $receipt $output $error
            $outputClose=Close-DangoProfileProcessOutput $outputReader $errorReader $output $error
            Set-DangoProfileOutputCloseFacts $receipt $outputClose
            if (-not $receipt.OutputResourcesCleanupConfirmed) { throw 'profile_run_tool_output_close_unconfirmed' }
            return [pscustomobject]@{ ExitCode=$receipt.ExitCode; Output=''; Receipt=$receipt }
        }
        $text=Wait-DangoProfileReadCompletion $output $error
        $receipt.OutputCaptured=$true; $receipt.OutputComplete=$true
        Set-DangoProfileReadCloseSnapshot $receipt $output $error
        $outputClose=Close-DangoProfileProcessOutput $outputReader $errorReader $output $error
        Set-DangoProfileOutputCloseFacts $receipt $outputClose
        if (-not $receipt.OutputResourcesCleanupConfirmed) { throw 'profile_run_tool_output_close_unconfirmed' }
        return [pscustomobject]@{ ExitCode=$receipt.ExitCode; Output=$text.Trim(); Receipt=$receipt }
    } catch {
        $hadFailure=$true
        $receipt.Failure=if ($_.Exception.Message -match '^profile_run_[a-z0-9_]+$') { $_.Exception.Message } else { 'profile_run_operation_failed' }
        throw
    } finally {
        Set-DangoProfileReadCloseSnapshot $receipt $output $error
        if ($receipt.Started) {
            try {
                # Even a failed initial identity guard must close this launch window before
                # the caller observes descendants; Dispose alone never stops a started tool.
                while (-not $receipt.Exited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
                    if ($process.WaitForExit(100)) { Set-DangoProfileProcessExited $process $receipt }
                }
                $identity=& $IdentityProbe $process $Executable
                $receipt.FinalImageQueryError=$identity.NativeErrorCode
                $receipt.ImageQueryError=$identity.NativeErrorCode
                $currentIdentity=Test-DangoProfileToolIdentity $identity $receipt.Pid $receipt.CreatedAt $Executable
                if ($currentIdentity) { $receipt.IdentityVerified=$true }
                elseif (-not $receipt.Exited) { $receipt.IdentityVerified=$false }
                if (-not $receipt.Exited -and $currentIdentity -and $process.Id -eq $receipt.Pid -and
                    $process.StartTime.ToUniversalTime() -eq $receipt.CreatedAt) {
                    $process.Kill([bool]$KillOwnedTree)
                    if ($process.WaitForExit(5000)) { Set-DangoProfileProcessExited $process $receipt }
                }
                $receipt.CleanupConfirmed=$receipt.Exited
            } catch { $receipt.CleanupConfirmed=$false }
        }
        if ($receipt.Started) {
            # Observe after this owned control process has ended, without adding
            # a shutdown task or extending the business-output success deadline.
            foreach ($task in @($output,$error)) {
                if ($null -ne $task -and -not $task.IsCompleted) { try { [void]$task.Wait(1000) } catch { } }
            }
            # Identity/input guards can fail before either reader property was
            # accessed. Retain any available reference before Process clears it.
            if ($null -eq $outputReader) { try { $outputReader=$process.StandardOutput } catch { } }
            if ($null -eq $errorReader) { try { $errorReader=$process.StandardError } catch { } }
            $outputClose=Close-DangoProfileProcessOutput $outputReader $errorReader $output $error $outputClose
            Set-DangoProfileOutputCloseFacts $receipt $outputClose
        }
        try { $process.Dispose(); $receipt.ProcessDisposeConfirmed=$true }
        catch {
            $receipt.CleanupConfirmed=$false
            if (-not $hadFailure) { $receipt.Failure='profile_run_tool_dispose_unconfirmed'; throw 'profile_run_tool_dispose_unconfirmed' }
        }
        if ($null -ne $output) { $receipt.StandardOutputTaskState=[string]$output.Status }
        if ($null -ne $error) { $receipt.StandardErrorTaskState=[string]$error.Status }
        $receipt.StandardOutputTaskSettled=$null -eq $output -or $output.IsCompleted
        $receipt.StandardErrorTaskSettled=$null -eq $error -or $error.IsCompleted
        $receipt.OutputReaderTasksSettled=$receipt.StandardOutputTaskSettled -and $receipt.StandardErrorTaskSettled
        if ($receipt.OutputResourcesCreated) {
            $receipt.OutputResourcesCleanupConfirmed=$receipt.OutputReaderTasksSettled -and $receipt.OutputReadersClosed
        }
        $receipt.FinalReadFailureCode=Get-DangoProfileReadFailureCode $output $error
    }
}

function Get-DangoProfileWindowsStartCommand {
    param($Plan, [int]$Port)
    $cmd=Join-Path ([Environment]::SystemDirectory) 'cmd.exe'
    $postgres=(Join-Path $Plan.PostgresBinDirectory 'postgres.exe').Replace('\','/')
    $data=$Plan.DataDirectory.Replace('\','/')
    $log=Join-Path $Plan.EvidenceDirectory 'postgresql.log'
    return '"'+$cmd+'" /C ""'+$postgres+'" -D "'+$data+'" -h 127.0.0.1 -p '+$Port+' < "nul" >> "'+$log+'" 2>&1"'
}

function Test-DangoProfilePostgresProof {
    param($Proof, [string]$Executable, [string]$DataDirectory, [int]$Port,
        $AttemptedAt, $ReturnedAt, $Plan)
    try {
        $attempt=ConvertTo-DangoProfileUtc $AttemptedAt; $returned=ConvertTo-DangoProfileUtc $ReturnedAt
        $pgCreated=ConvertTo-DangoProfileUtc $Proof.CreatedAt; $controlCreated=ConvertTo-DangoProfileUtc $Proof.ControlCreatedAt
        $bridge=$Proof.ShellBridge; $bridgeCreated=ConvertTo-DangoProfileUtc $bridge.CreatedAt
        if ($null -eq $Plan -or $attempt -gt $returned -or $Proof.Pid -le 0 -or $Proof.ControlPid -le 0 -or
            -not $Proof.ControlIdentityVerified -or $controlCreated -lt $attempt -or $pgCreated -lt $controlCreated -or $pgCreated -gt $returned -or
            $bridge.Pid -le 0 -or $Proof.ParentProcessId -ne $bridge.Pid -or $bridge.ParentProcessId -ne $Proof.ControlPid -or
            $bridgeCreated -lt $controlCreated -or $bridgeCreated -gt $pgCreated -or
            -not (Test-DangoProfilePath $Proof.ControlExecutable (Join-Path $Plan.PostgresBinDirectory 'pg_ctl.exe')) -or
            -not (Test-DangoProfilePath $bridge.Executable (Join-Path ([Environment]::SystemDirectory) 'cmd.exe')) -or
            -not (Test-DangoProfilePath $Proof.Executable $Executable) -or $Proof.Arguments.Count -ne 7 -or
            -not (Test-DangoProfilePath $Proof.Arguments[0] $Executable)) { return $false }
        if ($null -eq $Proof.ControlExitedAt) { return $false }
        $controlExited=ConvertTo-DangoProfileUtc $Proof.ControlExitedAt
        if ($controlExited -lt $controlCreated -or $controlExited -gt $returned -or $bridgeCreated -gt $controlExited) { return $false }
        $values=@{}
        for ($index=1; $index -lt 7; $index+=2) {
            $key=$Proof.Arguments[$index]
            if ($key -cnotin @('-D','-h','-p') -or $values.ContainsKey($key)) { return $false }
            $values[$key]=$Proof.Arguments[$index+1]
        }
        if (-not (Test-DangoProfilePath $values['-D'] $DataDirectory) -or $values['-h'] -cne '127.0.0.1' -or
            $values['-p'] -cne [string]$Port -or -not (Test-DangoProfilePath $Plan.DataDirectory $DataDirectory)) { return $false }
        $expectedControl=@('start','-D',$DataDirectory,'-l',(Join-Path $Plan.EvidenceDirectory 'postgresql.log'),
            '-o',"-h 127.0.0.1 -p $Port",'-w','-t','30')
        if ($Proof.ControlArguments.Count -ne $expectedControl.Count) { return $false }
        for ($index=0; $index -lt $expectedControl.Count; $index++) {
            if ($Proof.ControlArguments[$index] -cne $expectedControl[$index]) { return $false }
        }
        $expectedCommand=Get-DangoProfileWindowsStartCommand $Plan $Port
        if (-not [string]::Equals($bridge.CommandLine,$expectedCommand,[StringComparison]::OrdinalIgnoreCase)) { return $false }
        Get-DangoProfileNativeType
        $expectedArguments=@([DangoProfileRunNativeV2]::Arguments($expectedCommand))
        if ($bridge.Arguments.Count -ne $expectedArguments.Count) { return $false }
        for ($index=0; $index -lt $expectedArguments.Count; $index++) {
            if (-not [string]::Equals($bridge.Arguments[$index],$expectedArguments[$index],[StringComparison]::OrdinalIgnoreCase)) { return $false }
        }
        return $true
    } catch { return $false }
}

function Get-DangoProfileNativeType {
    $loaded='DangoProfileRunNativeV2' -as [type]
    if ($null -ne $loaded) {
        $readerOverloads=@($loaded.GetMethods() | Where-Object { $_.Name -ceq 'ReadBoundedAsync' -and $_.GetParameters().Count -eq 3 })
        if ($null -eq ('DangoProfileOutputCaptureV1' -as [type]) -or $null -eq ('DangoProfileOutputLimitExceptionV1' -as [type]) -or
            $readerOverloads.Count -ne 1 -or $readerOverloads[0].GetParameters()[2].ParameterType.FullName -cne 'DangoProfileOutputCaptureV1') {
            throw 'profile_run_fresh_powershell_host_required'
        }
    }
    if (-not ('DangoProfileRunNativeV2' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Win32.SafeHandles;
public sealed class DangoProfileOutputLimitExceptionV1 : InvalidOperationException {
 public DangoProfileOutputLimitExceptionV1() : base("profile_run_tool_output_bound_exceeded") {}
}
public enum DangoProfileReadFailureV1 { None, OutputLimit, IoFailure, ReaderDisposed, DecodeFailure, ReadFailure, Canceled }
public sealed class DangoProfileOutputCaptureV1 {
 private long characters, utf8Bytes; private int started, eof, canceled, failure;
 public int MaximumCharacters {get;} public int MaximumUtf8Bytes {get;}
 public long ObservedCharacters => Interlocked.Read(ref characters);
 public long? ObservedUtf8Bytes => MaximumUtf8Bytes>0 ? Interlocked.Read(ref utf8Bytes) : (long?)null;
 public bool ReadStarted => Volatile.Read(ref started)!=0;
 public bool EofCompleted => Volatile.Read(ref eof)!=0;
 public bool Canceled => Volatile.Read(ref canceled)!=0;
 public string FailureKind => ((DangoProfileReadFailureV1)Volatile.Read(ref failure)).ToString();
 public DangoProfileOutputCaptureV1(int maximumCharacters) : this(maximumCharacters,0) {}
 public DangoProfileOutputCaptureV1(int maximumCharacters,int maximumUtf8Bytes) {
  if(maximumCharacters<1 || maximumUtf8Bytes<0) throw new ArgumentOutOfRangeException();
  MaximumCharacters=maximumCharacters; MaximumUtf8Bytes=maximumUtf8Bytes;
 }
 internal void Start(){Volatile.Write(ref started,1);}
 internal void Characters(int count){Interlocked.Add(ref characters,count);}
 internal void Bytes(int count){Interlocked.Add(ref utf8Bytes,count);}
 internal bool OverLimit => ObservedCharacters>MaximumCharacters || MaximumUtf8Bytes>0 && Interlocked.Read(ref utf8Bytes)>MaximumUtf8Bytes;
 internal void Eof(){Volatile.Write(ref eof,1);}
 internal void Fail(DangoProfileReadFailureV1 kind){Volatile.Write(ref failure,(int)kind);if(kind==DangoProfileReadFailureV1.Canceled)Volatile.Write(ref canceled,1);}
}
public static class DangoProfileRunNativeV2 {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder image, ref uint size);
 [DllImport("kernel32.dll", SetLastError=true)] private static extern uint GetProcessId(SafeProcessHandle process);
 [DllImport("kernel32.dll", SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
 public static string QueryImage(SafeProcessHandle process, out int error) {
  error=0; var image=new StringBuilder(32768); uint size=32768;
  if(!QueryFullProcessImageNameW(process,0,image,ref size)){error=Marshal.GetLastWin32Error();return null;}
  return image.ToString();
 }
 public static int IdentityPid(SafeProcessHandle process, out DateTime createdAt, out int error) {
  createdAt=DateTime.MinValue; error=0; var pid=GetProcessId(process);
  if(pid==0){error=Marshal.GetLastWin32Error();return 0;}
  long created, exited, kernel, user;
  if(!GetProcessTimes(process,out created,out exited,out kernel,out user)){error=Marshal.GetLastWin32Error();return 0;}
  createdAt=DateTime.FromFileTimeUtc(created);return checked((int)pid);
 }
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)] public static extern bool CreateDirectoryW(string path, IntPtr security);
 [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 private static extern IntPtr CommandLineToArgvW(string text, out int count);
 [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
 public static string[] Arguments(string text) {
  int count; var ptr=CommandLineToArgvW(text,out count); if(ptr==IntPtr.Zero) throw new InvalidOperationException();
  try { var result=new string[count]; for(int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr,i*IntPtr.Size)); return result; }
  finally { LocalFree(ptr); }
 }
 public static Task<string> ReadBoundedAsync(TextReader reader, int maximum) {
  return ReadBoundedAsync(reader,maximum,new DangoProfileOutputCaptureV1(maximum));
 }
 public static async Task<string> ReadBoundedAsync(TextReader reader, int maximum, DangoProfileOutputCaptureV1 capture) {
  if(reader==null || capture==null || maximum!=capture.MaximumCharacters) throw new ArgumentException();
  var result=new StringBuilder(); var buffer=new char[4096]; int count;
  Encoder encoder=capture.MaximumUtf8Bytes>0 ? new UTF8Encoding(false,true).GetEncoder() : null;
  var bytes=encoder!=null ? new byte[Encoding.UTF8.GetMaxByteCount(buffer.Length)] : null;
  capture.Start();
  try {
   while((count=await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))>0) {
    capture.Characters(count);
    if(encoder!=null) {
     int used, written; bool complete;
     encoder.Convert(buffer,0,count,bytes,0,bytes.Length,false,out used,out written,out complete);
     capture.Bytes(written);
     if(used!=count || !complete) throw new InvalidOperationException();
    }
    if(capture.OverLimit) throw new DangoProfileOutputLimitExceptionV1();
    result.Append(buffer,0,count);
   }
   if(encoder!=null) {
    int used, written; bool complete;
    encoder.Convert(buffer,0,0,bytes,0,bytes.Length,true,out used,out written,out complete);
    capture.Bytes(written);
    if(used!=0 || !complete) throw new InvalidOperationException();
    if(capture.OverLimit) throw new DangoProfileOutputLimitExceptionV1();
   }
   capture.Eof();
   return result.ToString();
  } catch(DangoProfileOutputLimitExceptionV1){capture.Fail(DangoProfileReadFailureV1.OutputLimit);throw;}
    catch(IOException){capture.Fail(DangoProfileReadFailureV1.IoFailure);throw;}
    catch(ObjectDisposedException){capture.Fail(DangoProfileReadFailureV1.ReaderDisposed);throw;}
    catch(DecoderFallbackException){capture.Fail(DangoProfileReadFailureV1.DecodeFailure);throw;}
    catch(EncoderFallbackException){capture.Fail(DangoProfileReadFailureV1.DecodeFailure);throw;}
    catch(OperationCanceledException){capture.Fail(DangoProfileReadFailureV1.Canceled);throw;}
    catch(Exception){capture.Fail(DangoProfileReadFailureV1.ReadFailure);throw;}
 }
}
'@
    }
}

function New-DangoProfileReservation {
    param($Plan)
    Assert-DangoProfileRegularPath $Plan.EvidenceDirectory
    Get-DangoProfileNativeType
    if (-not [DangoProfileRunNativeV2]::CreateDirectoryW($Plan.EvidenceDirectory,[IntPtr]::Zero)) {
        throw 'profile_run_already_reserved'
    }
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetOwner($sid); $acl.SetAccessRuleProtection($true,$false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $Plan.EvidenceDirectory -AclObject $acl
    $reservation=[pscustomobject]@{ Version=1; OwnerId=[Guid]::NewGuid(); Stage=$Plan.Stage; ReservedAt=[DateTime]::UtcNow;
        Plan=$Plan; WholeSuiteReserved=$true; RetryAllowed=$false }
    Write-DangoProfileJson (Join-Path $Plan.EvidenceDirectory 'reservation.json') $reservation -CreateNew
    $ownerProcess=[Diagnostics.Process]::GetCurrentProcess()
    try {
        $owner=[pscustomobject]@{ Version=1; Stage=$Plan.Stage; RunName=$Plan.RunName; OwnerId=$reservation.OwnerId;
            OwnerPid=$ownerProcess.Id; OwnerCreatedAt=$ownerProcess.StartTime.ToUniversalTime().ToString('O');
            OwnerExecutable=$ownerProcess.MainModule.FileName; RunDirectory=$Plan.EvidenceDirectory;
            ExpectedTests=7; Budgets=$Plan.Budgets }
    } finally { $ownerProcess.Dispose() }
    Write-DangoProfileJson (Join-Path $Plan.EvidenceDirectory 'owner-manifest.json') $owner -CreateNew
    foreach ($name in @('attempts.jsonl','resources.jsonl')) {
        $ledger=[IO.FileStream]::new((Join-Path $Plan.EvidenceDirectory $name),[IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
        try { $ledger.Flush($true) } finally { $ledger.Dispose() }
    }
    $reservation | Add-Member Owner $owner
    return $reservation
}

function Write-DangoProfileJson {
    param([string]$Path, $Value, [switch]$CreateNew)
    Assert-DangoProfileRegularPath $Path
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 12))
    $mode=if ($CreateNew) { [IO.FileMode]::CreateNew } else { [IO.FileMode]::Create }
    $stream=[IO.FileStream]::new($Path,$mode,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
}

function Get-DangoProfilePostgresProof {
    param($Plan, [int]$Port, [int]$ControlExitCode, $ControlReceipt)
    $pidPath=Join-Path $Plan.DataDirectory 'postmaster.pid'
    Assert-DangoProfileRegularPath $pidPath
    $lines=@([IO.File]::ReadAllLines($pidPath))
    if ($lines.Count -lt 4 -or -not (Test-DangoProfilePath $lines[1] $Plan.DataDirectory) -or
        $lines[3] -cne [string]$Port) { throw 'profile_run_postmaster_proof_rejected' }
    $ownedPid=0
    if (-not [int]::TryParse($lines[0],[ref]$ownedPid) -or $ownedPid -le 0) { throw 'profile_run_postmaster_proof_rejected' }
    Get-DangoProfileNativeType
    $process=Get-DangoProfileExistingProcessRecord $ownedPid (Join-Path $Plan.PostgresBinDirectory 'postgres.exe')
    $bridge=Get-DangoProfileExistingProcessRecord $process.ParentProcessId (Join-Path ([Environment]::SystemDirectory) 'cmd.exe')
    return [pscustomobject]@{ ControlExitCode=$ControlExitCode; Pid=$ownedPid;
        CreatedAt=$process.CreatedAt; Executable=$process.Executable;
        Arguments=$process.Arguments; ParentProcessId=$process.ParentProcessId; ShellBridge=$bridge;
        ControlPid=if ($null -ne $ControlReceipt) { $ControlReceipt.Pid } else { 0 };
        ControlCreatedAt=if ($null -ne $ControlReceipt) { $ControlReceipt.CreatedAt } else { $null };
        ControlExitedAt=if ($null -ne $ControlReceipt) { $ControlReceipt.ExitedAt } else { $null };
        ControlExecutable=if ($null -ne $ControlReceipt) { $ControlReceipt.Executable } else { '' };
        ControlArguments=if ($null -ne $ControlReceipt) { $ControlReceipt.Arguments } else { @() };
        ControlIdentityVerified=$null -ne $ControlReceipt -and $ControlReceipt.IdentityVerified }
}

function Get-DangoProfileExistingProcessRecord {
    param([int]$ProcessIdValue, [string]$ExpectedExecutable)
    $first=Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessIdValue"
    if ($null -eq $first) { throw 'profile_run_process_record_unconfirmed' }
    $handleProcess=[Diagnostics.Process]::GetProcessById($ProcessIdValue)
    try {
        $identity=Get-DangoProfileProcessIdentity $handleProcess $ExpectedExecutable
        if (-not $identity.Verified -or $identity.Pid -ne $ProcessIdValue) { throw 'profile_run_process_native_identity_unconfirmed' }
        $second=Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessIdValue"
        if ($null -eq $second -or $first.ProcessId -ne $second.ProcessId -or $first.ParentProcessId -ne $second.ParentProcessId -or
            $first.ExecutablePath -cne $second.ExecutablePath -or $first.CommandLine -cne $second.CommandLine -or
            $first.CreationDate.ToUniversalTime() -ne $second.CreationDate.ToUniversalTime() -or
            -not (Test-DangoProfilePath $second.ExecutablePath $identity.ImagePath)) { throw 'profile_run_process_record_changed' }
        # CIM DATETIME has exactly microsecond precision; compare that representation
        # to the retained native handle's timestamp without a guessed time tolerance.
        $nativeMicroseconds=$identity.CreatedAt.Ticks-($identity.CreatedAt.Ticks%10)
        if ($second.CreationDate.ToUniversalTime().Ticks -ne $nativeMicroseconds) { throw 'profile_run_process_record_changed' }
        return [pscustomobject]@{ Pid=$identity.Pid; CreatedAt=$identity.CreatedAt; Executable=$identity.ImagePath;
            ParentProcessId=$second.ParentProcessId; CommandLine=$second.CommandLine;
            Arguments=@([DangoProfileRunNativeV2]::Arguments($second.CommandLine)) }
    } finally { $handleProcess.Dispose() }
}

function Observe-DangoProfileStartup {
    param($Plan, [int]$Port, [int]$ControlExitCode, $ControlReceipt, $AttemptedAt,
        [ValidateRange(1,10)][int]$ObservationSeconds=3,
        [scriptblock]$ProofReader={ param($plan,$port,$code,$receipt) Get-DangoProfilePostgresProof $plan $port $code $receipt },
        [scriptblock]$Delay={ param($milliseconds) [Threading.Thread]::Sleep($milliseconds) },
        [scriptblock]$Clock={ [DateTime]::UtcNow })
    $started=ConvertTo-DangoProfileUtc (& $Clock)
    $until=$started; $proof=$null; $verified=$false
    # Fixed iterations also bound a broken injected clock; this is observation, never a retry grant.
    for ($iteration=0; $iteration -le ($ObservationSeconds*10); $iteration++) {
        $until=ConvertTo-DangoProfileUtc (& $Clock)
        try {
            $observed=& $ProofReader $Plan $Port $ControlExitCode $ControlReceipt
            if ($null -ne $observed) {
                $proof=$observed
                $verified=Test-DangoProfilePostgresProof $proof (Join-Path $Plan.PostgresBinDirectory 'postgres.exe') `
                    $Plan.DataDirectory $Port $AttemptedAt $until $Plan
                if ($verified) { break }
            }
        } catch { $verified=$false }
        if (($until-$started).TotalSeconds -ge $ObservationSeconds) { break }
        & $Delay 100
    }
    return [pscustomobject]@{ Proof=$proof; Verified=$verified; ObservedUntil=$until;
        ControlExited=($null -ne $ControlReceipt -and $ControlReceipt.Exited);
        # A CMD descendant can still launch after pg_ctl exits and after this observation.
        # Only a proved PostgreSQL stop closes that capability; absence alone is insufficient.
        NoResourcesConfirmed=$false }
}

function Test-DangoProfileListener {
    param([int]$Port)
    return @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $Port }).Count -ne 0
}

function Assert-DangoProfileTrx {
    param([string]$Path)
    $file=[IO.FileInfo]::new($Path)
    Assert-DangoProfileRegularPath $Path
    if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 1048576) { throw 'profile_run_trx_bound_rejected' }
    $settings=[Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try { $document=[Xml.XmlDocument]::new(); $document.XmlResolver=$null; $document.Load($reader) } finally { $reader.Dispose() }
    $names=[Xml.XmlNamespaceManager]::new($document.NameTable); $names.AddNamespace('t',$document.DocumentElement.NamespaceURI)
    $results=@($document.SelectNodes('//t:UnitTestResult',$names))
    if ($results.Count -ne 7 -or @($results | Where-Object { $_.outcome -cne 'Passed' }).Count -ne 0) {
        throw 'profile_run_seven_passes_required'
    }
    $expected=@{ IndependentConnectionsWithSameExpectedVersionProduceOneCommitAndOneConflict=1;
        DuplicateNamesAndSequentialEditsDoNotChangeAccountPetOrRoomIdentity=1; FailureAfterSaveRollsBackNicknameAndVersion=2;
        CrossAccountSessionCannotReadOrWriteAndRevocationBeforeLocksIsRechecked=1; SessionExpiryAtPrecommitRollsBackNickname=1;
        ProfileHttpsUsesCurrentSessionRejectsForeignOwnershipCsrfOriginAndMalformedBodies=1 }
    foreach ($result in $results) {
        $definition=$document.SelectSingleNode("//t:UnitTest[@id='$($result.testId)']/t:TestMethod",$names)
        if ($null -eq $definition -or $definition.className -notmatch '^Dango\.Server\.Tests\.Accounts\.AccountProfilePostgresTests(?:,|$)' -or
            -not $expected.ContainsKey($definition.name) -or $expected[$definition.name] -le 0) {
            throw 'profile_run_test_manifest_rejected'
        }
        $expected[$definition.name]--
    }
    if (@($expected.Values | Where-Object { $_ -ne 0 }).Count -ne 0) { throw 'profile_run_test_manifest_rejected' }
}

function Read-DangoProfileLedger {
    param([string]$Path, [int]$MaximumRecords)
    Assert-DangoProfileRegularPath $Path
    $file=[IO.FileInfo]::new($Path)
    if (-not $file.Exists -or $file.Length -gt ($MaximumRecords*2048)) { throw 'profile_run_ledger_bound_rejected' }
    return [IO.File]::ReadAllLines($Path)
}

function Assert-DangoProfileAttempts {
    param($Plan, [Guid]$OwnerId, [string[]]$Lines)
    if ($Lines.Count -ne 67) { throw 'profile_run_attempt_count_rejected' }
    $used=@{}; $cases=@{}; $caseOperations=@{}; $sequence=0
    foreach ($line in $Lines) {
        if ($line.Length -eq 0 -or $line.Length -gt 2048) { throw 'profile_run_attempt_record_rejected' }
        $json=[Text.Json.JsonDocument]::Parse($line)
        try {
            $properties=@($json.RootElement.EnumerateObject())
            if ($properties.Count -ne 7 -or @($properties.Name | Sort-Object -Unique).Count -ne 7 -or
                @($properties.Name | Where-Object { $_ -cnotin @('Version','OwnerId','Sequence','CaseId','Operation','Used','AtUtc') }).Count -ne 0) {
                throw 'profile_run_attempt_record_rejected'
            }
            $atUtc=$json.RootElement.GetProperty('AtUtc').GetString()
            [void]$json.RootElement.GetProperty('Version').GetInt32()
            [void]$json.RootElement.GetProperty('Sequence').GetInt32()
            [void]$json.RootElement.GetProperty('Used').GetInt32()
        } finally { $json.Dispose() }
        $record=$line | ConvertFrom-Json
        $sequence++
        if ($record.Version -ne 1 -or [string]$record.OwnerId -cne $OwnerId.ToString('D') -or
            $record.Sequence -ne $sequence -or $record.Operation -cnotin $Plan.Budgets.Keys -or
            $record.CaseId -cnotin $Plan.CaseAttempts.Keys -or
            $record.Operation -cnotin $Plan.CaseBudgets[$record.CaseId].Keys) { throw 'profile_run_attempt_record_rejected' }
        $used[$record.Operation]=[int]$used[$record.Operation]+1
        $cases[$record.CaseId]=[int]$cases[$record.CaseId]+1
        $caseOperation=$record.CaseId+'/'+$record.Operation
        $caseOperations[$caseOperation]=[int]$caseOperations[$caseOperation]+1
        if ($record.Used -ne $used[$record.Operation] -or $record.Used -gt $Plan.Budgets[$record.Operation]) {
            throw 'profile_run_attempt_record_rejected'
        }
        $time=[DateTimeOffset]::MinValue
        if ($atUtc -cnotmatch '(?:Z|\+00:00)$' -or -not [DateTimeOffset]::TryParse($atUtc,[ref]$time) -or $time.Offset -ne [TimeSpan]::Zero) {
            throw 'profile_run_attempt_record_rejected'
        }
    }
    foreach ($operation in $Plan.Budgets.Keys) {
        if ($used[$operation] -ne $Plan.Budgets[$operation]) { throw 'profile_run_attempt_budget_rejected' }
    }
    foreach ($caseId in $Plan.CaseAttempts.Keys) {
        if ($cases[$caseId] -ne $Plan.CaseAttempts[$caseId]) { throw 'profile_run_attempt_case_rejected' }
        foreach ($operation in $Plan.CaseBudgets[$caseId].Keys) {
            if ($caseOperations[$caseId+'/'+$operation] -ne $Plan.CaseBudgets[$caseId][$operation]) { throw 'profile_run_attempt_case_budget_rejected' }
        }
    }
    return [pscustomobject]@{ Total=67; Operations=$used; Cases=$cases }
}

function Get-DangoProfileDatabaseSql {
    param([switch]$MeasureOnly)
    # Hash complete old records in PostgreSQL; only irreversible digests leave the database.
    # The two newly introduced profile fields are checked separately after the migration.
    $tables=[ordered]@{ invitations='test_invitations'; registrations='pending_registrations';
        challenges='email_challenges'; emailJobs='email_jobs'; users='users'; pets='pets'; rooms='rooms';
        memberships='room_memberships'; petStates='pet_room_states'; sessions='auth_sessions';
        audit='audit_logs'; outbox='domain_outbox'; leases='room_device_leases'; codes='public_room_credentials';
        bans='room_bans'; claims='user_claims'; logins='user_logins'; tokens='user_tokens' }
    $counts=@(); $digests=@()
    foreach ($name in $tables.Keys) {
        $table=$tables[$name]
        $row=if ($name -ceq 'users') { "(to_jsonb(t) - 'nickname' - 'profile_version')" } else { 'to_jsonb(t)' }
        $counts+="'$name',(SELECT count(*) FROM dango.$table)"
        $digests+="'$name',(SELECT COALESCE(json_agg(hash ORDER BY hash),'[]'::json) FROM (SELECT encode(sha256(convert_to(${row}::text,'UTF8')),'hex') AS hash FROM dango.$table t) rows)"
    }
    $payload=@"
json_build_object('database',current_database(),'role',current_user,'version',current_setting('server_version_num')::integer,
 'dataDirectory',current_setting('data_directory'),'listenAddresses',current_setting('listen_addresses'),
 'migrations',(SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM dango.__ef_migrations_history),
 'userIds',(SELECT COALESCE(json_agg(id ORDER BY id),'[]'::json) FROM dango.users),
 'profiles',(SELECT COALESCE(json_agg(json_build_object('id',id,'nicknameIsNull',(to_jsonb(u)->>'nickname') IS NULL,
   'version',(to_jsonb(u)->>'profile_version')::bigint) ORDER BY id),'[]'::json) FROM dango.users u),
 'counts',json_build_object($($counts -join ',')),
 'digests',json_build_object($($digests -join ',')))
"@
    $expression=if ($MeasureOnly) { "json_build_object('utf8Octets',octet_length(convert_to(($payload)::text,'UTF8')))" } else { $payload }
    return @"
BEGIN READ ONLY;
SELECT $expression;
COMMIT;
"@
}

function Assert-DangoProfileDatabaseEvidenceSize {
    param($Result)
    try {
        $properties=@()
        if ($null -ne $Result) { $properties=@($Result.PSObject.Properties) }
        if ($null -eq $Result -or $Result -isnot [pscustomobject] -or $properties.Count -ne 1 -or
            $properties[0].Name -cne 'utf8Octets' -or
            ($Result.utf8Octets -isnot [int] -and $Result.utf8Octets -isnot [long]) -or
            $Result.utf8Octets -lt 1 -or $Result.utf8Octets -gt 1048574) { throw 'rejected' }
        return [pscustomobject]@{ Utf8Octets=[long]$Result.utf8Octets; MaximumUtf8Bytes=1048576; TerminatorReserve=2 }
    } catch { throw 'profile_run_database_evidence_size_rejected' }
}

function Assert-DangoProfileOldGraph {
    param($Before, $After, [switch]$RequireInitialProfiles)
    foreach ($property in $Before.counts.PSObject.Properties) {
        $name=$property.Name
        $original=@($Before.digests.$name); $current=@($After.digests.$name)
        if ($original.Count -ne [long]$property.Value -or $current.Count -ne [long]$After.counts.$name -or
            @($original | Where-Object { $_ -cnotmatch '^[a-f0-9]{64}$' }).Count -ne 0 -or
            @($current | Where-Object { $_ -cnotmatch '^[a-f0-9]{64}$' }).Count -ne 0) { throw 'profile_run_graph_evidence_rejected' }
        $remaining=[Collections.Generic.List[string]]::new()
        foreach ($hash in $current) { $remaining.Add($hash) }
        foreach ($hash in $original) {
            if (-not $remaining.Remove($hash)) { throw 'profile_run_old_graph_changed' }
        }
        if ($name -cnotin @('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions') -and
            $remaining.Count -ne 0) { throw 'profile_run_unapproved_graph_created' }
    }
    foreach ($id in @($Before.userIds)) {
        $profile=@($After.profiles | Where-Object { $_.id -ceq $id })
        if ($profile.Count -ne 1 -or ($RequireInitialProfiles -and
            ($profile[0].nicknameIsNull -ne $true -or $profile[0].version -ne 1))) { throw 'profile_run_existing_profile_initialization_rejected' }
    }
}

function Get-DangoProfileScopedSql {
    param([string[]]$UserIds, [Guid]$Epoch, [Guid]$OwnerId)
    if ($UserIds.Count -ne 9 -or @($UserIds | Sort-Object -Unique).Count -ne 9 -or $Epoch -eq [Guid]::Empty -or $OwnerId -eq [Guid]::Empty) {
        throw 'profile_run_scoped_ids_rejected'
    }
    $ids=@()
    foreach ($id in $UserIds) {
        $guid=[Guid]::Empty
        if (-not [Guid]::TryParseExact($id,'D',[ref]$guid) -or $guid -eq [Guid]::Empty) { throw 'profile_run_scoped_ids_rejected' }
        $ids+="'$($guid.ToString('D'))'::uuid"
    }
    return @"
BEGIN READ ONLY;
WITH selected AS (SELECT * FROM dango.users WHERE id IN ($($ids -join ',')))
SELECT json_build_object('users',(SELECT count(*) FROM selected),
 'validUsers',(SELECT count(*) FROM selected WHERE lower(email) LIKE 'profilea-$($OwnerId.ToString('N'))-%@example.invalid' AND email_confirmed AND disabled_at IS NULL AND NOT recovery_locked),
 'namedUsers',(SELECT count(*) FROM selected WHERE nickname IS NOT NULL),
 'profileIncrements',(SELECT sum(profile_version-1) FROM selected),
 'completeGraphs',(SELECT count(*) FROM selected u
 JOIN dango.pets p ON p.user_id=u.id AND p.configuration_version=1
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
}

function Assert-DangoProfileScopedGraph {
    param($Evidence)
    $expected=@{ users=9; validUsers=9; namedUsers=4; profileIncrements=5; completeGraphs=9;
        pendingUnsentEmails=9; sessions=10; validSessions=10; revokedSessions=2 }
    if (@($Evidence.PSObject.Properties).Count -ne $expected.Count) { throw 'profile_run_scoped_graph_rejected' }
    foreach ($name in $expected.Keys) {
        if ($Evidence.$name -ne $expected[$name]) { throw 'profile_run_scoped_graph_rejected' }
    }
}

function Assert-DangoProfileResources {
    param($Plan, [Guid]$OwnerId, [string[]]$Lines)
    if ($Lines.Count -ne 28) { throw 'profile_run_resource_count_rejected' }
    $fixtures=@{}; $caseIds=@{}; $sequence=0; $httpsReady=0
    foreach ($line in $Lines) {
        if ($line.Length -eq 0 -or $line.Length -gt 2048) { throw 'profile_run_resource_record_rejected' }
        $json=[Text.Json.JsonDocument]::Parse($line)
        try {
            $names=@($json.RootElement.EnumerateObject() | ForEach-Object { $_.Name })
            if ($names.Count -ne 10 -or @($names | Sort-Object -Unique).Count -ne 10 -or
                @($names | Where-Object { $_ -cnotin @('Version','OwnerId','Sequence','FixtureId','CaseId','Phase',
                'RequestedHttps','HttpsStarted','NativeKeyDisposeConfirmed','AtUtc') }).Count -ne 0) { throw 'profile_run_resource_record_rejected' }
            $atUtc=$json.RootElement.GetProperty('AtUtc').GetString()
        } finally { $json.Dispose() }
        $record=$line | ConvertFrom-Json
        $sequence++; $id=[Guid]::Empty; $time=[DateTimeOffset]::MinValue
        if ($record.Version -ne 1 -or [string]$record.OwnerId -cne $OwnerId.ToString('D') -or $record.Sequence -ne $sequence -or
            -not [Guid]::TryParseExact([string]$record.FixtureId,'D',[ref]$id) -or $id -eq [Guid]::Empty -or
            -not $Plan.CaseAttempts.Contains($record.CaseId) -or $record.RequestedHttps -isnot [bool] -or
            $record.HttpsStarted -isnot [bool] -or $record.NativeKeyDisposeConfirmed -isnot [bool] -or
            $atUtc -cnotmatch '(?:Z|\+00:00)$' -or -not [DateTimeOffset]::TryParse($atUtc,[ref]$time) -or $time.Offset -ne [TimeSpan]::Zero) {
            throw 'profile_run_resource_record_rejected'
        }
        $key=$id.ToString('D')
        if (-not $fixtures.ContainsKey($key)) {
            if ($record.Phase -cne 'preparing' -or $caseIds.ContainsKey($record.CaseId)) { throw 'profile_run_resource_lifecycle_rejected' }
            $fixtures[$key]=[pscustomobject]@{ CaseId=$record.CaseId; RequestedHttps=$record.RequestedHttps; Step=0 }
            $caseIds[$record.CaseId]=1
        }
        $fixture=$fixtures[$key]
        $phase=@('preparing','ready','dispose_started','disposed')
        if ($fixture.Step -ge 4 -or $record.Phase -cne $phase[$fixture.Step] -or $record.CaseId -cne $fixture.CaseId -or
            $record.RequestedHttps -ne $fixture.RequestedHttps -or
            $record.RequestedHttps -ne ($record.CaseId -ceq 'https') -or
            $record.HttpsStarted -ne ($record.RequestedHttps -and $fixture.Step -ge 1) -or
            $record.NativeKeyDisposeConfirmed -ne ($fixture.Step -eq 3)) { throw 'profile_run_resource_lifecycle_rejected' }
        if ($record.Phase -ceq 'ready' -and $record.HttpsStarted) { $httpsReady++ }
        $fixture.Step++
    }
    if ($fixtures.Count -ne 7 -or $caseIds.Count -ne 7 -or $httpsReady -ne 1 -or
        @($fixtures.Values | Where-Object { $_.Step -ne 4 }).Count -ne 0) { throw 'profile_run_resource_lifecycle_rejected' }
    return [pscustomobject]@{ Fixtures=7; ResourceRecords=28; HttpsStarted=1; Disposed=7; NativeCleanupConfirmed=$true }
}

function New-DangoProfileDiagnosticReservation {
    param($Plan)
    $fixed=Get-DangoProfileFixedPlan $Plan
    if ($fixed.Stage -cne 'AccountProfileTargetDiagnostic') { throw 'profile_run_diagnostic_plan_required' }
    Assert-DangoProfileRegularPath $fixed.EvidenceDirectory
    Get-DangoProfileNativeType
    if (-not [DangoProfileRunNativeV2]::CreateDirectoryW($fixed.EvidenceDirectory,[IntPtr]::Zero)) {
        throw 'profile_run_diagnostic_already_reserved'
    }
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl=[Security.AccessControl.DirectorySecurity]::new(); $acl.SetOwner($sid); $acl.SetAccessRuleProtection($true,$false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $fixed.EvidenceDirectory -AclObject $acl
    $reservation=[pscustomobject]@{ Version=1; OwnerId=[Guid]::NewGuid(); Stage=$fixed.Stage; ReservedAt=[DateTime]::UtcNow;
        Plan=$fixed; WholeDiagnosticReserved=$true; RetryAllowed=$false; ReadOnlyQueries=1; BusinessWriteAttempts=0 }
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'reservation.json') $reservation -CreateNew
    $ownerProcess=[Diagnostics.Process]::GetCurrentProcess()
    try {
        $owner=[pscustomobject]@{ Version=1; Stage=$fixed.Stage; RunName=$fixed.RunName; OwnerId=$reservation.OwnerId;
            OwnerPid=$ownerProcess.Id; OwnerCreatedAt=$ownerProcess.StartTime.ToUniversalTime().ToString('O');
            OwnerExecutable=$ownerProcess.MainModule.FileName; RunDirectory=$fixed.EvidenceDirectory;
            MaximumToolProcesses=5; ReadOnlyQueries=1; BusinessWriteAttempts=0; ExpectedTests=0; RetryAllowed=$false }
    } finally { $ownerProcess.Dispose() }
    Write-DangoProfileJson (Join-Path $fixed.EvidenceDirectory 'owner-manifest.json') $owner -CreateNew
    $reservation | Add-Member Owner $owner
    return $reservation
}

function Get-DangoProfileDiagnosticSql {
    return @'
BEGIN READ ONLY;
SELECT json_build_object(
 'database',current_database(),'role',current_user,'version',current_setting('server_version_num')::integer,
 'dataDirectory',current_setting('data_directory'),'listenAddresses',current_setting('listen_addresses'),
 'migrations',(SELECT COALESCE(json_agg("MigrationId" ORDER BY "MigrationId"),'[]'::json) FROM dango.__ef_migrations_history),
 'profileColumnsPresent',(SELECT count(*)=2 FROM information_schema.columns WHERE table_schema='dango'
   AND table_name='users' AND column_name IN ('nickname','profile_version')));
COMMIT;
'@
}

function Assert-DangoProfileDiagnosticEvidence {
    param([string]$Json)
    $document=$null
    try {
        if ([string]::IsNullOrWhiteSpace($Json) -or $Json.Length -gt 131072) { throw 'rejected' }
        $options=[Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth=4
        $document=[Text.Json.JsonDocument]::Parse($Json,$options)
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'rejected' }
        $properties=@($document.RootElement.EnumerateObject())
        $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $expected=@('database','role','version','dataDirectory','listenAddresses','migrations','profileColumnsPresent')
        if ($properties.Count -ne 7) { throw 'rejected' }
        foreach ($property in $properties) {
            if ($property.Name -cnotin $expected -or -not $names.Add($property.Name)) { throw 'rejected' }
        }
        $values=[ordered]@{}
        foreach ($name in @('database','role','dataDirectory','listenAddresses')) {
            $element=$document.RootElement.GetProperty($name)
            if ($element.ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'rejected' }
            $value=$element.GetString(); $maximum=if ($name -ceq 'dataDirectory') { 32767 } else { 256 }
            if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $maximum -or $value -match '\p{Cc}') { throw 'rejected' }
            $values[$name]=$value
        }
        $version=0; $versionElement=$document.RootElement.GetProperty('version')
        if ($versionElement.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
            -not $versionElement.TryGetInt32([ref]$version) -or $version -lt 0) { throw 'rejected' }
        $values['version']=$version
        $migrationsElement=$document.RootElement.GetProperty('migrations')
        if ($migrationsElement.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $migrationsElement.GetArrayLength() -gt 32) { throw 'rejected' }
        $migrations=[Collections.Generic.List[string]]::new(); $migrationNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($element in $migrationsElement.EnumerateArray()) {
            if ($element.ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'rejected' }
            $migration=$element.GetString()
            if ($migration.Length -gt 128 -or $migration -cnotmatch '^[0-9]{14}_[A-Za-z][A-Za-z0-9_]*$' -or
                -not $migrationNames.Add($migration)) { throw 'rejected' }
            $migrations.Add($migration)
        }
        $values['migrations']=[string[]]$migrations.ToArray()
        $columns=$document.RootElement.GetProperty('profileColumnsPresent')
        if ($columns.ValueKind -notin @([Text.Json.JsonValueKind]::True,[Text.Json.JsonValueKind]::False)) { throw 'rejected' }
        $values['profileColumnsPresent']=$columns.GetBoolean()
        return [pscustomobject]$values
    } catch { throw 'profile_run_diagnostic_evidence_rejected' }
    finally { if ($null -ne $document) { $document.Dispose() } }
}

function Get-DangoProfileDiagnosticMatches {
    param($Plan, $Evidence)
    $fixed=Get-DangoProfileFixedPlan $Plan
    if ($fixed.Stage -cne 'AccountProfileTargetDiagnostic') { throw 'profile_run_diagnostic_plan_required' }
    $facts=Assert-DangoProfileDiagnosticEvidence ($Evidence | ConvertTo-Json -Depth 4)
    $database=$facts.database -ceq $fixed.Database; $role=$facts.role -ceq $fixed.Role
    $version=$facts.version -ge 180000 -and $facts.version -lt 190000
    $directory=$false
    try { $directory=Test-DangoProfilePath $facts.dataDirectory $fixed.DataDirectory } catch { $directory=$false }
    $listen=$facts.listenAddresses -ceq '127.0.0.1'
    $migrations=(@($facts.migrations) -join ',') -ceq ($fixed.PriorMigrations -join ',')
    return [pscustomobject]@{ DatabaseMatches=$database; RoleMatches=$role; VersionMatches=$version;
        DataDirectoryMatches=$directory; ListenAddressesMatches=$listen; MigrationsMatch=$migrations;
        AllExpectedTargetFactsMatch=($database -and $role -and $version -and $directory -and $listen -and $migrations);
        ProfileMigrationPresent=(@($facts.migrations) -ccontains $fixed.ProfileMigration) }
}

function Get-DangoProfileDiagnosticReviewInputs {
    param($Plan)
    $fixed=Get-DangoProfileFixedPlan $Plan
    if ($fixed.Stage -cne 'AccountProfileTargetDiagnostic') { throw 'profile_run_diagnostic_plan_required' }
    $expectedHost=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
    if (-not (Test-DangoProfilePath ([Environment]::ProcessPath) $expectedHost)) { throw 'profile_run_diagnostic_bundled_host_required' }
    $paths=[ordered]@{
        'eng/Dango.AccountProfilePostgres.psm1'=(Join-Path $fixed.Repository 'eng/Dango.AccountProfilePostgres.psm1')
        'eng/invoke-account-profile-target-diagnostic.ps1'=(Join-Path $fixed.Repository 'eng/invoke-account-profile-target-diagnostic.ps1')
        'state/state.json'=(Join-Path $fixed.StateDirectory 'state.json')
        'postgres-bin/postgres.exe'=(Join-Path $fixed.PostgresBinDirectory 'postgres.exe')
        'postgres-bin/pg_ctl.exe'=(Join-Path $fixed.PostgresBinDirectory 'pg_ctl.exe')
        'postgres-bin/psql.exe'=(Join-Path $fixed.PostgresBinDirectory 'psql.exe')
        'powershell-host/pwsh.exe'=$expectedHost
    }
    Assert-DangoProfileRegularPath $fixed.PostgresBinDirectory
    $enumerated=0
    if ([IO.Directory]::Exists($fixed.PostgresBinDirectory)) {
        Get-ChildItem -LiteralPath $fixed.PostgresBinDirectory -Recurse -Force | ForEach-Object {
            $file=$_
            $enumerated++
            if ($enumerated -gt 1024) { throw 'profile_run_diagnostic_review_input_bound_rejected' }
            Assert-DangoProfileRegularPath $file.FullName
            if (-not $file.PSIsContainer -and $file.Extension.ToLowerInvariant() -ceq '.dll') {
                $relative=[IO.Path]::GetRelativePath($fixed.PostgresBinDirectory,$file.FullName).Replace('\','/')
                $paths['postgres-bin/'+$relative]=$file.FullName
            }
        }
    }
    if ($paths.Count -gt 1024) { throw 'profile_run_diagnostic_review_input_bound_rejected' }
    $hashes=[ordered]@{}; $missing=@()
    foreach ($label in @($paths.Keys | Sort-Object -CaseSensitive)) {
        $path=$paths[$label]; Assert-DangoProfileRegularPath $path
        if (-not [IO.File]::Exists($path)) { $missing+=$label; continue }
        $hashes[$label]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{ Version=1; Stage=$fixed.Stage; RunName=$fixed.RunName; MaximumToolProcesses=5;
        ReadOnlyQueries=1; BusinessWriteAttempts=0; ExpectedTests=0; Hashes=$hashes; MissingInputs=$missing }
}

function New-DangoProfileDiagnosticApprovalManifest {
    param($Plan, [string]$Path)
    $physical=Assert-DangoProfileManifestPath $Plan $Path
    $inputs=Get-DangoProfileDiagnosticReviewInputs $Plan
    if ($inputs.MissingInputs.Count -ne 0) { throw 'profile_run_diagnostic_review_inputs_missing' }
    $json=$inputs | ConvertTo-Json -Depth 12
    if ([Text.UTF8Encoding]::new($false,$true).GetByteCount($json) -gt 65536) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $physical))
    Write-DangoProfileJson $physical $inputs -CreateNew
    return [pscustomobject]@{ Path=$physical; Sha256=(Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash; Inputs=$inputs }
}

function Assert-DangoProfileDiagnosticReviewInputs {
    param($Plan, [string]$Path, [string]$ApprovalManifestSha256)
    $document=$null
    try {
        $fixed=Get-DangoProfileFixedPlan $Plan
        if ($fixed.Stage -cne 'AccountProfileTargetDiagnostic') { throw 'profile_run_diagnostic_plan_required' }
        if ($ApprovalManifestSha256 -cnotmatch '^[A-F0-9]{64}$') { throw 'profile_run_approval_digest_required' }
        $physical=Assert-DangoProfileManifestPath $fixed $Path
        $file=[IO.FileInfo]::new($physical)
        if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 65536) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        $bytes=[IO.File]::ReadAllBytes($physical)
        $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        if ($digest -cne $ApprovalManifestSha256) { throw 'profile_run_diagnostic_approval_manifest_changed' }
        $json=[Text.UTF8Encoding]::new($false,$true).GetString($bytes)
        $options=[Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth=4
        $document=[Text.Json.JsonDocument]::Parse($json,$options)
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        $properties=@($document.RootElement.EnumerateObject()); $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $expected=@('Version','Stage','RunName','MaximumToolProcesses','ReadOnlyQueries','BusinessWriteAttempts','ExpectedTests','Hashes','MissingInputs')
        if ($properties.Count -ne 9) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        foreach ($property in $properties) {
            if ($property.Name -cnotin $expected -or -not $names.Add($property.Name)) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        }
        foreach ($entry in @{ Version=1; MaximumToolProcesses=5; ReadOnlyQueries=1; BusinessWriteAttempts=0; ExpectedTests=0 }.GetEnumerator()) {
            $element=$document.RootElement.GetProperty($entry.Key); $number=0
            if ($element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or -not $element.TryGetInt32([ref]$number) -or $number -ne $entry.Value) {
                throw 'profile_run_diagnostic_approval_manifest_invalid'
            }
        }
        foreach ($entry in @{ Stage=$fixed.Stage; RunName=$fixed.RunName }.GetEnumerator()) {
            $element=$document.RootElement.GetProperty($entry.Key)
            if ($element.ValueKind -ne [Text.Json.JsonValueKind]::String -or $element.GetString() -cne $entry.Value) {
                throw 'profile_run_diagnostic_approval_manifest_invalid'
            }
        }
        $missing=$document.RootElement.GetProperty('MissingInputs')
        if ($missing.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $missing.GetArrayLength() -ne 0) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        $approvedHashes=$document.RootElement.GetProperty('Hashes')
        if ($approvedHashes.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
        $current=Get-DangoProfileDiagnosticReviewInputs $fixed
        $hashProperties=@($approvedHashes.EnumerateObject()); $hashNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($current.MissingInputs.Count -ne 0 -or $hashProperties.Count -ne $current.Hashes.Count -or $hashProperties.Count -gt 1024) {
            throw 'profile_run_diagnostic_approval_manifest_invalid'
        }
        foreach ($property in $hashProperties) {
            if (-not $hashNames.Add($property.Name) -or $property.Name -cnotin @($current.Hashes.Keys) -or
                $property.Value.ValueKind -ne [Text.Json.JsonValueKind]::String) { throw 'profile_run_diagnostic_approval_manifest_invalid' }
            $hash=$property.Value.GetString()
            if ($hash -cnotmatch '^[A-F0-9]{64}$' -or $hash -cne $current.Hashes[$property.Name]) { throw 'profile_run_diagnostic_approved_input_changed' }
        }
        return [pscustomobject]@{ Path=$physical; Sha256=$digest; Inputs=$current }
    } catch {
        if ($_.Exception.Message -cmatch '^profile_run_[a-z_]+$') { throw }
        throw 'profile_run_diagnostic_approval_manifest_invalid'
    } finally { if ($null -ne $document) { $document.Dispose() } }
}

Export-ModuleMember -Function *-DangoProfile*

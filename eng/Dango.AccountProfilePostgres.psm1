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
        EvidenceDirectory=(Join-Path (Split-Path -Parent $state) 'run20261010profilea')
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
    return $info
}

function Invoke-DangoProfileProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory,
        [hashtable]$EnvironmentValues=@{}, [ValidateRange(1,300)][int]$TimeoutSeconds=30,
        [switch]$KillOwnedTree,
        [Collections.Generic.List[object]]$Receipts,
        [scriptblock]$ProcessFactory={ param($info) $process=[Diagnostics.Process]::new(); $process.StartInfo=$info; return $process })
    $info=New-DangoProfileProcessInfo $Executable $Arguments $WorkingDirectory $EnvironmentValues
    $process=& $ProcessFactory $info
    $receipt=[pscustomobject]@{ Capability=[Guid]::NewGuid(); Pid=0; CreatedAt=$null; Executable=$Executable;
        IdentityVerified=$false; Exited=$false; TimedOut=$false; CleanupConfirmed=$false }
    if ($null -ne $Receipts) { $Receipts.Add($receipt) }
    try {
        if (-not $process.Start()) { throw 'profile_run_tool_start_failed' }
        $receipt.Pid=$process.Id
        $receipt.CreatedAt=$process.StartTime.ToUniversalTime()
        try { $receipt.IdentityVerified=Test-DangoProfilePath $process.MainModule.FileName $Executable }
        catch {
            # A fast tool may already have exited; its retained Start() handle cannot be PID-reused.
            # Only the real Process started with this exact full path gets this exited-handle fallback.
            $receipt.IdentityVerified=$process -is [Diagnostics.Process] -and $process.HasExited -and
                (Test-DangoProfilePath $process.StartInfo.FileName $Executable)
        }
        if (-not $receipt.IdentityVerified) { throw 'profile_run_tool_identity_unconfirmed' }
        $process.StandardInput.Close()
        Get-DangoProfileNativeType
        $output=[DangoProfileRunNative]::ReadBoundedAsync($process.StandardOutput,131072)
        $error=[DangoProfileRunNative]::ReadBoundedAsync($process.StandardError,131072)
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $exited=$process.WaitForExit(100)
        while (-not $exited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds -and -not $output.IsFaulted -and -not $error.IsFaulted) {
            $exited=$process.WaitForExit(100)
        }
        if (-not $exited) {
            $receipt.TimedOut=-not $output.IsFaulted -and -not $error.IsFaulted
            # This handle was started here and proven before timeout; never find/kill by name.
            if ($process.Id -eq $receipt.Pid -and $process.StartTime.ToUniversalTime() -eq $receipt.CreatedAt -and
                (Test-DangoProfilePath $process.MainModule.FileName $Executable)) {
                $process.Kill([bool]$KillOwnedTree)
                $receipt.Exited=$process.WaitForExit(5000)
                $receipt.CleanupConfirmed=$receipt.Exited
            }
            if ($output.IsFaulted -or $error.IsFaulted) { throw 'profile_run_tool_output_bound_exceeded' }
            throw 'profile_run_tool_timeout'
        }
        $receipt.Exited=$true; $receipt.CleanupConfirmed=$true
        if ($output.IsFaulted -or $error.IsFaulted) { throw 'profile_run_tool_output_bound_exceeded' }
        if (-not $output.Wait(1000) -or -not $error.Wait(1000)) { throw 'profile_run_tool_output_incomplete' }
        $text=$output.GetAwaiter().GetResult()
        # Drain but never print arbitrary database/tool stderr or secrets.
        if ($error.Wait(1000)) { [void]$error.GetAwaiter().GetResult() }
        if ($text.Length -gt 131072) { throw 'profile_run_tool_output_bound_exceeded' }
        return [pscustomobject]@{ ExitCode=$process.ExitCode; Output=$text.Trim(); Receipt=$receipt }
    } finally { $process.Dispose() }
}

function Test-DangoProfilePostgresProof {
    param($Proof, [string]$Executable, [string]$DataDirectory, [int]$Port,
        [DateTime]$AttemptedAt, [DateTime]$ReturnedAt)
    $controlProven=$Proof.ControlExitCode -eq 0 -or ($Proof.ControlPid -gt 0 -and
        $Proof.ControlIdentityVerified -and $Proof.ParentProcessId -eq $Proof.ControlPid)
    if (-not $controlProven -or $Proof.Pid -le 0 -or $Proof.CreatedAt -lt $AttemptedAt -or
        $Proof.CreatedAt -gt $ReturnedAt -or -not (Test-DangoProfilePath $Proof.Executable $Executable) -or
        $Proof.Arguments.Count -ne 7 -or -not (Test-DangoProfilePath $Proof.Arguments[0] $Executable)) { return $false }
    $values=@{}
    for ($index=1; $index -lt 7; $index+=2) {
        $key=$Proof.Arguments[$index]
        if ($key -cnotin @('-D','-h','-p') -or $values.ContainsKey($key)) { return $false }
        $values[$key]=$Proof.Arguments[$index+1]
    }
    return (Test-DangoProfilePath $values['-D'] $DataDirectory) -and
        $values['-h'] -ceq '127.0.0.1' -and $values['-p'] -ceq [string]$Port
}

function Get-DangoProfileNativeType {
    if (-not ('DangoProfileRunNative' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;
public static class DangoProfileRunNative {
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
 public static async Task<string> ReadBoundedAsync(TextReader reader, int maximum) {
  var result=new StringBuilder(); var buffer=new char[4096]; int count;
  while((count=await reader.ReadAsync(buffer,0,buffer.Length))>0) {
   if(result.Length+count>maximum) throw new InvalidOperationException("profile_run_tool_output_bound_exceeded");
   result.Append(buffer,0,count);
  }
  return result.ToString();
 }
}
'@
    }
}

function New-DangoProfileReservation {
    param($Plan)
    Assert-DangoProfileRegularPath $Plan.EvidenceDirectory
    Get-DangoProfileNativeType
    if (-not [DangoProfileRunNative]::CreateDirectoryW($Plan.EvidenceDirectory,[IntPtr]::Zero)) {
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
    $process=Get-CimInstance Win32_Process -Filter "ProcessId = $ownedPid"
    if ($null -eq $process) { throw 'profile_run_postmaster_proof_rejected' }
    return [pscustomobject]@{ ControlExitCode=$ControlExitCode; Pid=$ownedPid;
        CreatedAt=$process.CreationDate.ToUniversalTime(); Executable=$process.ExecutablePath;
        Arguments=@([DangoProfileRunNative]::Arguments($process.CommandLine)); ParentProcessId=$process.ParentProcessId;
        ControlPid=if ($null -ne $ControlReceipt) { $ControlReceipt.Pid } else { 0 };
        ControlIdentityVerified=$null -ne $ControlReceipt -and $ControlReceipt.IdentityVerified }
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
    return @"
BEGIN READ ONLY;
SELECT json_build_object('database',current_database(),'role',current_user,'version',current_setting('server_version_num')::integer,
 'dataDirectory',current_setting('data_directory'),'listenAddresses',current_setting('listen_addresses'),
 'migrations',(SELECT json_agg("MigrationId" ORDER BY "MigrationId") FROM dango.__ef_migrations_history),
 'userIds',(SELECT COALESCE(json_agg(id ORDER BY id),'[]'::json) FROM dango.users),
 'profiles',(SELECT COALESCE(json_agg(json_build_object('id',id,'nicknameIsNull',(to_jsonb(u)->>'nickname') IS NULL,
   'version',(to_jsonb(u)->>'profile_version')::bigint) ORDER BY id),'[]'::json) FROM dango.users u),
 'counts',json_build_object($($counts -join ',')),
 'digests',json_build_object($($digests -join ',')));
COMMIT;
"@
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

Export-ModuleMember -Function *-DangoProfile*

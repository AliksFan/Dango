[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-profile-postgres-tests.ps1'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module $ModulePath -Force
$taskChecks=[Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Body) {
    $passed=$false; $failure=$null
    try { $passed=[bool](& $Body) } catch { $failure=$_.Exception.Message }
    $taskChecks.Add([pscustomobject]@{Name=$Name; Passed=$passed; Failure=$failure})
}
function Rejected([scriptblock]$Body, [string]$Code) {
    try { [void](& $Body); return $false } catch { return $_.Exception.Message -like ('*'+$Code+'*') }
}
$taskRepo=Split-Path -Parent $PSScriptRoot
$taskRunner=[IO.Path]::GetFullPath($RunnerPath)
$taskSandbox=Join-Path $taskRepo ('artifacts/account-profile-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$taskBin=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'
$taskPlan=Get-DangoProfilePlan run20261008b0 $taskBin $taskSandbox
$taskOwnerId=[Guid]::NewGuid()

Check 'ExplicitApprovalMissingRejects' { Rejected { Assert-DangoProfileApproval $false } 'profile_run_explicit_approval_required' }
Check 'ExactRunAndBinaryTargets' {
    (Rejected { Get-DangoProfilePlan run20261010other $taskBin $taskSandbox } 'profile_run_target_rejected') -and
    (Rejected { Get-DangoProfilePlan run20261008b0 ($taskBin+'-other') $taskSandbox } 'profile_run_binary_target_rejected')
}
Check 'ActualDryRunNeedsOnlyWorkspaceInputs' {
    $preview=& $taskRunner
    -not $preview.Executed -and $preview.Stage -ceq 'AccountProfileA' -and
    $preview.Filter -ceq 'FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests'
}
Check 'OldFlagsAndCallerFilterCannotExpandEntry' {
    (Rejected { & $taskRunner -B1 } 'parameter') -and
    (Rejected { & $taskRunner -TestFilter 'FullyQualifiedName~anything' } 'parameter')
}
Check 'MissingDigestRejectsBeforeReservation' {
    Rejected { & $taskRunner -ExecuteApprovedPlan -ApprovalManifest (Join-Path $taskRepo 'artifacts/missing-profile-approval.json') } 'profile_run_approval_digest_required'
}
Check 'EntryDryReturnAndInputValidationPrecedeReservation' {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $commands=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst]},$true))
    $review=@($commands | Where-Object { $_.GetCommandName() -ceq 'Assert-DangoProfileReviewInputs' })
    $reserve=@($commands | Where-Object { $_.GetCommandName() -ceq 'New-DangoProfileReservation' })
    $legacy=@($commands | Where-Object { $_.GetCommandName() -in @('Remove-Item','Stop-Process','Invoke-Expression') })
    $returns=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.ReturnStatementAst]},$true))
    $errors.Count -eq 0 -and $review.Count -eq 3 -and $reserve.Count -eq 1 -and $legacy.Count -eq 0 -and
    $review[0].Extent.StartOffset -lt $reserve[0].Extent.StartOffset -and $returns[0].Extent.StartOffset -lt $reserve[0].Extent.StartOffset -and
    @($review | Where-Object { $_.Extent.Text -notmatch '\$ApprovalManifestSha256' }).Count -eq 0
}

# A fictional repository is sufficient to freeze inputs; no database file is read.
# The actual dry entry imports its standard module; restore the caller's selected
# module so mutation checks exercise that implementation rather than the default.
Import-Module $ModulePath -Force
$taskInitialInputs=Get-DangoProfileReviewInputs $taskPlan
foreach ($relative in @($taskInitialInputs.MissingInputs)) {
    $path=Join-Path $taskSandbox $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path,'fictional-review-input', [Text.UTF8Encoding]::new($false))
}
foreach ($relative in @('src/Dango.Server/bin/Release/net10.0/side.dll','src/Dango.Server/bin/Release/net10.0/Dango.Server.runtimeconfig.json',
    'tests/Dango.Server.Tests/bin/Release/net10.0/runtime/native.dll','tests/Dango.Server.Tests/bin/Release/net10.0/testhost.exe')) {
    $path=Join-Path $taskSandbox $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path)); [IO.File]::WriteAllText($path,'fictional-dependent-input')
}
$taskManifest=Join-Path $taskSandbox 'artifacts/approved.json'
$taskApproval=New-DangoProfileApprovalManifest $taskPlan $taskManifest
Check 'FictionalManifestFreezesDependencyTreesAndDigest' {
    $accepted=Assert-DangoProfileReviewInputs $taskPlan $taskManifest $taskApproval.Sha256
    $accepted.Sha256 -ceq $taskApproval.Sha256 -and $accepted.Inputs.Hashes.Count -eq 12 -and
    $accepted.Inputs.Hashes.Contains('tests/Dango.Server.Tests/bin/Release/net10.0/runtime/native.dll')
}
Check 'ManifestCreateNewNeverReplacesReviewedFile' { Rejected { New-DangoProfileApprovalManifest $taskPlan $taskManifest } 'already exists' }
Check 'SourceChangeFailsFixedManifest' {
    $source=Join-Path $taskSandbox 'tests/Dango.Server.Tests/Accounts/ProfileTestOwner.cs'
    [IO.File]::AppendAllText($source,'changed')
    $result=Rejected { Assert-DangoProfileReviewInputs $taskPlan $taskManifest $taskApproval.Sha256 } 'profile_run_approved_input_changed'
    [IO.File]::WriteAllText($source,'fictional-review-input'); $result
}
Check 'ManifestAndDllChangingTogetherCannotReuseApproval' {
    $dll=Join-Path $taskSandbox 'src/Dango.Server/bin/Release/net10.0/Dango.Server.dll'
    [IO.File]::AppendAllText($dll,'changed')
    $newInputs=Get-DangoProfileReviewInputs $taskPlan
    Write-DangoProfileJson $taskManifest $newInputs
    Rejected { Assert-DangoProfileReviewInputs $taskPlan $taskManifest $taskApproval.Sha256 } 'profile_run_approval_manifest_changed'
}
Check 'ManifestCannotPointOutsideWorkspaceArtifacts' {
    Rejected { Assert-DangoProfileManifestPath $taskPlan (Join-Path $taskSandbox 'approval.json') } 'profile_run_manifest_path_rejected'
}
Check 'ChildEnvironmentDropsInheritedStagesAndPgSecrets' {
    $originalDango=$env:DANGO_BACKEND_B5C_TEST_APPROVED; $originalPg=$env:PGPASSWORD
    try {
        $env:DANGO_BACKEND_B5C_TEST_APPROVED='inherited'; $env:PGPASSWORD='fictional-inherited-secret'
        $info=New-DangoProfileProcessInfo 'fictional.exe' @('exact') $taskSandbox @{DANGO_BACKEND_PROFILE_TEST_APPROVED='1'}
        -not $info.Environment.ContainsKey('DANGO_BACKEND_B5C_TEST_APPROVED') -and -not $info.Environment.ContainsKey('PGPASSWORD') -and
        $info.Environment['DANGO_BACKEND_PROFILE_TEST_APPROVED'] -ceq '1' -and $env:PGPASSWORD -ceq 'fictional-inherited-secret'
    } finally { $env:DANGO_BACKEND_B5C_TEST_APPROVED=$originalDango; $env:PGPASSWORD=$originalPg }
}

# Fake processes expose the same narrow capability interface, without launching a process.
if (-not ('DangoProfileFakeProcess' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.IO;
public sealed class DangoProfileFakeModule { public string FileName {get;set;} }
public sealed class DangoProfileFakeProcess : IDisposable {
 public string Scenario; public string Executable; public int Waits; public bool Killed; public bool Tree; public bool Disposed;
 public int ProbeCalls; public bool NativePreloadedAtStart; public bool ReadersAccessed; public bool InitialQueryBeforeReaders;
 public int Id => 30000; public DateTime StartTime {get;}=DateTime.UtcNow; public int ExitCode=>0;
 public DateTime ExitTime => StartTime.AddMilliseconds(1);
 public bool HasExited => Killed || Scenario=="normal" || Scenario=="null_module" || Scenario=="final_image_error" || Scenario=="initial_unknown_then_exited" && Waits>=2;
 public DangoProfileFakeModule MainModule => Scenario=="null_module" ? null : new DangoProfileFakeModule { FileName=Scenario=="changed" && Waits>0? Executable+"other":Executable };
 public TextWriter StandardInput {get;}=new StringWriter();
 private TextReader output=new StringReader("ok"); private readonly TextReader error=new StringReader("");
 public TextReader StandardOutput {get {ReadersAccessed=true;return output;} set {output=value;}}
 public TextReader StandardError {get {ReadersAccessed=true;return error;}}
 public bool Start(){NativePreloadedAtStart=Array.Exists(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetType("DangoProfileRunNativeV2")!=null);return true;}
 public bool WaitForExit(int milliseconds) {Waits++; return Killed || Scenario=="normal" || Scenario=="null_module" || Scenario=="final_image_error" || Scenario=="initial_unknown_then_exited" && Waits>=2;}
 public void Kill(bool tree){Killed=true;Tree=tree;} public void Dispose(){Disposed=true;}
}
'@
}
function FakeProcess([string]$Scenario, [switch]$Overflow) {
    $fake=[DangoProfileFakeProcess]::new(); $fake.Scenario=$Scenario; $fake.Executable=Join-Path $taskSandbox 'fake.exe'
    if ($Overflow) { $fake.StandardOutput=[IO.StringReader]::new(('x'*131073)) }
    return $fake
}
function FakeIdentity($Process, [string]$ExpectedExecutable) {
    # Deliberately ignore MainModule and StartInfo. These answers model the native
    # image query made against the retained handle; they are never production grants.
    $Process.ProbeCalls++
    if ($Process.ProbeCalls -eq 1) { $Process.InitialQueryBeforeReaders=-not $Process.ReadersAccessed }
    $finalError=$Process.Scenario -ceq 'final_image_error' -and $Process.Waits -gt 0
    $unknown=$finalError -or $Process.Scenario -ceq 'unknown_image' -or
        ($Process.Scenario -ceq 'initial_unknown_then_exited' -and $Process.Waits -eq 0)
    $image=if ($unknown) { '' } elseif ($Process.Scenario -ceq 'wrong_image' -or
        ($Process.Scenario -ceq 'changed' -and $Process.Waits -gt 0)) { $Process.Executable+'other' }
        else { $Process.Executable }
    [pscustomobject]@{Verified=(-not $unknown -and (Test-DangoProfilePath $image $ExpectedExecutable));
        ImagePath=$image;NativeErrorCode=if ($finalError) { 31 } elseif ($unknown) { 5 } else { 0 };
        Pid=if ($Process.Scenario -ceq 'handle_pid_reused') { $Process.Id+1 } else { $Process.Id };
        CreatedAt=if ($Process.Scenario -ceq 'handle_creation_reused') { $Process.StartTime.ToUniversalTime().AddSeconds(-1) }
            else { $Process.StartTime.ToUniversalTime() }}
}
$taskFakeIdentity={param($process,$expectedExe) FakeIdentity $process $expectedExe}
Check 'FakeNormalProcessProducesCapabilityReceipt' {
    $fake=FakeProcess normal; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable @('only') $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity
    $result.Output -ceq 'ok' -and $receipts.Count -eq 1 -and $receipts[0].IdentityVerified -and $receipts[0].CleanupConfirmed -and
        $fake.NativePreloadedAtStart -and $fake.InitialQueryBeforeReaders -and $fake.Disposed
}
Check 'PgCtlTimeoutKillsOnlyOwnedToolHandle' {
    $fake=FakeProcess timeout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_timeout'
    $rejected -and $fake.Killed -and -not $fake.Tree -and $receipts[0].CleanupConfirmed
}
Check 'VstestTimeoutMayKillOwnedTestTreeWithoutClaimingFixtureDisposal' {
    $fake=FakeProcess timeout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -KillOwnedTree -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_timeout'
    $rejected -and $fake.Killed -and $fake.Tree -and -not ($receipts[0].PSObject.Properties.Name -contains 'NativeCleanupConfirmed')
}
Check 'IdentityDriftDoesNotKillUnprovenProcess' {
    $fake=FakeProcess changed; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_timeout'
    $rejected -and -not $fake.Killed -and -not $receipts[0].CleanupConfirmed
}
Check 'ReaderStopsAtOutputBound' {
    $fake=FakeProcess overflow -Overflow; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_bound_exceeded'
    $rejected -and $fake.Killed -and -not $fake.Tree
}
Check 'NullMainModuleUsesNativeImageIdentity' {
    $fake=FakeProcess null_module; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable @('only') $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity
    $result.ExitCode -eq 0 -and $receipts[0].IdentityVerified -and $receipts[0].CleanupConfirmed -and $fake.Disposed
}
Check 'WrongNativeImageCannotAuthorizeKill' {
    $fake=FakeProcess wrong_image; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_identity_unconfirmed'
    $rejected -and -not $fake.Killed -and -not $receipts[0].IdentityVerified -and -not $receipts[0].CleanupConfirmed -and $fake.Disposed
}
Check 'UnknownNativeImageCannotAuthorizeKillOrCleanup' {
    $fake=FakeProcess unknown_image; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_identity_unconfirmed'
    $rejected -and -not $fake.Killed -and -not $receipts[0].IdentityVerified -and -not $receipts[0].CleanupConfirmed -and
        $receipts[0].ImageQueryError -ne 0 -and $fake.Disposed
}
Check 'InitialImageFailureStillWaitsAndRechecksOwnedHandle' {
    $fake=FakeProcess initial_unknown_then_exited; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_identity_unconfirmed'
    $rejected -and $fake.Waits -ge 2 -and -not $fake.Killed -and $receipts[0].Exited -and
        $receipts[0].CleanupConfirmed -and $null -ne $receipts[0].ExitedAt -and $fake.Disposed
}
Check 'InitialFailureAndRecoveryKeepSeparateImageErrors' {
    $fake=FakeProcess initial_unknown_then_exited; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_identity_unconfirmed'
    $rejected -and $receipts[0].InitialImageQueryError -eq 5 -and $receipts[0].FinalImageQueryError -eq 0 -and
        $receipts[0].ImageQueryError -eq 0 -and $receipts[0].IdentityVerified -and $receipts[0].Exited -and
        $receipts[0].ExitedAt -eq $fake.ExitTime.ToUniversalTime() -and $receipts[0].CleanupConfirmed -and -not $fake.Killed
}
Check 'ValidatedNaturalExitKeepsProofWhenFinalImageQueryFails' {
    $fake=FakeProcess final_image_error; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity
    $result.ExitCode -eq 0 -and $receipts[0].InitialImageQueryError -eq 0 -and $receipts[0].FinalImageQueryError -eq 31 -and
        $receipts[0].ImageQueryError -eq 31 -and $receipts[0].IdentityVerified -and $receipts[0].Exited -and
        $receipts[0].ExitedAt -eq $fake.ExitTime.ToUniversalTime() -and $receipts[0].CleanupConfirmed -and -not $fake.Killed
}
Check 'OwnSafeProcessHandleReportsExactNativeImage' {
    # Inspect only this offline test host's retained process handle. No tool starts.
    $process=[Diagnostics.Process]::GetCurrentProcess()
    try {
        $identity=Get-DangoProfileProcessIdentity $process ([Environment]::ProcessPath)
        $identity.Verified -and $identity.NativeErrorCode -eq 0 -and
            (Test-DangoProfilePath $identity.ImagePath ([Environment]::ProcessPath))
    } finally { $process.Dispose() }
}
Check 'WrongExpectedImageFailsNativeHandleComparison' {
    $process=[Diagnostics.Process]::GetCurrentProcess()
    try {
        $identity=Get-DangoProfileProcessIdentity $process (([Environment]::ProcessPath)+'other')
        -not $identity.Verified -and $identity.NativeErrorCode -eq 0 -and
            (Test-DangoProfilePath $identity.ImagePath ([Environment]::ProcessPath))
    } finally { $process.Dispose() }
}
Check 'UnavailableSafeHandleIdentityIsUnknown' {
    $process=[Diagnostics.Process]::GetCurrentProcess(); $process.Dispose()
    $identity=Get-DangoProfileProcessIdentity $process ([Environment]::ProcessPath)
    -not $identity.Verified -and $identity.NativeErrorCode -ne 0
}
foreach ($case in @('handle_pid_reused','handle_creation_reused')) {
    Check ('NativeHandleLifetimeMismatchRejects_'+$case) {
        $fake=FakeProcess $case; $receipts=[Collections.Generic.List[object]]::new()
        $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_identity_unconfirmed'
        $rejected -and -not $fake.Killed -and -not $receipts[0].CleanupConfirmed
    }
}
Check 'JsonZDateTimeConversionLosesPrecisionThroughStringReparse' {
    # Preserve the failing diagnosis: ConvertFrom-Json already produced a UTC
    # DateTime, but culture formatting removes its timezone and subsecond ticks.
    $json='{"CreatedAt":"2026-10-10T03:04:05.1234567Z"}'
    $value=($json | ConvertFrom-Json).CreatedAt
    $bad=[DateTimeOffset]::Parse([string]$value)
    $value -is [DateTime] -and $value.Kind -eq [DateTimeKind]::Utc -and $bad.UtcTicks -ne $value.Ticks
}
Check 'UtcDateTimeAndZStringPreserveExactTicks' {
    $text='2026-10-10T03:04:05.1234567Z'; $value=('{"CreatedAt":"'+$text+'"}' | ConvertFrom-Json).CreatedAt
    $fromDate=ConvertTo-DangoProfileUtc $value; $fromText=ConvertTo-DangoProfileUtc $text
    $fromOffset=ConvertTo-DangoProfileUtc ([DateTimeOffset]::new($value))
    $fromDate.Ticks -eq $value.Ticks -and $fromText.Ticks -eq $value.Ticks -and $fromOffset.Ticks -eq $value.Ticks
}
Check 'ExplicitZeroOffsetStringPreservesExactTicks' {
    $text='2026-10-10T03:04:05.1234567+00:00'
    (ConvertTo-DangoProfileUtc $text).Ticks -eq ([DateTimeOffset]::Parse($text)).UtcTicks
}
foreach ($case in @('unspecified_kind','local_kind','timezone_missing','nonutc_offset','nonutc_text','invalid_text')) {
    Check ('AmbiguousOrInvalidTimeIsRejected_'+$case) {
        $value=switch ($case) {
            unspecified_kind { [DateTime]::SpecifyKind([DateTime]::new(2026,10,10,3,4,5),[DateTimeKind]::Unspecified) }
            local_kind { [DateTime]::SpecifyKind([DateTime]::new(2026,10,10,3,4,5),[DateTimeKind]::Local) }
            timezone_missing { '2026-10-10T03:04:05.1234567' }
            nonutc_offset { [DateTimeOffset]::new(2026,10,10,3,4,5,[TimeSpan]::FromHours(8)) }
            nonutc_text { '2026-10-10T03:04:05.1234567+08:00' }
            invalid_text { 'not-a-date' }
        }
        Rejected { ConvertTo-DangoProfileUtc $value } 'profile_run_utc_time_required'
    }
}
Check 'OnlyVstestRequestsTreeKill' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'Invoke-DangoProfileProcess'},$true))
    $treeCalls=@($calls | Where-Object { $_.Extent.Text -match '-KillOwnedTree' })
    $treeCalls.Count -eq 1 -and $treeCalls[0].Extent.Text -match "@\('vstest'"
}

$taskNow=[DateTime]::UtcNow; $taskPg=Join-Path $taskBin 'postgres.exe'; $taskData=Join-Path $taskSandbox 'fictional-pgdata'
$taskProofPlan=$taskPlan.PSObject.Copy(); $taskProofPlan.DataDirectory=$taskData; $taskProofPlan.EvidenceDirectory=$taskSandbox
$taskControl=Join-Path $taskBin 'pg_ctl.exe'
$taskStartArguments=@('start','-D',$taskData,'-l',(Join-Path $taskSandbox 'postgresql.log'),'-o','-h 127.0.0.1 -p 23456','-w','-t','30')
$taskProof=[pscustomobject]@{ControlExitCode=0;Pid=1234;CreatedAt=$taskNow;Executable=$taskPg;
    Arguments=@($taskPg,'-D',$taskData,'-h','127.0.0.1','-p','23456');ParentProcessId=2222;ControlPid=2222;ControlIdentityVerified=$true;
    ControlCreatedAt=$taskNow.AddMilliseconds(-20);ControlExecutable=$taskControl;ControlExitedAt=$taskNow.AddMilliseconds(50);
    ControlArguments=$taskStartArguments;ShellBridge=$null}
function Proven($proof) { Test-DangoProfilePostgresProof $proof $taskPg $taskData 23456 $taskNow.AddSeconds(-1) $taskNow.AddSeconds(1) $taskProofPlan }
function CmdBridgeProof {
    $proof=$taskProof | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $command=Get-DangoProfileWindowsStartCommand $taskProofPlan 23456
    Get-DangoProfileNativeType
    $proof.ControlExitCode=-1; $proof.ParentProcessId=3333
    $proof.ShellBridge=[pscustomobject]@{Pid=3333;ParentProcessId=2222;CreatedAt=$taskNow.AddMilliseconds(-5);
        Executable=(Join-Path ([Environment]::SystemDirectory) 'cmd.exe');CommandLine=$command;
        Arguments=@([DangoProfileRunNativeV2]::Arguments($command))}
    return $proof
}
Check 'SuccessfulAndFailedStartRequireExactPgProof' {
    $copy=CmdBridgeProof; $copy.ControlExitCode=0; $success=Proven $copy; $copy.ControlExitCode=-1
    $success -and (Proven $copy)
}
foreach ($case in @('foreign_parent','reused_pid_time','future_time','suffix_path','foreign_host','extra_argument','wrong_executable','unverified_control')) {
    Check ('RejectsPgProof_'+$case) {
        $copy=CmdBridgeProof
        switch ($case) {
            foreign_parent { $copy.ControlExitCode=-1; $copy.ParentProcessId=4444 }
            reused_pid_time { $copy.CreatedAt=$taskNow.AddSeconds(-2) }
            future_time { $copy.CreatedAt=$taskNow.AddSeconds(2) }
            suffix_path { $copy.Arguments[2]+='-other' }
            foreign_host { $copy.Arguments[4]='0.0.0.0' }
            extra_argument { $copy.Arguments+=@('-c','arbitrary') }
            wrong_executable { $copy.Executable+='-other' }
            unverified_control { $copy.ControlExitCode=-1; $copy.ControlIdentityVerified=$false }
        }
        -not (Proven $copy)
    }
}
Check 'SuccessfulPgCtlExitCannotBypassForeignParentProof' {
    $copy=$taskProof | ConvertTo-Json -Depth 8 | ConvertFrom-Json; $copy.ParentProcessId=3333
    -not (Proven $copy)
}
Check 'ExactWindowsCmdBridgeProvesOwnedFailedStart' { Proven (CmdBridgeProof) }
Check 'JsonRoundTrippedUtcBridgeTimesPreserveOwnership' {
    $proof=(CmdBridgeProof) | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $proof.CreatedAt -is [DateTime] -and $proof.CreatedAt.Kind -eq [DateTimeKind]::Utc -and (Proven $proof)
}
foreach ($case in @('foreign_command','foreign_shell_image','wrong_shell_parent','wrong_pg_parent','bridge_before_control',
    'bridge_after_pg','bridge_after_control_exit','unknown_control_exit','control_exit_before_creation','control_exit_after_observation',
    'control_pid_reused','control_time_kind','extra_shell_operation','extra_redirect',
    'wrong_shell_argv','missing_bridge','wrong_control_argv','wrong_control_image')) {
    Check ('CmdBridgeRejects_'+$case) {
        $proof=CmdBridgeProof
        switch ($case) {
            foreign_command {
                $originalCommand=$proof.ShellBridge.CommandLine
                $normalizedPg=$taskPg.Replace('\','/')
                $proof.ShellBridge.CommandLine=$originalCommand.Replace($normalizedPg,$normalizedPg+'other')
                if ($proof.ShellBridge.CommandLine -ceq $originalCommand) { throw 'offline_foreign_command_mutation_missing' }
            }
            foreign_shell_image { $proof.ShellBridge.Executable=$taskControl }
            wrong_shell_parent { $proof.ShellBridge.ParentProcessId=4444 }
            wrong_pg_parent { $proof.ParentProcessId=4444 }
            bridge_before_control { $proof.ShellBridge.CreatedAt=$proof.ControlCreatedAt.AddMilliseconds(-1) }
            bridge_after_pg { $proof.ShellBridge.CreatedAt=$proof.CreatedAt.AddMilliseconds(1) }
            bridge_after_control_exit { $proof.ControlExitedAt=$proof.ShellBridge.CreatedAt.AddMilliseconds(-1) }
            unknown_control_exit { $proof.ControlExitedAt=$null }
            control_exit_before_creation { $proof.ControlExitedAt=$proof.ControlCreatedAt.AddMilliseconds(-1) }
            control_exit_after_observation { $proof.ControlExitedAt=$taskNow.AddSeconds(2) }
            control_pid_reused { $proof.ControlCreatedAt=$proof.ShellBridge.CreatedAt.AddMilliseconds(1) }
            control_time_kind { $proof.ControlCreatedAt=[DateTime]::SpecifyKind($proof.ControlCreatedAt,[DateTimeKind]::Unspecified) }
            extra_shell_operation { $proof.ShellBridge.CommandLine+=' & echo unrelated' }
            extra_redirect { $proof.ShellBridge.CommandLine+=' >> "another.log"' }
            wrong_shell_argv { $proof.ShellBridge.Arguments+=@('/K','unrelated') }
            missing_bridge { $proof.ShellBridge=$null }
            wrong_control_argv { $proof.ControlArguments+=@('--extra') }
            wrong_control_image { $proof.ControlExecutable=$taskControl+'other' }
        }
        -not (Proven $proof)
    }
}
function ObservationState {
    [pscustomobject]@{At=$taskNow;Reads=0;Delays=0}
}
function ObservationControl {
    [pscustomobject]@{Started=$true;Pid=2222;CreatedAt=$taskProof.ControlCreatedAt;Executable=$taskControl;
        IdentityVerified=$true;Exited=$true;ExitedAt=$taskProof.ControlExitedAt;Arguments=$taskStartArguments}
}
Check 'LatePidfileIsObservedWithinBoundWithoutRestart' {
    $state=ObservationState; $control=ObservationControl; $proof=CmdBridgeProof
    $proof.CreatedAt=$taskNow.AddMilliseconds(100)
    $reader={param($plan,$port,$exit,$receipt) $state.Reads++; if ($state.Reads -ge 3) { $proof } else { $null }}
    $delay={param($milliseconds) $state.Delays++; $state.At=$state.At.AddMilliseconds($milliseconds)}
    $clock={ $state.At }
    $result=Observe-DangoProfileStartup $taskProofPlan 23456 -1 $control $taskNow.AddSeconds(-1) -ObservationSeconds 3 -ProofReader $reader -Delay $delay -Clock $clock
    $result.Verified -and $result.Proof.Pid -eq 1234 -and $state.Reads -ge 3 -and $state.Delays -ge 2 -and
        $result.ObservedUntil -le $taskNow.AddSeconds(3) -and -not $result.NoResourcesConfirmed
}
Check 'LatePidfileWithMissingBridgeRemainsUnconfirmed' {
    $state=ObservationState; $control=ObservationControl; $proof=CmdBridgeProof; $proof.ShellBridge=$null
    $proof.CreatedAt=$taskNow.AddMilliseconds(100)
    $reader={param($plan,$port,$exit,$receipt) $state.Reads++; if ($state.Reads -ge 3) { $proof } else { $null }}
    $delay={param($milliseconds) $state.Delays++; $state.At=$state.At.AddMilliseconds($milliseconds)}
    $result=Observe-DangoProfileStartup $taskProofPlan 23456 -1 $control $taskNow.AddSeconds(-1) -ObservationSeconds 3 -ProofReader $reader -Delay $delay -Clock { $state.At }
    -not $result.Verified -and $result.ControlExited -and -not $result.NoResourcesConfirmed -and
        $result.ObservedUntil -le $taskNow.AddSeconds(3)
}
Check 'EmptyStartupWindowDoesNotRefundStartedCapability' {
    $state=ObservationState; $control=ObservationControl
    $reader={param($plan,$port,$exit,$receipt) $state.Reads++; $null }
    $delay={param($milliseconds) $state.Delays++; $state.At=$state.At.AddMilliseconds($milliseconds)}
    $result=Observe-DangoProfileStartup $taskProofPlan 23456 -1 $control $taskNow.AddSeconds(-1) -ObservationSeconds 3 -ProofReader $reader -Delay $delay -Clock { $state.At }
    -not $result.Verified -and $result.ControlExited -and -not $result.NoResourcesConfirmed -and $state.Reads -gt 1 -and
        $result.ObservedUntil -le $taskNow.AddSeconds(3)
}
Check 'RunningControlAndNoPidfileCannotClaimNoResources' {
    $state=ObservationState; $control=ObservationControl; $control.Exited=$false; $control.ExitedAt=$null
    $reader={param($plan,$port,$exit,$receipt) $state.Reads++; $null }
    $delay={param($milliseconds) $state.Delays++; $state.At=$state.At.AddMilliseconds($milliseconds)}
    $result=Observe-DangoProfileStartup $taskProofPlan 23456 -1 $control $taskNow.AddSeconds(-1) -ObservationSeconds 3 -ProofReader $reader -Delay $delay -Clock { $state.At }
    -not $result.Verified -and -not $result.ControlExited -and -not $result.NoResourcesConfirmed -and
        $result.ObservedUntil -le $taskNow.AddSeconds(3)
}

$taskLines=[Collections.Generic.List[string]]::new(); $taskUsed=@{}; $taskSeq=0
foreach ($caseId in $taskPlan.CaseAttempts.Keys) {
    foreach ($operation in $taskPlan.CaseBudgets[$caseId].Keys) {
        for ($i=0;$i -lt $taskPlan.CaseBudgets[$caseId][$operation];$i++) {
            $taskUsed[$operation]=[int]$taskUsed[$operation]+1; $taskSeq++
            $taskLines.Add(([ordered]@{Version=1;OwnerId=$taskOwnerId.ToString('D');Sequence=$taskSeq;CaseId=$caseId;
                Operation=$operation;Used=$taskUsed[$operation];AtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Compress))
        }
    }
}
Check 'Exact67AttemptsAndPerCaseBudgetsPass' { (Assert-DangoProfileAttempts $taskPlan $taskOwnerId @($taskLines)).Total -eq 67 }
foreach ($case in @('missing','duplicate','foreign_owner','overbudget','case_swap','nonutc','extra_property','string_sequence')) {
    Check ('RejectsAttempt_'+$case) {
        $lines=@($taskLines); $record=$lines[0] | ConvertFrom-Json
        switch ($case) {
            missing { $lines=$lines[1..66] }
            duplicate { $lines[1]=$lines[0] }
            foreign_owner { $record.OwnerId=[Guid]::NewGuid().ToString('D') }
            overbudget { $record.Used=100 }
            case_swap { $record.CaseId='https' }
            nonutc { $record.AtUtc='2026-10-10T10:00:00+08:00' }
            extra_property { $record | Add-Member Secret 'fictional' }
            string_sequence { $record.Sequence='1' }
        }
        if ($case -notin @('missing','duplicate')) { $lines[0]=$record | ConvertTo-Json -Compress }
        Rejected { Assert-DangoProfileAttempts $taskPlan $taskOwnerId $lines } ''
    }
}

$taskResourceLines=[Collections.Generic.List[string]]::new(); $taskSeq=0
foreach ($caseId in $taskPlan.CaseAttempts.Keys) {
    $fixture=[Guid]::NewGuid()
    foreach ($phase in @('preparing','ready','dispose_started','disposed')) {
        $taskSeq++
        $taskResourceLines.Add(([ordered]@{Version=1;OwnerId=$taskOwnerId.ToString('D');Sequence=$taskSeq;FixtureId=$fixture.ToString('D');
            CaseId=$caseId;Phase=$phase;RequestedHttps=($caseId -ceq 'https');HttpsStarted=($caseId -ceq 'https' -and $phase -cne 'preparing');
            NativeKeyDisposeConfirmed=($phase -ceq 'disposed');AtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Compress))
    }
}
Check 'SevenFixturesOneHttpsAnd28LifecycleReceiptsPass' { (Assert-DangoProfileResources $taskPlan $taskOwnerId @($taskResourceLines)).NativeCleanupConfirmed }
foreach ($case in @('timeout_partial','dispose_failure','false_native_cleanup','foreign_fixture','wrong_https','early_cleanup','foreign_owner')) {
    Check ('RejectsResource_'+$case) {
        $lines=@($taskResourceLines); $record=$lines[3] | ConvertFrom-Json
        switch ($case) {
            timeout_partial { $lines=$lines[0..26] }
            dispose_failure { $record.Phase='dispose_failed'; $record.NativeKeyDisposeConfirmed=$false }
            false_native_cleanup { $record.NativeKeyDisposeConfirmed=$false }
            foreign_fixture { $record.FixtureId=[Guid]::NewGuid().ToString('D') }
            wrong_https { $record.HttpsStarted=$true }
            early_cleanup { $record.Phase='ready' }
            foreign_owner { $record.OwnerId=[Guid]::NewGuid().ToString('D') }
        }
        if ($case -ne 'timeout_partial') { $lines[3]=$record | ConvertTo-Json -Compress }
        Rejected { Assert-DangoProfileResources $taskPlan $taskOwnerId $lines } ''
    }
}

$taskMethods=@('IndependentConnectionsWithSameExpectedVersionProduceOneCommitAndOneConflict',
 'DuplicateNamesAndSequentialEditsDoNotChangeAccountPetOrRoomIdentity','FailureAfterSaveRollsBackNicknameAndVersion',
 'FailureAfterSaveRollsBackNicknameAndVersion','CrossAccountSessionCannotReadOrWriteAndRevocationBeforeLocksIsRechecked',
 'SessionExpiryAtPrecommitRollsBackNickname','ProfileHttpsUsesCurrentSessionRejectsForeignOwnershipCsrfOriginAndMalformedBodies')
function Trx([string[]]$Methods, [string]$Outcome='Passed') {
    $results=@();$defs=@()
    for($i=0;$i -lt $Methods.Count;$i++) {
        $results+="<UnitTestResult testId='id$i' executionId='execution$i' outcome='$Outcome'/>"
        $defs+="<UnitTest id='id$i'><TestMethod className='Dango.Server.Tests.Accounts.AccountProfilePostgresTests' name='$($Methods[$i])'/></UnitTest>"
    }
    return "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><Results>$($results -join '')</Results><TestDefinitions>$($defs -join '')</TestDefinitions></TestRun>"
}
$taskTrx=Join-Path $taskSandbox 'fake.trx'
Check 'ExactSevenPassedTrxCasesPass' { [IO.File]::WriteAllText($taskTrx,(Trx $taskMethods)); Assert-DangoProfileTrx $taskTrx; $true }
foreach ($case in @('zero','skip','extra','foreign_method','dtd')) {
    Check ('RejectsTrx_'+$case) {
        $xml=switch($case) {
            zero { Trx @() }
            skip { Trx $taskMethods 'NotExecuted' }
            extra { Trx (@($taskMethods)+'ExtraCase') }
            foreign_method { $methods=@($taskMethods);$methods[0]='ForeignCase';Trx $methods }
            dtd { '<!DOCTYPE a [<!ENTITY ex SYSTEM "file:///unrelated">]><TestRun>&ex;</TestRun>' }
        }
        [IO.File]::WriteAllText($taskTrx,$xml); Rejected { Assert-DangoProfileTrx $taskTrx } ''
    }
}
Check 'SqlEvidenceIsReadOnlyAndQuotesFormalHistoryColumn' {
    $sql=Get-DangoProfileDatabaseSql
    $sql.StartsWith('BEGIN READ ONLY;') -and $sql -cmatch 'json_agg\("MigrationId" ORDER BY "MigrationId"\)' -and
    $sql -notmatch '\bmigration_id\b' -and $sql -match "to_jsonb\(t\) - 'nickname' - 'profile_version'" -and
    $sql -notmatch '(?im)^\s*(UPDATE|INSERT|DELETE|ALTER|CREATE|DROP)\b'
}
Check 'EveryPsqlJsonProbeUsesQuietFraming' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $assignments=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and
        $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and $n.Left.VariablePath.UserPath -ceq 'taskProfilePsqlArgs'},$true))
    $assignments.Count -eq 1 -and $assignments[0].Extent.Text -match "'-q'" -and
    (Rejected { 'BEGIN'+[Environment]::NewLine+'{}'+[Environment]::NewLine+'COMMIT' | ConvertFrom-Json } '')
}
$taskBefore=[pscustomobject]@{counts=[pscustomobject]@{users=1;pets=1;audit=0};
    digests=[pscustomobject]@{users=@('a'*64);pets=@('b'*64);audit=@()};userIds=@([Guid]::NewGuid().ToString('D'))}
$taskAfter=[pscustomobject]@{counts=[pscustomobject]@{users=2;pets=2;audit=0};
    digests=[pscustomobject]@{users=@(('a'*64),('c'*64));pets=@(('b'*64),('d'*64));audit=@()};
    profiles=@([pscustomobject]@{id=$taskBefore.userIds[0];nicknameIsNull=$true;version=1})}
Check 'ExistingGraphDigestsAndInitialProfileSurvive' { Assert-DangoProfileOldGraph $taskBefore $taskAfter -RequireInitialProfiles; $true }
foreach ($case in @('old_pet_changed','old_user_deleted','old_nickname_changed','old_profile_version_changed','unapproved_audit')) {
    Check ('RejectsGraph_'+$case) {
        $after=$taskAfter | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        switch ($case) {
            old_pet_changed { $after.digests.pets[0]='e'*64 }
            old_user_deleted { $after.digests.users[0]='e'*64 }
            old_nickname_changed { $after.profiles[0].nicknameIsNull=$false }
            old_profile_version_changed { $after.profiles[0].version=2 }
            unapproved_audit { $after.counts.audit=1;$after.digests.audit=@('e'*64) }
        }
        Rejected { Assert-DangoProfileOldGraph $taskBefore $after -RequireInitialProfiles } ''
    }
}
$taskScoped=[pscustomobject]@{users=9;validUsers=9;namedUsers=4;profileIncrements=5;completeGraphs=9;pendingUnsentEmails=9;sessions=10;validSessions=10;revokedSessions=2}
Check 'ScopedGraphAndNoMailDispatchCountsPass' { Assert-DangoProfileScopedGraph $taskScoped; $true }
foreach ($field in @('validUsers','completeGraphs','pendingUnsentEmails','validSessions','profileIncrements','revokedSessions')) {
    Check ('RejectsScoped_'+$field) {
        $evidence=$taskScoped | ConvertTo-Json | ConvertFrom-Json; $evidence.$field--
        Rejected { Assert-DangoProfileScopedGraph $evidence } 'profile_run_scoped_graph_rejected'
    }
}
Check 'ScopedSqlBindsNineExactGuidsEpochAndOwnerInvalidEmails' {
    $ids=@(1..9 | ForEach-Object { [Guid]::NewGuid().ToString('D') }); $epoch=[Guid]::NewGuid()
    $sql=Get-DangoProfileScopedSql $ids $epoch $taskOwnerId
    $sql.Contains('profilea-'+$taskOwnerId.ToString('N')+'-%@example.invalid') -and $sql.Contains($epoch.ToString('D')) -and
    (Rejected { Get-DangoProfileScopedSql @('not-a-guid') $epoch $taskOwnerId } 'profile_run_scoped_ids_rejected')
}

$taskChecks | Select-Object Name,Passed,Failure | Format-Table -AutoSize
$taskPassed=@($taskChecks | Where-Object Passed).Count
Write-DangoProfileJson (Join-Path $taskSandbox 'offline-results.json') ([pscustomobject]@{ExecutedDatabase=$false;Checks=@($taskChecks)}) -CreateNew
if ($taskPassed -ne $taskChecks.Count) { throw ('profile_run_offline_boundary_failed:'+$taskPassed+'/'+$taskChecks.Count) }
Write-Output ('profile_run_offline_boundary_passed:'+$taskPassed+'/'+$taskChecks.Count)

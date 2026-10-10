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
Check 'DryEntryAstReturnsFixedWorkspacePlanBeforeReservation' {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $dry=@($ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.IfStatementAst] -and $_.Extent.Text -match '^if \(-not \$ExecuteApprovedPlan\)' })
    $returns=@(if($dry.Count -eq 1){$dry[0].FindAll({param($n) $n -is [Management.Automation.Language.ReturnStatementAst]},$true)})
    $reserve=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'New-DangoProfileReservation'},$true))
    $errors.Count -eq 0 -and $returns.Count -eq 1 -and $reserve.Count -eq 1 -and
        $returns[0].Extent.StartOffset -lt $reserve[0].Extent.StartOffset -and
        $taskPlan.Executed -eq $false -and $taskPlan.Stage -ceq 'AccountProfileA' -and
        $taskPlan.Filter -ceq 'FullyQualifiedName~Dango.Server.Tests.Accounts.AccountProfilePostgresTests'
}
Check 'OldFlagsAndCallerFilterCannotExpandEntry' {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $names=@($ast.ParamBlock.Parameters.Name.VariablePath.UserPath)
    $errors.Count -eq 0 -and $names -cnotcontains 'B1' -and $names -cnotcontains 'TestFilter' -and
        $names.Count -eq 6 -and $names -ccontains 'ExecuteApprovedPlan' -and $names -ccontains 'ApprovalManifestSha256'
}
Check 'MissingDigestRejectsBeforeReservation' {
    Rejected { Assert-DangoProfileReviewInputs $taskPlan (Join-Path $taskSandbox 'missing-profile-approval.json') '' } 'profile_run_approval_digest_required'
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
# The runner is inspected through AST only. All fake boundary checks keep using
# the explicitly selected module; the actual entry is never executed here.
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
using System; using System.IO; using System.Threading.Tasks;
public sealed class DangoProfileFakeModule { public string FileName {get;set;} }
public sealed class DangoProfilePendingReader : TextReader {
 private readonly TaskCompletionSource<int> end=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public int ReadCalls; public bool Closed; public bool FailClose;
 public override Task<int> ReadAsync(char[] buffer,int index,int count){ReadCalls++;return end.Task;}
 protected override void Dispose(bool disposing){if(FailClose)throw new IOException("fictional_stream_close_failure");Closed=true;end.TrySetResult(0);base.Dispose(disposing);}
}
public sealed class DangoProfileTrackedReader : StringReader {
 public bool Closed; public bool FailClose; public int CloseCalls; public DangoProfileTrackedReader(string value):base(value){}
 protected override void Dispose(bool disposing){CloseCalls++;if(FailClose)throw new ObjectDisposedException("fictional_close_failure_message");Closed=true;base.Dispose(disposing);}
}
public sealed class DangoProfileFaultReader : TextReader {
 public string Kind; public string Prefix; public int Calls; public bool Closed; public bool FailClose; public int CloseCalls;
 public DangoProfileFaultReader(string kind,string prefix=""){Kind=kind;Prefix=prefix;}
 private async Task<int> Delayed(){await Task.Delay(50);if(Kind=="delayed_cancel")throw new OperationCanceledException(new System.Threading.CancellationToken(true));throw new IOException("fictional_exception_message_password=never_real");}
 public override Task<int> ReadAsync(char[] buffer,int index,int count){
  Calls++;
  if(Prefix.Length>0 && Calls==1){Prefix.CopyTo(0,buffer,index,Prefix.Length);return Task.FromResult(Prefix.Length);}
  if(Kind=="delayed_io" || Kind=="delayed_cancel")return Delayed();
  if(Kind=="io")return Task.FromException<int>(new IOException("fictional_exception_message_password=never_real"));
  if(Kind=="disposed")return Task.FromException<int>(new ObjectDisposedException("fictional_exception_message_password=never_real"));
  if(Kind=="decode")return Task.FromException<int>(new System.Text.DecoderFallbackException("fictional_exception_message_password=never_real"));
  if(Kind=="cancel")return Task.FromCanceled<int>(new System.Threading.CancellationToken(true));
  return Task.FromException<int>(new InvalidOperationException("fictional_exception_message_password=never_real"));
 }
 protected override void Dispose(bool disposing){CloseCalls++;if(FailClose)throw new ObjectDisposedException("fictional_close_failure_message");Closed=true;base.Dispose(disposing);}
}
public sealed class DangoProfileUnsettledReader : TextReader {
 public int ReadCalls; public int CloseCalls; public bool Closed;
 private readonly TaskCompletionSource<int> end=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override Task<int> ReadAsync(char[] buffer,int index,int count){ReadCalls++;return end.Task;}
 protected override void Dispose(bool disposing){CloseCalls++;Closed=true;base.Dispose(disposing);}
}
public sealed class DangoProfileChunkReader : StringReader {
 public int MaximumChunk;
 public DangoProfileChunkReader(string text,int maximumChunk):base(text){MaximumChunk=maximumChunk;}
 public override Task<int> ReadAsync(char[] buffer,int index,int count){return base.ReadAsync(buffer,index,Math.Min(count,MaximumChunk));}
}
public sealed class DangoProfileCloseFaultWriter : StringWriter {
 protected override void Dispose(bool disposing){throw new IOException("fictional_stdin_exception_password=never_real");}
}
public sealed class DangoProfileFakeProcess : IDisposable {
 public string Scenario; public string Executable; public int Waits; public bool Killed; public bool Tree; public bool Disposed;
 public bool StartAccepted=true;
 public int ProbeCalls; public bool NativePreloadedAtStart; public bool ReadersAccessed; public bool InitialQueryBeforeReaders;
 public int Id => 30000; public DateTime StartTime {get;}=DateTime.UtcNow; public int PlannedExitCode; public int ExitCode=>PlannedExitCode;
 public DateTime ExitTime => StartTime.AddMilliseconds(1);
 public bool HasExited => Killed || Scenario=="normal" || Scenario=="null_module" || Scenario=="final_image_error" || Scenario=="initial_unknown_then_exited" && Waits>=2;
 public DangoProfileFakeModule MainModule => Scenario=="null_module" ? null : new DangoProfileFakeModule { FileName=Scenario=="changed" && Waits>0? Executable+"other":Executable };
 public TextWriter StandardInput {get;set;}=new StringWriter();
 private TextReader output=new StringReader("ok"); private TextReader error=new StringReader("");
 public TextReader StandardOutput {get {ReadersAccessed=true;return output;} set {output=value;}}
 public TextReader StandardError {get {ReadersAccessed=true;return error;} set {error=value;}}
 public bool Start(){NativePreloadedAtStart=Array.Exists(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetType("DangoProfileRunNativeV2")!=null);return StartAccepted;}
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
function PendingPipeProcess([string]$Executable, [string]$Scenario='normal') {
    $fake=FakeProcess $Scenario; $fake.Executable=$Executable
    $fake.StandardOutput=[DangoProfilePendingReader]::new()
    $fake.StandardError=[DangoProfileTrackedReader]::new('')
    return $fake
}
function StartPipeArguments($Plan, [int]$Port) {
    @('start','-D',$Plan.DataDirectory,'-l',(Join-Path $Plan.EvidenceDirectory 'postgresql.log'),
        '-o',"-h 127.0.0.1 -p $Port",'-w','-t','30')
}
$taskPipeControl=Join-Path $taskBin 'pg_ctl.exe'; $taskPipePort=23456
Check 'ExitedControlWithOpenInheritedPipeReproducesStrictEofFailure' {
    $fake=PendingPipeProcess $taskPipeControl; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts } 'profile_run_tool_output_incomplete'
    $rejected -and $receipts[0].IdentityVerified -and $receipts[0].Exited -and $receipts[0].CleanupConfirmed -and
        $fake.StandardOutput.ReadCalls -gt 0 -and -not $receipts[0].OutputComplete
}
Check 'ExactPgStartDiscardsInheritedOutputWithoutReaderTasks' {
    $fake=PendingPipeProcess $taskPipeControl; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort
    $result.ExitCode -eq 0 -and $result.Output -ceq '' -and $receipts[0].OutputContract -ceq 'PostgresStartIgnored' -and
        $receipts[0].ReaderTasksStarted -eq 0 -and $fake.StandardOutput.ReadCalls -eq 0 -and
        -not $receipts[0].OutputCaptured -and -not $receipts[0].OutputComplete -and $receipts[0].OutputDiscarded -and
        $receipts[0].IdentityVerified -and $receipts[0].ExitCodeRecorded -and $receipts[0].ExitCode -eq 0 -and
        $receipts[0].ExitedAt -eq $fake.ExitTime.ToUniversalTime() -and $receipts[0].CleanupConfirmed -and
        $receipts[0].StandardOutputClosed -and $receipts[0].StandardErrorClosed -and $receipts[0].OutputStreamsClosed -and
        $fake.StandardOutput.Closed -and $fake.StandardError.Closed
}
Check 'IgnoredPgStartStillRecordsNonzeroExitAsFailureEvidence' {
    $fake=PendingPipeProcess $taskPipeControl; $fake.PlannedExitCode=23; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort
    $result.ExitCode -eq 23 -and $receipts[0].ExitCodeRecorded -and $receipts[0].ExitCode -eq 23 -and
        $receipts[0].ExitedAt -eq $fake.ExitTime.ToUniversalTime() -and $receipts[0].ReaderTasksStarted -eq 0 -and
        $receipts[0].OutputStreamsClosed -and -not $receipts[0].OutputComplete
}
foreach ($case in @('migration','sql','vstest','version','status','stop','extra_argv','wrong_data','wrong_log','wrong_options',
    'wrong_workdir','wrong_plan','extra_plan_property','wrong_port','extra_environment','tree_kill','missing_plan')) {
    Check ('IgnoredOutputContractRejectsBeforeFactory_'+$case) {
        $plan=$taskPlan.PSObject.Copy(); $exe=$taskPipeControl; $arguments=StartPipeArguments $plan $taskPipePort
        $directory=$taskSandbox; $environment=@{}; $tree=$false; $port=$taskPipePort; $calls=[pscustomobject]@{Factories=0}
        switch ($case) {
            migration { $exe=Join-Path $taskSandbox 'dotnet.exe'; $arguments=@('Dango.Server.dll','--formal-migrate') }
            sql { $exe=Join-Path $taskBin 'psql.exe'; $arguments=@('-c','SELECT 1') }
            vstest { $exe=Join-Path $taskSandbox 'dotnet.exe'; $arguments=@('vstest','tests.dll') }
            version { $arguments=@('--version') }
            status { $arguments=@('status','-D',$plan.DataDirectory) }
            stop { $arguments=@('stop','-D',$plan.DataDirectory,'-m','fast') }
            extra_argv { $arguments+=@('--extra') }
            wrong_data { $arguments[2]=$arguments[2]+'-other' }
            wrong_log { $arguments[4]=$arguments[4]+'-other' }
            wrong_options { $arguments[6]='-h 0.0.0.0 -p 23456' }
            wrong_workdir { $directory=$taskSandbox+'-other' }
            wrong_plan { $plan.EvidenceDirectory=$plan.EvidenceDirectory+'-other'; $arguments=StartPipeArguments $plan $port }
            extra_plan_property { $plan | Add-Member ExtraPolicy 'fictional' }
            wrong_port { $port=0 }
            extra_environment { $environment=@{FICTIONAL_EXTRA='not-allowed'} }
            tree_kill { $tree=$true }
            missing_plan { $plan=$null }
        }
        $rejected=Rejected { Invoke-DangoProfileProcess $exe $arguments $directory -EnvironmentValues $environment -TimeoutSeconds 1 -KillOwnedTree:$tree -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $calls.Factories++; throw 'offline_factory_must_not_run'} -OutputContract PostgresStartIgnored -PostgresStartPlan $plan -PostgresStartPort $port } 'profile_run_start_output_contract_rejected'
        $rejected -and $calls.Factories -eq 0
    }
}
Check 'IgnoredStartTimeoutKillsOnlyOwnedControlAndClosesStreams' {
    $fake=PendingPipeProcess $taskPipeControl timeout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort } 'profile_run_tool_timeout'
    $rejected -and $fake.Killed -and -not $fake.Tree -and $receipts[0].TimedOut -and $receipts[0].Exited -and
        $receipts[0].ExitCodeRecorded -and $receipts[0].ExitedAt -eq $fake.ExitTime.ToUniversalTime() -and
        $receipts[0].ReaderTasksStarted -eq 0 -and $receipts[0].OutputStreamsClosed -and $receipts[0].CleanupConfirmed
}
Check 'IgnoredOutputCannotHideUnknownControlIdentity' {
    $fake=PendingPipeProcess $taskPipeControl unknown_image; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort } 'profile_run_tool_identity_unconfirmed'
    $rejected -and -not $fake.Killed -and -not $receipts[0].IdentityVerified -and -not $receipts[0].CleanupConfirmed -and
        $receipts[0].ReaderTasksStarted -eq 0 -and $receipts[0].OutputStreamsClosed
}
Check 'StartupReaderCloseFailureCannotContinueAsSuccess' {
    $fake=PendingPipeProcess $taskPipeControl; $fake.StandardOutput.FailClose=$true; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort } 'profile_run_tool_output_close_unconfirmed'
    $rejected -and $receipts[0].ExitCodeRecorded -and $receipts[0].Exited -and -not $receipts[0].StandardOutputClosed -and
        $receipts[0].StandardErrorClosed -and -not $receipts[0].OutputStreamsClosed -and $fake.StandardError.Closed
}
foreach ($tool in @('migration','sql','vstest')) {
    Check ('CompleteEofStillRequiredFor_'+$tool) {
        $exe=Join-Path $taskSandbox ($tool+'.exe'); $fake=PendingPipeProcess $exe; $receipts=[Collections.Generic.List[object]]::new()
        $rejected=Rejected { Invoke-DangoProfileProcess $exe @('fictional') $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $fake} -Receipts $receipts -OutputContract CompleteEof } 'profile_run_tool_output_incomplete'
        $rejected -and $receipts[0].OutputContract -ceq 'CompleteEof' -and $receipts[0].ReaderTasksStarted -eq 2 -and
            $fake.StandardOutput.ReadCalls -gt 0 -and -not $receipts[0].OutputComplete -and -not $receipts[0].OutputDiscarded
    }
}
Check 'LowercaseOutputModeDoesNotGrantStartupDiscardPolicy' {
    $calls=[pscustomobject]@{Factories=0}; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -TimeoutSeconds 1 -IdentityProbe $taskFakeIdentity -ProcessFactory {param($info) $calls.Factories++; throw 'offline_factory_must_not_run'} -Receipts $receipts -OutputContract postgresstartignored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort } 'OutputContract'
    $rejected -and $calls.Factories -eq 0 -and $receipts.Count -eq 0
}

# A4 diagnostics and evidence-mode checks use only injected fake processes/readers.
# No child executable, PostgreSQL directory, credentials, listener or trust store is accessed.
function SafeCapture($Capture) {
    if ($null -eq $Capture) { return $false }
    $names=@($Capture.PSObject.Properties.Name)
    $allowed=@('ObservedCharacters','MaximumCharacters','ObservedUtf8Bytes','MaximumUtf8Bytes','ReadStarted','EofCompleted','Canceled','FailureKind')
    $json=$Capture | ConvertTo-Json -Depth 6 -Compress
    $names.Count -eq $allowed.Count -and @($names | Where-Object { $_ -cnotin $allowed }).Count -eq 0 -and
        -not $json.Contains('fictional_exception_message') -and -not $json.Contains('fictional_reader_body') -and
        -not $json.Contains('fictional_stdin_exception')
}
foreach ($case in @('stdout_io','stderr_io','stdout_disposed','stdout_decode','stdout_invalid','stdout_cancel',
    'stdout_delayed_io','stdout_delayed_cancel','stdout_prefix_io','running_io')) {
    Check ('ReaderFailureGetsStableSafeClassification_'+$case) {
        $scenario=if($case -ceq 'running_io'){'timeout'}else{'normal'}
        $fake=FakeProcess $scenario; $receipts=[Collections.Generic.List[object]]::new()
        $kind=switch($case) {
            stdout_disposed {'disposed'}; stdout_decode {'decode'}; stdout_invalid {'invalid'}
            stdout_cancel {'cancel'}; stdout_delayed_io {'delayed_io'}; stdout_delayed_cancel {'delayed_cancel'}; default {'io'}
        }
        $prefix=if($case -ceq 'stdout_prefix_io'){'fictional_reader_body'}else{''}
        $reader=[DangoProfileFaultReader]::new($kind,$prefix)
        if($case -ceq 'stderr_io'){$fake.StandardError=$reader}else{$fake.StandardOutput=$reader}
        $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @('fictional') $taskSandbox -TimeoutSeconds 1 `
            -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_read_failed'
        if(-not $rejected -or $receipts.Count -ne 1 -or $receipts[0].PSObject.Properties.Name -cnotcontains 'StandardOutputCapture') { return $false }
        $capture=if($case -ceq 'stderr_io'){$receipts[0].StandardErrorCapture}else{$receipts[0].StandardOutputCapture}
        $receiptJson=$receipts[0] | ConvertTo-Json -Depth 8 -Compress
        $expectedKind=switch($kind) { io {'IoFailure'}; disposed {'ReaderDisposed'}; decode {'DecodeFailure'}; invalid {'ReadFailure'}; cancel {'Canceled'}; delayed_io {'IoFailure'}; delayed_cancel {'Canceled'} }
        $rejected -and (SafeCapture $capture) -and $capture.FailureKind -ceq $expectedKind -and $capture.ReadStarted -and
            -not $capture.EofCompleted -and -not $receipts[0].OutputComplete -and $receipts[0].CleanupConfirmed -and
            $capture.ObservedCharacters -eq $prefix.Length -and
            $capture.Canceled -eq ($kind -cin @('cancel','delayed_cancel')) -and -not $fake.Tree -and
            -not $receiptJson.Contains('fictional_exception_message') -and -not $receiptJson.Contains('fictional_reader_body')
    }
}
Check 'StdinCloseFailureIsStableAndStartsNoOutputReaders' {
    $fake=FakeProcess normal; $fake.StandardInput=[DangoProfileCloseFaultWriter]::new(); $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 `
        -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_input_close_failed'
    $rejected -and $receipts.Count -eq 1 -and $receipts[0].ReaderTasksStarted -eq 0 -and
        -not $receipts[0].OutputComplete -and $receipts[0].CleanupConfirmed -and $fake.Disposed
}
foreach ($length in @(131072,131073)) {
    Check ('DefaultCompleteEofAsciiBoundary_'+$length) {
        $fake=FakeProcess normal; $fake.StandardOutput=[IO.StringReader]::new(('x'*$length)); $receipts=[Collections.Generic.List[object]]::new()
        if($length -eq 131072) {
            $result=Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity
            $result.Output.Length -eq $length -and $receipts[0].OutputComplete -and (SafeCapture $receipts[0].StandardOutputCapture) -and
                $receipts[0].StandardOutputCapture.EofCompleted -and $receipts[0].StandardOutputCapture.FailureKind -ceq 'None'
        } else {
            $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_bound_exceeded'
            $rejected -and (SafeCapture $receipts[0].StandardOutputCapture) -and
                $receipts[0].StandardOutputCapture.FailureKind -ceq 'OutputLimit' -and -not $receipts[0].OutputComplete
        }
    }
}
# A4 resource cleanup is a separate fact from a process exit. These injected
# processes deliberately do not close readers in Process.Dispose. A pending
# TextReader must never be closed while its ReadAsync task is still running.
Check 'CompleteEofExplicitlyClosesBothReadersAfterTasksSettle' {
    $fake=FakeProcess normal
    $stdout=[DangoProfileTrackedReader]::new('ok'); $stderr=[DangoProfileTrackedReader]::new('')
    $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity
    $r=$receipts[0]
    $result.Output -ceq 'ok' -and $stdout.Closed -and $stderr.Closed -and $stdout.CloseCalls -eq 1 -and $stderr.CloseCalls -eq 1 -and
        $r.StandardOutputTaskSettled -and $r.StandardErrorTaskSettled -and $r.OutputReaderTasksSettled -and
        $r.StandardOutputClosed -and $r.StandardErrorClosed -and $r.OutputReadersClosed -and $r.OutputResourcesCleanupConfirmed -and
        -not $r.StandardOutputCloseFailed -and -not $r.StandardErrorCloseFailed -and $r.CleanupConfirmed -and $fake.Disposed
}
foreach($side in @('stdout','stderr','both')) {
    Check ('SuccessfulEofCannotHideIndependentCloseFailure_'+$side) {
        $fake=FakeProcess normal
        $stdout=[DangoProfileTrackedReader]::new('ok'); $stderr=[DangoProfileTrackedReader]::new('')
        $stdout.FailClose=$side -cin @('stdout','both'); $stderr.FailClose=$side -cin @('stderr','both')
        $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
        $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_close_unconfirmed'
        $r=$receipts[0]; $json=$r | ConvertTo-Json -Depth 8 -Compress
        $rejected -and $r.Failure -ceq 'profile_run_tool_output_close_unconfirmed' -and $r.CleanupConfirmed -and $r.OutputReaderTasksSettled -and -not $r.OutputResourcesCleanupConfirmed -and
            $r.StandardOutputCloseFailed -eq $stdout.FailClose -and $r.StandardErrorCloseFailed -eq $stderr.FailClose -and
            $r.StandardOutputClosed -eq (-not $stdout.FailClose) -and $r.StandardErrorClosed -eq (-not $stderr.FailClose) -and
            $stdout.CloseCalls -eq 1 -and $stderr.CloseCalls -eq 1 -and -not $json.Contains('fictional_close_failure_message')
    }
}
Check 'OriginalReadFailureSurvivesSeparateReaderCloseFailure' {
    $fake=FakeProcess normal; $stdout=[DangoProfileFaultReader]::new('io'); $stdout.FailClose=$true
    $stderr=[DangoProfileTrackedReader]::new(''); $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_read_failed'
    $r=$receipts[0]
    $rejected -and $r.Failure -ceq 'profile_run_tool_output_read_failed' -and $r.OutputCloseSnapshotTaken -and
        $r.StandardOutputFailureBeforeDispose -ceq 'IoFailure' -and $r.StandardOutputCapture.FailureKind -ceq 'IoFailure' -and
        $r.FinalReadFailureCode -ceq 'profile_run_tool_output_read_failed' -and $r.StandardOutputTaskSettled -and $r.StandardOutputCloseFailed -and
        -not $r.OutputResourcesCleanupConfirmed -and $r.CleanupConfirmed -and $stdout.CloseCalls -eq 1 -and $stderr.Closed
}
Check 'OriginalOutputLimitSurvivesSeparateReaderCloseFailure' {
    $fake=FakeProcess normal; $stdout=[DangoProfileTrackedReader]::new(('x'*131073)); $stdout.FailClose=$true
    $stderr=[DangoProfileTrackedReader]::new(''); $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_bound_exceeded'
    $r=$receipts[0]
    $rejected -and $r.Failure -ceq 'profile_run_tool_output_bound_exceeded' -and $r.OutputCloseSnapshotTaken -and
        $r.StandardOutputFailureBeforeDispose -ceq 'OutputLimit' -and $r.StandardOutputCapture.FailureKind -ceq 'OutputLimit' -and
        $r.FinalReadFailureCode -ceq 'profile_run_tool_output_bound_exceeded' -and $r.StandardOutputCloseFailed -and
        -not $r.OutputResourcesCleanupConfirmed -and $r.CleanupConfirmed -and $stdout.CloseCalls -eq 1 -and $stderr.Closed
}
foreach($primary in @('input_close','identity','timeout')) {
    Check ('OriginalPrimaryFailureSurvivesReaderCloseFailure_'+$primary) {
        $scenario=switch($primary){identity {'unknown_image'}; timeout {'timeout'}; default {'normal'}}
        $code=switch($primary){identity {'profile_run_tool_identity_unconfirmed'}; timeout {'profile_run_tool_timeout'}; default {'profile_run_tool_input_close_failed'}}
        $fake=FakeProcess $scenario; if($primary -ceq 'input_close'){$fake.StandardInput=[DangoProfileCloseFaultWriter]::new()}
        $stdout=[DangoProfileTrackedReader]::new('ok'); $stdout.FailClose=$true; $stderr=[DangoProfileTrackedReader]::new('')
        $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
        $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } $code
        $r=$receipts[0]
        $rejected -and $r.Failure -ceq $code -and $r.StandardOutputCloseFailed -and -not $r.OutputResourcesCleanupConfirmed -and $stderr.Closed -and
            $stdout.CloseCalls -eq 1 -and $r.CleanupConfirmed -eq ($primary -cne 'identity') -and
            $r.ReaderTasksStarted -eq $(if($primary -ceq 'timeout'){2}else{0})
    }
}
foreach($pending in @('stdout','both')) {
    Check ('ExitedProcessDoesNotProvePendingReaderCleanup_'+$pending) {
        $fake=FakeProcess normal; $stdout=[DangoProfileUnsettledReader]::new()
        $stderr=if($pending -ceq 'both'){[DangoProfileUnsettledReader]::new()}else{[DangoProfileTrackedReader]::new('')}
        $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
        $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_output_incomplete'
        $r=$receipts[0]
        $rejected -and $r.Failure -ceq 'profile_run_tool_output_incomplete' -and $r.Exited -and $r.CleanupConfirmed -and $fake.Disposed -and -not $r.StandardOutputTaskSettled -and
            $r.StandardOutputCloseSkippedPending -and -not $r.StandardOutputClosed -and $stdout.CloseCalls -eq 0 -and
            -not $r.OutputReaderTasksSettled -and -not $r.OutputResourcesCleanupConfirmed -and
            $r.StandardErrorTaskSettled -eq ($pending -ceq 'stdout') -and $r.StandardErrorClosed -eq ($pending -ceq 'stdout') -and
            $r.StandardErrorCloseSkippedPending -eq ($pending -ceq 'both') -and $stderr.CloseCalls -eq $(if($pending -ceq 'both'){0}else{1})
    }
}
Check 'RejectedStartHasNoOutputResourcesToClose' {
    $fake=FakeProcess normal; $fake.StartAccepted=$false
    $stdout=[DangoProfileTrackedReader]::new('ok'); $stderr=[DangoProfileTrackedReader]::new('')
    $fake.StandardOutput=$stdout; $fake.StandardError=$stderr; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity } 'profile_run_tool_start_failed'
    $rejected -and -not $receipts[0].Started -and $receipts[0].ReaderTasksStarted -eq 0 -and
        $receipts[0].OutputResourcesCleanupConfirmed -and $stdout.CloseCalls -eq 0 -and $stderr.CloseCalls -eq 0
}
Check 'IgnoredStartupWithNoReadTasksExplicitlyClosesBothResources' {
    $fake=PendingPipeProcess $taskPipeControl; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort
    $r=$receipts[0]
    $result.ExitCode -eq 0 -and $r.ReaderTasksStarted -eq 0 -and $r.OutputReaderTasksSettled -and $r.OutputReadersClosed -and
        $r.OutputResourcesCleanupConfirmed -and $fake.StandardOutput.Closed -and $fake.StandardError.Closed
}
Check 'IgnoredStartupCloseFailureRemainsUnconfirmedWithoutRetry' {
    $fake=PendingPipeProcess $taskPipeControl; $stdout=[DangoProfileTrackedReader]::new(''); $stdout.FailClose=$true
    $fake.StandardOutput=$stdout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskPipeControl (StartPipeArguments $taskPlan $taskPipePort) $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake} -IdentityProbe $taskFakeIdentity -OutputContract PostgresStartIgnored -PostgresStartPlan $taskPlan -PostgresStartPort $taskPipePort } 'profile_run_tool_output_close_unconfirmed'
    $r=$receipts[0]
    $rejected -and $r.CleanupConfirmed -and $r.StandardOutputCloseFailed -and $r.StandardErrorClosed -and
        -not $r.OutputResourcesCleanupConfirmed -and $stdout.CloseCalls -eq 1
}
foreach($fail in @($false,$true)) {
    Check ('OutputCloseFactsPreventRepeatedClose_'+$fail) {
        $stdout=[DangoProfileTrackedReader]::new(''); $stdout.FailClose=$fail; $stderr=[DangoProfileTrackedReader]::new('')
        $facts=Close-DangoProfileProcessOutput -OutputReader $stdout -ErrorReader $stderr -OutputTask ([Threading.Tasks.Task]::CompletedTask) -ErrorTask ([Threading.Tasks.Task]::CompletedTask)
        $again=Close-DangoProfileProcessOutput -OutputReader $stdout -ErrorReader $stderr -OutputTask ([Threading.Tasks.Task]::CompletedTask) -ErrorTask ([Threading.Tasks.Task]::CompletedTask) -PreviousClose $facts
        $stdout.CloseCalls -eq 1 -and $stderr.CloseCalls -eq 1 -and $again.StandardOutputCloseFailed -eq $fail -and
            $again.StandardOutputClosed -eq (-not $fail) -and $again.OutputResourcesCleanupConfirmed -eq (-not $fail)
    }
}
Check 'OutputCloseHelperSkipsUnsettledTasksAndClosesOtherStream' {
    $stdout=[DangoProfileUnsettledReader]::new(); $stderr=[DangoProfileTrackedReader]::new('')
    $pending=$stdout.ReadAsync([char[]]::new(1),0,1)
    $facts=Close-DangoProfileProcessOutput -OutputReader $stdout -ErrorReader $stderr -OutputTask $pending -ErrorTask ([Threading.Tasks.Task]::CompletedTask)
    -not $facts.StandardOutputTaskSettled -and $facts.StandardOutputCloseSkippedPending -and $stdout.CloseCalls -eq 0 -and
        $facts.StandardErrorTaskSettled -and $facts.StandardErrorClosed -and $stderr.CloseCalls -eq 1 -and -not $facts.OutputResourcesCleanupConfirmed
}
foreach($state in @('faulted','canceled')) {
    Check ('SettledUnsuccessfulTaskCanCloseItsReader_'+$state) {
        $stdout=[DangoProfileTrackedReader]::new(''); $stderr=[DangoProfileTrackedReader]::new('')
        $task=if($state -ceq 'faulted'){[Threading.Tasks.Task]::FromException([IO.IOException]::new('fictional_safe_fault'))}else{[Threading.Tasks.Task]::FromCanceled([Threading.CancellationToken]::new($true))}
        $facts=Close-DangoProfileProcessOutput -OutputReader $stdout -ErrorReader $stderr -OutputTask $task -ErrorTask ([Threading.Tasks.Task]::CompletedTask)
        $facts.OutputReaderTasksSettled -and $facts.OutputResourcesCleanupConfirmed -and $stdout.Closed -and $stderr.Closed
    }
}
Check 'OutputCloseHelperMissingReadersDoesNotClaimCleanup' {
    $facts=Close-DangoProfileProcessOutput -OutputReader $null -ErrorReader $null -OutputTask $null -ErrorTask $null
    -not $facts.OutputResourcesCleanupConfirmed -and $facts.OutputReaderTasksSettled -and -not $facts.OutputReadersClosed
}

# Only the uniquely recognized pure aggregation RHS is evaluated, with fictional
# receipts. The runner entry and its lifecycle, database and file operations do
# not execute in this suite.
foreach($case in @('none','all_closed','process_closed_output_open','mixed_output_open')) {
    Check ('RunnerOutputAggregationIndependentlyChecksEveryTool_'+$case) {
        $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
        $assignments=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and $n.Left.VariablePath.UserPath -ceq 'taskProfileOutputCleanup' -and
            $n.Right.Extent.Text -match 'OutputResourcesCleanupConfirmed'},$true))
        if($errors.Count -ne 0 -or $assignments.Count -ne 1){return $false}
        $rhs=$assignments[0].Right.Extent.Text
        if(($rhs -replace '\s','') -cne '@($taskProfileTools|Where-Object{-not$_.OutputResourcesCleanupConfirmed}).Count-eq0'){return $false}
        $closed=[pscustomobject]@{CleanupConfirmed=$true;OutputResourcesCleanupConfirmed=$true}
        $open=[pscustomobject]@{CleanupConfirmed=$true;OutputResourcesCleanupConfirmed=$false}
        $taskProfileTools=switch($case){none {@()}; all_closed {@($closed)}; process_closed_output_open {@($open)}; mixed_output_open {@($closed,$open)}}
        $observed=& ([scriptblock]::Create($rhs))
        $observed -eq ($case -cin @('none','all_closed'))
    }
}
Check 'RunnerResourceReceiptSeparatelyReportsProcessAndOutputCleanup' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $writes=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'Write-DangoProfileJson' -and
        $n.Extent.Text -match "'resource-receipt.json'"},$true))
    $errors.Count -eq 0 -and $writes.Count -eq 1 -and $writes[0].Extent.Text -match 'PostgreSqlCleanupConfirmed=\$taskProfileCleanup\s*;' -and
        $writes[0].Extent.Text -match 'ToolOutputResourcesCleanupConfirmed=\$taskProfileOutputCleanup\s*;' -and
        $writes[0].Extent.Text -match 'ToolProcesses=@\(\$taskProfileTools\)\s*;'
}
Check 'RunnerOutputCleanupHasItsOwnNoRetryGateBeforeSuccess' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $gates=@($ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.IfStatementAst] -and $_.Extent.Text -match '^if \(-not \$taskProfileOutputCleanup\)'})
    $throws=@(if($gates.Count -eq 1){$gates[0].FindAll({param($n) $n -is [Management.Automation.Language.ThrowStatementAst]},$true)})
    $success=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'Write-Output' -and
        $n.Extent.Text -match 'profile_run_seven_tests_passed_data_retained'},$true))
    $errors.Count -eq 0 -and $throws.Count -eq 1 -and $throws[0].Pipeline.Extent.Text -ceq "'profile_run_output_cleanup_unconfirmed_no_retry'" -and
        $success.Count -eq 1 -and $gates[0].Extent.StartOffset -lt $success[0].Extent.StartOffset
}

function EvidencePipeArguments($Plan,[int]$Port) {
    @('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$Port,
        '-U',$Plan.Role,'-d',$Plan.Database,'-c',(Get-DangoProfileDatabaseSql))
}
$taskEvidenceExe=Join-Path $taskBin 'psql.exe'; $taskEvidencePort=23456
# A deterministic 32-byte fake value satisfies shape checks without reading a secret.
$taskEvidenceEnvironment=@{PGPASSWORD=[Convert]::ToBase64String([byte[]](0..31));PGCLIENTENCODING='UTF8'}
foreach ($case in @('ascii_131073','ascii_1048576','ascii_1048577','crlf_exact','crlf_over','utf8_exact','utf8_over','split_pair','split_pairs_exact','split_pairs_over','stderr_exact','stderr_over')) {
    Check ('FixedEvidenceStreamBoundary_'+$case) {
        $fake=FakeProcess normal; $fake.Executable=$taskEvidenceExe; $receipts=[Collections.Generic.List[object]]::new(); $failure=$false
        $expectedCharacters=0; $expectedBytes=0
        switch($case) {
            ascii_131073 {$text='x'*131073;$expectedCharacters=131073;$expectedBytes=131073}
            ascii_1048576 {$text='x'*1048576;$expectedCharacters=1048576;$expectedBytes=1048576}
            ascii_1048577 {$text='x'*1048577;$failure=$true}
            crlf_exact {$text=('x'*1048574)+"`r`n";$expectedCharacters=1048576;$expectedBytes=1048576}
            crlf_over {$text=('x'*1048575)+"`r`n";$failure=$true}
            utf8_exact {$text=('汉'*349525)+'a';$expectedCharacters=349526;$expectedBytes=1048576}
            utf8_over {$text=('汉'*349525)+'aa';$failure=$true}
            split_pair {$text=[char]::ConvertFromUtf32(0x1F600);$expectedCharacters=2;$expectedBytes=4}
            split_pairs_exact {$text=[char]::ConvertFromUtf32(0x1F600)*262144;$expectedCharacters=524288;$expectedBytes=1048576}
            split_pairs_over {$text=([char]::ConvertFromUtf32(0x1F600)*262144)+'a';$failure=$true}
            stderr_exact {$text='ok';$fake.StandardError=[IO.StringReader]::new(('e'*131072));$expectedCharacters=2;$expectedBytes=2}
            stderr_over {$text='ok';$fake.StandardError=[IO.StringReader]::new(('e'*131073));$failure=$true}
        }
        $chunk=if($case -ceq 'split_pair'){1}elseif($case -clike 'split_pairs_*'){4095}else{4096}
        $fake.StandardOutput=[DangoProfileChunkReader]::new($text,$chunk)
        $invoke={Invoke-DangoProfileProcess $taskEvidenceExe (EvidencePipeArguments $taskPlan $taskEvidencePort) $taskSandbox `
            -EnvironmentValues $taskEvidenceEnvironment -TimeoutSeconds 2 -Receipts $receipts -ProcessFactory {param($info) $fake} `
            -IdentityProbe $taskFakeIdentity -OutputContract PostgresEvidenceJson -PostgresEvidencePlan $taskPlan -PostgresEvidencePort $taskEvidencePort}
        if($failure) {
            $rejected=Rejected $invoke 'profile_run_tool_output_bound_exceeded'
            if(-not $rejected -or $receipts.Count -ne 1 -or $receipts[0].PSObject.Properties.Name -cnotcontains 'StandardOutputCapture') { return $false }
            $capture=if($case -ceq 'stderr_over'){$receipts[0].StandardErrorCapture}else{$receipts[0].StandardOutputCapture}
            $rejected -and (SafeCapture $capture) -and $capture.FailureKind -ceq 'OutputLimit' -and
                -not $receipts[0].OutputComplete -and $receipts[0].StandardErrorCapture.MaximumCharacters -eq 131072
        } else {
            $result=& $invoke
            $result.Output -ceq $text.Trim() -and $receipts[0].OutputContract -ceq 'PostgresEvidenceJson' -and $receipts[0].OutputComplete -and
                (SafeCapture $receipts[0].StandardOutputCapture) -and (SafeCapture $receipts[0].StandardErrorCapture) -and
                $receipts[0].StandardOutputCapture.MaximumCharacters -eq 1048576 -and $receipts[0].StandardOutputCapture.MaximumUtf8Bytes -eq 1048576 -and
                $receipts[0].StandardOutputCapture.ObservedCharacters -eq $expectedCharacters -and $receipts[0].StandardOutputCapture.ObservedUtf8Bytes -eq $expectedBytes -and
                $receipts[0].StandardErrorCapture.MaximumCharacters -eq 131072 -and $receipts[0].StandardErrorCapture.MaximumUtf8Bytes -eq 0
        }
    }
}
foreach ($case in @('version','migration','vstest','different_sql','measure_sql','extra_argv','missing_no_password','wrong_host','wrong_argv_port',
    'wrong_user','wrong_database','wrong_workdir','wrong_plan','extra_plan_property','wrong_port','extra_environment','missing_encoding','wrong_encoding',
    'lowercase_encoding_key','missing_password','empty_password','wrong_password_shape','nonstring_password','tree_kill','missing_plan')) {
    Check ('EvidenceOutputContractRejectsBeforeFactory_'+$case) {
        $plan=Get-DangoProfilePlan run20261008b0 $taskBin $taskSandbox; $exe=$taskEvidenceExe; $port=$taskEvidencePort
        $arguments=EvidencePipeArguments $plan $port; $directory=$taskSandbox; $environment=$taskEvidenceEnvironment.Clone(); $tree=$false
        $calls=[pscustomobject]@{Factories=0}
        switch($case) {
            version {$arguments=@('--version')}
            migration {$exe=Join-Path $taskSandbox 'dotnet.exe';$arguments=@('Dango.Server.dll','--formal-migrate')}
            vstest {$exe=Join-Path $taskSandbox 'dotnet.exe';$arguments=@('vstest','tests.dll')}
            different_sql {$arguments[-1]+="`nSELECT 1;"}
            measure_sql {$arguments[-1]=Get-DangoProfileDatabaseSql -MeasureOnly}
            extra_argv {$arguments+=@('--extra')}
            missing_no_password {$arguments[1]='-W'}
            wrong_host {$arguments[8]='0.0.0.0'}
            wrong_argv_port {$arguments[10]='23457'}
            wrong_user {$arguments[12]='foreign'}
            wrong_database {$arguments[14]='foreign'}
            wrong_workdir {$directory=$taskSandbox+'-other'}
            wrong_plan {$plan.CaseBudgets.https.HttpProfilePatch++}
            extra_plan_property {$plan|Add-Member FictionalPolicy 'extra'}
            wrong_port {$port=0}
            extra_environment {$environment.FICTIONAL_EXTRA='not-allowed'}
            missing_encoding {$environment.Remove('PGCLIENTENCODING')}
            wrong_encoding {$environment.PGCLIENTENCODING='LATIN1'}
            lowercase_encoding_key {$environment.Remove('PGCLIENTENCODING');$environment.pgclientencoding='UTF8'}
            missing_password {$environment.Remove('PGPASSWORD')}
            empty_password {$environment.PGPASSWORD=''}
            wrong_password_shape {$environment.PGPASSWORD='fictional-short'}
            nonstring_password {$environment.PGPASSWORD=123}
            tree_kill {$tree=$true}
            missing_plan {$plan=$null}
        }
        $rejected=Rejected { Invoke-DangoProfileProcess $exe $arguments $directory -EnvironmentValues $environment -TimeoutSeconds 1 `
            -KillOwnedTree:$tree -ProcessFactory {param($info) $calls.Factories++;throw 'offline_factory_must_not_run'} `
            -OutputContract PostgresEvidenceJson -PostgresEvidencePlan $plan -PostgresEvidencePort $port } 'profile_run_evidence_output_contract_rejected'
        $rejected -and $calls.Factories -eq 0
    }
}
Check 'Utf8ChildEncodingIsStrictForBothStreamsAndOtherToolsKeepDefault' {
    $info=New-DangoProfileProcessInfo 'fictional.exe' @() $taskSandbox $taskEvidenceEnvironment
    $default=New-DangoProfileProcessInfo 'fictional.exe' @() $taskSandbox @{}
    $rejected=@()
    foreach($encoding in @($info.StandardOutputEncoding,$info.StandardErrorEncoding)) {
        if($null -eq $encoding){return $false}
        try {[void]$encoding.GetString([byte[]](0xC3,0x28));$rejected+=$false}
        catch {$rejected+=($_.Exception.InnerException -is [Text.DecoderFallbackException] -or $_.Exception -is [Text.DecoderFallbackException])}
    }
    $info.StandardOutputEncoding.WebName -ceq 'utf-8' -and $info.StandardErrorEncoding.WebName -ceq 'utf-8' -and
        @($rejected | Where-Object { -not $_ }).Count -eq 0 -and
        $null -eq $default.StandardOutputEncoding -and $null -eq $default.StandardErrorEncoding
}
Check 'EvidenceEofWithUnpairedSurrogateGetsSafeDecodeFailure' {
    $fake=FakeProcess normal; $fake.Executable=$taskEvidenceExe; $fake.StandardOutput=[DangoProfileChunkReader]::new([string][char]0xD83D,1)
    $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskEvidenceExe (EvidencePipeArguments $taskPlan $taskEvidencePort) $taskSandbox `
        -EnvironmentValues $taskEvidenceEnvironment -TimeoutSeconds 2 -Receipts $receipts -ProcessFactory {param($info) $fake} `
        -IdentityProbe $taskFakeIdentity -OutputContract PostgresEvidenceJson -PostgresEvidencePlan $taskPlan -PostgresEvidencePort $taskEvidencePort } 'profile_run_tool_output_read_failed'
    $rejected -and $receipts.Count -eq 1 -and (SafeCapture $receipts[0].StandardOutputCapture) -and
        $receipts[0].StandardOutputCapture.FailureKind -ceq 'DecodeFailure' -and -not $receipts[0].OutputComplete
}
Check 'SmallValidSizeWitnessCannotAuthorizeLaterOversizedSnapshot' {
    Assert-DangoProfileDatabaseEvidenceSize ([pscustomobject]@{utf8Octets=1})
    $fake=FakeProcess normal; $fake.Executable=$taskEvidenceExe; $fake.StandardOutput=[IO.StringReader]::new(('x'*1048577))
    $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $taskEvidenceExe (EvidencePipeArguments $taskPlan $taskEvidencePort) $taskSandbox `
        -EnvironmentValues $taskEvidenceEnvironment -TimeoutSeconds 2 -Receipts $receipts -ProcessFactory {param($info) $fake} `
        -IdentityProbe $taskFakeIdentity -OutputContract PostgresEvidenceJson -PostgresEvidencePlan $taskPlan -PostgresEvidencePort $taskEvidencePort } 'profile_run_tool_output_bound_exceeded'
    $rejected -and $receipts.Count -eq 1 -and $receipts[0].StandardOutputCapture.FailureKind -ceq 'OutputLimit' -and
        -not $receipts[0].OutputComplete -and $receipts[0].CleanupConfirmed
}
Check 'WholeAndMeasureSqlHaveOneIdenticalReadOnlyPayload' {
    $whole=Get-DangoProfileDatabaseSql; $measure=Get-DangoProfileDatabaseSql -MeasureOnly
    $prefix='BEGIN READ ONLY;'; $suffix='COMMIT;'
    if(-not $whole.Trim().StartsWith($prefix) -or -not $whole.Trim().EndsWith($suffix) -or
        -not $measure.Trim().StartsWith($prefix) -or -not $measure.Trim().EndsWith($suffix)){return $false}
    $wholeSelect=$whole.Trim().Substring($prefix.Length).Trim()
    $wholeSelect=$wholeSelect.Substring(0,$wholeSelect.Length-$suffix.Length).Trim()
    $measureSelect=$measure.Trim().Substring($prefix.Length).Trim()
    $measureSelect=$measureSelect.Substring(0,$measureSelect.Length-$suffix.Length).Trim()
    if(-not $wholeSelect.StartsWith('SELECT ') -or -not $wholeSelect.EndsWith(';')){return $false}
    $payload=$wholeSelect.Substring(7,$wholeSelect.Length-8)
    $measureSelect -ceq ("SELECT json_build_object('utf8Octets',octet_length(convert_to(($payload)::text,'UTF8')));") -and
        $whole -match 'sha256' -and $whole -match "'profiles'" -and $whole -match "'userIds'" -and
        $whole -notmatch '(?im)^\s*(UPDATE|INSERT|DELETE|ALTER|CREATE|DROP|TRUNCATE|COPY|DO|CALL)\b' -and
        $measure -notmatch '(?im)^\s*(UPDATE|INSERT|DELETE|ALTER|CREATE|DROP|TRUNCATE|COPY|DO|CALL)\b'
}
Check 'RunnerSnapshotAstMeasuresValidatesPreservesSizeThenReadsWithHardCap' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $helpers=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Invoke-DangoProfileSnapshot'},$true))
    if($errors.Count -ne 0 -or $helpers.Count -ne 1){return $false}
    $commands=@($helpers[0].FindAll({param($n) $n -is [Management.Automation.Language.CommandAst]},$true))
    $sql=@($commands|Where-Object {$_.GetCommandName() -ceq 'Get-DangoProfileDatabaseSql'})
    $reads=@($commands|Where-Object {$_.GetCommandName() -ceq 'Invoke-DangoProfileProcess'})
    $validation=@($commands|Where-Object {$_.GetCommandName() -ceq 'Assert-DangoProfileDatabaseEvidenceSize'})
    $writes=@($commands|Where-Object {$_.GetCommandName() -ceq 'Write-DangoProfileJson'})
    $loops=@($helpers[0].FindAll({param($n) $n -is [Management.Automation.Language.LoopStatementAst]},$true))
    $sql.Count -eq 1 -and $sql[0].Extent.Text -match '-MeasureOnly' -and $reads.Count -eq 2 -and
        $reads[0].Extent.Text -notmatch '-OutputContract' -and
        $reads[1].Extent.Text -match '-OutputContract PostgresEvidenceJson' -and
        $reads[1].Extent.Text -match '-PostgresEvidencePlan \$taskProfilePlan' -and $reads[1].Extent.Text -match '-PostgresEvidencePort \$taskProfileFacts.Port' -and
        $validation.Count -eq 1 -and $writes.Count -eq 1 -and $writes[0].Extent.Text -match '-CreateNew' -and
        $writes[0].Extent.Text -match '\-size\.json' -and $loops.Count -eq 0 -and
        $reads[0].Extent.StartOffset -lt $validation[0].Extent.StartOffset -and
        $validation[0].Extent.StartOffset -lt $writes[0].Extent.StartOffset -and $writes[0].Extent.StartOffset -lt $reads[1].Extent.StartOffset -and
        @($reads | Where-Object { $_.Extent.Text -notmatch "PGCLIENTENCODING='UTF8'" -or $_.Extent.Text -notmatch 'PGPASSWORD=\$taskProfilePassword' }).Count -eq 0
}
Check 'OnlyThreeOldSnapshotsUseHelperAndOtherToolsKeepDefaultContractAst' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst]},$true))
    $snapshots=@($calls | Where-Object { $_.GetCommandName() -ceq 'Invoke-DangoProfileSnapshot' })
    $modes=@($calls | Where-Object { $_.GetCommandName() -ceq 'Invoke-DangoProfileProcess' -and $_.Extent.Text -match '-OutputContract PostgresEvidenceJson' })
    $phases=@($snapshots | ForEach-Object { if($_.CommandElements.Count -eq 2 -and $_.CommandElements[1] -is [Management.Automation.Language.StringConstantExpressionAst]){$_.CommandElements[1].Value}else{'rejected'} })
    $errors.Count -eq 0 -and $snapshots.Count -eq 3 -and ($phases -join ',') -ceq 'before,migration,after' -and $modes.Count -eq 1 -and
        $modes[0].Extent.Text -match '\$taskProfilePsqlArgs' -and
        @($calls | Where-Object { $_.GetCommandName() -ceq 'Invoke-DangoProfileProcess' -and $_.Extent.Text -match '\$taskProfileScopedArgs' -and $_.Extent.Text -match '-OutputContract' }).Count -eq 0
}
foreach ($value in @(1,1048574,[long]1048574)) {
    Check ('TinyUtf8SizeAllowsIntegerBoundary_'+$value+'_'+$value.GetType().Name) {
        Assert-DangoProfileDatabaseEvidenceSize ([pscustomobject]@{utf8Octets=$value}); $true
    }
}
foreach ($case in @('zero','negative','over_by_one','whole_cap','huge','string','double','boolean','null','missing','extra','wrong_case','array','body')) {
    Check ('TinyUtf8SizeRejectsUntrustedWitness_'+$case) {
        $value=[pscustomobject]@{utf8Octets=1}
        switch($case) {
            zero {$value.utf8Octets=0};negative {$value.utf8Octets=-1};over_by_one {$value.utf8Octets=1048575};whole_cap {$value.utf8Octets=1048576}
            huge {$value.utf8Octets=[long]::MaxValue};string {$value.utf8Octets='1'};double {$value.utf8Octets=[double]1};boolean {$value.utf8Octets=$true}
            null {$value.utf8Octets=$null};missing {$value=[pscustomobject]@{}};extra {$value|Add-Member Body 'fictional_reader_body'}
            wrong_case {$value=[pscustomobject]@{Utf8Octets=1}};array {$value=@($value,$value)};body {$value='{"utf8Octets":1}'}
        }
        Rejected { Assert-DangoProfileDatabaseEvidenceSize $value } 'profile_run_database_evidence_size_rejected'
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

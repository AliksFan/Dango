[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'))
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
$taskRunner=Join-Path $PSScriptRoot 'invoke-account-profile-postgres-tests.ps1'
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
 public int Id => 30000; public DateTime StartTime {get;}=DateTime.UtcNow; public int ExitCode=>0;
 public DangoProfileFakeModule MainModule => new DangoProfileFakeModule { FileName=Scenario=="changed" && Waits>0? Executable+"other":Executable };
 public TextWriter StandardInput {get;}=new StringWriter();
 public TextReader StandardOutput {get;set;}=new StringReader("ok"); public TextReader StandardError {get;}=new StringReader("");
 public bool Start()=>true; public bool WaitForExit(int milliseconds) {Waits++; return Killed || Scenario=="normal";}
 public void Kill(bool tree){Killed=true;Tree=tree;} public void Dispose(){Disposed=true;}
}
'@
}
function FakeProcess([string]$Scenario, [switch]$Overflow) {
    $fake=[DangoProfileFakeProcess]::new(); $fake.Scenario=$Scenario; $fake.Executable=Join-Path $taskSandbox 'fake.exe'
    if ($Overflow) { $fake.StandardOutput=[IO.StringReader]::new(('x'*131073)) }
    return $fake
}
Check 'FakeNormalProcessProducesCapabilityReceipt' {
    $fake=FakeProcess normal; $receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable @('only') $taskSandbox -Receipts $receipts -ProcessFactory {param($info) $fake}
    $result.Output -ceq 'ok' -and $receipts.Count -eq 1 -and $receipts[0].IdentityVerified -and $receipts[0].CleanupConfirmed -and $fake.Disposed
}
Check 'PgCtlTimeoutKillsOnlyOwnedToolHandle' {
    $fake=FakeProcess timeout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} } 'profile_run_tool_timeout'
    $rejected -and $fake.Killed -and -not $fake.Tree -and $receipts[0].CleanupConfirmed
}
Check 'VstestTimeoutMayKillOwnedTestTreeWithoutClaimingFixtureDisposal' {
    $fake=FakeProcess timeout; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -KillOwnedTree -Receipts $receipts -ProcessFactory {param($info) $fake} } 'profile_run_tool_timeout'
    $rejected -and $fake.Killed -and $fake.Tree -and -not ($receipts[0].PSObject.Properties.Name -contains 'NativeCleanupConfirmed')
}
Check 'IdentityDriftDoesNotKillUnprovenProcess' {
    $fake=FakeProcess changed; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} } 'profile_run_tool_timeout'
    $rejected -and -not $fake.Killed -and -not $receipts[0].CleanupConfirmed
}
Check 'ReaderStopsAtOutputBound' {
    $fake=FakeProcess overflow -Overflow; $receipts=[Collections.Generic.List[object]]::new()
    $rejected=Rejected { Invoke-DangoProfileProcess $fake.Executable @() $taskSandbox -TimeoutSeconds 1 -Receipts $receipts -ProcessFactory {param($info) $fake} } 'profile_run_tool_output_bound_exceeded'
    $rejected -and $fake.Killed -and -not $fake.Tree
}
Check 'OnlyVstestRequestsTreeKill' {
    $tokens=$null; $errors=$null; $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    $calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'Invoke-DangoProfileProcess'},$true))
    $treeCalls=@($calls | Where-Object { $_.Extent.Text -match '-KillOwnedTree' })
    $treeCalls.Count -eq 1 -and $treeCalls[0].Extent.Text -match "@\('vstest'"
}

$taskNow=[DateTime]::UtcNow; $taskPg=Join-Path $taskBin 'postgres.exe'; $taskData=Join-Path $taskSandbox 'fictional-pgdata'
$taskProof=[pscustomobject]@{ControlExitCode=0;Pid=1234;CreatedAt=$taskNow;Executable=$taskPg;
    Arguments=@($taskPg,'-D',$taskData,'-h','127.0.0.1','-p','23456');ParentProcessId=2222;ControlPid=2222;ControlIdentityVerified=$true}
function Proven($proof) { Test-DangoProfilePostgresProof $proof $taskPg $taskData 23456 $taskNow.AddSeconds(-1) $taskNow.AddSeconds(1) }
Check 'SuccessfulAndFailedStartRequireExactPgProof' {
    $success=Proven $taskProof; $copy=$taskProof | ConvertTo-Json | ConvertFrom-Json; $copy.CreatedAt=$taskNow; $copy.ControlExitCode=-1
    $success -and (Proven $copy)
}
foreach ($case in @('foreign_parent','reused_pid_time','future_time','suffix_path','foreign_host','extra_argument','wrong_executable','unverified_control')) {
    Check ('RejectsPgProof_'+$case) {
        $copy=$taskProof | ConvertTo-Json | ConvertFrom-Json; $copy.CreatedAt=$taskNow
        switch ($case) {
            foreign_parent { $copy.ControlExitCode=-1; $copy.ParentProcessId=3333 }
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

[CmdletBinding()]
param([string]$Repository=(Split-Path -Parent $PSScriptRoot),[string]$RunnerOverride)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $Repository 'eng/Dango.AccountProfilePostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Repository 'eng/Dango.AccountSyncPostgres.psm1') -Force -DisableNameChecking
$artifactRoot=Join-Path $Repository 'artifacts/account-sync-b1a2-runner-offline-20261010'
$runner=if([string]::IsNullOrWhiteSpace($RunnerOverride)){Join-Path $Repository 'eng/invoke-account-sync-b1a-tests.ps1'}else{
    $overridePath=[IO.Path]::GetFullPath($RunnerOverride)
    $overrideRoot=[IO.Path]::GetFullPath($artifactRoot)+[IO.Path]::DirectorySeparatorChar
    if(-not $overridePath.StartsWith($overrideRoot,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($overridePath) -cne '.ps1'){throw 'runner_boundary_mutant_path_rejected'}
    $overridePath
}
Assert-DangoProfileRegularPath $runner
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$errors)
if ($errors.Count -ne 0) {throw 'runner_parser_failed'}
foreach($definition in $ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst]},$true)) {
    Invoke-Expression $definition.Extent.Text
}
$results=[Collections.Generic.List[object]]::new()
function Check {
    param([string]$Name,[scriptblock]$Body)
    & $Body;$results.Add([pscustomobject]@{Name=$Name;Passed=$true})
}
function Reject {
    param([scriptblock]$Body,[string]$Code)
    try{& $Body|Out-Null}catch{if($_.Exception.Message -ceq $Code){return};throw}
    throw 'missing_expected_rejection'
}
function Invoke-DangoProfileProcess {
    param($Executable,$Arguments,$WorkingDirectory,$EnvironmentValues,$TimeoutSeconds,$KillOwnedTree,$Receipts,
        $OutputContract,$PostgresStartPlan,$PostgresStartPort,$PostgresEvidencePlan,$PostgresEvidencePort)
    $record=[pscustomobject]@{Executable=$Executable;Arguments=$Arguments;OutputContract=$OutputContract}
    $Receipts.Add($record);return [pscustomobject]@{ExitCode=0;Receipt=$record;Output='fake'}
}
$taskSyncPlan=[pscustomobject]@{MaximumToolProcesses=10;Repository='unused'}
$taskSyncFacts=[pscustomobject]@{Port=63237};$taskSyncTools=[Collections.Generic.List[object]]::new();$taskSyncStopAttempted=$false
Check wrong_first_step {Reject {Invoke-DangoAccountSyncTool 'vstest' 'fake' @()} 'accountsync_run_tool_budget_rejected'}
Check exact_ten_steps {
    foreach($step in @('version','status','start','before-size','before-full','vstest','after-size','after-full','scoped','stop')) {
        Invoke-DangoAccountSyncTool $step 'fake' @()|Out-Null
    }
    if($taskSyncTools.Count -ne 10 -or -not $taskSyncStopAttempted){throw 'exact_sequence_failed'}
}
Check eleventh_tool {Reject {Invoke-DangoAccountSyncTool 'stop' 'fake' @()} 'accountsync_run_tool_budget_rejected'}
$taskSyncTools=[Collections.Generic.List[object]]::new();$taskSyncStopAttempted=$false
Check partial_failure_stop {
    Invoke-DangoAccountSyncTool 'version' 'fake' @()|Out-Null
    Invoke-DangoAccountSyncTool 'stop' 'fake' @()|Out-Null
    if($taskSyncTools.Count -ne 2){throw 'partial_cleanup_failed'}
}
Check repeated_stop {Reject {Invoke-DangoAccountSyncTool 'stop' 'fake' @()} 'accountsync_run_tool_budget_rejected'}
function Read-DangoProfileLedger {param($Path,$MaximumRecords)return $fakeLedger}
$fakeLedger=@('{"Version":1}')
Check object_ledger {if(@(Read-DangoAccountSyncLedgerRows 'fake' 1).Count -ne 1){throw 'object_ledger_failed'}}
Check ledger_count {Reject {Read-DangoAccountSyncLedgerRows 'fake' 2} 'accountsync_run_ledger_count_rejected'}
$fakeLedger=@('')
Check blank_ledger {Reject {Read-DangoAccountSyncLedgerRows 'fake' 1} 'accountsync_run_ledger_record_bound_rejected'}
$fakeLedger=@('"'+('界'*683)+'"')
Check utf8_ledger {Reject {Read-DangoAccountSyncLedgerRows 'fake' 1} 'accountsync_run_ledger_record_bound_rejected'}
$fakeLedger=@('1')
Check scalar_ledger {Reject {Read-DangoAccountSyncLedgerRows 'fake' 1} 'accountsync_run_ledger_record_rejected'}

$owner=[Guid]::NewGuid();$user=[Guid]::NewGuid()
$base=[pscustomobject]@{Version=1;OwnerId=$owner.ToString('D');Passed=$true;Final=[pscustomobject]@{
    UserId=$user.ToString('D');PetId=[Guid]::NewGuid().ToString('D');Nickname='新同步团子';ProfileVersion=3;
    ConfigurationVersion=6;Sessions=4;Revoked=4;Attempts=0;MailState='Pending';DefaultRoomId=[Guid]::NewGuid().ToString('D')};
    ConfigurationWrites=5;LoginWrites=4;ProfileWrites=2;LogoutWrites=4;DesktopHosts=3;HttpsHosts=1;HttpRequests=128;
    DurationSeconds=299.75;UsesSystemTrust=$false;UsesWindowsCredentials=$false;ActualFormalHost=$true;
    ActualApplicationHost=$true;HealthyNativeFrames=$true;ExactChoiceArchives=3;ExactSyncArchives=3}
function Clone-Journey{return $base|ConvertTo-Json -Depth 8 -Compress|ConvertFrom-Json}
Check valid_journey {Assert-DangoAccountSyncJourneyEvidence (Clone-Journey) $owner $user}
foreach($mutation in @(
    @{Name='extra_journey_field';Apply={param($e)$e|Add-Member 'Unexpected' 1};Code='accountsync_run_journey_shape_rejected'},
    @{Name='missing_journey_field';Apply={param($e)$e.PSObject.Properties.Remove('Passed')};Code='accountsync_run_journey_shape_rejected'},
    @{Name='string_integer';Apply={param($e)$e.ConfigurationWrites='5'}},
    @{Name='boolean_integer';Apply={param($e)$e.Version=$true}},
    @{Name='string_boolean';Apply={param($e)$e.Passed='True'}},
    @{Name='false_native';Apply={param($e)$e.HealthyNativeFrames=$false}},
    @{Name='decimal_profile_version';Apply={param($e)$e.Final.ProfileVersion=3.0}},
    @{Name='wrong_owner';Apply={param($e)$e.OwnerId=[Guid]::NewGuid().ToString('D')}},
    @{Name='wrong_user';Apply={param($e)$e.Final.UserId=[Guid]::NewGuid().ToString('D')}},
    @{Name='empty_pet';Apply={param($e)$e.Final.PetId=[Guid]::Empty.ToString('D')}},
    @{Name='malformed_room';Apply={param($e)$e.Final.DefaultRoomId='other'}},
    @{Name='sent_email';Apply={param($e)$e.Final.MailState='Sent'}},
    @{Name='nickname_mismatch';Apply={param($e)$e.Final.Nickname='other'}},
    @{Name='session_not_revoked';Apply={param($e)$e.Final.Revoked=3}},
    @{Name='wrong_archive_count';Apply={param($e)$e.ExactChoiceArchives=4}},
    @{Name='zero_http';Apply={param($e)$e.HttpRequests=0}},
    @{Name='too_many_http';Apply={param($e)$e.HttpRequests=513}},
    @{Name='string_http';Apply={param($e)$e.HttpRequests='512'}},
    @{Name='decimal_http';Apply={param($e)$e.HttpRequests=512.0}},
    @{Name='negative_duration';Apply={param($e)$e.DurationSeconds=-0.1}},
    @{Name='duration_limit';Apply={param($e)$e.DurationSeconds=600}},
    @{Name='nan_duration';Apply={param($e)$e.DurationSeconds=[double]::NaN}},
    @{Name='infinite_duration';Apply={param($e)$e.DurationSeconds=[double]::PositiveInfinity}},
    @{Name='string_duration';Apply={param($e)$e.DurationSeconds='1'}}
)) {
    $journey=Clone-Journey;& $mutation.Apply $journey
    $code=if($mutation.ContainsKey('Code')){$mutation.Code}else{'accountsync_run_journey_evidence_rejected'}
    Check $mutation.Name {Reject {Assert-DangoAccountSyncJourneyEvidence $journey $owner $user} $code}
}

Assert-DangoProfileRegularPath $artifactRoot
[void][IO.Directory]::CreateDirectory($artifactRoot)
$evidenceRoot=Join-Path $artifactRoot ('snapshots-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidenceRoot)
function Invoke-DangoAccountSyncTool {
    param($Step,$Executable,$Arguments,$EnvironmentValues,$OutputContract)
    if($Step.EndsWith('-size')){return [pscustomobject]@{ExitCode=0;Output='{"utf8Octets":100}'}}
    return [pscustomobject]@{ExitCode=0;Output=$fakeSnapshot;Receipt=[pscustomobject]@{OutputComplete=$fakeComplete}}
}
function Assert-DangoAccountSyncTarget {
    param($Plan,$Evidence)
    if(-not [IO.File]::Exists((Join-Path $Plan.EvidenceDirectory 'before.json'))){throw 'snapshot_checked_before_preservation'}
    if($Evidence.good -ne 1){throw 'fake_target_rejected'}
}
$taskSyncPlan=[pscustomobject]@{EvidenceDirectory=''};$taskSyncPsql='fake';$taskSyncPsqlArgs=@('-c','fake')
$taskSyncPsqlEnvironment=@{};$fakeComplete=$true;$fakeSnapshot='{"good":1}'
function New-SnapshotCase {
    $taskSyncPlan.EvidenceDirectory=Join-Path $evidenceRoot ([Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($taskSyncPlan.EvidenceDirectory)
}
New-SnapshotCase
Check snapshot_preserved_before_check {
    $result=Invoke-DangoAccountSyncSnapshot 'before'
    if($result.good -ne 1 -or [IO.File]::ReadAllText((Join-Path $taskSyncPlan.EvidenceDirectory 'before.json')) -cne ($fakeSnapshot+"`n")){
        throw 'snapshot_preservation_failed'
    }
}
New-SnapshotCase;$fakeSnapshot='{'
Check malformed_snapshot_preserved {
    $rejected=$false;try{Invoke-DangoAccountSyncSnapshot 'before'|Out-Null}catch{$rejected=$true}
    if(-not $rejected -or [IO.File]::ReadAllText((Join-Path $taskSyncPlan.EvidenceDirectory 'before.json')) -cne "{`n"){
        throw 'malformed_snapshot_not_preserved'
    }
}
New-SnapshotCase;$fakeSnapshot='"'+('界'*350000)+'"'
Check utf8_snapshot_bound {Reject {Invoke-DangoAccountSyncSnapshot 'before'} 'accountsync_run_database_snapshot_bound_rejected'}
New-SnapshotCase;$fakeSnapshot='a'*1048575
Check character_snapshot_bound {Reject {Invoke-DangoAccountSyncSnapshot 'before'} 'accountsync_run_database_snapshot_bound_rejected'}
New-SnapshotCase;$fakeSnapshot='{"good":1}';$fakeComplete=$false
Check incomplete_eof {Reject {Invoke-DangoAccountSyncSnapshot 'before'} 'accountsync_run_database_snapshot_failed'}

$evidence=[pscustomobject]@{Version=1;Passed=$results.Count;Failed=0;ParserPassed=$true;Checks=@($results);
    RealResourceOperations=0;RunnerSha256=(Get-FileHash -LiteralPath $runner).Hash;
    BoundaryScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;ArtifactOnlyMutation=([bool]$RunnerOverride)}
$path=Join-Path $artifactRoot ('result-'+[Guid]::NewGuid().ToString('N')+'.json')
$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($evidence|ConvertTo-Json -Depth 8))
$file=[IO.FileStream]::new($path,'CreateNew','Write','None',4096,[IO.FileOptions]::WriteThrough)
try{$file.Write($bytes);$file.Flush($true)}finally{$file.Dispose()}
$evidence|Select-Object Version,Passed,Failed,ParserPassed,RealResourceOperations,RunnerSha256
$path

[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-profile-postgres-tests.ps1'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module $ModulePath -Force
$taskRepo=Split-Path -Parent $PSScriptRoot
$taskSandbox=Join-Path $taskRepo ('artifacts/protected-graph-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$taskBin=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'
$taskRunner=[IO.Path]::GetFullPath($RunnerPath)
$taskChecks=[Collections.Generic.List[object]]::new()
$taskKeys=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions',
    'audit','outbox','leases','codes','bans','claims','logins','tokens','accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
$taskAccessory=@('accessoryBlobs','accessoryAssetReferences','accessoryCurrentOutfits','accessoryCommitReceipts')
$taskAllowedAdds=@('invitations','registrations','challenges','emailJobs','users','pets','rooms','memberships','petStates','sessions')
$taskSix=@('20261008074701_InitialAccountRoomFoundation','20261008085521_AccountLifecycle','20261009031410_RoomManagement',
    '20261009055558_RoomAccess','20261009062701_RoomAccessIntegrity','20261009124814_AccessoryAccountBackup')
$taskFrozenSources=@('src/Dango.Server/Hosting/FormalEntry.cs','src/Dango.Server/Hosting/FormalSingleMigration.cs',
    'src/Dango.Server/Persistence/DangoDatabaseOptions.cs','src/Dango.Server/Persistence/DangoDbContext.cs','src/Dango.Server/Persistence/Migrations/DangoDbContextModelSnapshot.cs')
function Check([string]$Name,[scriptblock]$Body) {
    $passed=$false;$failure=$null
    try{$passed=[bool](& $Body)}catch{$failure=$_.Exception.Message}
    $taskChecks.Add([pscustomobject]@{Name=$Name;Passed=$passed;Failure=$failure})
}
function Rejected([scriptblock]$Body,[string]$Code) {
    try{[void](& $Body);return $false}catch{return $_.Exception.Message -like ('*'+$Code+'*')}
}
function Plan {Get-DangoProfilePlan run20261008b0 $taskBin $taskSandbox}
function Graph {
    $counts=[ordered]@{};$digests=[ordered]@{}
    foreach($key in $taskKeys){$counts[$key]=[int]1;$digests[$key]=[string[]]@(('a'*64))}
    [pscustomobject]@{counts=[pscustomobject]$counts;digests=[pscustomobject]$digests;userIds=@();profiles=@()}
}
function CloneGraph($Graph) {$Graph|ConvertTo-Json -Depth 7 -Compress|ConvertFrom-Json}
function RunnerAst {
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    if($errors.Count -ne 0){throw 'protected_graph_offline_runner_ast_invalid'}
    return $ast
}
Check 'A5FixedPlanRequiresExactSixHistoryAndNewOwnerDirectory' {
    $plan=Plan
    ($plan.PriorMigrations -join ',') -ceq ($taskSix -join ',') -and $plan.PriorMigrations.Count -eq 6 -and
        (Test-DangoProfilePath $plan.EvidenceDirectory (Join-Path (Split-Path -Parent $plan.StateDirectory) 'run20261010profilea5')) -and
        $plan.ExpectedTests -eq 7 -and $plan.AccountGraphs -eq 9 -and $plan.LoginAttempts -eq 10 -and $plan.ProfileAttempts -eq 19
}
Check 'D1ReadOnlyDiagnosticKeepsItsOriginalFiveHistoryAndOwner' {
    $plan=Get-DangoProfileDiagnosticPlan run20261008b0 $taskBin $taskSandbox
    ($plan.PriorMigrations -join ',') -ceq ($taskSix[0..4] -join ',') -and $plan.ReadOnlyQueries -eq 1 -and $plan.BusinessWriteAttempts -eq 0 -and
        (Test-DangoProfilePath $plan.EvidenceDirectory (Join-Path (Split-Path -Parent $plan.StateDirectory) 'run20261010profiled1'))
}
Check 'ProtectedGraphAcceptsExactlyTwentyTwoCountAndDigestKeys' {
    $graph=Graph;[void](Assert-DangoProfileProtectedGraphShape $graph)
    @($graph.counts.PSObject.Properties).Count -eq 22 -and @($graph.digests.PSObject.Properties).Count -eq 22
}
Check 'ProtectedGraphAllowsEmptyTablesOnlyWithEmptyDigestArrays' {
    $graph=Graph;foreach($key in $taskKeys){$graph.counts.$key=[long]0;$graph.digests.$key=@()}
    [void](Assert-DangoProfileProtectedGraphShape $graph);$true
}
foreach($key in $taskKeys) {
    Check ('ProtectedShapeRejectsMissingCount_'+$key) {
        $graph=Graph;$graph.counts.PSObject.Properties.Remove($key)
        Rejected {Assert-DangoProfileProtectedGraphShape $graph} 'profile_run_protected_graph_shape_rejected'
    }
}
foreach($key in $taskAccessory) {
    Check ('ProtectedShapeRejectsMissingAccessoryDigests_'+$key) {
        $graph=Graph;$graph.digests.PSObject.Properties.Remove($key)
        Rejected {Assert-DangoProfileProtectedGraphShape $graph} 'profile_run_protected_graph_shape_rejected'
    }
}
foreach($case in @('extra_count','extra_digest','wrong_case_count','wrong_case_digest','counts_null','digests_null','count_negative','count_string','count_boolean',
    'count_double','count_null','count_unsigned','digest_scalar','digest_null','digest_object','digest_list','digest_uppercase','digest_short','digest_nonhex',
    'digest_number','digest_null_item','digest_missing_item','digest_extra_item')) {
    Check ('ProtectedShapeRejectsInvalidEvidence_'+$case) {
        $graph=Graph;$key='accessoryBlobs'
        switch($case){extra_count {$graph.counts|Add-Member Extra 0};extra_digest {$graph.digests|Add-Member Extra @()};
            wrong_case_count {$graph.counts.PSObject.Properties.Remove($key);$graph.counts|Add-Member AccessoryBlobs 1};
            wrong_case_digest {$graph.digests.PSObject.Properties.Remove($key);$graph.digests|Add-Member AccessoryBlobs @('a'*64)};
            counts_null {$graph.counts=$null};digests_null {$graph.digests=$null};count_negative {$graph.counts.$key=-1};count_string {$graph.counts.$key='1'};
            count_boolean {$graph.counts.$key=$true};count_double {$graph.counts.$key=[double]1};count_null {$graph.counts.$key=$null};count_unsigned {$graph.counts.$key=[uint32]1};
            digest_scalar {$graph.digests.$key='a'*64};digest_null {$graph.digests.$key=$null};digest_object {$graph.digests.$key=[pscustomobject]@{hash='a'*64}};
            digest_list {$graph.digests.$key=[Collections.Generic.List[string]]::new();$graph.digests.$key.Add('a'*64)};
            digest_uppercase {$graph.digests.$key=@('A'*64)};digest_short {$graph.digests.$key=@('a'*63)};digest_nonhex {$graph.digests.$key=@('g'*64)};
            digest_number {$graph.digests.$key=@(1)};digest_null_item {$graph.digests.$key=@($null)};digest_missing_item {$graph.digests.$key=@()};
            digest_extra_item {$graph.digests.$key=@(('a'*64),('b'*64))}}
        Rejected {Assert-DangoProfileProtectedGraphShape $graph} 'profile_run_protected_graph_shape_rejected'
    }
}
foreach($key in $taskAccessory) {
    foreach($change in @('change','delete','add')) {
        Check ('AccessoryFullRowsRejectEveryUnapprovedChange_'+$key+'_'+$change) {
            $before=Graph;$after=CloneGraph $before
            switch($change){change {$after.digests.$key=@('b'*64)};delete {$after.counts.$key=0;$after.digests.$key=@()};
                add {$after.counts.$key=2;$after.digests.$key=@(('a'*64),('b'*64))}}
            [void](Assert-DangoProfileProtectedGraphShape $before);[void](Assert-DangoProfileProtectedGraphShape $after)
            $code=if($change -ceq 'add'){'profile_run_unapproved_graph_created'}else{'profile_run_old_graph_changed'}
            Rejected {Assert-DangoProfileOldGraph $before $after} $code
        }
    }
}
foreach($key in $taskAllowedAdds) {
    Check ('LegacyAllowedNewBusinessRowsRemainPossible_'+$key) {
        $before=Graph;$after=CloneGraph $before;$after.counts.$key=2;$after.digests.$key=@(('a'*64),('b'*64))
        [void](Assert-DangoProfileProtectedGraphShape $before);[void](Assert-DangoProfileProtectedGraphShape $after)
        Assert-DangoProfileOldGraph $before $after;$true
    }
}
foreach($key in @('audit','outbox','leases','codes','bans','claims','logins','tokens')) {
    Check ('LegacyForbiddenNewRowsStayRejected_'+$key) {
        $before=Graph;$after=CloneGraph $before;$after.counts.$key=2;$after.digests.$key=@(('a'*64),('b'*64))
        [void](Assert-DangoProfileProtectedGraphShape $before);[void](Assert-DangoProfileProtectedGraphShape $after)
        Rejected {Assert-DangoProfileOldGraph $before $after} 'profile_run_unapproved_graph_created'
    }
}
Check 'AccessoryDigestsProtectDuplicateMultiplicity' {
    $before=Graph;$after=CloneGraph $before;$key='accessoryBlobs'
    $before.counts.$key=2;$after.counts.$key=2;$before.digests.$key=@(('a'*64),('a'*64));$after.digests.$key=@(('a'*64),('b'*64))
    [void](Assert-DangoProfileProtectedGraphShape $before);[void](Assert-DangoProfileProtectedGraphShape $after)
    Rejected {Assert-DangoProfileOldGraph $before $after} 'profile_run_old_graph_changed'
}
Check 'AccessoryPreservationUsesMultisetsWithoutRowOrderDependence' {
    $before=Graph;$after=CloneGraph $before;$key='accessoryCommitReceipts'
    $before.counts.$key=2;$after.counts.$key=2;$before.digests.$key=@(('a'*64),('b'*64));$after.digests.$key=@(('b'*64),('a'*64))
    [void](Assert-DangoProfileProtectedGraphShape $before);[void](Assert-DangoProfileProtectedGraphShape $after)
    Assert-DangoProfileOldGraph $before $after;$true
}
Check 'SqlIncludesCompleteGlobalAndPetAccessoryRowsWithoutFieldExclusion' {
    $sql=Get-DangoProfileDatabaseSql
    $tables=@('accessory_blobs','accessory_asset_references','accessory_current_outfits','accessory_commit_receipts')
    @($tables|Where-Object {$sql -notmatch ('FROM dango\.'+$_+'\b')}).Count -eq 0 -and
        @($taskAccessory|Where-Object {$sql -notmatch ("'"+$_+"'")}).Count -eq 0 -and
        $sql -match 'convert_to\(to_jsonb\(t\)::text' -and $sql -notmatch '(?i)(FROM dango\.accessory_\w+[^\n]*WHERE|outfit_json\s*-)' -and
        $sql.StartsWith('BEGIN READ ONLY;') -and $sql -notmatch '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP)\b'
}

# Each selected expression is recognized exactly before evaluation. Only a pure
# join/equality test receives fictional histories; the runner is never invoked.
function HistoryMatches([string[]]$History,[switch]$AfterMigration) {
    $ast=RunnerAst;$taskProfilePlan=Plan
    if($AfterMigration){$taskProfileMigrated=[pscustomobject]@{migrations=$History};$variable='taskProfileMigrated'}
    else{$taskProfileBefore=[pscustomobject]@{migrations=$History};$variable='taskProfileBefore'}
    $nodes=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.BinaryExpressionAst] -and $n.Extent.Text -match ('\$'+$variable+'\.migrations') -and
        $n.Extent.Text -match '\$taskProfilePlan\.PriorMigrations' -and $n.Operator -ceq [Management.Automation.Language.TokenKind]::Cne},$true))
    if($nodes.Count -ne 1){throw 'protected_graph_offline_history_expression_unrecognized'}
    $text=$nodes[0].Extent.Text
    $expected=if($AfterMigration){'(@($taskProfileMigrated.migrations)-join'','')-cne((@($taskProfilePlan.PriorMigrations)+$taskProfilePlan.Migration)-join'','')'}
        else{'(@($taskProfileBefore.migrations)-join'','')-cne($taskProfilePlan.PriorMigrations-join'','')'}
    if(($text-replace '\s','') -cne $expected){throw 'protected_graph_offline_history_expression_unrecognized'}
    return -not (& ([scriptblock]::Create($text)))
}
Check 'BeforeHistoryAcceptsExactlySixReviewedMigrationIds' {HistoryMatches $taskSix}
foreach($case in @('missing_accessory','replace_accessory','extra_history','reorder_history','change_old_history')) {
    Check ('BeforeHistoryRejectsAnyOtherHistory_'+$case) {
        $history=@($taskSix)
        switch($case){missing_accessory {$history=@($taskSix[0..4])};replace_accessory {$history[5]='20261009124814_FictionalOther'};
            extra_history {$history+='20261010055529_AccountProfile'};reorder_history {$history=@($taskSix[1],$taskSix[0])+$taskSix[2..5]};
            change_old_history {$history[0]='20261008074701_FictionalOther'}}
        -not (HistoryMatches $history)
    }
}
Check 'MigrationHistoryAllowsOnlyAccountProfileAfterExactSixIds' {HistoryMatches (@($taskSix)+'20261010055529_AccountProfile') -AfterMigration}
foreach($case in @('missing_profile','other_new_migration','missing_old','extra_new')) {
    Check ('MigrationHistoryRejectsUnreviewedOrLostHistory_'+$case) {
        $history=@($taskSix)+'20261010055529_AccountProfile'
        switch($case){missing_profile {$history=@($taskSix)};other_new_migration {$history[6]='20261010055529_FictionalOther'};
            missing_old {$history=@($history[1..6])};extra_new {$history+='20261011000000_FictionalFuture'}}
        -not (HistoryMatches $history -AfterMigration)
    }
}
Check 'RunnerChecksStrictShapeImmediatelyAfterEachSnapshotParse' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $shapes=@($calls|Where-Object {$_.GetCommandName() -ceq 'Assert-DangoProfileProtectedGraphShape'})
    $variables=@('taskProfileBefore','taskProfileMigrated','taskProfileAfter')
    if($shapes.Count -ne 3){return $false}
    foreach($variable in $variables){
        $assign=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $n.Left.VariablePath.UserPath -ceq $variable -and $n.Right.Extent.Text -match 'ConvertFrom-Json'},$true))
        $shape=@($shapes|Where-Object {$_.Extent.Text -cmatch ('\$'+$variable+'\b')})
        if($assign.Count -ne 1 -or $shape.Count -ne 1 -or $assign[0].Extent.EndOffset -gt $shape[0].Extent.StartOffset){return $false}
        $between=$ast.Extent.Text.Substring($assign[0].Extent.EndOffset,$shape[0].Extent.StartOffset-$assign[0].Extent.EndOffset)
        if(-not [string]::IsNullOrWhiteSpace($between)){return $false}
    }
    $true
}
Check 'RunnerPreservesBeforeSnapshotBeforeTargetComparison' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $write=@($calls|Where-Object {$_.GetCommandName() -ceq 'Write-DangoProfileJson' -and $_.Extent.Text -match "'before.json'" -and $_.Extent.Text -match '-CreateNew'})
    $guards=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match "throw 'profile_run_database_target_rejected'"},$true))
    $write.Count -eq 1 -and $guards.Count -eq 1 -and $write[0].Extent.StartOffset -lt $guards[0].Extent.StartOffset
}

# Freeze inputs only in a fictional repository. No installed executable, state,
# key ring or PostgreSQL directory is read or touched by these manifest checks.
function FictionalReview {
    $plan=Plan;$inputs=Get-DangoProfileReviewInputs $plan
    foreach($relative in @($inputs.MissingInputs)+$taskFrozenSources){
        $path=Join-Path $plan.Repository $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        if(-not [IO.File]::Exists($path)){[IO.File]::WriteAllText($path,'fictional-frozen-source',[Text.UTF8Encoding]::new($false))}
    }
    return $plan
}
Check 'ApprovalInputsIncludeAllFiveMigrationEntrypointAndModelSources' {
    $plan=FictionalReview;$inputs=Get-DangoProfileReviewInputs $plan
    @($taskFrozenSources|Where-Object {-not $inputs.Hashes.Contains($_)}).Count -eq 0 -and $inputs.MissingInputs.Count -eq 0
}
foreach($source in $taskFrozenSources) {
    Check ('ReviewedProfileExecutionSourceChangeRejects_'+[IO.Path]::GetFileName($source)) {
        $plan=FictionalReview;$path=Join-Path $taskSandbox ('artifacts/approval-'+[Guid]::NewGuid().ToString('N')+'.json')
        $approval=New-DangoProfileApprovalManifest $plan $path;$sourcePath=Join-Path $plan.Repository $source
        [IO.File]::AppendAllText($sourcePath,'fictional-change')
        try{Rejected {Assert-DangoProfileReviewInputs $plan $path $approval.Sha256} 'profile_run_approved_input_changed'}
        finally{[IO.File]::WriteAllText($sourcePath,'fictional-frozen-source',[Text.UTF8Encoding]::new($false))}
    }
}
$taskPassed=@($taskChecks|Where-Object Passed).Count
$taskChecks|Select-Object Name,Passed,Failure|Format-Table -AutoSize
Write-DangoProfileJson (Join-Path $taskSandbox 'offline-results.json') ([pscustomobject]@{ExecutedDatabase=$false;RunnerEntryExecuted=$false;Checks=@($taskChecks)}) -CreateNew
if($taskPassed -ne $taskChecks.Count){throw ('profile_run_protected_graph_offline_failed:'+$taskPassed+'/'+$taskChecks.Count)}
Write-Output ('profile_run_protected_graph_offline_passed:'+$taskPassed+'/'+$taskChecks.Count)

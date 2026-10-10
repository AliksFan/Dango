[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-profile-target-diagnostic.ps1'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$taskModule=Import-Module $ModulePath -Force -PassThru
$taskChecks=[Collections.Generic.List[object]]::new()
$taskRepo=Split-Path -Parent $PSScriptRoot
$taskSandbox=Join-Path $taskRepo ('artifacts/diagnostic-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$taskBin=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PostgreSQL/18/bin'
$taskRunner=[IO.Path]::GetFullPath($RunnerPath)
function Check([string]$Name,[scriptblock]$Body) {
    $passed=$false;$failure=$null
    try{$passed=[bool](& $Body)}catch{$failure=$_.Exception.Message}
    $taskChecks.Add([pscustomobject]@{Name=$Name;Passed=$passed;Failure=$failure})
}
function Rejected([scriptblock]$Body,[string]$Code) {
    try{[void](& $Body);return $false}catch{return $_.Exception.Message -like ('*'+$Code+'*')}
}
function Plan { Get-DangoProfileDiagnosticPlan run20261008b0 $taskBin $taskSandbox }
function Metadata($Plan) {
    [pscustomobject]@{database=$Plan.Database;role=$Plan.Role;version=180001;dataDirectory=$Plan.DataDirectory;
        listenAddresses='127.0.0.1';migrations=@($Plan.PriorMigrations);profileColumnsPresent=$false}
}
function Parse($Value) { Assert-DangoProfileDiagnosticEvidence ($Value | ConvertTo-Json -Depth 6 -Compress) }
function RunnerAst {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($taskRunner,[ref]$tokens,[ref]$errors)
    if($errors.Count -ne 0){throw 'diagnostic_offline_runner_ast_invalid'}
    return $ast
}
Check 'DiagnosticPlanHasZeroBusinessAndOneQueryFiveToolLimits' {
    $plan=Plan
    $names=@($plan.PSObject.Properties.Name)
    $expected=@('Version','Stage','RunName','Executed','StateDirectory','DataDirectory','EvidenceDirectory','Database','Role',
        'PostgresBinDirectory','Repository','PriorMigrations','ProfileMigration','MaximumToolProcesses','ReadOnlyQueries','BusinessWriteAttempts','ExpectedTests')
    $names.Count -eq $expected.Count -and @($names|Where-Object {$_ -cnotin $expected}).Count -eq 0 -and
        $plan.Version -eq 1 -and $plan.Stage -ceq 'AccountProfileTargetDiagnostic' -and $plan.RunName -ceq 'run20261008b0' -and -not $plan.Executed -and
        $plan.MaximumToolProcesses -eq 5 -and $plan.ReadOnlyQueries -eq 1 -and $plan.BusinessWriteAttempts -eq 0 -and $plan.ExpectedTests -eq 0 -and
        $plan.Database -ceq 'dango_backend_b0' -and $plan.Role -ceq 'dango_b0_owner' -and
        $plan.ProfileMigration -ceq '20261010055529_AccountProfile' -and (Test-DangoProfilePath $plan.PostgresBinDirectory $taskBin) -and
        (Test-DangoProfilePath $plan.DataDirectory (Join-Path $plan.StateDirectory 'pgdata')) -and
        (Test-DangoProfilePath $plan.EvidenceDirectory (Join-Path (Split-Path -Parent $plan.StateDirectory) 'run20261010profiled1')) -and
        -not (Test-DangoProfilePath $plan.EvidenceDirectory (Get-DangoProfilePlan run20261008b0 $taskBin $taskSandbox).EvidenceDirectory)
}
foreach($case in @('run','bin')) {
    Check ('DiagnosticPlanRejectsOtherTarget_'+$case) {
        [void](Plan)
        if($case -ceq 'run'){Rejected {Get-DangoProfileDiagnosticPlan other $taskBin $taskSandbox} 'profile_run_diagnostic_target_rejected'}
        else{Rejected {Get-DangoProfileDiagnosticPlan run20261008b0 ($taskBin+'other') $taskSandbox} 'profile_run_binary_target_rejected'}
    }
}
Check 'DiagnosticSqlIsOneReadOnlyMetadataSelectWithoutBusinessRows' {
    $sql=Get-DangoProfileDiagnosticSql
    $sql.StartsWith('BEGIN READ ONLY;') -and $sql.TrimEnd().EndsWith('COMMIT;') -and
        @([regex]::Matches($sql,'(?im)^\s*SELECT\b')).Count -eq 1 -and
        $sql -cmatch '"MigrationId"' -and $sql -match 'information_schema\.columns' -and
        $sql -notmatch '(?im)^\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP|TRUNCATE|CALL|DO)\b' -and
        $sql -notmatch '(?i)\bFROM\s+dango\.(users|pets|rooms|auth_sessions|audit_logs|domain_outbox|email_jobs|email_challenges)\b' -and
        $sql -notmatch '(?i)\b(counts|digests|userIds|nicknameIsNull|sha256|pg_read_file|pg_read_binary_file|COPY|lo_import|pg_sleep)\b'
}
Check 'ValidDiagnosticMetadataHasExactlySevenSafeFields' {
    $plan=Plan;$data=Parse (Metadata $plan)
    $expected=@('database','role','version','dataDirectory','listenAddresses','migrations','profileColumnsPresent')
    $names=@($data.PSObject.Properties.Name)
    $names.Count -eq 7 -and @($names|Where-Object {$_ -cnotin $expected}).Count -eq 0 -and
        $data.database -ceq $plan.Database -and $data.version -eq 180001 -and $data.profileColumnsPresent -eq $false
}
Check 'AllSixExpectedTargetFactsProduceTrueFlags' {
    $plan=Plan;$evidence=Parse (Metadata $plan);$flags=Get-DangoProfileDiagnosticMatches $plan $evidence
    $expected=@('DatabaseMatches','RoleMatches','VersionMatches','DataDirectoryMatches','ListenAddressesMatches','MigrationsMatch',
        'AllExpectedTargetFactsMatch','ProfileMigrationPresent')
    @($flags.PSObject.Properties).Count -eq 8 -and @($flags.PSObject.Properties.Name|Where-Object {$_ -cnotin $expected}).Count -eq 0 -and
        $flags.DatabaseMatches -and $flags.RoleMatches -and $flags.VersionMatches -and $flags.DataDirectoryMatches -and
        $flags.ListenAddressesMatches -and $flags.MigrationsMatch -and $flags.AllExpectedTargetFactsMatch -and -not $flags.ProfileMigrationPresent
}
foreach($field in @('database','role','version','dataDirectory','listenAddresses','migrations')) {
    Check ('SingleMismatchRemainsRecordedWithoutThrowOrMutation_'+$field) {
        $plan=Plan;$data=Metadata $plan;$before=$plan|ConvertTo-Json -Depth 6 -Compress
        switch($field){database {$data.database='fictional_other'};role {$data.role='fictional_other_owner'};version {$data.version=170001};
            dataDirectory {$data.dataDirectory=Join-Path $taskSandbox 'other-pgdata'};listenAddresses {$data.listenAddresses='localhost'};
            migrations {$data.migrations+=@('20261011000000_FictionalFuture')}}
        $evidence=Parse $data;$evidenceBefore=$evidence|ConvertTo-Json -Depth 6 -Compress
        $flags=Get-DangoProfileDiagnosticMatches $plan $evidence
        $falseName=@{database='DatabaseMatches';role='RoleMatches';version='VersionMatches';dataDirectory='DataDirectoryMatches';listenAddresses='ListenAddressesMatches';migrations='MigrationsMatch'}[$field]
        $comparisonNames=@('DatabaseMatches','RoleMatches','VersionMatches','DataDirectoryMatches','ListenAddressesMatches','MigrationsMatch')
        -not $flags.$falseName -and @($comparisonNames|Where-Object {-not $flags.$_}).Count -eq 1 -and
            -not $flags.AllExpectedTargetFactsMatch -and -not $flags.ProfileMigrationPresent -and
            ($plan|ConvertTo-Json -Depth 6 -Compress) -ceq $before -and ($evidence|ConvertTo-Json -Depth 6 -Compress) -ceq $evidenceBefore
    }
}
Check 'ProfileMigrationPresenceIsReportedWithoutTreatingItAsExpectedHistory' {
    $plan=Plan;$data=Metadata $plan;$data.migrations+=@($plan.ProfileMigration);$data.profileColumnsPresent=$true
    $flags=Get-DangoProfileDiagnosticMatches $plan (Parse $data)
    $flags.ProfileMigrationPresent -and -not $flags.MigrationsMatch -and -not $flags.AllExpectedTargetFactsMatch
}
foreach($version in @(179999,180000,189999,190000)) {
    Check ('DiagnosticVersionComparisonHonorsBothMajorBoundaries_'+$version) {
        $plan=Plan;$data=Metadata $plan;$data.version=$version
        $flags=Get-DangoProfileDiagnosticMatches $plan (Parse $data)
        $flags.VersionMatches -eq ($version -ge 180000 -and $version -lt 190000)
    }
}
foreach($field in @('database','role','version','dataDirectory','listenAddresses','migrations','profileColumnsPresent')) {
    Check ('MissingMetadataFieldRejects_'+$field) {
        $data=Metadata (Plan);$data.PSObject.Properties.Remove($field)
        Rejected {Parse $data} 'profile_run_diagnostic_evidence_rejected'
    }
}
foreach($case in @('extra','wrong_case','duplicate_key','array_root','null_root','two_objects','number_root','malformed','empty')) {
    Check ('StrictJsonMetadataRejects_'+$case) {
        $data=Metadata (Plan);$json=$data|ConvertTo-Json -Depth 6 -Compress
        $taskInputJson=switch($case){extra {$data|Add-Member Extra 'fictional';$data|ConvertTo-Json -Depth 6 -Compress};
            wrong_case {$json -creplace '"database":','"Database":'};
            duplicate_key {$json -creplace '"database":','"database":"fictional_other","database":'};
            array_root {'['+$json+']'};null_root {'null'};two_objects {$json+$json};number_root {'1'};malformed {'{'};empty {''}}
        Rejected {Assert-DangoProfileDiagnosticEvidence $taskInputJson} 'profile_run_diagnostic_evidence_rejected'
    }
}
foreach($case in @('database_null','database_number','database_empty','database_long','database_control','role_bool',
    'version_string','version_fraction','version_negative','version_int32_over','dataDirectory_empty','dataDirectory_long','listenAddresses_control',
    'migrations_null','migrations_string','migration_item_number','migration_bad_id','migration_unicode_digits','migration_duplicate','migrations_over32','migration_over128','profileColumns_string','profileColumns_null')) {
    Check ('MetadataTypeAndBoundRejects_'+$case) {
        $data=Metadata (Plan)
        switch($case){database_null {$data.database=$null};database_number {$data.database=1};database_empty {$data.database=''};
            database_long {$data.database='x'*257};database_control {$data.database="fictional`nvalue"};role_bool {$data.role=$true};
            version_string {$data.version='180001'};version_fraction {$data.version=180001.5};version_negative {$data.version=-1};version_int32_over {$data.version=2147483648L};
            dataDirectory_empty {$data.dataDirectory=''};dataDirectory_long {$data.dataDirectory='x'*32768};listenAddresses_control {$data.listenAddresses="127.0.0.1`t"};
            migrations_null {$data.migrations=$null};migrations_string {$data.migrations='20261011000000_Fictional'};migration_item_number {$data.migrations=@(1)};
            migration_bad_id {$data.migrations=@('fictional')};migration_unicode_digits {$data.migrations=@(('١'*14)+'_Fictional')};migration_duplicate {$data.migrations=@($data.migrations[0],$data.migrations[0])};
            migrations_over32 {$data.migrations=@(1..33|ForEach-Object {'20261011000000_Fictional'+$_})};
            migration_over128 {$data.migrations=@('20261011000000_'+('F'*115))};profileColumns_string {$data.profileColumnsPresent='false'};profileColumns_null {$data.profileColumnsPresent=$null}}
        Rejected {Parse $data} 'profile_run_diagnostic_evidence_rejected'
    }
}

# No executable or retained process handle is accessed by these injected fakes.
if(-not ('DangoDiagnosticFakeProcess' -as [type])) {
Add-Type -TypeDefinition @'
using System;using System.IO;using System.Threading.Tasks;
public sealed class DangoDiagnosticReader:StringReader {
 public int Reads,Closes; public bool Closed; public DangoDiagnosticReader(string value):base(value){}
 public override Task<int> ReadAsync(char[] b,int i,int n){Reads++;return base.ReadAsync(b,i,n);}
 protected override void Dispose(bool d){Closes++;Closed=true;base.Dispose(d);}
}
public sealed class DangoDiagnosticFakeProcess:IDisposable {
 public string Executable; public int Id=>35000; public DateTime StartTime{get;}=DateTime.UtcNow;
 public DateTime ExitTime=>StartTime.AddMilliseconds(1);public int ExitCode=>0;public bool HasExited=>true;public bool Disposed;
 public TextWriter StandardInput{get;}=new StringWriter();public DangoDiagnosticReader StandardOutput{get;}=new("ignored");public DangoDiagnosticReader StandardError{get;}=new("");
 public bool Start()=>true;public bool WaitForExit(int ms)=>true;public void Kill(bool tree){throw new InvalidOperationException("fake_already_exited");}public void Dispose(){Disposed=true;}
}
'@
}
function StartArguments($Plan,[int]$Port=23456) {
    @('start','-D',$Plan.DataDirectory,'-l',(Join-Path $Plan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $Port",'-w','-t','30')
}
$taskIdentity={param($process,$expected)[pscustomobject]@{Verified=$true;ImagePath=$process.Executable;NativeErrorCode=0;Pid=$process.Id;CreatedAt=$process.StartTime.ToUniversalTime()}}
Check 'ExactDiagnosticStartUsesNoReadTasksAndExplicitResourceClosure' {
    $plan=Plan;$fake=[DangoDiagnosticFakeProcess]::new();$fake.Executable=Join-Path $taskBin 'pg_ctl.exe';$receipts=[Collections.Generic.List[object]]::new()
    $result=Invoke-DangoProfileProcess $fake.Executable (StartArguments $plan) $plan.Repository -Receipts $receipts -IdentityProbe $taskIdentity -ProcessFactory {param($info)$fake} -OutputContract PostgresStartIgnored -PostgresStartPlan $plan -PostgresStartPort 23456
    $r=$receipts[0]
    $result.ExitCode -eq 0 -and $r.ReaderTasksStarted -eq 0 -and $fake.StandardOutput.Reads -eq 0 -and
        $r.CleanupConfirmed -and $r.OutputResourcesCleanupConfirmed -and $fake.StandardOutput.Closed -and $fake.StandardError.Closed -and $fake.Disposed
}
foreach($case in @('tool_count','query_count','business_budget','test_count','extra_plan','wrong_stage','wrong_directory','extra_argument','extra_environment','tree','missing_plan')) {
    Check ('DiagnosticStartMisuseRejectsBeforeFactory_'+$case) {
        $plan=Plan;$calls=[pscustomobject]@{Count=0};$exe=Join-Path $taskBin 'pg_ctl.exe';$args=StartArguments $plan;$env=@{};$tree=$false
        switch($case){tool_count {$plan.MaximumToolProcesses=6};query_count {$plan.ReadOnlyQueries=2};business_budget {$plan.BusinessWriteAttempts=1};test_count {$plan.ExpectedTests=7};
            extra_plan {$plan|Add-Member Extra $true};wrong_stage {$plan.Stage='AccountProfileA'};wrong_directory {$plan.EvidenceDirectory+='other';$args=StartArguments $plan};
            extra_argument {$args+='--extra'};extra_environment {$env=@{FICTIONAL_EXTRA='x'}};tree {$tree=$true};missing_plan {$plan=$null}}
        $rejected=Rejected {Invoke-DangoProfileProcess $exe $args $taskSandbox -EnvironmentValues $env -KillOwnedTree:$tree -ProcessFactory {param($info)$calls.Count++;throw 'fake_factory_must_not_run'} -OutputContract PostgresStartIgnored -PostgresStartPlan $plan -PostgresStartPort 23456} 'profile_run_start_output_contract_rejected'
        $rejected -and $calls.Count -eq 0
    }
}
Check 'DiagnosticPlanCannotGrantFullAccountEvidenceJsonMode' {
    $plan=Plan;$calls=[pscustomobject]@{Count=0}
    $args=@('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p','23456','-U',$plan.Role,'-d',$plan.Database,'-c',(Get-DangoProfileDatabaseSql))
    $env=@{PGPASSWORD=[Convert]::ToBase64String([byte[]](0..31));PGCLIENTENCODING='UTF8'}
    $rejected=Rejected {Invoke-DangoProfileProcess (Join-Path $taskBin 'psql.exe') $args $taskSandbox -EnvironmentValues $env -ProcessFactory {param($info)$calls.Count++;throw 'fake_factory_must_not_run'} -OutputContract PostgresEvidenceJson -PostgresEvidencePlan $plan -PostgresEvidencePort 23456} 'profile_run_evidence_output_contract_rejected'
    $rejected -and $calls.Count -eq 0
}

# The review-input provider is replaced only inside this isolated test host's
# module session, then restored. Every manifest hash below is fictional; no
# installed executable, database state or secret file is read.
function FakeInputs($Plan) {
    [pscustomobject]@{Version=1;Stage=$Plan.Stage;RunName=$Plan.RunName;MaximumToolProcesses=5;ReadOnlyQueries=1;BusinessWriteAttempts=0;ExpectedTests=0;
        Hashes=[ordered]@{'eng/Dango.AccountProfilePostgres.psm1'=('A'*64);'eng/invoke-account-profile-target-diagnostic.ps1'=('B'*64);'fictional-pg/pg_ctl.exe'=('C'*64);'fictional-state/state.json'=('D'*64)};MissingInputs=@()}
}
function WithFakeInputs($Inputs,[scriptblock]$Body) {
    & $taskModule {
        param($fakeInputs,$body)
        $original=(Get-Item Function:Get-DangoProfileDiagnosticReviewInputs).ScriptBlock
        try {
            $script:taskDiagnosticFakeInputs=$fakeInputs
            Set-Item Function:script:Get-DangoProfileDiagnosticReviewInputs -Value {param($Plan)return $script:taskDiagnosticFakeInputs}
            $resolved=Get-DangoProfileDiagnosticReviewInputs $null
            if(-not [object]::ReferenceEquals($resolved,$fakeInputs)){throw 'diagnostic_offline_fake_provider_not_installed'}
            & $body
        } finally {
            Set-Item Function:script:Get-DangoProfileDiagnosticReviewInputs -Value $original
            Remove-Variable taskDiagnosticFakeInputs -Scope Script -ErrorAction SilentlyContinue
        }
    } $Inputs $Body
}
function ManifestPath {Join-Path $taskSandbox ('artifacts/fictional-'+[Guid]::NewGuid().ToString('N')+'.json')}
Check 'FictionalDiagnosticManifestPinsExactNineFieldShapeAndExternalDigest' {
    $plan=Plan;$inputs=FakeInputs $plan;$path=ManifestPath
    WithFakeInputs $inputs {
        $approval=New-DangoProfileDiagnosticApprovalManifest $plan $path
        $accepted=Assert-DangoProfileDiagnosticReviewInputs $plan $path $approval.Sha256
        $accepted.Sha256 -ceq $approval.Sha256 -and $accepted.Inputs.Hashes.Count -eq 4 -and
            (Get-Content -LiteralPath $path -Raw|ConvertFrom-Json).PSObject.Properties.Name.Count -eq 9
    }
}
Check 'DiagnosticManifestCreateNewPreservesExistingReview' {
    $plan=Plan;$inputs=FakeInputs $plan;$path=ManifestPath
    WithFakeInputs $inputs {
        $approval=New-DangoProfileDiagnosticApprovalManifest $plan $path
        $rejected=Rejected {New-DangoProfileDiagnosticApprovalManifest $plan $path} 'already exists'
        $rejected -and (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ceq $approval.Sha256
    }
}
foreach($case in @('digest_missing','digest_lowercase','digest_wrong','manifest_changed','source_changed','source_added','source_missing')) {
    Check ('DiagnosticManifestRejectsChangedOrUnapprovedInputs_'+$case) {
        $plan=Plan;$inputs=FakeInputs $plan;$path=ManifestPath
        WithFakeInputs $inputs {
            $approval=New-DangoProfileDiagnosticApprovalManifest $plan $path;$digest=$approval.Sha256
            switch($case){digest_missing {$digest=''};digest_lowercase {$digest=$digest.ToLowerInvariant()};digest_wrong {$digest='0'*64};
                manifest_changed {[IO.File]::AppendAllText($path,' ')};source_changed {$inputs.Hashes['eng/Dango.AccountProfilePostgres.psm1']='E'*64};
                source_added {$inputs.Hashes['fictional-pg/added.dll']='F'*64};source_missing {$inputs.Hashes.Remove('fictional-pg/pg_ctl.exe')}}
            $expectedCode=switch($case){digest_missing {'profile_run_approval_digest_required'};digest_lowercase {'profile_run_approval_digest_required'};
                digest_wrong {'profile_run_diagnostic_approval_manifest_changed'};manifest_changed {'profile_run_diagnostic_approval_manifest_changed'};
                source_changed {'profile_run_diagnostic_approved_input_changed'};default {'profile_run_diagnostic_approval_manifest_invalid'}}
            Rejected {Assert-DangoProfileDiagnosticReviewInputs $plan $path $digest} $expectedCode
        }
    }
}
foreach($case in @('extra_field','missing_field','wrong_stage','business_budget','tool_limit','query_limit','tests_nonzero','hash_type','hash_lowercase','missing_list_nonempty')) {
    Check ('ExternallyDigestedManifestStillRejectsInvalidShape_'+$case) {
        $plan=Plan;$inputs=FakeInputs $plan;$path=ManifestPath
        WithFakeInputs $inputs {
            [void](New-DangoProfileDiagnosticApprovalManifest $plan $path)
            $content=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
            switch($case){extra_field {$content|Add-Member Extra $true};missing_field {$content.PSObject.Properties.Remove('ExpectedTests')};wrong_stage {$content.Stage='AccountProfileA'};
                business_budget {$content.BusinessWriteAttempts=1};tool_limit {$content.MaximumToolProcesses=6};query_limit {$content.ReadOnlyQueries=2};tests_nonzero {$content.ExpectedTests=7};
                hash_type {$content.Hashes.'eng/Dango.AccountProfilePostgres.psm1'=1};hash_lowercase {$content.Hashes.'eng/Dango.AccountProfilePostgres.psm1'='a'*64};missing_list_nonempty {$content.MissingInputs=@('fictional')}}
            [IO.File]::WriteAllText($path,($content|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
            $digest=(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
            $expectedCode=if($case -ceq 'hash_lowercase'){'profile_run_diagnostic_approved_input_changed'}else{'profile_run_diagnostic_approval_manifest_invalid'}
            Rejected {Assert-DangoProfileDiagnosticReviewInputs $plan $path $digest} $expectedCode
        }
    }
}

Check 'DiagnosticReservationAstCarriesZeroBusinessBudgetAtomically' {
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($ModulePath,[ref]$tokens,[ref]$errors)
    $functions=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'New-DangoProfileDiagnosticReservation'},$true))
    $errors.Count -eq 0 -and $functions.Count -eq 1 -and $functions[0].Extent.Text -match 'BusinessWriteAttempts=0' -and
        $functions[0].Extent.Text -match 'ReadOnlyQueries=1' -and $functions[0].Extent.Text -match 'MaximumToolProcesses=5' -and
        $functions[0].Extent.Text -match '(CreateDirectoryW|New-DangoProfileReservation)' -and $functions[0].Extent.Text -notmatch 'Remove-Item|Move-Item'
}
Check 'RunnerDryReturnAndExternalDigestValidationPrecedeReservation' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $reviews=@($calls|Where-Object {$_.GetCommandName() -ceq 'Assert-DangoProfileDiagnosticReviewInputs'})
    $reserves=@($calls|Where-Object {$_.GetCommandName() -ceq 'New-DangoProfileDiagnosticReservation'})
    $dry=@($ast.EndBlock.Statements|Where-Object {$_ -is [Management.Automation.Language.IfStatementAst] -and $_.Extent.Text -match '^if \(-not \$ExecuteApprovedPlan\)'})
    $returns=@(if($dry.Count -eq 1){$dry[0].FindAll({param($n)$n -is [Management.Automation.Language.ReturnStatementAst]},$true)})
    $reviews.Count -ge 1 -and $reserves.Count -eq 1 -and $returns.Count -eq 1 -and
        $reviews[0].Extent.StartOffset -lt $reserves[0].Extent.StartOffset -and $returns[0].Extent.StartOffset -lt $reserves[0].Extent.StartOffset -and
        @($reviews|Where-Object {$_.Extent.Text -notmatch '\$ApprovalManifestSha256'}).Count -eq 0
}
Check 'RunnerHasNoBusinessWriteOrExternalServiceEntry' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $forbidden=@('Get-DangoProfileDatabaseSql','Get-DangoProfileScopedSql','Assert-DangoProfileTrx','Invoke-Expression','Start-Process','Stop-Process','Remove-Item','Move-Item','Send-MailMessage')
    @($calls|Where-Object {$_.GetCommandName() -cin $forbidden}).Count -eq 0 -and
        $ast.Extent.Text -notmatch '(?i)--formal-migrate|vstest|dotnet\.exe|Dango\.Server\.(dll|Tests)|SmtpClient|CredentialManager|ReadCredential|Cert:\\|Import-Certificate|PostgresEvidenceJson|Get-DangoProfileSnapshot'
}
Check 'RunnerRecordsStrictMetadataCreateNewBeforeEvaluatingMatches' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $parse=@($calls|Where-Object {$_.GetCommandName() -ceq 'Assert-DangoProfileDiagnosticEvidence'})
    $comparisons=@($calls|Where-Object {$_.GetCommandName() -ceq 'Get-DangoProfileDiagnosticMatches'})
    $writes=@($calls|Where-Object {$_.GetCommandName() -ceq 'Write-DangoProfileJson' -and $_.Extent.Text -match 'target-facts\.json' -and $_.Extent.Text -match '-CreateNew'})
    $parse.Count -eq 1 -and $comparisons.Count -eq 1 -and $writes.Count -eq 1 -and
        $parse[0].Extent.StartOffset -lt $writes[0].Extent.StartOffset -and $writes[0].Extent.StartOffset -lt $comparisons[0].Extent.StartOffset
}
Check 'RunnerUsesExactlyOneDiagnosticQueryAndOnlyFiveBoundedTools' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $sql=@($calls|Where-Object {$_.GetCommandName() -ceq 'Get-DangoProfileDiagnosticSql'})
    $tools=@($calls|Where-Object {$_.GetCommandName() -ceq 'Invoke-DangoProfileProcess'})
    $sql.Count -eq 1 -and $tools.Count -eq 5 -and @($tools|Where-Object {$_.Extent.Text -match '-OutputContract PostgresStartIgnored'}).Count -eq 1 -and
        @($tools|Where-Object {$_.Extent.Text -match '-KillOwnedTree'}).Count -eq 0 -and
        @($tools|Where-Object {$_.Extent.Text -match '\$taskDiagPsql\b'}).Count -eq 1
}
Check 'RunnerReportsOutputResourceCleanupSeparatelyAndBlocksRetry' {
    $ast=RunnerAst;$text=$ast.Extent.Text
    $text -match 'PostgreSqlCleanupConfirmed\s*=' -and $text -match 'ToolOutputResourcesCleanupConfirmed\s*=' -and
        $text -match 'OutputResourcesCleanupConfirmed' -and $text -match "'profile_run_output_cleanup_unconfirmed_no_retry'" -and
        $text -match 'RetryAllowed=\$false' -and $text -match 'BusinessWriteAttempts=0'
}
Check 'ReviewInputAstOnlyFreezesNamedBinaryAndSourceInputsPlusStateHash' {
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($ModulePath,[ref]$tokens,[ref]$errors)
    $functions=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Get-DangoProfileDiagnosticReviewInputs'},$true))
    if($errors.Count -ne 0 -or $functions.Count -ne 1){return $false}
    $text=$functions[0].Extent.Text
    $text -match "'eng/Dango.AccountProfilePostgres.psm1'" -and $text -match "'eng/invoke-account-profile-target-diagnostic.ps1'" -and
        $text -match "'state/state.json'" -and $text -match "'postgres-bin/postgres.exe'" -and $text -match "'postgres-bin/pg_ctl.exe'" -and
        $text -match "'postgres-bin/psql.exe'" -and $text -match "'powershell-host/pwsh.exe'" -and
        $text -match "-ceq '.dll'" -and $text -match 'Get-FileHash' -and $text -match '1024' -and
        $text -notmatch '(?i)credential|ReadAllText|ReadAllBytes|Get-Content|b1tests-keys|Dango.Server|DataDirectory'
}
Check 'RunnerDurablyRecordsOneReadOnlyAttemptBeforeSingleQuery' {
    $ast=RunnerAst;$calls=@($ast.FindAll({param($n)$n -is [Management.Automation.Language.CommandAst]},$true))
    $attempt=@($calls|Where-Object {$_.GetCommandName() -ceq 'Write-DangoProfileJson' -and $_.Extent.Text -match 'query-attempt\.json' -and $_.Extent.Text -match '-CreateNew'})
    $query=@($calls|Where-Object {$_.GetCommandName() -ceq 'Invoke-DangoProfileProcess' -and $_.Extent.Text -match '\$taskDiagPsql\b'})
    $attempt.Count -eq 1 -and $query.Count -eq 1 -and $attempt[0].Extent.StartOffset -lt $query[0].Extent.StartOffset -and
        $attempt[0].Extent.Text -match 'ReadOnlyQuery=1' -and $attempt[0].Extent.Text -match 'BusinessWriteAttempts=0'
}

$taskPassed=@($taskChecks|Where-Object Passed).Count
$taskChecks|Select-Object Name,Passed,Failure|Format-Table -AutoSize
Write-DangoProfileJson (Join-Path $taskSandbox 'offline-results.json') ([pscustomobject]@{ExecutedDatabase=$false;RunnerEntryExecuted=$false;Checks=@($taskChecks)}) -CreateNew
if($taskPassed -ne $taskChecks.Count){throw ('profile_run_diagnostic_offline_failed:'+$taskPassed+'/'+$taskChecks.Count)}
Write-Output ('profile_run_diagnostic_offline_passed:'+$taskPassed+'/'+$taskChecks.Count)

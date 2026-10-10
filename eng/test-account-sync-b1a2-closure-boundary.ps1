[CmdletBinding()]
param([string]$ModulePath=(Join-Path $PSScriptRoot 'Dango.AccountSyncClosurePostgres.psm1'),
    [string]$RunnerPath=(Join-Path $PSScriptRoot 'invoke-account-sync-b1a2-closure.ps1'))
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountProfilePostgres.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Dango.AccountSyncPostgres.psm1') -Force -DisableNameChecking
$taskModule=Import-Module $ModulePath -Force -PassThru -DisableNameChecking
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskSandbox=Join-Path $taskRepository ('artifacts/account-sync-b1a2-closure-offline/'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($taskSandbox)
$taskChecks=[Collections.Generic.List[object]]::new()
# Every test file and manifest is fictional and beneath this UUID artifact directory.
# No original state, credential, installed binary or backend reservation is opened; no child process or socket starts.
$taskBinaryOriginal=& $taskModule { (Get-Command Get-DangoAccountSyncClosureBinaryInputs).ScriptBlock }
& $taskModule {
    function script:Get-DangoAccountSyncClosureBinaryInputs {
        param($Plan)
        [pscustomobject]@{Hashes=[ordered]@{'postgres.exe'=('A'*64);'pg_ctl.exe'=('B'*64);'psql.exe'=('C'*64)};MissingInputs=@()}
    }
}
function Check([string]$TestName,[scriptblock]$Body) {
    $passed=$false;$failure=$null
    try{$passed=[bool](& $Body)}catch{$failure=$_.Exception.Message}
    $taskChecks.Add([pscustomobject]@{Name=$TestName;Passed=$passed;Failure=$failure})
}
function Rejected([scriptblock]$Body,[string]$Code) {
    try{[void](& $Body)}catch{
        if($_.Exception.Message.Contains($Code,[StringComparison]::Ordinal)){return $true}
        throw ('closure_offline_wrong_rejection: '+$_.Exception.Message)
    }
    throw 'closure_offline_rejection_missing'
}
function Plan {Get-DangoAccountSyncClosurePlan $taskSandbox}
function Clone($Value) {$Value|ConvertTo-Json -Depth 32 -Compress|ConvertFrom-Json -Depth 32}
function Facts {
    $plan=Plan
    [pscustomobject]@{database=$plan.Database;role=$plan.Role;version=180001;listenAddresses='127.0.0.1';
        dataDirectory=$plan.DataDirectory;migrations=@($plan.PriorMigrations);users=1;validUsers=1;namedUsers=1;
        completeGraphs=1;pendingUnsentEmails=1;sessions=4;validSessions=4;revokedSessions=4}
}
function Contract([string]$Step) {
    $plan=Plan;$exe=Join-Path $plan.PostgresBinDirectory 'pg_ctl.exe';$timeout=30;$output='CompleteEof';$environment=@{}
    $arguments=switch -CaseSensitive ($Step) {
        version {$exe=Join-Path $plan.PostgresBinDirectory 'postgres.exe';@('--version')}
        status {@('status','-D',$plan.DataDirectory)}
        start {$timeout=40;$output='PostgresStartIgnored';@('start','-D',$plan.DataDirectory,'-l',
            (Join-Path $plan.EvidenceDirectory 'postgresql.log'),'-o',"-h 127.0.0.1 -p $($plan.DatabasePort)",'-w','-t','30')}
        scoped {$exe=Join-Path $plan.PostgresBinDirectory 'psql.exe';$environment=@{PGPASSWORD=(('A'*43)+'=');PGCLIENTENCODING='UTF8'};
            @('-X','-w','-q','-A','-t','-v','ON_ERROR_STOP=1','-h','127.0.0.1','-p',[string]$plan.DatabasePort,
                '-U',$plan.Role,'-d',$plan.Database,'-c',(Get-DangoAccountSyncClosureSql $plan))}
        stop {$timeout=40;@('stop','-D',$plan.DataDirectory,'-m','fast','-w','-t','30')}
    }
    @{Plan=$plan;Step=$Step;Executable=$exe;Arguments=[string[]]$arguments;WorkingDirectory=$plan.Repository;
        EnvironmentValues=$environment;TimeoutSeconds=$timeout;KillOwnedTree=$false;OutputContract=$output;PreserveRawOutput=($Step -ceq 'scoped')}
}
function WriteFixture([string]$Relative,[string]$Content='fictional closure source') {
    $path=Join-Path $taskSandbox $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path,$Content,[Text.UTF8Encoding]::new($false));return $path
}
try {
    Check fixed_scope {
        $plan=Plan;[void](Assert-DangoAccountSyncClosureFixedPlan $plan)
        $plan.Stage -ceq 'AccountSyncB1a2Closure' -and $plan.DatabasePort -eq 63237 -and
            [IO.Path]::GetFileName($plan.EvidenceDirectory) -ceq 'run20261010accountsyncb1a2closure' -and
            $plan.MaximumToolProcesses -eq 5 -and $plan.ReadOnlyQueries -eq 1 -and $plan.BusinessWriteAttempts -eq 0 -and
            $plan.MigrationOperations -eq 0 -and $plan.Certificates -eq 0 -and $plan.HttpsHosts -eq 0 -and $plan.DesktopHosts -eq 0 -and
            $plan.UserId -ceq 'c0179320-d30f-421f-a43a-7fd83f72999b' -and
            $plan.SourceOwnerId -ceq '4fd0debb-576b-4410-a8da-f8f434a2e390' -and -not $plan.RetryAllowed
    }
    Check shared_fixed_dispatch {(Get-DangoProfileFixedPlan (Plan)).Stage -ceq 'AccountSyncB1a2Closure'}
    foreach($property in (Plan).PSObject.Properties) {
        if($property.Name -ceq 'Repository'){continue}
        $name=$property.Name
        Check ('fixed_change_'+$name) {
            $plan=Plan
            if($plan.$name -is [bool]){$plan.$name=-not $plan.$name}
            elseif($plan.$name -is [int]){$plan.$name++}
            elseif($plan.$name -is [Array]){$plan.$name[0]+='_other'}
            else{$plan.$name+='_other'}
            Rejected {Assert-DangoAccountSyncClosureFixedPlan $plan} 'accountsync_closure_fixed_plan_rejected'
        }
    }
    Check fixed_extra {$plan=Plan;$plan|Add-Member Extra 'unexpected';Rejected {Assert-DangoAccountSyncClosureFixedPlan $plan} 'accountsync_closure_fixed_plan_rejected'}
    Check fixed_missing {$plan=Plan;$plan.PSObject.Properties.Remove('PetId');Rejected {Assert-DangoAccountSyncClosureFixedPlan $plan} 'accountsync_closure_fixed_plan_rejected'}
    Check sql_ascii_and_exact_ids {
        $plan=Plan;$sql=Get-DangoAccountSyncClosureSql $plan
        $sql -cnotmatch '[^\x00-\x7F]' -and $sql.Contains("p.id='$($plan.PetId)'::uuid",[StringComparison]::Ordinal) -and
            $sql.Contains("r.id='$($plan.DefaultRoomId)'::uuid",[StringComparison]::Ordinal) -and
            $sql.Contains("id='$($plan.UserId)'::uuid",[StringComparison]::Ordinal) -and
            $sql.Contains($plan.RecoveryEpoch,[StringComparison]::Ordinal) -and
            $sql.Contains(([Guid]$plan.SourceOwnerId).ToString('N'),[StringComparison]::Ordinal) -and
            $sql -cmatch "nickname=U&'\\65B0\\540C\\6B65\\56E2\\5B50'" -and
            @([regex]::Matches($sql,'(?im)^\s*SELECT\b')).Count -eq 1 -and
            $sql.StartsWith('BEGIN READ ONLY;') -and $sql.TrimEnd().EndsWith('COMMIT;') -and
            $sql.IndexOf("'migrations'",[StringComparison]::Ordinal) -lt $sql.IndexOf("'users'",[StringComparison]::Ordinal)
    }
    Check valid_facts { [void](Assert-DangoAccountSyncClosureEvidence (Plan) ((Facts)|ConvertTo-Json -Depth 8));$true }
    foreach($field in (Facts).PSObject.Properties.Name) {
        Check ('facts_wrong_'+$field) {
            $facts=Facts
            if($field -ceq 'version'){$facts.$field=190000}
            elseif($facts.$field -is [int]){$facts.$field++}
            elseif($facts.$field -is [Array]){$facts.$field[0]+='_wrong'}
            else{$facts.$field+='_wrong'}
            Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) ($facts|ConvertTo-Json -Depth 8)} 'accountsync_closure_evidence_rejected'
        }
        Check ('facts_missing_'+$field) {
            $facts=Facts;$facts.PSObject.Properties.Remove($field)
            Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) ($facts|ConvertTo-Json -Depth 8)} 'accountsync_closure_evidence_rejected'
        }
    }
    foreach($field in @('users','validUsers','namedUsers','completeGraphs','pendingUnsentEmails','sessions','validSessions','revokedSessions','version')) {
        foreach($type in @('string','boolean','decimal','null')) {
            Check ('facts_type_'+$field+'_'+$type) {
                $json=(Facts)|ConvertTo-Json -Depth 8 -Compress
                $value=switch($type){string{'"1"'};boolean{'true'};decimal{'1.0'};null{'null'}}
                $json=[regex]::Replace($json,('"'+$field+'":\d+'),('"'+$field+'":'+$value))
                Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) $json} 'accountsync_closure_evidence_rejected'
            }
        }
    }
    Check facts_extra {$facts=Facts;$facts|Add-Member Extra 1;Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) ($facts|ConvertTo-Json -Depth 8)} 'accountsync_closure_evidence_rejected'}
    foreach($version in @(180000,189999)) {
        $boundaryVersion=$version
        Check ('facts_valid_pg18_'+$version) {$facts=Facts;$facts.version=$boundaryVersion;[void](Assert-DangoAccountSyncClosureEvidence (Plan) ($facts|ConvertTo-Json -Depth 8));$true}
    }
    Check facts_invalid_pg17 {$facts=Facts;$facts.version=179999;Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) ($facts|ConvertTo-Json -Depth 8)} 'accountsync_closure_evidence_rejected'}
    Check facts_duplicate {$json=(Facts)|ConvertTo-Json -Depth 8 -Compress;$json=$json.Replace('"users":1','"users":1,"users":1');Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) $json} 'accountsync_closure_evidence_rejected'}
    foreach($json in @('[]','null','1','{','{"database":"x"}',('{"x":"'+('界'*44000)+'"}'))) {
        $value=$json
        Check ('facts_malformed_or_bound_'+$taskChecks.Count) {Rejected {Assert-DangoAccountSyncClosureEvidence (Plan) $value} 'accountsync_closure_evidence_rejected'}
    }
    $state=[pscustomobject]@{RunName=(Plan).RunName;Database=(Plan).Database;Owner=(Plan).Role;DataDirectory=(Plan).DataDirectory;Port=63237;Version=1}
    Check valid_state {[void](Assert-DangoAccountSyncClosureState (Plan) ($state|ConvertTo-Json));$true}
    foreach($field in $state.PSObject.Properties.Name) {
        Check ('state_wrong_'+$field) {$copy=Clone $state;$copy.$field=[string]$copy.$field+'_wrong';Rejected {Assert-DangoAccountSyncClosureState (Plan) ($copy|ConvertTo-Json)} 'accountsync_closure_state_rejected'}
    }
    Check state_duplicate {$json=$state|ConvertTo-Json -Compress;$json=$json.Replace('"Port":63237','"Port":63237,"Port":63237');Rejected {Assert-DangoAccountSyncClosureState (Plan) $json} 'accountsync_closure_state_rejected'}
    foreach($step in @('version','status','start','scoped','stop')) {
        Check ('contract_valid_'+$step) {$contract=Contract $step;[void](Assert-DangoAccountSyncClosureToolContract @contract);$true}
        # Splat the exact contract dictionary, then mutate every argv position independently.
        for($index=0;$index -lt (Contract $step).Arguments.Count;$index++) {
            $at=$index
            Check ('contract_argv_'+$step+'_'+$at) {
                $contract=Contract $step;$contract.Arguments[$at]+='_wrong'
                Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'
            }
        }
        foreach($field in @('Executable','WorkingDirectory','TimeoutSeconds','KillOwnedTree','OutputContract','PreserveRawOutput')) {
            Check ('contract_option_'+$step+'_'+$field) {
                $contract=Contract $step
                switch($field){TimeoutSeconds{$contract.TimeoutSeconds++};KillOwnedTree{$contract.KillOwnedTree=$true};PreserveRawOutput{$contract.PreserveRawOutput=-not $contract.PreserveRawOutput};default{$contract[$field]+='_wrong'}}
                Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'
            }
        }
    }
    foreach($value in @('LATIN1','CP936','utf8',$null)) {
        $encoding=$value
        Check ('scoped_encoding_'+$taskChecks.Count) {$contract=Contract scoped;$contract.EnvironmentValues.PGCLIENTENCODING=$encoding;Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'}
    }
    Check scoped_extra_environment {$contract=Contract scoped;$contract.EnvironmentValues.EXTRA='unapproved';Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'}
    Check scoped_noncanonical_password {$contract=Contract scoped;$contract.EnvironmentValues.PGPASSWORD=('A'*42)+'B=';Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'}
    Check start_extra_environment {$contract=Contract start;$contract.EnvironmentValues.PGPASSWORD=('A'*43)+'=';Rejected {Assert-DangoAccountSyncClosureToolContract @contract} 'accountsync_closure_tool_contract_rejected'}

    $sourceInputs=Get-DangoAccountSyncClosureInputs (Get-DangoAccountSyncClosurePlan $taskRepository)
    foreach($relative in @($sourceInputs.Hashes.Keys)+@($sourceInputs.MissingInputs)) {
        if($relative.StartsWith('postgres-bin/')){continue}
        [void](WriteFixture $relative)
    }
    $taskManifest=Join-Path $taskSandbox 'artifacts/fictional-approved.json'
    $taskApproval=New-DangoAccountSyncClosureApproval (Plan) $taskManifest
    Check approval_valid {[void](Assert-DangoAccountSyncClosureApproval (Plan) $taskManifest $taskApproval.Sha256);$true}
    Check approval_contains_binary_and_metadata_freeze {
        $inputs=Get-DangoAccountSyncClosureInputs (Plan)
        $inputs.PostgresTools.Count -eq 3 -and $inputs.Hashes.Contains('tests/Dango.Server.Tests/Configuration/AccountSyncScopedSqlMetadataTests.cs') -and
            $inputs.SqlSha256 -cmatch '^[A-F0-9]{64}$' -and $inputs.MissingInputs.Count -eq 0
    }
    foreach($field in @('Stage','RunName','SqlSha256','PostgresTools','Plan','Hashes','MissingInputs','Version')) {
        Check ('approval_changed_'+$field) {
            $inputs=Clone $taskApproval.Inputs
            switch($field){Stage{$inputs.Stage='AccountSyncB1a2'};RunName{$inputs.RunName='old'};SqlSha256{$inputs.SqlSha256='D'*64};
                PostgresTools{$inputs.PostgresTools.'psql.exe'='D'*64};Plan{$inputs.Plan.ReadOnlyQueries=2};
                Hashes{$inputs.Hashes.'eng/Dango.AccountSyncPostgres.psm1'='D'*64};MissingInputs{$inputs.MissingInputs=@('missing')};Version{$inputs.Version='1'}}
            $path=WriteFixture ('artifacts/changed-'+$field+'.json') ($inputs|ConvertTo-Json -Depth 32)
            $digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Rejected {Assert-DangoAccountSyncClosureApproval (Plan) $path $digest} 'accountsync_closure_json_value_rejected'
        }
    }
    Check approval_duplicate_nested {
        $json=$taskApproval.Inputs|ConvertTo-Json -Depth 32 -Compress
        $json=$json.Replace('"ReadOnlyQueries":1','"ReadOnlyQueries":1,"ReadOnlyQueries":1')
        $path=WriteFixture 'artifacts/duplicate.json' $json;$digest=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Rejected {Assert-DangoAccountSyncClosureApproval (Plan) $path $digest} 'accountsync_closure_json_value_rejected'
    }
    Check approval_wrong_digest {Rejected {Assert-DangoAccountSyncClosureApproval (Plan) $taskManifest ('D'*64)} 'accountsync_closure_approval_changed'}
    Check approval_lowercase_digest {Rejected {Assert-DangoAccountSyncClosureApproval (Plan) $taskManifest $taskApproval.Sha256.ToLowerInvariant()} 'accountsync_closure_approval_digest_required'}
    Check approval_file_changed {
        [void](WriteFixture 'eng/Dango.AccountSyncPostgres.psm1' 'changed SQL source')
        Rejected {Assert-DangoAccountSyncClosureApproval (Plan) $taskManifest $taskApproval.Sha256} 'accountsync_closure_json_value_rejected'
    }
    if(-not ('DangoClosureFakeProcessV1' -as [type])) {
        Add-Type -TypeDefinition @'
using System; using System.IO; using System.Threading.Tasks;
public sealed class DangoClosureFakeProcessV1 : IDisposable {
 public int Id=>40000; public DateTime StartTime {get;}=DateTime.UtcNow;
 public DateTime ExitTime=>StartTime.AddMilliseconds(1); public bool HasExited=>true; public int ExitCode=>1;
 public TextWriter StandardInput {get;set;}=new StringWriter();
 public TextReader StandardOutput {get;set;}=new StringReader("  {}\r\n ");
 public TextReader StandardError {get;set;}=new StringReader("ERROR: fictional_failure\r\n");
 public bool Disposed; public bool Start()=>true; public bool WaitForExit(int ms)=>true;
 public void Kill(bool tree){} public void Dispose(){Disposed=true;}
}
public sealed class DangoClosurePendingReaderV1 : TextReader {
 private readonly TaskCompletionSource<int> end=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override Task<int> ReadAsync(char[] buffer,int index,int count)=>end.Task;
 protected override void Dispose(bool disposing){end.TrySetResult(0);base.Dispose(disposing);}
}
'@
    }
    $taskFakeIdentity={param($process,$expected)[pscustomobject]@{Verified=$true;ImagePath=$expected;NativeErrorCode=0;Pid=$process.Id;CreatedAt=$process.StartTime.ToUniversalTime()}}
    Check shared_default_trim_unchanged {
        $fake=[DangoClosureFakeProcessV1]::new();$receipts=[Collections.Generic.List[object]]::new()
        $result=Invoke-DangoProfileProcess 'fictional.exe' @() $taskSandbox -Receipts $receipts `
            -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity
        $result.Output -ceq '{}' -and @($result.PSObject.Properties.Name) -cnotcontains 'RawOutput' -and
            $result.Receipt.OutputComplete -and $fake.Disposed
    }
    function RawHelperOptions {
        $contract=Contract scoped
        @{Executable=$contract.Executable;Arguments=$contract.Arguments;WorkingDirectory=$contract.WorkingDirectory;
            EnvironmentValues=$contract.EnvironmentValues;TimeoutSeconds=30;OutputContract='CompleteEof';PreserveRawOutput=$true;
            PostgresEvidencePlan=$contract.Plan;PostgresEvidencePort=63237}
    }
    Check shared_raw_stdout_stderr_exact_crlf {
        $fake=[DangoClosureFakeProcessV1]::new();$receipts=[Collections.Generic.List[object]]::new();$options=RawHelperOptions
        $result=Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity
        $result.RawOutput -ceq "  {}`r`n " -and $result.RawError -ceq "ERROR: fictional_failure`r`n" -and
            $result.ExitCode -eq 1 -and $result.Receipt.OutputComplete -and $result.Receipt.OutputResourcesCleanupConfirmed -and $fake.Disposed
    }
    foreach($field in @('OutputContract','PostgresEvidencePort','PostgresEvidencePlan','Arguments','PreserveRawOutput')) {
        Check ('shared_raw_contract_'+$field) {
            $options=RawHelperOptions
            switch($field){OutputContract{$options.OutputContract='PostgresStartIgnored'};PostgresEvidencePort{$options.PostgresEvidencePort=5432};
                PostgresEvidencePlan{$options.PostgresEvidencePlan=Get-DangoAccountSyncPlan $taskSandbox};Arguments{$options.Arguments=@('--version')};
                PreserveRawOutput{$options.PreserveRawOutput=$false}}
            $called=[pscustomobject]@{Factories=0}
            $rejected=Rejected {Invoke-DangoProfileProcess @options -ProcessFactory {param($info)$called.Factories++;throw 'must_not_launch'}} 'contract_rejected'
            $rejected -and $called.Factories -eq 0
        }
    }
    foreach($stream in @('stdout','stderr')) {
        Check ('shared_raw_utf8_byte_bound_'+$stream) {
            $fake=[DangoClosureFakeProcessV1]::new();$receipts=[Collections.Generic.List[object]]::new();$options=RawHelperOptions
            if($stream -ceq 'stdout'){$fake.StandardOutput=[IO.StringReader]::new(('界'*43691))}
            else{$fake.StandardError=[IO.StringReader]::new(('界'*43691))}
            $rejected=Rejected {Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity} 'profile_run_tool_output_bound_exceeded'
            $rejected -and -not $receipts[0].OutputComplete
        }
    }
    Check shared_raw_requires_full_eof {
        $fake=[DangoClosureFakeProcessV1]::new();$fake.StandardOutput=[DangoClosurePendingReaderV1]::new()
        $receipts=[Collections.Generic.List[object]]::new();$options=RawHelperOptions
        $rejected=Rejected {Invoke-DangoProfileProcess @options -Receipts $receipts -ProcessFactory {param($info)$fake} -IdentityProbe $taskFakeIdentity} 'profile_run_tool_output_incomplete'
        $rejected -and -not $receipts[0].OutputComplete
    }
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($RunnerPath,[ref]$tokens,[ref]$errors)
    if($errors.Count -ne 0){throw 'closure_offline_runner_parse_failed'}
    foreach($definition in $ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)){
        Invoke-Expression $definition.Extent.Text
    }
    function Assert-DangoAccountSyncClosureFrozenInputs {}
    function Write-DangoProfileJson {param($Path,$Value,[switch]$CreateNew)$script:taskMockQueryReservations++}
    function Invoke-DangoProfileProcess {
        param($Executable,$Arguments,$WorkingDirectory,$EnvironmentValues,$TimeoutSeconds,$KillOwnedTree,$Receipts,$OutputContract,$PostgresStartPlan,$PostgresStartPort,$PreserveRawOutput,$PostgresEvidencePlan,$PostgresEvidencePort)
        $record=[pscustomobject]@{Executable=$Executable;Arguments=$Arguments;OutputContract=$OutputContract}
        if(-not $taskMockOmitReceipt){$Receipts.Add($record)}
        return [pscustomobject]@{ExitCode=0;Receipt=$record;Output='fictional'}
    }
    function ResetRunner {
        $script:taskClosurePlan=Plan;$script:taskClosureTools=[Collections.Generic.List[object]]::new()
        $script:taskClosureToolAttempts=0;$script:taskClosureToolIndex=0;$script:taskClosureStopAttempted=$false
        $script:taskClosureStopOwnershipVerified=$false;$script:taskClosureQueryAttempted=$false
        $script:taskClosureReservation=[pscustomobject]@{OwnerId=[Guid]::NewGuid()}
        $script:taskMockQueryReservations=0;$script:taskMockOmitReceipt=$false
    }
    function RunStep([string]$Step) {
        $contract=Contract $Step
        Invoke-DangoAccountSyncClosureTool $Step $contract.Executable $contract.Arguments $contract.EnvironmentValues `
            -TimeoutSeconds $contract.TimeoutSeconds -OutputContract $contract.OutputContract -PreserveRawOutput:$contract.PreserveRawOutput|Out-Null
    }
    Check runner_wrong_first {ResetRunner;Rejected {RunStep scoped} 'accountsync_closure_tool_order_rejected'}
    Check runner_exact_five {
        ResetRunner;foreach($step in @('version','status','start','scoped')){RunStep $step}
        $script:taskClosureStopOwnershipVerified=$true;RunStep stop
        $taskClosureToolAttempts -eq 5 -and $taskClosureTools.Count -eq 5 -and $taskMockQueryReservations -eq 1
    }
    Check runner_sixth {Rejected {RunStep stop} 'accountsync_closure_tool_order_rejected'}
    Check runner_stop_needs_ownership {ResetRunner;foreach($step in @('version','status','start')){RunStep $step};Rejected {RunStep stop} 'accountsync_closure_tool_order_rejected'}
    Check runner_failure_can_only_spend_stop {
        $script:taskClosureStopOwnershipVerified=$true;RunStep stop
        $taskClosureToolAttempts -eq 4 -and $taskMockQueryReservations -eq 0 -and
            (Rejected {RunStep scoped} 'accountsync_closure_tool_order_rejected')
    }
    Check runner_receipt_omission_cannot_bypass_budget {
        ResetRunner;$script:taskMockOmitReceipt=$true
        foreach($step in @('version','status','start','scoped')){RunStep $step}
        $script:taskClosureStopOwnershipVerified=$true;RunStep stop
        $taskClosureToolAttempts -eq 5 -and $taskClosureTools.Count -eq 0 -and
            (Rejected {RunStep version} 'accountsync_closure_tool_order_rejected')
    }
    Check runner_contract_blocks_command_injection {
        ResetRunner;$contract=Contract version;$contract.Arguments=@('--version','-D','other')
        Rejected {Invoke-DangoAccountSyncClosureTool version $contract.Executable $contract.Arguments} 'accountsync_closure_tool_contract_rejected'
    }
    Check runner_query_cannot_repeat {
        ResetRunner;foreach($step in @('version','status','start')){RunStep $step};$script:taskClosureQueryAttempted=$true
        Rejected {RunStep scoped} 'accountsync_closure_query_already_consumed'
    }
    $text=[IO.File]::ReadAllText($RunnerPath)
    Check runner_reservation_before_state {
        $text.IndexOf('$taskClosureReservation=New-DangoAccountSyncClosureReservation',[StringComparison]::Ordinal) -lt
            $text.IndexOf('$stateBytes=Read-DangoAccountSyncClosureBytes',[StringComparison]::Ordinal) -and
            $text -cnotmatch "b1tests-keys|b1test-state.json|vstest|dotnet|Set-Acl"
    }
    Check runner_facts_saved_before_validation {
        $text.IndexOf('Save-DangoAccountSyncClosureFacts $taskClosurePlan $query.RawOutput',[StringComparison]::Ordinal) -lt
            $text.IndexOf('Assert-DangoAccountSyncClosureEvidence $taskClosurePlan $query.RawOutput',[StringComparison]::Ordinal) -and
            $text -cmatch 'Receipt.OutputComplete' -and $text -cmatch 'Receipt.OutputResourcesCleanupConfirmed'
    }
    Check runner_receipt_success_requires_cleanup {
        $text -cmatch 'ClosurePassed=\([\s\S]*?\$taskClosurePostgresCleanup -and \$taskClosureCmdCleanup -and \$taskClosureToolCleanup -and \$taskClosureOutputCleanup -and[\s\S]*?\$taskClosurePidFileAbsent -and \$taskClosureListenerAbsent\)'
    }
    # Use a module-local fake plan solely to exercise the durable facts writer under artifacts.
    $factsDirectory=Join-Path $taskSandbox 'saved-facts';[void][IO.Directory]::CreateDirectory($factsDirectory)
    Check facts_raw_diagnostic_saved_create_new {
        & $taskModule {
            param($directory)
            function Assert-DangoAccountSyncClosureFixedPlan {param($Plan)return $Plan}
            $plan=[pscustomobject]@{EvidenceDirectory=$directory}
            Save-DangoAccountSyncClosureFacts $plan '{"unexpected":1}'
            if([IO.File]::ReadAllText((Join-Path $directory 'target-facts.json')) -cne '{"unexpected":1}'){return $false}
            try{Save-DangoAccountSyncClosureFacts $plan '{}';return $false}catch{return $_.Exception -is [IO.IOException] -or $_.Exception.InnerException -is [IO.IOException]}
        } $factsDirectory
    }
    Check stderr_exact_raw_create_new_and_utf8_bound {
        & $taskModule {
            param($directory)
            function Assert-DangoAccountSyncClosureFixedPlan {param($Plan)return $Plan}
            $plan=[pscustomobject]@{EvidenceDirectory=$directory}
            Save-DangoAccountSyncClosureFacts $plan "ERROR: fictional`r`n" -FileName 'query-stderr.txt'
            if([IO.File]::ReadAllText((Join-Path $directory 'query-stderr.txt')) -cne "ERROR: fictional`r`n"){return $false}
            $duplicate=$false
            try{Save-DangoAccountSyncClosureFacts $plan '' -FileName 'query-stderr.txt'}catch{$duplicate=$true}
            try{Save-DangoAccountSyncClosureFacts $plan ('界'*43691);return $false}catch{
                return $duplicate -and $_.Exception.Message -ceq 'accountsync_closure_facts_bound_rejected'
            }
        } $factsDirectory
    }
    Check private_acl_requires_owner_only_and_protection {
        $sid=[Security.Principal.SecurityIdentifier]::new('S-1-5-21-111-222-333-1001')
        $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetOwner($sid);$acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','Allow'))
        if(-not(Test-DangoAccountSyncClosurePrivateAcl $acl $sid $true)){return $false}
        $other=[Security.Principal.SecurityIdentifier]::new('S-1-5-21-111-222-333-1002')
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($other,'Read','Allow'))
        -not(Test-DangoAccountSyncClosurePrivateAcl $acl $sid $true)
    }
    Check artifact_reservation_create_new_consumes_once {
        & $taskModule {
            param($plan,$directory)
            function Assert-DangoAccountSyncClosureFixedPlan {param($Plan)return $Plan}
            $plan.EvidenceDirectory=$directory
            $reservation=New-DangoAccountSyncClosureReservation $plan
            if(-not [IO.File]::Exists((Join-Path $directory 'reservation.json')) -or $reservation.RetryAllowed){return $false}
            try{New-DangoAccountSyncClosureReservation $plan;return $false}catch{
                return $_.Exception.Message -ceq 'accountsync_closure_already_reserved'
            }
        } (Plan) (Join-Path $taskSandbox 'fictional-reservation')
    }
}finally{
    & $taskModule {param($original)Set-Item -LiteralPath Function:script:Get-DangoAccountSyncClosureBinaryInputs -Value $original} $taskBinaryOriginal
}
$taskResult=Join-Path $taskSandbox 'offline-results.json'
[IO.File]::WriteAllText($taskResult,([pscustomobject]@{Version=1;Checks=@($taskChecks);Passed=@($taskChecks|Where-Object Passed).Count;
    Failed=@($taskChecks|Where-Object{-not $_.Passed}).Count;UsesRealResources=$false}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
$failed=@($taskChecks|Where-Object{-not $_.Passed})
if($failed.Count -ne 0){$failed|Format-Table -AutoSize;throw ('closure_offline_failed:'+ $failed.Count)}
Write-Output ('closure_offline_passed:'+ $taskChecks.Count+';evidence='+$taskResult)

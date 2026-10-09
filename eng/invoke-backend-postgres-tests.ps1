[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunName,
    [Parameter(Mandatory)][string]$PostgresBinDirectory,
    [switch]$Initialize,
    [switch]$CreateDatabase,
    [switch]$B1,
    [switch]$B2,
    [switch]$B3,
    [switch]$B4,
    [switch]$B5A,
    [switch]$B5B,
    [string]$TestFilter,
    [switch]$ExecuteApprovedPlan
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This tool is limited to a fictional, explicitly approved cluster. Each new stage requires precise authorization.
if (-not $IsWindows) { throw 'backend_test_windows_required' }
if ($RunName -notmatch '^run[a-z0-9]{1,40}$') { throw 'backend_test_run_name_invalid' }
if ($B2 -and ($B1 -or $Initialize -or $CreateDatabase)) { throw 'backend_b2_existing_target_only' }
if ($B3 -and ($B1 -or $B2 -or $Initialize -or $CreateDatabase)) { throw 'backend_b3_existing_target_only' }
if ($B3 -and $RunName -ne 'run20261008b0') { throw 'backend_b3_target_not_approved' }
if ($B4 -and ($B1 -or $B2 -or $B3 -or $Initialize -or $CreateDatabase)) { throw 'backend_b4_existing_target_only' }
if ($B4 -and $RunName -ne 'run20261008b0') { throw 'backend_b4_target_not_approved' }
if ($B5A -and ($B1 -or $B2 -or $B3 -or $B4 -or $B5B -or $Initialize -or $CreateDatabase)) { throw 'backend_b5a_existing_target_only' }
if ($B5A -and $RunName -ne 'run20261008b0') { throw 'backend_b5a_target_not_approved' }
if ($B5A -and $TestFilter -and ($TestFilter.Length -gt 512 -or $TestFilter -notmatch '^FullyQualifiedName(?:~|=)[A-Za-z0-9_.]+$')) { throw 'backend_b5a_filter_invalid' }
if ($B5B -and ($B1 -or $B2 -or $B3 -or $B4 -or $B5A -or $Initialize -or $CreateDatabase)) { throw 'backend_b5b_existing_target_only' }
if ($B5B -and $RunName -ne 'run20261008b0') { throw 'backend_b5b_target_not_approved' }
if ($B5B -and $TestFilter -and ($TestFilter.Length -gt 512 -or $TestFilter -notmatch '^FullyQualifiedName(?:~|=)[A-Za-z0-9_.]+$')) { throw 'backend_b5b_filter_invalid' }
$taskRepoDirectory = Split-Path -Parent $PSScriptRoot
$taskStateBase = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango\BackendDevelopment'
$taskStateDirectory = [IO.Path]::GetFullPath((Join-Path $taskStateBase $RunName))
$taskDataDirectory = Join-Path $taskStateDirectory 'pgdata'
$taskDatabaseName = 'dango_backend_b0'
$taskOwnerName = 'dango_b0_owner'
$taskBinaryDirectory = [IO.Path]::GetFullPath($PostgresBinDirectory)
$taskStateFile = Join-Path $taskStateDirectory 'state.json'
$taskCredentialFile = Join-Path $taskStateDirectory 'credential.dpapi'

foreach ($taskBinary in @('postgres.exe', 'initdb.exe', 'pg_ctl.exe', 'createdb.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskBinaryDirectory $taskBinary) -PathType Leaf)) { throw 'backend_test_binary_missing' }
}
for ($taskAncestor = [IO.DirectoryInfo]::new($taskDataDirectory); $null -ne $taskAncestor; $taskAncestor = $taskAncestor.Parent) {
    if ($taskAncestor.Exists -and ($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'backend_test_reparse_target_rejected' }
}
if (-not $ExecuteApprovedPlan) {
    [pscustomobject]@{ RunName=$RunName; DataDirectory=$taskDataDirectory; Database=$taskDatabaseName; Initialize=[bool]$Initialize; CreateDatabase=[bool]$CreateDatabase; B1=[bool]$B1; B2=[bool]$B2; B3=[bool]$B3; B4=[bool]$B4; B5A=[bool]$B5A; B5B=[bool]$B5B; Writes=$(if ($B5B) { 'only B5B fictional account/session/room operations and scoped own pet/code/lease fixtures; existing schema and keys; no migration, SMTP, WinCred, data deletion, restore or expiry' } elseif ($B5A) { 'only B5A fictional account, registration, session and own pet configuration tests; existing schema and keys; no migration, SMTP, WinCred, deletion or restore' } elseif ($B4) { 'only RoomAccess and RoomAccessIntegrity forward migrations; fictional B1-B4 regressions; scoped lease/outbox work and permanent expiry only for generated targets; no reset or restore' } elseif ($B3) { 'only RoomManagement forward migration and fictional tests; permanent expiry only for exact B3-generated room IDs; no reset or restore' } elseif ($B2) { 'fictional tests only; existing schema; no migration, reset, deletion or restore' } else { 'explicit forward migration and fictional tests; no reset, deletion or restore' }) }
    return
}

function Invoke-TaskProcess {
    param([string]$Executable, [string[]]$Arguments, [hashtable]$EnvironmentValues = @{}, [switch]$PermitFailure)
    $taskProcessInfo = [Diagnostics.ProcessStartInfo]::new()
    $taskProcessInfo.FileName = $Executable
    $taskProcessInfo.WorkingDirectory = $taskRepoDirectory
    $taskProcessInfo.UseShellExecute = $false
    $taskProcessInfo.CreateNoWindow = $true
    $taskProcessInfo.RedirectStandardOutput = $true
    $taskProcessInfo.RedirectStandardError = $true
    foreach ($taskArgument in $Arguments) { $taskProcessInfo.ArgumentList.Add($taskArgument) }
    foreach ($taskEnvironmentName in $EnvironmentValues.Keys) { $taskProcessInfo.Environment[$taskEnvironmentName] = $EnvironmentValues[$taskEnvironmentName] }
    $taskProcess = [Diagnostics.Process]::new()
    $taskProcess.StartInfo = $taskProcessInfo
    try {
        [void]$taskProcess.Start()
        $taskOutputRead = $taskProcess.StandardOutput.ReadToEndAsync()
        $taskErrorRead = $taskProcess.StandardError.ReadToEndAsync()
        $taskProcess.WaitForExit()
        # On Windows a daemon may inherit the pipe after pg_ctl exits. Do not wait for daemon EOF.
        $taskOutput = if ($taskOutputRead.Wait(1000)) { $taskOutputRead.GetAwaiter().GetResult() } else { 'backend_test_background_output_detached' }
        $taskError = if ($taskErrorRead.Wait(1000)) { $taskErrorRead.GetAwaiter().GetResult() } else { '' }
        # PostgreSQL commands receive credentials only through inherited environment or protected file.
        if ($taskOutput) { Write-Host $taskOutput.TrimEnd() }
        if ($taskError) { Write-Host $taskError.TrimEnd() }
        if ($taskProcess.ExitCode -ne 0 -and -not $PermitFailure) { throw 'backend_test_child_failed' }
        return $taskProcess.ExitCode
    } finally { $taskProcess.Dispose() }
}

$taskVersion = & (Join-Path $taskBinaryDirectory 'postgres.exe') --version
if ($LASTEXITCODE -ne 0 -or $taskVersion -notmatch 'PostgreSQL\) 18\.') { throw 'backend_test_postgres18_required' }

if ($B5A -or $B5B) {
    # Client integration stages cannot create cluster credentials, keys or a recovery epoch.
    foreach ($taskRequiredFile in @($taskStateFile, $taskCredentialFile, (Join-Path $taskStateDirectory 'b1test-state.json'))) {
        if (-not (Test-Path -LiteralPath $taskRequiredFile -PathType Leaf)) { throw 'backend_client_existing_state_required' }
        if ((Get-Item -LiteralPath $taskRequiredFile).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'backend_test_reparse_target_rejected' }
    }
    $taskExistingKeyDirectory = Join-Path $taskStateDirectory 'b1tests-keys'
    if (-not (Test-Path -LiteralPath $taskDataDirectory -PathType Container) -or
        -not (Test-Path -LiteralPath $taskExistingKeyDirectory -PathType Container)) { throw 'backend_client_existing_target_required' }
    if ((Get-Item -LiteralPath $taskExistingKeyDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'backend_test_reparse_target_rejected' }
    $taskExistingEpochState = Get-Content -LiteralPath (Join-Path $taskStateDirectory 'b1test-state.json') -Raw | ConvertFrom-Json
    if ($taskExistingEpochState.Version -ne 1 -or [Guid]::Parse($taskExistingEpochState.RecoveryEpoch) -eq [Guid]::Empty) { throw 'backend_client_epoch_invalid' }
}

if ($Initialize) {
    if (Test-Path -LiteralPath $taskStateDirectory) { throw 'backend_test_existing_target_no_reinitialize' }
    [void][IO.Directory]::CreateDirectory($taskStateDirectory)
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $taskAcl = [Security.AccessControl.DirectorySecurity]::new()
    $taskAcl.SetOwner($taskIdentity)
    $taskAcl.SetAccessRuleProtection($true, $false)
    $taskInheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($taskIdentity, 'FullControl', $taskInheritance, 'None', 'Allow'))
    Set-Acl -LiteralPath $taskStateDirectory -AclObject $taskAcl
    $taskPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $taskEncrypted = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($taskPassword), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($taskCredentialFile, $taskEncrypted)
    $taskPasswordInput = Join-Path $taskStateDirectory 'initdb-password.txt'
    try {
        [IO.File]::WriteAllText($taskPasswordInput, $taskPassword, [Text.UTF8Encoding]::new($false))
        [void](Invoke-TaskProcess (Join-Path $taskBinaryDirectory 'initdb.exe') @('-D', $taskDataDirectory, '-U', $taskOwnerName, '--pwfile', $taskPasswordInput, '--auth', 'scram-sha-256', '--encoding', 'UTF8', '--locale', 'C'))
    } finally {
        # Only our transient plaintext input is removed; cluster, encrypted credentials and logs remain.
        if (Test-Path -LiteralPath $taskPasswordInput) { Remove-Item -LiteralPath $taskPasswordInput }
    }
    $taskPortProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $taskPortProbe.Start()
    $taskPort = $taskPortProbe.LocalEndpoint.Port
    $taskPortProbe.Stop()
    $taskState = [pscustomobject]@{ RunName=$RunName; Database=$taskDatabaseName; Owner=$taskOwnerName; DataDirectory=$taskDataDirectory; Port=$taskPort; Version=1 }
    [IO.File]::WriteAllText($taskStateFile, ($taskState | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
} else {
    if (-not (Test-Path -LiteralPath $taskStateFile) -or -not (Test-Path -LiteralPath $taskCredentialFile)) { throw 'backend_test_state_missing' }
    $taskState = Get-Content -LiteralPath $taskStateFile -Raw | ConvertFrom-Json
    if ($taskState.RunName -ne $RunName -or $taskState.Database -ne $taskDatabaseName -or $taskState.Owner -ne $taskOwnerName -or $taskState.DataDirectory -ne $taskDataDirectory -or $taskState.Version -ne 1) { throw 'backend_test_state_mismatch' }
    $taskPort = [int]$taskState.Port
    if ($taskPort -lt 1 -or $taskPort -gt 65535) { throw 'backend_test_port_invalid' }
    $taskPlain = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskCredentialFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $taskPassword = [Text.Encoding]::UTF8.GetString($taskPlain)
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($taskPlain)
}

$taskControlExecutable = Join-Path $taskBinaryDirectory 'pg_ctl.exe'
if ((Invoke-TaskProcess $taskControlExecutable @('status', '-D', $taskDataDirectory) -PermitFailure) -eq 0) { throw 'backend_test_cluster_already_running' }
$taskStartAttempted = $false
$taskStartedPid = 0
$taskStartTime = [DateTime]::UtcNow
try {
    $taskStartAttempted = $true
    [void](Invoke-TaskProcess $taskControlExecutable @('start', '-D', $taskDataDirectory, '-l', (Join-Path $taskStateDirectory 'postgres.log'), '-o', "-h 127.0.0.1 -p $taskPort -c timezone=UTC -c max_connections=32 -c data_directory=`"$taskDataDirectory`"", '-w'))
    $taskStartedPid = [int](Get-Content -LiteralPath (Join-Path $taskDataDirectory 'postmaster.pid') -TotalCount 1)
    if ($Initialize -or $CreateDatabase) {
        [void](Invoke-TaskProcess (Join-Path $taskBinaryDirectory 'createdb.exe') @('-h', '127.0.0.1', '-p', "$taskPort", '-U', $taskOwnerName, $taskDatabaseName) @{ PGPASSWORD=$taskPassword })
    }
    $taskConnection = "Host=127.0.0.1;Port=$taskPort;Database=$taskDatabaseName;Username=$taskOwnerName;Password=$taskPassword;Include Error Detail=false"
    $taskResultsBase = if ($B5B) { 'artifacts/backend-b5b/postgres-tests' } elseif ($B5A) { 'artifacts/backend-b5a/postgres-tests' } else { 'artifacts/backend-foundation/postgres-tests' }
    $taskResultsDirectory = Join-Path $taskResultsBase ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    # Stage permission cannot leak in from the parent shell or another previous test run.
    $taskTestEnvironment = @{
        DANGO_BACKEND_TEST_CONNECTION=$taskConnection; DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskDataDirectory
        DANGO_BACKEND_B1_TEST_APPROVED='0'; DANGO_BACKEND_B3_TEST_APPROVED='0'; DANGO_BACKEND_B4_TEST_APPROVED='0'; DANGO_BACKEND_B5A_TEST_APPROVED='0'; DANGO_BACKEND_B5B_TEST_APPROVED='0'
    }
    if ($B1 -or $B2 -or $B3 -or $B4 -or $B5A -or $B5B) {
        $taskB1StateFile = Join-Path $taskStateDirectory 'b1test-state.json'
        if (Test-Path -LiteralPath $taskB1StateFile) {
            $taskB1State = Get-Content -LiteralPath $taskB1StateFile -Raw | ConvertFrom-Json
            $taskB1Epoch = [Guid]::Parse($taskB1State.RecoveryEpoch)
        } else {
            if ($B2 -or $B3 -or $B4 -or $B5A -or $B5B) { throw 'backend_existing_epoch_required' }
            $taskB1Epoch = [Guid]::NewGuid()
            [IO.File]::WriteAllText($taskB1StateFile, (@{ RecoveryEpoch=$taskB1Epoch.ToString('D'); Version=1 } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        }
        if ($taskB1Epoch -eq [Guid]::Empty) { throw 'backend_b1_epoch_invalid' }
        $taskB1KeyDirectory = Join-Path $taskStateDirectory 'b1tests-keys'
        if (($B2 -or $B3 -or $B4 -or $B5A -or $B5B) -and -not (Test-Path -LiteralPath $taskB1KeyDirectory -PathType Container)) { throw 'backend_existing_keys_required' }
        if (-not ($B5A -or $B5B)) { $taskTestEnvironment.DANGO_BACKEND_B1_TEST_APPROVED = '1' }
        if ($B5A) { $taskTestEnvironment.DANGO_BACKEND_B5A_TEST_APPROVED = '1' }
        if ($B5B) { $taskTestEnvironment.DANGO_BACKEND_B5B_TEST_APPROVED = '1' }
        $taskTestEnvironment.DANGO_BACKEND_B1_RECOVERY_EPOCH = $taskB1Epoch.ToString('D')
        $taskTestEnvironment.DANGO_BACKEND_B1_KEY_DIRECTORY = $taskB1KeyDirectory
        # B2 uses existing tables only; fixture validation rejects any pending migration.
        if ($B3 -or $B4) { $taskTestEnvironment.DANGO_BACKEND_B3_TEST_APPROVED = '1' }
        if ($B4) { $taskTestEnvironment.DANGO_BACKEND_B4_TEST_APPROVED = '1' }
        if ($B1 -or $B3 -or $B4) {
            $taskMigrationEnvironment = @{
                DANGO_DATABASE=$taskDatabaseName; DANGO_CONNECTION_STRING=$taskConnection; DANGO_KEYS_DIRECTORY=$taskB1KeyDirectory
                DANGO_ENDPOINT=('https://127.0.0.1:' + $taskState.Port); DANGO_RECOVERY_EPOCH=$taskB1Epoch.ToString('D')
                DANGO_EXPECTED_DATA_DIRECTORY=$taskDataDirectory
            }
            if ($B4) {
                $taskMigrationEnvironment.DANGO_EXPECTED_MIGRATION = ''
                $taskMigrationEnvironment.DANGO_EXPECTED_MIGRATIONS = '20261009055558_RoomAccess,20261009062701_RoomAccessIntegrity'
            } else {
                $taskMigrationEnvironment.DANGO_EXPECTED_MIGRATIONS = ''
                $taskMigrationEnvironment.DANGO_EXPECTED_MIGRATION = if ($B3) { '20261009031410_RoomManagement' } else { '20261008085521_AccountLifecycle' }
            }
            [void](Invoke-TaskProcess 'dotnet' @('src/Dango.Server/bin/Release/net10.0/Dango.Server.dll', '--formal-migrate') $taskMigrationEnvironment)
        }
    }
    $taskBaseAllowedFilter = 'TestCategory=PostgresIntegration&TestCategory!=BackendB5AIntegration&TestCategory!=BackendB5BIntegration'
    $taskTestFilter = if ($TestFilter) { "($taskBaseAllowedFilter)&($TestFilter)" } elseif ($B2 -or $B3 -or $B4) { "$taskBaseAllowedFilter&FullyQualifiedName!~PostgresFoundationTests" } else { $taskBaseAllowedFilter }
    if ($B4) {
        # A requested subset can narrow B4, never re-enable B0 schema/probe writes.
        $taskB4AllowedFilter = "$taskBaseAllowedFilter&FullyQualifiedName!~PostgresFoundationTests"
        $taskTestFilter = if ($TestFilter) { "($taskB4AllowedFilter)&($TestFilter)" } else { $taskB4AllowedFilter }
    }
    if ($B5A) {
        # Extra filters may narrow B5A, never opt into B0/B1/B3/B4 data operations.
        $taskB5AAllowedFilter = 'TestCategory=PostgresIntegration&TestCategory=BackendB5AIntegration&FullyQualifiedName~Dango.Server.Tests.Accounts.AccountClientHttpsIntegrationTests'
        $taskTestFilter = if ($TestFilter) { "($taskB5AAllowedFilter)&($TestFilter)" } else { $taskB5AAllowedFilter }
    }
    if ($B5B) {
        # Extra filters may narrow B5B; no earlier-stage tests or resource writes are enabled.
        $taskB5BAllowedFilter = 'TestCategory=PostgresIntegration&TestCategory=BackendB5BIntegration&FullyQualifiedName~Dango.Server.Tests.RoomManagement.RoomClientHttpsIntegrationTests'
        $taskTestFilter = if ($TestFilter) { "($taskB5BAllowedFilter)&($TestFilter)" } else { $taskB5BAllowedFilter }
    }
    [void](Invoke-TaskProcess 'dotnet' @('test', 'tests/Dango.Server.Tests/Dango.Server.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--filter', $taskTestFilter, '--logger', 'trx;LogFileName=backend-postgres.trx', '--results-directory', $taskResultsDirectory) $taskTestEnvironment)
} finally {
    if ($taskStartAttempted) {
        $taskPidFile = Join-Path $taskDataDirectory 'postmaster.pid'
        if (Test-Path -LiteralPath $taskPidFile) {
            $taskCurrentPid = [int](Get-Content -LiteralPath $taskPidFile -TotalCount 1)
            $taskCurrentProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $taskCurrentPid"
            $taskExpectedExecutable = Join-Path $taskBinaryDirectory 'postgres.exe'
            $taskIdentityMatches = $null -ne $taskCurrentProcess -and $taskCurrentProcess.ExecutablePath -eq $taskExpectedExecutable -and
                $taskCurrentProcess.CommandLine.Replace('/', '\').Contains($taskDataDirectory) -and
                $taskCurrentProcess.CreationDate.ToUniversalTime() -ge $taskStartTime.AddSeconds(-1) -and
                ($taskStartedPid -eq 0 -or $taskStartedPid -eq $taskCurrentPid)
            if ($taskIdentityMatches) {
                $taskStartedPid = $taskCurrentPid
                [void](Invoke-TaskProcess $taskControlExecutable @('stop', '-D', $taskDataDirectory, '-m', 'fast', '-w'))
            } else { Write-Warning "backend_test_identity_unverified_no_stop_attempted; possible_residual_pid=$taskCurrentPid" }
        }
    }
    $taskPassword = $null
    $taskConnection = $null
    Write-Host "Retained approved cluster: $taskDataDirectory; database=$taskDatabaseName; port=$taskPort; started_pid=$taskStartedPid"
}

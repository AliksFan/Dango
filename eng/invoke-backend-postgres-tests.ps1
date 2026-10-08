[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunName,
    [Parameter(Mandatory)][string]$PostgresBinDirectory,
    [switch]$Initialize,
    [switch]$CreateDatabase,
    [switch]$B1,
    [switch]$ExecuteApprovedPlan
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This B0 tool is limited to a fictional, explicitly approved cluster. It never resets or deletes it.
if (-not $IsWindows) { throw 'backend_test_windows_required' }
if ($RunName -notmatch '^run[a-z0-9]{1,40}$') { throw 'backend_test_run_name_invalid' }
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
    [pscustomobject]@{ RunName=$RunName; DataDirectory=$taskDataDirectory; Database=$taskDatabaseName; Initialize=[bool]$Initialize; CreateDatabase=[bool]$CreateDatabase; B1=[bool]$B1; Writes='explicit forward migration and fictional tests; no reset, deletion or restore' }
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
    $taskResultsDirectory = Join-Path 'artifacts/backend-foundation/postgres-tests' ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
    $taskTestEnvironment = @{ DANGO_BACKEND_TEST_CONNECTION=$taskConnection; DANGO_BACKEND_TEST_DATA_DIRECTORY=$taskDataDirectory }
    if ($B1) { $taskTestEnvironment.DANGO_BACKEND_B1_TEST_APPROVED = '1' }
    [void](Invoke-TaskProcess 'dotnet' @('test', 'tests/Dango.Server.Tests/Dango.Server.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--filter', 'TestCategory=PostgresIntegration', '--logger', 'trx;LogFileName=backend-postgres.trx', '--results-directory', $taskResultsDirectory) $taskTestEnvironment)
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

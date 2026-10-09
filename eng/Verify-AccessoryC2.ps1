[CmdletBinding()]
param(
    [string]$ResultsDirectory = 'artifacts/accessory-c2/final',
    [string]$NativeModule
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepositoryRoot = Split-Path -Parent $PSScriptRoot
$taskOriginalFixture = [Environment]::GetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', 'Process')
$taskProtectedDll = Join-Path $taskRepositoryRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskExpectedDllHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'

function Invoke-C2Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "C2 verification failed with exit code $LASTEXITCODE." }
}

Push-Location $taskRepositoryRoot
try {
    if ((Get-FileHash -LiteralPath $taskProtectedDll -Algorithm SHA256).Hash -ne $taskExpectedDllHash) { throw 'Protected physics DLL differs from the baseline.' }
    & (Join-Path $PSScriptRoot 'Test-AccessoryC2Runner.ps1')
    $taskBuild = @('build', 'Dango.slnx', '-c', 'Release', '-m:1', '-nr:false')
    if ($NativeModule) {
        $taskModulePath = (Resolve-Path -LiteralPath $NativeModule).Path
        if (-not (Test-Path -LiteralPath $taskModulePath -PathType Leaf)) { throw 'C2 isolated physics module is missing.' }
        $taskBuild += "-p:DangoPhysicsModulePath=$taskModulePath"
    }
    Invoke-C2Dotnet -Arguments $taskBuild
    $taskFixture = Join-Path $taskRepositoryRoot 'src/Dango.Server/bin/Release/net10.0/Dango.Server.exe'
    [Environment]::SetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', $taskFixture, 'Process')
    # Only the old anonymous, memory-backed network fixture is started by Client.Sync regressions.
    # Formal PostgreSQL tests are excluded; this script never grants data-stage authorization.
    $taskDesktopFilter = 'FullyQualifiedName~AccountSync|FullyQualifiedName~AccessoryStorageTests|FullyQualifiedName~AccessoryDeletionTests|FullyQualifiedName~AccessoryCanonicalImageIntegrationTests|FullyQualifiedName~AccessoryBackupModuleIntegrationTests|FullyQualifiedName~AccessoryAccountControllerTests|FullyQualifiedName~AccountPanelViewTests|FullyQualifiedName~AccountIntegrationBoundaryTests'
    if ($NativeModule) { $taskDesktopFilter += '|FullyQualifiedName~UnifiedLiveHomeTests' }
    $taskChecks = @(
        @{ Project = 'tests/Dango.Protocol.Tests/Dango.Protocol.Tests.csproj'; Log = 'protocol.trx' },
        @{ Project = 'tests/Dango.AccessoryAssets.Tests/Dango.AccessoryAssets.Tests.csproj'; Log = 'assets.trx' },
        @{ Project = 'tests/Dango.AccessoryModeration.Tests/Dango.AccessoryModeration.Tests.csproj'; Log = 'moderation.trx' },
        @{ Project = 'tests/Dango.Client.Sync.Tests/Dango.Client.Sync.Tests.csproj'; Log = 'client.trx' },
        @{ Project = 'tests/Dango.Server.Tests/Dango.Server.Tests.csproj'; Log = 'server-static.trx'; Filter = 'TestCategory!=PostgresIntegration' },
        @{ Project = 'tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj'; Log = 'desktop.trx'; Filter = $taskDesktopFilter }
    )
    foreach ($taskCheck in $taskChecks) {
        $taskArguments = @('test', $taskCheck.Project, '-c', 'Release', '--no-build', '--no-restore', '--logger',
            "trx;LogFileName=$($taskCheck.Log)", '--results-directory', $ResultsDirectory)
        if ($taskCheck.ContainsKey('Filter')) { $taskArguments += @('--filter', $taskCheck.Filter) }
        Invoke-C2Dotnet -Arguments $taskArguments
    }
    if ((Get-FileHash -LiteralPath $taskProtectedDll -Algorithm SHA256).Hash -ne $taskExpectedDllHash) { throw 'Protected physics DLL changed during verification.' }
    $taskResidualServers = @(Get-Process -Name Dango.Server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskFixture })
    if ($taskResidualServers.Count) { throw 'The C2 regression fixture left a server process running.' }
    if (-not $NativeModule) { Write-Host 'Unified live-home native checks were not included; supply an isolated module to run them.' }
}
finally {
    [Environment]::SetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', $taskOriginalFixture, 'Process')
    Pop-Location
}

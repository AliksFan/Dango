[CmdletBinding()]
param([string]$ResultsDirectory = 'artifacts/accessory-account-sync')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepositoryRoot = Split-Path -Parent $PSScriptRoot
$taskOriginalFixture = [Environment]::GetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', 'Process')
$taskProtectedDll = Join-Path $taskRepositoryRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskExpectedDllHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'

function Invoke-CheckedDotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet verification failed with exit code $LASTEXITCODE." }
}

Push-Location $taskRepositoryRoot
try {
    if ((Get-FileHash -LiteralPath $taskProtectedDll -Algorithm SHA256).Hash -ne $taskExpectedDllHash) {
        throw 'Protected physics DLL differs from this increment baseline.'
    }
    Invoke-CheckedDotnet -Arguments @('build', 'Dango.slnx', '-c', 'Release', '-m:1', '-nr:false')
    $taskFixture = Join-Path $taskRepositoryRoot 'src/Dango.Server/bin/Release/net10.0/Dango.Server.exe'
    if (-not (Test-Path -LiteralPath $taskFixture -PathType Leaf)) { throw 'Windows network-test fixture was not built.' }
    [Environment]::SetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', $taskFixture, 'Process')

    $taskChecks = @(
        @{ Project = 'tests/Dango.Protocol.Tests/Dango.Protocol.Tests.csproj'; Log = 'contract-final.trx' },
        @{ Project = 'tests/Dango.AccessoryAssets.Tests/Dango.AccessoryAssets.Tests.csproj'; Log = 'assets-final.trx' },
        @{ Project = 'tests/Dango.Client.Sync.Tests/Dango.Client.Sync.Tests.csproj'; Log = 'client-final.trx' },
        @{ Project = 'tests/Dango.AccessoryModeration.Tests/Dango.AccessoryModeration.Tests.csproj'; Log = 'moderation-final.trx' },
        @{ Project = 'tests/Dango.Desktop.Tests/Dango.Desktop.Tests.csproj'; Log = 'desktop-final.trx';
           Filter = 'FullyQualifiedName~AccountSync|FullyQualifiedName~AccessoryStorageTests|FullyQualifiedName~AccessoryDeletionTests|FullyQualifiedName~AccessoryCanonicalImageIntegrationTests|FullyQualifiedName~AccessoryBackupModuleIntegrationTests' }
    )
    foreach ($taskCheck in $taskChecks) {
        $taskArguments = @('test', $taskCheck.Project, '-c', 'Release', '--no-build', '--logger',
            "trx;LogFileName=$($taskCheck.Log)", '--results-directory', $ResultsDirectory)
        if ($taskCheck.ContainsKey('Filter')) { $taskArguments += @('--filter', $taskCheck.Filter) }
        Invoke-CheckedDotnet -Arguments $taskArguments
    }
    Invoke-CheckedDotnet -Arguments @('run', '--project', 'eng/tools/Dango.AccessoryReview', '-c', 'Release', '--no-build', '--', '--help')
    if ((Get-FileHash -LiteralPath $taskProtectedDll -Algorithm SHA256).Hash -ne $taskExpectedDllHash) {
        throw 'Protected physics DLL changed during verification.'
    }
    $taskResidualServers = @(Get-Process -Name Dango.Server -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $taskFixture })
    if ($taskResidualServers.Count -ne 0) { throw 'The network-test fixture left a server process running.' }
}
finally {
    [Environment]::SetEnvironmentVariable('DANGO_SYNC_SERVER_PATH', $taskOriginalFixture, 'Process')
    Pop-Location
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory = $true)][string]$PhysicsModulePath,
    [switch]$Build,
    [switch]$Verify,
    [switch]$FallbackB
)
$ErrorActionPreference = 'Stop'
$syncRepository = Split-Path -Parent $PSScriptRoot
$syncModule = (Resolve-Path -LiteralPath $PhysicsModulePath).Path
$syncArtifacts = Join-Path $syncRepository 'artifacts/multiplayer-sync'
$syncOutput = Join-Path $syncArtifacts 'implementation/build'
$syncSession = Join-Path $syncArtifacts $RunName
if (Test-Path -LiteralPath $syncSession) { throw 'Session exists; choose a new RunName. No files were removed.' }
Push-Location $syncRepository
try {
    if ($Build) {
        & dotnet build Dango.slnx -c Release --artifacts-path $syncOutput "-p:DangoPhysicsModulePath=$syncModule"
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    $syncSample = Join-Path $syncOutput 'bin/Dango.MultiplayerSample/release/Dango.MultiplayerSample.exe'
    $syncServer = Join-Path $syncOutput 'bin/Dango.Server/release/Dango.Server.exe'
    $syncBundled = Join-Path (Split-Path $syncSample) 'dango_physics_module.dll'
    foreach ($syncFile in @($syncSample, $syncServer, $syncBundled)) {
        if (-not (Test-Path -LiteralPath $syncFile -PathType Leaf)) { throw "Missing final-build artifact: $syncFile" }
    }
    if ((Get-FileHash -LiteralPath $syncModule).Hash -ne (Get-FileHash -LiteralPath $syncBundled).Hash) { throw 'Native fingerprint mismatch; rebuild required.' }
    $syncStart = [System.Diagnostics.ProcessStartInfo]::new($syncSample)
    $syncStart.UseShellExecute = $false
    $syncStart.CreateNoWindow = $true
    foreach ($syncArg in @('--session', $RunName, '--server', $syncServer)) { $syncStart.ArgumentList.Add($syncArg) }
    if ($Verify) { $syncStart.ArgumentList.Add('--verify') }
    if ($FallbackB) { $syncStart.ArgumentList.Add('--fallback-b') }
    $syncProcess = [System.Diagnostics.Process]::Start($syncStart)
    Write-Output "Session launcher PID $($syncProcess.Id); evidence: $syncSession"
    if ($Verify) {
        $syncProcess.WaitForExit()
        if ($syncProcess.ExitCode -ne 0) { throw "Verification failed ($($syncProcess.ExitCode)). Evidence retained: $syncSession" }
    }
} finally { Pop-Location }

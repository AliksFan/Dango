param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]+$')][string]$RunName,
    [Parameter(Mandatory)][string]$NativeModule,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$modulePath = (Resolve-Path -LiteralPath $NativeModule).Path
$expected = 'BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507'
if ((Get-FileHash -LiteralPath $modulePath).Hash -ne $expected) { throw 'The isolated room candidate module differs.' }
$protectedLibrary = Join-Path $repoRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$protectedHash = (Get-FileHash -LiteralPath $protectedLibrary).Hash
if ($protectedHash -ne '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A') { throw 'Protected library differs.' }
$buildRoot = Join-Path $repoRoot "artifacts/desktop-pet-room/$RunName"
if (Test-Path -LiteralPath $buildRoot) { throw 'Use a fresh RunName. Existing review outputs are preserved.' }
& dotnet build (Join-Path $repoRoot 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release --artifacts-path $buildRoot "-p:DangoPhysicsModulePath=$modulePath"
if ($LASTEXITCODE -ne 0) { throw 'Review build failed.' }
if ((Get-FileHash -LiteralPath $protectedLibrary).Hash -ne $protectedHash) { throw 'Protected library changed.' }
$exe = Join-Path $buildRoot 'bin/Dango.Desktop/release/Dango.Desktop.exe'
if ((Get-FileHash -LiteralPath (Join-Path (Split-Path $exe) 'dango_physics_module.dll')).Hash -ne $expected) { throw 'Review module copy differs.' }
$reviewLauncher = Join-Path (Split-Path $exe) 'start-desktop-pet-room-review.cmd'
@('@echo off', 'start "" "%~dp0Dango.Desktop.exe" --desktop-pet-room-review') |
    Set-Content -LiteralPath $reviewLauncher -Encoding Ascii
if (!$NoLaunch) {
    # The requested interactive review opens the actual layered room with isolated settings and mutex.
    $reviewProcess = Start-Process -FilePath $exe -ArgumentList '--desktop-pet-room-review' -PassThru
    Write-Output "Desktop pet room review PID: $($reviewProcess.Id)"
}

param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]+$')][string]$RunName,
    [string]$CalibrationData,
    [ValidateSet('Static', 'Eyes', 'Hats', 'Hosts')][string]$Mode = 'Static',
    [switch]$Build,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = Join-Path $repoRoot "artifacts/p1-eye-hat/$RunName"
$buildRoot = Join-Path $outputRoot 'preview-build'
$executable = Join-Path $buildRoot 'bin/Dango.JointReview/release/Dango.JointReview.exe'
if ($Mode -eq 'Hosts') { $executable = Join-Path $buildRoot 'bin/Dango.Desktop/release/Dango.Desktop.exe' }
$dataPath = $null
if ($Mode -eq 'Static') {
    if (!$CalibrationData) { throw 'Static review requires CalibrationData.' }
    $dataPath = (Resolve-Path -LiteralPath $CalibrationData).Path
    if (!(Get-ChildItem -LiteralPath $dataPath -Filter 'body-*.json' -File)) { throw 'Missing static module calibration frames.' }
}
$formalLibrary = Join-Path $repoRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$moduleLibrary = Join-Path $repoRoot 'artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll'
function Assert-LibrariesUnchanged {
    if ((Get-FileHash -LiteralPath $formalLibrary).Hash -ne '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A') { throw 'Formal library differs from the accepted baseline.' }
    if ((Get-FileHash -LiteralPath $moduleLibrary).Hash -ne '00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C') { throw 'Unified module differs from the accepted baseline.' }
}
Assert-LibrariesUnchanged
if ($Build) {
    if (Test-Path -LiteralPath $buildRoot) { throw 'Preview build directory already exists. Use a fresh RunName; nothing was overwritten or stopped.' }
    if ($Mode -eq 'Hosts') {
        & dotnet build (Join-Path $repoRoot 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release --artifacts-path $buildRoot "-p:DangoPhysicsModulePath=$moduleLibrary"
    } else {
        & dotnet build (Join-Path $repoRoot 'eng/tools/Dango.JointReview/Dango.JointReview.csproj') -c Release --artifacts-path $buildRoot
    }
    if ($LASTEXITCODE -ne 0) { throw "Managed preview build failed: $LASTEXITCODE" }
    Assert-LibrariesUnchanged
}
if (!(Test-Path -LiteralPath $executable)) { throw 'Preview executable missing. Build once with a fresh RunName.' }
if ($Mode -in @('Eyes', 'Hats', 'Hosts')) {
    $isolatedModule = Join-Path (Split-Path $executable) 'dango_physics_module.dll'
    if (!(Test-Path -LiteralPath $isolatedModule)) { [IO.File]::Copy($moduleLibrary, $isolatedModule, $false) }
    if ((Get-FileHash -LiteralPath $isolatedModule).Hash -ne (Get-FileHash -LiteralPath $moduleLibrary).Hash) { throw 'Isolated module copy differs.' }
}
if (!$NoLaunch) {
    # This is the interactive calibration window requested for the manual gate.
    # It reads fictional static frames and does not start a physics world.
    if ($Mode -eq 'Eyes') { & $executable --eye-motion-review $isolatedModule }
    elseif ($Mode -eq 'Hats') { & $executable --hat-motion-review $isolatedModule }
    elseif ($Mode -eq 'Hosts') { & $executable --eye-hat-review }
    else { & $executable --attachment-calibration $dataPath }
}

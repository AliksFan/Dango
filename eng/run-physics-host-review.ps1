param(
    [switch]$Build,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$modulePath = Join-Path $repoRoot 'artifacts/native-spike/physics-module-unified-build/Release/dango_physics_module.dll'
$reviewOutput = Join-Path $repoRoot 'artifacts/p2d-physics-desktop/host-review'
$executable = Join-Path $reviewOutput 'Dango.Desktop.exe'
$approvedHash = '00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C'

if (!(Test-Path -LiteralPath $modulePath)) {
    throw 'Missing accepted module. Follow docs/phase-1-physics-module.md; do not rebuild or replace the old formal DLL.'
}
if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $approvedHash) {
    throw 'The module differs from the accepted unified baseline. Revalidate it before desktop review.'
}
if ($Build) {
    $runningReview = Get-CimInstance Win32_Process -Filter "Name = 'Dango.Desktop.exe'" |
        Where-Object { $_.ExecutablePath -eq $executable }
    if ($runningReview) { throw 'Close this review application normally before rebuilding. No process was stopped.' }
    & dotnet publish (Join-Path $repoRoot 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release -r win-x64 --self-contained true `
        -o $reviewOutput "-p:DangoPhysicsModulePath=$modulePath"
    if ($LASTEXITCODE -ne 0) { throw "Desktop review publish failed: $LASTEXITCODE" }
}
if (!(Test-Path -LiteralPath $executable)) { throw 'Review build missing. Run this script with -Build first.' }
if ((Get-FileHash -LiteralPath (Join-Path $reviewOutput 'dango_physics_module.dll') -Algorithm SHA256).Hash -ne $approvedHash) {
    throw 'The review output contains a different physics module.'
}
if (!$NoLaunch) {
    & $executable --physics-host-review
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$cmake = if ($cmakeCommand) { $cmakeCommand.Source } else { $null }
if (-not $cmake) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.CMake.Project -property installationPath
        if ($installation) { $cmake = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' }
    }
}
if (-not $cmake -or -not (Test-Path -LiteralPath $cmake)) { throw 'CMake and a Windows x64 C compiler are required to build the desktop belt modules (Visual Studio C++ CMake tools or CMake on PATH).' }
$build = Join-Path $OutputDirectory 'build'
& $cmake -S (Join-Path $repo 'eng/native/box2d') -B $build -A x64 -DDANGO_BELT_ONLY=ON
if ($LASTEXITCODE -ne 0) { throw 'Belt native configuration failed.' }
& $cmake --build $build --config $Configuration --target dango_belt_product dango_belt_room
if ($LASTEXITCODE -ne 0) { throw 'Belt native build failed.' }
foreach ($name in @('dango_belt_product.dll', 'dango_belt_room.dll')) {
    Copy-Item -LiteralPath (Join-Path $build ($Configuration + '/' + $name)) -Destination (Join-Path $OutputDirectory $name) -Force
}
& (Join-Path $PSScriptRoot 'test-belt-native-package.ps1') -NativeDirectory $OutputDirectory -RepositoryRoot $repo -WriteManifest | Out-Null

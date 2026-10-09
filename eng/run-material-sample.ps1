[CmdletBinding()]
param([string]$NativeModulePath, [string]$BaselineNativeModulePath)

$ErrorActionPreference = 'Stop'
$roundRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$roundOutput = Join-Path $roundRepositoryRoot 'artifacts\round-material\app'
if (-not $NativeModulePath) {
    $NativeModulePath = Join-Path $roundRepositoryRoot 'artifacts\round-material\native-drag-final\Release\dango_physics_module.dll'
}
$NativeModulePath = (Resolve-Path -LiteralPath $NativeModulePath).Path
$roundLaunchArguments = @('"' + $NativeModulePath + '"')
if ($BaselineNativeModulePath) {
    $BaselineNativeModulePath = (Resolve-Path -LiteralPath $BaselineNativeModulePath).Path
    $roundLaunchArguments += @('--baseline-library', ('"{0}"' -f $BaselineNativeModulePath))
}
& dotnet build (Join-Path $roundRepositoryRoot 'eng\tools\Dango.MaterialSample\Dango.MaterialSample.csproj') -c Release -o $roundOutput
if ($LASTEXITCODE -ne 0) { throw 'Material sample build failed.' }
Start-Process -FilePath (Join-Path $roundOutput 'Dango.MaterialSample.exe') -ArgumentList $roundLaunchArguments

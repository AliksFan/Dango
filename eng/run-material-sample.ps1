[CmdletBinding()]
param([string]$NativeModulePath)

$ErrorActionPreference = 'Stop'
$roundRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$roundOutput = Join-Path $roundRepositoryRoot 'artifacts\round-material\app'
if (-not $NativeModulePath) {
    $NativeModulePath = Join-Path $roundRepositoryRoot 'artifacts\round-material\native-drag-final\Release\dango_physics_module.dll'
}
$NativeModulePath = (Resolve-Path -LiteralPath $NativeModulePath).Path
& dotnet build (Join-Path $roundRepositoryRoot 'eng\tools\Dango.MaterialSample\Dango.MaterialSample.csproj') -c Release -o $roundOutput
if ($LASTEXITCODE -ne 0) { throw 'Material sample build failed.' }
Start-Process -FilePath (Join-Path $roundOutput 'Dango.MaterialSample.exe') -ArgumentList @('"' + $NativeModulePath + '"')

param(
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}$')]
    [string]$RunName = 'manual-review',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reviewBase = [System.IO.Path]::GetFullPath((Join-Path $repository 'artifacts/three-dango-slots'))
$reviewData = [System.IO.Path]::GetFullPath((Join-Path $reviewBase $RunName))
if (-not $reviewData.StartsWith($reviewBase + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The review data directory must stay inside the three-dango-slots artifact directory.'
}

if ($Build) {
    & dotnet build (Join-Path $repository 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The product build failed; the review was not started.' }
}

$program = Join-Path $repository 'src/Dango.Desktop/bin/Release/net10.0-windows/Dango.Desktop.exe'
if (-not (Test-Path -LiteralPath $program -PathType Leaf)) {
    throw 'Build the candidate first, or run this script with -Build.'
}

# Always supply an isolated root. Reusing RunName preserves only this review's saves.
Write-Host "Review data: artifacts/three-dango-slots/$RunName"
& $program --data-root $reviewData

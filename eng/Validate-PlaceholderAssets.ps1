[CmdletBinding()]
param(
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$placeholderRoot = Join-Path $repositoryRoot 'assets\placeholder'

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $placeholderRoot 'manifest.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Placeholder manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.manifestVersion -ne 1) {
    throw "Unsupported placeholder manifest version: $($manifest.manifestVersion)"
}

if (-not $manifest.files -or $manifest.files.Count -eq 0) {
    throw 'Placeholder manifest must contain at least one file.'
}

$placeholderRootFull = [System.IO.Path]::GetFullPath($placeholderRoot) + [System.IO.Path]::DirectorySeparatorChar
foreach ($fileEntry in $manifest.files) {
    if ([string]::IsNullOrWhiteSpace($fileEntry.path) -or
        [System.IO.Path]::IsPathRooted($fileEntry.path)) {
        throw "Placeholder path must be repository-relative: $($fileEntry.path)"
    }

    if ([string]::IsNullOrWhiteSpace($fileEntry.source) -or
        [string]::IsNullOrWhiteSpace($fileEntry.license)) {
        throw "Placeholder entry must declare source and license: $($fileEntry.path)"
    }

    $assetPath = [System.IO.Path]::GetFullPath((Join-Path $placeholderRoot $fileEntry.path))
    if (-not $assetPath.StartsWith($placeholderRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Placeholder path escapes its directory: $($fileEntry.path)"
    }

    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Placeholder file not found: $($fileEntry.path)"
    }
}

Write-Output "Validated $($manifest.files.Count) placeholder asset entry."

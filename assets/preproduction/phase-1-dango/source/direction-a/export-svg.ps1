[CmdletBinding()]
param(
    [string]$EdgePath
)

$ErrorActionPreference = 'Stop'

if (-not $EdgePath) {
    $edgeCandidates = @(
        'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
        'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
    )
    $EdgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $EdgePath -or -not (Test-Path -LiteralPath $EdgePath)) {
    throw 'Microsoft Edge was not found. Pass its absolute path with -EdgePath.'
}

$sourceDirectory = $PSScriptRoot
$assetRoot = Split-Path (Split-Path $sourceDirectory -Parent) -Parent
$exportDirectory = Join-Path $assetRoot 'export\direction-a'
$profileDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('dango-svg-export-' + [guid]::NewGuid().ToString('N'))
$resolvedTempDirectory = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$resolvedProfileDirectory = [System.IO.Path]::GetFullPath($profileDirectory)
if (-not $resolvedProfileDirectory.StartsWith($resolvedTempDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a browser profile outside the temporary directory: $resolvedProfileDirectory"
}
$sourceNames = @(
    'body',
    'eyes-default',
    'mouth-default',
    'blush-default',
    'hat-sprout',
    'preview-default'
)

New-Item -ItemType Directory -Force -Path $exportDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $profileDirectory | Out-Null

try {
    $exports = @()
    foreach ($name in $sourceNames) {
        $sourcePath = Join-Path $sourceDirectory ($name + '.svg')
        $outputPath = Join-Path $exportDirectory ($name + '.png')
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }
        $sourceUri = ([uri]$sourcePath).AbsoluteUri
        $imageProfileDirectory = Join-Path $resolvedProfileDirectory $name
        $arguments = @(
            '--headless=new',
            '--disable-gpu',
            '--disable-background-networking',
            '--disable-component-update',
            '--disable-extensions',
            '--hide-scrollbars',
            '--no-default-browser-check',
            '--no-first-run',
            '--force-device-scale-factor=1',
            '--default-background-color=00000000',
            '--window-size=160,120',
            ('--user-data-dir=' + $imageProfileDirectory),
            ('--screenshot=' + $outputPath),
            $sourceUri
        )

        $process = Start-Process -FilePath $EdgePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $exports += [pscustomobject]@{
            Name = $name
            SourcePath = $sourcePath
            OutputPath = $outputPath
            Process = $process
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $missingExports = @($exports | Where-Object {
            -not (Test-Path -LiteralPath $_.OutputPath) -or (Get-Item -LiteralPath $_.OutputPath).Length -eq 0
        })
        if ($missingExports.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($missingExports.Count -ne 0) {
        $missingNames = ($missingExports | ForEach-Object Name) -join ', '
        throw "Timed out while exporting: $missingNames"
    }
}
finally {
    foreach ($export in @($exports)) {
        if ($null -ne $export.Process -and -not $export.Process.HasExited) {
            Stop-Process -Id $export.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    if (Test-Path -LiteralPath $resolvedProfileDirectory) {
        Remove-Item -LiteralPath $resolvedProfileDirectory -Recurse -Force
    }
}

Write-Output "Exported $($sourceNames.Count) transparent PNG files to $exportDirectory"

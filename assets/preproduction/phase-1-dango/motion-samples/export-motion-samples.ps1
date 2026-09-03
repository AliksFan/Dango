[CmdletBinding()]
param([string]$EdgePath)

$ErrorActionPreference = 'Stop'

if (-not $EdgePath) {
    $EdgePath = @(
        'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
        'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $EdgePath -or -not (Test-Path -LiteralPath $EdgePath)) {
    throw 'Microsoft Edge was not found. Pass its absolute path with -EdgePath.'
}

$sampleRoot = $PSScriptRoot
$sourceRoot = Join-Path $sampleRoot 'source'
$exportRoot = Join-Path $sampleRoot 'export'
$profileRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('dango-motion-export-' + [guid]::NewGuid().ToString('N'))
$resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$resolvedProfileRoot = [System.IO.Path]::GetFullPath($profileRoot)
if (-not $resolvedProfileRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a browser profile outside the temporary directory: $resolvedProfileRoot"
}

$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.svg' | Sort-Object FullName)
if ($sourceFiles.Count -eq 0) {
    throw 'No SVG motion sources were found.'
}

New-Item -ItemType Directory -Force -Path $exportRoot | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedProfileRoot | Out-Null
$exports = @()

try {
    foreach ($sourceFile in $sourceFiles) {
        $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length + 1)
        $relativePng = [System.IO.Path]::ChangeExtension($relativePath, '.png')
        $outputPath = Join-Path $exportRoot $relativePng
        $outputDirectory = Split-Path $outputPath -Parent
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }

        $profilePath = Join-Path $resolvedProfileRoot ([guid]::NewGuid().ToString('N'))
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
            ('--user-data-dir=' + $profilePath),
            ('--screenshot=' + $outputPath),
            ([uri]$sourceFile.FullName).AbsoluteUri
        )
        $process = Start-Process -FilePath $EdgePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        $exports += [pscustomobject]@{ Source = $sourceFile; Output = $outputPath; Process = $process }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $missing = @($exports | Where-Object {
            -not (Test-Path -LiteralPath $_.Output) -or (Get-Item -LiteralPath $_.Output).Length -eq 0
        })
        if ($missing.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($missing.Count -ne 0) {
        throw ('Timed out while exporting: ' + (($missing | ForEach-Object Output) -join ', '))
    }
}
finally {
    foreach ($export in @($exports)) {
        if ($null -ne $export.Process -and -not $export.Process.HasExited) {
            Stop-Process -Id $export.Process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    $escapedProfileRoot = [System.Management.Automation.WildcardPattern]::Escape($resolvedProfileRoot)
    Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" |
        Where-Object { $_.CommandLine -like ('*' + $escapedProfileRoot + '*') } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $resolvedProfileRoot); $attempt++) {
        try {
            Remove-Item -LiteralPath $resolvedProfileRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            if ($attempt -eq 19) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
}

$sourceEntries = @(
    [ordered]@{
        path = 'motion-samples.json'
        sourceType = 'project-created'
        source = 'Approved AP-004 timing, layer, anchor, and pose mapping'
        license = 'Dango project-owned source'
        tool = 'Codex apply_patch'
        createdOn = '2026-09-02'
        modifications = 'Uses only body, eyes, and hat layers'
    },
    [ordered]@{
        path = 'export-motion-samples.ps1'
        sourceType = 'project-code-generated'
        source = 'Deterministic local SVG export helper'
        license = 'Dango project-owned source'
        tool = 'Codex apply_patch'
        createdOn = '2026-09-02'
        modifications = 'Exports fixed 160 by 120 transparent PNG files with local Microsoft Edge'
    }
)
foreach ($sourceFile in $sourceFiles) {
    $relative = 'source/' + $sourceFile.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
    $sourceEntries += [ordered]@{
        path = $relative
        sourceType = 'project-created'
        source = 'Original deterministic vector key pose for the approved direction A'
        license = 'Dango project-owned original; no third-party source'
        tool = 'Codex apply_patch'
        createdOn = '2026-09-02'
        modifications = 'Solid-color editor-neutral SVG with no mouth, blush, shadow, or limbs'
    }
}
$sourceProvenance = [ordered]@{ '$schema' = '../provenance.schema.json'; version = 1; files = $sourceEntries }
$sourceJson = ($sourceProvenance | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $sampleRoot 'provenance.json'), $sourceJson + "`n", [System.Text.UTF8Encoding]::new($false))

$exportEntries = @()
foreach ($export in $exports) {
    $relativeOutput = $export.Output.Substring($exportRoot.Length + 1).Replace('\', '/')
    $relativeSource = 'source/' + $export.Source.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
    $exportEntries += [ordered]@{
        path = $relativeOutput
        sourceType = 'project-code-generated'
        source = $relativeSource
        license = 'Dango project-owned original; no third-party source'
        tool = 'Microsoft Edge headless via export-motion-samples.ps1'
        createdOn = '2026-09-02'
        modifications = 'Transparent 160 by 120 raster export'
    }
}
$exportProvenance = [ordered]@{ '$schema' = '../../provenance.schema.json'; version = 1; files = $exportEntries }
$exportJson = ($exportProvenance | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $exportRoot 'provenance.json'), $exportJson + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Output "Exported $($exports.Count) AP-004 PNG files to $exportRoot"

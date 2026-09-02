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

$reviewRoot = $PSScriptRoot
$sourcePath = Join-Path $reviewRoot 'phase-1-motion-samples.html'
$outputPath = Join-Path $reviewRoot 'phase-1-motion-samples.png'
$profilePath = Join-Path ([System.IO.Path]::GetTempPath()) ('dango-motion-review-' + [guid]::NewGuid().ToString('N'))
$resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$resolvedProfilePath = [System.IO.Path]::GetFullPath($profilePath)
if (-not $resolvedProfilePath.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a browser profile outside the temporary directory: $resolvedProfilePath"
}

if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
New-Item -ItemType Directory -Force -Path $resolvedProfilePath | Out-Null
try {
    $arguments = @(
        '--headless=new', '--disable-gpu', '--disable-background-networking', '--disable-component-update',
        '--disable-extensions', '--hide-scrollbars', '--no-default-browser-check', '--no-first-run',
        '--allow-file-access-from-files', '--force-device-scale-factor=1', '--window-size=920,500',
        ('--user-data-dir=' + $resolvedProfilePath), ('--screenshot=' + $outputPath),
        ([uri]$sourcePath).AbsoluteUri
    )
    $process = Start-Process -FilePath $EdgePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ((-not (Test-Path -LiteralPath $outputPath) -or (Get-Item -LiteralPath $outputPath).Length -eq 0) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-Path -LiteralPath $outputPath)) { throw 'Timed out while exporting the review board.' }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    $escapedProfilePath = [System.Management.Automation.WildcardPattern]::Escape($resolvedProfilePath)
    Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" |
        Where-Object { $_.CommandLine -like ('*' + $escapedProfilePath + '*') } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $resolvedProfilePath); $attempt++) {
        try { Remove-Item -LiteralPath $resolvedProfilePath -Recurse -Force -ErrorAction Stop }
        catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 200 }
    }
}
Write-Output "Exported AP-004 review board to $outputPath"

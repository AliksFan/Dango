param(
    [Parameter(Mandatory = $true)][string]$ProbePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('Window', 'Wallpaper')][string]$Mode = 'Wallpaper',
    [ValidateRange(10, 3600)][int]$StableSeconds = 60
)

$ErrorActionPreference = 'Stop'
$probeExecutable = (Resolve-Path -LiteralPath $ProbePath).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'Use a new output directory.' }
$localDataRoot = [Environment]::GetFolderPath('LocalApplicationData')
foreach ($protectedRoot in @((Join-Path $localDataRoot 'Dango'), (Join-Path $localDataRoot 'Programs\Dango'))) {
    if ($outputPath.Equals($protectedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $outputPath.StartsWith($protectedRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Output must not be inside the daily settings or installation directories.'
    }
}
if ($outputPath.Contains('"')) { throw 'Invalid output path.' }
$startedUtc = [DateTime]::UtcNow.ToString('o')
$probeProcess = Start-Process -FilePath $probeExecutable -ArgumentList @(('"' + $outputPath + '"'), [string]$StableSeconds, $Mode, '--memory-diagnostic') -WindowStyle Hidden -PassThru
# Retain the OS process handle before waiting, so ExitCode remains available after exit.
$retainedHandle = $probeProcess.Handle
$waitSeconds = 60 + 5 * $StableSeconds + 20 * 8 + 30 + 120
$exited = $probeProcess.WaitForExit($waitSeconds * 1000)
if ($exited) { $probeProcess.WaitForExit() }
$exitCode = if ($exited) { $probeProcess.ExitCode } else { $null }
$record = [ordered]@{
    processId = $probeProcess.Id
    startedUtc = $startedUtc
    exitedUtc = if ($exited) { [DateTime]::UtcNow.ToString('o') } else { $null }
    exited = $exited
    exitCode = $exitCode
    timedOut = -not $exited
    processWasNotStoppedByWaiter = $true
}
if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
    throw "Probe exited before creating its output directory; process ID $($probeProcess.Id), exit code $exitCode."
}
$exitPath = Join-Path $outputPath 'process-exit.json'
$exitJson = $record | ConvertTo-Json
$exitStream = [System.IO.File]::Open($exitPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
try {
    $exitBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($exitJson + [Environment]::NewLine)
    $exitStream.Write($exitBytes, 0, $exitBytes.Length)
}
finally { $exitStream.Dispose() }
$probeProcess.Dispose()
if (-not $exited) { throw 'Diagnostic timeout: process was not stopped; inspect the recorded test PID.' }
if ($exitCode -ne 0) { throw "Diagnostic process failed with exit code $exitCode." }

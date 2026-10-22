param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][string]$PackagePath
)
$ErrorActionPreference = 'Stop'
$recoveryRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
$recoveryPackage = Test-DangoPackage -PackagePath $PackagePath
$recoveryPackageRoot = (Resolve-Path -LiteralPath $PackagePath).Path
& git -C $recoveryRepo diff --quiet $recoveryPackage.Manifest.sourceCommit -- src eng/native Directory.Build.props Directory.Build.targets
if ($LASTEXITCODE -ne 0) { throw 'Product sources differ from the selected validated package.' }
$recoveryEvidence = Join-Path $recoveryRepo "artifacts/local-mode-closeout/recovery-$RunName"
$recoveryRoot = Join-Path $recoveryRepo "artifacts/local-room-catalog/runs/recovery-$RunName"
foreach ($recoveryTarget in @($recoveryEvidence, $recoveryRoot)) {
    if (Test-Path -LiteralPath $recoveryTarget) { throw 'Use a fresh RunName; existing data and evidence are preserved.' }
    for ($recoveryCursor = $recoveryTarget; $recoveryCursor; $recoveryCursor = Split-Path -Parent $recoveryCursor) {
        if ((Test-Path -LiteralPath $recoveryCursor) -and
            ((Get-Item -LiteralPath $recoveryCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Recovery verification paths cannot traverse links.'
        }
    }
}
$recoveryProtected = Join-Path $recoveryRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$recoveryProtectedBefore = (Get-FileHash -LiteralPath $recoveryProtected).Hash
$recoverySources = @('eng/tools/Dango.LocalRoomProbe/Dango.LocalRoomProbe.csproj',
    'eng/tools/Dango.LocalRoomProbe/Program.cs', 'eng/tools/Dango.LocalRoomProbe/BeltRecoveryVerification.cs',
    'eng/run-local-room-recovery-check.ps1')
function Get-RecoverySourceHashes {
    $recoveryHashes = [ordered]@{}
    foreach ($recoveryRelative in $recoverySources) {
        $recoveryHashes[$recoveryRelative] = (Get-FileHash -LiteralPath (Join-Path $recoveryRepo $recoveryRelative)).Hash
    }
    return $recoveryHashes
}
$recoverySourceBefore = Get-RecoverySourceHashes
$null = New-Item -ItemType Directory -Path $recoveryEvidence
$recoveryBuild = Join-Path $recoveryEvidence 'probe'
$recoveryReport = [ordered]@{
    baseHead = (& git -C $recoveryRepo rev-parse HEAD).Trim()
    sourceHashes = $recoverySourceBefore
    packageSource = $recoveryPackage.Manifest.sourceCommit
    packageSourceDirty = $recoveryPackage.Manifest.sourceDirty
    binaryHashes = [ordered]@{}
    steps = @()
    passed = $false
    processesReleased = $false
    dataRetained = $true
    normalApplicationShell = $false
    realOsInput = $false
    wallClockPerformance = $false
}
$recoveryChildren = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
try {
    & dotnet build (Join-Path $recoveryRepo 'eng/tools/Dango.LocalRoomProbe/Dango.LocalRoomProbe.csproj') -c Release `
        -o $recoveryBuild "-p:DangoProductPackage=$recoveryPackageRoot" -v minimal *> (Join-Path $recoveryEvidence 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Recovery probe build failed; see build.log.' }
    foreach ($recoveryName in @('Dango.Desktop.dll', 'Dango.Core.dll', 'Dango.Physics.dll', 'Dango.Protocol.dll',
        'dango_belt_product.dll', 'dango_belt_room.dll', 'belt-native-manifest.json')) {
        $recoveryHash = (Get-FileHash -LiteralPath (Join-Path $recoveryBuild $recoveryName)).Hash
        if ($recoveryHash -ne (Get-FileHash -LiteralPath (Join-Path $recoveryPackageRoot $recoveryName)).Hash) {
            throw "Probe does not use the exact packaged binary: $recoveryName"
        }
        $recoveryReport.binaryHashes[$recoveryName] = $recoveryHash
    }
    $recoveryExe = Join-Path $recoveryBuild 'Dango.LocalRoomProbe.exe'
    $recoveryModule = Join-Path $recoveryBuild 'dango_belt_room.dll'
    foreach ($recoveryMode in @('init', 'recovery-write', 'recovery-read', 'recovery-unsafe')) {
        $recoveryArguments = @($recoveryMode, ('"{0}"' -f $recoveryRoot))
        if ($recoveryMode -ne 'init') { $recoveryArguments += ('"{0}"' -f $recoveryModule) }
        $recoveryChild = Start-Process -FilePath $recoveryExe -ArgumentList $recoveryArguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $recoveryEvidence "$recoveryMode.out.log") `
            -RedirectStandardError (Join-Path $recoveryEvidence "$recoveryMode.err.log")
        $recoveryChildren.Add($recoveryChild)
        if (!$recoveryChild.WaitForExit(45000)) { throw "Owned recovery process timed out: $recoveryMode" }
        $recoveryChild.Refresh()
        $recoveryReport.steps += [ordered]@{ mode = $recoveryMode; pid = $recoveryChild.Id; exitCode = $recoveryChild.ExitCode; exited = $true }
        if ($recoveryChild.ExitCode -ne 0) { throw "Recovery mode failed: $recoveryMode; see its error log." }
    }
    $recoveryAfter = Get-RecoverySourceHashes
    if (($recoverySourceBefore | ConvertTo-Json -Compress) -cne ($recoveryAfter | ConvertTo-Json -Compress)) {
        throw 'Verification sources changed during the run.'
    }
    $recoveryReport.passed = $true
} catch {
    $recoveryReport['failure'] = $_.Exception.Message
    throw
} finally {
    foreach ($recoveryChild in $recoveryChildren) {
        if (!$recoveryChild.HasExited) { $recoveryChild.Kill(); $recoveryChild.WaitForExit(5000) | Out-Null }
    }
    $recoveryReport.processesReleased = @($recoveryChildren | Where-Object { !$_.HasExited }).Count -eq 0
    $recoveryReport['protectedDllUnchanged'] = (Get-FileHash -LiteralPath $recoveryProtected).Hash -eq $recoveryProtectedBefore
    $recoveryReport.passed = $recoveryReport.passed -and $recoveryReport.processesReleased -and $recoveryReport.protectedDllUnchanged
    $recoveryReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $recoveryEvidence 'summary.json') -Encoding utf8
    Write-Output (Join-Path $recoveryEvidence 'summary.json')
    if (!$recoveryReport.protectedDllUnchanged) { throw 'Protected source DLL changed.' }
    if (!$recoveryReport.processesReleased) { throw 'An owned recovery process did not exit.' }
}

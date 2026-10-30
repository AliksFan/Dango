param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][ValidateSet('room', 'mouse')][string]$Mode,
    [ValidateRange(1, 3600)][double]$ActiveSeconds = 600,
    [switch]$ScreenAuthorized
)
$ErrorActionPreference = 'Stop'
if (!$ScreenAuthorized) { throw 'Obtain explicit screen/input authorization before running this visible check.' }
$visibleRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
$visiblePackage = Test-DangoPackage -PackagePath $PackagePath
$visiblePackageRoot = (Resolve-Path -LiteralPath $PackagePath).Path
& git -C $visibleRepo diff --quiet $visiblePackage.Manifest.sourceCommit -- src eng/native Directory.Build.props Directory.Build.targets
if ($LASTEXITCODE -ne 0) { throw 'Product sources differ from the selected package.' }
$visibleEvidence = Join-Path $visibleRepo "artifacts/local-mode-closeout/visible-$RunName"
$visibleRoot = Join-Path $visibleRepo "artifacts/local-room-catalog/runs/visible-$RunName"
foreach ($visibleTarget in @($visibleEvidence, $visibleRoot)) {
    if (Test-Path -LiteralPath $visibleTarget) { throw 'Use a fresh RunName; existing data and evidence are preserved.' }
    for ($visibleCursor = $visibleTarget; $visibleCursor; $visibleCursor = Split-Path -Parent $visibleCursor) {
        if ((Test-Path -LiteralPath $visibleCursor) -and
            ((Get-Item -LiteralPath $visibleCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Verification paths cannot traverse links.'
        }
    }
}
$visibleProtected = Join-Path $visibleRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$visibleProtectedHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'
if ((Get-FileHash -LiteralPath $visibleProtected).Hash -ne $visibleProtectedHash) { throw 'Protected source DLL differs.' }
function Get-VisibleSources {
    $visibleHashes = [ordered]@{}
    foreach ($visibleFile in @(Get-ChildItem -LiteralPath (Join-Path $visibleRepo 'eng/tools/Dango.LocalRoomProbe') -File |
        Where-Object { $_.Extension -in @('.cs', '.csproj') } | Sort-Object Name)) {
        $visibleHashes[$visibleFile.Name] = (Get-FileHash -LiteralPath $visibleFile.FullName).Hash
    }
    $visibleHashes['runner'] = (Get-FileHash -LiteralPath $PSCommandPath).Hash
    return $visibleHashes
}
$visibleSources = Get-VisibleSources
$null = New-Item -ItemType Directory -Path $visibleEvidence
$visibleBuild = Join-Path $visibleEvidence 'probe'
$visibleReport = [ordered]@{
    baseHead = (& git -C $visibleRepo rev-parse HEAD).Trim()
    sourceHashes = $visibleSources
    packageSource = $visiblePackage.Manifest.sourceCommit
    packageSourceDirty = $visiblePackage.Manifest.sourceDirty
    mode = $Mode
    binaryHashes = [ordered]@{}
    steps = @()
    passed = $false
    processesReleased = $false
    dataRetained = $true
    scope = 'packaged product host and real OS input on the current Windows display; no performance or environment matrix acceptance'
}
$visibleChildren = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
try {
    & dotnet build (Join-Path $visibleRepo 'eng/tools/Dango.LocalRoomProbe/Dango.LocalRoomProbe.csproj') -c Release `
        -o $visibleBuild "-p:DangoProductPackage=$visiblePackageRoot" -v minimal *> (Join-Path $visibleEvidence 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Visible probe build failed; see build.log.' }
    $visibleNames = @('Dango.Desktop.dll', 'Dango.Core.dll', 'Dango.Physics.dll', 'Dango.Protocol.dll',
        'dango_belt_product.dll', 'dango_belt_room.dll', 'belt-native-manifest.json')
    foreach ($visibleName in $visibleNames) {
        $visibleHash = (Get-FileHash -LiteralPath (Join-Path $visibleBuild $visibleName)).Hash
        if ($visibleHash -ne (Get-FileHash -LiteralPath (Join-Path $visiblePackageRoot $visibleName)).Hash) {
            throw "Probe does not use the exact packaged binary: $visibleName"
        }
        $visibleReport.binaryHashes[$visibleName] = $visibleHash
    }
    $visibleExe = Join-Path $visibleBuild 'Dango.LocalRoomProbe.exe'
    $visibleModes = @('init', $(if ($Mode -eq 'room') { 'visible-observe' } else { 'desktop-mouse' }))
    foreach ($visibleMode in $visibleModes) {
        $visibleInfo = [System.Diagnostics.ProcessStartInfo]::new($visibleExe)
        $visibleInfo.UseShellExecute = $false
        $visibleInfo.CreateNoWindow = $true
        $visibleInfo.RedirectStandardOutput = $true
        $visibleInfo.RedirectStandardError = $true
        $visibleInfo.Environment['DANGO_LOCAL_SCREEN_AUTHORIZED'] = '1'
        $visibleInfo.ArgumentList.Add($visibleMode)
        $visibleInfo.ArgumentList.Add($visibleRoot)
        if ($visibleMode -ne 'init') { $visibleInfo.ArgumentList.Add((Join-Path $visibleBuild 'dango_belt_room.dll')) }
        if ($visibleMode -eq 'visible-observe') { $visibleInfo.ArgumentList.Add($ActiveSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }
        $visibleChild = [System.Diagnostics.Process]::Start($visibleInfo)
        if (!$visibleChild) { throw 'Cannot start owned visible probe.' }
        $visibleChildren.Add($visibleChild)
        $visibleOut = $visibleChild.StandardOutput.ReadToEndAsync()
        $visibleErr = $visibleChild.StandardError.ReadToEndAsync()
        $visibleStarted = [DateTimeOffset]::Now
        Write-Output "Mode=$visibleMode PID=$($visibleChild.Id) started=$($visibleStarted.ToString('o'))"
        Write-Output "Owned data and progress: $visibleRoot"
        $visibleDeadline = $visibleStarted.AddSeconds($(if ($visibleMode -eq 'visible-observe') { $ActiveSeconds + 180 } else { 90 }))
        while (!$visibleChild.WaitForExit(1000)) {
            if ([DateTimeOffset]::Now -gt $visibleDeadline) { throw 'Owned visible probe exceeded its wall-clock deadline.' }
        }
        $visibleOut.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $visibleEvidence "$visibleMode.out.log") -Encoding utf8
        $visibleErr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $visibleEvidence "$visibleMode.err.log") -Encoding utf8
        $visibleReport.steps += [ordered]@{ mode = $visibleMode; pid = $visibleChild.Id; started = $visibleStarted.ToString('o');
            ended = [DateTimeOffset]::Now.ToString('o'); exitCode = $visibleChild.ExitCode }
        if ($visibleChild.ExitCode -ne 0) { throw "Visible mode failed: $visibleMode; inspect its error log and retained data." }
    }
    $visibleResultName = if ($Mode -eq 'room') { 'visible-result.json' } else { 'desktop-mouse-result.json' }
    $visibleResult = Get-Content -LiteralPath (Join-Path $visibleRoot $visibleResultName) -Raw | ConvertFrom-Json
    if (!$visibleResult.passed) { throw 'Visible probe result did not pass.' }
    if (($visibleSources | ConvertTo-Json -Compress) -cne ((Get-VisibleSources) | ConvertTo-Json -Compress)) {
        throw 'Verification sources changed during the run.'
    }
    $visibleReport.passed = $true
} catch {
    $visibleReport['failure'] = $_.Exception.Message
    throw
} finally {
    foreach ($visibleChild in $visibleChildren) {
        if (!$visibleChild.HasExited) {
            # Let the owning UI thread release only its injected input and dispose
            # before terminating. Never kill a process while its injected Down may persist.
            if (Test-Path -LiteralPath $visibleRoot) {
                Set-Content -LiteralPath (Join-Path $visibleRoot 'cancel-screen-requested') -Value 'owned runner stopping' -Encoding utf8
            }
            if (!$visibleChild.WaitForExit(5000)) {
                if ($visibleChild.MainModule.FileName -ne $visibleExe) { throw 'Owned probe path changed; process preserved.' }
                if (!('DangoVisibleCleanup' -as [type])) {
                    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DangoVisibleCleanup {
    public delegate bool Visit(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Visit callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
}
'@
                }
                $visibleCleanupPid = $visibleChild.Id
                $visibleOwnedWindows = [Collections.Generic.List[IntPtr]]::new()
                [DangoVisibleCleanup]::EnumWindows({ param($window, $parameter)
                    [uint32]$visibleWindowPid = 0
                    $null = [DangoVisibleCleanup]::GetWindowThreadProcessId($window, [ref]$visibleWindowPid)
                    if ($visibleWindowPid -eq $visibleCleanupPid) { $visibleOwnedWindows.Add($window) }
                    return $true
                }, [IntPtr]::Zero) | Out-Null
                foreach ($visibleOwnedWindow in $visibleOwnedWindows) { $null = [DangoVisibleCleanup]::ShowWindow($visibleOwnedWindow, 0) }
                Start-Sleep -Milliseconds 700
                $visibleControlsUp = @(@(0x41, 0x44, 0x20, 0x01) | Where-Object {
                    ([DangoVisibleCleanup]::GetAsyncKeyState($_) -band 0x8000) -ne 0
                }).Count -eq 0
                if (!$visibleControlsUp) {
                    $visibleReport['cleanupFailure'] = 'Owned probe did not cooperate; controls remain down. Hidden process preserved; no unowned input released.'
                } elseif (!$visibleChild.HasExited) {
                    $visibleChild.Kill($true)
                    $visibleChild.WaitForExit(5000) | Out-Null
                    $visibleReport['forcedExitAfterControlsUp'] = $true
                }
            }
        }
    }
    $visibleReport.processesReleased = @($visibleChildren | Where-Object { !$_.HasExited }).Count -eq 0
    $visibleReport['protectedDllUnchanged'] = (Get-FileHash -LiteralPath $visibleProtected).Hash -eq $visibleProtectedHash
    $visibleReport.passed = $visibleReport.passed -and $visibleReport.processesReleased -and $visibleReport.protectedDllUnchanged
    $visibleReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $visibleEvidence 'summary.json') -Encoding utf8
    Write-Output (Join-Path $visibleEvidence 'summary.json')
    if (!$visibleReport.processesReleased -or !$visibleReport.protectedDllUnchanged) { throw 'Visible verification cleanup or protected DLL check failed.' }
}

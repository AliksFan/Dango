[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$AcceptanceSettingsRoot,
    [Parameter(Mandatory)][string]$EvidenceRoot
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Use Windows PowerShell 5.1 for this Windows UIAutomation probe.' }
Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class DangoWindowProbe {
    public delegate bool Visitor(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visitor visit, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    public static IntPtr[] Visible(int pid) {
        var result = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused) {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner == pid && IsWindowVisible(hwnd)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
    public static Rect PhysicalRect(IntPtr hwnd) {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { Rect rect; if (!GetWindowRect(hwnd, out rect)) throw new InvalidOperationException("Cannot measure owned window."); return rect; }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
    public static Rect PhysicalWorkArea(IntPtr hwnd) {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            var info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) throw new InvalidOperationException("Cannot read candidate monitor work area.");
            return info.Work;
        } finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
}
'@
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
if (-not $evidence.StartsWith((Join-Path $repo 'artifacts') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $evidence)) { throw 'Evidence must be a new directory inside this checkout artifacts.' }
$installModule = Get-Module Dango.LocalInstall
& $installModule { param($path) Assert-DangoNoReparse $path } $evidence
& $installModule { param($root) Assert-DangoProductStopped; Assert-DangoOwnedRoot (Get-DangoFullPath $root) } $InstallRoot
$state = Get-DangoInstallState $InstallRoot
$packageRoot = Join-Path $InstallRoot ('versions/' + $state.current)
$package = Test-DangoPackage $packageRoot
if ($package.Manifest.settingsContract -cne 'dango-desktop-settings-v4-preset-sidecar-v1') { throw 'A sidecar-compatible candidate is required.' }
& $installModule { param($root, $package) $null = Assert-DangoAcceptanceSettingsRoot $root $package } $AcceptanceSettingsRoot $packageRoot
foreach ($name in @('Dango.LocalInstall.psm1','Manage-Dango.ps1')) {
    $sourceTool = Join-Path $PSScriptRoot ('local-install/' + $name)
    $installedTool = Join-Path $InstallRoot ('tools/' + $name)
    & $installModule { param($path) Assert-DangoNoReparse $path } $installedTool
    if ((Get-FileHash -LiteralPath $sourceTool -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $installedTool -Algorithm SHA256).Hash) { throw 'Installed management tools differ from the reviewed source.' }
}
Remove-Module Dango.LocalInstall
Import-Module (Join-Path $InstallRoot 'tools/Dango.LocalInstall.psm1') -Force
$installModule = Get-Module Dango.LocalInstall
$currentSession = (Get-Process -Id $PID).SessionId
$null = New-Item -ItemType Directory -Path $evidence
$results = New-Object Collections.ArrayList
$ownedPid = $null
function Record([string]$Name, $Detail) { $null = $results.Add([ordered]@{ name=$Name; passed=$true; detail=$Detail }); Write-Output "PASS $Name" }
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Find-Control([string]$Id) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $element = $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Candidate control unavailable: $Id" }
    return $element
}
function Invoke-Control([string]$Id) {
    $element = Find-Control $Id
    Require $element.Current.IsEnabled "Candidate control disabled: $Id"
    ([Windows.Automation.InvokePattern]$element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 350
}
function Wait-Home {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $handles = @([DangoWindowProbe]::Visible($script:ownedPid))
        foreach ($handle in $handles) {
            $candidate = [Windows.Automation.AutomationElement]::FromHandle($handle)
            $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'FaceButton')
            if ($null -ne $candidate.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)) {
                $script:window = $candidate; $script:handle = $handle; return
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The actual product home did not appear.'
}
function Reject([scriptblock]$Action) {
    try { $null = & $Action } catch { return }
    throw 'An unsafe running-product operation was accepted.'
}
try {
    # The shortcut is an owned acceptance entry, never the user's desktop entry.
    $shortcutPath = Join-Path $evidence 'Dango-Acceptance.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $InstallRoot 'tools/Manage-Dango.ps1') +
        '" -Action Start -AcceptanceSettingsRoot "' + $AcceptanceSettingsRoot + '"'
    $shortcut.WorkingDirectory = $InstallRoot
    $shortcut.Save()
    $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    Start-Process -FilePath $shortcutPath -WindowStyle Hidden | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $processes = @(Get-CimInstance Win32_Process -Filter "Name='Dango.Desktop.exe'" | Where-Object {
            $_.SessionId -eq $currentSession -and $_.ExecutablePath -ieq (Join-Path $packageRoot 'Dango.Desktop.exe') })
        if ($processes.Count -eq 1) {
            $scope = & $installModule { param($line) Get-DangoProcessSettingsScope $line } $processes[0].CommandLine
            Require ($scope -ieq [IO.Path]::GetFullPath($AcceptanceSettingsRoot).TrimEnd('\')) 'Started process has a different acceptance scope.'
            $ownedPid = [int]$processes[0].ProcessId; break
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    Require ($null -ne $ownedPid) 'Acceptance shortcut did not start the selected package.'
    Wait-Home
    Start-Sleep -Milliseconds 1200
    $module = @((Get-Process -Id $ownedPid).Modules | Where-Object ModuleName -ceq 'dango_physics_module.dll')
    Require ($module.Count -eq 1 -and $module[0].FileName -ieq (Join-Path $packageRoot 'dango_physics_module.dll')) 'Product did not load its exact packaged module.'
    Record 'actual-shortcut-cold-start-and-packaged-native-load' @{ processId=$ownedPid; version=$state.current }
    Require (@([DangoWindowProbe]::Visible($ownedPid)).Count -eq 1) 'A desktop scene appeared before room entry.'
    Invoke-Control 'FaceButton'
    Require ((Find-Control 'AppearancePreviewNotice').Current.Name -ne '') 'Preview persistence warning is absent.'
    Require (-not (Find-Control 'ApplyButton').Current.IsEnabled) 'Preview falsely exposes saving.'
    Invoke-Control 'CloseEditorButton'
    Invoke-Control 'WardrobeButton'
    Require (-not (Find-Control 'CreateButton').Current.IsEnabled) 'Unimplemented creation is actionable.'
    Invoke-Control 'CloseEditorButton'
    Record 'real-home-editors-and-preview-boundary' $true
    Invoke-Control 'SettingsButton'
    $readyName = -join [char[]]@(0x573A,0x666F,0x5DF2,0x5C31,0x7EEA)
    Require ((Find-Control 'SettingRunState').Current.Name -ceq $readyName) 'Product native scene is not Ready.'
    Require (-not (Find-Control 'DisplayToggleButton').Current.IsEnabled) 'Room-gated desktop visibility became available.'
    Record 'native-ready-and-desktop-room-gate' $true
    $sizes = @()
    foreach ($id in @('Landscape1080pSettingsButton','Landscape1440pSettingsButton','PortraitSettingsButton')) {
        Invoke-Control $id
        $dpi = [DangoWindowProbe]::GetDpiForWindow($handle)
        $work = [DangoWindowProbe]::PhysicalWorkArea($handle)
        $scale = $dpi / 96.0
        $target = switch ($id) {
            'Landscape1080pSettingsButton' { @(960,540) }
            'Landscape1440pSettingsButton' { @(1280,720) }
            'PortraitSettingsButton' { @([Math]::Max(480,360*$scale),[Math]::Max(800,640*$scale)) }
        }
        $expectedWidth = [Math]::Min($target[0],$work.Right-$work.Left-12)
        $expectedHeight = [Math]::Min($target[1],$work.Bottom-$work.Top-12)
        if ($id -eq 'PortraitSettingsButton') { $expectedHeight = [Math]::Min($expectedHeight,($work.Bottom-$work.Top)*0.9) }
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $rect = [DangoWindowProbe]::PhysicalRect($handle)
            $converged = [Math]::Abs(($rect.Right-$rect.Left)-$expectedWidth) -le 2 -and
                [Math]::Abs(($rect.Bottom-$rect.Top)-$expectedHeight) -le 2
            if ($converged) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        $sizes += @{ control=$id; width=$rect.Right-$rect.Left; height=$rect.Bottom-$rect.Top; dpi=$dpi;
            expectedWidth=$expectedWidth; expectedHeight=$expectedHeight }
        Require $converged 'Preset does not match its physical-pixel/DPI/work-area contract.'
    }
    Record 'three-actual-physical-window-sizes-measured-not-manual-acceptance' $sizes
    Invoke-Control 'Landscape1080pSettingsButton'
    $duplicate = Start-DangoInstalled $InstallRoot -AcceptanceSettingsRoot $AcceptanceSettingsRoot
    Require ($duplicate.ActivatedExisting -and $duplicate.ProcessId -eq $ownedPid) 'Duplicate launch created or activated the wrong process.'
    Reject { Start-DangoInstalled $InstallRoot }
    Reject { Set-DangoActiveVersion $InstallRoot $state.previous }
    Reject { Undo-DangoUpdate $InstallRoot }
    Record 'same-instance-cross-scope-and-running-update-refusals' $true
    Wait-Home
    Invoke-Control 'CloseWindowButton'
    Require (@([DangoWindowProbe]::Visible($ownedPid)).Count -eq 0) 'Close did not hide the candidate window.'
    Require ($null -ne (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) 'Close unexpectedly exited the product.'
    $reopen = Start-DangoInstalled $InstallRoot -AcceptanceSettingsRoot $AcceptanceSettingsRoot
    Require ($reopen.ProcessId -eq $ownedPid -and $reopen.ActivatedExisting) 'Reopen lost the original product instance.'
    Wait-Home
    Record 'close-to-tray-lifetime-and-manager-reopen' $true
    Invoke-Control 'SettingsButton'
    Invoke-Control 'SettingsExitButton'
    $dialog = $null
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, '6')
    foreach ($dialogHandle in [DangoWindowProbe]::Visible($ownedPid)) {
        $candidate = [Windows.Automation.AutomationElement]::FromHandle($dialogHandle)
        $exitTitle = (-join [char[]]@(0x9000,0x51FA)) + ' Dango'
        if ($candidate.Current.ClassName -cne '#32770' -or $candidate.Current.Name -cne $exitTitle) { continue }
        $yes = $candidate.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $yes) { $dialog = $yes; break }
    }
    Require ($null -ne $dialog) 'Graceful exit confirmation did not appear.'
    ([Windows.Automation.InvokePattern]$dialog.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Require ($null -eq (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) 'Graceful exit left the owned product process running.'
    Record 'graceful-exit-without-owned-process-residue' $true
}
catch { $null = $results.Add([ordered]@{ name='candidate-window-probe'; passed=$false; detail=$_.Exception.Message }); throw }
finally {
    $residue = if ($null -ne $ownedPid) { $null -ne (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) } else { $null }
    $summary = [ordered]@{ passed=@($results | Where-Object passed).Count; failed=@($results | Where-Object { -not $_.passed }).Count;
        processId=$ownedPid; ownedProcessStillRunning=$residue; results=@($results);
        manualAcceptance='pending'; keyboardAndTrayMenuInteraction='not-verified'; secondScale='not-verified' }
    [IO.File]::WriteAllText((Join-Path $evidence 'verification.json'), ($summary | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    # Never kill a process on failure. Preserve the owned PID and all evidence for recovery.
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PhysicsModulePath,

    [string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'

if (-not $EvidenceRoot) {
    $EvidenceRoot = Join-Path $PSScriptRoot ('..\\artifacts\\local-install-tests\\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) { throw "Evidence root already exists: $EvidenceRoot" }
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

$modulePath = Join-Path $PSScriptRoot 'local-install\\Dango.LocalInstall.psm1'
$results = New-Object System.Collections.ArrayList

function Add-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    [void]$results.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if ($Passed) { Write-Host "PASS  $Name" } else { Write-Host "FAIL  $Name -- $Detail" }
}

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; Add-Result $Name $true '' }
    catch { Add-Result $Name $false $_.Exception.Message }
}

function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Equal($Expected, $Actual, [string]$Message) { if ($Expected -cne $Actual) { throw "$Message Expected=[$Expected] Actual=[$Actual]" } }
function Assert-Throws([scriptblock]$Body, [string]$Message) { try { & $Body } catch { return }; throw $Message }
function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read-StateBytes([string]$Root) { [IO.File]::ReadAllBytes((Join-Path $Root 'install-state.json')) }
function Assert-BytesEqual([byte[]]$Expected, [byte[]]$Actual, [string]$Message) {
    Assert-True ($Expected.Length -eq $Actual.Length) $Message
    for ($i = 0; $i -lt $Expected.Length; $i++) { if ($Expected[$i] -ne $Actual[$i]) { throw $Message } }
}

function New-TestPackage([string]$Name, [string]$Commit, [hashtable]$Options) {
    $root = Join-Path $EvidenceRoot ("package-" + $Name)
    New-Item -ItemType Directory -Path (Join-Path $root 'licenses') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Dango.Desktop.exe'), "fixture executable $Name", [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $PhysicsModulePath -Destination (Join-Path $root 'dango_physics_module.dll')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\\src\\Dango.Desktop\\runtimes\\win-x64\\native\\dango_box2d.dll') -Destination (Join-Path $root 'dango_box2d.dll')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\\box2d\\LICENSE.box2d.txt') -Destination (Join-Path $root 'licenses\\Box2D-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\\box2d\\dependency.json') -Destination (Join-Path $root 'licenses\\Box2D-dependency.json')
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).TrimStart('\\').Replace('\', '/')
        [ordered]@{ path = $rel; size = $_.Length; sha256 = Get-Hash $_.FullName }
    })
    if ($Options.ContainsKey('traversal')) { $files[0].path = '../outside.txt' }
    $manifest = [ordered]@{
        packageFormat = 2; packageType = 'internal-candidate'; runtime = 'win-x64'; selfContained = $true
        sourceCommit = $Commit; sourceDirty = $false; settingsContract = $(if ($Options.ContainsKey('contract')) { $Options.contract } else { 'dango-desktop-settings-v4' })
        settingsSourceHashes = $(Get-DangoSettingsSourceContract -RepositoryRoot (Join-Path $PSScriptRoot '..'))
        files = $files
    }
    [IO.File]::WriteAllText((Join-Path $root 'package-manifest.json'), (($manifest | ConvertTo-Json -Depth 6) + "`n"), [Text.UTF8Encoding]::new($false))
    if ($Options.ContainsKey('extra')) { [IO.File]::WriteAllText((Join-Path $root 'unexpected.txt'), 'unexpected') }
    if ($Options.ContainsKey('missing')) { Remove-Item -LiteralPath (Join-Path $root $Options.missing) -Force }
    return $root
}

if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
    Add-Result 'module-present' $false "Missing module: $modulePath"
    $summary = [ordered]@{ evidenceRoot = $EvidenceRoot; passed = 0; failed = 1; results = @($results) }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'summary.json') -Encoding UTF8
    Write-Host "Evidence: $EvidenceRoot"
    exit 1
}
if (-not (Test-Path -LiteralPath $PhysicsModulePath -PathType Leaf)) { throw "Physics module does not exist: $PhysicsModulePath" }

Import-Module $modulePath -Force
$commitA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$commitB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$packageA = New-TestPackage 'A' $commitA @{}
$packageB = New-TestPackage 'B' $commitB @{}
$installRoot = Join-Path $EvidenceRoot 'install'

Invoke-Case 'package-test-returns-derived-version' {
    $tested = Test-DangoPackage -PackagePath $packageA
    Assert-True ($tested.VersionId -match '^a{12}-[0-9a-f]{12}$') 'VersionId is not derived in the required form.'
}
Invoke-Case 'settings-source-contract-rejects-one-byte-change' {
    $repo = Join-Path $EvidenceRoot 'fixture-settings-source-repo'
    $sourceRoot = Join-Path $PSScriptRoot '..\\src\\Dango.Desktop'
    $contract = Get-DangoSettingsSourceContract -RepositoryRoot (Join-Path $PSScriptRoot '..')
    foreach ($relative in $contract.Keys) {
        $target = Join-Path $repo ('src\\Dango.Desktop\\' + $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $target
    }
    $copy = Get-DangoSettingsSourceContract -RepositoryRoot $repo
    Assert-Equal $contract['App.xaml.cs'] $copy['App.xaml.cs'] 'Copied settings source contract differs.'
    [IO.File]::AppendAllText((Join-Path $repo 'src\\Dango.Desktop\\App.xaml.cs'), ' ')
    Assert-Throws { Get-DangoSettingsSourceContract -RepositoryRoot $repo } 'A one-byte settings source change was accepted.'
}
Invoke-Case 'settings-source-hash-metadata-rejected' {
    $bad = New-TestPackage 'settings-source-hash' $commitA @{}
    $manifestPath = Join-Path $bad 'package-manifest.json'; $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    $manifest.settingsSourceHashes.'App.xaml.cs' = ('0' * 64)
    [IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 7) + "`n"), [Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-DangoPackage -PackagePath $bad } 'Wrong settings source hash metadata was accepted.'
}

# The production stop guard is deliberately bypassed only inside this module scope for fixture-only state tests.
$installModule = Get-Module Dango.LocalInstall
if ($null -eq $installModule) { throw 'Imported local install module has an unexpected name.' }
& $installModule { Set-Item -Path Function:Assert-DangoProductStopped -Value { } -Force }

Invoke-Case 'stage-only-initializes-null-state-without-activation' {
    $staged = Install-DangoPackage -PackagePath $packageA -InstallRoot $installRoot -StageOnly
    $state = Get-DangoInstallState -InstallRoot $installRoot
    Assert-Equal $staged.VersionId (Test-DangoPackage $packageA).VersionId 'Stage VersionId changed.'
    Assert-True ($null -eq $state.current -and $null -eq $state.previous) 'StageOnly activated a version.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot "versions\\$($staged.VersionId)\\Dango.Desktop.exe")) 'Staged files are absent.'
}
Invoke-Case 'install-a-then-activate' {
    $a = Install-DangoPackage -PackagePath $packageA -InstallRoot $installRoot
    Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $a.VersionId
    $state = Get-DangoInstallState -InstallRoot $installRoot
    Assert-Equal $a.VersionId $state.current 'A is not active.'
    Assert-True ($null -eq $state.previous) 'First activation has previous.'
}
Invoke-Case 'reinstall-active-package-keeps-previous' {
    $before = Get-DangoInstallState -InstallRoot $installRoot
    $a = Install-DangoPackage -PackagePath $packageA -InstallRoot $installRoot
    Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $a.VersionId
    $after = Get-DangoInstallState -InstallRoot $installRoot
    Assert-Equal $before.previous $after.previous 'Repeated install changed previous.'
}
Invoke-Case 'update-b-and-undo-to-a' {
    $b = Install-DangoPackage -PackagePath $packageB -InstallRoot $installRoot
    Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $b.VersionId
    $updated = Get-DangoInstallState -InstallRoot $installRoot
    Assert-Equal $b.VersionId $updated.current 'B is not active.'
    Assert-Equal (Test-DangoPackage $packageA).VersionId $updated.previous 'A was not previous.'
    Undo-DangoUpdate -InstallRoot $installRoot
    $undone = Get-DangoInstallState -InstallRoot $installRoot
    Assert-Equal (Test-DangoPackage $packageA).VersionId $undone.current 'Undo did not restore A.'
}
Invoke-Case 'fixture-settings-remain-untouched' {
    $savedLocalAppData = $env:LOCALAPPDATA; $fixtureLocalAppData = Join-Path $EvidenceRoot 'fixture-localappdata'; $env:LOCALAPPDATA = $fixtureLocalAppData
    try {
        $settings = Join-Path $fixtureLocalAppData 'Dango\\settings.json'; New-Item -ItemType Directory -Path (Split-Path -Parent $settings) -Force | Out-Null
        [IO.File]::WriteAllText($settings, '{"schemaVersion":4,"width":1000,"height":700,"hatPresetId":"bowler","interactionEnabled":true,"introSeen":true}', [Text.UTF8Encoding]::new($false))
        $settingsInstall = Join-Path $EvidenceRoot 'settings-install'
        $a = Install-DangoPackage -PackagePath $packageA -InstallRoot $settingsInstall; Set-DangoActiveVersion -InstallRoot $settingsInstall -VersionId $a.VersionId
        $b = Install-DangoPackage -PackagePath $packageB -InstallRoot $settingsInstall; Set-DangoActiveVersion -InstallRoot $settingsInstall -VersionId $b.VersionId
        # This is a fixture-only stand-in for the user's ordinary settings save; no application is started.
        [IO.File]::WriteAllText($settings, '{"schemaVersion":4,"width":1000,"height":700,"hatPresetId":"sprout","interactionEnabled":false,"introSeen":true}', [Text.UTF8Encoding]::new($false))
        $afterSave = [IO.File]::ReadAllBytes($settings)
        Undo-DangoUpdate -InstallRoot $settingsInstall
        Assert-BytesEqual $afterSave ([IO.File]::ReadAllBytes($settings)) 'Rollback rewrote the latest fixture settings bytes.'
    }
    finally { $env:LOCALAPPDATA = $savedLocalAppData }
}
Invoke-Case 'missing-file-rejected-with-state-preserved' {
    $bad = New-TestPackage 'missing' $commitA @{ missing = 'Dango.Desktop.exe' }; $before = Read-StateBytes $installRoot
    Assert-Throws { Install-DangoPackage -PackagePath $bad -InstallRoot $installRoot } 'Missing file package was accepted.'; Assert-BytesEqual $before (Read-StateBytes $installRoot) 'State changed after rejected package.'
}
Invoke-Case 'tampered-file-rejected-with-state-preserved' {
    $bad = New-TestPackage 'tamper' $commitA @{}; [IO.File]::AppendAllText((Join-Path $bad 'Dango.Desktop.exe'), 'tamper'); $before = Read-StateBytes $installRoot
    Assert-Throws { Install-DangoPackage -PackagePath $bad -InstallRoot $installRoot } 'Tampered package was accepted.'; Assert-BytesEqual $before (Read-StateBytes $installRoot) 'State changed after tamper.'
}
Invoke-Case 'extra-file-rejected' { $bad = New-TestPackage 'extra' $commitA @{ extra = $true }; Assert-Throws { Test-DangoPackage -PackagePath $bad } 'Extra file package was accepted.' }
Invoke-Case 'path-traversal-rejected' { $bad = New-TestPackage 'traversal' $commitA @{ traversal = $true }; Assert-Throws { Test-DangoPackage -PackagePath $bad } 'Traversal manifest was accepted.' }
Invoke-Case 'settings-contract-rejected' { $bad = New-TestPackage 'contract' $commitA @{ contract = 'other' }; Assert-Throws { Test-DangoPackage -PackagePath $bad } 'Incompatible contract was accepted.' }
Invoke-Case 'undo-without-previous-rejected' {
    $root = Join-Path $EvidenceRoot 'no-previous'; $a = Install-DangoPackage -PackagePath $packageA -InstallRoot $root; Set-DangoActiveVersion -InstallRoot $root -VersionId $a.VersionId
    Assert-Throws { Undo-DangoUpdate -InstallRoot $root } 'Undo without previous was accepted.'
}
Invoke-Case 'bad-previous-package-rejected' {
    $root = Join-Path $EvidenceRoot 'bad-previous'; $a = Install-DangoPackage -PackagePath $packageA -InstallRoot $root; Set-DangoActiveVersion -InstallRoot $root -VersionId $a.VersionId
    $b = Install-DangoPackage -PackagePath $packageB -InstallRoot $root; Set-DangoActiveVersion -InstallRoot $root -VersionId $b.VersionId
    [IO.File]::AppendAllText((Join-Path $root "versions\\$($a.VersionId)\\Dango.Desktop.exe"), 'bad'); $before = Read-StateBytes $root
    Assert-Throws { Undo-DangoUpdate -InstallRoot $root } 'Undo accepted a corrupted previous version.'; Assert-BytesEqual $before (Read-StateBytes $root) 'State changed after failed undo.'
}
Invoke-Case 'locked-state-file-preserves-existing-state' {
    $before = Read-StateBytes $installRoot
    $lock = [IO.File]::Open((Join-Path $installRoot 'install-state.json'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $b = (Test-DangoPackage $packageB).VersionId
        Assert-Throws { Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $b } 'Locked state file did not reject activation.'
    }
    finally { $lock.Dispose() }
    Assert-BytesEqual $before (Read-StateBytes $installRoot) 'State changed after a state write failure.'
}
Invoke-Case 'installer-rollback-shortcut-conflict-preserves-state-and-desktop-shortcut' {
    $desktopShortcut = Join-Path $EvidenceRoot 'installer-desktop.lnk'; New-DangoShortcut -InstallRoot $installRoot -ShortcutPath $desktopShortcut
    $rollbackShortcut = Join-Path $installRoot 'Rollback.lnk'; $shell = New-Object -ComObject WScript.Shell; $rollback = $shell.CreateShortcut($rollbackShortcut); $rollback.TargetPath = "$env:WINDIR\\System32\\notepad.exe"; $rollback.Save()
    $stateBefore = Read-StateBytes $installRoot; $desktopBefore = [IO.File]::ReadAllBytes($desktopShortcut)
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'install-desktop-entry.ps1') -PackagePath $packageB -InstallRoot $installRoot -ShortcutPath $desktopShortcut
    Assert-True ($LASTEXITCODE -ne 0) 'Installer accepted a conflicting rollback shortcut.'
    Assert-BytesEqual $stateBefore (Read-StateBytes $installRoot) 'State changed despite rollback shortcut conflict.'
    Assert-BytesEqual $desktopBefore ([IO.File]::ReadAllBytes($desktopShortcut)) 'Desktop shortcut changed despite rollback shortcut conflict.'
}
Invoke-Case 'shortcut-conflict-is-not-overwritten' {
    $shortcut = Join-Path $EvidenceRoot 'existing.lnk'; $shell = New-Object -ComObject WScript.Shell; $existing = $shell.CreateShortcut($shortcut); $existing.TargetPath = "$env:WINDIR\\System32\\notepad.exe"; $existing.Save()
    $before = ($shell.CreateShortcut($shortcut)).TargetPath
    Assert-Throws { New-DangoShortcut -InstallRoot $installRoot -ShortcutPath $shortcut } 'Existing shortcut was overwritten.'
    Assert-Equal $before (($shell.CreateShortcut($shortcut)).TargetPath) 'Existing shell link target changed.'
}
Invoke-Case 'shortcut-created-is-a-real-shell-link' {
    $shortcut = Join-Path $EvidenceRoot 'dango.lnk'; New-DangoShortcut -InstallRoot $installRoot -ShortcutPath $shortcut
    $shell = New-Object -ComObject WScript.Shell; $link = $shell.CreateShortcut($shortcut)
    Assert-True ($link.TargetPath -like '*powershell.exe' -and $link.Arguments -like '*tools\Manage-Dango.ps1*') 'Shortcut does not invoke the installed management wrapper.'
}
Invoke-Case 'start-with-ordinary-mutex-rejects-unverifiable-owner-without-state-change' {
    $before = Read-StateBytes $installRoot; $processesBefore = @(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    try { Assert-Throws { Start-DangoInstalled -InstallRoot $installRoot } 'Start accepted an ordinary mutex without a matching product process.' }
    finally { $mutex.Dispose() }
    Assert-BytesEqual $before (Read-StateBytes $installRoot) 'Start changed state while rejecting an unverifiable owner.'
    Assert-Equal ($processesBefore -join ',') (@(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -join ',') 'Start created a Dango process while rejecting an unverifiable owner.'
}
Invoke-Case 'start-rejects-state-pointing-to-missing-version' {
    $root = Join-Path $EvidenceRoot 'missing-start-version'; Install-DangoPackage -PackagePath $packageA -InstallRoot $root -StageOnly | Out-Null
    $statePath = Join-Path $root 'install-state.json'
    [IO.File]::WriteAllText($statePath, '{"stateFormat":1,"settingsContract":"dango-desktop-settings-v4","current":"cccccccccccc-dddddddddddd","previous":null}', [Text.UTF8Encoding]::new($false))
    $before = Read-StateBytes $root; $processesBefore = @(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    Assert-Throws { Start-DangoInstalled -InstallRoot $root } 'Start accepted a state that points to a missing version.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Start changed state when the current version was missing.'
    Assert-Equal ($processesBefore -join ',') (@(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -join ',') 'Start created a Dango process for a missing version.'
}
Invoke-Case 'running-product-rejects-activation-with-state-preserved' {
    # Restore the actual guard, then use the documented product mutex only; no process is stopped.
    Remove-Module Dango.LocalInstall -Force; Import-Module $modulePath -Force
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    try { $before = Read-StateBytes $installRoot; $b = (Test-DangoPackage $packageB).VersionId; Assert-Throws { Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $b } 'Running product did not block activation.'; Assert-BytesEqual $before (Read-StateBytes $installRoot) 'State changed while product guard rejected activation.' }
    finally { $mutex.Dispose() }
}

$passed = @($results | Where-Object { $_.passed }).Count
$failed = $results.Count - $passed
$summary = [ordered]@{ evidenceRoot = $EvidenceRoot; passed = $passed; failed = $failed; results = @($results) }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'summary.json') -Encoding UTF8
Write-Host "Evidence: $EvidenceRoot"
if ($failed -gt 0) { exit 1 }

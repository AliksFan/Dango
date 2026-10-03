[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PhysicsModulePath,

    [string]$EvidenceRoot,
    [string]$SettingsRepositoryRoot,
    [string]$LegacyToolsPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SettingsRepositoryRoot)) {
    $SettingsRepositoryRoot = Split-Path -Parent $PSScriptRoot
}

if (-not $EvidenceRoot) {
    $EvidenceRoot = Join-Path $PSScriptRoot ('..\\artifacts\\local-install-tests\\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) { throw "Evidence root already exists: $EvidenceRoot" }
New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null

$modulePath = Join-Path $PSScriptRoot 'local-install\\Dango.LocalInstall.psm1'
$results = New-Object System.Collections.ArrayList
$legacySettingsHashes = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '4F7A837A22888A076A25A0491670872902769A0A14D9BF6C09CAE44DAB260C84'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'B7524524B0217CAB84248C4C73A533FD36E105517D3B88DF981EC64DC7E60C47'
    'AppShell/DangoApplicationHost.cs' = 'D21472100CC2C9BE877BBDFBA60A8A8503477EAF0FBB69A8F7ACE117B19EC4B7'
    'App.xaml.cs' = '0314E2E847EEC15BAB7E69A191FAB1B260C199714022127C7B930D4C32E17822'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}

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
        settingsSourceHashes = $(if ($Options.ContainsKey('sourceHashes')) { $Options.sourceHashes } else { $legacySettingsHashes })
        files = $files
    }
    [IO.File]::WriteAllText((Join-Path $root 'package-manifest.json'), (($manifest | ConvertTo-Json -Depth 6) + "`n"), [Text.UTF8Encoding]::new($false))
    if ($Options.ContainsKey('extra')) { [IO.File]::WriteAllText((Join-Path $root 'unexpected.txt'), 'unexpected') }
    if ($Options.ContainsKey('missing')) { Remove-Item -LiteralPath (Join-Path $root $Options.missing) -Force }
    return $root
}

function New-LegacyInstallFixture([string]$Name) {
    if (-not $LegacyToolsPath) { throw 'Reviewed legacy tool fixtures were not prepared.' }
    $root = Join-Path $EvidenceRoot $Name
    $null = Install-DangoPackage -PackagePath $packageA -InstallRoot $root
    foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) {
        Copy-Item -LiteralPath (Join-Path $LegacyToolsPath $name) -Destination (Join-Path $root ('tools\' + $name)) -Force
    }
    New-DangoShortcut -InstallRoot $root -ShortcutPath (Join-Path $root 'Rollback.lnk') -Action Rollback
    return $root
}

function New-InterruptedMigrationFixture([string]$Name, [switch]$NewToolsActive) {
    $root = New-LegacyInstallFixture $Name
    $id = [Guid]::NewGuid().ToString('N')
    $backup = Join-Path $root ('.tools-backup-' + $id)
    $staging = Join-Path $root ('.tools-staging-' + $id)
    New-Item -ItemType Directory -Path $staging | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('local-install\' + $name)) -Destination (Join-Path $staging $name)
        $hashes[$name] = (Get-Hash (Join-Path $staging $name)).ToUpperInvariant()
    }
    $journal = [ordered]@{
        migrationFormat = 1; migrationId = $id; phase = 'prepared'
        stateSha256 = (Get-Hash (Join-Path $root 'install-state.json')).ToUpperInvariant()
        ownerSha256 = (Get-Hash (Join-Path $root 'install-owner.json')).ToUpperInvariant()
        newToolHashes = $hashes
    }
    [IO.File]::WriteAllText((Join-Path $root 'tools-migration.json'), ($journal | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    [IO.Directory]::Move((Join-Path $root 'tools'), $backup)
    if ($NewToolsActive) { [IO.Directory]::Move($staging, (Join-Path $root 'tools')) }
    return $root
}

function New-AcceptanceSettingsFixture([string]$Name) {
    $root = Join-Path $EvidenceRoot $Name
    New-Item -ItemType Directory -Path $root | Out-Null
    [IO.File]::WriteAllText((Join-Path $root '.dango-local-install-acceptance.json'), '{"application":"Dango","acceptanceFormat":1,"fictional":true}', [Text.UTF8Encoding]::new($false))
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
if (-not $LegacyToolsPath) {
    # Retrieve immutable reviewed source blobs; never copy tools from a daily installation.
    $LegacyToolsPath = Join-Path $EvidenceRoot 'fixture-reviewed-legacy-tools'
    New-Item -ItemType Directory -Path $LegacyToolsPath | Out-Null
    $fixtureRepository = Split-Path -Parent $PSScriptRoot
    foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = (Get-Command git -ErrorAction Stop).Source
        $info.Arguments = '-C "' + $fixtureRepository + '" show 0bdf5c1:eng/local-install/' + $name
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        $target = [IO.File]::Open((Join-Path $LegacyToolsPath $name), [IO.FileMode]::CreateNew)
        try { $process.StandardOutput.BaseStream.CopyTo($target) }
        finally { $target.Dispose() }
        $detail = $process.StandardError.ReadToEnd(); $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw ('Cannot read the reviewed legacy tool fixture. Provide LegacyToolsPath from that baseline: ' + $detail) }
        $process.Dispose()
    }
}
$commitA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$commitB = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$packageA = New-TestPackage 'A' $commitA @{}
$newSettingsContract = 'dango-desktop-settings-v4-preset-sidecar-v1'
$newSettingsHashes = Get-DangoSettingsCompatibilityContract $newSettingsContract
$packageB = New-TestPackage 'B' $commitB @{ contract = $newSettingsContract; sourceHashes = $newSettingsHashes }
$installRoot = Join-Path $EvidenceRoot 'install'

Invoke-Case 'package-test-returns-derived-version' {
    $tested = Test-DangoPackage -PackagePath $packageA
    Assert-True ($tested.VersionId -match '^a{12}-[0-9a-f]{12}$') 'VersionId is not derived in the required form.'
}
Invoke-Case 'legacy-settings-contract-is-frozen' {
    $contract = Get-DangoSettingsCompatibilityContract -SettingsContract 'dango-desktop-settings-v4'
    Assert-Equal 5 $contract.Count 'Legacy contract lost or gained a source.'
    foreach ($relative in $legacySettingsHashes.Keys) { Assert-Equal $legacySettingsHashes[$relative] $contract[$relative] 'Legacy source protection changed.' }
}
Invoke-Case 'explicit-management-tools-migration-entry-exists' {
    Assert-True (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'migrate-local-install-tools.ps1') -PathType Leaf) 'Explicit migration entry is absent.'
    Assert-True ($null -ne (Get-Command Update-DangoManagementTools -ErrorAction SilentlyContinue)) 'Module migration operation is absent.'
}
Invoke-Case 'settings-source-contract-rejects-one-byte-change' {
    $repo = Join-Path $EvidenceRoot 'fixture-settings-source-repo'
    $sourceRoot = Join-Path $SettingsRepositoryRoot 'src\\Dango.Desktop'
    $contract = Get-DangoSettingsSourceContract -RepositoryRoot $SettingsRepositoryRoot
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
Invoke-Case 'legacy-contract-cannot-borrow-another-contract-source-hash' {
    $bad = New-TestPackage 'legacy-source-relabel' $commitA @{}
    $manifestPath = Join-Path $bad 'package-manifest.json'; $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    $manifest.settingsSourceHashes.'App.xaml.cs' = ('F' * 64)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-DangoPackage $bad } 'Legacy source evidence was weakened by a compatibility table.'
}
Invoke-Case 'new-source-evidence-cannot-be-relabeled-as-legacy-or-vice-versa' {
    $newAsOld = New-TestPackage 'new-as-old' $commitB @{ sourceHashes = $newSettingsHashes }
    Assert-Throws { Test-DangoPackage $newAsOld } 'New source hashes were accepted under the legacy contract id.'
    $oldAsNew = New-TestPackage 'old-as-new' $commitA @{ contract = $newSettingsContract }
    Assert-Throws { Test-DangoPackage $oldAsNew } 'Legacy source hashes were accepted under the new contract id.'
    $checked = Test-DangoPackage $packageB
    Assert-Equal $newSettingsContract $checked.Manifest.settingsContract 'Valid new-contract B fixture was not recognized.'
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
Invoke-Case 'installed-management-status-works-without-install-root-parameter' {
    $root = Join-Path $EvidenceRoot 'manage-entry-install'
    $null = Install-DangoPackage -PackagePath $packageA -InstallRoot $root -StageOnly
    $manager = Join-Path $root 'tools\\Manage-Dango.ps1'
    $output = @(& powershell -NoProfile -NonInteractive -File $manager -Action Status 2>&1) | Out-String
    Assert-Equal 0 $LASTEXITCODE 'Installed management Status failed without InstallRoot.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($output)) 'Installed management Status returned no readable state output.'
}
Invoke-Case 'installed-management-start-rejects-ordinary-mutex-without-running-fixture' {
    $root = Join-Path $EvidenceRoot 'manage-entry-install'
    $a = Test-DangoPackage -PackagePath $packageA; Set-DangoActiveVersion -InstallRoot $root -VersionId $a.VersionId
    $manager = Join-Path $root 'tools\\Manage-Dango.ps1'; $stateBefore = Read-StateBytes $root
    $processesBefore = @(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    $savedPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $output = @(& powershell -NoProfile -NonInteractive -File $manager -Action Start 2>&1) | Out-String }
    finally { $ErrorActionPreference = $savedPreference; $mutex.Dispose() }
    Assert-True ($LASTEXITCODE -ne 0) 'Installed management Start accepted an ordinary mutex without a matching process.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($output)) 'Installed management Start supplied no rejection output.'
    Assert-BytesEqual $stateBefore (Read-StateBytes $root) 'Installed management Start changed state while rejecting the mutex owner.'
    Assert-Equal ($processesBefore -join ',') (@(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -join ',') 'Installed management Start launched the fixture executable.'
}
Invoke-Case 'shell-style-management-entry-loads-utility-modules-and-rejects-real-other-instance' {
    $root = Join-Path $EvidenceRoot 'manage-entry-install'
    $manager = Join-Path $root 'tools\\Manage-Dango.ps1'
    $modulePathBefore = $env:PSModulePath; $emptyModulePath = Join-Path $EvidenceRoot 'empty-psmodulepath'; New-Item -ItemType Directory -Path $emptyModulePath -Force | Out-Null
    $processesBefore = @(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $env:PSModulePath = $emptyModulePath
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    try {
        $statusOut = Join-Path $EvidenceRoot 'shell-status.stdout.txt'; $statusErr = Join-Path $EvidenceRoot 'shell-status.stderr.txt'
        $status = Start-Process -FilePath powershell -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $manager, '-Action', 'Status') -WindowStyle Hidden -RedirectStandardOutput $statusOut -RedirectStandardError $statusErr -Wait -PassThru
        Assert-Equal 0 $status.ExitCode 'Shell-style Status failed with an empty PSModulePath.'
        $statusJson = [IO.File]::ReadAllText($statusOut) | ConvertFrom-Json
        Assert-True ($statusJson.PSObject.Properties.Name -contains 'current') 'Shell-style Status did not return JSON state.'
        $startOut = Join-Path $EvidenceRoot 'shell-start.stdout.txt'; $startErr = Join-Path $EvidenceRoot 'shell-start.stderr.txt'
        $start = Start-Process -FilePath powershell -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $manager, '-Action', 'Start') -WindowStyle Hidden -RedirectStandardOutput $startOut -RedirectStandardError $startErr -Wait -PassThru
        $startText = [IO.File]::ReadAllText($startOut) + [IO.File]::ReadAllText($startErr)
        Assert-Equal 1 $start.ExitCode 'Shell-style Start did not reject the other Dango instance.'
        Assert-True ($startText -match [regex]::Escape('Another Dango version is running')) 'Shell-style Start failed for a reason other than the existing Dango instance.'
    }
    finally { $mutex.Dispose(); $env:PSModulePath = $modulePathBefore }
    Assert-Equal ($processesBefore -join ',') (@(Get-Process -Name 'Dango.Desktop' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -join ',') 'Shell-style management entry changed the Dango process list.'
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
        $preset = [IO.Path]::ChangeExtension($settings, '.window-preset.json')
        [IO.File]::WriteAllText($preset, '{"schemaVersion":1,"windowSizePreset":"Portrait"}', [Text.UTF8Encoding]::new($false))
        $presetAfterSave = [IO.File]::ReadAllBytes($preset)
        Undo-DangoUpdate -InstallRoot $settingsInstall
        Assert-BytesEqual $afterSave ([IO.File]::ReadAllBytes($settings)) 'Rollback rewrote the latest fixture settings bytes.'
        Assert-BytesEqual $presetAfterSave ([IO.File]::ReadAllBytes($preset)) 'Rollback rewrote the latest fixture sidecar bytes.'
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
    $pending = @(Get-ChildItem -LiteralPath $installRoot -Filter 'install-state.json.pending-*')
    Assert-True ($pending.Count -ge 1) 'Persistent-lock failure discarded the pending state.'
    $pendingState = [IO.File]::ReadAllText($pending[-1].FullName) | ConvertFrom-Json
    Assert-Equal (Test-DangoPackage $packageB).VersionId $pendingState.current 'Persistent-lock failure changed the proposed pending content.'
}
Invoke-Case 'transient-windows-replacement-failure-retries-without-deleting-state' {
    $root = Join-Path $EvidenceRoot 'atomic-retry'; New-Item -ItemType Directory -Path $root | Out-Null
    $path = Join-Path $root 'state.json'; [IO.File]::WriteAllText($path, '{"old":true}')
    & $installModule {
        $script:AtomicMoveBefore = ${function:Move-DangoAtomicJsonFile}; $script:AtomicMoveAttempts = 0
        Set-Item Function:Move-DangoAtomicJsonFile -Value {
            param($Temporary, $Path)
            $script:AtomicMoveAttempts++
            if ($script:AtomicMoveAttempts -eq 1) {
                if ([IO.File]::ReadAllText($Path) -cne '{"old":true}') { throw 'Destination was discarded before retry.' }
                throw [IO.IOException]::new('Injected Windows unable-to-remove-replaced-file error.', -2147023721)
            }
            & $script:AtomicMoveBefore $Temporary $Path
        }
    }
    try { & $installModule { param($target) Write-DangoAtomicJson $target ([ordered]@{ next = $true }) } $path }
    finally { & $installModule { Set-Item Function:Move-DangoAtomicJsonFile -Value $script:AtomicMoveBefore } }
    Assert-Equal 2 (& $installModule { $script:AtomicMoveAttempts }) 'Transient error was not retried exactly once.'
    Assert-True (([IO.File]::ReadAllText($path) | ConvertFrom-Json).next) 'Retry did not atomically publish the new state.'
}
Invoke-Case 'unrelated-atomic-replacement-failure-is-not-retried' {
    $root = Join-Path $EvidenceRoot 'atomic-no-retry'; New-Item -ItemType Directory -Path $root | Out-Null
    $path = Join-Path $root 'state.json'; [IO.File]::WriteAllText($path, '{"old":true}')
    & $installModule {
        $script:AtomicMoveBefore = ${function:Move-DangoAtomicJsonFile}; $script:AtomicMoveAttempts = 0
        Set-Item Function:Move-DangoAtomicJsonFile -Value {
            param($Temporary, $Path)
            $script:AtomicMoveAttempts++
            throw [IO.IOException]::new('Injected nontransient IO failure.', -2147024891)
        }
    }
    try { Assert-Throws { & $installModule { param($target) Write-DangoAtomicJson $target ([ordered]@{ next = $true }) } $path } 'Unrelated IO failure was hidden.' }
    finally { & $installModule { Set-Item Function:Move-DangoAtomicJsonFile -Value $script:AtomicMoveBefore } }
    Assert-Equal 1 (& $installModule { $script:AtomicMoveAttempts }) 'Unrelated IO failure was retried.'
    Assert-Equal '{"old":true}' ([IO.File]::ReadAllText($path)) 'Nontransient failure changed original state.'
    Assert-Equal 1 @(Get-ChildItem -LiteralPath $root -Filter '*.pending-*').Count 'Pending evidence was discarded.'
}
Invoke-Case 'atomic-retry-refuses-changed-target-or-pending-content' {
    foreach ($change in @('target', 'pending')) {
        $root = Join-Path $EvidenceRoot ('atomic-changed-' + $change); New-Item -ItemType Directory -Path $root | Out-Null
        $path = Join-Path $root 'state.json'; [IO.File]::WriteAllText($path, '{"old":true}')
        & $installModule {
            param($selected)
            $script:AtomicMoveBefore = ${function:Move-DangoAtomicJsonFile}; $script:AtomicMoveAttempts = 0; $script:AtomicChangedInput = $selected
            Set-Item Function:Move-DangoAtomicJsonFile -Value {
                param($Temporary, $Path)
                $script:AtomicMoveAttempts++
                if ($script:AtomicChangedInput -eq 'target') { [IO.File]::WriteAllText($Path, '{"external":true}') }
                else { [IO.File]::WriteAllText($Temporary, '{"external":true}') }
                throw [IO.IOException]::new('Injected transient error after external input modification.', -2147023721)
            }
        } $change
        try { Assert-Throws { & $installModule { param($target) Write-DangoAtomicJson $target ([ordered]@{ next = $true }) } $path } 'Retry overwrote changed inputs.' }
        finally { & $installModule { Set-Item Function:Move-DangoAtomicJsonFile -Value $script:AtomicMoveBefore } }
        Assert-Equal 1 (& $installModule { $script:AtomicMoveAttempts }) 'Changed inputs reached another replacement attempt.'
        $expected = '{"old":true}'; if ($change -eq 'target') { $expected = '{"external":true}' }
        Assert-Equal $expected ([IO.File]::ReadAllText($path)) 'Retry overwrote the changed or original target.'
        Assert-Equal 1 @(Get-ChildItem -LiteralPath $root -Filter '*.pending-*').Count 'Changed pending evidence was discarded.'
    }
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
Invoke-Case 'old-package-refuses-isolated-acceptance-launch' {
    $root = Join-Path $EvidenceRoot 'acceptance-old-package'
    $null = Install-DangoPackage $packageA $root
    $settings = New-AcceptanceSettingsFixture 'acceptance-old-fixture'
    $before = Read-StateBytes $root
    Assert-Throws { Start-DangoInstalled $root -AcceptanceSettingsRoot $settings } 'Legacy package accepted an isolation argument it cannot understand.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Rejected old acceptance launch changed state.'
}
Invoke-Case 'acceptance-directory-requires-marker-and-rejects-protected-roots' {
    $settings = New-AcceptanceSettingsFixture 'acceptance-valid-directory'
    $actual = & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $settings $packageB
    Assert-Equal $settings $actual 'Owned fixture directory was not accepted.'
    $unowned = Join-Path $EvidenceRoot 'acceptance-no-marker'; New-Item -ItemType Directory -Path $unowned | Out-Null
    Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $unowned $packageB } 'Unowned acceptance directory was accepted.'
    Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $EvidenceRoot $packageB } 'Package ancestor was accepted as fixture settings root.'
    $daily = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango'
    Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $daily $packageB } 'Daily settings root was accepted for isolated launch.'
    [IO.File]::WriteAllText((Join-Path $settings '.dango-local-install-acceptance.json'), '{"application":"Dango","acceptanceFormat":1,"fictional":false}')
    Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $settings $packageB } 'Nonfictional settings marker was accepted.'
    [IO.File]::WriteAllText((Join-Path $settings '.dango-local-install-acceptance.json'), '{"application":"Dango","acceptanceFormat":"1","fictional":true}')
    Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $settings $packageB } 'String-valued marker schema was accepted.'
}
Invoke-Case 'process-settings-scope-parses-windows-quoted-arguments' {
    $parsed = & $installModule { Get-DangoProcessSettingsScope '"C:\Fixture\Dango.Desktop.exe" --local-install-acceptance-settings "C:\Fixture\Settings with spaces"' }
    Assert-Equal 'C:\Fixture\Settings with spaces' $parsed 'Quoted scope path was parsed incorrectly.'
    $normal = & $installModule { Get-DangoProcessSettingsScope '"C:\Fixture\Dango.Desktop.exe"' }
    Assert-True ($null -eq $normal) 'Exe-alone command line was not treated as an ordinary settings scope.'
    Assert-Throws { & $installModule { Get-DangoProcessSettingsScope '"C:\Fixture\Dango.Desktop.exe" --local-install-acceptance-settings C:\Fixture\A --local-install-acceptance-settings C:\Fixture\B' } } 'Duplicate settings scopes were accepted.'
    Assert-Throws { & $installModule { Get-DangoProcessSettingsScope '"C:\Fixture\Dango.Desktop.exe" --local-install-acceptance-settings' } } 'Scope without a path was accepted.'
}
Invoke-Case 'process-settings-scope-rejects-unknown-and-mixed-review-arguments' {
    foreach ($command in @(
        '"C:\Fixture\Dango.Desktop.exe" --unknown',
        '"C:\Fixture\Dango.Desktop.exe" --smoke-test',
        '"C:\Fixture\Dango.Desktop.exe" --local-install-acceptance-settings C:\Fixture\A --unknown',
        '"C:\Fixture\Dango.Desktop.exe" --physics-host-review --local-install-acceptance-settings C:\Fixture\A',
        '"C:\Fixture\Dango.Desktop.exe" "text --local-install-acceptance-settings C:\Fixture\fake"'
    )) {
        Assert-Throws { & $installModule { param($value) Get-DangoProcessSettingsScope $value } $command } 'Unknown or mixed-review command line was treated as an ordinary settings scope.'
    }
}
foreach ($settingsName in @('settings.json', 'settings.window-preset.json')) {
    foreach ($dangling in @($false, $true)) {
        Invoke-Case ('acceptance-refuses-settings-reparse-' + $settingsName + '-' + $dangling) {
            $settings = New-AcceptanceSettingsFixture ('reparse-settings-' + $settingsName + '-' + $dangling)
            $target = Join-Path $EvidenceRoot ('reparse-target-' + $settingsName + '-' + $dangling)
            New-Item -ItemType Directory -Path $target | Out-Null
            $link = Join-Path $settings $settingsName
            $null = New-Item -ItemType Junction -Path $link -Target $target
            if ($dangling) { [IO.Directory]::Move($target, ($target + '-retained')) }
            $entry = @(Get-ChildItem -LiteralPath $settings -Force | Where-Object { $_.Name -ceq $settingsName })
            Assert-True ($entry.Count -eq 1 -and ($entry[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'Test did not create a real settings reparse entry.'
            Assert-Throws { & $installModule { param($fixture, $package) Assert-DangoAcceptanceSettingsRoot $fixture $package } $settings $packageB } 'Acceptance root allowed a settings reparse point.'
        }
    }
}
Invoke-Case 'installed-start-requires-identical-existing-settings-scope' {
    $candidateContract = (Test-DangoPackage $packageB).Manifest.settingsContract
    if ($candidateContract -cne 'dango-desktop-settings-v4-preset-sidecar-v1') { throw 'This scope test requires the reviewed new-contract B fixture.' }
    $root = Join-Path $EvidenceRoot 'acceptance-existing-scope'; $null = Install-DangoPackage $packageB $root
    $settingsA = New-AcceptanceSettingsFixture 'acceptance-existing-A'
    $settingsB = New-AcceptanceSettingsFixture 'acceptance-existing-B'
    $state = Get-DangoInstallState $root; $before = Read-StateBytes $root
    $executable = Join-Path $root ('versions\' + $state.current + '\Dango.Desktop.exe')
    if (& $installModule { Test-DangoProductRunning }) { throw 'A real ordinary Dango instance prevents the fixture settings-scope test.' }
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    $activation = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::AutoReset, 'Local\Dango.Phase1.App.Activate')
    & $installModule {
        param($path, $fixture)
        $script:OrdinaryProcessesBefore = ${function:Get-DangoOrdinaryProcesses}
        $script:ScopedProcessFixture = [pscustomobject]@{ Id = 424242; Path = $path; CommandLine = '"' + $path + '" --local-install-acceptance-settings "' + $fixture + '"' }
        Set-Item Function:Get-DangoOrdinaryProcesses -Value { $script:ScopedProcessFixture }
    } $executable $settingsA
    try {
        Assert-Throws { Start-DangoInstalled $root } 'Ordinary Start claimed an existing acceptance instance.'
        Assert-Throws { Start-DangoInstalled $root -AcceptanceSettingsRoot $settingsB } 'Different acceptance fixture claimed the existing instance.'
        $same = Start-DangoInstalled $root -AcceptanceSettingsRoot $settingsA
        Assert-True ($same.ActivatedExisting -and $same.ProcessId -eq 424242 -and $activation.WaitOne(0)) 'Identical acceptance scope did not activate its existing fixture instance.'
        & $installModule { $script:ScopedProcessFixture.CommandLine = '"' + $script:ScopedProcessFixture.Path + '"' }
        Assert-Throws { Start-DangoInstalled $root -AcceptanceSettingsRoot $settingsA } 'Acceptance Start claimed an existing ordinary instance.'
        $ordinary = Start-DangoInstalled $root
        Assert-True ($ordinary.ActivatedExisting -and $activation.WaitOne(0)) 'Identical ordinary scope did not activate its existing fixture instance.'
    }
    finally {
        & $installModule { Set-Item Function:Get-DangoOrdinaryProcesses -Value $script:OrdinaryProcessesBefore }
        $activation.Dispose(); $mutex.Dispose()
    }
    Assert-BytesEqual $before (Read-StateBytes $root) 'Existing-instance scope checks changed install state.'
}
Invoke-Case 'ordinary-update-refuses-old-management-tools-and-keeps-state' {
    $root = New-LegacyInstallFixture 'old-tools-update-refusal'
    $before = Read-StateBytes $root; $toolsBefore = Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')
    Assert-Throws { Install-DangoPackage -PackagePath $packageB -InstallRoot $root -StageOnly } 'Ordinary update silently replaced the old manager.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Ordinary update modified state.'
    Assert-Equal $toolsBefore (Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')) 'Ordinary update modified old tools.'
}
Invoke-Case 'explicit-migration-preserves-state-shortcut-and-rollback-old-package' {
    $root = New-LegacyInstallFixture 'migration-success'
    $before = Read-StateBytes $root; $shortcutBefore = [IO.File]::ReadAllBytes((Join-Path $root 'Rollback.lnk'))
    $result = Update-DangoManagementTools -InstallRoot $root
    Assert-Equal 'completed' $result.Phase 'Migration did not complete.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Migration rewrote install state.'
    Assert-BytesEqual $shortcutBefore ([IO.File]::ReadAllBytes((Join-Path $root 'Rollback.lnk'))) 'Migration rewrote the shortcut.'
    Assert-Equal (Get-Hash (Join-Path $LegacyToolsPath 'Dango.LocalInstall.psm1')) (Get-Hash (Join-Path $result.BackupPath 'Dango.LocalInstall.psm1')) 'Old manager backup differs.'
    $b = Install-DangoPackage -PackagePath $packageB -InstallRoot $root
    $manager = Join-Path $root 'tools\Manage-Dango.ps1'
    $output = @(& powershell -NoProfile -NonInteractive -File $manager -Action Rollback 2>&1) | Out-String
    Assert-Equal 0 $LASTEXITCODE 'Migrated manager cannot roll back to the old package.'
    Assert-Equal (Test-DangoPackage $packageA).VersionId (Get-DangoInstallState $root).current 'Migrated manager restored the wrong package.'
    Assert-Throws { Update-DangoManagementTools $root } 'Completed one-time migration was repeated.'
}
Invoke-Case 'migration-rejects-tampered-old-tools-without-changing-state' {
    $root = New-LegacyInstallFixture 'migration-tampered-old'
    [IO.File]::AppendAllText((Join-Path $root 'tools\Manage-Dango.ps1'), ' ')
    $before = Read-StateBytes $root; $toolsBefore = Get-Hash (Join-Path $root 'tools\Manage-Dango.ps1')
    Assert-Throws { Update-DangoManagementTools $root } 'Migration accepted an unknown old tool fingerprint.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Rejected migration rewrote state.'
    Assert-Equal $toolsBefore (Get-Hash (Join-Path $root 'tools\Manage-Dango.ps1')) 'Rejected migration overwrote user-modified tools.'
}
Invoke-Case 'migration-rejects-corrupt-installed-package-before-tools-change' {
    $root = New-LegacyInstallFixture 'migration-corrupt-package'
    $state = Get-DangoInstallState $root
    [IO.File]::AppendAllText((Join-Path $root ('versions\' + $state.current + '\Dango.Desktop.exe')), 'bad')
    $before = Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')
    Assert-Throws { Update-DangoManagementTools $root } 'Migration ignored an installed package integrity failure.'
    Assert-Equal $before (Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')) 'Tools changed after package rejection.'
}
Invoke-Case 'migration-shortcut-conflict-keeps-user-link-and-old-tools' {
    $root = New-LegacyInstallFixture 'migration-shortcut-conflict'
    $shortcut = Join-Path $root 'Rollback.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut); $link.TargetPath = (Join-Path $env:WINDIR 'System32\notepad.exe'); $link.Save()
    $linkBefore = [IO.File]::ReadAllBytes($shortcut); $toolsBefore = Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')
    Assert-Throws { Update-DangoManagementTools $root } 'Migration accepted a conflicting rollback shortcut.'
    Assert-BytesEqual $linkBefore ([IO.File]::ReadAllBytes($shortcut)) 'Migration changed a user link.'
    Assert-Equal $toolsBefore (Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')) 'Shortcut rejection changed tools.'
}
Invoke-Case 'interrupted-directory-switch-recovers-exact-old-manager' {
    foreach ($active in @($false, $true)) {
        $root = New-InterruptedMigrationFixture ('migration-interrupted-' + $active) -NewToolsActive:$active
        $before = Read-StateBytes $root; $shortcutBefore = [IO.File]::ReadAllBytes((Join-Path $root 'Rollback.lnk'))
        if ($active) { Assert-Throws { Start-DangoInstalled $root } 'Pending migration allowed an application launch.' }
        $result = Restore-DangoManagementToolsMigration $root
        Assert-Equal 'restored' $result.Phase 'Recovery did not report restored.'
        Assert-Equal (Get-Hash (Join-Path $LegacyToolsPath 'Dango.LocalInstall.psm1')) (Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')) 'Recovery restored unknown tools.'
        Assert-BytesEqual $before (Read-StateBytes $root) 'Recovery rewrote state.'
        Assert-BytesEqual $shortcutBefore ([IO.File]::ReadAllBytes((Join-Path $root 'Rollback.lnk'))) 'Recovery rewrote shortcut.'
    }
}
Invoke-Case 'interrupted-migration-rejects-tampered-backup' {
    $root = New-InterruptedMigrationFixture 'migration-bad-backup'
    $journal = [IO.File]::ReadAllText((Join-Path $root 'tools-migration.json')) | ConvertFrom-Json
    $backup = Join-Path $root ('.tools-backup-' + $journal.migrationId)
    [IO.File]::AppendAllText((Join-Path $backup 'Manage-Dango.ps1'), ' ')
    $before = Read-StateBytes $root
    Assert-Throws { Restore-DangoManagementToolsMigration $root } 'Recovery accepted a modified backup.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Rejected recovery changed state.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'tools'))) 'Rejected recovery moved unknown tools into service.'
}
Invoke-Case 'migration-recovery-refuses-reparse-backup' {
    $root = New-InterruptedMigrationFixture 'migration-reparse-backup'
    $journal = [IO.File]::ReadAllText((Join-Path $root 'tools-migration.json')) | ConvertFrom-Json
    $backup = Join-Path $root ('.tools-backup-' + $journal.migrationId)
    $retained = Join-Path $root ('.fixture-retained-backup-' + $journal.migrationId)
    [IO.Directory]::Move($backup, $retained)
    $null = New-Item -ItemType Junction -Path $backup -Target $retained
    $before = Read-StateBytes $root
    Assert-Throws { Restore-DangoManagementToolsMigration $root } 'Recovery accepted a reparse-point backup.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Reparse rejection changed state.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'tools'))) 'Reparse backup was activated.'
}
Invoke-Case 'explicit-migration-entry-respects-install-operation-lock' {
    $root = New-LegacyInstallFixture 'migration-operation-lock'
    $before = Read-StateBytes $root
    $held = & $installModule { param($target) Enter-DangoInstallLock $target } $root
    $preferenceBefore = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $output = @(& powershell -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'migrate-local-install-tools.ps1') -InstallRoot $root 2>&1) | Out-String
        Assert-True ($LASTEXITCODE -ne 0 -and $output -match 'operation is in progress') 'Migration entry ignored the held install lock.'
    }
    finally { $ErrorActionPreference = $preferenceBefore; & $installModule { param($mutex) Exit-DangoInstallLock $mutex } $held }
    Assert-BytesEqual $before (Read-StateBytes $root) 'Lock rejection changed state.'
}
Invoke-Case 'interrupted-migration-rejects-unknown-active-tools-and-state-change' {
    $root = New-InterruptedMigrationFixture 'migration-unknown-tools' -NewToolsActive
    [IO.File]::AppendAllText((Join-Path $root 'tools\Manage-Dango.ps1'), ' ')
    $before = Get-Hash (Join-Path $root 'tools\Manage-Dango.ps1')
    Assert-Throws { Restore-DangoManagementToolsMigration $root } 'Recovery overwrote unknown active tools.'
    Assert-Equal $before (Get-Hash (Join-Path $root 'tools\Manage-Dango.ps1')) 'Recovery discarded modified active tools.'
    $changedRoot = New-InterruptedMigrationFixture 'migration-changed-state'
    [IO.File]::AppendAllText((Join-Path $changedRoot 'install-state.json'), ' ')
    Assert-Throws { Restore-DangoManagementToolsMigration $changedRoot } 'Recovery ignored an install-state snapshot change.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $changedRoot 'tools'))) 'Snapshot rejection activated a manager.'
}
Invoke-Case 'migration-completion-write-failure-restores-old-tools' {
    $root = New-LegacyInstallFixture 'migration-write-failure'
    $before = Read-StateBytes $root
    & $installModule {
        $script:MigrationOriginalAtomicWrite = ${function:Write-DangoAtomicJson}
        Set-Item Function:Write-DangoAtomicJson -Value {
            param($Path, $Value)
            if ((Split-Path -Leaf $Path) -eq 'tools-migration.json' -and $Value.phase -eq 'completed') { throw 'Injected migration completion write failure.' }
            & $script:MigrationOriginalAtomicWrite $Path $Value
        }
    }
    try { Assert-Throws { Update-DangoManagementTools $root } 'Completion write failure did not fail migration.' }
    finally { & $installModule { Set-Item Function:Write-DangoAtomicJson -Value $script:MigrationOriginalAtomicWrite } }
    Assert-Equal (Get-Hash (Join-Path $LegacyToolsPath 'Dango.LocalInstall.psm1')) (Get-Hash (Join-Path $root 'tools\Dango.LocalInstall.psm1')) 'Failure did not restore old tools.'
    Assert-BytesEqual $before (Read-StateBytes $root) 'Failure changed state.'
    Assert-Equal 'restored' (([IO.File]::ReadAllText((Join-Path $root 'tools-migration.json')) | ConvertFrom-Json).phase) 'Failure was not recorded as restored.'
}
Invoke-Case 'migration-and-recovery-reject-running-product' {
    $root = New-LegacyInstallFixture 'migration-running'
    $recoveryRoot = New-InterruptedMigrationFixture 'recovery-running'
    Remove-Module Dango.LocalInstall -Force; Import-Module $modulePath -Force
    $mutex = New-Object Threading.Mutex($false, 'Local\Dango.Phase1.App.Mutex')
    try {
        Assert-Throws { Update-DangoManagementTools $root } 'Running product allowed tools migration.'
        Assert-Throws { Restore-DangoManagementToolsMigration $recoveryRoot } 'Running product allowed tools recovery.'
    }
    finally { $mutex.Dispose() }
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

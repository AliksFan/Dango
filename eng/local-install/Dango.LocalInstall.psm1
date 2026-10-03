# Windows PowerShell 5.1 compatible; no SDK or repository needed after installation.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
# Shell shortcuts can inherit a PowerShell 7 module path while running Windows PowerShell.
# Load the host's own manifests, including Get-FileHash (a script function in PS 5.1).
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
Import-Module (Join-Path $PSHOME 'Modules/CimCmdlets/CimCmdlets.psd1') -ErrorAction Stop
$script:SettingsContract = 'dango-desktop-settings-v4'
$script:SettingsSourceHashes = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '4F7A837A22888A076A25A0491670872902769A0A14D9BF6C09CAE44DAB260C84'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'B7524524B0217CAB84248C4C73A533FD36E105517D3B88DF981EC64DC7E60C47'
    'AppShell/DangoApplicationHost.cs' = 'D21472100CC2C9BE877BBDFBA60A8A8503477EAF0FBB69A8F7ACE117B19EC4B7'
    'App.xaml.cs' = '0314E2E847EEC15BAB7E69A191FAB1B260C199714022127C7B930D4C32E17822'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
$script:SettingsContracts = @{ 'dango-desktop-settings-v4' = $script:SettingsSourceHashes }
$script:SettingsContracts['dango-desktop-settings-v4-preset-sidecar-v1'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = 'C60F9DECA4766F3EA547B8C1D5F555A7542B1449AF8C848962204019F3491D5A'
    'App.xaml.cs' = '7E112AFB285DD17A27285FBE35F72B9B178A99133E0D4FC6CA5FA0113463D19B'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
$script:LegacyToolHashes = @{
    'Dango.LocalInstall.psm1' = '5500CB19FFE0486C82DE9C42EC0C41DA27E65606618A2360DDA97AC6F9CCA77B'
    'Manage-Dango.ps1' = '58668DDD3C117BF8F82E0F83F6C32D847CDDB6AB3B44564D11ED8EC946C11738'
}
$script:RequiredHashes = @{
    'dango_physics_module.dll' = '00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C'
    'dango_box2d.dll' = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'
    'licenses/Box2D-LICENSE.txt' = '68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2'
    'licenses/Box2D-dependency.json' = 'F878EE6173D5037446F75FFED09F286AEEAFB1B50BEAE794D1615A59CC27E698'
}

function Get-DangoFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A nonempty local path is required.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($full -notmatch '^[A-Za-z]:\\.+' -or $full.Contains('"')) { throw "A local directory below a drive root is required: $Path" }
    return $full
}

function Assert-DangoNoReparse([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not supported: $cursor"
            }
        }
        $cursor = Split-Path -Parent $cursor
    }
}

function Get-DangoHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-DangoSettingsCompatibilityContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SettingsContract)
    if (@($script:SettingsContracts.Keys) -cnotcontains $SettingsContract) { throw 'Unsupported settings compatibility contract.' }
    $result = [ordered]@{}
    foreach ($relative in @($script:SettingsContracts[$SettingsContract].Keys | Sort-Object)) {
        $result[$relative] = $script:SettingsContracts[$SettingsContract][$relative]
    }
    return $result
}

function Get-DangoSettingsSourceContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot, [string]$SettingsContract)
    $contracts = @($script:SettingsContracts.Keys)
    if ($SettingsContract) {
        $null = Get-DangoSettingsCompatibilityContract $SettingsContract
        $contracts = @($SettingsContract)
    }
    $result = [ordered]@{}
    foreach ($relative in @($script:SettingsSourceHashes.Keys | Sort-Object)) {
        $source = [IO.File]::ReadAllText((Join-Path $RepositoryRoot ('src/Dango.Desktop/' + $relative)))
        $source = $source.Replace(([string][char]13 + [char]10), [string][char]10)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))).Replace('-', '') }
        finally { $sha.Dispose() }
        $result[$relative] = $hash
    }
    foreach ($contract in $contracts) {
        $expected = $script:SettingsContracts[$contract]
        $matched = $true
        foreach ($relative in $expected.Keys) { if ($result[$relative] -cne $expected[$relative]) { $matched = $false; break } }
        if ($matched) { return $result }
    }
    throw 'Settings source contract changed. Review migration and rollback compatibility before publishing.'
}

function Get-DangoPackageFiles([string]$Root) {
    foreach ($item in Get-ChildItem -LiteralPath $Root -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Package contains a reparse point: $($item.Name)" }
        if ($item.PSIsContainer) { Get-DangoPackageFiles $item.FullName }
        else { $item }
    }
}

function Test-DangoPackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PackagePath)
    $root = Get-DangoFullPath $PackagePath
    Assert-DangoNoReparse $root
    $manifestPath = Join-Path $root 'package-manifest.json'
    $manifestHash = Get-DangoHash $manifestPath
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ($manifest.packageFormat -ne 2 -or $manifest.packageType -cne 'internal-candidate' -or
        $manifest.runtime -cne 'win-x64' -or $manifest.selfContained -isnot [bool] -or -not $manifest.selfContained -or
        $manifest.sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or $manifest.sourceDirty -isnot [bool] -or
        @($script:SettingsContracts.Keys) -cnotcontains $manifest.settingsContract) {
        throw 'Unsupported package metadata or settings compatibility contract.'
    }
    $sourceHashes = Get-DangoSettingsCompatibilityContract $manifest.settingsContract
    if (@($manifest.settingsSourceHashes.PSObject.Properties).Count -ne $sourceHashes.Count) { throw 'Missing settings source contract evidence.' }
    foreach ($relative in $sourceHashes.Keys) {
        if ($manifest.settingsSourceHashes.$relative -cne $sourceHashes[$relative]) { throw 'The settings source contract is incompatible.' }
    }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $records = @($manifest.files)
    if ($records.Count -eq 0) { throw 'The package file list is empty.' }
    $actualFiles = @(Get-DangoPackageFiles $root)
    foreach ($record in $records) {
        $relative = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or
            $relative -match '(^/|[<>:"|?*]|[\x00-\x1f])' -or $relative -ieq 'package-manifest.json') {
            throw "Invalid package path: $relative"
        }
        foreach ($segment in $relative.Split('/')) {
            if (-not $segment -or $segment -in @('.', '..') -or $segment -match '[. ]$') { throw "Invalid package path: $relative" }
        }
        if (-not $paths.Add($relative)) { throw "Duplicate package path: $relative" }
        $file = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing package file: $relative" }
        if ($record.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
            (Get-Item -LiteralPath $file).Length -ne $record.size -or (Get-DangoHash $file) -ne $record.sha256) {
            throw "Package integrity check failed: $relative"
        }
    }
    if ($actualFiles.Count -ne $records.Count + 1) { throw 'The package contains unlisted or missing files.' }
    foreach ($file in $actualFiles) {
        $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        if ($relative -ine 'package-manifest.json' -and -not $paths.Contains($relative)) { throw "Unlisted package file: $relative" }
    }
    if (-not $paths.Contains('Dango.Desktop.exe')) { throw 'The package is missing Dango.Desktop.exe.' }
    foreach ($required in $script:RequiredHashes.Keys) {
        if (-not $paths.Contains($required) -or (Get-DangoHash (Join-Path $root $required)) -ne $script:RequiredHashes[$required]) {
            throw "Protected package input differs from the accepted baseline: $required"
        }
    }
    return [pscustomobject]@{
        VersionId = $manifest.sourceCommit.Substring(0, 12) + '-' + $manifestHash.Substring(0, 12).ToLowerInvariant()
        Root = $root; Manifest = $manifest; ManifestHash = $manifestHash
    }
}

function Enter-DangoInstallLock([string]$Root) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Root.ToUpperInvariant()))).Replace('-', '') }
    finally { $sha.Dispose() }
    $mutex = [Threading.Mutex]::new($false, ('Local\Dango.LocalInstall.' + $key))
    try {
        try { $acquired = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another Dango installation or launch operation is in progress.' }
        return $mutex
    }
    catch { $mutex.Dispose(); throw }
}

function Exit-DangoInstallLock($Mutex) {
    $Mutex.ReleaseMutex()
    $Mutex.Dispose()
}

function Write-DangoAtomicJson([string]$Path, $Value) {
    $temporary = $Path + '.pending-' + [Guid]::NewGuid().ToString('N')
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    $targetExisted = [IO.File]::Exists($Path)
    $originalHash = $null
    if ($targetExisted) { $originalHash = Get-DangoHash $Path }
    $pendingHash = Get-DangoHash $temporary
    # Windows scanners can briefly prevent an atomic replacement. Never fall back to deleting the destination.
    # A persistent or unrelated failure still preserves the old state and pending file for diagnosis.
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        if ($attempt -gt 0) {
            Assert-DangoNoReparse $Path
            Assert-DangoNoReparse $temporary
            if ([IO.File]::Exists($Path) -ne $targetExisted -or
                ($targetExisted -and (Get-DangoHash $Path) -cne $originalHash) -or
                -not [IO.File]::Exists($temporary) -or (Get-DangoHash $temporary) -cne $pendingHash) {
                throw 'Atomic replacement inputs changed during retry. Original files and pending evidence were preserved.'
            }
        }
        try { Move-DangoAtomicJsonFile $temporary $Path; return }
        catch {
            $cause = $_.Exception
            while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
            if ($cause -isnot [IO.IOException] -or ($cause.HResult -band 65535) -notin @(32, 33, 1175) -or $attempt -eq 9) { throw }
            Start-Sleep -Milliseconds 50
        }
    }
}

function Move-DangoAtomicJsonFile([string]$Temporary, [string]$Path) {
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($Temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($Temporary, $Path) }
}

function Assert-DangoOwnedRoot([string]$Root) {
    Assert-DangoNoReparse $Root
    $ownerPath = Join-Path $Root 'install-owner.json'
    Assert-DangoNoReparse $ownerPath
    $owner = [IO.File]::ReadAllText($ownerPath) | ConvertFrom-Json
    if ($owner.application -cne 'Dango' -or $owner.installFormat -ne 1) { throw 'The directory is not a supported Dango installation.' }
}

function Get-DangoInstallState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot)
    $root = Get-DangoFullPath $InstallRoot
    Assert-DangoOwnedRoot $root
    $path = Join-Path $root 'install-state.json'
    Assert-DangoNoReparse $path
    $state = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($state.stateFormat -ne 1 -or $state.settingsContract -cne $script:SettingsContract) { throw 'Invalid installation state.' }
    foreach ($id in @($state.current, $state.previous)) {
        if ($null -ne $id -and $id -cnotmatch '^[0-9a-f]{12}-[0-9a-f]{12}$') { throw 'Invalid version identifier in installation state.' }
    }
    if ($null -eq $state.current -and $null -ne $state.previous) { throw 'Invalid previous-only installation state.' }
    return $state
}

function Initialize-DangoRoot([string]$Root) {
    Assert-DangoNoReparse $Root
    Assert-DangoNoPendingToolsMigration $Root
    if (Test-Path -LiteralPath $Root) {
        Assert-DangoOwnedRoot $Root
        $null = Get-DangoInstallState $Root
        foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) {
            $target = Join-Path $Root ('tools\' + $name)
            Assert-DangoNoReparse $target
            if ((Get-DangoHash $target) -ne (Get-DangoHash (Join-Path $PSScriptRoot $name))) {
                throw 'Installed management tools differ. Refusing to overwrite them during an application update.'
            }
        }
        return
    }
    $null = New-Item -ItemType Directory -Path $Root
    $null = New-Item -ItemType Directory -Path (Join-Path $Root 'tools')
    $null = New-Item -ItemType Directory -Path (Join-Path $Root 'versions')
    foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $Root ('tools\' + $name))
    }
    Write-DangoAtomicJson (Join-Path $Root 'install-state.json') ([ordered]@{
        stateFormat = 1; settingsContract = $script:SettingsContract; current = $null; previous = $null
    })
    Write-DangoAtomicJson (Join-Path $Root 'install-owner.json') ([ordered]@{ application = 'Dango'; installFormat = 1 })
}

function Test-DangoProductRunning {
    try { $mutex = [Threading.Mutex]::OpenExisting('Local\Dango.Phase1.App.Mutex') }
    catch [Threading.WaitHandleCannotBeOpenedException] { return $false }
    try { return $true }
    finally { $mutex.Dispose() }
}

function Assert-DangoProductStopped {
    if (Test-DangoProductRunning) { throw 'Dango is running. Exit it normally from the tray before activating or rolling back a version.' }
}

function Get-DangoOrdinaryProcesses {
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    foreach ($process in Get-CimInstance Win32_Process -Filter "Name = 'Dango.Desktop.exe'") {
        if ($process.SessionId -eq $session -and $process.CommandLine -and
            $process.CommandLine -notmatch '--(physics-host-review|eye-hat-review|soft-body-preview|paired-contact-preview|smoke-test|box2d-native-probe)') {
            [pscustomobject]@{ Id = $process.ProcessId; Path = $process.ExecutablePath; CommandLine = $process.CommandLine }
        }
    }
}

function Get-DangoInstalledPackage([string]$Root, [string]$VersionId) {
    if ($VersionId -cnotmatch '^[0-9a-f]{12}-[0-9a-f]{12}$') { throw 'Invalid version identifier.' }
    $package = Test-DangoPackage (Join-Path $Root ('versions\' + $VersionId))
    if ($package.VersionId -cne $VersionId) { throw 'The installed version does not match its directory identifier.' }
    return $package
}

function Set-DangoActiveVersionCore([string]$Root, [string]$VersionId) {
    Assert-DangoNoPendingToolsMigration $Root
    $state = Get-DangoInstallState $Root
    $null = Get-DangoInstalledPackage $Root $VersionId
    if ($state.current -ceq $VersionId) { return $state }
    Assert-DangoProductStopped
    $next = [ordered]@{
        stateFormat = 1; settingsContract = $script:SettingsContract; current = $VersionId; previous = $state.current
    }
    Write-DangoAtomicJson (Join-Path $Root 'install-state.json') $next
    return [pscustomobject]$next
}

function Install-DangoPackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PackagePath, [Parameter(Mandatory)][string]$InstallRoot, [switch]$StageOnly)
    $package = Test-DangoPackage $PackagePath
    $root = Get-DangoFullPath $InstallRoot
    if ($root.StartsWith($package.Root + '\', [StringComparison]::OrdinalIgnoreCase) -or $root -ieq $package.Root) {
        throw 'The installation cannot be placed inside the source package.'
    }
    $lock = Enter-DangoInstallLock $root
    try {
        Initialize-DangoRoot $root
        $versions = Join-Path $root 'versions'
        Assert-DangoNoReparse $versions
        $target = Join-Path $versions $package.VersionId
        if (Test-Path -LiteralPath $target) { $null = Get-DangoInstalledPackage $root $package.VersionId }
        else {
            $staging = Join-Path $root ('.staging-' + [Guid]::NewGuid().ToString('N'))
            $null = New-Item -ItemType Directory -Path $staging
            foreach ($relative in @($package.Manifest.files | ForEach-Object { $_.path }) + @('package-manifest.json')) {
                $destination = Join-Path $staging $relative
                $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
                Copy-Item -LiteralPath (Join-Path $package.Root $relative) -Destination $destination
            }
            $copied = Test-DangoPackage $staging
            if ($copied.VersionId -cne $package.VersionId) { throw 'The package changed while being copied.' }
            # Both paths are below the owned root; the destination is new.
            [IO.Directory]::Move($staging, $target)
        }
        if (-not $StageOnly) { $null = Set-DangoActiveVersionCore $root $package.VersionId }
        return [pscustomobject]@{ VersionId = $package.VersionId; StagedOnly = [bool]$StageOnly; InstallRoot = $root }
    }
    finally { Exit-DangoInstallLock $lock }
}

function Set-DangoActiveVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot, [Parameter(Mandatory)][string]$VersionId)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try { Set-DangoActiveVersionCore $root $VersionId }
    finally { Exit-DangoInstallLock $lock }
}

function Undo-DangoUpdate {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try {
        $state = Get-DangoInstallState $root
        if ($null -eq $state.previous) { throw 'There is no previous version. The first installation cannot be rolled back.' }
        Set-DangoActiveVersionCore $root $state.previous
    }
    finally { Exit-DangoInstallLock $lock }
}

function Get-DangoShortcutSpec([string]$Root, [string]$Action) {
    return [pscustomobject]@{
        TargetPath = (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe')
        Arguments = '-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $Root 'tools\Manage-Dango.ps1') + '" -Action ' + $Action + ' -ShowErrors'
        WorkingDirectory = $Root
    }
}

function Assert-DangoShortcutAvailable([string]$Root, [string]$Path, [string]$Action) {
    if ([IO.Path]::GetExtension($Path) -ine '.lnk') { throw 'A .lnk shortcut path is required.' }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $Path) -PathType Container)) { throw 'The shortcut parent directory does not exist.' }
    if (Test-Path -LiteralPath $Path) {
        $shortcut = $null
        $shell = New-Object -ComObject WScript.Shell
        try {
            $shortcut = $shell.CreateShortcut($Path)
            $spec = Get-DangoShortcutSpec $Root $Action
            if ($shortcut.TargetPath -ine $spec.TargetPath -or $shortcut.Arguments -cne $spec.Arguments -or $shortcut.WorkingDirectory -ine $Root) {
                throw "An unrelated shortcut already exists; it will not be overwritten: $Path"
            }
        }
        finally {
            if ($null -ne $shortcut) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
            $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
}

function New-DangoShortcut {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot, [Parameter(Mandatory)][string]$ShortcutPath,
        [ValidateSet('Start', 'Rollback')][string]$Action = 'Start')
    $root = Get-DangoFullPath $InstallRoot
    Assert-DangoOwnedRoot $root
    $path = Get-DangoFullPath $ShortcutPath
    if ([IO.Path]::GetExtension($path) -ine '.lnk') { throw 'A .lnk shortcut path is required.' }
    Assert-DangoShortcutAvailable $root $path $Action
    if (Test-Path -LiteralPath $path) { return }
    $temporary = Join-Path (Split-Path -Parent $path) ('.dango-' + [Guid]::NewGuid().ToString('N') + '.lnk')
    $spec = Get-DangoShortcutSpec $root $Action
    $shortcut = $null
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($temporary)
        $shortcut.TargetPath = $spec.TargetPath
        $shortcut.Arguments = $spec.Arguments
        $shortcut.WorkingDirectory = $spec.WorkingDirectory
        $shortcut.WindowStyle = 7
        $shortcut.Description = 'Dango - ' + $Action
        $shortcut.Save()
        [IO.File]::Move($temporary, $path)
    }
    finally {
        if ($null -ne $shortcut) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function Start-DangoInstalled {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot, [string]$AcceptanceSettingsRoot)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try {
        Assert-DangoNoPendingToolsMigration $root
        $state = Get-DangoInstallState $root
        if ($null -eq $state.current) { throw 'No version is active. Activate the staged package after exiting the old Dango instance.' }
        $package = Get-DangoInstalledPackage $root $state.current
        $executable = Join-Path $package.Root 'Dango.Desktop.exe'
        $settingsScope = $null
        if ($PSBoundParameters.ContainsKey('AcceptanceSettingsRoot')) {
            if ($package.Manifest.settingsContract -cne 'dango-desktop-settings-v4-preset-sidecar-v1') {
                throw 'This package does not support isolated local-install acceptance settings.'
            }
            $settingsScope = Assert-DangoAcceptanceSettingsRoot $AcceptanceSettingsRoot $package.Root
        }
        if (Test-DangoProductRunning) {
            $same = @(Get-DangoOrdinaryProcesses | Where-Object { $_.Path -ieq $executable })
            if ($same.Count -ne 1) { throw 'Another Dango version is running. Exit it normally before using this version.' }
            if ((Get-DangoProcessSettingsScope $same[0].CommandLine) -ine $settingsScope) {
                throw 'Dango is running with a different settings scope. Exit it normally before this launch.'
            }
            $activation = [Threading.EventWaitHandle]::OpenExisting('Local\Dango.Phase1.App.Activate')
            try { $null = $activation.Set() }
            finally { $activation.Dispose() }
            return [pscustomobject]@{ VersionId = $state.current; ProcessId = $same[0].Id; ActivatedExisting = $true }
        }
        $launchParameters = @{ FilePath = $executable; WorkingDirectory = $package.Root; WindowStyle = 'Normal'; PassThru = $true }
        if ($null -ne $settingsScope) { $launchParameters.ArgumentList = '--local-install-acceptance-settings "' + $settingsScope + '"' }
        $process = Start-Process @launchParameters
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.HasExited) { throw 'Dango exited before its ordinary instance was confirmed. No running-version success is reported.' }
            if (Test-DangoProductRunning) {
                $same = @(Get-DangoOrdinaryProcesses | Where-Object { $_.Path -ieq $executable })
                if ($same.Count -eq 1 -and $same[0].Id -eq $process.Id) {
                    if ((Get-DangoProcessSettingsScope $same[0].CommandLine) -ine $settingsScope) {
                        throw 'The Dango instance did not confirm the requested settings scope.'
                    }
                    return [pscustomobject]@{ VersionId = $state.current; ProcessId = $process.Id; ActivatedExisting = $false }
                }
                throw 'The ordinary Dango instance belongs to another process. Exit it normally before retrying.'
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        throw 'Dango has not confirmed its ordinary instance. Inspect this process before retrying; no process was stopped.'
    }
    finally { Exit-DangoInstallLock $lock }
}

function Assert-DangoAcceptanceSettingsRoot([string]$Path, [string]$PackageRoot) {
    if ($Path -notmatch '^[A-Za-z]:[\\/].+') { throw 'An absolute local acceptance settings directory is required.' }
    $root = Get-DangoFullPath $Path
    Assert-DangoNoReparse $root
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Acceptance settings directory must already exist.' }
    $dailyRoot = Get-DangoFullPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Dango')
    foreach ($protected in @($dailyRoot, $PackageRoot)) {
        if ($root -ieq $protected -or $root.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $protected.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Acceptance settings directory must be separate from daily settings and the application package.'
        }
    }
    $markerPath = Join-Path $root '.dango-local-install-acceptance.json'
    Assert-DangoNoReparse $markerPath
    $marker = [IO.File]::ReadAllText($markerPath) | ConvertFrom-Json
    if (@($marker.PSObject.Properties).Count -ne 3 -or $marker.application -cne 'Dango' -or
        ($marker.acceptanceFormat -isnot [int] -and $marker.acceptanceFormat -isnot [long]) -or
        $marker.acceptanceFormat -ne 1 -or $marker.fictional -isnot [bool] -or -not $marker.fictional) {
        throw 'Acceptance settings directory does not have the expected fictional-fixture owner marker.'
    }
    return $root
}

function Get-DangoProcessSettingsScope([string]$CommandLine) {
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { throw 'Dango process command line is unavailable; its settings scope cannot be confirmed.' }
    if (-not ('Dango.LocalInstall.CommandLine' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Dango.LocalInstall {
    public static class CommandLine {
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);
        public static string[] Parse(string commandLine) {
            int count;
            IntPtr pointer = CommandLineToArgvW(commandLine, out count);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Cannot parse Dango process arguments.");
            try {
                string[] result = new string[count];
                for (int index = 0; index < count; index++) result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size));
                return result;
            } finally { LocalFree(pointer); }
        }
    }
}
'@
    }
    $commandArgs = [Dango.LocalInstall.CommandLine]::Parse($CommandLine)
    $scope = $null
    for ($index = 1; $index -lt $commandArgs.Length; $index++) {
        if ($commandArgs[$index] -ceq '--local-install-acceptance-settings') {
            if ($null -ne $scope -or $index + 1 -ge $commandArgs.Length) { throw 'Dango process acceptance settings scope is ambiguous.' }
            $index++
            if ($commandArgs[$index] -notmatch '^[A-Za-z]:[\\/].+') { throw 'Dango process acceptance settings scope is not an absolute local path.' }
            $scope = Get-DangoFullPath $commandArgs[$index]
        }
    }
    return $scope
}

function Assert-DangoToolDirectory([string]$Path, $Hashes) {
    Assert-DangoNoReparse $Path
    $items = @(Get-ChildItem -LiteralPath $Path -Force)
    if ($items.Count -ne 2) { throw 'Management tools directory contains missing or unknown entries.' }
    foreach ($name in $script:LegacyToolHashes.Keys) {
        $file = Join-Path $Path $name
        Assert-DangoNoReparse $file
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-DangoHash $file) -cne $Hashes[$name]) {
            throw "Unrecognized management tool fingerprint: $name"
        }
    }
}

function Get-DangoCurrentToolHashes {
    $hashes = [ordered]@{}
    foreach ($name in $script:LegacyToolHashes.Keys) { $hashes[$name] = Get-DangoHash (Join-Path $PSScriptRoot $name) }
    return $hashes
}

function Assert-DangoNoPendingToolsMigration([string]$Root) {
    $path = Join-Path $Root 'tools-migration.json'
    if (Test-Path -LiteralPath $path) {
        Assert-DangoNoReparse $path
        $journal = [IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($journal.migrationFormat -ne 1 -or $journal.migrationId -cnotmatch '^[0-9a-f]{32}$' -or
            $journal.phase -cnotin @('completed', 'restored')) {
            throw 'A management tools migration is incomplete. Run the explicit migration Recover action before starting or updating Dango.'
        }
    }
}

function Assert-DangoMigrationSnapshot([string]$Root, $Journal) {
    Assert-DangoOwnedRoot $Root
    $null = Get-DangoInstallState $Root
    if ($Journal.migrationFormat -ne 1 -or $Journal.migrationId -cnotmatch '^[0-9a-f]{32}$' -or
        $Journal.phase -cnotin @('prepared', 'completed', 'restored') -or
        $Journal.stateSha256 -cne (Get-DangoHash (Join-Path $Root 'install-state.json')) -or
        $Journal.ownerSha256 -cne (Get-DangoHash (Join-Path $Root 'install-owner.json'))) {
        throw 'Management tools migration journal or installation snapshot differs; automatic recovery is refused.'
    }
    $sourceHashes = Get-DangoCurrentToolHashes
    if (@($Journal.newToolHashes.PSObject.Properties).Count -ne 2) { throw 'Invalid management tools migration fingerprints.' }
    foreach ($name in $sourceHashes.Keys) {
        if ($Journal.newToolHashes.$name -cne $sourceHashes[$name]) { throw 'Migration recovery must use the same reviewed new management tools.' }
    }
}

function Assert-DangoInstalledVersions([string]$Root) {
    $versions = Join-Path $Root 'versions'
    Assert-DangoNoReparse $versions
    foreach ($item in Get-ChildItem -LiteralPath $versions -Force) {
        if (-not $item.PSIsContainer -or $item.Name -cnotmatch '^[0-9a-f]{12}-[0-9a-f]{12}$') { throw 'Unknown installed version entry.' }
        $null = Get-DangoInstalledPackage $Root $item.Name
    }
    $state = Get-DangoInstallState $Root
    foreach ($id in @($state.current, $state.previous)) { if ($null -ne $id) { $null = Get-DangoInstalledPackage $Root $id } }
    $rollback = Join-Path $Root 'Rollback.lnk'
    if (Test-Path -LiteralPath $rollback) { Assert-DangoNoReparse $rollback; Assert-DangoShortcutAvailable $Root $rollback 'Rollback' }
}

function Restore-DangoManagementToolsMigrationCore([string]$Root) {
    $journalPath = Join-Path $Root 'tools-migration.json'
    Assert-DangoNoReparse $journalPath
    $journal = [IO.File]::ReadAllText($journalPath) | ConvertFrom-Json
    Assert-DangoMigrationSnapshot $Root $journal
    if ($journal.phase -cne 'prepared') { throw 'Only an interrupted migration can be recovered; completed migrations keep the compatible new manager.' }
    Assert-DangoProductStopped
    Assert-DangoInstalledVersions $Root
    $tools = Join-Path $Root 'tools'
    $backup = Join-Path $Root ('.tools-backup-' + $journal.migrationId)
    $staging = Join-Path $Root ('.tools-staging-' + $journal.migrationId)
    Assert-DangoNoReparse $tools
    Assert-DangoNoReparse $backup
    Assert-DangoNoReparse $staging
    if (Test-Path -LiteralPath $staging) { Assert-DangoToolDirectory $staging (Get-DangoCurrentToolHashes) }
    if (Test-Path -LiteralPath $backup) {
        Assert-DangoToolDirectory $backup $script:LegacyToolHashes
        if (Test-Path -LiteralPath $tools) {
            Assert-DangoToolDirectory $tools (Get-DangoCurrentToolHashes)
            $rejected = Join-Path $Root ('.tools-rejected-' + $journal.migrationId)
            Assert-DangoNoReparse $rejected
            if (Test-Path -LiteralPath $rejected) { throw 'Recovery target already exists; no files were overwritten.' }
            [IO.Directory]::Move($tools, $rejected)
        }
        [IO.Directory]::Move($backup, $tools)
    }
    else { Assert-DangoToolDirectory $tools $script:LegacyToolHashes }
    Assert-DangoToolDirectory $tools $script:LegacyToolHashes
    $journal.phase = 'restored'
    Write-DangoAtomicJson $journalPath $journal
    return [pscustomobject]@{ InstallRoot = $Root; MigrationId = $journal.migrationId; Phase = 'restored' }
}

function Restore-DangoManagementToolsMigration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try { Restore-DangoManagementToolsMigrationCore $root }
    finally { Exit-DangoInstallLock $lock }
}

function Update-DangoManagementTools {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$InstallRoot)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try {
        Assert-DangoOwnedRoot $root
        $null = Get-DangoInstallState $root
        Assert-DangoNoPendingToolsMigration $root
        $journalPath = Join-Path $root 'tools-migration.json'
        if (Test-Path -LiteralPath $journalPath) {
            $previousJournal = [IO.File]::ReadAllText($journalPath) | ConvertFrom-Json
            if ($previousJournal.phase -ceq 'completed') { throw 'This installation has already completed its one-time management tools migration.' }
        }
        Assert-DangoProductStopped
        Assert-DangoInstalledVersions $root
        $tools = Join-Path $root 'tools'
        Assert-DangoToolDirectory $tools $script:LegacyToolHashes
        $sourceHashes = Get-DangoCurrentToolHashes
        Assert-DangoToolDirectory $PSScriptRoot $sourceHashes
        $id = [Guid]::NewGuid().ToString('N')
        $staging = Join-Path $root ('.tools-staging-' + $id)
        $backup = Join-Path $root ('.tools-backup-' + $id)
        Assert-DangoNoReparse $staging
        Assert-DangoNoReparse $backup
        $null = New-Item -ItemType Directory -Path $staging
        foreach ($name in $sourceHashes.Keys) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $staging $name) }
        Assert-DangoToolDirectory $staging $sourceHashes
        foreach ($name in $sourceHashes.Keys) {
            $tokens = $null; $errors = $null
            $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $staging $name), [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw "Staged management tool syntax is invalid: $name" }
        }
        $hostPath = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
        $probe = & $hostPath -NoProfile -NonInteractive -File (Join-Path $staging 'Manage-Dango.ps1') -Action Status -InstallRoot $root
        if ($LASTEXITCODE -ne 0) { throw 'Staged management tools could not read the existing installation state.' }
        $null = ($probe | Out-String | ConvertFrom-Json)
        $journal = [ordered]@{
            migrationFormat = 1; migrationId = $id; phase = 'prepared'
            stateSha256 = Get-DangoHash (Join-Path $root 'install-state.json')
            ownerSha256 = Get-DangoHash (Join-Path $root 'install-owner.json')
            newToolHashes = $sourceHashes
        }
        Write-DangoAtomicJson $journalPath $journal
        try {
            Assert-DangoProductStopped
            Assert-DangoToolDirectory $tools $script:LegacyToolHashes
            [IO.Directory]::Move($tools, $backup)
            [IO.Directory]::Move($staging, $tools)
            Assert-DangoToolDirectory $tools $sourceHashes
            $journal.phase = 'completed'
            Write-DangoAtomicJson $journalPath $journal
        }
        catch {
            $failure = $_
            try { $null = Restore-DangoManagementToolsMigrationCore $root }
            catch { throw ('Management tools migration failed. Explicit Recover is required; no files were discarded. ' + $_.Exception.Message) }
            throw $failure
        }
        return [pscustomobject]@{ InstallRoot = $root; MigrationId = $id; Phase = 'completed'; BackupPath = $backup }
    }
    finally { Exit-DangoInstallLock $lock }
}

Export-ModuleMember -Function Test-DangoPackage, Get-DangoSettingsCompatibilityContract, Get-DangoSettingsSourceContract, Install-DangoPackage, Set-DangoActiveVersion, Undo-DangoUpdate, Get-DangoInstallState, New-DangoShortcut, Start-DangoInstalled, Update-DangoManagementTools, Restore-DangoManagementToolsMigration

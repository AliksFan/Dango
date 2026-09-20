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

function Get-DangoSettingsSourceContract {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $result = [ordered]@{}
    foreach ($relative in @($script:SettingsSourceHashes.Keys | Sort-Object)) {
        $source = [IO.File]::ReadAllText((Join-Path $RepositoryRoot ('src/Dango.Desktop/' + $relative)))
        $source = $source.Replace(([string][char]13 + [char]10), [string][char]10)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))).Replace('-', '') }
        finally { $sha.Dispose() }
        if ($hash -cne $script:SettingsSourceHashes[$relative]) {
            throw "Settings source contract changed: $relative. Review migration and rollback compatibility before publishing."
        }
        $result[$relative] = $hash
    }
    return $result
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
        $manifest.settingsContract -cne $script:SettingsContract) {
        throw 'Unsupported package metadata or settings compatibility contract.'
    }
    if (@($manifest.settingsSourceHashes.PSObject.Properties).Count -ne $script:SettingsSourceHashes.Count) { throw 'Missing settings source contract evidence.' }
    foreach ($relative in $script:SettingsSourceHashes.Keys) {
        if ($manifest.settingsSourceHashes.$relative -cne $script:SettingsSourceHashes[$relative]) { throw 'The settings source contract is incompatible.' }
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
    # Failed replacement preserves the old state and the pending file for diagnosis.
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
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
            [pscustomobject]@{ Id = $process.ProcessId; Path = $process.ExecutablePath }
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
    param([Parameter(Mandatory)][string]$InstallRoot)
    $root = Get-DangoFullPath $InstallRoot
    $lock = Enter-DangoInstallLock $root
    try {
        $state = Get-DangoInstallState $root
        if ($null -eq $state.current) { throw 'No version is active. Activate the staged package after exiting the old Dango instance.' }
        $package = Get-DangoInstalledPackage $root $state.current
        $executable = Join-Path $package.Root 'Dango.Desktop.exe'
        if (Test-DangoProductRunning) {
            $same = @(Get-DangoOrdinaryProcesses | Where-Object { $_.Path -ieq $executable })
            if ($same.Count -ne 1) { throw 'Another Dango version is running. Exit it normally before using this version.' }
            $activation = [Threading.EventWaitHandle]::OpenExisting('Local\Dango.Phase1.App.Activate')
            try { $null = $activation.Set() }
            finally { $activation.Dispose() }
            return [pscustomobject]@{ VersionId = $state.current; ProcessId = $same[0].Id; ActivatedExisting = $true }
        }
        $process = Start-Process -FilePath $executable -WorkingDirectory $package.Root -WindowStyle Normal -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.HasExited) { throw 'Dango exited before its ordinary instance was confirmed. No running-version success is reported.' }
            if (Test-DangoProductRunning) {
                $same = @(Get-DangoOrdinaryProcesses | Where-Object { $_.Path -ieq $executable })
                if ($same.Count -eq 1 -and $same[0].Id -eq $process.Id) {
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

Export-ModuleMember -Function Test-DangoPackage, Get-DangoSettingsSourceContract, Install-DangoPackage, Set-DangoActiveVersion, Undo-DangoUpdate, Get-DangoInstallState, New-DangoShortcut, Start-DangoInstalled

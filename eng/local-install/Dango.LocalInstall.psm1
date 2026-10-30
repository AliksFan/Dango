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
    'App.xaml.cs' = '26054FB5051849AD2467C43FAD1FAEC3F4351609DDB1E02DF10329539953637E'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
# Separate compatibility identity for the normal belt room host. Old contracts remain valid for rollback.
$script:SettingsContracts['dango-desktop-settings-v4-belt-default-v1'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = 'DD745264BAC8F22B0FAF400C54943B34C6B908CD02CC9FE07712EE0E82C69CA6'
    'App.xaml.cs' = 'F8B88AE99D7B6A739BAE59DEA5181E14E802A1CA4531D2961106F5E4C32063B7'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
# Same persisted format, with one product route and directory-scoped acceptance instances.
# Keep the v1 source identity intact so existing packages can still be verified and rolled back.
$script:SettingsContracts['dango-desktop-settings-v4-belt-default-v2'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = '6A9252BA0C1B10A7233C7F3397CDED666D9E6C966AE3A588CABE165E819DC7BE'
    'App.xaml.cs' = 'CB0A83CA07F6D34CFDF761A027DF28F4B55DAE40E5F94B2C1D6F1FCA22A3A6DD'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
# Tool-only previews are no longer compiled into the desktop product. Persisted data is unchanged.
$script:SettingsContracts['dango-desktop-settings-v4-belt-default-v3'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = '6A9252BA0C1B10A7233C7F3397CDED666D9E6C966AE3A588CABE165E819DC7BE'
    'App.xaml.cs' = '8CBA416D20D59E43F1BB5C797A04E9D9821F0900EF1645B514D699FBCFF12B53'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
# B4 centralizes composition; persisted settings, scope identity and prior contracts are unchanged.
$script:SettingsContracts['dango-desktop-settings-v4-belt-default-v4'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = 'FD5B22CD2B5D4BC536836223B9211483A827BF6733C4F80E9F93BA1170415E8B'
    'App.xaml.cs' = '8CBA416D20D59E43F1BB5C797A04E9D9821F0900EF1645B514D699FBCFF12B53'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
}
# Roster-v2 adds verified v1 backups and editable accessory projects (ADR 0010).
$script:SettingsContracts['dango-desktop-settings-v4-roster-v2'] = @{
    'AppShell/ControlCenter/ControlCenterWindowSettingsStore.cs' = '48FF4A9AB801FFF52228E6DB760F9248E047111DDBF0DA5AD22E78B32DE97329'
    'AppShell/ControlCenter/ApplicationSettingsSession.cs' = 'E68D1E6997AF8E73305C1D374978B60B18FF107A4C07825B4A2C86212F96FB6E'
    'AppShell/DangoApplicationHost.cs' = '4E31BB596A6C400C83F2880A7D4380E2D77A4D16B1EF1176FAE8EC56DC15724E'
    'App.xaml.cs' = 'DE8BB2D3E731D667C8F81ABE6546959DD9D28BF0FE38BF37FDCA43EB4E5DA9E6'
    'Verification/VerificationSessionStore.cs' = '925A313A44DBE72C35FBA230010A521E384C529D0EA705788E1D8AE4D7B2ACBD'
    'AppShell/DangoApplicationHost.Dangos.cs' = 'DC636E277DF6581D977828581A5291A8030C6E35AC02966BDD310BB054A6B4C6'
    'Dangos/DangoRosterDocument.cs' = 'A755A51F551162391C4A4C90398EA4E0C17469576DCAD4E4DDBD4A43A66F95D3'
    'Dangos/DangoRosterStore.cs' = '3BAB22A92EC9F8F31FDCDE2212C3C7A27719C130D5EC15317918148FFDFAA435'
    'Dangos/DangoRosterSnapshot.cs' = 'EBAA243F3869296229196A3FD5BF5AC7E8C4E47E6B23F8506AD9BF4AEE01EF39'
    'Dangos/LegacyDangoRosterDocument.cs' = '87E6B9ECBA0C53388C3F8377AAE57FD6CA0FD020962C8D4A6C62F2F8BFC82B40'
    'Accessories/IAccessorySettingsPersistence.cs' = '486FE12546D00441B5EE00CBC6EB1EC4E754B858BE712E766E96A40E568F7435'
    'Accessories/AccessorySettingsStore.cs' = '7DDF7CE8DC7C011A8E9DAFCA980D201F64C6FF1725C6E367E9A8DA016352D3BE'
    'Accessories/AccessorySettingsSession.cs' = 'AE7F520EF841FEA0C12CA0DCEEE94B27E092383928F201A7DC59AF2D212CF394'
    'Accessories/AccessoryConfiguration.cs' = '410FC1AC56FE3FBFD959811E219EFC4D6BBDAB346FE964C634C59FE7330B5D23'
    'Accessories/Editing/AccessoryEditorProject.cs' = 'A2F911D076F89FB3121B613ECBBEAF7A1B90F20D82E2884B7627C23E359C8165'
    'Accessories/Editing/AccessorySkeletonConfiguration.cs' = '109FAC82DA96A7FA3898538265850213DACDA125F7AF398D1EB5EFF1B3B9DC3B'
    'Accessories/Editing/AccessoryRigConfiguration.cs' = 'F5785B7A94820BE88D1BEA1B0CD64F912DF9A6118DB20AEDDE10C7A0EEA20578'
}
# Same roster-v2 data format; retain the original source identity for existing packages.
$script:SettingsContracts['dango-desktop-settings-v4-roster-v2-startup-v1'] = $script:SettingsContracts['dango-desktop-settings-v4-roster-v2'].Clone()
# Include the already integrated desktop display-settings binding; its persisted format is unchanged.
$script:SettingsContracts['dango-desktop-settings-v4-roster-v2-startup-v1']['AppShell/DangoApplicationHost.cs'] = '4E6E24756AB600D80D6162F59CA7A85DDEFE252F3F08E45F709AFE4A4B0DD84B'
$script:SettingsContracts['dango-desktop-settings-v4-roster-v2-startup-v1']['Dangos/DangoRosterStore.cs'] = 'AFB71158105E9D24E90FCC29B2C90A96F0C9D091AE9915DD317F4990B819A927'
$script:RosterV2SettingsContracts = @('dango-desktop-settings-v4-roster-v2', 'dango-desktop-settings-v4-roster-v2-startup-v1')
$script:BeltSettingsContracts = @('dango-desktop-settings-v4-belt-default-v1', 'dango-desktop-settings-v4-belt-default-v2', 'dango-desktop-settings-v4-belt-default-v3', 'dango-desktop-settings-v4-belt-default-v4', 'dango-desktop-settings-v4-roster-v2', 'dango-desktop-settings-v4-roster-v2-startup-v1')
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
    $sourceHashes = @{}
    foreach ($contract in $contracts) {
        $expected = $script:SettingsContracts[$contract]
        $result = [ordered]@{}
        $matched = $true
        foreach ($relative in @($expected.Keys | Sort-Object)) {
            if (-not $sourceHashes.ContainsKey($relative)) {
                $path = Join-Path $RepositoryRoot ('src/Dango.Desktop/' + $relative)
                if (-not [IO.File]::Exists($path)) { $matched = $false; break }
                $source = [IO.File]::ReadAllText($path)
                $source = $source.Replace(([string][char]13 + [char]10), [string][char]10)
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $sourceHashes[$relative] = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source))).Replace('-', '') }
                finally { $sha.Dispose() }
            }
            $result[$relative] = $sourceHashes[$relative]
            if ($result[$relative] -cne $expected[$relative]) { $matched = $false; break }
        }
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

function Assert-DangoBeltPackageContract($Root, $PackageManifest, $Paths) {
    if ($PackageManifest.beltNativeContract -cne 'dango-belt-native-v1') { throw 'Missing belt native package contract.' }
    foreach ($name in @('dango_belt_product.dll', 'dango_belt_room.dll', 'belt-native-manifest.json')) {
        if (-not $Paths.Contains($name)) { throw "Missing belt native package file: $name" }
    }
    $native = [IO.File]::ReadAllText((Join-Path $Root 'belt-native-manifest.json')) | ConvertFrom-Json
    if ($native.format -ne 1 -or $native.contract -cne 'dango-belt-native-v1' -or $native.origin -cne 'source-build' -or
        @($native.modules).Count -ne 2) { throw 'Unsupported belt native build evidence.' }
    $sources = @($native.sourceHashes.PSObject.Properties)
    if ($sources.Count -eq 0) { throw 'Missing belt native source hashes.' }
    foreach ($source in $sources) {
        if ($source.Name -cnotmatch '^eng/native/box2d/(CMakeLists\.txt|dango_belt[A-Za-z0-9_]*\.(c|h)|dango_round_ball_types\.h)$' -or
            $source.Value -cnotmatch '^[0-9A-F]{64}$') { throw 'Invalid belt native source hash evidence.' }
    }
    foreach ($name in @('CMakeLists.txt', 'dango_belt.c', 'dango_belt.h', 'dango_belt_math.h', 'dango_belt_guard.h',
        'dango_belt_basis_cache.h', 'dango_belt_profile.h', 'dango_belt_room.c', 'dango_belt_room.h',
        'dango_belt_room_collection.h', 'dango_belt_room_collection_impl.h', 'dango_round_ball_types.h')) {
        if ($null -eq $native.sourceHashes.PSObject.Properties['eng/native/box2d/' + $name]) { throw "Missing belt native source binding: $name" }
    }
    foreach ($name in @('dango_belt_product.dll', 'dango_belt_room.dll')) {
        $records = @($native.modules | Where-Object { $_.path -ceq $name })
        $requiredCapabilities = if ($name -ceq 'dango_belt_room.dll') { 255 } else { 127 }
        $model = if ($name -ceq 'dango_belt_room.dll') { 'continuous-belt-room-v1' } else { 'continuous-belt-v1' }
        if ($records.Count -ne 1 -or $records[0].abi -ne 1 -or $records[0].capabilities -ne $requiredCapabilities -or
            $records[0].model -cne $model -or $records[0].tuning -cne 'C455-F-v1' -or
            $records[0].sha256 -cne (Get-DangoHash (Join-Path $Root $name))) {
            throw "Belt native artifact binding mismatch: $name"
        }
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
        # Belt-default packages use the independent native room. The historical module is optional,
        # but remains hash-protected when supplied for older diagnostics.
        if ($required -ceq 'dango_physics_module.dll' -and
            $manifest.settingsContract -cin $script:BeltSettingsContracts -and -not $paths.Contains($required)) { continue }
        if (-not $paths.Contains($required) -or (Get-DangoHash (Join-Path $root $required)) -ne $script:RequiredHashes[$required]) {
            throw "Protected package input differs from the accepted baseline: $required"
        }
    }
    if ($manifest.settingsContract -cin $script:BeltSettingsContracts) {
        Assert-DangoBeltPackageContract $root $manifest $paths
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

function Test-DangoInstanceRunning([string]$InstanceName) {
    try { $mutex = [Threading.Mutex]::OpenExisting($InstanceName + '.Mutex') }
    catch [Threading.WaitHandleCannotBeOpenedException] { return $false }
    try { return $true }
    finally { $mutex.Dispose() }
}

function Test-DangoProductRunning {
    if (Test-DangoInstanceRunning 'Local\Dango.Phase1.App') { return $true }
    # Current products may own a directory-scoped mutex. Process metadata also
    # guards activation/rollback when no legacy global mutex exists.
    return @(Get-DangoOrdinaryProcesses).Count -gt 0
}

function Get-DangoInstalledInstanceName([string]$SettingsContract, [string]$SettingsScope) {
    $name = 'Local\Dango.Phase1.App'
    if (@('dango-desktop-settings-v4-belt-default-v2', 'dango-desktop-settings-v4-belt-default-v3', 'dango-desktop-settings-v4-belt-default-v4', 'dango-desktop-settings-v4-roster-v2', 'dango-desktop-settings-v4-roster-v2-startup-v1') -ccontains $SettingsContract -and $null -ne $SettingsScope -and $SettingsScope.Length -gt 0) {
        # Match App.ProductInstanceName: absolute settings.json path, invariant
        # uppercase, UTF-8 SHA256, first 24 uppercase hex characters.
        $settings = [IO.Path]::GetFullPath((Join-Path $SettingsScope 'settings.json')).ToUpperInvariant()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $identity = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($settings))).Replace('-', '') }
        finally { $sha.Dispose() }
        return $name + '.Data.' + $identity.Substring(0, 24)
    }
    return $name
}

function Assert-DangoProductStopped {
    if (Test-DangoProductRunning) { throw 'Dango is running. Exit it normally from the tray before activating or rolling back a version.' }
}

function Get-DangoOrdinaryProcesses {
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    foreach ($process in Get-CimInstance Win32_Process -Filter "Name = 'Dango.Desktop.exe'") {
        if ($process.SessionId -eq $session -and
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
    $target = Get-DangoInstalledPackage $Root $VersionId
    if ($state.current -ceq $VersionId) { return $state }
    if ($null -ne $state.current) {
        $current = Get-DangoInstalledPackage $Root $state.current
        if ($current.Manifest.settingsContract -cin $script:RosterV2SettingsContracts -and
            $target.Manifest.settingsContract -cnotin $script:RosterV2SettingsContracts) {
            throw 'Automatic downgrade from roster-v2 to an older settings contract is blocked. Exit Dango and follow docs/adr/0010-dango-roster-editable-accessory-upgrade.md: preserve the current roster, restore the verified v1 backup offline, then explicitly launch the prior executable. This command does not restore settings.'
        }
    }
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
            if ($package.Manifest.settingsContract -cne 'dango-desktop-settings-v4-preset-sidecar-v1' -and
                $package.Manifest.settingsContract -cnotin $script:BeltSettingsContracts) {
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
            $instanceName = Get-DangoInstalledInstanceName $package.Manifest.settingsContract $settingsScope
            $activation = [Threading.EventWaitHandle]::OpenExisting($instanceName + '.Activate')
            try { $null = $activation.Set() }
            finally { $activation.Dispose() }
            return [pscustomobject]@{ VersionId = $state.current; ProcessId = $same[0].Id; ActivatedExisting = $true }
        }
        $instanceName = Get-DangoInstalledInstanceName $package.Manifest.settingsContract $settingsScope
        $launchParameters = @{ FilePath = $executable; WorkingDirectory = $package.Root; WindowStyle = 'Normal'; PassThru = $true }
        if ($null -ne $settingsScope) { $launchParameters.ArgumentList = '--local-install-acceptance-settings "' + $settingsScope + '"' }
        $process = Start-Process @launchParameters
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.HasExited) { throw 'Dango exited before its ordinary instance was confirmed. No running-version success is reported.' }
            if (Test-DangoInstanceRunning $instanceName) {
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
    # Enumerate directory entries rather than Test-Path, which can miss a dangling link's target.
    foreach ($entry in Get-ChildItem -LiteralPath $root -Force) {
        if ($entry.Name -iin @('settings.json', 'settings.window-preset.json') -and
            (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $entry.PSIsContainer)) {
            throw 'Acceptance settings files must be ordinary files; reparse points and directories are refused.'
        }
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
    if ($commandArgs.Length -eq 1) { return $null }
    if ($commandArgs.Length -ne 3 -or $commandArgs[1] -cne '--local-install-acceptance-settings') {
        throw 'Dango process arguments are not an ordinary or isolated local-install launch.'
    }
    if ($commandArgs[2] -notmatch '^[A-Za-z]:[\\/].+') { throw 'Dango process acceptance settings scope is not an absolute local path.' }
    return (Get-DangoFullPath $commandArgs[2])
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

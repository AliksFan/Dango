# Exercises real package files without launching the Dango application.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackageA,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$PackageB,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$EvidenceRoot
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourceTools = Join-Path $PSScriptRoot 'local-install'
$sourceModule = Join-Path $sourceTools 'Dango.LocalInstall.psm1'
Import-Module $sourceModule -Force
$results = New-Object Collections.ArrayList
$evidenceCreated = $false

function Assert-Real([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-RealHash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant() }
function Write-RealEvidenceJson([string]$Path, $Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
}
function Invoke-RealCase([string]$Name, [scriptblock]$Body) {
    try {
        $null = & $Body
        [void]$results.Add([ordered]@{ name = $Name; passed = $true })
        Write-Host ('PASS ' + $Name)
    }
    catch {
        [void]$results.Add([ordered]@{ name = $Name; passed = $false })
        Write-Host ('FAIL ' + $Name)
        throw
    }
}
function Assert-RealApplicationPackage($Package) {
    # Reject the textual executable fixtures used by test-local-install.ps1.
    $stream = [IO.File]::OpenRead((Join-Path $Package.Root 'Dango.Desktop.exe'))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        Assert-Real ($stream.Length -ge 64 -and $reader.ReadUInt16() -eq 0x5A4D) 'Package executable is not a real PE application.'
        $stream.Position = 60; $offset = $reader.ReadInt32()
        Assert-Real ($offset -ge 64 -and $offset -le $stream.Length - 6) 'Package executable has an invalid PE header.'
        $stream.Position = $offset
        Assert-Real ($reader.ReadUInt32() -eq 0x00004550 -and $reader.ReadUInt16() -eq 0x8664) 'Package executable is not an x64 PE application.'
    }
    finally { $reader.Dispose(); $stream.Dispose() }
    $assemblyPath = Join-Path $Package.Root 'Dango.Desktop.dll'
    $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    Assert-Real ($identity.Name -ceq 'Dango.Desktop') 'Package application assembly identity is unexpected.'
    foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'Dango.Desktop.runtimeconfig.json')) {
        Assert-Real (Test-Path -LiteralPath (Join-Path $Package.Root $name) -PathType Leaf) 'A self-contained application runtime file is missing.'
    }
}
function Assert-RealEvidenceTarget([string]$Path, $A, $B) {
    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $artifacts = Join-Path $repositoryRoot 'artifacts'
    Assert-Real ($root.StartsWith($artifacts + '\', [StringComparison]::OrdinalIgnoreCase)) 'EvidenceRoot must be a new descendant of this repository artifacts directory.'
    foreach ($package in @($A, $B)) {
        Assert-Real (-not $root.StartsWith($package.Root + '\', [StringComparison]::OrdinalIgnoreCase) -and $root -ine $package.Root) 'EvidenceRoot must not modify a source package.'
    }
    $cursor = $root
    while ($cursor) {
        $parent = Split-Path -Parent $cursor
        if ($parent -and (Test-Path -LiteralPath $parent -PathType Container)) {
            $leaf = Split-Path -Leaf $cursor
            foreach ($entry in Get-ChildItem -LiteralPath $parent -Force) {
                if ($entry.Name -ieq $leaf) {
                    Assert-Real (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'EvidenceRoot or an ancestor is a reparse point.'
                    Assert-Real ($cursor -ine $root) 'EvidenceRoot already exists; no existing evidence may be overwritten.'
                }
            }
        }
        $cursor = $parent
    }
    return $root
}
function Export-RealLegacyTools([string]$Destination) {
    $expected = @{
        'Dango.LocalInstall.psm1' = '5500CB19FFE0486C82DE9C42EC0C41DA27E65606618A2360DDA97AC6F9CCA77B'
        'Manage-Dango.ps1' = '58668DDD3C117BF8F82E0F83F6C32D847CDDB6AB3B44564D11ED8EC946C11738'
    }
    $null = New-Item -ItemType Directory -Path $Destination
    foreach ($name in $expected.Keys) {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = (Get-Command git -ErrorAction Stop).Source
        $info.Arguments = '-C "' + $repositoryRoot + '" show 0bdf5c1:eng/local-install/' + $name
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        try {
            $target = [IO.File]::Open((Join-Path $Destination $name), [IO.FileMode]::CreateNew)
            try { $process.StandardOutput.BaseStream.CopyTo($target) }
            finally { $target.Dispose() }
            $null = $process.StandardError.ReadToEnd(); $process.WaitForExit()
            Assert-Real ($process.ExitCode -eq 0) 'The reviewed legacy Git source blob is unavailable.'
        }
        finally { $process.Dispose() }
        Assert-Real ((Get-RealHash (Join-Path $Destination $name)) -ceq $expected[$name]) 'Legacy management tool fingerprint is not the reviewed baseline.'
    }
}
function Get-RealPreservedHashes {
    return [ordered]@{
        main = Get-RealHash $settingsPath
        preset = Get-RealHash $presetPath
        marker = Get-RealHash $markerPath
        shortcut = Get-RealHash $rollbackPath
    }
}
function Assert-RealPreservedFiles($Expected) {
    $actual = Get-RealPreservedHashes
    foreach ($name in $Expected.Keys) { Assert-Real ($actual[$name] -ceq $Expected[$name]) 'An operation changed a preserved fictional setting or shortcut.' }
    $main = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    $preset = [IO.File]::ReadAllText($presetPath) | ConvertFrom-Json
    Assert-Real ($main.schemaVersion -eq 4 -and $main.width -eq 1000 -and $main.height -eq 700 -and $main.hatPresetId -ceq 'bowler') 'Fictional main settings JSON changed.'
    Assert-Real ($preset.schemaVersion -eq 1 -and $preset.selectedPreset -ceq 'Portrait') 'Fictional preset JSON changed.'
}
function Save-RealStateEvidence([string]$Name) {
    $state = Get-DangoInstallState -InstallRoot $installRoot
    Write-RealEvidenceJson (Join-Path $evidence ($Name + '.json')) ([ordered]@{
        state = $state; stateSha256 = Get-RealHash (Join-Path $installRoot 'install-state.json')
        preserved = Get-RealPreservedHashes
    })
}

try {
    # Complete both read-only package checks before creating an evidence directory.
    $a = Test-DangoPackage -PackagePath $PackageA
    $b = Test-DangoPackage -PackagePath $PackageB
    Assert-Real ($a.Manifest.settingsContract -ceq 'dango-desktop-settings-v4') 'PackageA must be the reviewed legacy candidate.'
    Assert-Real ($b.Manifest.settingsContract -ceq 'dango-desktop-settings-v4-preset-sidecar-v1') 'PackageB must be the reviewed compatible sidecar candidate.'
    Assert-Real ($a.VersionId -cne $b.VersionId) 'Two distinct package versions are required.'
    Assert-RealApplicationPackage $a
    Assert-RealApplicationPackage $b
    $evidence = Assert-RealEvidenceTarget $EvidenceRoot $a $b
    $preflightModule = Get-Module Dango.LocalInstall
    & $preflightModule { Assert-DangoProductStopped }
    $null = New-Item -ItemType Directory -Path $evidence
    $evidenceCreated = $true
    $installRoot = Join-Path $evidence 'install'
    $legacyTools = Join-Path $evidence 'reviewed-legacy-tools'
    $settingsRoot = Join-Path $evidence 'fictional-settings'
    $null = New-Item -ItemType Directory -Path $settingsRoot
    $settingsPath = Join-Path $settingsRoot 'settings.json'
    $presetPath = Join-Path $settingsRoot 'settings.window-preset.json'
    $markerPath = Join-Path $settingsRoot '.dango-local-install-acceptance.json'
    Write-RealEvidenceJson $settingsPath ([ordered]@{ schemaVersion = 4; width = 1000; height = 700; hatPresetId = 'bowler'; interactionEnabled = $true; introSeen = $true })
    Write-RealEvidenceJson $presetPath ([ordered]@{ schemaVersion = 1; selectedPreset = 'Portrait' })
    Write-RealEvidenceJson $markerPath ([ordered]@{ application = 'Dango'; acceptanceFormat = 1; fictional = $true })
    $newToolHashes = [ordered]@{}
    foreach ($name in @('Dango.LocalInstall.psm1', 'Manage-Dango.ps1')) { $newToolHashes[$name] = Get-RealHash (Join-Path $sourceTools $name) }

    Invoke-RealCase 'export-reviewed-legacy-tools' { Export-RealLegacyTools $legacyTools }
    Invoke-RealCase 'legacy-stage-only-leaves-null-current' {
        Import-Module (Join-Path $legacyTools 'Dango.LocalInstall.psm1') -Force
        $staged = Install-DangoPackage -PackagePath $a.Root -InstallRoot $installRoot -StageOnly
        $state = Get-DangoInstallState -InstallRoot $installRoot
        Assert-Real ($staged.VersionId -ceq $a.VersionId -and $null -eq $state.current -and $null -eq $state.previous) 'Initial legacy StageOnly changed the active state.'
        Import-Module $sourceModule -Force
    }
    $rollbackPath = Join-Path $installRoot 'Rollback.lnk'
    Invoke-RealCase 'activate-real-a-and-create-isolated-rollback-link' {
        $null = Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $a.VersionId
        New-DangoShortcut -InstallRoot $installRoot -ShortcutPath $rollbackPath -Action Rollback
        $state = Get-DangoInstallState -InstallRoot $installRoot
        Assert-Real ($state.current -ceq $a.VersionId -and $null -eq $state.previous) 'Initial A activation is incorrect.'
    }
    $preserved = Get-RealPreservedHashes
    Save-RealStateEvidence 'a-active'
    Invoke-RealCase 'ordinary-b-update-rejects-legacy-tools' {
        $stateHash = Get-RealHash (Join-Path $installRoot 'install-state.json')
        $oldToolsHash = Get-RealHash (Join-Path $installRoot 'tools\Dango.LocalInstall.psm1')
        $rejected = $false
        try { $null = Install-DangoPackage -PackagePath $b.Root -InstallRoot $installRoot -StageOnly }
        catch { Assert-Real ($_.Exception.Message -match 'Installed management tools differ') 'Ordinary update failed for a reason other than legacy tool protection.'; $rejected = $true }
        Assert-Real $rejected 'Ordinary update silently accepted the old management tools.'
        Assert-Real ((Get-RealHash (Join-Path $installRoot 'install-state.json')) -ceq $stateHash) 'Rejected update changed active state.'
        Assert-Real ((Get-RealHash (Join-Path $installRoot 'tools\Dango.LocalInstall.psm1')) -ceq $oldToolsHash) 'Rejected update changed old tools.'
        Assert-Real (-not (Test-Path -LiteralPath (Join-Path $installRoot ('versions\' + $b.VersionId)))) 'Rejected update staged B despite the tool mismatch.'
        Assert-RealPreservedFiles $preserved
    }
    Invoke-RealCase 'explicit-migration-preserves-state-and-fixtures' {
        $stateHash = Get-RealHash (Join-Path $installRoot 'install-state.json')
        $migrated = Update-DangoManagementTools -InstallRoot $installRoot
        Assert-Real ($migrated.Phase -ceq 'completed') 'Management migration did not complete.'
        Assert-Real ((Get-RealHash (Join-Path $installRoot 'install-state.json')) -ceq $stateHash) 'Management migration changed application state.'
        foreach ($name in $newToolHashes.Keys) {
            Assert-Real ((Get-RealHash (Join-Path $installRoot ('tools\' + $name))) -ceq $newToolHashes[$name]) 'Migrated management tools differ from the reviewed new source.'
            Assert-Real ((Get-RealHash (Join-Path $migrated.BackupPath $name)) -ceq (Get-RealHash (Join-Path $legacyTools $name))) 'Legacy tool backup bytes changed.'
        }
        $journal = [IO.File]::ReadAllText((Join-Path $installRoot 'tools-migration.json')) | ConvertFrom-Json
        Assert-Real ($journal.phase -ceq 'completed') 'Migration receipt was not persisted.'
        Assert-RealPreservedFiles $preserved
        Import-Module (Join-Path $installRoot 'tools\Dango.LocalInstall.psm1') -Force
    }
    Save-RealStateEvidence 'tools-migrated'
    Invoke-RealCase 'real-b-stage-only-keeps-a-current' {
        $stateHash = Get-RealHash (Join-Path $installRoot 'install-state.json')
        $staged = Install-DangoPackage -PackagePath $b.Root -InstallRoot $installRoot -StageOnly
        Assert-Real ($staged.VersionId -ceq $b.VersionId) 'Real B StageOnly returned a different version.'
        Assert-Real ((Get-RealHash (Join-Path $installRoot 'install-state.json')) -ceq $stateHash) 'Real B StageOnly changed current or previous.'
        Assert-RealPreservedFiles $preserved
    }
    Save-RealStateEvidence 'b-staged'
    Invoke-RealCase 'activate-real-b-keeps-real-a-previous' {
        $null = Set-DangoActiveVersion -InstallRoot $installRoot -VersionId $b.VersionId
        $state = Get-DangoInstallState -InstallRoot $installRoot
        Assert-Real ($state.current -ceq $b.VersionId -and $state.previous -ceq $a.VersionId) 'Real B activation did not retain real A as previous.'
        Assert-RealPreservedFiles $preserved
    }
    Save-RealStateEvidence 'b-active'
    Invoke-RealCase 'rollback-real-a-keeps-new-manager-and-fixtures' {
        $null = Undo-DangoUpdate -InstallRoot $installRoot
        $state = Get-DangoInstallState -InstallRoot $installRoot
        Assert-Real ($state.current -ceq $a.VersionId -and $state.previous -ceq $b.VersionId) 'Rollback did not exchange real A and B.'
        foreach ($name in $newToolHashes.Keys) { Assert-Real ((Get-RealHash (Join-Path $installRoot ('tools\' + $name))) -ceq $newToolHashes[$name]) 'Application rollback changed the new manager.' }
        Assert-RealPreservedFiles $preserved
    }
    Save-RealStateEvidence 'a-rolled-back'
    Invoke-RealCase 'source-and-installed-real-packages-remain-intact' {
        foreach ($package in @($a, $b)) {
            $sourceChecked = Test-DangoPackage -PackagePath $package.Root
            $installedChecked = Test-DangoPackage -PackagePath (Join-Path $installRoot ('versions\' + $package.VersionId))
            Assert-Real ($sourceChecked.VersionId -ceq $package.VersionId -and $installedChecked.VersionId -ceq $package.VersionId) 'A source or installed package changed during the exercise.'
        }
    }
    Write-RealEvidenceJson (Join-Path $evidence 'summary.json') ([ordered]@{
        passed = $results.Count; failed = 0; applicationLaunched = $false; dailyInstallChanged = $false
        a = [ordered]@{ versionId = $a.VersionId; manifestSha256 = $a.ManifestHash; settingsContract = $a.Manifest.settingsContract }
        b = [ordered]@{ versionId = $b.VersionId; manifestSha256 = $b.ManifestHash; settingsContract = $b.Manifest.settingsContract }
        finalState = Get-DangoInstallState -InstallRoot $installRoot; results = @($results)
    })
    Write-Host ('PASS real-package-install-summary ' + $results.Count)
    exit 0
}
catch {
    $failure = $_.Exception
    if ($evidenceCreated) {
        Write-RealEvidenceJson (Join-Path $evidence 'summary.json') ([ordered]@{
            passed = @($results | Where-Object { $_.passed }).Count; failed = 1
            applicationLaunched = $false; dailyInstallChanged = $false; results = @($results)
            failureType = $failure.GetType().FullName; failureDetail = $failure.Message
        })
    }
    Write-Host 'FAIL real-package-install-summary; existing files and evidence are retained.'
    exit 1
}

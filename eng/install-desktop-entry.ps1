[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Dango'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Dango.lnk'),
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'local-install\Dango.LocalInstall.psm1') -Force
# Detect a conflicting desktop entry before installing or switching anything.
$module = Get-Module Dango.LocalInstall
& $module {
    param($root, $path)
    Assert-DangoShortcutAvailable (Get-DangoFullPath $root) (Get-DangoFullPath $path) 'Start'
    if (Test-Path -LiteralPath $root) {
        Assert-DangoShortcutAvailable (Get-DangoFullPath $root) (Join-Path (Get-DangoFullPath $root) 'Rollback.lnk') 'Rollback'
    }
} $InstallRoot $ShortcutPath
$result = Install-DangoPackage -PackagePath $PackagePath -InstallRoot $InstallRoot -StageOnly
New-DangoShortcut -InstallRoot $InstallRoot -ShortcutPath $ShortcutPath
New-DangoShortcut -InstallRoot $InstallRoot -ShortcutPath (Join-Path $InstallRoot 'Rollback.lnk') -Action Rollback
if (-not $StageOnly) {
    $null = Set-DangoActiveVersion -InstallRoot $InstallRoot -VersionId $result.VersionId
    $result.StagedOnly = $false
}
$result
Get-DangoInstallState -InstallRoot $InstallRoot

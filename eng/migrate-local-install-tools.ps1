[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallRoot,
    [ValidateSet('Migrate', 'Recover')][string]$Action = 'Migrate'
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'local-install\Dango.LocalInstall.psm1') -Force
switch ($Action) {
    'Migrate' { Update-DangoManagementTools -InstallRoot $InstallRoot | ConvertTo-Json }
    'Recover' { Restore-DangoManagementToolsMigration -InstallRoot $InstallRoot | ConvertTo-Json }
}

[CmdletBinding()]
param(
    [ValidateSet('Status', 'Start', 'Activate', 'Rollback')][string]$Action = 'Status',
    [string]$InstallRoot,
    [string]$VersionId,
    [string]$AcceptanceSettingsRoot,
    [switch]$ShowErrors
)
$ErrorActionPreference = 'Stop'
try {
    # Windows PowerShell 5.1 does not reliably populate PSScriptRoot in parameter defaults.
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) { $InstallRoot = Split-Path -Parent $PSScriptRoot }
    Import-Module (Join-Path $PSScriptRoot 'Dango.LocalInstall.psm1') -Force
    switch ($Action) {
        'Status' { Get-DangoInstallState -InstallRoot $InstallRoot | ConvertTo-Json }
        'Start' {
            $startParameters = @{ InstallRoot = $InstallRoot }
            if ($PSBoundParameters.ContainsKey('AcceptanceSettingsRoot')) { $startParameters.AcceptanceSettingsRoot = $AcceptanceSettingsRoot }
            Start-DangoInstalled @startParameters | ConvertTo-Json
        }
        'Activate' { Set-DangoActiveVersion -InstallRoot $InstallRoot -VersionId $VersionId | ConvertTo-Json }
        'Rollback' {
            $state = Undo-DangoUpdate -InstallRoot $InstallRoot
            $state | ConvertTo-Json
            if ($ShowErrors) {
                Add-Type -AssemblyName PresentationFramework
                $null = [Windows.MessageBox]::Show('已切换到上一版。双击桌面 Dango 启动，已保存的设置会保留。', 'Dango')
            }
        }
    }
    exit 0
}
catch {
    if ($ShowErrors) {
        Add-Type -AssemblyName PresentationFramework
        $detail = $_.Exception.Message
        $message = switch -Regex ($detail) {
            'Dango is running|Another Dango version is running' { '团子还在运行。请先从托盘菜单选择“退出”，再试一次。'; break }
            'no previous version' { '当前没有可回退的上一版。'; break }
            'No version is active' { '新版本已准备好，但还没有启用。请完成首次版本切换。'; break }
            'integrity|Missing package|Protected package|does not match|unlisted' { '程序文件不完整或已发生变化，本次操作已停止。请重新准备完整版本。'; break }
            'operation is in progress' { 'Dango 正在启动或切换版本，请稍后再试。'; break }
            default { 'Dango 未能完成本次操作：' + [Environment]::NewLine + $detail }
        }
        $null = [Windows.MessageBox]::Show($message, 'Dango', 'OK', 'Warning')
    }
    else { Write-Error -ErrorRecord $_ -ErrorAction Continue }
    exit 1
}

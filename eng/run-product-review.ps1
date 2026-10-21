param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][ValidateSet('face', 'local-room', 'desktop-pet-room')][string]$Scenario,
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskBuild = Join-Path $taskRepo "artifacts/product-review/$Scenario/$RunName"
if (Test-Path -LiteralPath $taskBuild) { throw 'Use a fresh RunName; existing artifacts and data are preserved.' }
for ($taskCursor = $taskBuild; $taskCursor; $taskCursor = Split-Path -Parent $taskCursor) {
    if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Product review paths cannot traverse links.'
    }
}
$taskProtected = Join-Path $taskRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskProtectedHash = (Get-FileHash -LiteralPath $taskProtected).Hash
try {
    & dotnet build (Join-Path $taskRepo 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release --artifacts-path $taskBuild
    if ($LASTEXITCODE -ne 0) { throw 'Default product build failed.' }
    $taskExe = Join-Path $taskBuild 'bin/Dango.Desktop/release/Dango.Desktop.exe'
    foreach ($taskModule in @('dango_belt_product.dll', 'dango_belt_room.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path (Split-Path $taskExe) $taskModule))) { throw "Missing default product module: $taskModule" }
    }
    $taskData = Join-Path $taskBuild 'acceptance-data'
    New-Item -ItemType Directory -Path $taskData | Out-Null
    Set-Content -LiteralPath (Join-Path $taskData '.dango-local-install-acceptance.json') -Encoding utf8 -Value '{"application":"Dango","acceptanceFormat":1,"fictional":true}'
    [ordered]@{ scenario = $Scenario; executable = $taskExe; arguments = @('--local-install-acceptance-settings', $taskData); humanAcceptance = 'pending' } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskBuild 'launch.json') -Encoding utf8
    Write-Output "Default product executable: $taskExe"
    Write-Output "Owned acceptance data: $taskData"
    Write-Output 'Use the product home and real porch doors to enter the requested scenario.'
    if ($Show) {
        # -Show explicitly requests a visible product window; obtain screen authorization before invoking it.
        $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @('--local-install-acceptance-settings', ('"{0}"' -f $taskData)) -PassThru
        Write-Output "Owned product PID: $($taskProcess.Id)"
    }
} finally {
    if ((Get-FileHash -LiteralPath $taskProtected).Hash -ne $taskProtectedHash) { throw 'Protected source DLL changed.' }
}

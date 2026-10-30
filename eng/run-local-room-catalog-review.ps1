param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProtected = Join-Path $taskRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskProtectedHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'
if ((Get-FileHash -LiteralPath $taskProtected).Hash -ne $taskProtectedHash) { throw 'Protected source DLL differs.' }
$taskBuild = Join-Path $taskRepo "artifacts/local-room-catalog/review-$RunName"
$taskRoot = Join-Path $taskRepo "artifacts/local-room-catalog/runs/$RunName"
$taskImportRoot = "$taskRoot-import"
$taskUiRoot = "$taskRoot-ui"
foreach ($taskTarget in @($taskBuild, $taskRoot, $taskImportRoot, $taskUiRoot)) {
    if (Test-Path -LiteralPath $taskTarget) { throw 'Use a fresh RunName; existing artifacts and data are preserved.' }
    for ($taskCursor = $taskTarget; $taskCursor; $taskCursor = Split-Path -Parent $taskCursor) {
        if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Acceptance paths cannot traverse links.' }
    }
}
$taskHead = (& git -C $taskRepo rev-parse HEAD).Trim()
function Get-TaskSourceStamp {
    $taskPaths = @(& git -c core.quotepath=false -C $taskRepo ls-files --cached --others --exclude-standard -- src eng tests Dango.slnx Directory.Build.props Directory.Build.targets global.json .editorconfig)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory source.' }
    $taskInventory = @(
        foreach ($taskRelative in ($taskPaths | Sort-Object -Unique)) {
            $taskSource = Join-Path $taskRepo $taskRelative
            if (Test-Path -LiteralPath $taskSource -PathType Leaf) { [ordered]@{ path=$taskRelative.Replace('\','/'); sha256=(Get-FileHash -LiteralPath $taskSource).Hash } }
        }
    )
    $taskCanonical = ConvertTo-Json -InputObject $taskInventory -Compress
    [ordered]@{ files=$taskInventory; sha256=[Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($taskCanonical))) }
}
$taskSourceBefore = Get-TaskSourceStamp
& dotnet build (Join-Path $taskRepo 'eng/tools/Dango.LocalRoomProbe/Dango.LocalRoomProbe.csproj') -c Release --artifacts-path $taskBuild
if ($LASTEXITCODE -ne 0) { throw 'Default product probe build failed.' }
$taskModule = Join-Path $taskBuild 'bin/Dango.LocalRoomProbe/release/dango_belt_room.dll'
$taskExpected = (Get-FileHash -LiteralPath $taskModule).Hash
$taskSourceAfter = Get-TaskSourceStamp
if ($taskSourceBefore.sha256 -ne $taskSourceAfter.sha256) { throw 'Source changed during candidate build; this run is not acceptance evidence.' }
$taskSourceBefore | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskBuild 'source-snapshot.json') -Encoding utf8
$taskExe = Join-Path $taskBuild 'bin/Dango.LocalRoomProbe/release/Dango.LocalRoomProbe.exe'
$taskReport = [ordered]@{ baseHead=$taskHead; sourceSha256=$taskSourceBefore.sha256; nativeSha256=$taskExpected; protectedSha256=$taskProtectedHash; checks=@(); ownedProcessIds=@(); manualProcessId=$null; ports=@(); dataRetained=$true }
$taskChildren = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
function Invoke-TaskProbe([string[]]$ProbeArguments) {
    & $taskExe @ProbeArguments
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $($ProbeArguments[0])" }
    $taskReport.checks += $ProbeArguments[0]
}
function Start-TaskProbe([string]$Mode, [string]$Root, [string]$Extra, [string]$LogName) {
    $taskArguments = @($Mode, ('"{0}"' -f $Root))
    if ($Extra) { $taskArguments += ('"{0}"' -f $Extra) }
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskBuild "$LogName.out.log") -RedirectStandardError (Join-Path $taskBuild "$LogName.err.log")
    $taskChildren.Add($taskProcess)
    $taskReport.ownedProcessIds += $taskProcess.Id
    return $taskProcess
}
function Wait-TaskMarker([string]$MarkerPath) {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $MarkerPath) -and [DateTime]::UtcNow -lt $taskDeadline) { Start-Sleep -Milliseconds 25 }
    if (!(Test-Path -LiteralPath $MarkerPath)) { throw "Probe marker timed out: $([System.IO.Path]::GetFileName($MarkerPath))" }
}
try {
    Invoke-TaskProbe @('init', $taskRoot)
    Invoke-TaskProbe @('write', $taskRoot)
    # Each invocation above and below is a separate executable process that has exited.
    Invoke-TaskProbe @('read', $taskRoot)
    $taskRaceA = Start-TaskProbe 'race' $taskRoot 'a' 'race-a'
    $taskRaceB = Start-TaskProbe 'race' $taskRoot 'b' 'race-b'
    Wait-TaskMarker (Join-Path $taskRoot 'a.ready'); Wait-TaskMarker (Join-Path $taskRoot 'b.ready')
    Set-Content -LiteralPath (Join-Path $taskRoot 'race.go') -Value 'go'
    foreach ($taskChild in @($taskRaceA, $taskRaceB)) {
        if (!$taskChild.WaitForExit(15000)) { throw 'Concurrent probe did not terminate.' }
    }
    if (@($taskRaceA, $taskRaceB | Where-Object ExitCode -eq 0).Count -ne 1 -or
        @($taskRaceA, $taskRaceB | Where-Object ExitCode -eq 3).Count -ne 1) { throw 'Concurrent last-slot result was not one success and one refusal.' }
    Invoke-TaskProbe @('read-race', $taskRoot)
    $taskCheckpoint = Start-TaskProbe 'checkpoint-wait' $taskRoot '' 'checkpoint'
    Wait-TaskMarker (Join-Path $taskRoot 'checkpoint.ready')
    Stop-Process -Id $taskCheckpoint.Id; $taskCheckpoint.WaitForExit()
    Invoke-TaskProbe @('read-checkpoint', $taskRoot)
    Invoke-TaskProbe @('init', $taskImportRoot)
    Invoke-TaskProbe @('seed-legacy', $taskImportRoot)
    $taskImport = Start-TaskProbe 'pause-import' $taskImportRoot '' 'import-interruption'
    Wait-TaskMarker (Join-Path $taskImportRoot 'import.ready')
    Stop-Process -Id $taskImport.Id; $taskImport.WaitForExit()
    Invoke-TaskProbe @('read-import', $taskImportRoot)
    Invoke-TaskProbe @('init', $taskUiRoot)
    Invoke-TaskProbe @('write', $taskUiRoot)
    Invoke-TaskProbe @('ui-check', $taskUiRoot, $taskModule)
    if ($Show) {
        $taskManual = Start-TaskProbe 'show' $taskUiRoot $taskModule 'manual'
        $taskReport.manualProcessId = $taskManual.Id
    }
    $taskReport['passed'] = $true
} finally {
    foreach ($taskChild in $taskChildren) {
        if (!$taskChild.HasExited -and $taskChild.Id -ne $taskReport.manualProcessId) { Stop-Process -Id $taskChild.Id; $taskChild.WaitForExit() }
    }
    if ((Get-FileHash -LiteralPath $taskProtected).Hash -ne $taskProtectedHash) { throw 'Protected source DLL changed.' }
    $taskReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskBuild 'verification.json') -Encoding utf8
    Write-Output (Join-Path $taskBuild 'verification.json')
}

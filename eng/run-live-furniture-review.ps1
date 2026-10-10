param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][string]$NativeModule,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedCandidateSha256,
    [switch]$NoLaunch,
    [switch]$Verify,
    [switch]$Observe
)

$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskModule = (Resolve-Path -LiteralPath $NativeModule).Path
$taskExpectedHash = $ExpectedCandidateSha256.ToUpperInvariant()
$taskProtectedModule = Join-Path $taskRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskProtectedHash = (Get-FileHash -LiteralPath $taskProtectedModule -Algorithm SHA256).Hash
if ($taskProtectedHash -ne '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A') { throw 'Protected production DLL differs from the frozen source.' }
if ($taskModule -eq $taskProtectedModule) { throw 'Use the isolated candidate module.' }
$taskBuild = Join-Path $taskRepo "artifacts/furniture/$RunName"
if (Test-Path -LiteralPath $taskBuild) { throw 'Use a fresh RunName; existing artifacts are preserved.' }
$taskCheck = [System.IO.File]::Open($taskModule, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
$taskCheck.Dispose()
$taskLock = [System.IO.File]::Open($taskModule, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    if ((Get-FileHash -LiteralPath $taskModule -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'Candidate SHA-256 differs.' }
    & dotnet build (Join-Path $taskRepo 'eng/tools/Dango.JointReview/Dango.JointReview.csproj') -c Release --artifacts-path $taskBuild "-p:DangoPhysicsModulePath=$taskModule"
    if ($LASTEXITCODE -ne 0) { throw 'Live furniture build failed.' }
    $taskExe = Join-Path $taskBuild 'bin/Dango.JointReview/release/Dango.JointReview.exe'
    $taskCopiedModule = Join-Path (Split-Path -Parent $taskExe) 'dango_physics_module.dll'
    if ((Get-FileHash -LiteralPath $taskCopiedModule -Algorithm SHA256).Hash -ne $taskExpectedHash -or
        (Get-FileHash -LiteralPath $taskModule -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'Candidate copy/source differs.' }
}
finally {
    $taskLock.Dispose()
    if ((Get-FileHash -LiteralPath $taskProtectedModule -Algorithm SHA256).Hash -ne $taskProtectedHash) { throw 'Protected production DLL changed.' }
}
$taskData = Join-Path $taskBuild 'data'
New-Item -ItemType Directory -Path $taskData | Out-Null
[ordered]@{ purpose = 'live-furniture-local-acceptance'; schema = 1; owner = [guid]::NewGuid().ToString() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'owner.json') -Encoding utf8
[ordered]@{
    schema = 1; approvedSource = 'cba27b9'; candidateSha256 = $taskExpectedHash
    protectedDllSha256 = $taskProtectedHash; scope = 'single-player live editing; existing platform physics'
    executable = 'bin/Dango.JointReview/release/Dango.JointReview.exe'; data = 'data'
    humanAcceptance = 'pending'; hardwareInput = 'not verified by automated gates'; newRoundModel = 'not integrated'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBuild 'candidate-manifest.json') -Encoding utf8

if ($Verify) {
    $taskVerification = Join-Path $taskBuild 'verification'
    $taskArgs = @('--furniture-layout-verify', ('"{0}"' -f $taskCopiedModule), ('"{0}"' -f $taskVerification))
    $taskVerifier = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -PassThru -WindowStyle Hidden
    $taskVerifier.WaitForExit()
    if ($taskVerifier.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $taskVerification 'verification-summary.json'))) {
        throw "Real file/native verification failed (exit $($taskVerifier.ExitCode))."
    }
    Write-Output "Verification summary: $(Join-Path $taskVerification 'verification-summary.json')"
}
Write-Output "Acceptance executable: $taskExe"
Write-Output "Candidate SHA-256: $taskExpectedHash"
Write-Output "Owned data: $taskData"
if (!$NoLaunch) {
    $taskMode = if ($Observe) { '--furniture-layout-smoke' } else { '--furniture-layout' }
    $taskEvidence = Join-Path $taskBuild 'window-evidence'
    $taskArgs = @($taskMode, ('"{0}"' -f $taskCopiedModule), ('"{0}"' -f $taskData), ('"{0}"' -f $taskEvidence))
    # The user explicitly authorized the visible independent acceptance window.
    $taskWindow = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -PassThru
    Write-Output "Furniture acceptance PID: $($taskWindow.Id)"
}

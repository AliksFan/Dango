param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][string]$NativeModule,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedCandidateSha256,
    [switch]$NoLaunch,
    [switch]$Observe,
    [switch]$WindowCheck
)

$ErrorActionPreference = 'Stop'
if ($Observe -and $WindowCheck) { throw 'Observe and WindowCheck use different drivers; select only one.' }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$modulePath = (Resolve-Path -LiteralPath $NativeModule).Path
if (!(Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw 'NativeModule must be an existing round-ball candidate DLL.' }
$expectedHash = $ExpectedCandidateSha256.ToUpperInvariant()
$protectedLibrary = Join-Path $repoRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$protectedHash = (Get-FileHash -LiteralPath $protectedLibrary -Algorithm SHA256).Hash
if ($protectedHash -ne '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A') { throw 'Protected library differs.' }
if ($modulePath -eq $protectedLibrary) { throw 'Use an isolated round-ball module candidate, not the protected production library.' }
$buildRoot = Join-Path $repoRoot "artifacts/furniture/$RunName"
if (Test-Path -LiteralPath $buildRoot) { throw 'Use a fresh RunName. Existing outputs are preserved.' }

# Never stop an existing review process or reuse its loaded DLL.
$candidateCheck = [System.IO.File]::Open($modulePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
$candidateCheck.Dispose()
$candidateLock = [System.IO.File]::Open($modulePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Candidate SHA-256 differs from the explicit expected hash.' }
    [System.IO.Directory]::CreateDirectory($buildRoot) | Out-Null
    & dotnet build (Join-Path $repoRoot 'eng/tools/Dango.JointReview/Dango.JointReview.csproj') -c Release --artifacts-path $buildRoot "-p:DangoPhysicsModulePath=$modulePath" 2>&1 | Tee-Object -FilePath (Join-Path $buildRoot 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Round-ball review build failed; the failed build log is preserved.' }
    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Candidate source changed.' }
    $exe = Join-Path $buildRoot 'bin/Dango.JointReview/release/Dango.JointReview.exe'
    $copiedModule = Join-Path (Split-Path -Parent $exe) 'dango_physics_module.dll'
    if ((Get-FileHash -LiteralPath $copiedModule -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Review module copy differs.' }
}
finally {
    $candidateLock.Dispose()
    if ((Get-FileHash -LiteralPath $protectedLibrary -Algorithm SHA256).Hash -ne $protectedHash) { throw 'Protected library changed.' }
}

$sourceNames = @('eng/tools/Dango.JointReview/RoundBallSession.cs', 'eng/tools/Dango.JointReview/RoundBallWindow.cs', 'eng/tools/Dango.JointReview/RoundBallWindowDiagnostics.cs', 'eng/tools/Dango.JointReview/Program.cs', 'eng/run-round-ball-review.ps1')
$sourceHashes = foreach ($sourceName in $sourceNames) {
    [pscustomobject]@{ Path = $sourceName; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot $sourceName) -Algorithm SHA256).Hash }
}
$reviewProcess = $null
$evidence = $null
if (!$NoLaunch) {
    $arguments = @('--round-ball', ('"{0}"' -f $copiedModule))
    if ($Observe) {
        $evidence = Join-Path $buildRoot 'observation'
        $arguments = @('--round-ball-smoke', ('"{0}"' -f $copiedModule), ('"{0}"' -f $evidence))
    }
    if ($WindowCheck) {
        $evidence = Join-Path $buildRoot 'window-check'
        $arguments = @('--round-ball-window-check', ('"{0}"' -f $copiedModule), ('"{0}"' -f $evidence))
    }
    # This is the explicitly authorized visible, independent acceptance window.
    $reviewProcess = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
}
$manifest = [ordered]@{
    SchemaVersion = 1
    SourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    NativeModuleSha256 = $expectedHash
    CopiedNativeModuleSha256 = (Get-FileHash -LiteralPath $copiedModule -Algorithm SHA256).Hash
    ReviewAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $exe) 'Dango.JointReview.dll') -Algorithm SHA256).Hash
    ProtectedLibraryBeforeSha256 = $protectedHash
    ProtectedLibraryAfterSha256 = (Get-FileHash -LiteralPath $protectedLibrary -Algorithm SHA256).Hash
    SourceHashes = @($sourceHashes)
    Launched = !$NoLaunch
    Observation = [bool]$Observe
    ExternalWindowCheck = [bool]$WindowCheck
    ProcessId = if ($null -ne $reviewProcess) { $reviewProcess.Id } else { $null }
    ObservationEvidence = $evidence
    ManualAcceptance = 'Not performed by build or launch'
}
[System.IO.File]::WriteAllText((Join-Path $buildRoot 'launch-manifest.json'), ($manifest | ConvertTo-Json -Depth 5))
Write-Output "Review executable: $exe"
Write-Output "Candidate SHA-256: $expectedHash"
if ($null -ne $reviewProcess) { Write-Output "Round-ball review PID: $($reviewProcess.Id)" }
if ($null -ne $evidence) { Write-Output "Ten-minute observation evidence: $evidence" }

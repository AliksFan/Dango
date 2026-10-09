param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [Parameter(Mandatory)][string]$NativeModule,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedCandidateSha256,
    [switch]$NoLaunch,
    [switch]$Observe
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$modulePath = (Resolve-Path -LiteralPath $NativeModule).Path
if (!(Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw 'NativeModule must be an existing candidate DLL.' }
$expectedHash = $ExpectedCandidateSha256.ToUpperInvariant()
$protectedLibrary = Join-Path $repoRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$protectedHash = (Get-FileHash -LiteralPath $protectedLibrary -Algorithm SHA256).Hash
if ($protectedHash -ne '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A') { throw 'Protected library differs.' }
if ($modulePath -eq $protectedLibrary) { throw 'Use an isolated physics-module candidate, not the protected production library.' }
$buildRoot = Join-Path $repoRoot "artifacts/furniture/$RunName"
if (Test-Path -LiteralPath $buildRoot) { throw 'Use a fresh RunName. Existing outputs are preserved.' }

# Reject a candidate already opened by another process; never stop that process.
$candidateCheck = [System.IO.File]::Open($modulePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
$candidateCheck.Dispose()
# Keep the source read-only while MSBuild copies it into the new isolated output.
$candidateLock = [System.IO.File]::Open($modulePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Candidate SHA-256 differs from the explicit expected hash.' }
    & dotnet build (Join-Path $repoRoot 'eng/tools/Dango.JointReview/Dango.JointReview.csproj') -c Release --artifacts-path $buildRoot "-p:DangoPhysicsModulePath=$modulePath"
    if ($LASTEXITCODE -ne 0) { throw 'Static-platform review build failed.' }
    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Candidate source changed.' }
    $exe = Join-Path $buildRoot 'bin/Dango.JointReview/release/Dango.JointReview.exe'
    $copiedModule = Join-Path (Split-Path -Parent $exe) 'dango_physics_module.dll'
    if ((Get-FileHash -LiteralPath $copiedModule -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Review module copy differs.' }
}
finally {
    $candidateLock.Dispose()
    if ((Get-FileHash -LiteralPath $protectedLibrary -Algorithm SHA256).Hash -ne $protectedHash) { throw 'Protected library changed.' }
}

Write-Output "Review executable: $exe"
Write-Output "Candidate SHA-256: $expectedHash"
if (!$NoLaunch) {
    $arguments = @('--static-platform', ('"{0}"' -f $copiedModule))
    if ($Observe) {
        $evidence = Join-Path $buildRoot 'observation'
        $arguments = @('--static-platform-smoke', ('"{0}"' -f $copiedModule), ('"{0}"' -f $evidence))
    }
    # The user explicitly authorized this visible independent acceptance window.
    $reviewProcess = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    Write-Output "Static-platform review PID: $($reviewProcess.Id)"
    if ($Observe) { Write-Output "Ten-minute observation evidence: $evidence" }
}

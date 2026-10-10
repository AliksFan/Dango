[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Box2DSourceRoot,
    [string]$CMakePath,
    [string]$ArtifactRoot,
    [string]$NativeSourceRoot,
    [switch]$BuildOnly,
    [switch]$LongOnly,
    [switch]$NegativesOnly
)
$ErrorActionPreference = 'Stop'
if ($LongOnly -and $NegativesOnly) { throw 'LongOnly and NegativesOnly are mutually exclusive.' }
if (-not $CMakePath) {
    $cmakeCommand = Get-Command cmake -CommandType Application -ErrorAction SilentlyContinue
    if ($cmakeCommand) { $CMakePath = $cmakeCommand.Source }
    else {
        $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path -LiteralPath $vswherePath) {
            $CMakePath = @(& $vswherePath -latest -products '*' -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe') | Select-Object -First 1
        }
    }
    if (-not $CMakePath) { throw 'CMake was not found. Add it to PATH or pass -CMakePath.' }
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $repositoryRoot 'artifacts/p1-followup/replay' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
$previousManifest = if ($NegativesOnly) {
    Get-Content -LiteralPath (Join-Path $ArtifactRoot 'build-manifest.json') -Raw | ConvertFrom-Json
}
if (-not $NativeSourceRoot) {
    $archive = Join-Path $ArtifactRoot 'ce68521-native.zip'
    $frozenRoot = Join-Path $ArtifactRoot 'frozen-ce68521'
    if (-not (Test-Path -LiteralPath $archive)) {
        & git -C $repositoryRoot archive --format=zip "--output=$archive" ce68521bdd5765c67f9e3de97cd4376e5c34b1bb eng/native/box2d
        if ($LASTEXITCODE -ne 0) { throw 'Frozen native archive failed.' }
    }
    if (-not (Test-Path -LiteralPath $frozenRoot)) { Expand-Archive -LiteralPath $archive -DestinationPath $frozenRoot }
    $NativeSourceRoot = Join-Path $frozenRoot 'eng/native/box2d'
}
$buildRoot = Join-Path $ArtifactRoot 'build'
& $CMakePath -S $PSScriptRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 "-DBOX2D_SOURCE_DIR=$Box2DSourceRoot" "-DDANGO_NATIVE_SOURCE_DIR=$NativeSourceRoot" *> (Join-Path $ArtifactRoot 'configure.log')
if ($LASTEXITCODE -ne 0) { throw 'Replay CMake configure failed. See configure.log.' }
& $CMakePath --build $buildRoot --config Release --target dango_physics_replay_probe *> (Join-Path $ArtifactRoot 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Replay CMake build failed. See build.log.' }
$probe = Join-Path $buildRoot 'Release/dango_physics_replay_probe.exe'
$buildIdentity = Get-Content -LiteralPath (Join-Path $buildRoot 'build-identity.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $buildRoot 'build-fingerprint.txt') -Algorithm SHA256).Hash -ne $buildIdentity.BuildId) { throw 'Build identity does not match its actual fingerprint.' }
$manifest = [ordered]@{
    DefaultExtractedBaselineCommit = 'ce68521bdd5765c67f9e3de97cd4376e5c34b1bb'
    NativeSourceOverrideUsed = [bool]$PSBoundParameters.ContainsKey('NativeSourceRoot')
    BuildIdentity = $buildIdentity
    BuildFingerprint = Get-Content -LiteralPath (Join-Path $buildRoot 'build-fingerprint.txt') -Raw
    Box2DSourceManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $buildRoot 'box2d-source-manifest.json') -Algorithm SHA256).Hash
    ExecutableSha256 = (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash
    NativeCollisionSha256 = (Get-FileHash -LiteralPath (Join-Path $NativeSourceRoot 'dango_collision_review.c') -Algorithm SHA256).Hash
    Box2DCommit = '8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3'
    DtBits = '0x3C888865'
    HostOs = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    HostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    ParallelLoadControlled = $false
    Real600SecondObservation = $false
    ProductionCheckpointSupported = $false
    LinuxVerified = $false
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'build-manifest.json') -Encoding utf8
if ($BuildOnly) { $manifest | ConvertTo-Json -Depth 8; return }
if ($NegativesOnly -and ($previousManifest.ExecutableSha256 -ne $manifest.ExecutableSha256 -or
    $previousManifest.BuildIdentity.BuildId -ne $buildIdentity.BuildId)) { throw 'NegativesOnly requires the identical prior executable and build identity.' }

function Invoke-ReplayChild {
    param([string]$Name, [string[]]$Arguments, [int]$ExpectedExit, [int]$TimeoutSeconds = 300)
    $stdout = Join-Path $ArtifactRoot ($Name+'.jsonl')
    $stderr = Join-Path $ArtifactRoot ($Name+'.stderr.txt')
    if ((Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash -ne $manifest.ExecutableSha256) { throw 'Replay executable changed during this run.' }
    # Exact executable and argument array; no shell and no persistent resource.
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $probe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $processId = $process.Id
    $outTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
        $process.Kill()
        $process.WaitForExit()
        $outTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdout -Encoding utf8
        $errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderr -Encoding utf8
        $process.Dispose()
        throw "Isolated replay child exceeded timeout: $Name ($processId)."
    }
    $exitCode = $process.ExitCode
    $output = $outTask.GetAwaiter().GetResult()
    $output | Set-Content -LiteralPath $stdout -Encoding utf8
    $errorTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderr -Encoding utf8
    $process.Dispose()
    $result = $output.Trim() | ConvertFrom-Json
    if ($result.BuildId -ne $buildIdentity.BuildId) { throw "Build identity mismatch: $Name" }
    if ($result.ProcessId -ne $processId -or -not $result.ResourcesReturned -or $result.OwnerThreadViolation) { throw "PID/resource/owner evidence failed: $Name" }
    if ($ExpectedExit -ge 0 -and $exitCode -ne $ExpectedExit) { throw "Expected exit $ExpectedExit, got $exitCode for $Name" }
    [pscustomobject]@{Name=$Name;ProcessId=$processId;ExitCode=$exitCode;Exited=$true;Result=$result}
}

$evidence = [Collections.Generic.List[object]]::new()
if ($NegativesOnly) {
    $priorEvidence = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Raw | ConvertFrom-Json
    foreach ($prior in $priorEvidence | Where-Object { $_.Name -notlike 'mutate-*' -and $_.Name -notlike 'negative-*' }) {
        if ($prior.Result.BuildId -ne $buildIdentity.BuildId -or -not $prior.Exited) { throw 'Prior normal evidence has a different build or incomplete child.' }
        $evidence.Add($prior)
    }
    if (@($evidence | Where-Object { $_.Name -like '*-replay' }).Count -ne 9) { throw 'NegativesOnly requires all nine prior normal fixture results.' }
}
$fixtures = if ($NegativesOnly) { @() } elseif ($LongOnly) { @('history-600s','entropy-600s') } else {
    @('floor','dynamic-collision','stack','airborne','removed-lower','topology-boundaries-reset','stop','history-600s','entropy-600s')
}
foreach ($fixture in $fixtures) {
    $journal = Join-Path $ArtifactRoot ($fixture+'.journal')
    $oracle = Join-Path $ArtifactRoot ($fixture+'.oracle')
    $capture = Invoke-ReplayChild ($fixture+'-capture') @('capture',$fixture,$journal,$oracle) 0
    $evidence.Add($capture)
    $replayArguments = @('replay',$journal,$oracle)
    if ($fixture -eq 'entropy-600s') { $replayArguments += '--oversize-diagnostic' }
    # Timing/budget failures are measured red gates, not a harness exception.
    $replayed = Invoke-ReplayChild ($fixture+'-replay') $replayArguments -1
    if ($capture.ProcessId -eq $replayed.ProcessId -or -not $replayed.Result.DifferentProcess) { throw "Not two distinct OS processes: $fixture" }
    $evidence.Add($replayed)
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Encoding utf8
}
if (-not $LongOnly) {
    $variants = @(
        @('airborne','missing-jump','oracle-initial-dynamics-semantics'),
        @('stop','lost-stop','oracle-initial-dynamics-semantics'),
        @('dynamic-collision','add-order','record-add-order'),
        @('floor','tick','record-ordinal-tick-segment'),
        @('floor','segment','record-recovery-boundary'),
        @('floor','version','journal-version-config-build'),
        @('floor','build-id','journal-version-config-build'),
        @('floor','dt','journal-version-config-build'),
        @('floor','length','journal-version-config-build')
    )
    foreach ($variant in $variants) {
        $source = Join-Path $ArtifactRoot ($variant[0]+'.journal')
        $mutated = Join-Path $ArtifactRoot ('negative-'+$variant[1]+'.journal')
        $mutation = Invoke-ReplayChild ('mutate-'+$variant[1]) @('mutate',$source,$mutated,$variant[1]) 0
        $failed = Invoke-ReplayChild ('negative-'+$variant[1]) @('replay',$mutated,(Join-Path $ArtifactRoot ($variant[0]+'.oracle'))) 1
        if ($failed.Result.FailureReason -ne $variant[2]) { throw "Unexpected negative failure reason for $($variant[1]): $($failed.Result.FailureReason)" }
        $evidence.Add($mutation); $evidence.Add($failed)
        $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Encoding utf8
    }
}
$normal = @($evidence | Where-Object { $_.Name -like '*-replay' })
$negative = @($evidence | Where-Object { $_.Name -like 'negative-*' })
$scopeComplete = $normal.Count -eq 9 -and $negative.Count -eq 9
$summary = [ordered]@{
    Scope = 'isolated-same-build-replay-feasibility'
    AdmissionScopeComplete = $scopeComplete
    NormalReplayCount = $normal.Count
    ExpectedNegativeCount = $negative.Count
    ExpectedNegativesPassed = $negative.Count -eq 9 -and @($negative | Where-Object { $_.ExitCode -ne 1 }).Count -eq 0
    NormalReplayCorrectnessPassed = @($normal | Where-Object { -not $_.Result.CorrectnessPassed }).Count -eq 0
    NormalReplayBudgetPassed = @($normal | Where-Object { -not $_.Result.JournalBudgetPassed -or -not $_.Result.RestoreTimeBudgetPassed }).Count -eq 0
    BAdmitted = $scopeComplete -and @($normal | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0 -and @($negative | Where-Object { $_.ExitCode -ne 1 }).Count -eq 0
    ProcessCount = $evidence.Count
    AllProcessesExited = @($evidence | Where-Object { -not $_.Exited }).Count -eq 0
    AllOwnerThreadsSingle = @($evidence | Where-Object { $_.Result.OwnerThreadViolation }).Count -eq 0
    FullCheckpointSupported = $false
    Real600SecondObservation = $false
    LinuxVerified = $false
    ParallelLoadControlled = $false
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Encoding utf8
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'summary.json') -Encoding utf8
$summary | ConvertTo-Json
if (-not $summary.BAdmitted) { exit 1 }

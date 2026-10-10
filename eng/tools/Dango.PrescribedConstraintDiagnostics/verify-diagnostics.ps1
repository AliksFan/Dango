[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Box2DSourceRoot,
    [string]$CMakePath,
    [string]$ArtifactRoot,
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $repoRoot 'artifacts/p1-r0' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
if (Test-Path -LiteralPath (Join-Path $ArtifactRoot 'summary.json')) { throw 'Use a fresh artifact directory; previous results must be retained.' }
[void][IO.Directory]::CreateDirectory($ArtifactRoot)
$dependencyRoot = (Resolve-Path -LiteralPath $Box2DSourceRoot).Path
$dependencyManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/p1-followup/replay/build/box2d-source-manifest.json') -Raw | ConvertFrom-Json
foreach ($source in $dependencyManifest) {
    if ((Get-FileHash -LiteralPath (Join-Path $dependencyRoot $source.Path)).Hash -ne $source.Sha256) { throw "Dependency changed: $($source.Path)" }
}
if (@(Get-ChildItem -LiteralPath $dependencyRoot -Recurse -File).Count -ne $dependencyManifest.Count) { throw 'Dependency file set changed.' }
$protectedBinaries = @(
    @{ Path = 'artifacts/p1-followup/native-build/Release/dango_physics_module.dll'; Expected = '4E2B92B5CC73A6C586A7A9F8EFE96F03187AECBAC6A5975E34BFBC9BB7E32A68' },
    @{ Path = 'artifacts/authority-native/build/Release/dango_physics_module.dll'; Expected = 'C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A' },
    @{ Path = 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'; Expected = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A' },
    @{ Path = 'artifacts/p1-followup/replay/build/Release/dango_physics_replay_probe.exe'; Expected = 'B4707336E379A234F2C68B29B186342F253295AA2971475F5A0D22B29003A750' }
)
function Assert-ProtectedBinaries {
    foreach ($binary in $protectedBinaries) {
        if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $binary.Path)).Hash -ne $binary.Expected) { throw "Protected binary changed: $($binary.Path)" }
    }
}
Assert-ProtectedBinaries
if (-not $CMakePath) {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmakeCommand) { $CMakePath = $cmakeCommand.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        $installation = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
        if (-not $installation) { throw 'CMake not found; specify -CMakePath.' }
        $CMakePath = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    }
}
$buildRoot = Join-Path $ArtifactRoot 'build'
& $CMakePath -S $PSScriptRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 "-DBOX2D_SOURCE_DIR=$dependencyRoot" "-DDANGO_NATIVE_SOURCE_DIR=$(Join-Path $repoRoot 'eng/native/box2d')" *> (Join-Path $ArtifactRoot 'configure.log')
if ($LASTEXITCODE) { throw 'R0 configure failed; see configure.log.' }
& $CMakePath --build $buildRoot --config Release --parallel 2 *> (Join-Path $ArtifactRoot 'build.log')
if ($LASTEXITCODE) { throw 'R0 build failed; see build.log.' }
$nativeSources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'eng/native/box2d') -File | Where-Object { $_.Extension -in '.c','.h' } | Sort-Object Name | ForEach-Object { @{ Path = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
$toolSources = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Sort-Object Name | ForEach-Object { @{ Path = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
$executables = @(0..2 | ForEach-Object { $path = Join-Path $buildRoot "Release/dango_constraint_diagnostics_$_.exe"; @{ Variant = $_; Sha256 = (Get-FileHash -LiteralPath $path).Hash } })
@{ ContractHead = 'e036d42c1bd6cc45a94a15cbd84d49e2c1921117'; NativeBaselineHead = '6ae33f49336ef4141177e22c65585e2415106e5d'; Compiler = (Get-Content -LiteralPath (Join-Path $buildRoot 'compiler.json') -Raw | ConvertFrom-Json); Compile = 'Release; static runtime; /W4 /WX /fp:precise; /Brepro; BOX2D_VALIDATE=ON'; NativeSources = $nativeSources; ToolSources = $toolSources; DependencySources = $dependencyManifest; Executables = $executables; ProtectedBinaries = $protectedBinaries } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'build-manifest.json') -Encoding utf8
if ($BuildOnly) { return }
function Write-Pair([IO.BinaryWriter]$writer,$pair) {
    if (-not $pair) { foreach ($unused in 1..8) { $writer.Write([uint32]0) }; return }
    $writer.Write([int32]$pair.EntityA.Value); $writer.Write([int32]$pair.EntityB.Value)
    $flags = [uint32]0
    if ($pair.Touching) { $flags = $flags -bor 1 }
    if ($pair.SupportsA) { $flags = $flags -bor 2 }
    if ($pair.SupportsB) { $flags = $flags -bor 4 }
    $writer.Write($flags); $writer.Write([uint32]0)
    foreach ($field in 'NormalX','NormalY','Gap','MinimumProjectedWidth') { $writer.Write([single]$pair.$field) }
}
function Write-Fixture([string]$name,$steps,[int]$expectedError,[int]$expectedSubsteps) {
    $path = Join-Path $ArtifactRoot "$name.fixture"
    $writer = [IO.BinaryWriter]::new([IO.File]::Open($path,[IO.FileMode]::CreateNew))
    try {
        $writer.Write([uint32]0x30525044); $writer.Write([uint32]$steps.Count); $writer.Write([int32]$expectedError); $writer.Write([int32]$expectedSubsteps)
        foreach ($step in $steps) {
            if ($step.Bodies.Count -ne 2 -or $step.StartPairs.Count -gt 1 -or $step.EndPairs.Count -gt 1) { throw 'Fixture slots exceed ABI.' }
            $writer.Write([uint32]1); $writer.Write([uint32]248)
            $writer.Write([uint64]$step.Epoch); $writer.Write([uint64]$step.StepOrdinal)
            $writer.Write([double]$step.StartSampleTime); $writer.Write([double]$step.EndSampleTime)
            $writer.Write([uint64]$step.StartSceneRevision); $writer.Write([uint64]$step.EndSceneRevision)
            $writer.Write([int32]$step.Bodies.Count); $writer.Write([int32]$step.StartPairs.Count); $writer.Write([int32]$step.EndPairs.Count); $writer.Write([int32]0)
            foreach ($body in $step.Bodies) {
                $writer.Write([int32]$body.EntityId.Value); $writer.Write([uint32][bool]$body.StartFloorGrounded); $writer.Write([uint32][bool]$body.EndFloorGrounded)
                $writer.Write([int32]$body.StartSupportEntityId.Value); $writer.Write([int32]$body.EndSupportEntityId.Value); $writer.Write([int32]0)
                foreach ($endpoint in 'Start','End') { foreach ($field in 'CoreX','CoreY','CoreVelocityX','CoreVelocityY') { $writer.Write([single]$body.$endpoint.$field) } }
            }
            Write-Pair $writer $step.StartPairs[0]; Write-Pair $writer $step.EndPairs[0]
        }
    } finally { $writer.Dispose() }
    if ((Get-Item -LiteralPath $path).Length -ne 16+248*$steps.Count) { throw 'Fixture sizeof mismatch.' }
    return $path
}
$evidenceSources = @()
$fixturePaths = @{}
foreach ($name in 'stack','push') {
    $relative = "artifacts/p1-followup/semantic-prescribed-final2/$name-60hz.json"
    $sourcePath = Join-Path $repoRoot $relative
    $evidenceSources += @{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath $sourcePath).Hash }
    $report = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
    $steps = @($report.diagnostics | ForEach-Object { $_.prescribedInput })
    if ($name -eq 'stack') { $steps += $report.firstFailure.prescribedInput; $expectedError = -91; $expectedSubsteps = 5 }
    else { $expectedError = 0; $expectedSubsteps = 16 }
    $fixturePaths[$name] = Write-Fixture $name $steps $expectedError $expectedSubsteps
}
$overlapRelative = 'artifacts/p1-followup/semantic-prescribed-final2-counterexamples/deep-overlap-zero-velocity.json'
$overlapSource = Join-Path $repoRoot $overlapRelative
$evidenceSources += @{ Path = $overlapRelative; Sha256 = (Get-FileHash -LiteralPath $overlapSource).Hash }
$overlap = Get-Content -LiteralPath $overlapSource -Raw | ConvertFrom-Json
$fixturePaths['overlap'] = Write-Fixture 'overlap' @($overlap.prescribedInput) -422 16
@{ EvidenceSources = $evidenceSources; Fixtures = @($fixturePaths.GetEnumerator() | Sort-Object Name | ForEach-Object { @{ Name = $_.Key; Sha256 = (Get-FileHash -LiteralPath $_.Value).Hash; Bytes = (Get-Item -LiteralPath $_.Value).Length } }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'input-manifest.json') -Encoding utf8
$frozenDll = Join-Path $repoRoot $protectedBinaries[0].Path
$processEvidence = @()
foreach ($name in 'stack','push','overlap') {
    foreach ($variant in 0,1) {
        $prefix = Join-Path $ArtifactRoot "$name-$variant"
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $buildRoot "Release/dango_constraint_diagnostics_$variant.exe"))
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        foreach ($argument in @($fixturePaths[$name],$frozenDll,"$prefix.output.bin","$prefix.observations.bin","$prefix.failure.jsonl")) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start); $childId = $process.Id
        $stdout = $process.StandardOutput.ReadToEnd(); $stderr = $process.StandardError.ReadToEnd(); $process.WaitForExit()
        $stdout | Set-Content -LiteralPath "$prefix.stdout.log"; $stderr | Set-Content -LiteralPath "$prefix.stderr.log"
        $result = $stdout.Trim() | ConvertFrom-Json
        $processEvidence += @{ Name = "$name-$variant"; ProcessId = $childId; ExitCode = $process.ExitCode; Exited = $process.HasExited; Result = $result }
        if ($process.ExitCode -ne 0 -or -not $result.Success) { throw "R0 comparison failed: $name-$variant" }
        $process.Dispose()
    }
    if ((Get-FileHash -LiteralPath (Join-Path $ArtifactRoot "$name-0.output.bin")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $ArtifactRoot "$name-1.output.bin")).Hash) { throw "Observed raw state or trace differs: $name" }
}
$negativePrefix = Join-Path $ArtifactRoot 'mutation-2'
$negativeStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $buildRoot 'Release/dango_constraint_diagnostics_2.exe'))
$negativeStart.UseShellExecute = $false; $negativeStart.CreateNoWindow = $true; $negativeStart.WindowStyle = 'Hidden'
$negativeStart.RedirectStandardOutput = $true; $negativeStart.RedirectStandardError = $true
foreach ($argument in @($fixturePaths['stack'],$frozenDll,"$negativePrefix.output.bin","$negativePrefix.observations.bin","$negativePrefix.failure.jsonl")) { $negativeStart.ArgumentList.Add($argument) }
$negativeProcess = [Diagnostics.Process]::Start($negativeStart); $negativeId = $negativeProcess.Id
$negativeStdout = $negativeProcess.StandardOutput.ReadToEnd(); $negativeStderr = $negativeProcess.StandardError.ReadToEnd(); $negativeProcess.WaitForExit()
$negativeStdout | Set-Content -LiteralPath "$negativePrefix.stdout.log"; $negativeStderr | Set-Content -LiteralPath "$negativePrefix.stderr.log"
$negativeResult = $negativeStdout.Trim() | ConvertFrom-Json
$processEvidence += @{ Name = 'mutation-2'; ProcessId = $negativeId; ExitCode = $negativeProcess.ExitCode; Exited = $negativeProcess.HasExited; Result = $negativeResult }
if ($negativeProcess.ExitCode -ne 1 -or $negativeResult.ObserverMutations -lt 1 -or $negativeResult.Success) { throw 'Mutating observer was not rejected.' }
$negativeProcess.Dispose()
Assert-ProtectedBinaries
$processEvidence | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Encoding utf8
@{ DiagnosticValid = $true; ComparedWithFrozenDll = $true; NoObserverVsObserverRaw17BitwiseEqual = $true; MutatingObserverRejected = $true; Processes = $processEvidence.Count; AllExited = $true; P1Admitted = $false; RecoveryImplemented = $false; SolverChanged = $false; LinuxVerified = $false; UiStarted = $false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'summary.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $ArtifactRoot 'summary.json')

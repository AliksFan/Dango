[CmdletBinding()]
param([string]$ArtifactRoot,[string]$Box2DSourceRoot,[string]$CMakePath)
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if(-not $ArtifactRoot) { $ArtifactRoot=Join-Path $repoRoot 'artifacts/p1-r1' }
$ArtifactRoot=[IO.Path]::GetFullPath($ArtifactRoot)
if(Test-Path -LiteralPath (Join-Path $ArtifactRoot 'input-manifest.json')) { throw 'Use fresh artifacts; never replace prior evidence.' }
[void][IO.Directory]::CreateDirectory($ArtifactRoot)
$cache=Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/p1-followup/native-build/CMakeCache.txt')
if(-not $Box2DSourceRoot) { $Box2DSourceRoot=($cache | Where-Object { $_ -match '^BOX2D_SOURCE_DIR:[^=]+=' } | Select-Object -First 1) -replace '^[^=]+=','' }
if(-not $CMakePath) { $CMakePath=($cache | Where-Object { $_ -match '^CMAKE_COMMAND:[^=]+=' } | Select-Object -First 1) -replace '^[^=]+=','' }
$dependencyRoot=(Resolve-Path -LiteralPath $Box2DSourceRoot).Path
$dependencies=Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/p1-followup/replay/build/box2d-source-manifest.json') -Raw | ConvertFrom-Json
foreach($source in $dependencies) {
    if((Get-FileHash -LiteralPath (Join-Path $dependencyRoot $source.Path)).Hash -ne $source.Sha256) { throw "Dependency changed: $($source.Path)" }
}
if(@(Get-ChildItem -LiteralPath $dependencyRoot -Recurse -File).Count -ne $dependencies.Count) { throw 'Dependency file set changed.' }
$protected=@(
    @{Path='artifacts/p1-followup/native-build/Release/dango_physics_module.dll';Sha256='4E2B92B5CC73A6C586A7A9F8EFE96F03187AECBAC6A5975E34BFBC9BB7E32A68'},
    @{Path='artifacts/authority-native/build/Release/dango_physics_module.dll';Sha256='C7D3140079BEFD29E3E8512B5CF69376D4B342AD583A7C458EB89420A72FE07A'},
    @{Path='src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll';Sha256='6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'},
    @{Path='artifacts/p1-followup/replay/build/Release/dango_physics_replay_probe.exe';Sha256='B4707336E379A234F2C68B29B186342F253295AA2971475F5A0D22B29003A750'}
)
function Assert-Protected {
    foreach($file in $protected) {
        if((Get-FileHash -LiteralPath (Join-Path $repoRoot $file.Path)).Hash -ne $file.Sha256) { throw "Protected binary changed: $($file.Path)" }
    }
}
Assert-Protected
$observation=Join-Path $repoRoot 'artifacts/p1-r0/stack-1.observations.bin'
if((Get-FileHash -LiteralPath $observation).Hash -ne '65D48C97AEB515A306789C26BDEE124C328AF5FA59A15DDC1EF4BD5B097D9AD6') { throw 'R0 input changed.' }
$r0=Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/p1-r0/summary.json') -Raw | ConvertFrom-Json
if(-not $r0.DiagnosticValid) { throw 'R0 invalid.' }
$stream=[IO.File]::OpenRead($observation); $reader=[IO.BinaryReader]::new($stream)
$records=@()
try {
    if($stream.Length%416) { throw 'Unexpected record layout.' }
    while($stream.Position -lt $stream.Length) {
        $start=$stream.Position
        $tick=$reader.ReadUInt64();$call=$reader.ReadUInt64()
        $substep=$reader.ReadInt32();$entity=$reader.ReadInt32();$phase=$reader.ReadInt32()
        $stage=$reader.ReadInt32();$iteration=$reader.ReadInt32();$reserved=$reader.ReadInt32()
        if($tick -eq 42 -and $substep -eq 5 -and $entity -eq 1 -and $phase -eq 0) {
            if($reserved) { throw 'Reserved field not zero.' }
            $bytes=$reader.ReadBytes(372)
            if($bytes.Length -ne 372) { throw 'Incomplete state.' }
            $records+=@{Call=$call;Stage=$stage;Iteration=$iteration;Bytes=$bytes}
        }
        $stream.Position=$start+416
    }
} finally { $reader.Dispose() }
if($records.Count -ne 98 -or $records[0].Stage -ne 0 -or $records[0].Iteration -ne 0 -or
    $records[-1].Stage -ne 3 -or $records[-1].Iteration -ne 96 -or @($records.Call | Select-Object -Unique).Count -ne 1) { throw 'Wrong R0 call.' }
for($ordinal=1;$ordinal -le 96;$ordinal++) {
    if($records[$ordinal].Stage -ne 1 -or $records[$ordinal].Iteration -ne $ordinal) { throw 'Missing original sweep.' }
}
$entryPath=Join-Path $ArtifactRoot 'entry.bin';$exitPath=Join-Path $ArtifactRoot 'original-exit.bin'
[IO.File]::WriteAllBytes($entryPath,$records[0].Bytes)
[IO.File]::WriteAllBytes($exitPath,$records[-1].Bytes)
$inputs=@{ObservationSha256=(Get-FileHash $observation).Hash;Call=$records[0].Call;Tick=42;Substep=5;Entity=1;Phase=0;EntryStage=0;EntryBytes=372;
    EntrySha256=(Get-FileHash $entryPath).Hash;OriginalExitSha256=(Get-FileHash $exitPath).Hash;DependencyFiles=$dependencies.Count;Protected=$protected}
$inputs | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'input-manifest.json') -Encoding utf8
$buildRoot=Join-Path $ArtifactRoot 'build'
& $CMakePath -S $PSScriptRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 "-DBOX2D_SOURCE_DIR=$dependencyRoot" "-DDANGO_NATIVE_SOURCE_DIR=$(Join-Path $repoRoot 'eng/native/box2d')" *> (Join-Path $ArtifactRoot 'configure.log')
if($LASTEXITCODE) { throw 'R1 configure failed; inspect log.' }
& $CMakePath --build $buildRoot --config Release --parallel 2 *> (Join-Path $ArtifactRoot 'build.log')
if($LASTEXITCODE) { throw 'R1 build failed; inspect log.' }
$exe=Join-Path $buildRoot 'Release/dango_position_projection.exe'
@{ExecutableSha256=(Get-FileHash $exe).Hash;ToolSources=@(Get-ChildItem $PSScriptRoot -File | Sort-Object Name | ForEach-Object { @{Path=$_.Name;Sha256=(Get-FileHash $_.FullName).Hash} });
    OriginalHeaderSha256=(Get-FileHash (Join-Path $repoRoot 'eng/native/box2d/dango_self_constraints.h')).Hash;CandidateHeaderSha256=(Get-FileHash (Join-Path $repoRoot 'eng/native/box2d/dango_prescribed_position_projection.h')).Hash} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'build-manifest.json') -Encoding utf8
$processes=@()
foreach($variant in -1,0,1,2,3,4,5) {
    $prefix=Join-Path $ArtifactRoot "variant-$variant"
    $start=[Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @($entryPath,$exitPath,[string]$variant,$prefix)) { $start.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($start);$childId=$process.Id
    $stdout=$process.StandardOutput.ReadToEnd();$stderr=$process.StandardError.ReadToEnd();$process.WaitForExit()
    $stdout | Set-Content -LiteralPath "$prefix.stdout.log" -Encoding utf8
    $stderr | Set-Content -LiteralPath "$prefix.stderr.log" -Encoding utf8
    $result=$stdout.Trim() | ConvertFrom-Json
    $processes+=@{Variant=$variant;Pid=$childId;ExitCode=$process.ExitCode;Exited=$process.HasExited;CpuMilliseconds=$process.TotalProcessorTime.TotalMilliseconds;Result=$result;StateSha256=(Get-FileHash "$prefix.state.bin").Hash}
    $process.Dispose()
    $processes | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'process-evidence.json') -Encoding utf8
    if($variant -eq -1 -and (-not $result.Success -or $processes[-1].ExitCode -ne 0)) { throw 'Original failure was not reproduced bitwise; stop.' }
}
Assert-Protected
$baseline=$processes | Where-Object Variant -eq -1
$candidate=$processes | Where-Object Variant -eq 0
$negatives=@($processes | Where-Object Variant -gt 0)
$negativeGate=@($negatives | Where-Object { $_.ExitCode -ne 1 -or $_.Result.Success }).Count -eq 0
$summary=@{BaselineRedReproduced=$baseline.Result.Success;LocalCandidatePassed=($candidate.ExitCode -eq 0 -and $candidate.Result.Success);
    NegativeControlsRejected=$negativeGate;Processes=$processes.Count;AllExited=(@($processes | Where-Object { -not $_.Exited }).Count -eq 0);
    ProtectedBinariesUnchanged=$true;ToolModifiesProductionSource=$false;WorldsCreated=0;UiStarted=$false;P1Admitted=$false;P2Started=$false;
    StopBeforeIntegration=($candidate.ExitCode -ne 0 -or -not $negativeGate)}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'summary.json') -Encoding utf8
$processes | Select-Object Variant,ExitCode,@{n='Iterations';e={$_.Result.Iterations}},@{n='Residual';e={$_.Result.MaximumResidual}},@{n='Claimed';e={$_.Result.ClaimedSuccess}},@{n='Milliseconds';e={$_.Result.SolveMilliseconds}} | Format-Table | Out-String
$summary | ConvertTo-Json

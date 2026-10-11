[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolDllPath,
    [Parameter(Mandatory)][string]$NativeDllPath,
    [Parameter(Mandatory)][string]$NativeProbePath,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string[]]$Mutations = @('healthyReset','oldMove','oldJump','halfReady','skipSpawnOverlap','skipSpawnBounds','skipSpawnGround','oldEpoch','noReason','faultAutoRebirth','observerIgnored','earlyRelease','badRecordAsNew','onlyPetId','oldWriter','unknownFaultAsLost','staleReady'),
    [int]$ProcessTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$toolPath = (Resolve-Path -LiteralPath $ToolDllPath).Path
$nativePath = (Resolve-Path -LiteralPath $NativeDllPath).Path
$probePath = (Resolve-Path -LiteralPath $NativeProbePath).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$expectedNativeHash = '59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC'
$nativeHash = (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
$toolHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash
$probeHash = (Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash
$requiredMutations = @('healthyReset','oldMove','oldJump','halfReady','skipSpawnOverlap','skipSpawnBounds','skipSpawnGround','oldEpoch','noReason','faultAutoRebirth','observerIgnored','earlyRelease','badRecordAsNew','onlyPetId','oldWriter','unknownFaultAsLost','staleReady')
if ($Mutations.Count -ne $requiredMutations.Count -or @($Mutations | Sort-Object -Unique).Count -ne $Mutations.Count -or
    @($requiredMutations | Where-Object { $_ -cnotin $Mutations }).Count -ne 0) {
    throw 'Complete admission requires every frozen mutation exactly once.'
}
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourceFiles = [string[]]@('eng/tools/Dango.SafeSpawnAdmission','eng/tools/Dango.SafeSpawnAdmission.Core','src/Dango.Physics','eng/native/box2d' | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot $_) -Recurse -File | Where-Object {
        @($_.FullName.Split([IO.Path]::DirectorySeparatorChar) | Where-Object { $_ -cin @('bin','obj') }).Count -eq 0
    } | ForEach-Object { [IO.Path]::GetRelativePath($sourceRoot, $_.FullName) }
})
[Array]::Sort($sourceFiles, [StringComparer]::Ordinal)
$sourceInventory = [Collections.Generic.List[object]]::new()
$sourceHasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
foreach ($relative in $sourceFiles) {
    $sourceFile = Join-Path $sourceRoot $relative
    $bytes = [IO.File]::ReadAllBytes($sourceFile)
    $sourceHasher.AppendData([Text.Encoding]::UTF8.GetBytes($relative.Replace([IO.Path]::DirectorySeparatorChar,[char]'/')))
    $sourceHasher.AppendData($bytes)
    $sourceInventory.Add([pscustomobject]@{Path=$relative.Replace([IO.Path]::DirectorySeparatorChar,[char]'/');Bytes=$bytes.Length;Sha256=(Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash})
}
$sourceHash = [Convert]::ToHexString($sourceHasher.GetHashAndReset())
$sourceHasher.Dispose()
$parameterHash = $null
if ($nativeHash -ne $expectedNativeHash) { throw 'The frozen R1 native module hash differs.' }
if ($ProcessTimeoutSeconds -lt 1 -or $ProcessTimeoutSeconds -gt 60) { throw 'Process timeout must be within 1..60 seconds.' }
if ((Test-Path -LiteralPath $evidencePath) -and (Get-ChildItem -LiteralPath $evidencePath -Force | Select-Object -First 1)) {
    throw 'Evidence directory must be new or empty; prior evidence is preserved.'
}
[void][IO.Directory]::CreateDirectory($evidencePath)
$processes = [Collections.Generic.List[object]]::new()
$commonChecks = @('frozen-dll','native-allocation-returned','native-owner-disposed')
$suiteChecks = @('planner-left-tangent-rejected','planner-right-tangent-rejected','planner-invalid-bounds-rejected','planner-group-tangent-rejected','planner-live-capacity-rejected',
    'tick0-1','gravity-1','600-native-steps-1','checkpoint-unsupported-1','tick0-2','gravity-2','600-native-steps-2','checkpoint-unsupported-2',
    'candidate-secondAdd-whole-dispose','candidate-cancel-whole-dispose','candidate-error-whole-dispose','cross-thread-rejected','native-candidate-latest-facts-race',
    'lower-actually-supports','remove-lower-survivor-retained','new-member-space-precheck','live-member-real-reentry','native-node-fault-probe',
    'real-native-raw-right-containment','real-native-raw-left-containment','real-native-raw-ground-containment',
    'readonly-dto-raw-ground-containment','readonly-dto-raw-right-containment',
    'tick0-velocity-stops-before-publication','tick0-support-stops-before-publication','tick0-cooldown-stops-before-publication')
$continuityChecks = @(foreach ($scene in @('push','stack','airborne')) { foreach ($transition in @('reconnect','takeover','pause')) { "$scene-$transition-120-bits" } })
$suiteChecks += $continuityChecks
$mutationChecks = @{
    halfReady='whole-group-before-ready';skipSpawnOverlap='independent-envelope-before-add';skipSpawnBounds='independent-envelope-before-add';skipSpawnGround='independent-envelope-before-add';
    oldMove='stale-intent-not-inherited';oldJump='stale-intent-not-inherited';oldEpoch='epoch-and-reason-native-reuse';noReason='epoch-and-reason-native-reuse';faultAutoRebirth='native-node-fault-probe'
}

function Test-NonEmptyGuid($Value) {
    $parsed = [Guid]::Empty
    return $Value -is [string] -and [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -and $parsed -ne [Guid]::Empty
}

function Invoke-RecoveryProbe {
    param([string]$Name, [string[]]$ProbeArguments, [int]$ExpectedExit = 0, [bool]$ExpectedPass = $true,
        [string]$ExpectedMode = 'suite', [string]$ExpectedMutation = '', [string[]]$RequiredChecks = @())
    # PowerShell emits no object for an empty branch, so an explicitly passed
    # empty check list may arrive as null. Do not turn that into a missing name.
    $RequiredChecks = @($RequiredChecks | Where-Object { -not [string]::IsNullOrEmpty($_) })
    $outputPath = Join-Path $evidencePath "$Name.json"
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $dotnetPath
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    foreach ($argument in @($toolPath, '--dll', $nativePath, '--probe', $probePath, '--output', $outputPath) + $ProbeArguments) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Unable to start $Name." }
    $taskPid = $process.Id
    # Read both pipes concurrently, including for intentionally failing mutants.
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($ProcessTimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $watch.Stop()
    [IO.File]::WriteAllText((Join-Path $evidencePath "$Name.stdout.log"), $stdout)
    [IO.File]::WriteAllText((Join-Path $evidencePath "$Name.stderr.log"), $stderr)
    $exitCode = $process.ExitCode
    $cpuMs = $process.TotalProcessorTime.TotalMilliseconds
    $process.Dispose()
    $report = if (Test-Path -LiteralPath $outputPath) { Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json } else { $null }
    $valid = -not $timedOut -and $exitCode -eq $ExpectedExit -and $null -ne $report -and
        $report.Version -eq 1 -and $report.ProcessId -eq $taskPid -and $report.DllHash -eq $nativeHash -and
        $report.Mode -ceq $ExpectedMode -and $report.Mutation -ceq $ExpectedMutation -and
        $report.SourceHash -ceq $sourceHash -and $report.ParameterHash -cmatch '^[A-F0-9]{64}$' -and
        $report.Pass -is [bool] -and
        $report.Pass -eq $ExpectedPass -and @($report.Checks).Count -gt 0 -and
        $null -ne $report.NativeCounts.Created -and $report.NativeCounts.Created -ge 0 -and
        $null -ne $report.Allocation.Before -and $report.Allocation.Before -ge 0 -and
        $report.NativeCounts.Created -eq $report.NativeCounts.Disposed -and
        $report.Allocation.Before -eq $report.Allocation.After
    $names = @($report.Checks | ForEach-Object { $_.Name })
    $valid = $valid -and @($names | Sort-Object -Unique).Count -eq $names.Count -and
        @($commonChecks + $RequiredChecks | Where-Object { $_ -cnotin $names }).Count -eq 0 -and
        @($report.Checks | Where-Object { $_.Pass -isnot [bool] -or $_.Name -ceq 'exception' }).Count -eq 0 -and
        @($report.Checks | Where-Object { $_.Name -cin $commonChecks -and -not $_.Pass }).Count -eq 0
    if ($null -eq $script:parameterHash) { $script:parameterHash = $report.ParameterHash }
    $valid = $valid -and $report.ParameterHash -ceq $script:parameterHash
    if ($null -ne $report.ExternalNativeProbe) {
        $external = $report.ExternalNativeProbe
        $valid = $valid -and $external.ProcessId -gt 0 -and $external.ProcessId -ne $taskPid -and
            $external.ExitCode -eq $(if ($ExpectedMutation -ceq 'faultAutoRebirth') { 1 } else { 0 }) -and
            $external.Created -gt 0 -and $external.Created -eq $external.Disposed -and
            $external.Before -ge 0 -and $external.Before -eq $external.After
    }
    if ($ExpectedMutation -ceq 'faultAutoRebirth' -and $null -eq $report.ExternalNativeProbe) { $valid = $false }
    if ($ExpectedPass) { $valid = $valid -and @($report.Checks | Where-Object { -not $_.Pass }).Count -eq 0 }
    else {
        $oracle = if ($ExpectedMutation -ceq 'healthyReset') { $continuityChecks } elseif ($mutationChecks.ContainsKey($ExpectedMutation)) { @($mutationChecks[$ExpectedMutation]) } else { @($ExpectedMutation) }
        $valid = $valid -and @($report.Checks | Where-Object { $_.Name -cin $oracle -and -not $_.Pass }).Count -gt 0
    }
    $processes.Add([pscustomobject]@{ Name=$Name; ProcessId=$taskPid; Exit=$exitCode; ExpectedExit=$ExpectedExit;
        ExpectedPass=$ExpectedPass; TimedOut=$timedOut; Exited=$true; CpuMilliseconds=$cpuMs;
        WallMilliseconds=$watch.Elapsed.TotalMilliseconds; Verified=$valid })
    $processes | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'processes.json') -Encoding utf8
    Write-Host "$Name : exit $exitCode; verified $valid"
    if (-not $valid) { throw "Probe $Name did not meet its independent process/evidence gate." }
    return $report
}

try {
    $suite = Invoke-RecoveryProbe -Name 'suite' -ProbeArguments @('--mode', 'suite') -RequiredChecks $suiteChecks
    if ($null -eq $suite.ExternalNativeProbe -or $suite.NativeCounts.Created -le 0 -or $suite.NativeCounts.Reset -ne 0) { throw 'Normal suite requires actual native worlds and independent native probe, with no Reset.' }
    foreach ($mutation in $Mutations) {
        if ($mutation -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid mutation name.' }
        $needed = if ($mutation -ceq 'healthyReset') { $suiteChecks } else { @() }
        $mutant = Invoke-RecoveryProbe -Name "mutant-$mutation" -ProbeArguments @('--mode', 'suite', '--mutation', $mutation) -ExpectedExit 1 -ExpectedPass $false -ExpectedMutation $mutation -RequiredChecks $needed
        if ($mutant.Mutation -ne $mutation) { throw 'Mutation identity differs from the requested mutant.' }
    }

    $recoveries = [Collections.Generic.List[object]]::new()
    foreach ($disposition in @('lost', 'released', 'fault', 'unknown')) {
      $prefixes = if ($disposition -in @('lost','released')) { @(120,1200) } else { @(120) }
      $priorDigest = $null
      foreach ($prefix in $prefixes) {
        $caseName = "$disposition-$prefix"
        $metadataPath = Join-Path $evidencePath "$caseName.metadata.json"
        $producerChecks = @('metadata-only-bounded') + $(if ($disposition -ceq 'fault') { @('native-node-fault-probe','producer-same-native-fault-source') } else { @('producer-actual-native-prefix') })
        if ($disposition -ceq 'released') { $producerChecks += 'actual-qualified-release' }
        $producer = Invoke-RecoveryProbe -Name "$caseName-produce" -ProbeArguments @('--mode', 'produce', '--steps', $prefix.ToString(), '--metadata', $metadataPath, '--disposition', $disposition) -ExpectedMode 'produce' -RequiredChecks $producerChecks
        if (-not (Test-Path -LiteralPath $metadataPath)) { throw "No metadata produced for $disposition." }
        $metadataBytes = (Get-Item -LiteralPath $metadataPath).Length
        if ($metadataBytes -le 0 -or $metadataBytes -gt 65536) { throw 'Metadata size is outside the frozen budget.' }
        $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
        $receipt = Get-Content -Raw -LiteralPath "$metadataPath.disposition.json" | ConvertFrom-Json
        if ($receipt.Version -ne 1 -or $receipt.Disposition -cne $disposition -or $receipt.ProducerProcessId -ne $producer.ProcessId -or
            $receipt.MetadataHash -ne (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash -or
            -not (Test-NonEmptyGuid $metadata.playEpoch) -or $receipt.SourceEpoch -cne $metadata.playEpoch -or
            $producer.Recovery.PriorEpoch -cne $metadata.playEpoch -or $producer.Recovery.Tick -ne $prefix -or $metadata.lastSuccessfulTick -ne $prefix -or
            $metadata.configuration.parameterHash -cne $parameterHash) { throw 'Metadata/receipt does not identify this actual producer and frozen configuration.' }
        if ($disposition -ceq 'fault') {
            $external = $producer.ExternalNativeProbe
            if ($null -eq $external -or $external.LastSafeTick -ne $prefix -or $external.Steps -ne $prefix -or $external.NativeErrorCode -ne -422 -or
                $producer.NativeCounts.Created -ne 0) { throw 'Fault metadata must come from one actual native fault source.' }
        }
        elseif ($producer.NativeCounts.Created -ne 1 -or $producer.NativeCounts.Reset -ne 0) { throw 'Native prefix requires one actual unchanged world.' }
        # Producer has actually exited; consumer is a different process and reads no physical checkpoint.
        $consumerChecks = if ($disposition -in @('fault','unknown')) { @('trusted-disposition-blocks-rebirth') } else { @('recover-tick0','group-publication','old-epoch-ready-input-rejected','five-second-budget','recovery-600-steps') }
        $consumer = Invoke-RecoveryProbe -Name "$caseName-consume" -ProbeArguments @('--mode', 'consume', '--metadata', $metadataPath, '--disposition', $disposition) -ExpectedMode 'consume' -RequiredChecks $consumerChecks
        if ($producer.ProcessId -eq $consumer.ProcessId) { throw 'Producer and consumer process identity must differ.' }
        if ($consumer.Recovery.PriorEpoch -cne $metadata.playEpoch) { throw 'Consumer must recover the actual producer source epoch.' }
        if ($disposition -in @('fault', 'unknown')) {
            if (-not $consumer.Recovery.Blocked -or $consumer.Recovery.Ready -or $consumer.Recovery.Published -or
                $consumer.Recovery.Epoch -ne [Guid]::Empty.ToString('D') -or $consumer.NativeCounts.Created -ne 0 -or $consumer.NativeCounts.Reset -ne 0) {
                throw "Disposition $disposition must block before native creation."
            }
        }
        else {
            $expectedReason = if ($disposition -ceq 'lost') { 'ConfirmedWorldLost' } else { 'NormallyReleased' }
            if ($consumer.Recovery.Blocked -or -not $consumer.Recovery.Ready -or -not $consumer.Recovery.Published -or $consumer.Recovery.AwaitingReady -or
                $consumer.Recovery.Tick -ne 1 -or -not (Test-NonEmptyGuid $consumer.Recovery.Epoch) -or
                $consumer.NativeCounts.Created -ne 1 -or $consumer.NativeCounts.Reset -ne 0 -or
                $consumer.Recovery.Epoch -eq $consumer.Recovery.PriorEpoch -or
                $consumer.Recovery.Reason -cne $expectedReason -or
                $consumer.Recovery.RecordValidationToHealthyMs -le 0 -or
                $consumer.Recovery.RecordValidationToHealthyMs -gt 5000) {
                throw "Disposition $disposition did not produce a bounded new-epoch healthy recovery."
            }
            if ([string]::IsNullOrWhiteSpace($consumer.RawDigest) -or ($null -ne $priorDigest -and $priorDigest -ne $consumer.RawDigest)) {
                throw 'New first-healthy physical state must be independent of prior history length.'
            }
            $priorDigest = $consumer.RawDigest
        }
        $recoveries.Add([pscustomobject]@{Disposition=$disposition; PrefixSteps=$prefix; MetadataBytes=$metadataBytes;
            ProducerPid=$producer.ProcessId; ConsumerPid=$consumer.ProcessId;
            Reason=$consumer.Recovery.Reason; Blocked=$consumer.Recovery.Blocked;
            PriorEpoch=$consumer.Recovery.PriorEpoch; Epoch=$consumer.Recovery.Epoch;
            RecoveryMilliseconds=$consumer.Recovery.RecordValidationToHealthyMs})
      }
    }
    if ((Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash -ne $toolHash -or
        (Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash -ne $probeHash -or
        (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash -ne $nativeHash -or
        @($sourceInventory | Where-Object { (Get-FileHash -LiteralPath (Join-Path $sourceRoot $_.Path) -Algorithm SHA256).Hash -ne $_.Sha256 }).Count -gt 0) {
        throw 'Source or executing binaries changed during verification.'
    }
    $evidenceFiles = @(Get-ChildItem -LiteralPath $evidencePath -File | ForEach-Object {
        [pscustomobject]@{Path=$_.Name;Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $manifest = [ordered]@{ Gate='isolated-safe-spawn-cross-process'; Pass=$true;
        NativeHash=$nativeHash; ToolHash=$toolHash; NativeProbeHash=$probeHash;
        SourceHash=$sourceHash; ParameterHash=$parameterHash; SourceFiles=$sourceInventory; EvidenceFiles=$evidenceFiles;
        Processes=$processes; Recoveries=$recoveries; MutationCount=$Mutations.Count;
        FormalServerVerified=$false; DatabaseVerified=$false; LinuxVerified=$false; RealWindowVerified=$false;
        MultiClientVerified=$false; ManualAcceptance=$false; CompleteCheckpointAdmitted=$false }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidencePath 'manifest.json') -Encoding utf8
    Write-Output 'Isolated process/evidence gates passed; formal and manual gates remain unverified.'
}
catch {
    [ordered]@{Gate='isolated-safe-spawn-cross-process';Pass=$false;Error=$_.Exception.Message;Processes=$processes} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'failure.json') -Encoding utf8
    throw
}

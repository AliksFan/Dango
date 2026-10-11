[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Box2DArchivePath,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$SdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0.300'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BoundedProcess.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$archive = (Resolve-Path -LiteralPath $Box2DArchivePath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $evidence.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must stay inside this worktree artifacts directory.' }
if ((Test-Path -LiteralPath $evidence) -and (Get-ChildItem -LiteralPath $evidence -Force | Select-Object -First 1)) { throw 'Evidence directory must be new or empty.' }
if ($SdkImage -cne 'mcr.microsoft.com/dotnet/sdk:10.0.300') { throw 'This run uses the approved official .NET SDK image.' }
$dependency = Get-Content -Raw -LiteralPath (Join-Path $root 'eng/native/box2d/dependency.json') | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -cne $dependency.sourceArchive.sha256) { throw 'Pinned Box2D archive hash differs.' }
[void][IO.Directory]::CreateDirectory($evidence)
$inputs = Join-Path $evidence 'inputs'
$output = Join-Path $evidence 'output'
[void][IO.Directory]::CreateDirectory($output)
$snapshot = Join-Path $inputs 'repo'
[void][IO.Directory]::CreateDirectory($snapshot)
$inventory = [Collections.Generic.List[object]]::new()
$parts = @('eng/tools/Dango.SafeSpawnAdmission','eng/tools/Dango.SafeSpawnAdmission.Core','src/Dango.Physics','eng/native/box2d')
foreach ($part in $parts) {
    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $root $part) -Recurse -File) {
        if (@($item.FullName.Split([IO.Path]::DirectorySeparatorChar) | Where-Object { $_ -cin @('bin','obj') }).Count) { continue }
        $relative = [IO.Path]::GetRelativePath($root, $item.FullName)
        $target = Join-Path $snapshot $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath $item.FullName -Destination $target
        $inventory.Add([pscustomobject]@{Path=$relative.Replace([IO.Path]::DirectorySeparatorChar,[char]'/');Bytes=$item.Length;Sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash})
    }
}
foreach ($relative in @('Directory.Build.props','Directory.Build.targets','global.json')) {
    $target = Join-Path $snapshot $relative
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
    $inventory.Add([pscustomobject]@{Path=$relative;Bytes=(Get-Item -LiteralPath $target).Length;Sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash})
}
if (-not (Test-Path -LiteralPath (Join-Path $snapshot 'eng/tools/Dango.SafeSpawnAdmission/linux/run-linux.sh'))) { throw 'Linux runner has not been integrated.' }
Copy-Item -LiteralPath $archive -Destination (Join-Path $inputs 'box2d.zip')
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'host-source-files.json') -Encoding utf8
$gitHead = (& git -C $root rev-parse HEAD).Trim()
$docker = (Get-Command docker -ErrorAction Stop).Source
$container = 'dango-p1-linux-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$label = 'dango.p1.run=' + $container
$createAttempted = $false
$daemonStarted = $false
$hostResult = [ordered]@{Version=1;GitHead=$gitHead;ContainerName=$container;TaskLabel=$label;SdkImage=$SdkImage;
    DaemonStartedByTask=$false;ContainerRemoved=$false;ContainerCreationAttempted=$false;VerificationNetwork='none';PublishedPorts=@();Pass=$false}
$queryProcesses = [Collections.Generic.List[object]]::new()

function Invoke-DockerQuery([string]$Name, [string[]]$Arguments, [int]$TimeoutMilliseconds = 5000) {
    $result = Invoke-DangoBoundedProcess $docker (@('--context','desktop-linux') + $Arguments) $TimeoutMilliseconds
    $queryProcesses.Add([pscustomobject]@{Name=$Name;ProcessId=$result.ProcessId;ExitCode=$result.ExitCode;
        TimedOut=$result.TimedOut;Exited=$result.Exited;WallMilliseconds=$result.WallMilliseconds})
    $result.StdErr | Set-Content -LiteralPath (Join-Path $evidence "$Name.log") -Encoding utf8
    return $result
}

function Read-DockerQuery([string]$Name, [string[]]$Arguments, [int]$TimeoutMilliseconds = 5000) {
    $result = Invoke-DockerQuery $Name $Arguments $TimeoutMilliseconds
    if ($result.TimedOut -or $result.ExitCode -ne 0) { throw "Docker query $Name failed or timed out; state is unconfirmed." }
    return $result.StdOut
}

function Invoke-DockerLogged([string]$Name, [string[]]$Arguments) {
    & $docker --context desktop-linux @Arguments 2>&1 | Set-Content -LiteralPath (Join-Path $evidence "$Name.log") -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Docker operation $Name failed; see its retained log." }
}

try {
    $dockerContext = (& $docker context show).Trim()
    if ($LASTEXITCODE -ne 0 -or $dockerContext -cne 'desktop-linux') { throw 'Expected the existing desktop-linux Docker context.' }
    $initial = Invoke-DockerQuery 'engine-initial' @('info','--format','{{.OSType}}')
    if ($initial.TimedOut -or $initial.ExitCode -ne 0) {
        $desktop = Join-Path $env:ProgramFiles 'Docker/Docker/Docker Desktop.exe'
        if (-not (Test-Path -LiteralPath $desktop)) { throw 'Existing Docker Desktop installation unavailable.' }
        Write-Host 'Starting the existing Docker Desktop for the user-selected temporary Linux run.'
        Start-Process -FilePath $desktop -WindowStyle Hidden
        $daemonStarted = $true
        $boot = [Diagnostics.Stopwatch]::StartNew()
        $engineReady = $false
        $probeNumber = 0
        while (-not $engineReady -and $boot.Elapsed.TotalMilliseconds -lt 180000) {
            $remainingMilliseconds = [int][Math]::Floor(180000 - $boot.Elapsed.TotalMilliseconds)
            Start-Sleep -Milliseconds ([Math]::Min(3000,$remainingMilliseconds))
            $remainingMilliseconds = [int][Math]::Floor(180000 - $boot.Elapsed.TotalMilliseconds)
            if ($remainingMilliseconds -le 0) { break }
            $probeNumber++
            $probe = Invoke-DockerQuery "engine-boot-$probeNumber" @('info','--format','{{.OSType}}') ([Math]::Min(5000,$remainingMilliseconds))
            $engineReady = -not $probe.TimedOut -and $probe.ExitCode -eq 0
        }
        $hostResult.EngineStartupMilliseconds = $boot.Elapsed.TotalMilliseconds
        if (-not $engineReady) { throw 'Docker engine did not become available; user assistance may be needed in Docker Desktop.' }
    }
    $engine = Read-DockerQuery 'engine-details' @('info','--format','{{json .}}') | ConvertFrom-Json
    if ($engine.OSType -cne 'linux' -or $engine.Architecture -cnotin @('x86_64','amd64')) { throw 'Docker engine must run Linux x64.' }
    $hostResult.DaemonStartedByTask = $daemonStarted
    $hostResult.Engine = [ordered]@{ServerVersion=$engine.ServerVersion;OSType=$engine.OSType;Architecture=$engine.Architecture}
    Write-Host 'Pulling the official SDK image; the exact image digest is frozen before execution.'
    Invoke-DockerLogged -Name 'image-pull' -Arguments @('pull','--platform','linux/amd64',$SdkImage)
    $sdk = (Read-DockerQuery 'image-inspect' @('image','inspect',$SdkImage) | ConvertFrom-Json)[0]
    if ($sdk.Os -cne 'linux' -or $sdk.Architecture -cne 'amd64' -or @($sdk.RepoDigests).Count -eq 0) { throw 'SDK image platform or immutable digest is missing.' }
    $hostResult.ImageId = $sdk.Id
    $hostResult.RepoDigests = $sdk.RepoDigests
    $hostResult.ImageArchitecture = $sdk.Architecture
    $createAttempted = $true
    $hostResult.ContainerCreationAttempted = $true
    Invoke-DockerLogged -Name 'container-create' -Arguments @('run','--detach','--rm','--name',$container,'--label',$label,
        '--platform','linux/amd64','--cpus','2','--memory','3g','--pids-limit','128',
        '--mount',"type=bind,source=$inputs,target=/inputs,readonly",'--mount',"type=bind,source=$output,target=/evidence",
        '--entrypoint','sleep',$sdk.Id,'infinity')
    Write-Host 'Installing build tools in the temporary container, then disconnecting its network.'
    Invoke-DockerLogged -Name 'container-tools' -Arguments @('exec','--env','DEBIAN_FRONTEND=noninteractive',$container,'bash','-c',
        'apt-get update && apt-get install -y --no-install-recommends build-essential cmake python3 binutils unzip')
    Invoke-DockerLogged -Name 'container-network-disconnect' -Arguments @('network','disconnect','bridge',$container)
    $isolated = (Read-DockerQuery 'container-isolation-inspect' @('inspect',$container) | ConvertFrom-Json)[0]
    if (@($isolated.NetworkSettings.Networks.PSObject.Properties).Count -ne 0 -or
        @($isolated.HostConfig.PortBindings.PSObject.Properties).Count -ne 0 -or $isolated.HostConfig.Privileged) { throw 'Container isolation does not match the approved plan.' }
    $inputMount = @($isolated.Mounts | Where-Object { $_.Destination -ceq '/inputs' })
    $outputMount = @($isolated.Mounts | Where-Object { $_.Destination -ceq '/evidence' })
    if (@($isolated.Mounts).Count -ne 2 -or $inputMount.Count -ne 1 -or $outputMount.Count -ne 1 -or
        $inputMount[0].RW -or -not $outputMount[0].RW -or
        $inputMount[0].Type -cne 'bind' -or $outputMount[0].Type -cne 'bind') { throw 'Expected only a read-only input mount and separate writable output mount.' }
    $hostResult.Isolation = [ordered]@{Networks=@($isolated.NetworkSettings.Networks.PSObject.Properties.Name);
        Mounts=@($isolated.Mounts | Select-Object Type,Source,Destination,RW);Privileged=$isolated.HostConfig.Privileged;
        NanoCpus=$isolated.HostConfig.NanoCpus;Memory=$isolated.HostConfig.Memory;PidsLimit=$isolated.HostConfig.PidsLimit}
    Write-Host 'Running the same-source Linux build and real headless gates without networking.'
    Invoke-DockerLogged -Name 'linux-execution' -Arguments @('exec',$container,'bash',
        '/inputs/repo/eng/tools/Dango.SafeSpawnAdmission/linux/run-linux.sh','/inputs/repo','/inputs/box2d.zip','/evidence')
    $linuxManifest = Get-Content -Raw -LiteralPath (Join-Path $output 'manifest.json') | ConvertFrom-Json
    if ($linuxManifest.Pass -isnot [bool] -or -not $linuxManifest.Pass) { throw 'Linux verifier did not confirm its complete gate.' }
    $hostResult.Pass = $true
}
catch {
    $hostResult.Error = $_.Exception.Message
    throw
}
finally {
    $hostResult.DaemonStartedByTask = $daemonStarted
    if ($createAttempted) {
        try {
            $listing = Read-DockerQuery 'cleanup-list-before' @('container','ls','--all','--no-trunc','--filter',"name=^/$container$",'--format','{{.ID}}')
            $listed = @($listing -split '\r?\n' | Where-Object { $_.Length -gt 0 })
            if ($listed.Count -gt 1) { throw 'Unique task container query returned multiple objects.' }
            if ($listed.Count -eq 1) {
                $ownedId = $listed[0].Trim()
                $owned = @(Read-DockerQuery 'cleanup-owned-inspect' @('inspect',$ownedId) | ConvertFrom-Json)
                if ($owned.Count -ne 1 -or $owned[0].Id -cne $ownedId -or
                    $owned[0].Name -cne "/$container" -or $owned[0].Config.Labels.'dango.p1.run' -cne $container) {
                    throw 'Task container ownership could not be confirmed; no container was stopped.'
                }
                $hostResult.ContainerId = $ownedId
                $stopped = Read-DockerQuery 'container-stop' @('stop','--time','10',$ownedId) 15000
                $remainingListing = Read-DockerQuery 'cleanup-list-after' @('container','ls','--all','--no-trunc','--filter',"name=^/$container$",'--format','{{.ID}}')
                $remaining = @($remainingListing -split '\r?\n' | Where-Object { $_.Length -gt 0 })
                if ($remaining.Count -ne 0) { throw 'Task container still exists after cleanup.' }
            }
            $hostResult.ContainerRemoved = $true
        }
        catch {
            $hostResult.Pass = $false
            $hostResult.CleanupError = $_.Exception.Message
        }
    }
    try {
        $finalProbe = Invoke-DockerQuery 'engine-final' @('info','--format','{{.OSType}}')
        $hostResult.DockerEngineStillRunning = -not $finalProbe.TimedOut -and $finalProbe.ExitCode -eq 0
    }
    catch {
        $hostResult.DockerEngineStillRunning = $false
        $hostResult.EngineFinalProbeError = $_.Exception.Message
        $hostResult.Pass = $false
    }
    $hostResult.QueryProcesses = $queryProcesses.ToArray()
    $hostResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'host-manifest.json') -Encoding utf8
}
if (-not $hostResult.Pass) { throw 'Linux gate or task-owned container cleanup failed.' }
Write-Output 'Linux gates completed and the task-owned temporary container was removed; SDK cache and Docker engine remain available.'

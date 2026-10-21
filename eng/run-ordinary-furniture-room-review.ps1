param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [switch]$Show,
    [switch]$Verify,
    [switch]$Observe,
    [ValidateRange(0, 3600)][double]$ActiveSeconds = 600
)

$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskExpectedProtectedHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'
$taskProtectedModule = Join-Path $taskRepo 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$taskBuild = Join-Path $taskRepo "artifacts/furniture-room/$RunName"
if (Test-Path -LiteralPath $taskBuild) { throw 'Use a fresh RunName; existing artifacts and data are preserved.' }
for ($taskCursor = $taskBuild; $taskCursor; $taskCursor = Split-Path -Parent $taskCursor) {
    if ((Test-Path -LiteralPath $taskCursor) -and ((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Acceptance paths cannot traverse links.' }
}
if ((Get-FileHash -LiteralPath $taskProtectedModule -Algorithm SHA256).Hash -ne $taskExpectedProtectedHash) { throw 'Protected production DLL differs from the frozen source.' }

function Get-TaskSourceSnapshot {
    $taskRelevantPaths = @('src', 'eng', 'tests', 'docs', 'AGENTS.md', '.editorconfig',
        'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
        'global.json', 'NuGet.Config', 'Dango.sln', 'Dango.slnx')
    $taskPaths = @(& git -c core.quotepath=false -C $taskRepo ls-files --cached --others --exclude-standard -- @taskRelevantPaths)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory tracked and non-ignored untracked candidate source.' }
    $taskChanges = @(& git -C $taskRepo status --porcelain --untracked-files=all -- @taskRelevantPaths)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine candidate source dirty state.' }
    $taskBaseHead = (& git -C $taskRepo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record candidate base HEAD.' }
    $taskSourceFiles = @(
        foreach ($taskRelative in ($taskPaths | Sort-Object -Unique)) {
            $taskSourcePath = Join-Path $taskRepo $taskRelative
            $taskSourceHash = if (Test-Path -LiteralPath $taskSourcePath -PathType Leaf) {
                (Get-FileHash -LiteralPath $taskSourcePath -Algorithm SHA256).Hash
            } else { 'missing' }
            [ordered]@{ path = $taskRelative.Replace('\', '/'); sha256 = $taskSourceHash }
        }
    )
    $taskCanonical = ConvertTo-Json -InputObject $taskSourceFiles -Compress -Depth 4
    $taskSha = [System.Security.Cryptography.SHA256]::Create()
    try { $taskTreeHash = [System.BitConverter]::ToString($taskSha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($taskCanonical))).Replace('-', '') }
    finally { $taskSha.Dispose() }
    [ordered]@{ schema = 1; baseHead = $taskBaseHead; sourceDirty = $taskChanges.Count -gt 0;
        sourceSnapshotSha256 = $taskTreeHash; hashMeaning = 'SHA256 of canonical relative-path and per-file SHA256 inventory';
        sourceFiles = $taskSourceFiles }
}

$taskSourceBefore = Get-TaskSourceSnapshot
New-Item -ItemType Directory -Path $taskBuild | Out-Null
$taskSourceBefore | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $taskBuild 'source-snapshot.json') -Encoding utf8
try {
    # One serial build creates both the actual Desktop application and its friend verification tool.
    & dotnet build (Join-Path $taskRepo 'eng/tools/Dango.JointReview/Dango.JointReview.csproj') -c Release --artifacts-path $taskBuild
    if ($LASTEXITCODE -ne 0) { throw 'Ordinary-room furniture candidate build failed.' }
    $taskReviewExe = Join-Path $taskBuild 'bin/Dango.JointReview/release/Dango.JointReview.exe'
    $taskDesktopExe = Join-Path $taskBuild 'bin/Dango.Desktop/release/Dango.Desktop.exe'
    if (!(Test-Path -LiteralPath $taskReviewExe) -or !(Test-Path -LiteralPath $taskDesktopExe)) { throw 'Build did not produce the actual Desktop application and verification tool.' }
    $taskReviewModule = Join-Path (Split-Path -Parent $taskReviewExe) 'dango_belt_room.dll'
    $taskDesktopModule = Join-Path (Split-Path -Parent $taskDesktopExe) 'dango_belt_room.dll'
    $taskModule = $taskDesktopModule
    $taskExpectedHash = (Get-FileHash -LiteralPath $taskModule -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $taskReviewModule -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'Default room module copies differ.' }
    $taskSourceAfter = Get-TaskSourceSnapshot
    if ($taskSourceAfter.sourceSnapshotSha256 -ne $taskSourceBefore.sourceSnapshotSha256 -or
        $taskSourceAfter.baseHead -ne $taskSourceBefore.baseHead -or $taskSourceAfter.sourceDirty -ne $taskSourceBefore.sourceDirty) {
        $taskSourceAfter | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath (Join-Path $taskBuild 'source-snapshot-after-drift.json') -Encoding utf8
        throw 'Actual candidate source changed during the build; this run cannot be used for acceptance.'
    }
}
finally {
    if ((Get-FileHash -LiteralPath $taskProtectedModule -Algorithm SHA256).Hash -ne $taskExpectedProtectedHash) { throw 'Protected production DLL changed during build.' }
}

$taskData = Join-Path $taskBuild 'acceptance-data'
if (Test-Path -LiteralPath $taskData) { throw 'Actual Desktop review data must be fresh and owned by this run.' }
New-Item -ItemType Directory -Path $taskData | Out-Null
[ordered]@{ purpose = 'ordinary-furniture-room-acceptance'; schema = 1; owner = [guid]::NewGuid().ToString() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'owner.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $taskData '.dango-local-install-acceptance.json') -Encoding utf8 -Value '{"application":"Dango","acceptanceFormat":1,"fictional":true}'
[ordered]@{
    schema = 1; sourceBaseHead = $taskSourceBefore.baseHead; sourceDirty = $taskSourceBefore.sourceDirty
    sourceSnapshotSha256 = $taskSourceBefore.sourceSnapshotSha256; sourceSnapshot = 'source-snapshot.json'
    sourceIdentityMeaning = 'Base HEAD is not the dirty candidate; source snapshot identifies actual tested files'
    candidateSha256 = $taskExpectedHash
    protectedDllSha256 = $taskExpectedProtectedHash
    scope = 'actual main-program ordinary room; Windows local single-player; default continuous body and one official static platform'
    executable = 'bin/Dango.Desktop/release/Dango.Desktop.exe'
    arguments = @('--local-install-acceptance-settings', $taskData); data = 'acceptance-data'
    verificationTool = 'bin/Dango.JointReview/release/Dango.JointReview.exe'
    humanAcceptance = 'pending'; hardwareInput = 'not verified by software-driven gates'
    composition59FpsGate = 'separate and pending'; realOs150PercentDpi = 'separate and pending'
    integrationPermissions = 'no main merge, push, daily installation, production DLL replacement or deployment'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskBuild 'candidate-manifest.json') -Encoding utf8

try {
    if ($Verify -or $Observe) {
        $taskVerifyData = Join-Path $taskBuild 'verification-data'
        New-Item -ItemType Directory -Path $taskVerifyData | Out-Null
        [ordered]@{ purpose = 'ordinary-furniture-room-acceptance'; schema = 1; owner = [guid]::NewGuid().ToString() } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskVerifyData 'owner.json') -Encoding utf8
        $taskEvidence = Join-Path $taskBuild 'verification'
        $taskVerificationMode = if ($Observe) { '--ordinary-furniture-room-verify' } else { '--ordinary-furniture-room-verify-short' }
        $taskArguments = @($taskVerificationMode, ('"{0}"' -f $taskReviewModule), ('"{0}"' -f $taskVerifyData), ('"{0}"' -f $taskEvidence))
        if ($Observe) { $taskArguments += $ActiveSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture) }
        # The executable creates the actual product host windows; this hides only the helper launch surface.
        $taskVerifier = Start-Process -FilePath $taskReviewExe -ArgumentList $taskArguments -PassThru -WindowStyle Hidden
        Write-Output "Actual product verification PID: $($taskVerifier.Id)"
        Write-Output "Progress: $(Join-Path $taskEvidence 'progress.json')"
        $taskVerifierLimitSeconds = if ($Observe) { $ActiveSeconds + 300 } else { 300 }
        $taskVerifierFinished = $taskVerifier.WaitForExit([int][Math]::Ceiling($taskVerifierLimitSeconds * 1000))
        if (!$taskVerifierFinished) {
            # Terminate only this owned helper and its owned restart children, never another Dango process.
            if (!$taskVerifier.HasExited) { $taskVerifier.Kill($true); $taskVerifier.WaitForExit(10000) | Out-Null }
            [ordered]@{ passed = $false; phase = 'external-verifier-timeout';
                verifierProcessId = $taskVerifier.Id; wallLimitSeconds = $taskVerifierLimitSeconds;
                action = 'terminated the owned verifier process tree'; progress = 'verification/progress.json' } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBuild 'verification-timeout.json') -Encoding utf8
            throw 'Owned actual-product verifier exceeded its external wall-clock deadline; inspect verification-timeout.json.'
        }
        $taskVerifier.Refresh()
        $taskSummary = Join-Path $taskEvidence 'verification-summary.json'
        if ($taskVerifier.ExitCode -ne 0 -or !(Test-Path -LiteralPath $taskSummary)) { throw "Actual ordinary-room furniture verification failed (exit $($taskVerifier.ExitCode)); inspect owned evidence." }
        $taskResult = Get-Content -LiteralPath $taskSummary -Raw | ConvertFrom-Json
        if (!$taskResult.Passed) { throw 'Actual ordinary-room furniture verification did not pass.' }
        Write-Output "Verification summary: $taskSummary"
    }
    Write-Output "Actual Desktop acceptance executable: $taskDesktopExe"
    Write-Output "Candidate SHA-256: $taskExpectedHash"
    Write-Output "Owned actual Desktop data: $taskData"
    if ($Show) {
        # -Show explicitly requests a visible window; enter the room through the actual product porch.
        $taskWindow = Start-Process -FilePath $taskDesktopExe -ArgumentList @('--local-install-acceptance-settings', ('"{0}"' -f $taskData)) -PassThru
        if ($taskWindow.WaitForExit(2000)) {
            $taskWindow.Refresh()
            [ordered]@{ processId = $taskWindow.Id; exitCode = $taskWindow.ExitCode;
                processStatus = 'new candidate exited during launch; no acceptance window confirmed';
                executable = 'bin/Dango.Desktop/release/Dango.Desktop.exe'; arguments = @('--local-install-acceptance-settings', $taskData) } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBuild 'manual-window.json') -Encoding utf8
            throw 'The new actual Desktop candidate exited during launch; an old review or startup failure cannot count as this candidate opening.'
        }
        $taskWindow.Refresh()
        [ordered]@{ processId = $taskWindow.Id; executable = 'bin/Dango.Desktop/release/Dango.Desktop.exe';
            arguments = @('--local-install-acceptance-settings', $taskData); data = 'acceptance-data';
            survivedInitialLaunch = $true; visibleMainWindowHandleObserved = $taskWindow.MainWindowHandle -ne [IntPtr]::Zero;
            processStatus = 'left open for user manual acceptance' } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBuild 'manual-window.json') -Encoding utf8
        Write-Output "Actual ordinary-room acceptance PID: $($taskWindow.Id)"
    }
}
finally {
    if ((Get-FileHash -LiteralPath $taskProtectedModule -Algorithm SHA256).Hash -ne $taskExpectedProtectedHash) { throw 'Protected production DLL changed during verification/launch.' }
    if ((Get-FileHash -LiteralPath $taskModule -Algorithm SHA256).Hash -ne $taskExpectedHash) { throw 'Candidate source changed during verification/launch.' }
}

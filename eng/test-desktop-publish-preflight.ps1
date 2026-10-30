[CmdletBinding()]
param([Parameter(Mandatory)][string]$PhysicsModulePath, [string]$BeltNativeDirectory, [string]$PackagePath, [string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
if (-not $EvidenceRoot) {
    $EvidenceRoot = Join-Path $PSScriptRoot ('../artifacts/windows-local-closeout/preflight-' + [Guid]::NewGuid().ToString('N'))
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'Evidence directory must be new.' }
$null = New-Item -ItemType Directory -Path $EvidenceRoot
$preflight = Join-Path $PSScriptRoot 'test-desktop-publish-inputs.ps1'
$results = New-Object Collections.ArrayList
function Run-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; $null = $results.Add([ordered]@{ name=$Name; passed=$true }); Write-Output "PASS $Name" }
    catch { $null = $results.Add([ordered]@{ name=$Name; passed=$false; detail=$_.Exception.Message }); Write-Output "FAIL $Name $($_.Exception.Message)" }
}
function Expect-Rejection([scriptblock]$Body, [string]$Message) {
    try { $null = & $Body } catch { if ($_.Exception.Message -like "*$Message*") { return }; throw }
    throw 'Invalid publish input was accepted.'
}
Run-Case 'missing-module-rejected-before-output' {
    Expect-Rejection { & $preflight -PhysicsModulePath (Join-Path $EvidenceRoot 'fictional-missing.dll') } 'missing'
}
Run-Case 'wrong-module-hash-rejected-before-loading' {
    $wrong = Join-Path $EvidenceRoot 'fictional-wrong.dll'
    [IO.File]::WriteAllText($wrong, 'fictional invalid module')
    Expect-Rejection { & $preflight -PhysicsModulePath $wrong } 'hash mismatch'
}
Run-Case 'accepted-module-hash-abi-and-seven-exports' {
    $checked = & $preflight -PhysicsModulePath $PhysicsModulePath
    if ($checked.NativeAbi -ne 1 -or @($checked.RequiredExports).Count -ne 7 -or -not $checked.ProtectedInputsVerified) {
        throw 'Native preflight returned incomplete evidence.'
    }
}
Run-Case 'missing-protected-input-rejected' {
    Expect-Rejection { & $preflight -PhysicsModulePath $PhysicsModulePath -RepositoryRoot $EvidenceRoot } 'missing'
}
Run-Case 'default-publish-needs-no-legacy-module' {
    $checked = & $preflight
    if ($checked.PhysicsModulePath -or -not $checked.ProtectedInputsVerified) { throw 'Default preflight requires the historical module.' }
}
if ($BeltNativeDirectory) {
    Run-Case 'belt-source-artifact-and-native-identity-binding' {
        $checked = & $preflight -BeltNativeDirectory $BeltNativeDirectory
        if (@($checked.BeltNative.Modules).Count -ne 2) { throw 'Missing belt evidence.' }
    }
    Run-Case 'belt-manifest-source-tamper-rejected' {
        $copy = Join-Path $EvidenceRoot 'source-tamper'
        $null = New-Item -ItemType Directory -Path $copy
        foreach ($name in @('dango_belt_product.dll', 'dango_belt_room.dll', 'belt-native-manifest.json')) {
            Copy-Item -LiteralPath (Join-Path $BeltNativeDirectory $name) -Destination (Join-Path $copy $name)
        }
        $path = Join-Path $copy 'belt-native-manifest.json'
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $manifest.sourceHashes.'eng/native/box2d/dango_belt.c' = ('0' * 64)
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path
        Expect-Rejection { & $preflight -BeltNativeDirectory $copy } 'source hash mismatch'
    }
    Run-Case 'belt-manifest-artifact-tamper-rejected' {
        $copy = Join-Path $EvidenceRoot 'source-tamper'
        $path = Join-Path $copy 'belt-native-manifest.json'
        Copy-Item -LiteralPath (Join-Path $BeltNativeDirectory 'belt-native-manifest.json') -Destination $path -Force
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $manifest.modules[0].sha256 = ('0' * 64)
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path
        Expect-Rejection { & $preflight -BeltNativeDirectory $copy } 'artifact binding mismatch'
    }
}
if ($PackagePath) {
    Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
    Run-Case 'published-package-integrity-and-belt-contract' {
        $null = Test-DangoPackage -PackagePath $PackagePath
    }
    Run-Case 'new-contract-cannot-omit-belt-room-despite-consistent-file-list' {
        $copy = Join-Path $EvidenceRoot 'missing-room-package'
        $null = New-Item -ItemType Directory -Path $copy
        $manifest = Get-Content -LiteralPath (Join-Path $PackagePath 'package-manifest.json') -Raw | ConvertFrom-Json
        $keep = @('Dango.Desktop.exe', 'dango_belt_product.dll', 'belt-native-manifest.json', 'dango_box2d.dll',
            'licenses/Box2D-LICENSE.txt', 'licenses/Box2D-dependency.json')
        $manifest.files = @($manifest.files | Where-Object { $_.path -in $keep })
        foreach ($record in $manifest.files) {
            $destination = Join-Path $copy $record.path
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            Copy-Item -LiteralPath (Join-Path $PackagePath $record.path) -Destination $destination
        }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $copy 'package-manifest.json')
        Expect-Rejection { Test-DangoPackage -PackagePath $copy } 'Missing belt native package file'
    }
}
$summary = [ordered]@{ passed=@($results | Where-Object { $_.passed }).Count;
    failed=@($results | Where-Object { -not $_.passed }).Count; results=@($results) }
[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'summary.json'), ($summary | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
if ($summary.failed -gt 0) { exit 1 }

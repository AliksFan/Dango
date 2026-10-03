[CmdletBinding()]
param([Parameter(Mandatory)][string]$PhysicsModulePath, [string]$EvidenceRoot)
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
$summary = [ordered]@{ passed=@($results | Where-Object { $_.passed }).Count;
    failed=@($results | Where-Object { -not $_.passed }).Count; results=@($results) }
[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'summary.json'), ($summary | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
if ($summary.failed -gt 0) { exit 1 }

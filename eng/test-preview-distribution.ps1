[CmdletBinding()]
param([string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'preview-distribution.psm1') -Force
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo ('artifacts/preview-license-tests/' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'Evidence directory must be new.' }
$fixture = Join-Path $EvidenceRoot 'repository'
$app = Join-Path $EvidenceRoot 'application'
$null = [IO.Directory]::CreateDirectory($fixture)
$null = [IO.Directory]::CreateDirectory($app)

# Independent release contract: actual source bytes, arranged as the csproj
# publish layout. No runtime, app, installation, or desktop is executed.
$files = @(
    @('eng/native/box2d/LICENSE.box2d.txt', 'licenses/Box2D-LICENSE.txt'),
    @('eng/native/box2d/dependency.json', 'licenses/Box2D-dependency.json'),
    @('src/Dango.Desktop/Accessories/Editing/licenses/LICENSE.txt', 'licenses/WebView2/LICENSE.txt'),
    @('src/Dango.Desktop/Accessories/Editing/licenses/NOTICE.txt', 'licenses/WebView2/NOTICE.txt'),
    @('src/Dango.Desktop/Accessories/Editing/licenses/dependency.json', 'licenses/WebView2/dependency.json'),
    @('src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-minipaint.txt', 'AccessoryEditor/licenses/MIT-minipaint.txt'),
    @('src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-jquery.txt', 'AccessoryEditor/licenses/MIT-jquery.txt'),
    @('src/Dango.Desktop/Accessories/Editing/Web/NOTICE.txt', 'AccessoryEditor/NOTICE.txt')
)
foreach ($pair in $files) {
    foreach ($destination in @((Join-Path $fixture $pair[0]), (Join-Path $app $pair[1]))) {
        $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
        [IO.File]::Copy((Join-Path $repo $pair[0]), $destination)
    }
}
foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) { [IO.File]::Copy((Join-Path $repo $name), (Join-Path $fixture $name)) }
$script:passed = 0
function Check([string]$CaseLabel, [scriptblock]$Body) {
    # Scriptblocks see the invoking function's scope. Do not shadow the
    # fixture loop's $name with a case-description parameter here.
    & $Body
    $script:passed++
    Write-Output "PASS $CaseLabel"
}
function Reject([scriptblock]$Body, [string]$Expected) {
    try { $null = & $Body } catch { if ($_.Exception.Message.Contains($Expected)) { return }; throw }
    throw "Expected rejection: $Expected"
}
Check 'complete-source-and-third-party-inventory-accepted' {
    $inputs = @(Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app)
    if ($inputs.Count -ne 2 -or $inputs[0].Relative -cne 'LICENSE' -or $inputs[1].Relative -cne 'THIRD-PARTY-NOTICES.md') { throw 'Root license inventory incomplete.' }
}
foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Check "missing-$name-rejected" {
        $file = Join-Path $fixture $name
        Move-Item -LiteralPath $file -Destination ($file + '.held')
        try { Reject { Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app } "Missing distribution legal input: $name" }
        finally { Move-Item -LiteralPath ($file + '.held') -Destination $file }
    }
}
Check 'missing-third-party-notice-rejected' {
    $file = Join-Path $app 'licenses/WebView2/NOTICE.txt'
    Move-Item -LiteralPath $file -Destination ($file + '.held')
    try { Reject { Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app } 'Missing application legal input: licenses/WebView2/NOTICE.txt' }
    finally { Move-Item -LiteralPath ($file + '.held') -Destination $file }
}
Check 'changed-third-party-license-rejected' {
    $file = Join-Path $app 'AccessoryEditor/licenses/MIT-jquery.txt'
    $original = [IO.File]::ReadAllBytes($file)
    try {
        [IO.File]::WriteAllText($file, 'fictional altered license')
        Reject { Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app } 'Application legal input differs from source'
    } finally { [IO.File]::WriteAllBytes($file, $original) }
}
Check 'copied-license-bytes-and-inventory-preserved' {
    $inputs = @(Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app)
    $bundle = Join-Path $EvidenceRoot 'bundle'
    Copy-DangoPreviewLicenses -Inputs $inputs -BundleRoot $bundle
    foreach ($inputFile in $inputs) {
        if ((Get-FileHash -LiteralPath (Join-Path $bundle $inputFile.Relative)).Hash -cne (Get-FileHash -LiteralPath $inputFile.File).Hash) { throw 'Copied legal bytes differ.' }
    }
    if (@(Get-ChildItem -LiteralPath $bundle -File -Recurse).Count -ne 2) { throw 'Copied root legal inventory differs.' }
}
Check 'changed-input-after-inventory-rejected' {
    $inputs = @(Get-DangoPreviewLicenseInputs -RepositoryRoot $fixture -ApplicationRoot $app)
    [IO.File]::AppendAllText((Join-Path $fixture 'LICENSE'), "`nfictional tamper")
    Reject { Copy-DangoPreviewLicenses -Inputs $inputs -BundleRoot (Join-Path $EvidenceRoot 'tampered-copy') } 'Distribution legal copy changed'
}
Write-Output "Preview legal contracts: $script:passed passed. Fixtures preserved at $EvidenceRoot"

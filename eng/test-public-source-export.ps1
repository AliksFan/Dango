[CmdletBinding()]
param([string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent $PSScriptRoot
$exporter = Join-Path $PSScriptRoot 'export-public-source.ps1'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo ('artifacts/public-source-export-tests/' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'Test evidence directory must be new.' }
$fixture = Join-Path $EvidenceRoot 'fictional-repository'
$null = [IO.Directory]::CreateDirectory($fixture)
function Write-Fixture([string]$Relative, [string]$Text) {
    $path = Join-Path $fixture $Relative
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
}
function Git-Fixture([string[]]$Arguments) {
    $result = & git -C $fixture @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fictional Git operation failed: $result" }
    return $result
}
$script:passed = 0
function Check([string]$CaseLabel, [scriptblock]$Body) { & $Body; $script:passed++; Write-Output "PASS $CaseLabel" }
function Reject([scriptblock]$Body, [string]$Expected) {
    try { $null = & $Body } catch { if ($_.Exception.Message.Contains($Expected)) { return }; throw }
    throw "Expected rejection: $Expected"
}
# This isolated fictional fixture is never made public. Git configuration is
# invocation-local and cannot change the developer's identity or repositories.
$null = Git-Fixture @('init', '--quiet')
Write-Fixture '.gitignore' "artifacts/`nprivate.local`n"
Write-Fixture '.gitattributes' "* -text`n"
Write-Fixture 'LICENSE' "MIT License`nFictional fixture authors`n"
Write-Fixture 'THIRD-PARTY-NOTICES.md' "# Fixture third-party notices`n"
Write-Fixture 'third-party/LICENSE.txt' "Fictional retained third-party license`n"
Write-Fixture 'src/example.txt' "Fictional source bytes`n"
[IO.File]::WriteAllBytes((Join-Path $fixture 'src/fictional-binary.bin'), [byte[]]@(0, 255, 128, 13, 10, 26, 0, 254))
$images = @(
    'docs/planning/assets/2026-10-26-accessory-editor/compact-slider-reference.png',
    'docs/planning/assets/2026-10-26-accessory-editor/image-tools-reference.png',
    'docs/planning/assets/2026-10-26-accessory-editor/stacking-buttons-reference.png',
    'docs/planning/assets/2026-10-27-accessory-editor/canvas-layout-reference.png'
)
foreach ($image in $images) { Write-Fixture $image 'fictional unlicensed image bytes' }
Write-Fixture 'docs/planning/references.md' @'
# Fictional design notes
![slider](assets/2026-10-26-accessory-editor/compact-slider-reference.png)
[tools](assets/2026-10-26-accessory-editor/image-tools-reference.png)
![stack][stacking]
[stacking]: assets/2026-10-26-accessory-editor/stacking-buttons-reference.png "caption"
<img src="assets/2026-10-27-accessory-editor/canvas-layout-reference.png" alt="canvas">
[retained document](../../THIRD-PARTY-NOTICES.md)
'@
Write-Fixture 'artifacts/fictional-diagnostic.log' 'fictional tracked diagnostic, never distribute'
Write-Fixture 'private.local' 'fictional ignored private configuration, never distribute'
$null = Git-Fixture @('add', '.')
$null = Git-Fixture @('add', '-f', 'artifacts/fictional-diagnostic.log')
$null = Git-Fixture @('-c', 'user.name=Fictional Test', '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'Fictional export contract inputs')
$head = (Git-Fixture @('rev-parse', 'HEAD')).Trim()
$tracked = @(Git-Fixture @('ls-tree', '-r', '--name-only', 'HEAD'))
$before = @{}
foreach ($relative in $tracked) { $before[$relative] = (Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash }
$null = & $exporter -RunName 'contract' -RepositoryRoot $fixture
$output = Join-Path $fixture 'artifacts/open-source/contract'
$manifest = Get-Content -LiteralPath (Join-Path $output 'source-manifest.json') -Raw | ConvertFrom-Json
$zipPath = Join-Path $output 'Dango-public-source.zip'
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $names = @($entries.FullName)
    Check 'tracked-artifacts-four-unlicensed-images-and-history-excluded' {
        if (@($names | Where-Object { $_.StartsWith('artifacts/') -or $_.StartsWith('.git/') -or $_ -cin $images }).Count) { throw 'Excluded file leaked.' }
        if (@($manifest.excludedTrackedPaths).Count -ne 5 -or @($manifest.exclusionRules).Count -ne 5) { throw 'Exclusion inventory incomplete.' }
        if ($manifest.containsGitHistory -or $manifest.publicationPerformed -or $manifest.sourceCommit -cne $head) { throw 'Export provenance differs.' }
    }
    Check 'source-and-license-notices-retained-without-private-config' {
        foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'third-party/LICENSE.txt', 'src/example.txt', 'src/fictional-binary.bin', '.gitignore')) {
            if ($required -cnotin $names) { throw "Missing retained file: $required" }
        }
        if ('private.local' -cin $names) { throw 'Untracked private configuration leaked.' }
    }
    Check 'markdown-links-become-clear-export-only-placeholders' {
        $reader = [IO.StreamReader]::new($zip.GetEntry('docs/planning/references.md').Open())
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($content -match '\.png' -or -not $content.Contains('redistribution permission is not recorded') -or -not $content.Contains('[retained document](../../THIRD-PARTY-NOTICES.md)')) { throw 'Markdown has a dangling excluded image or lost a retained link.' }
        if (@($manifest.documentConversions).Count -ne 1 -or $manifest.documentConversions[0].originalSha256 -cne $before['docs/planning/references.md']) { throw 'Document transformation audit incomplete.' }
    }
    Check 'every-zip-file-size-and-hash-matches-the-machine-inventory' {
        if ($entries.Count -ne @($manifest.files).Count) { throw 'ZIP inventory count differs.' }
        foreach ($entry in $entries) {
            $records = @($manifest.files | Where-Object { $_.path -ceq $entry.FullName })
            if ($records.Count -ne 1 -or $records[0].size -ne $entry.Length) { throw 'ZIP inventory size differs.' }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($hash -cne $records[0].sha256) { throw 'ZIP inventory hash differs.' }
            if ($entry.FullName -ne 'docs/planning/references.md' -and $hash -cne $before[$entry.FullName]) { throw 'Unrelated source bytes changed.' }
        }
        $archiveHash = (Get-FileHash -LiteralPath $zipPath).Hash
        if ($archiveHash -cne $manifest.archiveSha256 -or (Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Raw) -cne "$archiveHash  Dango-public-source.zip`n") { throw 'Archive checksum differs.' }
    }
} finally { $zip.Dispose() }
Check 'existing-output-rejected-without-changing-export' {
    $beforeZip = (Get-FileHash -LiteralPath $zipPath).Hash
    Reject { & $exporter -RunName 'contract' -RepositoryRoot $fixture } 'Export output already exists'
    if ((Get-FileHash -LiteralPath $zipPath).Hash -cne $beforeZip) { throw 'Existing output changed.' }
}
Check 'dirty-tracked-and-untracked-inputs-rejected-before-output' {
    Write-Fixture 'src/example.txt' 'fictional dirty source'
    try { Reject { & $exporter -RunName 'dirty-tracked' -RepositoryRoot $fixture } 'clean committed worktree' }
    finally { Write-Fixture 'src/example.txt' "Fictional source bytes`n" }
    Write-Fixture 'fictional-untracked.txt' 'fictional untracked source'
    try { Reject { & $exporter -RunName 'dirty-untracked' -RepositoryRoot $fixture } 'clean committed worktree' }
    finally { [IO.File]::Delete((Join-Path $fixture 'fictional-untracked.txt')) }
    if ((Test-Path -LiteralPath (Join-Path $fixture 'artifacts/open-source/dirty-tracked')) -or (Test-Path -LiteralPath (Join-Path $fixture 'artifacts/open-source/dirty-untracked'))) { throw 'Dirty export created output.' }
}
Check 'original-source-head-status-and-all-tracked-bytes-unchanged' {
    if ((Git-Fixture @('rev-parse', 'HEAD')).Trim() -cne $head -or @(Git-Fixture @('status', '--porcelain', '--untracked-files=all')).Count) { throw 'Original Git state changed.' }
    foreach ($relative in $tracked) { if ((Get-FileHash -LiteralPath (Join-Path $fixture $relative)).Hash -cne $before[$relative]) { throw "Original source changed: $relative" } }
}
Write-Output "Public source export contracts: $script:passed passed. Fictional fixture preserved at $EvidenceRoot"

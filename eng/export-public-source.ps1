[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunName,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($RunName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') { throw 'Invalid export run name.' }
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$head = (& git -C $repo rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'Export requires a committed source HEAD.' }
$status = @(& git -C $repo status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw 'Export requires a clean committed worktree, including untracked files.' }
$excludedImages = @(
    'docs/planning/assets/2026-10-26-accessory-editor/compact-slider-reference.png',
    'docs/planning/assets/2026-10-26-accessory-editor/image-tools-reference.png',
    'docs/planning/assets/2026-10-26-accessory-editor/stacking-buttons-reference.png',
    'docs/planning/assets/2026-10-27-accessory-editor/canvas-layout-reference.png'
)
$tracked = @(& git -C $repo ls-tree -r --name-only HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate committed source.' }
$excluded = @($tracked | Where-Object { $_.StartsWith('artifacts/', [StringComparison]::Ordinal) -or $_ -cin $excludedImages })
$output = Join-Path $repo "artifacts/open-source/$RunName"
for ($cursor = $output; $cursor; $cursor = Split-Path -Parent $cursor) {
    if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Export paths must not traverse links.'
    }
}
if (Test-Path -LiteralPath $output) { throw 'Export output already exists; previous artifacts are preserved.' }

function Test-ExcludedImage([string]$Document, [string]$Url) {
    $urlValue = [Uri]::UnescapeDataString($Url.Trim('<', '>').Split([char[]]@('#', '?'))[0]).Replace('\', '/')
    if ($urlValue -match '^[A-Za-z][A-Za-z0-9+.-]*:' -or $urlValue.StartsWith('//')) { return $false }
    $segments = [Collections.Generic.List[string]]::new()
    $base = if ($urlValue.StartsWith('/')) { '' } else { $Document.Substring(0, [Math]::Max(0, $Document.LastIndexOf('/') + 1)) }
    foreach ($part in ($base + $urlValue.TrimStart('/')).Split('/')) {
        if ($part -eq '.' -or $part -eq '') { continue }
        if ($part -eq '..') { if ($segments.Count -gt 0) { $segments.RemoveAt($segments.Count - 1) }; continue }
        $segments.Add($part)
    }
    return (($segments -join '/') -cin $excludedImages)
}
function Convert-ReferenceImages([string]$Document, [string]$Content) {
    $placeholder = '[Reference image omitted from public source: redistribution permission is not recorded.]'
    $inline = '!?(\[(?<label>[^\]]*)\])\(\s*(?<url><[^>]*>|[^\s)]+)[^)]*\)'
    $result = [regex]::Replace($Content, $inline, [Text.RegularExpressions.MatchEvaluator]{ param($m)
        if (Test-ExcludedImage $Document $m.Groups['url'].Value) { return $placeholder }; return $m.Value
    })
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $result = [regex]::Replace($result, '(?m)^\s*\[(?<id>[^\]]+)\]:[ \t]*(?<url><[^>]*>|\S+)[^\r\n]*', [Text.RegularExpressions.MatchEvaluator]{ param($m)
        if (Test-ExcludedImage $Document $m.Groups['url'].Value) { $null = $ids.Add($m.Groups['id'].Value); return "> $placeholder" }; return $m.Value
    })
    $result = [regex]::Replace($result, '!?\[(?<label>[^\]]+)\](?:\[(?<id>[^\]]*)\])?', [Text.RegularExpressions.MatchEvaluator]{ param($m)
        $id = if ($m.Groups['id'].Success -and $m.Groups['id'].Value) { $m.Groups['id'].Value } else { $m.Groups['label'].Value }
        if ($ids.Contains($id)) { return $placeholder }; return $m.Value
    })
    $result = [regex]::Replace($result, '<img\b[^>]*\bsrc\s*=\s*["''](?<url>[^"'']+)["''][^>]*>', [Text.RegularExpressions.MatchEvaluator]{ param($m)
        if (Test-ExcludedImage $Document $m.Groups['url'].Value) { return $placeholder }; return $m.Value
    }, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $result
}

$null = [IO.Directory]::CreateDirectory($output)
$staging = Join-Path $output '.source-input.zip'
$zipPath = Join-Path $output 'Dango-public-source.zip'
$pathspecs = @('.', ':(exclude)artifacts') + @($excludedImages | ForEach-Object { ':(exclude)' + $_ })
# Git writes binary bytes directly. Never pipe archive output through PowerShell.
& git -C $repo archive --format=zip "--output=$staging" HEAD -- @pathspecs
if ($LASTEXITCODE -ne 0) { throw 'Source archive creation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inputZip = $null
$outputZip = $null
$records = [Collections.Generic.List[object]]::new()
$transforms = [Collections.Generic.List[object]]::new()
try {
    $inputZip = [IO.Compression.ZipFile]::OpenRead($staging)
    $outputZip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    foreach ($entry in $inputZip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $path = $entry.FullName.Replace('\', '/')
        if ($path.StartsWith('artifacts/') -or $path.StartsWith('.git/') -or $path -cin $excludedImages) { throw "Excluded source reached archive: $path" }
        $source = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try { $source.CopyTo($memory); $bytes = $memory.ToArray() } finally { $source.Dispose(); $memory.Dispose() }
        if ($path.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
            $textValue = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
            $converted = Convert-ReferenceImages $path $textValue
            if ($converted -cne $textValue) {
                $originalHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes($converted)
                $transforms.Add([ordered]@{ path = $path; operation = 'replace-excluded-reference-image-links-with-permission-placeholder'; originalSha256 = $originalHash })
            }
        }
        $newEntry = $outputZip.CreateEntry($path, [IO.Compression.CompressionLevel]::Optimal)
        $newEntry.LastWriteTime = $entry.LastWriteTime
        $target = $newEntry.Open()
        try { $target.Write($bytes, 0, $bytes.Length) } finally { $target.Dispose() }
        $records.Add([ordered]@{ path = $path; size = $bytes.Length; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) })
    }
} finally {
    if ($outputZip) { $outputZip.Dispose() }
    if ($inputZip) { $inputZip.Dispose() }
    # Only this invocation's generated scratch archive is removed.
    if (Test-Path -LiteralPath $staging) { [IO.File]::Delete($staging) }
}
$required = @('LICENSE', 'THIRD-PARTY-NOTICES.md')
foreach ($path in $required) { if ($path -cnotin @($records | ForEach-Object { $_.path })) { throw "Source export is missing $path" } }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$manifest = [ordered]@{
    format = 1; sourceCommit = $head; sourceDirty = $false; containsGitHistory = $false
    scope = 'public-source-candidate'; publicationPerformed = $false
    exclusionRules = @('artifacts/**') + $excludedImages
    excludedTrackedPaths = $excluded
    documentConversions = @($transforms.ToArray())
    archive = 'Dango-public-source.zip'; archiveSha256 = $zipHash
    files = @($records.ToArray() | Sort-Object { $_.path })
}
[IO.File]::WriteAllText((Join-Path $output 'source-manifest.json'), ($manifest | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$zipHash  Dango-public-source.zip`n", [Text.Encoding]::ASCII)
if ((& git -C $repo rev-parse HEAD).Trim() -cne $head) { throw 'Source HEAD changed during export.' }
$afterStatus = @(& git -C $repo status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or ($afterStatus -join "`n") -cne ($status -join "`n")) { throw 'Source worktree status changed during export.' }
Write-Output "Public source candidate (not published): $zipPath"

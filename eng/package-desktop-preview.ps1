[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RunName,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($RunName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') { throw 'Invalid output run name.' }
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-preview\.[1-9][0-9]*$') {
    throw 'Use a preview version such as 0.1.0-preview.1.'
}

function Assert-NoLinks([string]$Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = Split-Path -Path $cursor -Parent) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Paths must not traverse links: $Path"
        }
    }
}

function Write-Utf8Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
}

function Get-FileRecord([string]$Root, [string]$File) {
    return [ordered]@{
        path = $File.Substring($Root.Length + 1).Replace('\', '/')
        size = (Get-Item -LiteralPath $File).Length
        sha256 = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
    }
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'preview-distribution.psm1') -Force
$package = Test-DangoPackage -PackagePath $PackagePath
if ($package.Manifest.sourceDirty) { throw 'Preview packaging requires a clean source package.' }
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $package.Manifest.sourceCommit) { throw 'Package source must match this checkout HEAD.' }
$status = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) { throw 'Commit the release inputs before packaging.' }
$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package.Root 'Dango.Desktop.exe')).ProductVersion
if ($productVersion -cne $Version -and $productVersion -cne "$Version+$head") {
    throw "Executable version $productVersion does not match $Version and its source."
}

$docsRoot = Join-Path $repo "docs/releases/$Version"
$guide = Join-Path $docsRoot 'guide.txt'
$notes = Join-Path $docsRoot 'release-notes.md'
foreach ($inputPath in @($guide, $notes)) {
    Assert-NoLinks $inputPath
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing release input: $inputPath" }
}
$sourceLicenses = @(Get-DangoPreviewLicenseInputs -RepositoryRoot $repo -ApplicationRoot $package.Root)

# Locate license text from the exact runtime packs used by the existing publish.
$assetsPath = Join-Path (Split-Path -Path $package.Root -Parent) 'build/obj/Dango.Desktop/project.assets.json'
Assert-NoLinks $assetsPath
$assets = [IO.File]::ReadAllText($assetsPath) | ConvertFrom-Json
$runtimeConfig = [IO.File]::ReadAllText((Join-Path $package.Root 'Dango.Desktop.runtimeconfig.json')) | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
if ($frameworks.Count -ne 2 -or
    @($frameworks.name) -cnotcontains 'Microsoft.NETCore.App' -or
    @($frameworks.name) -cnotcontains 'Microsoft.WindowsDesktop.App') {
    throw 'This preview wrapper supports only the current self-contained desktop runtime pair.'
}
$licenseInputs = @()
foreach ($library in @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -ne 'project' })) {
    if ($library.Name -cne 'Microsoft.Web.WebView2/1.0.4258.31') {
        throw 'Additional package dependencies require an explicit license inventory before distribution.'
    }
    $dependencyRoot = Join-Path $repo 'src/Dango.Desktop/Accessories/Editing/licenses'
    $dependency = [IO.File]::ReadAllText((Join-Path $dependencyRoot 'dependency.json')) | ConvertFrom-Json
    if ($dependency.package -cne 'Microsoft.Web.WebView2' -or $dependency.version -cne '1.0.4258.31') {
        throw 'WebView2 package and license inventory disagree.'
    }
    $pack = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder 'microsoft.web.webview2/1.0.4258.31'
        if (Test-Path -LiteralPath $candidate -PathType Container) { $pack = $candidate; break }
    }
    if (-not $pack) { throw 'Missing restored WebView2 SDK.' }
    $archive = Join-Path $pack 'microsoft.web.webview2.1.0.4258.31.nupkg'
    Assert-NoLinks $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $dependency.packageSha256) {
        throw 'Restored WebView2 SDK does not match the audited package.'
    }
    foreach ($name in @('LICENSE.txt', 'NOTICE.txt')) {
        $file = Join-Path $pack $name
        Assert-NoLinks $file
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        if ($hash -cne (Get-FileHash -LiteralPath (Join-Path $dependencyRoot $name) -Algorithm SHA256).Hash) {
            throw 'WebView2 SDK license material changed.'
        }
        $licenseInputs += [pscustomobject]@{
            File = $file; Relative = "licenses/Microsoft.Web.WebView2/$name"; Package = 'Microsoft.Web.WebView2'
            Version = '1.0.4258.31'; Sha256 = $hash
        }
    }
}
foreach ($framework in $frameworks) {
    $id = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
    $pack = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($id + '/' + $framework.version)
        if (Test-Path -LiteralPath $candidate -PathType Container) { $pack = $candidate; break }
    }
    if (-not $pack) { throw "Missing restored runtime pack: $id/$($framework.version)" }
    $licenseNames = if ($framework.name -ceq 'Microsoft.NETCore.App') { @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') } else { @('LICENSE') }
    foreach ($name in $licenseNames) {
        $file = Join-Path $pack $name
        Assert-NoLinks $file
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing runtime license: $file" }
        $licenseInputs += [pscustomobject]@{
            File = $file; Relative = "licenses/$id/$name"; Package = $id; Version = $framework.version
            Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        }
    }
}

$output = Join-Path $repo "artifacts/preview-releases/$RunName"
Assert-NoLinks $output
if (Test-Path -LiteralPath $output) { throw 'Output must be new; previous artifacts are preserved.' }
$name = "Dango-v$Version-win-x64"
$bundle = Join-Path $output $name
$application = Join-Path $bundle 'app'
$null = New-Item -ItemType Directory -Path $output
$null = [IO.Directory]::CreateDirectory($application)

# Copy only the validated inventory, preserving the internal candidate verbatim.
foreach ($relative in @($package.Manifest.files.path) + @('package-manifest.json')) {
    $destination = Join-Path $application $relative
    $null = [IO.Directory]::CreateDirectory((Split-Path -Path $destination -Parent))
    [IO.File]::Copy((Join-Path $package.Root $relative), $destination, $false)
}
$copied = Test-DangoPackage -PackagePath $application
if ($copied.ManifestHash -cne $package.ManifestHash) { throw 'Copied candidate manifest changed.' }
Copy-Item -LiteralPath $guide -Destination (Join-Path $bundle 'README.zh-CN.txt')
Copy-Item -LiteralPath $notes -Destination (Join-Path $bundle 'RELEASE-NOTES.zh-CN.md')
$launcher = Join-Path $bundle 'Start-Dango.cmd'
[IO.File]::WriteAllText($launcher, "@echo off`r`nstart `"`" `"%~dp0app\Dango.Desktop.exe`"`r`n", [Text.Encoding]::ASCII)
Copy-DangoPreviewLicenses -Inputs (@($sourceLicenses) + @($licenseInputs)) -BundleRoot $bundle
$extraFiles = @(Get-ChildItem -LiteralPath $bundle -File -Recurse | Where-Object { -not $_.FullName.StartsWith($application + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object FullName | ForEach-Object { Get-FileRecord $bundle $_.FullName })
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $application 'Dango.Desktop.exe')
$release = [ordered]@{
    distributionFormat = 1; product = 'Dango'; version = $Version; runtime = 'win-x64'
    scope = 'local-single-player-preview'; preparationStatus = 'local-draft'; publicReleaseApproved = $false
    sourceCommit = $head; sourceDirty = $false; codeSigningStatus = [string]$signature.Status
    entryPoint = 'Start-Dango.cmd'; application = 'app/Dango.Desktop.exe'
    packageManifest = 'app/package-manifest.json'; packageManifestSha256 = $package.ManifestHash
    packageFileCount = @($package.Manifest.files).Count
    sourceLicenses = @($sourceLicenses | ForEach-Object { [ordered]@{ path = $_.Relative; sha256 = $_.Sha256 } })
    runtimeLicenses = @($licenseInputs | ForEach-Object { [ordered]@{ package = $_.Package; version = $_.Version; path = $_.Relative; sha256 = $_.Sha256 } })
    distributionFiles = $extraFiles
}
Write-Utf8Json (Join-Path $bundle 'release-manifest.json') $release
$unchanged = Test-DangoPackage -PackagePath $package.Root
if ($unchanged.ManifestHash -cne $package.ManifestHash) { throw 'Source package changed during packaging.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $output ($name + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($bundle, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$expectedEntries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath $bundle -File -Recurse) {
    $record = Get-FileRecord $bundle $file.FullName
    $expectedEntries.Add(($name + '/' + $record.path), $record)
}
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
$archiveFileCount = 0
try {
    foreach ($entry in $archive.Entries) {
        $entryName = $entry.FullName.Replace('\', '/')
        if ($entryName.EndsWith('/')) { continue }
        if (-not $expectedEntries.ContainsKey($entryName)) { throw "Unexpected or duplicate ZIP entry: $entryName" }
        $expected = $expectedEntries[$entryName]
        if ($entry.Length -ne $expected.size) { throw "ZIP entry size differs: $($entry.FullName)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($entryHash -cne $expected.sha256) { throw "ZIP entry hash differs: $($entry.FullName)" }
        $null = $expectedEntries.Remove($entryName)
        $archiveFileCount++
    }
    if ($expectedEntries.Count -ne 0) { throw 'ZIP is missing distribution files.' }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$zipHash  $name.zip`n", [Text.Encoding]::ASCII)
$summary = [ordered]@{
    version = $Version; sourceCommit = $head; sourceDirty = $false; archive = $name + '.zip'
    archiveBytes = (Get-Item -LiteralPath $zip).Length; archiveSha256 = $zipHash
    packageManifestSha256 = $package.ManifestHash; packageFileCount = @($package.Manifest.files).Count
    archiveEntriesVerified = $true; archiveFileCount = $archiveFileCount
    applicationTestsRun = $false; publicReleaseApproved = $false; createdAtUtc = [DateTime]::UtcNow.ToString('o')
}
Write-Utf8Json (Join-Path $output 'artifact-summary.json') $summary
Write-Output "Local preview draft: $zip"
Write-Output "SHA256: $zipHash"
Write-Output 'No tag, upload, installation, or application launch was performed.'

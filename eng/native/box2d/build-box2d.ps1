[CmdletBinding()]
param(
    [string]$SourceRoot,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$CMakePath
)

$ErrorActionPreference = 'Stop'
$box2dVersion = '3.1.1'
$box2dCommit = '8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3'
$archiveSha256 = 'FF94DDF7D75FFA6171502FDC7A7F336594560278978305CFC56C72268BC66721'
$embeddedLicenseSha256 = '68A3E676D7E94093B102D5CBA0D4E04AF812040D6F230C3DB67A6664574E43D2'
$acceptedSourceLicenseHashes = @(
    $embeddedLicenseSha256,
    '0085A12E93E9F9FDC25A3FA206042302F9298D3D472ADA43844C10CCAD700A0B'
)
$archiveUrl = "https://github.com/erincatto/box2d/archive/$box2dCommit.zip"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$artifactRoot = Join-Path $repositoryRoot 'artifacts\native-spike'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$sourceVerifiedByArchive = $false

if (-not $SourceRoot) {
    $archivePath = Join-Path $artifactRoot "box2d-$box2dCommit.zip"
    if (-not (Test-Path -LiteralPath $archivePath)) {
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    }
    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualArchiveHash -ne $archiveSha256) {
        throw "Box2D source archive hash mismatch. Expected $archiveSha256, got $actualArchiveHash."
    }

    $extractionRoot = Join-Path $artifactRoot "box2d-$box2dCommit"
    $SourceRoot = Join-Path $extractionRoot "box2d-$box2dCommit"
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot 'CMakeLists.txt'))) {
        if (Test-Path -LiteralPath $extractionRoot) {
            throw "Incomplete Box2D extraction already exists: $extractionRoot"
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionRoot
    }
    $sourceVerifiedByArchive = $true
}

$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
$sourceLicense = Join-Path $SourceRoot 'LICENSE'
if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot 'CMakeLists.txt')) -or
    -not (Test-Path -LiteralPath $sourceLicense)) {
    throw "SourceRoot is not a Box2D source tree: $SourceRoot"
}
if (-not $sourceVerifiedByArchive) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot '.git'))) {
        throw 'A custom SourceRoot must be a Git checkout so its exact commit can be verified.'
    }
    $actualCommit = (& git -C $SourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $box2dCommit) {
        throw "Box2D source commit mismatch. Expected $box2dCommit, got $actualCommit."
    }
}
$actualLicenseHash = (Get-FileHash -LiteralPath $sourceLicense -Algorithm SHA256).Hash
if ($actualLicenseHash -notin $acceptedSourceLicenseHashes) {
    throw "Box2D license hash mismatch. Got $actualLicenseHash."
}

if (-not $CMakePath) {
    $CMakePath = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CMakePath -or -not (Test-Path -LiteralPath $CMakePath)) {
    throw 'CMake was not found. Install the Visual Studio C++ CMake tools or pass -CMakePath.'
}

$buildRoot = Join-Path $artifactRoot "box2d-build-$Configuration"
& $CMakePath -S $PSScriptRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 "-DBOX2D_SOURCE_DIR=$SourceRoot"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed with exit code $LASTEXITCODE." }
& $CMakePath --build $buildRoot --config $Configuration --target dango_box2d
if ($LASTEXITCODE -ne 0) { throw "CMake build failed with exit code $LASTEXITCODE." }

$builtLibrary = Join-Path $buildRoot "$Configuration\dango_box2d.dll"
if (-not (Test-Path -LiteralPath $builtLibrary)) {
    throw "Native build did not produce $builtLibrary"
}

$nativeRelativePath = 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$nativePath = Join-Path $repositoryRoot ($nativeRelativePath.Replace('/', '\'))
$nativeDirectory = Split-Path -Parent $nativePath
New-Item -ItemType Directory -Force -Path $nativeDirectory | Out-Null
Copy-Item -LiteralPath $builtLibrary -Destination $nativePath -Force

$binaryHash = (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
$manifest = [ordered]@{
    name = 'Box2D'
    version = $box2dVersion
    tag = "v$box2dVersion"
    commit = $box2dCommit
    sourceArchive = [ordered]@{
        url = $archiveUrl
        sha256 = $archiveSha256
    }
    license = [ordered]@{
        path = 'LICENSE.box2d.txt'
        sha256 = $embeddedLicenseSha256
    }
    native = [ordered]@{
        rid = 'win-x64'
        path = $nativeRelativePath
        sha256 = $binaryHash
        abiVersion = 5
        runtimeDownload = $false
        linkage = 'MSVC static runtime; official Box2D linked into the Dango bridge DLL'
        exports = @(
            'dango_box2d_get_abi_version',
            'dango_box2d_get_version_major',
            'dango_box2d_get_version_minor',
            'dango_box2d_get_version_revision',
            'dango_box2d_run_lifecycle_probe',
            'dango_box2d_soft_body_create',
            'dango_box2d_soft_body_create_configured',
            'dango_box2d_soft_body_step',
            'dango_box2d_soft_body_destroy'
        )
    }
}
$manifestJson = ($manifest | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText(
    (Join-Path $PSScriptRoot 'dependency.json'),
    $manifestJson + "`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Built Box2D $box2dVersion ($box2dCommit) for win-x64"
Write-Output "Native DLL: $nativePath"
Write-Output "SHA-256: $binaryHash"

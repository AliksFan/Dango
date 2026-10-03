[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$RunName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PhysicsModulePath
)

$ErrorActionPreference = 'Stop'

$acceptedModuleHash = '00FF064A9A1A4DB67712403A8D01F19845A9EDF6B582D5FCA7CB9D0B567EF69C'
$acceptedBox2DHash = '6712D66ECEF9588123CAFCD868455080598B5B58711A698FB8AF022A0CDA191A'

if ($RunName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') {
    throw 'RunName must contain only letters, digits, dot, underscore, or hyphen, and cannot begin with punctuation.'
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-Sha256([string]$Path, [string]$ExpectedHash, [string]$Description) {
    $actualHash = Get-Sha256 $Path
    if ($actualHash -ne $ExpectedHash) {
        throw "$Description hash mismatch. Expected $ExpectedHash, got $actualHash."
    }
}

function Get-PackageFileRecord([string]$Root, [string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    return [ordered]@{
        path = $item.FullName.Substring($Root.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1).Replace('\', '/')
        size = $item.Length
        sha256 = Get-Sha256 $item.FullName
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$nativeInputs = & (Join-Path $PSScriptRoot 'test-desktop-publish-inputs.ps1') -PhysicsModulePath $PhysicsModulePath -RepositoryRoot $repoRoot
Import-Module (Join-Path $PSScriptRoot 'local-install\Dango.LocalInstall.psm1') -Force
$settingsContract = 'dango-desktop-settings-v4-preset-sidecar-v1'
$settingsSourceHashes = Get-DangoSettingsSourceContract -RepositoryRoot $repoRoot -SettingsContract $settingsContract
$projectPath = Join-Path $repoRoot 'src\Dango.Desktop\Dango.Desktop.csproj'
$oldLibraryPath = Join-Path $repoRoot 'src\Dango.Desktop\runtimes\win-x64\native\dango_box2d.dll'
$licensePath = Join-Path $repoRoot 'eng\native\box2d\LICENSE.box2d.txt'
$dependencyManifestPath = Join-Path $repoRoot 'eng\native\box2d\dependency.json'

$moduleFullPath = $nativeInputs.PhysicsModulePath
Assert-Sha256 $moduleFullPath $acceptedModuleHash 'Accepted physics module input'
Assert-Sha256 $oldLibraryPath $acceptedBox2DHash 'Protected Box2D source library'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw "Required license is missing: $licensePath" }
if (-not (Test-Path -LiteralPath $dependencyManifestPath -PathType Leaf)) { throw "Required dependency manifest is missing: $dependencyManifestPath" }
$licenseHash = Get-Sha256 $licensePath
$dependencyManifestHash = Get-Sha256 $dependencyManifestPath
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve the source commit.' }
$sourceDirty = @(& git -C $repoRoot status --porcelain).Count -gt 0
if ($LASTEXITCODE -ne 0) { throw 'Unable to read source working-tree status.' }

$entryRoot = Join-Path $repoRoot 'artifacts\desktop-entry'
$outputRoot = Join-Path $entryRoot $RunName
$packageRoot = Join-Path $outputRoot 'package'
$buildRoot = Join-Path $outputRoot 'build'
if (Test-Path -LiteralPath $outputRoot) {
    throw "Refusing to use existing output directory: $outputRoot"
}

# Do not create, remove, rebuild, launch, or stop anything before all protected inputs pass.
New-Item -ItemType Directory -Path $outputRoot -ErrorAction Stop | Out-Null

try {
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        --artifacts-path $buildRoot -o $packageRoot "-p:DangoPhysicsModulePath=$moduleFullPath" `
        '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed: $LASTEXITCODE" }

    $packageModulePath = Join-Path $packageRoot 'dango_physics_module.dll'
    $packageBox2DPath = Join-Path $packageRoot 'dango_box2d.dll'
    $packageLicensePath = Join-Path $packageRoot 'licenses\Box2D-LICENSE.txt'
    $packageDependencyManifestPath = Join-Path $packageRoot 'licenses\Box2D-dependency.json'
    $executablePath = Join-Path $packageRoot 'Dango.Desktop.exe'

    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw 'Published package is missing Dango.Desktop.exe.' }
    Assert-Sha256 $packageModulePath $acceptedModuleHash 'Packaged physics module'
    Assert-Sha256 $packageBox2DPath $acceptedBox2DHash 'Packaged Box2D library'
    if (-not (Test-Path -LiteralPath $packageLicensePath -PathType Leaf)) { throw 'Published package is missing Box2D license.' }
    if (-not (Test-Path -LiteralPath $packageDependencyManifestPath -PathType Leaf)) { throw 'Published package is missing Box2D dependency manifest.' }
    Assert-Sha256 $packageLicensePath $licenseHash 'Packaged Box2D license'
    Assert-Sha256 $packageDependencyManifestPath $dependencyManifestHash 'Packaged Box2D dependency manifest'
    Assert-Sha256 $oldLibraryPath $acceptedBox2DHash 'Protected Box2D source library after publish'
    Assert-Sha256 $moduleFullPath $acceptedModuleHash 'Accepted physics module input after publish'
    $null = Get-DangoSettingsSourceContract -RepositoryRoot $repoRoot -SettingsContract $settingsContract

    $manifestPath = Join-Path $packageRoot 'package-manifest.json'
    $records = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName |
        ForEach-Object { Get-PackageFileRecord $packageRoot $_.FullName })
    $packageManifest = [ordered]@{
        packageFormat = 2
        packageType = 'internal-candidate'
        sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty
        runtime = 'win-x64'
        selfContained = $true
        settingsContract = $settingsContract
        settingsSourceHashes = $settingsSourceHashes
        files = $records
    }
    [IO.File]::WriteAllText($manifestPath, (($packageManifest | ConvertTo-Json -Depth 4) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Output "Internal package candidate: $packageRoot"
    Write-Output "Package manifest: $manifestPath"
}
catch {
    # This script deliberately leaves the newly created output intact for diagnosis.
    # It never removes an existing output directory or modifies the protected inputs.
    throw
}

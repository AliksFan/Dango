[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Box2DSourceRoot,
    [Parameter(Mandatory)][string]$CMakePath,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$AcceptedFurnitureModule,
    [Parameter(Mandatory)][string]$AcceptedRoundBallModule,
    [Parameter(Mandatory)][string]$LegacyRoundBallModule,
    [Parameter(Mandatory)][string]$BeforePorchModule,
    [Parameter(Mandatory)][string]$AcceptedThreeModule
    , [Parameter(Mandatory)][string]$AcceptedCollectionBaselineModule
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$evidenceRoot = [IO.Path]::GetFullPath($OutputRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/furniture-collection-native')) + [IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $evidenceRoot)) {
    throw 'Use a new evidence directory inside artifacts/furniture-collection-native.'
}
foreach ($inputPath in @($CMakePath,$AcceptedFurnitureModule,$AcceptedRoundBallModule,$LegacyRoundBallModule,$BeforePorchModule,$AcceptedThreeModule,$AcceptedCollectionBaselineModule)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing read-only input: $inputPath" }
}
New-Item -ItemType Directory -Path $evidenceRoot | Out-Null
$protectedPath = Join-Path $repositoryRoot 'src/Dango.Desktop/runtimes/win-x64/native/dango_box2d.dll'
$protectedBefore = (Get-FileHash -LiteralPath $protectedPath).Hash
$manifest = [ordered]@{ protected_before=$protectedBefore; commands=@(); inputs=@(); outcomes=@() }
foreach ($inputPath in @($AcceptedFurnitureModule,$AcceptedRoundBallModule,$LegacyRoundBallModule,$BeforePorchModule,$AcceptedThreeModule,$AcceptedCollectionBaselineModule)) {
    $manifest.inputs += @{ path=[IO.Path]::GetFullPath($inputPath); sha256=(Get-FileHash -LiteralPath $inputPath).Hash }
}
function Invoke-Recorded {
    param([string]$Executable,[string[]]$Arguments,[string]$Log,[int]$ExpectedExit=0)
    $manifest.commands += @{ executable=$Executable; arguments=$Arguments; log=$Log }
    & $Executable @Arguments *> (Join-Path $evidenceRoot $Log)
    $observedExit = $LASTEXITCODE
    $manifest.outcomes += @{ log=$Log; expected_exit=$ExpectedExit; actual_exit=$observedExit }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'manifest.json')
    if ($observedExit -ne $ExpectedExit) { throw "Unexpected exit $observedExit for $Log (expected $ExpectedExit)." }
}
$buildRoot = Join-Path $evidenceRoot 'build'
$environmentNames = @('DANGO_PHYSICS_MODULE_PATH','DANGO_STATIC_PLATFORM_BASELINE_PATH','DANGO_ROUND_BALL_BASELINE_PATH',
    'DANGO_ROUND_BALL_LEGACY_PATH','DANGO_PHYSICS_BASELINE_PATH','DANGO_PHYSICS_ACCEPTED_THREE_PATH','DANGO_FURNITURE_COLLECTION_BASELINE_PATH')
$priorEnvironment = @{}
foreach ($environmentName in $environmentNames) { $priorEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName) }
Push-Location $repositoryRoot
try {
    Invoke-Recorded $CMakePath @('-S','eng/native/box2d','-B',$buildRoot,'-G','Visual Studio 17 2022','-A','x64',"-DBOX2D_SOURCE_DIR=$Box2DSourceRoot") 'configure.txt'
    Invoke-Recorded $CMakePath @('--build',$buildRoot,'--config','Release','--target','dango_physics_module',
        'dango_furniture_collection_tests_0','dango_furniture_collection_tests_1','dango_furniture_collection_tests_2',
        'dango_static_platform_tests','dango_round_ball_tests') 'build.txt'
    foreach ($variant in @(0,1,2)) {
        Invoke-Recorded (Join-Path $buildRoot "Release/dango_furniture_collection_tests_$variant.exe") @() "collection-$variant.txt" $(if($variant -eq 0){0}else{1})
    }
    Invoke-Recorded (Join-Path $buildRoot 'Release/dango_static_platform_tests.exe') @() 'legacy-platform.txt'
    Invoke-Recorded (Join-Path $buildRoot 'Release/dango_round_ball_tests.exe') @() 'legacy-ball.txt'
    $env:DANGO_PHYSICS_MODULE_PATH = Join-Path $buildRoot 'Release/dango_physics_module.dll'
    $env:DANGO_STATIC_PLATFORM_BASELINE_PATH = $AcceptedFurnitureModule
    $env:DANGO_ROUND_BALL_BASELINE_PATH = $AcceptedRoundBallModule
    $env:DANGO_ROUND_BALL_LEGACY_PATH = $LegacyRoundBallModule
    $env:DANGO_PHYSICS_BASELINE_PATH = $BeforePorchModule
    $env:DANGO_PHYSICS_ACCEPTED_THREE_PATH = $AcceptedThreeModule
    $env:DANGO_FURNITURE_COLLECTION_BASELINE_PATH = $AcceptedCollectionBaselineModule
    $filter = 'FullyQualifiedName~FurnitureCollectionTests|FullyQualifiedName~RoundBallTests|FullyQualifiedName~LiveFurniturePhysicsTests|FullyQualifiedName~PorchPhysicsBoundsTests|FullyQualifiedName~DangoPhysicsWorldTests|FullyQualifiedName~StaticPlatformTests|FullyQualifiedName~DangoPhysicsUnifiedModuleTests'
    Invoke-Recorded 'dotnet' @('test','tests/Dango.Physics.Tests/Dango.Physics.Tests.csproj','-c','Release','--filter',$filter,
        '--logger','trx;LogFileName=collection-and-regression.trx','--results-directory',(Join-Path $evidenceRoot 'managed-results')) 'managed-test.txt'
    $manifest.candidate_module = @{path=$env:DANGO_PHYSICS_MODULE_PATH;sha256=(Get-FileHash -LiteralPath $env:DANGO_PHYSICS_MODULE_PATH).Hash}
    $manifest.status = 'passed'
}
catch { $manifest.status='failed'; $manifest.error=$_.Exception.Message; throw }
finally {
    Pop-Location
    foreach ($environmentName in $environmentNames) { [Environment]::SetEnvironmentVariable($environmentName,$priorEnvironment[$environmentName]) }
    $manifest.protected_after=(Get-FileHash -LiteralPath $protectedPath).Hash
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'manifest.json')
    if ($manifest.protected_after -ne $protectedBefore) { throw 'Protected production DLL changed.' }
}

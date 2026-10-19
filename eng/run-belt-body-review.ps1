param(
    [string]$NativeModulePath,
    [string]$ReviewDirectory,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$binaryDirectory = Join-Path $taskRoot 'src/Dango.Desktop/bin/Release/net10.0-windows'
if (-not $ReviewDirectory) { $ReviewDirectory = Join-Path $taskRoot 'artifacts/belt-product/human-review' }
$ReviewDirectory = [IO.Path]::GetFullPath($ReviewDirectory)
$marker = Join-Path $ReviewDirectory '.dango-belt-body-review.json'
if (Test-Path -LiteralPath $ReviewDirectory) {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'Choose a new directory or an existing owned belt review directory.' }
    $owner = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($owner.application -ne 'Dango' -or $owner.beltBodyReviewFormat -ne 1 -or $owner.fictional -isnot [bool] -or $owner.fictional -ne $true -or @($owner.PSObject.Properties).Count -ne 3) {
        throw 'The directory does not carry the exact fictional review ownership marker.'
    }
} else {
    $null = New-Item -ItemType Directory -Path $ReviewDirectory
    [IO.File]::WriteAllText($marker, '{"application":"Dango","beltBodyReviewFormat":1,"fictional":true}')
}
# The application independently rejects daily/package directories and reparse points before reading settings.
if (-not $NoBuild) {
    if (-not $NativeModulePath) {
        $nativeBuild = Join-Path $taskRoot 'artifacts/belt-product/native'
        & cmake -S (Join-Path $taskRoot 'eng/native/box2d') -B $nativeBuild -G 'Visual Studio 17 2022' -A x64 -DDANGO_BELT_ONLY=ON
        if ($LASTEXITCODE -ne 0) { throw 'Belt native configuration failed.' }
        & cmake --build $nativeBuild --config Release --target dango_belt_product
        if ($LASTEXITCODE -ne 0) { throw 'Belt native build failed.' }
        $NativeModulePath = Join-Path $nativeBuild 'Release/dango_belt_product.dll'
    }
    $NativeModulePath = (Resolve-Path -LiteralPath $NativeModulePath).Path
    & dotnet build (Join-Path $taskRoot 'src/Dango.Desktop/Dango.Desktop.csproj') -c Release "-p:DangoBeltModulePath=$NativeModulePath"
    if ($LASTEXITCODE -ne 0) { throw 'Product build failed; no review window was launched.' }
}
$executable = Join-Path $binaryDirectory 'Dango.Desktop.exe'
if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath (Join-Path $binaryDirectory 'dango_belt_product.dll'))) {
    throw 'The isolated Release product and its independent belt DLL must be built first.'
}
# This explicitly requested review is an interactive window. It does not install or replace daily files.
$candidate = Start-Process -FilePath $executable -ArgumentList @('--belt-body-review', '--belt-body-review-settings', ('"' + $ReviewDirectory + '"')) -PassThru
[pscustomobject]@{ ProcessId = $candidate.Id; SettingsDirectory = $ReviewDirectory; SleepDefault = 'disabled-pending-F6'; Installed = $false }

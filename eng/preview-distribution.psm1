Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Keep the original candidate untouched. These legal files are required in the
# candidate as well as the outer source-license documents in the ZIP root.
function Get-DangoPreviewLicenseInputs {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot,
          [Parameter(Mandatory)][string]$ApplicationRoot)

    $requiredApplicationFiles = [ordered]@{
        'licenses/Box2D-LICENSE.txt' = 'eng/native/box2d/LICENSE.box2d.txt'
        'licenses/Box2D-dependency.json' = 'eng/native/box2d/dependency.json'
        'licenses/WebView2/LICENSE.txt' = 'src/Dango.Desktop/Accessories/Editing/licenses/LICENSE.txt'
        'licenses/WebView2/NOTICE.txt' = 'src/Dango.Desktop/Accessories/Editing/licenses/NOTICE.txt'
        'licenses/WebView2/dependency.json' = 'src/Dango.Desktop/Accessories/Editing/licenses/dependency.json'
        'AccessoryEditor/licenses/MIT-minipaint.txt' = 'src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-minipaint.txt'
        'AccessoryEditor/licenses/MIT-jquery.txt' = 'src/Dango.Desktop/Accessories/Editing/Web/licenses/MIT-jquery.txt'
        'AccessoryEditor/NOTICE.txt' = 'src/Dango.Desktop/Accessories/Editing/Web/NOTICE.txt'
    }
    foreach ($relative in $requiredApplicationFiles.Keys) {
        $file = Join-Path $ApplicationRoot $relative
        $source = Join-Path $RepositoryRoot $requiredApplicationFiles[$relative]
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing application legal input: $relative" }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
            throw "Application legal input differs from source: $relative"
        }
    }
    foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        $file = Join-Path $RepositoryRoot $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing distribution legal input: $name" }
        for ($cursor = [IO.Path]::GetFullPath($file); $cursor; $cursor = Split-Path -Parent $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Distribution legal input traverses a link: $name"
            }
        }
        [pscustomobject]@{ File = $file; Relative = $name; Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
}

function Copy-DangoPreviewLicenses {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Inputs,
          [Parameter(Mandatory)][string]$BundleRoot)
    foreach ($license in $Inputs) {
        $destination = Join-Path $BundleRoot $license.Relative
        $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
        [IO.File]::Copy($license.File, $destination, $false)
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $license.Sha256) {
            throw "Distribution legal copy changed: $($license.Relative)"
        }
    }
}

Export-ModuleMember -Function Get-DangoPreviewLicenseInputs, Copy-DangoPreviewLicenses

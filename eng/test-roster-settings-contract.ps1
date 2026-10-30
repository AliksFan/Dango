[CmdletBinding()]
param([string]$RepositoryRoot, [string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $RepositoryRoot ('artifacts/settings-contract-tests/' + [Guid]::NewGuid().ToString('N')) }
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (Test-Path -LiteralPath $EvidenceRoot) { throw 'The fixture evidence root must be new.' }
$null = New-Item -ItemType Directory -Path $EvidenceRoot
Import-Module (Join-Path $PSScriptRoot 'local-install/Dango.LocalInstall.psm1') -Force
$previousRoster = 'dango-desktop-settings-v4-roster-v2'
$contract = 'dango-desktop-settings-v4-roster-v2-startup-v1'
$results = New-Object Collections.ArrayList
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    try { $null = & $Action } catch { if ($_.Exception.Message -notlike $Expected) { throw }; return }
    throw 'Expected fixture rejection did not occur.'
}
function Case([string]$Name, [scriptblock]$Action) {
    try { & $Action; [void]$results.Add([ordered]@{ name = $Name; passed = $true }); Write-Host "PASS $Name" }
    catch { [void]$results.Add([ordered]@{ name = $Name; passed = $false; detail = $_.Exception.Message }); Write-Host "FAIL $Name $($_.Exception.Message)" }
}
function Contract-Digest($Fields) {
    $text = ''
    foreach ($key in @($Fields.Keys | Sort-Object)) { $text += $key + '=' + $Fields[$key] + "`n" }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
    finally { $sha.Dispose() }
}
$frozen = @{
    'dango-desktop-settings-v4' = '7A3085D2A13220597A5785AAFB450E99FC4689B608EBD6FBC3EC0141F53A61D3'
    'dango-desktop-settings-v4-preset-sidecar-v1' = '6C75AB3844647A396BF8907A8822F32DFDCE208DAA2AB3B04C4751320D4632CA'
    'dango-desktop-settings-v4-belt-default-v1' = '424EEB3CC12FA812E530F9F36709D03478410FB79F737141FD6187DA3C4950A8'
    'dango-desktop-settings-v4-belt-default-v2' = '8A6FA0F35A2C9A92DB5F9E33749C279EAD73E687AB9C92A504D4120380136AF0'
    'dango-desktop-settings-v4-belt-default-v3' = '4B1BD2FB99E980BA697FAA6B6C461CF4F979A55605FC190F5FEC86F351B42FBB'
    'dango-desktop-settings-v4-belt-default-v4' = '4FB6EDB7C149091D5A1024D728BFCD5F01A08C91F183F847B3CA637840453F35'
}
Case 'all-six-legacy-contracts-retain-their-exact-five-source-hashes' {
    foreach ($id in $frozen.Keys) {
        $fields = Get-DangoSettingsCompatibilityContract $id
        Assert-True ($fields.Count -eq 5) 'A legacy key set changed.'
        Assert-True ((Contract-Digest $fields) -ceq $frozen[$id]) 'A frozen legacy source hash changed.'
    }
}
Case 'original-roster-v2-retains-its-exact-seventeen-source-hashes' {
    $fields = Get-DangoSettingsCompatibilityContract $previousRoster
    Assert-True ($fields.Count -eq 17) 'The original roster-v2 key set changed.'
    Assert-True ((Contract-Digest $fields) -ceq '6C204DEABE3376CCCFA38536E4C59EC74401F418C62B0BA875FE68E3E31961F3') 'The original roster-v2 source hashes changed.'
}
Case 'new-contract-matches-all-seventeen-current-persistence-sources' {
    $expected = Get-DangoSettingsCompatibilityContract $contract
    $actual = Get-DangoSettingsSourceContract -RepositoryRoot $RepositoryRoot -SettingsContract $contract
    Assert-True ($actual.Count -eq 17 -and $actual.Count -eq $expected.Count) 'The current contract omitted a persistence source.'
    foreach ($key in $expected.Keys) { Assert-True ($expected[$key] -ceq $actual[$key]) ('Source differs: ' + $key) }
    $automatic = Get-DangoSettingsSourceContract -RepositoryRoot $RepositoryRoot
    Assert-True ((Contract-Digest $actual) -ceq (Contract-Digest $automatic)) 'Automatic detection returned a partial contract.'
}
Case 'every-single-source-change-and-missing-migration-source-is-rejected' {
    $repo = Join-Path $EvidenceRoot 'source-fixture'
    $expected = Get-DangoSettingsCompatibilityContract $contract
    foreach ($key in $expected.Keys) {
        $path = Join-Path $repo ('src/Dango.Desktop/' + $key)
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot ('src/Dango.Desktop/' + $key)) -Destination $path
    }
    $null = Get-DangoSettingsSourceContract -RepositoryRoot $repo -SettingsContract $contract
    foreach ($key in $expected.Keys) {
        $path = Join-Path $repo ('src/Dango.Desktop/' + $key); $bytes = [IO.File]::ReadAllBytes($path)
        [IO.File]::AppendAllText($path, ' ')
        Assert-Throws { Get-DangoSettingsSourceContract -RepositoryRoot $repo -SettingsContract $contract } 'Settings source contract changed*'
        Assert-Throws { Get-DangoSettingsSourceContract -RepositoryRoot $repo } 'Settings source contract changed*'
        [IO.File]::WriteAllBytes($path, $bytes)
    }
    $missing = Join-Path $repo 'src/Dango.Desktop/Dangos/LegacyDangoRosterDocument.cs'
    $bytes = [IO.File]::ReadAllBytes($missing)
    Remove-Item -LiteralPath $missing
    Assert-Throws { Get-DangoSettingsSourceContract -RepositoryRoot $repo -SettingsContract $contract } 'Settings source contract changed*'
    [IO.File]::WriteAllBytes($missing, $bytes)
}
$module = Get-Module Dango.LocalInstall
Case 'roster-contract-remains-belt-and-directory-scoped-and-publisher-selects-it' {
    $isBelt = & $module { param($id) $script:BeltSettingsContracts -ccontains $id } $contract
    Assert-True $isBelt 'The new contract lost belt package validation.'
    $scope = Join-Path $EvidenceRoot 'fictional-settings-scope'
    $current = & $module { param($id, $scope) Get-DangoInstalledInstanceName $id $scope } $contract $scope
    $originalRoster = & $module { param($id, $scope) Get-DangoInstalledInstanceName $id $scope } $previousRoster $scope
    $previous = & $module { param($scope) Get-DangoInstalledInstanceName 'dango-desktop-settings-v4-belt-default-v4' $scope } $scope
    Assert-True ($current -ceq $previous -and $current -ceq $originalRoster -and $current -like '*.Data.*') 'The directory-scoped instance identity changed.'
    $publisher = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'publish-desktop-entry.ps1'))
    Assert-True ($publisher.Contains("`$settingsContract = '" + $contract + "'")) 'The publisher uses an old contract.'
}
# Test only the actual activation state machine with in-memory package/state/writer doubles.
# No installed package, daily settings, process, shortcut, real activation or rollback is accessed.
& $module {
    Set-Item Function:Assert-DangoNoPendingToolsMigration -Value { param($Root) } -Force
    Set-Item Function:Assert-DangoProductStopped -Value { } -Force
    Set-Item Function:Get-DangoInstallState -Value { param($Root) return $script:FixtureState } -Force
    Set-Item Function:Get-DangoInstalledPackage -Value {
        param($Root, $VersionId)
        return [pscustomobject]@{ Manifest = [pscustomobject]@{ settingsContract = $script:FixturePackages[$VersionId] } }
    } -Force
    Set-Item Function:Write-DangoAtomicJson -Value { param($Path, $Value) $script:FixtureWrites++; $script:FixtureState = [pscustomobject]$Value } -Force
}
Case 'automatic-cross-generation-downgrade-is-blocked-before-any-state-write' {
    foreach ($rosterContract in @($previousRoster, $contract)) {
        foreach ($old in $frozen.Keys) {
        $answer = & $module {
            param($new, $old)
            $script:FixtureState = [pscustomobject]@{ current = 'new'; previous = 'old' }
            $script:FixturePackages = @{ new = $new; old = $old }; $script:FixtureWrites = 0
            try { $null = Set-DangoActiveVersionCore 'C:\fictional-contract-fixture' 'old'; throw 'Downgrade was accepted.' }
            catch {
                if ($_.Exception.Message -notlike 'Automatic downgrade*0010-dango-roster-editable-accessory-upgrade.md*') { throw }
            }
            return ($script:FixtureWrites -eq 0 -and $script:FixtureState.current -ceq 'new')
        } $rosterContract $old
        Assert-True $answer 'A cross-generation rejection changed installation state.'
        }
    }
}
Case 'initial-activation-forward-upgrade-same-generation-and-old-generation-switches-still-work' {
    foreach ($pair in @(@($null, $contract), @('dango-desktop-settings-v4-belt-default-v4', $contract), @($contract, $contract),
        @($previousRoster, $contract), @($contract, $previousRoster),
        @('dango-desktop-settings-v4', 'dango-desktop-settings-v4-belt-default-v4'))) {
        $answer = & $module {
            param($from, $to)
            $script:FixtureState = [pscustomobject]@{ current = $(if ($null -eq $from) { $null } else { 'from' }); previous = $null }
            $script:FixturePackages = @{ from = $from; to = $to }; $script:FixtureWrites = 0
            $null = Set-DangoActiveVersionCore 'C:\fictional-contract-fixture' 'to'
            return ($script:FixtureWrites -eq 1 -and $script:FixtureState.current -ceq 'to')
        } $pair[0] $pair[1]
        Assert-True $answer 'A supported activation was changed by the downgrade guard.'
    }
}
$failed = @($results | Where-Object { -not $_.passed }).Count
$summary = [ordered]@{ evidenceRoot = $EvidenceRoot; passed = $results.Count - $failed; failed = $failed; results = @($results) }
[IO.File]::WriteAllText((Join-Path $EvidenceRoot 'summary.json'), ($summary | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Evidence: $EvidenceRoot"
if ($failed -gt 0) { exit 1 }

[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArtifactRoot)
$ErrorActionPreference = 'Stop'
$ArtifactRoot = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$summary = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'summary.json') -Raw | ConvertFrom-Json
if (-not $summary.DiagnosticValid) { throw 'Cannot interpret a diagnostic that changed the frozen behavior.' }
# Same-build Windows x64 C layout: 40 bytes of metadata, 93 floats, 4 padding.
# This is project-owned test state, never a Box2D private format or checkpoint.
$stream = [IO.File]::OpenRead((Join-Path $ArtifactRoot 'stack-1.observations.bin'))
$reader = [IO.BinaryReader]::new($stream)
$selected = @()
try {
    if ($stream.Length % 416 -ne 0) { throw 'Unexpected diagnostic record layout.' }
    while ($stream.Position -lt $stream.Length) {
        $start = $stream.Position
        $tick = $reader.ReadUInt64(); $call = $reader.ReadUInt64()
        $substep = $reader.ReadInt32(); $entity = $reader.ReadInt32(); $phase = $reader.ReadInt32()
        $stage = $reader.ReadInt32(); $iteration = $reader.ReadInt32(); $reserved = $reader.ReadInt32()
        if ($reserved -ne 0 -or $substep -lt 1 -or $substep -gt 16 -or $entity -notin 1,2 -or $stage -lt 0 -or $stage -gt 3 -or $phase -notin 0,1 -or $iteration -lt 0 -or $iteration -gt 96) { throw 'Invalid diagnostic metadata.' }
        if ($tick -eq 42 -and $substep -eq 5 -and $entity -eq 1 -and $phase -eq 0) {
            $values = @(0..92 | ForEach-Object { $reader.ReadSingle() })
            if (@($values | Where-Object { -not [single]::IsFinite($_) }).Count) { throw 'Nonfinite failure state.' }
            $selected += [ordered]@{ Iteration = $iteration; Stage = $stage; Call = $call; Values = $values }
        }
        $stream.Position = $start + 416
    }
} finally { $reader.Dispose() }
if ($selected.Count -ne 98 -or $selected[0].Stage -ne 0 -or $selected[-1].Stage -ne 3 -or $selected[-1].Iteration -ne 96) { throw 'The expected failed 96-sweep call was not captured.' }
$last = $selected[-1].Values
$nodes = @(0..16 | ForEach-Object { [ordered]@{ Node = $_; X = $last[2*$_]; Y = $last[2*$_+1]; Vx = $last[34+2*$_]; Vy = $last[34+2*$_+1]; InverseMass = $last[68+$_] } })
$parameters = [ordered]@{ RadialLength = $last[85]; PerimeterLength = $last[86]; RadialMin = $last[87]; RadialMax = $last[88]; PerimeterMin = $last[89]; PerimeterMax = $last[90]; Radius = $last[91]; Ground = $last[92] }
$floor = [single]($last[92]+$last[91]); $minimum = [single]($last[85]*$last[87])
$height = [double]$floor - [double]$last[33]
$circleSquare = [double]$minimum*$minimum - $height*$height
if ($circleSquare -lt 0) { throw 'No real circle-floor intersection for this two-constraint calculation.' }
$horizontal = [Math]::Sqrt($circleSquare)
$dx = [double]$last[24]-[double]$last[32]; $dy = [double]$last[25]-[double]$last[33]
$length = [Math]::Sqrt($dx*$dx+$dy*$dy)
$radialCorrectionY = ([double]$minimum-$length)*$dy/$length
$eX = [double]$last[24]-[double]$last[22]; $eY = [double]$last[25]-[double]$last[23]
$fX = [double]$last[26]-[double]$last[24]; $fY = [double]$last[27]-[double]$last[25]
$middleAngleGradientY = -($eX/($eX*$eX+$eY*$eY)+$fX/($fX*$fX+$fY*$fY))
$terminalPositions = @($selected | Where-Object { $_.Stage -eq 1 -and $_.Iteration -ge 90 } | ForEach-Object {
    $bytes = [Collections.Generic.List[byte]]::new()
    foreach ($value in $_.Values[0..33]) { $bytes.AddRange([BitConverter]::GetBytes([single]$value)) }
    [ordered]@{ Iteration = $_.Iteration; PositionBits = [Convert]::ToHexString($bytes.ToArray()) }
})
$failedRows = @(Get-Content -LiteralPath (Join-Path $ArtifactRoot 'stack-1.failure.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$record = [ordered]@{
    Scope = 'Read-only same-build decoding and signed-gradient geometry; no additional world solve'
    RecordSize = 416; SelectedStates = $selected.Count; Tick = 42; Substep = 5; Entity = 1; Phase = 'direct self constraint'; SolverIterations = 96
    Parameters = $parameters; NodesAtFailedLocalExit = $nodes; TerminalIterations = $terminalPositions
    TerminalResiduals = @($failedRows | Select-Object -Last 8 | Select-Object Iteration,MaximumResidual,WorstKind,WorstNode)
    FinalRows = $failedRows[-1].Rows
    DistinctFinalPositionVectors = @($terminalPositions.PositionBits | Sort-Object -Unique).Count
    FloorY = $floor; MinimumRadialLength = $minimum; FloorRelativeToCoreY = $height
    Node12ExactlyAtFloor = ($last[25] -eq $floor); Node12RadialViolation = [double]$minimum-$length
    IsolatedRadialCorrectionY = $radialCorrectionY; IsolatedRawAngleMiddleGradientY = $middleAngleGradientY
    CircleFloorIntersectionHorizontalDistance = $horizontal
    CircleFloorIntersectionTargetX = @(([double]$last[32]-$horizontal),([double]$last[32]+$horizontal))
    CircleFloorIntersectionIsOnlyTwoConstraintGeometry = $true; GlobalShapeFeasibilityProven = $false; LiveWorldModified = $false
}
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'decoded-failure.json') -Encoding utf8
[pscustomobject]$record | Select-Object Tick,Substep,Entity,SolverIterations,Node12ExactlyAtFloor,Node12RadialViolation,IsolatedRadialCorrectionY,IsolatedRawAngleMiddleGradientY,CircleFloorIntersectionHorizontalDistance,DistinctFinalPositionVectors,GlobalShapeFeasibilityProven | ConvertTo-Json

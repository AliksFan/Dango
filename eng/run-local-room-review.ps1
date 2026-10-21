param(
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$')][string]$RunName,
    [switch]$Show
)
& (Join-Path $PSScriptRoot 'run-product-review.ps1') -RunName $RunName -Scenario 'local-room' -Show:$Show

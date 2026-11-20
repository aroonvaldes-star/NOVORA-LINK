# Compatibility entry point. NOVORA V1 is launched from the project root.
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'Start.ps1') @Arguments
exit $LASTEXITCODE

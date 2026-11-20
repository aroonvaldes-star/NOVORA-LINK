# Compatibility entry point. The experimental UI was not production-ready for V1.
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'Start.ps1') @Arguments
exit $LASTEXITCODE

[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$projects = @(Get-ChildItem (Join-Path $repo 'tests') -Recurse -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|Benchmark|NOVORA\.PhysicalRelayHarness)[\\/]' } |
    Sort-Object FullName)
if (-not $projects.Count) { throw 'No se encontraron suites de pruebas.' }
$failed = @()
foreach ($project in $projects) {
    Write-Host "Pruebas: $($project.Directory.Name)"
    & dotnet run --project $project.FullName -c $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) { $failed += $project.Directory.Name }
}
if ($failed.Count) { throw "Fallaron $($failed.Count) suites: $($failed -join ', ')" }
Write-Host "$($projects.Count) suites correctas. No se ejecutaron pruebas físicas ni Benchmark."

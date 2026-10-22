#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$AppPath = 'C:\Program Files\NOVORA\NOVORA.exe',
    [switch]$Apply,
    [switch]$Undo,
    [string]$BackupPath = ''
)

$ErrorActionPreference = 'Stop'
$AppPath = [IO.Path]::GetFullPath($AppPath)
if (-not $Undo -and -not (Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw "No existe el ejecutable: $AppPath" }
if ($Apply -and $Undo) { throw 'Elige Apply o Undo.' }
if ([string]::IsNullOrWhiteSpace($BackupPath)) {
    $BackupPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ve-startup-20261012\firewall-before.clixml'
}
$BackupPath = [IO.Path]::GetFullPath($BackupPath)
$hashAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $suffix = ([BitConverter]::ToString($hashAlgorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($AppPath.ToLowerInvariant())))).Replace('-', '').Substring(0, 12) }
finally { $hashAlgorithm.Dispose() }
$ruleNames = @('TCP', 'UDP') | ForEach-Object { "NOVORA-PrivateLAN-$suffix-$_" }

if ($Undo) {
    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) { throw 'No existe la copia de las reglas anteriores.' }
    $backup = Import-Clixml -LiteralPath $BackupPath
    if ($backup.AppPath -ne $AppPath) { throw 'La copia pertenece a otro ejecutable.' }
} else {
    $blocks = @(Get-NetFirewallRule -Direction Inbound -Enabled True -Action Block | Where-Object {
        $program = ($_ | Get-NetFirewallApplicationFilter).Program
        $program -eq $AppPath -and ([int]$_.Profile -eq 0 -or ([int]$_.Profile -band 2) -ne 0)
    })
    Write-Output "Programa: $AppPath"
    Write-Output 'Permitir TCP/UDP solo en perfil Private, desde LocalSubnet y para este programa.'
    Write-Output 'Conservar los bloqueos del programa en los perfiles restantes.'
    $blocks | Select-Object Name, Profile, Enabled, PolicyStoreSourceType
    if (-not $Apply) {
        Write-Output 'Vista previa; no se modificó el firewall. Usa -Apply para aplicar o -Apply -WhatIf para simular.'
        return
    }
    if ($blocks | Where-Object { $_.PolicyStoreSourceType -ne 'Local' }) {
        throw 'Hay bloqueos administrados por una política externa; no se modificaron.'
    }
    $hasBackup = Test-Path -LiteralPath $BackupPath -PathType Leaf
    if ($hasBackup) {
        $backup = Import-Clixml -LiteralPath $BackupPath
        if ($backup.AppPath -ne $AppPath) { throw 'La copia existente pertenece a otro ejecutable.' }
    } else {
        $backup = [PSCustomObject]@{
            AppPath = $AppPath
            Blocks = @($blocks | Select-Object Name, Profile, Enabled)
            AddedRules = @($ruleNames)
        }
    }
    for ($ruleIndex = 0; $ruleIndex -lt $ruleNames.Count; $ruleIndex++) {
        $existing = Get-NetFirewallRule -Name $ruleNames[$ruleIndex] -ErrorAction SilentlyContinue
        if (-not $existing) { continue }
        if (-not $hasBackup -or $backup.AddedRules -notcontains $ruleNames[$ruleIndex]) {
            throw 'Una regla con este nombre ya existe y no pertenece a esta copia. No se modificó.'
        }
        $program = ($existing | Get-NetFirewallApplicationFilter).Program
        $addresses = @(($existing | Get-NetFirewallAddressFilter).RemoteAddress)
        $ports = $existing | Get-NetFirewallPortFilter
        $expectedProtocol = @('TCP', 'UDP')[$ruleIndex]
        $protocolNumber = @(6, 17)[$ruleIndex]
        if ($program -ne $AppPath -or [int]$existing.Profile -ne 2 -or $existing.Enabled -ne 'True' -or
            $existing.Action -ne 'Allow' -or $existing.Direction -ne 'Inbound' -or
            $addresses.Count -ne 1 -or $addresses[0] -ne 'LocalSubnet' -or
            ($ports.Protocol -ne $expectedProtocol -and $ports.Protocol -ne $protocolNumber) -or
            $ports.LocalPort -ne 'Any' -or $ports.RemotePort -ne 'Any') {
            throw 'Una regla propia cambió de alcance. No se modificó; revisa la regla antes de repetir Apply.'
        }
    }
    $newBlocks = @($blocks | Where-Object { $_.Name -notin @($backup.Blocks.Name) } |
        Select-Object Name, Profile, Enabled)
    $backup.Blocks = @($backup.Blocks) + $newBlocks
}

if (-not $WhatIfPreference) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Ejecuta este script desde una PowerShell como administrador para aplicar o revertir.'
    }
}
if (-not $PSCmdlet.ShouldProcess($AppPath, $(if ($Undo) { 'Restaurar reglas previas' } else { 'Habilitar LAN privada para NOVORA' }))) { return }

if ($Undo) {
    foreach ($rule in $backup.Blocks) {
        Set-NetFirewallRule -Name $rule.Name -Profile $rule.Profile -Enabled $rule.Enabled
    }
    foreach ($ruleName in $backup.AddedRules) {
        Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
    Write-Output 'Restauradas las reglas anteriores del programa.'
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $BackupPath) | Out-Null
$backup | Export-Clixml -LiteralPath $BackupPath
foreach ($rule in $blocks) {
    $remainingProfiles = if ([int]$rule.Profile -eq 0) { 5 } else { [int]$rule.Profile -band 5 }
    if ($remainingProfiles -eq 0) { Disable-NetFirewallRule -Name $rule.Name }
    else { Set-NetFirewallRule -Name $rule.Name -Profile $remainingProfiles }
}
for ($ruleIndex = 0; $ruleIndex -lt $ruleNames.Count; $ruleIndex++) {
    $protocol = @('TCP', 'UDP')[$ruleIndex]
    if (-not (Get-NetFirewallRule -Name $ruleNames[$ruleIndex] -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name $ruleNames[$ruleIndex] -DisplayName "NOVORA LAN privada $protocol" `
            -Direction Inbound -Action Allow -Profile Private -Program $AppPath `
            -Protocol $protocol -RemoteAddress LocalSubnet | Out-Null
    }
}
Write-Output "LAN privada habilitada para el programa. Copia para Undo: $BackupPath"

# The real repair script runs against in-memory NetSecurity collaborators.
# This test does not read or change the Windows firewall.
$ErrorActionPreference = 'Stop'
$repairScript = Join-Path $PSScriptRoot '..\scripts\Repair-NOVORA-PrivateLanFirewall.ps1'
$versionsRoot = if ($env:NOVORA_VERSIONES_DIR) { $env:NOVORA_VERSIONES_DIR } else { Join-Path $PSScriptRoot '..\..\..\Versiones' }
$testRoot = Join-Path $versionsRoot ('Generados\NOVORA-LINK\ve-startup-20261012\firewall-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$appPath = [IO.Path]::GetFullPath((Join-Path $testRoot 'NOVORA.exe'))
Set-Content -LiteralPath $appPath -Value 'test fixture only'
$backupPath = Join-Path $testRoot 'before.clixml'
$global:novoraTestRules = @{}
$global:novoraTestIsAdministrator = $true

# The production administrator guard stays intact. Its principal collaborator,
# like the NetSecurity commands below, is simulated for this in-memory fixture.
function New-Object {
    [CmdletBinding()] param([Parameter(Position=0)][string]$TypeName,
        [Parameter(Position=1)][object[]]$ArgumentList)
    if ($TypeName -ne 'Security.Principal.WindowsPrincipal') { throw "Unexpected fixture factory: $TypeName" }
    $principal = [PSCustomObject]@{}
    $principal | Add-Member -MemberType ScriptMethod -Name IsInRole -Value {
        param($Role)
        return $global:novoraTestIsAdministrator
    }
    return $principal
}

function New-TestBlock([string]$Name) {
    [PSCustomObject]@{ Name=$Name; Profile=6; Enabled='True'; PolicyStoreSourceType='Local';
        Direction='Inbound'; Action='Block'; Program=$appPath; Protocol='TCP'; RemoteAddress='Any' }
}
function Get-NetFirewallRule {
    [CmdletBinding()] param([string]$Name, [string]$Direction, [string]$Enabled, [string]$Action)
    @($global:novoraTestRules.Values) | Where-Object {
        (!$Name -or $_.Name -eq $Name) -and (!$Direction -or $_.Direction -eq $Direction) -and
        (!$Enabled -or $_.Enabled -eq $Enabled) -and (!$Action -or $_.Action -eq $Action)
    }
}
function Get-NetFirewallApplicationFilter {
    param([Parameter(ValueFromPipeline=$true)]$InputObject)
    process { [PSCustomObject]@{Program=$InputObject.Program} }
}
function Get-NetFirewallAddressFilter {
    param([Parameter(ValueFromPipeline=$true)]$InputObject)
    process { [PSCustomObject]@{RemoteAddress=$InputObject.RemoteAddress} }
}
function Get-NetFirewallPortFilter {
    param([Parameter(ValueFromPipeline=$true)]$InputObject)
    process { [PSCustomObject]@{Protocol=$InputObject.Protocol; LocalPort='Any'; RemotePort='Any'} }
}
function Set-NetFirewallRule {
    [CmdletBinding()] param([string]$Name, $Profile, $Enabled, $Direction, $Action)
    foreach ($property in @('Profile','Enabled','Direction','Action')) {
        if ($PSBoundParameters.ContainsKey($property)) { $global:novoraTestRules[$Name].$property=$PSBoundParameters[$property] }
    }
}
function Disable-NetFirewallRule { param([string]$Name) $global:novoraTestRules[$Name].Enabled='False' }
function New-NetFirewallRule {
    [CmdletBinding()] param($Name,$DisplayName,$Direction,$Action,$Profile,$Program,$Protocol,$RemoteAddress)
    $global:novoraTestRules[$Name]=[PSCustomObject]@{Name=$Name; Direction=$Direction; Action=$Action;
        Profile=2; Enabled='True'; Program=$Program; Protocol=$Protocol; RemoteAddress=$RemoteAddress}
    $global:novoraTestRules[$Name]
}
function Remove-NetFirewallRule {
    param([Parameter(ValueFromPipeline=$true)]$InputObject)
    process { $global:novoraTestRules.Remove($InputObject.Name) }
}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }

$global:novoraTestRules['initial']=New-TestBlock 'initial'
& $repairScript -AppPath $appPath -BackupPath $backupPath | Out-Null
Require ($global:novoraTestRules['initial'].Profile -eq 6 -and $global:novoraTestRules.Count -eq 1) 'Preview changed firewall state.'
& $repairScript -AppPath $appPath -BackupPath $backupPath -Apply -WhatIf | Out-Null
Require ($global:novoraTestRules['initial'].Profile -eq 6 -and $global:novoraTestRules.Count -eq 1) 'WhatIf changed firewall state.'
$global:novoraTestIsAdministrator = $false
$nonAdminRejected = $false
try { & $repairScript -AppPath $appPath -BackupPath $backupPath -Apply -Confirm:$false | Out-Null }
catch { $nonAdminRejected = $_.Exception.Message -like '*administrador*' }
Require $nonAdminRejected 'The production non-administrator guard must remain enforced.'
Require ($global:novoraTestRules['initial'].Profile -eq 6 -and $global:novoraTestRules.Count -eq 1 -and !(Test-Path -LiteralPath $backupPath)) 'The rejected non-admin Apply changed state.'
$global:novoraTestIsAdministrator = $true
& $repairScript -AppPath $appPath -BackupPath $backupPath -Apply -Confirm:$false | Out-Null
Require ($global:novoraTestRules['initial'].Profile -eq 4) 'Apply failed to preserve Public blocking.'
$created=@($global:novoraTestRules.Values | Where-Object {$_.Action -eq 'Allow'})
Require ($created.Count -eq 2) 'Apply must create TCP and UDP rules.'
foreach ($rule in $created) {
    Require ($rule.Program -eq $appPath -and $rule.Profile -eq 2 -and $rule.RemoteAddress -eq 'LocalSubnet') 'LAN exception escaped its program/private/subnet scope.'
}
& $repairScript -AppPath $appPath -BackupPath $backupPath -Undo -Confirm:$false | Out-Null
Require ($global:novoraTestRules['initial'].Profile -eq 6 -and $global:novoraTestRules.Count -eq 1) 'Undo failed to restore the first application.'

& $repairScript -AppPath $appPath -BackupPath $backupPath -Apply -Confirm:$false | Out-Null
$global:novoraTestRules['later']=New-TestBlock 'later'
& $repairScript -AppPath $appPath -BackupPath $backupPath -Apply -Confirm:$false | Out-Null
& $repairScript -AppPath $appPath -BackupPath $backupPath -Undo -Confirm:$false | Out-Null
Require ($global:novoraTestRules['later'].Profile -eq 6) 'Undo lost a block encountered by a repeated Apply.'

$existingName=$created[0].Name
$global:novoraTestRules[$existingName]=[PSCustomObject]@{Name=$existingName; Direction='Inbound'; Action='Allow';
    Profile=6; Enabled='False'; Program='other-program.exe'; Protocol='UDP'; RemoteAddress='Any'}
$rejected=$false
try { & $repairScript -AppPath $appPath -BackupPath (Join-Path $testRoot 'unowned.clixml') -Apply -Confirm:$false | Out-Null }
catch { $rejected=$true }
Require $rejected 'An unowned existing allow-rule name must be rejected.'
Require ($global:novoraTestRules[$existingName].Profile -eq 6 -and $global:novoraTestRules[$existingName].Enabled -eq 'False' -and
    $global:novoraTestRules['initial'].Profile -eq 6) 'Rejecting an unowned rule must not modify it or existing blocks.'
Write-Output 'PASS: firewall preview, private scope, repeated Apply/Undo, and unowned-rule rejection.'


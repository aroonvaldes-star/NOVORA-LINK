[CmdletBinding()]
param([string]$RepoPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference = 'Stop'
function Read-UsbSource([string]$Relative) { [IO.File]::ReadAllText((Join-Path $RepoPath $Relative)) }
function Check-UsbSource([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL SOURCE: $Name" }
    Write-Host "PASS SOURCE: $Name" -ForegroundColor Green
}
$pc = Read-UsbSource 'src\NOVORA\UI\NLUIWindowMainAndroidControl.cs'
$auto = Read-UsbSource 'src\NOVORA\UI\NLUIWindowMainAutomaticUsb.cs'
$discovery = Read-UsbSource 'src\NOVORA\UI\NLUIWindowMainDiscoveryLifecycle.cs'
$ui = Read-UsbSource 'src\NOVORA.Android\AndroidUI\NLAndroidUIActivity.cs'
$svc = Read-UsbSource 'src\NOVORA.Android\AndroidService\NLAndroidServiceControlAutomaticUsb.cs'
$bridge = Read-UsbSource 'src\NOVORA.Android\AndroidUI\NLAndroidUIUsbBootstrapActivity.cs'
$session = Read-UsbSource 'src\NOVORA\Control\NLControlSession.cs'
$client = Read-UsbSource 'src\NOVORA\Control\NLControlClient.cs'
$engines = Read-UsbSource 'src\NOVORA\UI\NLUIWindowMainAndroidEngines.cs'
Check-UsbSource ($pc.Contains('InitializeAutomaticUsb();') -and $pc.Contains('QueueAutomaticUsb();')) 'PC usa carga y cambios de dispositivo'
Check-UsbSource ($discovery -match 'DevicesChangedNV\s*\+=\s*AutomaticUsbDevicesChanged') 'Reutiliza el tracker existente'
Check-UsbSource ($auto.Contains('_automaticUsb.Observe') -and $auto.Contains('_automaticUsb.TryBegin')) 'Politica probada gobierna el arranque real'
Check-UsbSource (-not ($auto -match 'Task\.Delay|PeriodicTimer|DispatcherTimer|new\s+Timer')) 'No agrega descubrimiento periodico'
Check-UsbSource ($auto.Contains('CheckAutomaticUsb') -and $auto.Contains('transport: "USB"')) 'Operacion ligada a generacion y transporte USB'
Check-UsbSource ($ui.Contains('RunAutomaticUsbAsync') -and $ui.Contains('ConnectAutomaticUsbAsync')) 'Invitacion ejecuta conexion sin pulsar boton'
Check-UsbSource ($ui.Contains('_automaticUsbResumed') -and $ui.Contains('protected override void OnResume()')) 'Espera activity resumida y servicio enlazado'
Check-UsbSource (-not $ui.Contains('GetStringExtra("novora.usb")')) 'Launcher no consume invitaciones de apps externas'
Check-UsbSource ($bridge.Contains('Permission = "android.permission.DUMP"')) 'Entrada bootstrap protegida'
Check-UsbSource ($session.Contains('client.ConnectLanAsync(invitation, true)') -and $client.Contains('CryptographicOperations.FixedTimeEquals')) 'Conserva TLS y pinning USB'
Check-UsbSource ($engines.Contains('_androidControl?.IsAuthorized == true') -and $engines.Contains('device.Connected && !device.IsWifiConnection')) 'No falsea autorizacion de LinkEngine'
Check-UsbSource ($ui.Contains('VpnService.Prepare(this)') -and $ui.Contains('EnableActions(!_busy)')) 'Permiso VPN y capacidades reales conservados'
Check-UsbSource ($svc.Contains('NOVORA_USB_EVIDENCE_V1') -and -not $svc.Contains('Log.Info("NOVORA-USB", text')) 'Evidencia sin credenciales'
Write-Host 'SOURCE CONTRACTS: PASS (no equivalen a una prueba fisica)' -ForegroundColor Green
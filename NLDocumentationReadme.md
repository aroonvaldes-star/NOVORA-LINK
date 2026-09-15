# NOVORA-LINK: codigo de escritorio

El [bloque 3 de Android](Documentation/NLDocumentationAndroidSession.md) añade servicio de continuidad USB/LAN y notificaciones de control. Se comprobó código y núcleo de sesión; servicio físico en Samsung y distribución APK siguen pendientes.

## Nueva app Android: control USB y LAN

Esta entrega añade una aplicación Android nueva para controlar NOVORA PC 1.4 por USB o LAN con invitación QR y detección puntual. Consulta el [bloque 1](Documentation/NLDocumentationAndroidControl.md) y el [bloque 2](Documentation/NLDocumentationAndroidLan.md). La antigua APK de Internet continúa retirada: el enlace de control nuevo no restaura la VPN ni Internet USB. La validación física PC–Android y la distribución firmada siguen pendientes.

Entrega de codigo Windows WPF con la integracion de la antigua APK NOVORA retirada. ADB y scrcpy se conservan para las funciones generales de conexion y captura. El boton de Internet USB dependiente de la app retirada permanece deshabilitado. El control de la nueva app y sus límites de validación se documentan arriba.

## Nombres
Los archivos propios usan Motor + Carpeta + Funcion. LE identifica LinkEngine, VE VisionEngine, ST STEngine y NL los componentes comunes. Ejemplos: UI/NLUIWindowMain.xaml, Application/NLApplicationApp.xaml y Tool/NLToolVerify.py. Documentation/NLDocumentationNamingMap.json relaciona los nombres anteriores con los nuevos.

Los nombres exigidos por herramientas externas (Cargo.toml, Cargo.lock) y los binarios/licencias de terceros conservan sus nombres originales. El ejecutable propio del relay se llama LENetworkRelay.exe. Los nombres publicos de propiedades, claves persistidas y simbolos nativos se conservan cuando son contratos de funcionamiento.

## Compilar desde PowerShell
Requiere Windows y el SDK .NET indicado en el proyecto. La restauracion inicial necesita acceso a NuGet. Para reconstruir RelayCore se requiere Rust/Cargo y las herramientas nativas de Windows.

```powershell
dotnet build .\NLProjectSolution.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion' }
dotnet test .\tests\NOVORA.Tests\NLProjectTests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas' }
python .\Tool\NLToolVerify.py
if ($LASTEXITCODE -ne 0) { throw 'Fallo la verificacion' }
```

Es un paquete de codigo con herramientas auxiliares, no un instalador. Los resultados generados bin, obj y target no se distribuyen. La investigacion historica se conserva en la carpeta original y no forma parte de este paquete de codigo actualizado. No se incluye el historial Git ni se publica esta reorganizacion en GitHub.

## NVIDIA opcional
El código específico y sus referencias se centralizan en [NVIDIA](src/NOVORA/NVIDIA/NLNVIDIAArquitectura.md). VisionEngine conserva la coordinación del video; la configuración compartida mantiene sus claves.


Android: [bloque 4 — confianza y reconexión LAN](Documentation/NLDocumentationAndroidTrust.md).


Android: [bloque 5 — motores e Internet USB](Documentation/NLDocumentationAndroidEngines.md).


Android: [bloque 6 — instalación y actualización desde HOME](Documentation/NLDocumentationAndroidInstall.md).

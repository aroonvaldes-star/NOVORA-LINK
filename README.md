# NOVORA-LINK

NOVORA-LINK conecta una computadora Windows con un dispositivo Android y organiza sus funciones en motores independientes.

> **Estado de esta carpeta:** NOVORA-LINK 1.4 PRERELEASE PRE FINAL. Es candidata a la rama principal, no una declaración de Release estable.

## Motores

- **LinkEngine (LE):** conectividad/reverse tethering y transporte de red.
- **VisionEngine (VE):** pantalla, audio, control, gamepad, intercambio e integración Android↔Windows.
- **ExInEngine:** entrada externa y sesiones de control independientes.
- **NL / común:** aplicación, interfaz, servicios, modelos, control y descubrimiento.
- **NLNVIDIA:** integración/aceleración NVIDIA opcional; VisionEngine conserva su fallback cuando corresponda.

STEngine y sus adaptadores de estabilidad no forman parte de este baseline. Sus expedientes anteriores se conservan como historia de auditoría, pero no representan una función disponible.

Los motores deben poder detenerse o recuperarse de forma independiente siempre que la función no requiera explícitamente otra capa.

## Dispositivo

La arquitectura vigente está orientada a **un único Android activo por computadora**. Los límites históricos de cinco clientes DATA se redujeron a uno en esta candidata.

## Aplicaciones

### Windows
Proyecto: `src/NOVORA/NLProjectDesktop.csproj`

Interfaz: panel dinámico WPF en `src/NOVORA/UI/`.

### Android
Proyecto: `src/NOVORA.Android/NLProjectAndroid.csproj`

Package ID: `com.novora.appcontrol`

El proyecto Android exige empaquetado explícito. Para validación de código use `-t:Compile`; no fuerce un APK salvo que la prueba realmente lo necesite.

## Nomenclatura

Prefijos vigentes:

| Prefijo | Área |
|---|---|
| `NL` | NOVORA-LINK / común |
| `LE` | LinkEngine |
| `VE` | VisionEngine |
| `NLNVIDIA` | NVIDIA |

La autoridad para migraciones de nombres es:

`Documentation/NLDocumentationNamingMap.json`

No introduzca nombres antiguos sin comprobar primero ese mapa.

## Zero Polling

NOVORA usa **Zero Polling por defecto**. Se prefieren eventos, callbacks, push, socket readiness, notificaciones del sistema, estado en memoria y snapshots bajo demanda.

Los timers siguen permitidos cuando son necesarios para timeout, deadline, debounce, pacing o backoff; esos usos no son sondeo por sí mismos.

## Privacidad

NOVORA minimiza almacenamiento de información personal. No debe guardar innecesariamente contraseñas, cuentas, correos, contactos, OTP, tokens, cookies, historial del portapapeles ni contenido privado.

Consulte `SECURITY.md` y `Documentation/NLDocumentationProjectRules.md`.

## Manual

- Español: `Documentation/Manual/ES/NLDocumentationManualUsuarioES.md`
- English: `Documentation/Manual/EN/NLDocumentationUserManualEN.md`

El manual español es prioritario. Ambas apps exponen acceso al manual.

## Compilación / validación

En Windows, use:

```powershell
Set-Location "$env:USERPROFILE\Desktop\NOVORA-LINK"

dotnet build .\NOVORA.sln `
  -c Release `
  -p:UseSharedCompilation=false

if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación Release de NOVORA PC."
}

dotnet build .\src\NOVORA.Android\NLProjectAndroid.csproj `
  -c Release `
  -p:NovoraPackage=true `
  -p:UseSharedCompilation=false

if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación Release de NOVORA Android."
}

$novoraExe = Join-Path $PWD "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\NOVORA.exe"
$novoraDirectory = Split-Path -Parent $novoraExe
$androidApk = Join-Path $PWD "src\NOVORA.Android\bin\Release\net10.0-android\com.novora.appcontrol-Signed.apk"

if (-not (Test-Path -LiteralPath $novoraExe)) {
    throw "No se encontró NOVORA.exe: $novoraExe"
}

if (-not (Test-Path -LiteralPath $androidApk)) {
    throw "No se encontró el APK: $androidApk"
}

Write-Host ""
Write-Host "Compilación completa." -ForegroundColor Green
Write-Host "PC:      $novoraExe"
Write-Host "Android: $androidApk"
Write-Host ""

Start-Process `
  -FilePath $novoraExe `
  -WorkingDirectory $novoraDirectory
```

El gate ejecuta validaciones por bloque y no confunde build con prueba física.

## Instalador

Fuente Inno Setup:

`Tool/NLInstallerSetup.iss`

La prerelease instala NOVORA por usuario y deja acceso a NOVORA y ambos manuales desde el Escritorio.

## Terceros

NOVORA conserva temporalmente componentes de terceros mientras no exista una sustitución propia validada. Consulte:

- `THIRD-PARTY-NOTICES.md`
- `ACKNOWLEDGEMENTS.md`
- `Legal/`

No se reclama autoría sobre componentes de terceros.

## Licencia

El código original de NOVORA-LINK se distribuye bajo la `NOVORA-LINK COMMUNITY PROPRIETARY LICENSE (NLCPL) v1.0`. Consulte `LICENSE`. Los componentes de terceros mantienen sus licencias propias.

## Estado funcional

Consulte `Documentation/Release/NLDocumentationPreFinalFunctionalAnalysis.md`. Ese documento diferencia entre:

- implementado;
- conectado;
- verificado estáticamente;
- pendiente de build Windows;
- pendiente de prueba física;
- pendiente de terminar.

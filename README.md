# NOVORA-LINK

NOVORA-LINK conecta una computadora Windows con un dispositivo Android y organiza sus funciones en motores independientes.

> **Estado de esta carpeta:** NOVORA-LINK 1.4 PRERELEASE PRE FINAL. Es candidata a la rama principal, no una declaración de Release estable.

## Motores

- **LinkEngine (LE):** conectividad/reverse tethering y transporte de red.
- **VisionEngine (VE):** pantalla, audio, control, gamepad, intercambio e integración Android↔Windows.
- **STEngine (ST):** medición técnica de NOVORA.
- **NL / común:** aplicación, interfaz, servicios, modelos, control y descubrimiento.
- **NLNVIDIA:** integración/aceleración NVIDIA opcional; VisionEngine conserva su fallback cuando corresponda.

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
| `ST` | STEngine |
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tool\NLToolReleaseGate.ps1
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

## Estado funcional

Consulte `Documentation/Release/NLDocumentationPreFinalFunctionalAnalysis.md`. Ese documento diferencia entre:

- implementado;
- conectado;
- verificado estáticamente;
- pendiente de build Windows;
- pendiente de prueba física;
- pendiente de terminar.

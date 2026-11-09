# Conectividad dinámica USB/LAN — implementación fases A y B

Fecha: 2026-11-08 · Autor: Claude (Opus 5.5) · Plan de referencia:
`docs/superpowers/plans/2026-11-08-conectividad-dinamica.md` (Codex).

## Decisión sobre el plan

El plan de Codex era correcto en el diagnóstico pero ponía primero una identidad
criptográfica por instalación (Tarea 1) antes de cualquier resultado visible. Se
ejecutó por fases, empezando por lo que el usuario pidió: LINK Control siempre
conectado con NOVORA LINK y motores iniciables por USB o LAN sin elegir ruta.

- **Fase A (PC Linux):** hecha y probada.
- **Fase B (LINK Control):** hecha; **no compilada** en este equipo (sin SDK
  Android ni .NET 10). Versión subida por regla n+1 a 1.4.125.
- **Fase C (recuperación de motores al cambiar de ruta)** y **Fase D (ExIn LAN sin
  cable, identidad criptográfica)**: pendientes.

## Fase A — NOVORA-LINUX

| Archivo | Cambio |
|---|---|
| `Control/LinuxAutoConnectPolicy.cs` (nuevo) | Política pura: elección de interfaz LAN, decisión USB, reintentos 0/1/2/4/8/15 s. |
| `Control/AdbDeviceTracker.cs` (nuevo) | `adb track-devices -l` con tramas ADB, reinicio con backoff, sin huérfanos al salir. |
| `Control/LinuxAutoConnector.cs` (nuevo) | Coordinador: LAN sigue la red activa; USB se enlaza al conectar el cable; avisos sin repetición. |
| `Control/LinuxControlHost.cs` | Implementa `IAutoConnectTarget`; gate de rutas UI/coordinador; arranques idempotentes; `SelectTransport` ya no bloquea. |
| `Control/LinuxConnectionSelection.cs` | Motores usan la ruta autorizada (USB preferido); la captura activa conserva su ruta. |
| `App.cs`, `MainWindow*.cs` | Arranque automático solo en la app real; Inicio muestra «Conectado / Conectando / Reconectando»; botones manuales sincronizados; parada manual = pausa. |

Política aplicada:

1. LAN escucha sola en la interfaz física con gateway; sigue cambios de red.
2. USB autorizado en ADB → bootstrap protegido automático. Sesión cerrada con cable
   conectado → nuevo bootstrap a los 2 s. Bootstrap no confirmado en 20 s → reintento;
   máximo 3 por conexión del cable (cada uno abre LINK Control en el teléfono).
3. Dos teléfonos USB sin elección previa → se pide elegir; nunca se elige en silencio.
4. LAN con motores activos no se mueve a USB al conectar el cable; LAN inactiva sí.
5. Estado ADB desconocido (servidor reiniciando) nunca cierra rutas.
6. ADB por Wi-Fi no se considera USB.

## Fase B — LINK Control (sin compilar)

| Archivo | Cambio |
|---|---|
| `AndroidService/NLAndroidServiceDynamicConnection.cs` (nuevo) | Tras una sesión iniciada por ADB: guarda la PC (`trust.enroll`), la busca en LAN por huella, guarda la ruta LAN, la deja como recuperación y como PC automática al abrir. |
| `AndroidService/NLAndroidServiceControl.cs` | Recuperación sin límite (1, 2, 4, 8 y luego 15 s) mientras esté en primer plano; USB manual también prepara la ruta LAN. |
| `AndroidService/NLAndroidServiceControlAutomaticUsb.cs` | Lanza la preparación LAN tras confirmar el túnel ADB. |
| `NLProjectAndroid.csproj` | n+1: `ApplicationVersion` 125 / `1.4.125`. APK, descriptor y hashes siguen en 1.4.124 hasta compilar. |

Límites conocidos: la credencial creada por USB sirve para LAN en Linux (el almacén
de confianza no depende del transporte). En Windows no se verificó; si Windows la
rechaza por LAN, la recuperación se detiene con el mensaje de identidad rechazada.
El almacén Android conserva una entrada por huella: la ruta LAN reemplaza la USB
guardada para esa PC. Un teléfono que nunca se enlazó por cable necesita el código
LAN una vez.

## Verificación

- Build Release Linux y pruebas: 0 advertencias, 0 errores.
- `--auto-connect` (nuevo), `--control-only` (nuevo), `--connection-selection`,
  `--windows-ui`, `--exin-ui`, `--ve-ui`: PASS.
- Suite completa sin flags: sigue fallando en `TestFfmpegDecoderAsync` (timeout del
  entorno, **preexistente**, antes de cualquier cambio de esta sesión).
- Android: solo verificación sintáctica con Roslyn (sin errores CS1xxx); los errores
  restantes son tipos de Android ausentes, idénticos en el archivo original.
- Físico (SM-A566E por ADB Wi-Fi, LINK Control 1.4.124): al abrir la app sin tocar
  nada, el control LAN quedó escuchando en `10.0.0.8:27215`, descubrimiento UDP 27216
  y `adb track-devices` activo; el teléfono por ADB Wi-Fi no se trató como USB. El
  teléfono no tenía esta PC guardada, por lo que no se conectó solo por LAN (esperado
  con 1.4.124). Pendiente: prueba con cable, emparejamiento LAN y APK 1.4.125.

Sin commit, push, instalación de APK ni emparejamiento de depuración.

## Ampliación (misma sesión): ADB Wi-Fi, datos móviles y actualizador

### ADB Wi-Fi como ruta de enlace
Con autorización del usuario, un teléfono con depuración inalámbrica emparejada se enlaza
como por cable (bootstrap protegido + `adb reverse`), solo si no hay cable y el teléfono no
está ya conectado por LAN. La UI lo muestra como «ADB Wi-Fi». **Probado físicamente**:
SM-A566E con LINK Control 1.4.124 mostró «Conectado a Aroonvaldes-star» sin código, con
VisionEngine/LinkEngine disponibles.

### Datos móviles (plan de Codex `2026-11-08-datos-moviles-ahorro-claude.md`)
Implementado y probado en esta máquina:
- Contrato compartido `NOVORA LINK/Control/NLControlMobileData.cs`: perfiles Extremo
  (120 kbit/s · 5 fps · 480 px), Ahorro (250 kbit/s · **30 fps** por petición del usuario ·
  720 px) y Solo control; política con generación/revisión; contador bidireccional de 64 bits
  con tope que no se reinicia al reconectar y que, al alcanzarse, bloquea medios.
- Capacidad aditiva `MobileData` en `NLControlNetworkCapabilities`. Sin ella se conservan
  los mínimos 250 kbit/s y 10 fps (política de captura y encoder).
- Encoder Android: VBR solo en captura móvil y si el codec lo admite; el estado `UsesVbr`
  refleja la configuración realmente aplicada (fallback informado).
- Linux: perfiles «Móvil extremo/ahorro» en PC y en las opciones que ve Android; el PC no
  envía ofertas móviles a un LINK Control que no anunció `MobileData`; las ofertas móviles
  activan `CaptureFramePacing` (Android descarta cuadros antes del encoder) y apagan audio.
- Android 1.4.125 anuncia `MobileData` y acepta los mínimos móviles solo en esas ofertas.

Pendiente del plan de Codex: ruta remota WAN (sin VPN/servidor de prueba disponible: bloqueo),
selector de tope y contadores en la UI Android, conteo en todos los canales, Solo control
en vivo y mediciones físicas de consumo.

### Actualizador de LINK Control desde GitHub
`AndroidUI/UpdateLINKGitHub.cs` (portable) consulta la última release estable pública de
`aroonvaldes-star/LINK-CONTROL`, exige que APK y descriptor pertenezcan a ese repositorio y
etiqueta, valida paquete/versión/`MinSdk`, descarga y verifica tamaño y SHA-256, e instala con
`PackageInstaller`. Búsqueda automática cada 6 h; descarga automática solo en red no medida y
sin motores activos; botón «Buscar actualización». La autenticidad la garantiza Android: solo
instala sobre la app actual con la misma firma. Prueba real: encontró 1.4.124, descargó
14 035 363 bytes y verificó el SHA-256.

### Firma
La 1.4.124 publicada está firmada con «CN=Android Debug», SHA-256 del certificado
`0B:64:42:82:56:B7:B8:D2:6A:7E:A0:57:FD:0E:01:41:24:07:28:E9:18:9A:1B:CE:38:12:BA:5A:58:72:5A:22`
(creada 2026-10-04). Ese keystore no está en este equipo ni en el disco montado.

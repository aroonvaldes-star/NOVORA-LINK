# NOVORA-LINK — Política de Privacidad / Privacy Policy

> **Español (principal) · English below**

**Última actualización / Last updated: 5 de septiembre de 2026 / September 5, 2026**

## Español

NOVORA-LINK está diseñado para operar principalmente de forma local entre una PC con Windows y dispositivos Android autorizados por el usuario.

### Principios

NOVORA-LINK no debe utilizarse intencionalmente para:

- recolectar contraseñas o credenciales;
- realizar seguimiento publicitario oculto;
- vender información personal;
- enviar contenido de pantalla, audio, portapapeles o archivos a terceros sin una función explícita que lo requiera;
- conservar permanentemente contenido sensible sin una acción clara del usuario.

### Datos almacenados localmente

NOVORA-LINK puede almacenar preferencias locales, por ejemplo en:

`%LocalAppData%\NOVORA\settings.json`

Estas preferencias pueden incluir configuración de audio, monitor, dispositivo seleccionado, identificadores técnicos, bitrate, FPS, resolución, tema y nombres personalizados.

### Comunicación con Android

Cuando el usuario conecta y autoriza un dispositivo, NOVORA-LINK puede procesar la información técnica necesaria para:

- detectar el dispositivo;
- iniciar sesiones ADB;
- instalar o actualizar componentes Android requeridos;
- proporcionar conectividad mediante LinkEngine;
- transmitir pantalla, audio y eventos de entrada mediante VisionEngine o mecanismos de compatibilidad;
- intercambiar portapapeles o archivos cuando el usuario activa esas funciones.

El contenido procesado por estas funciones debe permanecer dentro de la sesión necesaria para ejecutarlas, salvo que el usuario lo envíe deliberadamente mediante un servicio externo.

### LinkEngine y tráfico de red

Cuando el usuario activa reverse tethering, VPN o conectividad mediante LinkEngine, el tráfico del dispositivo puede utilizar la conexión de Windows para acceder a Internet. Los sitios, aplicaciones y proveedores utilizados por el dispositivo podrán observar la información normal asociada a esas conexiones.

El tráfico transportado por LinkEngine no debe considerarse telemetría de NOVORA únicamente por atravesar el motor.

### VisionEngine

VisionEngine puede procesar frames de pantalla, audio, eventos de entrada, portapapeles y datos de intercambio para ejecutar las funciones solicitadas por el usuario. NOVORA-LINK no debe conservar estos contenidos por defecto más allá de lo técnicamente necesario para la sesión, salvo una acción explícita como guardar, transferir o grabar.

### GitHub y actualizaciones

NOVORA-LINK puede comunicarse con GitHub mediante HTTPS para consultar releases, descargar componentes o verificar actualizaciones. GitHub puede recibir la información normal de una petición de red conforme a sus propias políticas.

### Telemetría

La línea 1.3 no incorpora un backend propio de analítica publicitaria o perfiles de usuario. Si una versión futura introduce cuentas, telemetría remota, sincronización en la nube o recolección adicional, esta política deberá actualizarse antes de su publicación y la interfaz deberá explicar qué datos se recopilan y para qué.

### Eliminación de datos locales

El usuario puede cerrar NOVORA-LINK y eliminar `%LocalAppData%\NOVORA\settings.json` para borrar preferencias locales. Los archivos temporales de `%TEMP%\NOVORA\` pueden eliminarse cuando no exista una operación en curso.

### Terceros

NOVORA-LINK integra o utiliza software de terceros sujeto a sus propias licencias y políticas. Consulta `THIRD-PARTY-NOTICES.md` y `ACKNOWLEDGEMENTS.md`.

---

## English

NOVORA-LINK is designed to operate primarily locally between a Windows PC and Android devices authorized by the user.

### Principles

NOVORA-LINK must not intentionally collect passwords or credentials, perform hidden advertising tracking, sell personal information, transmit screen/audio/clipboard/file content to unrelated third parties without an explicit feature requiring it, or permanently retain sensitive content without clear user action.

### Locally stored data

NOVORA-LINK may store local preferences, for example in `%LocalAppData%\NOVORA\settings.json`. These may include audio, monitor, selected-device, technical identifier, bitrate, FPS, resolution, theme and custom-name settings.

### Android communication

After the user connects and authorizes a device, NOVORA-LINK may process technical information needed for device discovery, ADB sessions, Android component provisioning, LinkEngine connectivity, VisionEngine screen/audio/input operation, clipboard exchange and file transfer.

### LinkEngine and network traffic

When reverse tethering, VPN or LinkEngine connectivity is enabled, Android traffic may access the Internet through the Windows connection. Network destinations and providers may observe normal connection metadata. Traffic carried by LinkEngine is not NOVORA analytics merely because it passes through the engine.

### VisionEngine

VisionEngine may process screen frames, audio, input events, clipboard information and exchange data to perform user-requested features. Such content should not be retained by default beyond what is technically required for the session unless the user explicitly saves, transfers or records it.

### GitHub and updates

NOVORA-LINK may communicate with GitHub over HTTPS for releases, downloads or updates. GitHub may receive normal network-request information under its own policies.

### Telemetry

The 1.3 line does not include a NOVORA-owned advertising analytics or user-profiling backend. If future versions introduce accounts, remote telemetry, cloud synchronization or additional collection, this policy must be updated before release and the interface must explain what is collected and why.

### Local-data deletion

Users may close NOVORA-LINK and delete `%LocalAppData%\NOVORA\settings.json` to remove local preferences. Temporary `%TEMP%\NOVORA\` files may be removed when no operation is running.

### Third parties

NOVORA-LINK integrates or uses third-party software subject to its own licenses and policies. See `THIRD-PARTY-NOTICES.md` and `ACKNOWLEDGEMENTS.md`.

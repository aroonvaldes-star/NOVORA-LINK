# Diseño de estabilidad y capacidad de datos para LE y VE

Estado: propuesta para implementación. Fecha: 13 de octubre de 2026.

NOVORA debe proteger la latencia durante juego, video y transferencias simultáneas. El usuario eligió reducir primero las descargas y después la calidad de video cuando sea necesario. La capacidad útil se mejora eliminando sobrecarga y esperas evitables; el software no aumenta la capacidad física contratada.

El escenario informado incluye un router principal Technicolor y un TP-Link C20 usado como expansor. Otros dispositivos descargan directamente desde el router. NOVORA puede regular sus propias rutas; el control global de esos dispositivos corresponde al punto de salida de la red. No se presume cómo están conectados ambos routers ni su velocidad.

## Situación actual comprobada en el código

Checkout: E:/NOVORA/NOVORA LINK/GITHUB/NL. HEAD de referencia: 5afbabc003fbbffac7aa50fee29a452eeb6a5e22, con trabajo local sin commit y 15 commits remotos por reconciliar. AppControl declara 1.4.101/code101. Estos documentos se apoyan en el árbol local, que incluye NovoraRelay y archivos todavía sin seguimiento.

| Área | Situación | Consecuencia para el diseño |
| --- | --- | --- |
| LE DATA | IPv4 dentro de un TCP compartido; proxy TCP/UDP; contadores LE_METRIC | Hace falta regulación agregada y por flujo. La política no elimina bloqueos de retransmisión del TCP exterior. |
| LE SpeedTest | Cinco muestras HTTP antes de cargar 8 MiB | Hay que medir también durante subida y bajada; RTT HTTP debe conservar su etiqueta. |
| VE LAN | TCP/TLS separados para VIDEO, AUDIO, CONTROL y FILES | Un limitador dentro del relay LE no controla esos sockets VE. |
| FILES v1 | Bloque útil 24 KiB, hexadecimal 48 KiB, JSON anidado, una confirmación por bloque | Sobrecarga aproximada 2:1 del payload y espera secuencial por respuesta. |
| FILES límites | JSON máximo 65 536 bytes; mensaje interior 51 000 caracteres; archivo hasta 2 GiB; timeout 30 s | No basta aumentar el bloque. V2 requiere negociación independiente. |
| Encoder Android | Cola de 8 paquetes, recuperación por keyframe; SetParameters solo solicita keyframe | Faltan edad/bytes de cola y ajuste vivo del bitrate. |
| Compositor | Presenta cada frame recibido al encoder | El límite de FPS debe aplicarse antes de codificar. |
| VE PC | Métricas de decode/render y renderer de 2 frames con último frame | Los descartes del renderer no prueban pérdida de red ni ahorran tráfico ya recibido. |
| Ajustes de calidad | Guardar calidad detiene VE; no hay actuator activo de bitrate | La adaptación necesita operación nueva, negociada, distinta de reiniciar video. |

Fuentes de código: NLControlFileTransfer.cs, NLControlProtocol.cs, VEExchangeLanFileSender.cs, VETransportLanSession.cs, NLAndroidVideoEncoder.cs, NLAndroidVideoCompositor.cs, NLAndroidServiceVeLanCapture.cs, NLAndroidVideoService.cs, VEPerformanceManager.cs y NovoraRelay/src/main.rs. Las rutas completas y tareas están en los planes enlazados.

## Enfoques evaluados

| Enfoque | Beneficio | Coste y límite |
| --- | --- | --- |
| Solo límites manuales | Entrega pequeña, reversible y comprobable | No adapta calidad ni elimina espera por ACK de FILES. |
| Presupuestos por ruta, adaptación VE y FILES v2 | Aborda congestión y capacidad útil manteniendo clientes antiguos | Requiere contrato compartido, coordinación PC/Android y pruebas físicas. Es el enfoque recomendado por etapas. |
| Sustituir DATA por transporte multiplexado nuevo | Puede reducir bloqueo entre flujos ante pérdidas | Mayor cambio de compatibilidad y seguridad. Se evalúa después del baseline, fuera de la primera entrega. |

## Arquitectura y alcance de los presupuestos

Se distinguen dos dominios: Internet y enlace del dispositivo. Internet tiene subida Android→Internet y bajada Internet→Android. El enlace del dispositivo tiene Android→PC y PC→Android. La correspondencia de sentidos está explícita; los nombres TX/RX sin punto de observación no determinan el presupuesto.

Cada ruta se identifica con LinkKey, generación de sesión, transporte físico comprobado, interfaz y dispositivo. USB físico, ADB por Wi-Fi y LAN no se agrupan solo porque lleven al mismo serial. LE y VE comparten presupuesto únicamente cuando la asociación al mismo cuello físico está comprobada o configurada expresamente. La subida WAN no se resta sin más de la bajada local.

La PC toma decisiones para toda la sesión. Android aplica los límites de su dirección y devuelve estado. El proceso Rust aplica la asignación de LE. FILES, encoder y escritores VE aplican su parte. Los presupuestos concedidos a consumidores de una misma ruta no pueden superar su techo. La medición debe indicar datos útiles, bytes de protocolo y si se dispone del consumo real del enlace; TLS/ADB/controladores añaden costes que la aplicación no observa íntegramente.

Control/audio y tráfico interactivo tienen servicio acotado y preferente; video respeta el techo elegido por el usuario; transferencias usan el remanente. Un flujo no gana prioridad solo por usar UDP o paquetes pequeños. La reserva preferente máxima inicial es 20 % del presupuesto de la ruta, salvo audio/control esenciales declarados que requieran más. El resto usa reparto justo. Si la capacidad no alcanza para lo esencial, se informa capacidad insuficiente y se reduce video; no se promete continuidad garantizada.

## Contrato común propuesto

Los tipos nuevos se definen en NLControlNetworkPolicy.cs y se compilan desde el mismo archivo en PC y Android. Dirección FromAndroid corresponde a subida WAN o Android→PC; ToAndroid corresponde a bajada WAN o PC→Android. Domain distingue Internet de DeviceLink.

~~~csharp
public enum NLNetworkDomain { Internet, DeviceLink }
public enum NLNetworkDirection { FromAndroid, ToAndroid }
public enum NLNetworkTrafficClass { Interactive, Audio, Video, BestEffort, Bulk }
public enum NLNetworkPolicyMode { Disabled, Observe, Manual, Adaptive }
public enum NLNetworkSampleSource { UsbControl, LanControl, TunnelHttp,
    InternetPcHttp, InternetAndroidHttp, Pipeline }
public sealed record NLNetworkPolicy(long Revision, long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkPolicyMode Mode,
    long? FromAndroidBytesPerSecond, long? ToAndroidBytesPerSecond);
public sealed record NLNetworkSample(long Generation, string LinkKey, NLNetworkDomain Domain,
    NLNetworkDirection Direction, NLNetworkSampleSource Source,
    long ObservedAtTicks, double WindowSeconds,
    double? RttMs, double? WriteWaitP95Ms, double? QueueAgeP95Ms,
    long QueueBytes, long UsefulBytes, long ProtocolBytes, long Dropped,
    bool Valid);
public sealed record NLNetworkGrant(long Revision, long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkDirection Direction, NLNetworkTrafficClass TrafficClass,
    long BytesPerSecond, int BurstBytes, long ValidUntilTicks);
public sealed record NLNetworkBaseline(long Generation, string LinkKey,
    NLNetworkDomain Domain, NLNetworkSampleSource Source,
    double RttP95Ms, long ObservedAtTicks, bool QuietCalibrationConfirmed);
public sealed record NLControlNetworkCapabilities(int Version = 1,
    bool StabilityTelemetry = false, bool LiveVideoBitrate = false,
    bool CaptureFramePacing = false, bool LiveGeometry = false,
    int[]? FilesWireVersions = null);
~~~

ObservedAtTicks y ValidUntilTicks son locales al proceso que los produce. Al transmitir, se envían secuencia, ventana y edad local; el receptor marca su tiempo de recepción. No compara ticks de dos dispositivos. Los DTO serializados agregan las capacidades y métricas como campos opcionales al final de los records existentes.

Los límites usan bytes/segundo enteros y valores positivos hasta 1 250 000 000 B/s. Null significa capacidad desconocida o límite no configurado, nunca capacidad infinita medida. Las tasas observadas de tráfico no se presentan como capacidad disponible. Sin techo válido el controlador queda en Observe.

La clave de policy/grant/pacer incluye LinkKey, Domain, Direction y Generation; clases distintas comparten el techo de esa clave. NLNetworkDecision entrega asignaciones explícitas por clase. Reducir Bulk conserva los grants Interactive/Audio existentes y deja capacidad sin utilizar si hace falta; esa reducción no se transforma automáticamente en permiso BestEffort para el mismo flujo. Flujos Internet desconocidos usan BestEffort con DRR; su tráfico no se declara juego ni descarga por el solo protocolo.

## Medición y comportamiento adaptativo

Las ventanas locales son de 1 s. El RTT usa reloj monotónico del emisor y secuencia; fuentes: heartbeat USB existente, eco LAN negociado, HTTP del túnel y HTTP Internet. No se etiqueta RTT/2 como latencia unilateral. No se inventa porcentaje de pérdida de paquetes a partir de descartes internos o probes fallidos.

La calibración sin carga confirmada conserva NLNetworkBaseline por ruta/fuente; usa p95 de los 30 s idle y se invalida al cambiar generación/ruta. Cada fuente obtiene su propio p95 reciente, calculado con muestras crudas de los últimos 10 s y al menos 10 probes válidos; no se mezclan HTTP Internet, heartbeat y waits de pipeline. Sin baseline/fuente suficiente la parte RTT permanece en Observe, aunque Manual siga limitando tráfico. Para decidir por edad de cola propia no hace falta atribuir esa edad a un RTT externo.

El benchmark toma al menos 30 s de muestras sin carga y 60 s durante cada carga. Publica p50/p95/p99, variación entre RTT consecutivos, errores de sonda y goodput. Las sondas no ejecutan descargas de capacidad continuamente ni se lanzan al comenzar un juego. El monitor existente de VEObservationWindow permanece de solo lectura; una máquina de estado independiente controla la adaptación mientras la sesión esté activa.

Parámetros iniciales del controlador, sujetos a la matriz de aceptación: evaluar cada 1 s; considerar congestión después de 2 ventanas válidas con RTT p95 sobre baseline+20 ms o edad de cola propia p95>40 ms y presión de escritura corroborante. Reducir la asignación bulk 20 %, como máximo una vez cada 2 s. Si persiste 3 s con bulk reducido y la cola de video envejece, solicitar bitrate -15 %. Recuperar +5 % después de 10 s sanos, como máximo una vez cada 5 s. Respetar pisos/techos de usuario y codec. La resolución no cambia más de una vez cada 10 s ni se recupera antes de 30 s sanos.

Una muestra con más de 3 s de edad, generación ajena, NaN o duración inválida no decide cambios. Si faltan muestras, se congela la última asignación válida y se muestra Observe/telemetría caducada; nunca se libera silenciosamente el límite manual. Al cambiar ruta se elimina el baseline y se cancela cualquier orden pendiente de la generación anterior.

Congestión Domain=Internet reduce presupuesto WAN de LE. Solo muestras Domain=DeviceLink de la misma ruta, con presión local corroborante, autorizan reducir video/FILES locales. Un RTT Internet elevado por sí solo no cambia calidad de VE; VideoRateFactor permanece 1 para una decisión exclusivamente WAN.

Hay que diferenciar red, decode, presentación y almacenamiento. Los errores de decode se calculan por ventana; un error histórico no mantiene el modo congestionado para siempre. Si empeora Internet con LE enviando poco y el enlace local permanece sano, se informa posible congestión externa, sin atribuir causalidad confirmada al router.

## LinkEngine

Mantener cuatro bytes de ID seguidos de IPv4, las autorizaciones actuales y TLS/pinning LAN. Conservar LE_METRIC sin alterar su formato. Añadir LE_QOS como JSON versionado, máximo 4096 bytes y una muestra por segundo. La configuración del proceso usa stdin del hijo, revisión/generación y ACK; no abre un puerto LAN nuevo. Un binario sin readiness/capacidad mantiene modo legado visible.

LE_CONFIG lleva Domain en todos los grants y cuatro techos inequívocos: wanUploadBps, wanDownloadBps, deviceFromAndroidBps y deviceToAndroidBps, además de LinkKey, revisión/generación y el array grants. El ACK confirma esos campos y asignaciones. El mismo paquete puede consumir un grant en cada dominio que atraviesa, pero nunca dos veces el mismo grant. El SpeedTest del túnel consume DeviceLink y no Internet.

Regular WAN en los proxies TCP/UDP, contando presupuesto agregado compartido. La bajada se limita antes de leer otro bloque del socket upstream; esto aplica backpressure TCP, pero no elimina una cola formada aguas arriba. UDP requiere descarte explícito y métricas cuando una cola acotada se llena.

El reparto por flujo se aplica en fronteras IPv4 reales, antes de serializar al DATA compartido o al despachar las copias WAN. Mantener orden dentro del flujo, límites por flujo y total, y un algoritmo DRR con quantum de 1500 bytes. CoDel/AQM solo se añade sobre colas de paquetes propias, después de verificar comportamiento y compatibilidad; no sobre bytes TLS arbitrarios. La referencia de [FQ-CoDel](https://www.rfc-editor.org/rfc/rfc8290.html) guía reparto y control de espera, pero el primer scheduler de NOVORA no se denomina FQ-CoDel si no implementa el algoritmo completo.

## VisionEngine

La primera palanca es limitar FILES y adaptar el bitrate sin desmontar captura. Incorporar video-bitrate mediante MediaCodec.SetParameters en un ejecutor serializado; distinguir solicitado, aceptado por API y caudal observado. Algunos dispositivos pueden ignorar el cambio. [MediaCodec](https://developer.android.com/reference/android/media/MediaCodec#setParameters(android.os.Bundle)).

Después aplicar pacing al compositor antes de producir video comprimido, conservando consumo de textura y PTS. No eliminar P-frames arbitrarios. Coalescer solicitudes de keyframe: una pendiente, separación mínima inicial 1 s. Medir retraso hasta keyframe y no generar tormentas de cuadros grandes durante congestión.

La primera entrega no cambia resolución automáticamente. La segunda reutiliza la pantalla virtual en LAN y USB con rollback del perfil; evita recrear MediaProjection mientras la sesión siga viva. Geometría, configuración del codec y keyframe deben llegar en ese orden. Android 14 requiere respetar el consentimiento por sesión y usar resize/setSurface en cambios de la existente. [MediaProjection](https://developer.android.com/about/versions/14/behavior-changes-14#user-consent-media-projection).

Mantener los cuatro canales TLS separados. El canal FILES sirve ambas direcciones mediante una negociación de propietario y una transferencia activa total, sin dos lectores simultáneos del stream. Android→PC migra desde control file.* únicamente con FILES v2 acordado; v1 conserva su camino actual.

## FILES v2 propuesto

El CONTROL global conserva Version=1. Los peers intercambian FilesWireVersions; elegir 2 solo si ambos lo anuncian y la oferta selecciona 2. Un peer antiguo recibe exactamente v1, sin tantear bytes ni convertir un stream JSON en binario a mitad de una transferencia. Si fracasa la negociación se utiliza v1 antes de abrir el canal; si falla un v2 ya abierto, se cierra y requiere un canal nuevo.

El canal autentica primero el hello JSON existente; posteriormente usa frames binarios con header fijo de 36 bytes: magic NLF2 (4), kind (1), flags reservados 0 (1), headerBytes=36 (2), payloadLength (4), transferId UUID en orden RFC 4122 (16), offset (8). Los enteros son big-endian. Kind 1=Begin, 2=Data, 3=Ack, 4=Commit, 5=Cancel, 6=Error, 7=Complete, 8=Cancelled. Metadata Begin es UTF-8 JSON hasta 8192 bytes; payload Data hasta 65 536 bytes; Ack lleva crédito receptor de 4 bytes y offset acumulativo en el header; Commit lleva SHA-256 binario de 32 bytes. Complete solo lo emite el receptor después de publicar el archivo; lleva longitud final en offset y SHA-256 comprobado de 32 bytes. Cancel/Cancelled tienen payload vacío; Cancelled confirma cierre de transferencia y limpieza del parcial. Error tiene UTF-8 hasta 1024 bytes. Estos límites pertenecen al subprotocolo y no amplían MaxFrame de CONTROL.

Comenzar con bloques de 64 KiB y ventana de 256 KiB, máximo negociado 1 MiB. La ventana mide bytes enviados sin confirmación. El receptor confirma offset contiguo escrito al parcial y crédito; el commit final confirma longitud, SHA-256, flush/cierre y publicación del resultado. ACK por bloque no implica durabilidad física. Conservar máximo 2 GiB y limpieza de parciales. Providers SAF sin rename fiable deben rechazar el commit y conservar diagnóstico de limpieza, sin devolver éxito sobre un parcial.

El sender solo declara éxito tras Complete con transferencia, longitud y hash esperados; el último Ack no equivale a Complete. No envía Commit hasta que todos los Data estén confirmados. El sender es el único escritor de Begin/Data/Commit/Cancel; el receiver es el único escritor de Ack/Complete/Cancelled/Error. El arbitraje concede un propietario antes de abrir la transferencia. Cada extremo tiene un único lector que distribuye los frames por estado; un error de encuadre termina el canal, no inicia otra transferencia sobre él.

Budget pacing ocurre antes de iniciar el plazo de respuesta. En v1 el timeout de 30 s no incluye espera voluntaria de presupuesto. V2 tiene deadline de 30 s para respuesta a Begin, para Complete después de Commit y para Cancelled después de Cancel, incluido archivo vacío. Data tiene reloj de progreso de 30 s mientras hay bytes en vuelo. Crédito 0/pausa declarada emite Ack vivo cada 2 s, watchdog de 6 s sin ese estado y duración máxima inicial de pausa de 120 s; al superarla se cancela con motivo visible. Un Ack duplicado no suma crédito: crédito representa máximo de bytes adicionales pendientes, siempre acotado al límite negociado. Control/cancelación usa una reserva independiente del crédito Data; una pausa no bloquea Cancel. Desconexión, timeout o encuadre incierto cierra FILES sin tumbar video/control salvo pérdida de la sesión completa.

Un Ack válido con crédito 0 declara pausa de Data. Mientras la pausa permanece viva, se suspende su reloj de progreso de 30 s incluso con bytes ya recibidos pendientes de SAF; al reanudar se reinicia el reloj de progreso. El watchdog de 6 s y el máximo de pausa de 120 s siempre corren. La pausa Data no suspende deadlines de Begin/Complete/Cancelled. Se prueba pausa SAF de 60 s con datos pendientes y Ack vivo cada 2 s, sin timeout falso.

El receptor v2 separa el lector de frames, el escritor SAF serializado y un único escritor de respuestas. Data se admite en una cola limitada por el crédito anunciado, nunca más de 1 MiB; no se bloquea el lector esperando escritura SAF. Ack de pausa/estado y Cancel pasan por la ruta de control aunque el almacenamiento esté bloqueado. No se devuelve crédito de memoria antes de terminar el write; cualquier Data sobre la ventana se rechaza. Cierre/generación impide que una operación SAF tardía publique un commit para una sesión ya terminada.

Un coordinador FIFO determina el único propietario de transferencia del canal; si PC y Android piden Begin a la vez, PC arbitra solicitudes identificadas y concede una sola. La asignación puede expirar antes de Begin; una transferencia activa no se preempta para iniciar otra. Solo Complete, Error terminal, Cancelled confirmado o cierre del canal liberan la propiedad. Enviar Commit/Cancel no la libera. El cierre por timeout libera solicitudes de esa generación con error y exige nuevo canal. Reanudación después de desconexión se deja fuera de v2 inicial.

## Opciones condicionadas a medición

Audio PCM estéreo 48 kHz/16 bits consume 1,536 Mbps antes de headers. Evaluar un codec de audio negociado solo si ese consumo es material respecto al cuello observado; medir CPU, demora y compatibilidad con grabación. Pools y separación de lectura/decode se consideran únicamente si allocation/GC o decode bloquean el caudal, con ownership hasta que grabador y observers suelten el paquete.

Un prototipo LAN de DATA multiplexado debe comparar transportes manteniendo autenticación y control de congestión. [QUIC](https://www.rfc-editor.org/rfc/rfc9000.html#section-13) reduce bloqueo entre streams, pero sigue compartiendo capacidad y congestión; ADB reverse USB actual no transporta UDP. No se sustituye TCP por UDP crudo ni se habilita el prototipo por defecto.

## Restricciones globales

- Protocolo CONTROL Version=1 y DATA v1 intactos en la primera entrega; FILES v2 se negocia por separado.
- Windows net8.0-windows10.0.26100.0; Android net10.0-android, mínimo API 26; Rust edition 2021 con Cargo.lock bloqueado.
- Modo inicial Observe; Manual y Adaptive requieren activación visible y techos válidos. Prioridad elegida: latencia estable.
- Un controlador autoritativo por sesión; órdenes idempotentes con secuencia/generación; capacidades ausentes equivalen a false/v1.
- No reducir seguridad TLS, pinning, permisos MediaProjection/SAF ni límites de archivos para ganar velocidad.
- No modificar el monitor VE de 20 minutos para que controle bitrate; tiene un contrato existente de solo lectura.
- No registrar tokens, contenidos, nombres privados de archivos o payloads en la telemetría predeterminada.
- No agrandar buffers globalmente, descartar bloques FILES ni prometer eliminar congestión de dispositivos fuera de NOVORA.
- En una futura entrega, verificar versión vigente, aplicar n+1 y sincronizar APK, descriptor, relay/Windows, instalador y hashes; no fijar hoy un número de versión futuro.

## Aceptación y orden de entrega

Las pruebas deterministas cubren reloj, límites, secuencia, fairness, memoria, compatibilidad, fallo y cierre. La aceptación física usa baseline y candidato en la misma ruta/configuración y 3 repeticiones por escenario. Objetivo inicial en un cuello controlado: reducir al menos 30 % el exceso de RTT p95 respecto al idle cuando la baseline presente congestión; con modo desactivado, goodput no debe caer más de 5 %. Son criterios de aceptación propuestos, no resultados medidos ni garantías en el Technicolor/C20.

FILES v2 debe superar v1 bajo RTT artificial 20/50 ms, manteniendo SHA-256 y cota de memoria, con el mismo presupuesto de tráfico. Encoder debe sostener sesión/audio/control al cambiar bitrate; un codec incompatible conserva perfil y declara la limitación. Una entrega adaptativa espera las verificaciones físicas USB/LAN; un build solo confirma código.

Orden: [plan general](../plans/2026-10-13-le-ve-estabilidad-y-capacidad.md), [plan LE](../plans/2026-10-13-le-estabilidad-red.md) y [plan VE](../plans/2026-10-13-ve-capacidad-transferencia.md).

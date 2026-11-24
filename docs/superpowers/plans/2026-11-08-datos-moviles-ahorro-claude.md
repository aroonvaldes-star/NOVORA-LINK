# Datos móviles y ahorro de LINK Control — plan para Claude

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrar una ruta remota por Internet con la conexión dinámica de NOVORA-LINUX y limitar el consumo móvil desde LINK Control, con mediciones verificables.

**Architecture:** Añadir una ruta remota autenticada al coordinador dinámico existente y una política de consumo Android independiente de la ruta física. Reutilizar H.264 y los canales autorizados; negociar extensiones de manera aditiva. La ruta WAN no se obtiene simplemente admitiendo una IP pública en el protocolo LAN.

**Tech Stack:** C#/.NET, Android MediaProjection/MediaCodec, TCP/TLS existente, coordinador dinámico Linux. Primera prueba WAN mediante VPN de superposición ya disponible y explícitamente configurada; la aplicación no instala ni crea esa infraestructura automáticamente.

**Spec:** Requisitos y diseño de este documento; integrar con `../specs/2026-11-08-conectividad-dinamica-design.md` y el plan `2026-11-08-conectividad-dinamica.md`, leyéndolos de nuevo antes de implementar porque Claude los está desarrollando.

## Estado y autoridad

2026-11-08. El usuario solicita este plan y su entrega a Claude para implementar en la conexión dinámica. Codex solo escribe documentación en este turno; no modifica Linux ni Android. Los valores siguientes son propuestas técnicas para el primer ensayo, no preferencias previamente confirmadas ni resultados medidos. Claude conserva la propiedad de su integración Linux; coordinar superficies compartidas antes de editar. No hacer commits, push, instalaciones ni publicaciones por efecto de este plan.

## Global Constraints

- Alcance de prueba: LINK Control y NOVORA-LINUX; preservar clientes Windows y USB/LAN actuales.
- Conservar identidad de dispositivo, revocación y permisos del diseño dinámico. Una IP, nombre o identificador de VPN no autoriza por sí solo.
- No prometer conexión sin Internet: una VPN necesita una red subyacente que transporte los paquetes; agotar saldo puede cortar también una sesión existente.
- No degradar toda sesión LAN/USB para ahorrar datos; activar la política por ruta medida o elección explícita, tratando transporte desconocido como medido hasta identificarlo.
- No usar speedtests automáticos ni tráfico de prueba voluminoso sobre móvil.
- N+1 al preparar la siguiente APK de entrega: consultar el máximo vigente real, verificar firma original, APK, descriptor y SHA-256. Fuente observada: 1.4.124/código 124; no reservar 125 ni confiar en la ficha histórica 120.
- No cambiar codec a HEVC/AV1 en la primera entrega: exige negociación y receptor compatibles que este plan no presupone.
- No exponer ADB por Internet ni asumir soporte remoto de UHID/ExIn.
- No enrutar todo Internet del teléfono por la PC como efecto de activar control/video remoto; evitar ciclos con la VPN de LinkEngine.

## Diseño y alternativas

1. **Primera prueba recomendada: VPN de superposición existente + TLS de NOVORA.** Aporta conectividad privada entre equipos distantes; verificar rutas efectivas, incluidos los canales de video. Requiere que el entorno facilite una VPN funcional y que el endpoint Linux sea alcanzable. No equivale a tener esa VPN implementada hoy.
2. **Relay público propio:** ambos extremos abren conexiones salientes; resuelve escenarios sin ruta directa pero añade servidor, operación, coste y protocolo de multiplexación. Queda para otro incremento si no existe VPN de prueba.
3. **IP pública y reenvío de puertos:** depende de alcance entrante y configuración del router; no es el camino predeterminado para móvil/CGNAT.

No hay servidor ni VPN de ensayo identificados en esta sesión. Implementar y probar las piezas portables, pero no declarar WAN funcional hasta disponer del medio real. Si no hay infraestructura, reportar el bloqueo exacto; no simular una entrega completa con loopback.

### Perfiles propuestos

| Perfil | Bitrate objetivo | FPS máximo | Lado mayor máximo | Audio | Video nominal MB/h |
|---|---:|---:|---:|---|---:|
| Extremo (predeterminado en móvil) | 120000 bit/s | 5 | 480 px | Apagado | 54 |
| Ahorro | 250000 bit/s | 10 | 720 px | Apagado | 112,5 |
| Solo control | Sin captura ni audio | — | — | Apagado | 0 de video |

Preservar aspecto y alineación exigida por el codec. El mínimo propuesto de video es 100000 bit/s y 1 fps en modo móvil negociado; los límites heredados siguen vigentes para clientes sin esa capacidad. Un perfil necesita validación visual: no prometer lectura de texto pequeño a 480 px. MB decimales = bit/s × 3600 / 8 / 1000000; cifras nominales excluyen audio, TLS, VPN, retransmisión y otros canales.

El ahorro principal será bitrate variable cuando el codec lo soporte, menos cuadros de entrada y ausencia de captura en Solo control. Pantalla estática no equivale automáticamente a cero bytes. Evaluar supresión de cuadros idénticos antes de codificar como segunda fase, solo si una medición demuestra beneficio y coste de GPU/CPU aceptable. Nunca descartar paquetes H.264 arbitrariamente para ahorrar: puede romper dependencias y congelar el receptor.

### Contrato aditivo propuesto

Negociar `mobileDataV1` en el intercambio de capacidades autenticado; no enviar acciones desconocidas a receptores antiguos. La política debe estar reconocida antes de anunciar el perfil como aplicado.

Tipos nuevos portables (nombres propuestos, adaptar una sola vez al modelo de Claude y actualizar productores y consumidores juntos):

```csharp
enum MobileProfile { Extreme, Economy, ControlOnly }
record MobilePolicy(long Generation, long Revision, MobileProfile Profile,
    long? SessionLimitBytes);
record MobileUsage(long Generation, long SentBytes, long ReceivedBytes,
    long VideoSentBytes, long AudioSentBytes, long FileBytes);
```

`SessionLimitBytes = null` significa sin tope configurado; un entero positivo configura el presupuesto bidireccional. Persistir el perfil y la preferencia del tope; no fijar silenciosamente un cupo mensual ni reiniciar el presupuesto al reconectar. Límite alcanzado: detener video/audio, bloquear nuevas transferencias y conservar control mínimo, avisando que ese control aún consume datos. Por tanto es un límite de medios, no garantía de factura cero adicional. Mostrar consumo acumulado real de la sesión lógica, con subcontadores por canal y advertencia de sobrecarga no incluida.

## Review Focus

- VPN presente con transporte subyacente móvil/desconocido: no confundirla con una red gratuita.
- Receptor antiguo o encoder que rechaza VBR/bitrate bajo: informar capacidad real y mantener fallback compatible.
- Reconexión con respuesta tardía: ninguna generación antigua puede restaurar video o borrar el presupuesto.
- Límite alcanzado con paquetes en vuelo: medir exceso acotado por colas; no anunciar un tope estricto de facturación.
- Cambio de tamaño/rotación e imagen estática: conservar aspecto, coordenadas de control y recuperación por keyframe.

## Mapa de archivos

Existentes a revisar/modificar por Claude en su integración:

- Android: `src/LINK Control/AndroidService/NLAndroidServiceControl.cs`, `NLAndroidServiceControlNetwork.cs`, `NLAndroidServiceVeLanCapture.cs`.
- Captura Android: `src/LINK Control/AndroidVideo/NLAndroidVideoEncoder.cs`, `NLAndroidVideoEncoderConfiguration.cs`, `NLAndroidVideoCompositor.cs`, `NLAndroidVideoService.cs`, `NLAndroidVideoLanClient.cs`.
- UI: `src/LINK Control/AndroidUI/NLAndroidUIVisionSettingsPage.cs` y `NLAndroidUIMainShell.cs`.
- Compartidos: `src/NOVORA LINK/Control/NLControlCapturePolicy.cs`, `NLControlProtocol.cs`, `NLControlVeLanProtocol.cs`, `NLControlClient.cs`, `NLControlSession.cs`, `NLControlTrustedPc.cs`. Android los enlaza desde su csproj; comprobar consumidores Windows antes de alterar contratos.
- Linux, solo Claude: `src/NOVORA-LINUX/Control/LinuxControlHost.cs`, `LinuxControlServer.cs`, `ControlProtocol.cs` y adaptadores de VisionEngine/rutas creados durante la conexión dinámica.
- Crear política portable en `src/LINK Control/AndroidVideo/NLAndroidMobileDataPolicy.cs` y contabilidad en `src/LINK Control/AndroidService/NLAndroidMobileUsage.cs`; extraer contratos compartidos a archivo portable si ambos extremos los necesitan.
- Pruebas: ampliar `tests/NOVORA.AndroidVideoEncoder.Tests`, `tests/NOVORA.CapturePolicy.Tests`, `tests/NOVORA.NetworkSession.Tests`, `tests/NOVORA.DynamicAddress.Tests`; añadir `src/NOVORA-LINUX/tests/MobileConnectionChecks.cs` al harness actual.

## Task 1: Integración de ruta remota sin debilitar LAN

**Interfaces:** consume identidad y coordinador dinámico de Claude; produce ruta `Remote` autorizada y capacidad `mobileDataV1`. El endpoint remoto incluye host/IP, puerto y huella, nunca una autorización implícita.

- [ ] Releer estado/diff actuales. Confirmar quién edita los archivos compartidos y resolver nombres del contrato con el coordinador que ya exista; no crear otro coordinador.
- [ ] Añadir pruebas que fallen: LAN sigue rechazando direcciones públicas, la ruta remota explícita admite endpoint VPN configurado, huella equivocada y dispositivo revocado se rechazan, cliente antiguo conserva contrato anterior.
- [ ] Implementar validación propia para la ruta remota; no relajar globalmente `NLControlLanInvitation.ValidateAddress`. Admitir la familia de direcciones real de la VPN de prueba, incluida IPv6 si la requiere; si no está implementada, mostrar rechazo explícito.
- [ ] Configurar manualmente el endpoint remoto en Android para la primera prueba. No usar broadcast para descubrir una PC distante ni aceptar enlaces no autenticados. Confirmar acceso al listener Linux y a TODOS los endpoints anunciados por video/audio/control/files.
- [ ] Verificar TLS extremo a extremo, propiedad de generación y prohibición de ejecutar comandos de una ruta anterior. Una VPN externa no sustituye la confianza NOVORA.
- [ ] Ejecutar suites DynamicAddress, NetworkSession y harness Linux; registrar resultados y cobertura WAN pendiente si falta infraestructura.

## Task 2: Política móvil y negociación de captura

**Interfaces:** consume `MobilePolicy`; produce límites efectivos y ack con generación/revisión, perfil solicitado, perfil efectivo y motivo si no puede aplicarse.

- [ ] Pruebas rojas: Extremo = 120000/5/480, Ahorro = 250000/10/720; límite positivo o null; rechazar valores inválidos; política vieja/repetida no cambia estado; cliente antiguo conserva mínimo de 250000 bit/s y 10 fps de la política heredada.
- [ ] Implementar política portable y negociación. Revisar los mínimos duplicados: `NLAndroidVideoEncoder.TrySetTargetBitrate`, `NLAndroidVideoService` y `NLControlCapturePolicySession` rechazan hoy los valores bajos. Cambiar solo el camino con capacidad móvil negociada.
- [ ] Resolver el techo inicial: el encoder y el pacer actuales limitan aumentos al máximo de la oferta inicial. Pasar de Extremo a Ahorro requiere nueva oferta/captura autorizada, o preparar un techo negociado separado del objetivo; nunca aparentar que subió cuando el setter lo rechazó.
- [ ] Respetar LiveGeometry=false: cambio de resolución requiere reinicio coordinado, nueva generación de captura y permiso válido; no afirmar ajuste geométrico en vivo.
- [ ] Ejecutar CapturePolicy, AndroidVideoEncoder y NetworkSession; comprobar fallback Windows/Android anteriores.

## Task 3: Encoder económico sin romper el decodificador

**Interfaces:** consume límites efectivos; produce H.264 válido y telemetría de bitrate/cuadros efectivos.

- [ ] Pruebas rojas: solicitar VBR solo si soportado; fallback informado; máximo de cuadros respetado; valores bajos se aplican solo en móvil; giro mantiene aspecto/alineación y coordenadas.
- [ ] En `NLAndroidVideoEncoderConfiguration` sustituir la preferencia de CBR por selección VBR exclusiva del perfil móvil cuando esté soportada. Si el hardware rechaza el perfil, informar y ofrecer Solo control o un perfil validado; no aumentar consumo en silencio.
- [ ] Reducir cuadros antes del encoder mediante el compositor/pacer existente y, si procede, `KEY_MAX_FPS_TO_ENCODER`. No confiar solo en `frame-rate` como límite de entrada. Mantener el keyframe de recuperación y la cola existentes en el primer ensayo.
- [ ] Medir imagen estática, scroll y movimiento. Evaluar después ampliar el intervalo de keyframes únicamente si la reducción compensa el tiempo de recuperación; conservar intervalo actual de 2 s hasta tener evidencia.
- [ ] Ejecutar AndroidVideoEncoder y CapturePolicy; prueba física de decodificación Linux, rotación, recuperación y legibilidad. Stubs de MediaCodec no prueban calidad ni consumo real.

## Task 4: Solo control, medición y presupuesto

**Interfaces:** consume `MobilePolicy`; produce `MobileUsage` por sesión lógica y estados `Applied`, `Unsupported`, `LimitReached` negociados.

- [ ] Pruebas rojas: Solo control detiene captura y audio conservando el canal de mando; volver a video exige oferta vigente; presupuesto no se reinicia al reconectar; límite cancela medios/archivos y rechaza nuevas transferencias.
- [ ] Contar bytes bidireccionales en las fronteras de todos los canales de NOVORA, incluidos reintentos y reconexión; no duplicar el agregado sumando contadores de capas anidadas. Contabilidad antes de TLS debe etiquetarse como tráfico NOVORA, no facturación del operador.
- [ ] Mantener acumulado ligado a PC/dispositivo y sesión lógica. Parada explícita finaliza la sesión; un nuevo socket no la reinicia. Usar enteros de 64 bits y snapshot coherente frente a envíos concurrentes.
- [ ] Detener captura/audio al llegar al tope, cancelar transferencias según contrato FILES vigente, purgar colas viejas y mantener control de volumen acotado. Documentar máximo de exceso por buffers y paquetes en vuelo.
- [ ] UI Android: selector Extremo/Ahorro/Solo control, perfil efectivo, MB enviados/recibidos, tope opcional y aviso de límite. No iniciar audio, archivos o pruebas de velocidad automáticamente. UI Linux de Claude refleja confirmación real.
- [ ] Añadir pruebas de concurrencia, cancelación, desconexión durante transferencia y cambio de perfil con límite ya agotado. Ejecutar suites FILES y captura afectadas.

## Task 5: Recuperación de móvil y convivencia con VPN

**Interfaces:** consume eventos de red y estados del coordinador dinámico; produce nueva ruta autorizada sin replay de acciones ni recuperación de medios cancelados.

- [ ] Pruebas rojas: pérdida 30 s/120 s, cambio Wi-Fi→móvil, IP distinta, callback tardío, olvidar PC durante recovery, bloqueo del operador. No marcar conectado por icono VPN o presencia de interfaz.
- [ ] Extender recuperación de ruta remota con política del coordinador: 0/1/2/4/8 s, luego cada 15 s con jitter hasta 20%, intento máximo 8 s; errores de autenticación requieren intervención. Evitar bucles duplicados con el recovery LAN actual de tres intentos.
- [ ] Al recuperar, negociar capacidades y recrear canales/permisos válidos sin repetir mutaciones de resultado desconocido. Mantener Solo control o límite agotado tras cualquier reconexión.
- [ ] Detectar red medida usando ConnectivityManager y capacidades disponibles; una VPN sin información subyacente se trata conservadoramente. No cerrar una ruta sana solo por un callback no relacionado.
- [ ] Revisar `AndroidVpn/NLAndroidVpnService.cs`: si el ensayo usa otra VPN basada en VpnService, no intentar activar simultáneamente la VPN LinkEngine de la misma persona/perfil. Mostrar incompatibilidad y conservar control/video. Verificar que el túnel no se enruta a sí mismo.
- [ ] Ejecutar pruebas NetworkSession y Linux con reloj inyectado, y prueba física de corte/restablecimiento de datos sin presentar conexión residual como tráfico operativo.

## Task 6: Ensayo reproducible, entrega y n+1

- [ ] Ejecutar desde el checkout, individualmente y revisando el resultado completo:

```bash
dotnet run --project tests/NOVORA.AndroidVideoEncoder.Tests -c Release
dotnet run --project tests/NOVORA.CapturePolicy.Tests -c Release
dotnet run --project tests/NOVORA.NetworkSession.Tests -c Release
dotnet run --project tests/NOVORA.DynamicAddress.Tests -c Release
dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release
dotnet build 'src/LINK Control/NLProjectAndroid.csproj' -c Release -p:AndroidBuildApplicationPackage=false
```

Éxito: código 0 y assertions relevantes aprobadas; una limitación de SDK/hardware/red no cuenta como pass. Descubrir los flags adicionales de las pruebas Linux en su `Program.cs` vigente y ejecutar los de conexión/video añadidos.

- [ ] Comparar perfil actual y nuevos perfiles con el mismo teléfono, escena y duración: 10 min estática, 10 min menús/scroll y 10 min movimiento. Desactivar audio para comparar video; medir audio por separado si se habilita. No hacer estas pruebas con saldo limitado sin acordar el presupuesto.
- [ ] Medir reposo Solo control durante 30 min. Objetivo provisional <= 1 MB/h de tráfico NOVORA agregado; medir VPN/operador por separado. No es promesa de consumo total ni criterio logrado sin medición.
- [ ] Registrar bytes reales, MB/min, resolución, fps, bitrate, keyframes, pérdidas, reconexiones, latencia, CPU/batería y legibilidad. Para Extremo, referencia nominal 0,9 MB/min de video; informar exceso real, no excluir keyframes para aparentar éxito.
- [ ] Probar 60 min de sesión mixta remota con móvil real, PC en otra red, LAN/USB ausentes y capturas de contadores antes/después; verificar tope, revocación, caída, recuperación y dispositivo correcto. Registrar proveedor VPN/ruta directa o relay porque cambia el resultado.
- [ ] Repetir regresión USB/LAN y compatibilidad de contratos Windows. No declarar paridad ExIn remota sin su permiso/ruta HID real.
- [ ] Solo al preparar artefacto de entrega: consultar APK y descriptor vigentes, aplicar N+1, firma original, hash y sincronización. No instalar ni publicar sin la solicitud correspondiente.
- [ ] Escribir informe en `docs/informes/2026-11-08-datos-moviles-ahorro.md` con comandos, resultados, perfiles efectivos, mediciones, infraestructura usada y pendientes. No registrar tokens, secretos ni claves.

## Fuentes técnicas

- Android MediaFormat, control de bitrate y límite de entrada de cuadros: https://developer.android.com/reference/android/media/MediaFormat
- Android VpnService, red subyacente y ciclo de vida: https://developer.android.com/reference/android/net/VpnService
- Android VPN, protección del socket de túnel y servicio: https://developer.android.com/develop/connectivity/vpn
- WireGuard keepalive, únicamente como referencia de mantenimiento de NAT, no dependencia impuesta: https://www.wireguard.com/quickstart/

## Criterio de cierre

Código compilado + pruebas de contrato + video realmente decodificado por Linux + conexión por móvil entre redes distintas + consumo comparativo medido + recovery y revocación. Si falta infraestructura o hardware, entregar estado parcial con el bloqueo concreto. No confundir el plan, una VPN activada o una prueba loopback con este cierre.

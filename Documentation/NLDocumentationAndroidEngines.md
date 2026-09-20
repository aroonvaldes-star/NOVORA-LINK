# NOVORA Android — bloque 5: motores e Internet USB

Entrega de código para PC 1.4 y Android com.novora.appcontrol 1.4.4 (versionCode 5). El protocolo de control sigue en versión 1; Engines es un campo opcional. Clientes anteriores conservan sus funciones y Android nuevo deshabilita los botones nuevos cuando PC no anuncia las capacidades.

## Comportamiento

Android muestra el teléfono seleccionado en PC, el estado real de VisionEngine y el estado/mensaje de LinkEngine. VisionEngine permite iniciar, detener y reiniciar usando el motor existente. Para iniciar video se necesita un teléfono disponible para PC y un monitor seleccionado en Configuración. Controlar por LAN no crea por sí solo el enlace ADB de captura.

Internet USB se inicia desde Android, dentro de una sesión de control USB autorizada para ese mismo teléfono. NOVORA explica el cambio de ruta; después Android solicita el permiso VPN. Si se cancela, no se inicia LinkEngine. Si cambia la sesión o su revisión durante el permiso, se pide repetir la acción sobre el estado actualizado.

Después del consentimiento se prepara el servicio VPN Android y se envía la solicitud a PC. PC responde que aceptó el inicio y continúa trabajando; esa respuesta no afirma que exista Internet. Los eventos de LinkEngine actualizan el progreso y el resultado. El servicio VPN anuncia túnel activo únicamente después de establecer el descriptor TUN y el canal DATA.

Detener LinkEngine cierra la VPN local y solicita detener el motor que inició esa sesión. La notificación VPN incluye Detener VPN. Desconectar control, perder USB, cambiar el dispositivo o cerrar PC cancela el inicio y limpia el motor de esa sesión. La detención por fallo que encuentre el control ocupado se difiere al próximo evento disponible de la misma sesión, con hasta tres intentos. Un reinicio demasiado rápido mientras cierra la instancia anterior se rechaza con un mensaje para volver a intentar.

## Transporte y alcance

- Control general NOVORA: USB 27214 o LAN TLS de bloques anteriores.
- Inicio de Internet: solo sesión USB autorizada. Pasar de USB a LAN cierra el túnel USB de esta entrega.
- LinkEngine CONTROL: Android 127.0.0.1:27183 mediante ADB reverse; longitud int32 big-endian, HELLO/ACK y HEARTBEAT/ACK. Latidos cada dos segundos; no representan tráfico de usuario.
- LinkEngine DATA: Android 127.0.0.1:27184 mediante ADB reverse; identificador inicial de cuatro bytes seguido de paquetes IPv4 sin un encabezado adicional.
- TUN IPv4 10.0.0.2/32, MTU 1500, ruta IPv4 por defecto y DNS 8.8.8.8. La salida de DNS va a través de PC. El relay existente atiende TCP/UDP; esta entrega no agrega soporte ICMP ni IPv6.
- NOVORA se excluye del túnel para mantener sus canales de control y protege los sockets USB. Las demás apps del perfil Android usan el túnel mientras esté activo. No se activa bypass IPv6: la familia no configurada queda bloqueada por Android.
- No es una VPN comercial ni un túnel cifrado extremo a extremo hacia Internet. CONTROL/DATA de LinkEngine dependen del enlace local USB/ADB y no se exponen a LAN. La conexión de PC y el cifrado propio de cada aplicación determinan la salida a Internet.
- Sin inicio automático al arrancar Android, sin always-on y sin recuperación automática de la VPN. Si falla, se libera la ruta y se requiere iniciar otra vez. El teléfono puede volver a su red normal después de cerrar el túnel; no se implementa un bloqueo permanente de Internet.
- Lectura TUN por disponibilidad del descriptor y tubería de cancelación; no se consulta TUN periódicamente. Los intentos de conectar puertos están limitados a 25 por canal, con espera de un segundo y límite de dos segundos por intento.

Referencias: [guía VPN Android](https://developer.android.com/develop/connectivity/vpn) y [contrato de VpnService.Builder](https://developer.android.com/reference/android/net/VpnService.Builder). El protocolo de datos se contrastó con LETransportHandshake, LEProtocolHeartbeat y RelayCore del checkout actual.

## Verificación

Pruebas de contrato/capacidades, compatibilidad con PC anterior, revisión obsoleta, enmarcado CONTROL real de PC, heartbeat válido/incorrecto, identificador DATA, paquetes fragmentados/coalescidos y entradas inválidas. Ver Resultado-verificacion.md de la entrega para conteos finales.

Android se comprueba con el objetivo Compile; aún faltan empaquetado completo, manifiesto Android final e instalación firmada. No se produce un APK en cada compilación o prueba.

Prueba física pendiente: aceptar/rechazar permiso VPN; iniciar desde USB; abrir una página y verificar DNS y tráfico a través de PC; probar pérdida de USB, detener desde ambas notificaciones, cambiar teléfono, cerrar PC, revocar permiso desde Android y detener durante inicio. Verificar que el dispositivo libere el túnel y que PC cierre sus recursos; comprobar varias versiones/fabricantes antes de afirmar compatibilidad amplia.

No se afirma acceso real a Internet, latencia, estabilidad de juegos ni rendimiento medido a partir de la compilación o de los latidos. No se restauró la app retirada; se agregó el cliente VPN dentro de la app única nueva.


Distribución y primer inicio: [bloque 6 — instalación y actualización desde HOME](NLDocumentationAndroidInstall.md). La instalación es independiente de la vinculación y del consentimiento VPN; el APK firmado sigue pendiente de distribución.


## Actualización de interfaz, archivos y grabación (15 septiembre 2026)

Consultar [Android universal](NLDocumentationAndroidUniversal.md) para el comportamiento actual, rutas, comandos, APK 1.4.7 y límites comprobados. Las verificaciones históricas de esta sección conservan su alcance original.

## Corrección de arranque 1.4.8 — 15 septiembre 2026

Se reprodujo en Samsung SM-A566E con Android 16 el cierre de 1.4.7: UnsatisfiedLinkError en MainActivity.n_onCreate. Se deshabilita AndroidEnableMarshalMethods para usar registro JNI dinámico. Después apareció InvalidCastException al obtener IWindowManager; se corrige con JavaCast<IWindowManager>().

APK 1.4.8, versionCode 9, instalado mediante actualización conservando datos. Arranque en frío y pantalla de conexión comprobados por ADB y árbol de interfaz; proceso activo tras el arranque. Esto sustituye la ausencia de prueba física del arranque reportada antes; las demás pruebas completas PC↔Android siguen pendientes.

Windows, Android y los tres proyectos auxiliares de VE compilan. 230 pruebas .NET aprobadas. RelayCore Release compila con 21 advertencias existentes y aviso de dependencia net2. Los proyectos WPF se verificaron secuencialmente tras una colisión de archivos generados al intentar compilarlos en paralelo.

Referencia del problema de enlace en .NET: https://github.com/dotnet/maui/issues/35209

# Android universal y archivos NOVORA — 15 septiembre 2026

## Comportamiento implementado

La aplicación conserva Conectar, Control y Configuraciones. Los ajustes usan las capacidades anunciadas por PC, requieren confirmación y descartan operaciones de una sesión anterior. Los controles se adaptan al espacio disponible y respetan las barras del sistema.

La burbuja flotante se habilita voluntariamente con el permiso de Android. Aparece con VisionEngine confirmado, permite moverla, abrir herramientas, capturar, grabar, ajustar y detener VE. Los accesos a aplicaciones son opcionales y configurables; sin favoritos no aparece esa sección. Ocultarla dura la sesión. El inicio automático se realiza tras confirmación de PC.

## Archivos por tipo

PC usa el Escritorio real de Windows/NOVORA-Files. Android usa almacenamiento compartido/NOVORA. Ambos usan Imagenes, Videos, Audio, Documentos, Comprimidos, Instaladores y Otros. La app Android solicita acceso a esa carpeta para explorarla. No controla las carpetas privadas ni las preferencias de guardado de otras aplicaciones.

Compartir desde otras apps y el selector interno Android usan la sesión autenticada existente. Cada archivo se transmite por bloques, se comprueba longitud y SHA-256 y se confirma después de guardarse. Máximo 100 archivos por selección, 2 GiB por archivo; se necesita longitud conocida. Desconexión o cancelación limpia el temporal local. Los nombres repetidos reciben sufijo, sin sobrescribir originales.

Arrastrar/soltar y portapapeles de archivos reutilizan la clasificación por tipo. Los APK se guardan como instaladores; no se instalan automáticamente. El portapapeles de texto no crea archivos por sí solo. La ruta ADB conserva su mecanismo de transporte y no añade una comprobación SHA-256 remota. Un cable desconectado durante la limpieza puede dejar un temporal .novora-* identificable.

## Captura y grabación

La captura guarda una imagen PNG del fotograma real en PC/Imagenes. La grabación usa H.264 recibido por VE y audio PCM decodificado del teléfono, sin micrófono, en PC/Videos como MKV. No necesita instalar FFmpeg adicional. La capacidad solo se anuncia con video H.264 y audio del teléfono activos. H.265/AV1 no están habilitados para esta grabación.

Iniciar es una solicitud: se informa Preparando hasta recibir un fotograma clave; se cancela si no llega en ocho segundos. Solo se anuncia audio incluido cuando se han escrito muestras. Las colas tienen límites de memoria; un disco lento detiene y explica el error. Desconectar o detener VE finaliza el archivo. El temporal no se presenta como grabación final hasta cerrar el contenedor.

La opacidad ajusta la burbuja en pantalla. La exclusión de la burbuja de capturas es experimental mediante las protecciones de Android: no se garantiza recuperar la imagen que había debajo. El panel solicita exclusión. No hay marca de agua independiente añadida al video. La conversión PNG SDR usa BT.601/BT.709 según tamaño; HDR y rango completo requieren comprobación visual adicional.

## Integración y protocolo

La versión de protocolo sigue siendo 1 con campos opcionales VideoSettings, Media y FileSharing. Los comandos file.begin, file.chunk, file.end y file.cancel requieren vinculación. Los bloques se codifican en hexadecimal para respetar el tamaño de trama incluso con JSON anidado. Ajustes y medios validan revisión de estado; la transferencia tiene su propia identidad y offsets.

La sesión de control sigue siendo única, mantenida por el servicio Android. Los cambios se notifican por eventos; se conservan los plazos de operaciones y la supervisión existente de conexión, sin añadir sondeos periódicos para la interfaz.

## Verificación y límites

Compilación Release Windows y APK Android: cero errores y cero advertencias. Batería .NET: 230/230 pruebas aprobadas. Incluye transferencia autenticada por socket, rechazo sin vinculación, hash/tamaño/nombres/cancelación, respuesta seguida de cierre de conexión, contenedor H.264/PCM leído con FFmpeg nativo y captura PNG con comprobación de píxeles.

No hubo dispositivo ADB conectado ni emulador disponible. Permanecen pendientes las pruebas reales PC↔teléfono, permisos y galería, Android Compartir, flotante sobre otras apps, exclusión visual y audio/video prolongados. La compilación y pruebas locales no certifican compatibilidad universal. El APK 1.4.7 (8), Android 8+ (API 26), conserva la firma de desarrollo existente.

## Corrección de arranque 1.4.8 — 15 septiembre 2026

Se reprodujo en Samsung SM-A566E con Android 16 el cierre de 1.4.7: UnsatisfiedLinkError en MainActivity.n_onCreate. Se deshabilita AndroidEnableMarshalMethods para usar registro JNI dinámico. Después apareció InvalidCastException al obtener IWindowManager; se corrige con JavaCast<IWindowManager>().

APK 1.4.8, versionCode 9, instalado mediante actualización conservando datos. Arranque en frío y pantalla de conexión comprobados por ADB y árbol de interfaz; proceso activo tras el arranque. Esto sustituye la ausencia de prueba física del arranque reportada antes; las demás pruebas completas PC↔Android siguen pendientes.

Windows, Android y los tres proyectos auxiliares de VE compilan. 230 pruebas .NET aprobadas. RelayCore Release compila con 21 advertencias existentes y aviso de dependencia net2. Los proyectos WPF se verificaron secuencialmente tras una colisión de archivos generados al intentar compilarlos en paralelo.

Referencia del problema de enlace en .NET: https://github.com/dotnet/maui/issues/35209

## Icono Android 1.4.15

- android:icon y android:roundIcon apuntan al recurso novora_logo existente. El PNG original conserva transparencia alfa; no se agrega fondo al recurso.
- La version visible es 1.4.15 y el codigo de version es 15. La forma o fondo que imponga el launcher depende de Android y de su configuracion.
- Pendiente comprobar la apariencia en el cajon de aplicaciones del telefono tras instalar la actualizacion.

## USB automatico y panel directo Android 1.4.14

- USB ya no muestra ni solicita un codigo. PC entrega una invitacion temporal por ADB al telefono seleccionado, con endpoint fijo 127.0.0.1:27214 y huella de certificado. Android valida el destino y usa TLS con certificado fijado. LAN conserva su verificacion QR.
- PC incluye Permitir recordar este telefono por USB, habilitado inicialmente. Recordar esta PC en Android guarda la autorizacion en ambos equipos. USB y LAN usan almacenes de confianza separados en PC; las credenciales USB solo aceptan el destino loopback fijo. Telefonos autorizados permite revocar ambos. La reconexion USB requiere cable y listener preparado en PC.
- Una sola entrada Ajustes en la app contiene los ajustes VE y tambien los controles de burbuja, opacidad, permisos, favoritos y acceso multimedia. No hay boton de configuracion duplicado en Control.
- La burbuja ya no usa FLAG_SECURE, incluida la forma plegada: los valores antiguos de exclusion no generan zonas negras. El icono queda recortado al contorno circular. Las tarjetas del panel tienen altura y separacion explicitas para evitar solapamientos.
- Monitor, perfil, bitrate, resolucion, FPS y audio del panel se eligen y aplican dentro de la burbuja, sin abrir la app. Cada seleccion envia el conjunto validado y reinicia VE; durante grabacion se bloquean. La app principal conserva Aplicar cambios para sus borradores.
- Corregida la navegacion que cambiaba de pestana con cada evento de sesion. Corregida la carrera de cierre de listeners cuando Stop coincidia con Accept.
- Prueba local TLS USB: conexion inicial, permiso de recordar de PC, guardado, reconexion y rechazo tras revocacion. Suite completa: 288/288 aprobadas tras corregir el cierre. Esto no equivale a probar ADB ni la apariencia en un telefono.
- Version Android 1.4.14, codigo 14. Requiere actualizar y reiniciar PC, e instalar el APK nuevo. Pendiente verificar el enlace ADB real y capturas de VE con el panel y la burbuja.
- Android compilado y paquete firmado sin errores ni advertencias. SHA-256 del APK: 7B59967BBBEF842E69D9CEC157C67A875424242F9815FC8A1F671CDB3C230360.

## Ajustes unificados y monitor Android 1.4.13

- Un selector de monitor del PC muestra los nombres y dimensiones anunciados por Windows. DisplaySettingsChanged actualiza la lista por eventos; se comprueba de nuevo la presencia del monitor al aplicar.
- Monitor, perfil, bitrate, resolucion, FPS y salida de audio se envian juntos mediante applyVideoSettings. Desaparecen los botones Guardar por fila. La burbuja lleva a la pantalla unificada para estos ajustes.
- Una sola accion: Aplicar cambios y reiniciar VE; si esta detenido, Aplicar cambios e iniciar VE. La PC valida todos los valores y la revision antes de detener, vuelve a validarlos antes de modificar, guarda y arranca VE. Las selecciones explicitas prevalecen sobre los valores por defecto del perfil.
- Mientras se graba o prepara una grabacion, aplicar queda bloqueado. Un fallo de inicio se informa como ajustes guardados sin inicio confirmado; no se presenta como reinicio exitoso. LinkEngine mantiene su estado.
- Requiere la PC actualizada: CanApplyTogether anuncia compatibilidad. PCs anteriores deshabilitan el boton. Version Android 1.4.13, codigo 13.
- Paquete Android firmado sin errores ni advertencias. SHA-256: 21011A78AAABEDE4507CAD89AEBFB629AFAE2F3C7FEDEC43DA2E93568F1F6DB5. Suite completa: 285/285 aprobadas en esta ejecucion; esto no demuestra corregido el fallo intermitente de cierre previamente observado.
- 16 pruebas de validacion especificas cubren valores invalidos, monitor retirado, revision antigua, grabacion, capacidad de PC y estado de VE. Pendiente comprobar el reinicio y cambio de monitor en una sesion real PC-Android.

## Panel visible en VE y notificaciones Android 1.4.12

- El panel abierto ya no usa FLAG_SECURE: se permite su captura en VE y sus grabaciones. La preferencia experimental de exclusion solo afecta a la burbuja plegada. La interfaz explica que esa exclusion puede producir una zona negra.
- La notificacion existente muestra preparacion, grabacion activa, estado del audio y mensaje final o error confirmado por PC. Al perder la conexion se muestra la perdida de conexion, sin mantener una confirmacion antigua de grabacion.
- Las actualizaciones usan Session.Changed; no se agregan consultas periodicas. Android 13+ requiere permitir las notificaciones para verlas en el panel de notificaciones.
- Version 1.4.12, codigo 12. Pendiente comprobar en un telefono el panel transmitido por VE y las transiciones de grabacion en notificaciones.

## Burbuja y controles multimedia Android 1.4.11

- El logo respeta el padding interior al calcular su centrado.
- Las apps elegidas pueden mostrar una tarjeta de reproduccion si publican una sesion multimedia Android: titulo, artista, progreso horizontal, duracion, tiempo restante, anterior, reproducir/pausar y siguiente. Los botones solo se habilitan cuando la app declara admitir la accion.
- Las apps sin sesion multimedia conservan su acceso para abrirlas. No hay controles inventados para apps que no los exponen.
- En Control flotante, usar Activar controles multimedia y habilitar NOVORA en Acceso a notificaciones de Android. Es opcional; el servicio no lee ni almacena contenido de notificaciones.
- MediaSessionManager y MediaController entregan cambios por eventos. El reloj se extrapola localmente cada segundo mientras hay una tarjeta abierta reproduciendo; se cancelan temporizadores y callbacks al plegar o retirar la burbuja.
- Compatibilidad minima conservada: Android API 26. Version 1.4.11, codigo 11. Pendiente la validacion visual y de controles con Spotify en un telefono real.
- Paquete firmado Android y compilacion PC correctos, sin errores ni advertencias. SHA-256 del APK: 7570DBF1DE8F39E45A82AB35EAB00F0387659A720F55876E00CF444CD5D0AA7B. Coincide con la copia distribuida por PC.
- Referencia: https://developer.android.com/reference/android/media/session/MediaSessionManager

## Rediseno Android — 15 septiembre 2026

La pantalla principal se reorganiza con pestanas Conectar, Control y Ajustes. Conectar mantiene busqueda LAN, USB, QR, equipos guardados y pegado de invitacion. Control concentra la tarjeta de PC, VisionEngine, LinkEngine, archivos, manual y herramientas. Ajustes conserva perfil, bitrate, resolucion, FPS, audio, VPN, flotante y privacidad.

VisionEngine y LinkEngine usan un solo boton por motor. El texto cambia entre Iniciar y Detener segun las capacidades confirmadas por NOVORA PC. Detener conserva confirmacion de seguridad; iniciar LinkEngine conserva la regla de transporte USB y el permiso VPN de Android. La interfaz no habilita comandos si la PC no anuncia esa capacidad.

La burbuja flotante se ajusta al mismo lenguaje visual: logo NOVORA, panel compacto, herramientas principales, apps favoritas opcionales y controles rapidos. Si el usuario no elige apps favoritas, esa seccion no aparece.

Registro anterior: APK 1.4.10, versionCode 10, SHA-256 14CA10E53BA940BEBC82C8BAABA1AE39AC8B02FDA44648AA95A8CA036DC33438. La version visible se calcula como 1.4.$(ApplicationVersion) para mantenerla alineada con el codigo de version. Compilacion Android Release y paquete firmado: cero errores y cero advertencias. La corroboracion posterior del rediseno obtuvo 268/269 pruebas .NET aprobadas: fallo el cierre de NLControlServer en WrongCodeOrVersionCannotReadState. Tambien se detecto que RenderSession puede cambiar la pestana elegida. Ambos hallazgos siguen pendientes. El Samsung conectado tenia instalada la 1.4.8 en aquella comprobacion.


## Instalador Windows todo en uno — 15 septiembre 2026

La prerelease incluye instalador por usuario para Windows 11 24H2 o posterior x64, con .NET, herramientas, dependencia Visual C++ del relay y APK Android 1.4.8. [Descarga, instalacion y advertencias](NLDocumentationInstaller.md). [Pruebas del instalador y limites](NLDocumentationInstallerVerification.md). Los textos anteriores conservan su alcance historico; instalar no certifica todas las funciones PC-Android.


## Numeración y próxima release oficial

1.4 identifica la versión de NOVORA-LINK; .x es la revisión interna de compilaciones o ediciones. Consulta [numeración y aviso de 1.4 oficial](NLDocumentationReleaseVersion.md) y [pruebas de esta revisión](NLDocumentationReleaseVersionVerification.md). La revisión Windows 1.4.1-experimental comprueba una vez al abrir si existe una release oficial posterior.

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

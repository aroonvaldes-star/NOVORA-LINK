# Cierre funcional de VisionEngine Linux

Estado: aprobado por el usuario; implementación y pruebas locales completadas, validación física pendiente. Fecha: 2026-11-05.

## Objetivo y alcance

Completar la pestaña VisionEngine y su backend en `Dev_Local/src/NOVORA-LINUX`: perfiles, aceleración forzada, teclado/ratón, grabación, transferencia de archivos, monitor y audio. Conservar los cambios existentes de Copilot y LinkEngine. No modificar el comportamiento de Windows, ExInEngine ni LinkEngine. El usuario autorizó los cambios indispensables de LINK Control; el contrato compartido incorpora únicamente campos opcionales para FILES USB. No hacer commit, push ni instalaciones.

## Enfoques

1. Recomendado: extender Avalonia + SDL3 + FFmpeg y los contratos existentes de LINK Control. Conserva la arquitectura y las rutas USB/LAN; requiere completar eventos SDL, negociación y ciclo de vida de cada canal.
2. Sustituir la presentación por un reproductor embebido nuevo. Añade dependencias y una migración sin resolver los contratos de entrada y archivos; no se recomienda para este cierre.

El usuario aprobó extender la arquitectura existente, los cambios Android indispensables y la verificación de touchpad de laptop.

## Comportamiento propuesto

### Ajustes, perfiles y aceleración

- Separar identificadores del protocolo y etiquetas traducidas. Validar antes de modificar estado.
- Una configuración tipada, persistida por usuario, alimenta UI, USB y LAN. Cambios remotos actualizan la UI. Mostrar cuáles requieren nueva sesión y bloquear cambios incompatibles con grabación.
- Perfiles Automático, Competitivo, Equilibrado, Calidad, Fluidez y Software aplican presets explícitos de captura y decoder; los ajustes manuales sustituyen los valores de captura sugeridos.
- Separar perfil de aceleración: Automática, Software y Forzada con backend elegido entre los comprobados en este equipo. Descubrir FFmpeg y probar decodificación real; listar un decoder o renderer no acredita GPU funcional.
- Automática permite software y comunica el backend efectivo. Forzada falla con diagnóstico si el backend solicitado no funciona; no degrada silenciosamente a software. Mostrar decoder y renderer por separado.
- Verificar la cadena completa: descarga de frames hardware a RGBA y rotación antes de SDL cuando corresponda. No confundir aceleración del renderer con decodificación hardware.

### Teclado y ratón

- Procesar eventos de la ventana SDL y serializar el protocolo binario existente que recibe el servicio de accesibilidad Android. Un único escritor y un lector de respuestas por canal de control.
- Teclas de navegación, texto Unicode, botones, arrastre y rueda. Coordenadas transformadas por barras, escala HiDPI y rotación. Ignorar clicks fuera de imagen y liberar entradas al perder foco, cerrar o desconectar.
- Activación explícita del control remoto. Mostrar que Android requiere su servicio de accesibilidad; no prometer entrada si Android la rechaza.
- Límite confirmado por revisión independiente: el servicio Android actual solo ejecuta Backspace, Delete, Enter, Home, Back y Recents en su ruta de teclas, además de texto Unicode y gestos. Flechas, Tab, modificadores y atajos completos no están implementados allí. Dentro del alcance Linux se implementarán texto, esas teclas y ratón; teclado completo requiere ampliar el alcance Android o un mecanismo adicional expresamente acordado. El usuario aprobó ampliar Android. Se añadieron navegación, selección, Tab y atajos de edición accesibles; no se promete inyección global arbitraria.

### Grabación

- Botones iniciar/detener, carpeta y estado con ruta final. Propuesta por defecto: MKV con video y audio disponible, timestamps de origen y finalización verificable con FFprobe.
- Escribir temporalmente y publicar el archivo final solo después de cerrar correctamente el muxer. Evitar sobrescritura; conservar e identificar una captura parcial ante fallo.
- Iniciar desde keyframe con configuración del codec. La grabación no debe bloquear el drenaje del decoder. Rotación/cambio de geometría finaliza un segmento reproducible y comienza otro; comunicar segmentación.
- Detener, desconectar y revocar cierran captura y recursos. Audio ausente produce grabación de video con aviso.

### Archivos

- Envío y recepción PC ↔ Android con selección de archivos/carpetas, destino local, progreso y cancelación. Integrar el contrato FILES V2 y sus leases/canal autenticado de LINK Control, tanto USB como LAN.
- Mantener un solo lector/escritor por stream y separar archivos de mensajes de control. No anunciar soporte antes de conectar el canal y sus acciones de negociación.
- Negociar `FilesWireVersion = 2` y `CaptureGeneration > 0`, validar leases contra esa generación y descartarlos al cerrar/revocar. No degradar a V1 después de negociar V2 si falla el canal. La transferencia estará disponible durante la sesión VE autenticada; una sesión sin captura independiente queda fuera de este cierre.
- Validar rutas relativas y tamaños; impedir traversal, escapes por enlaces simbólicos y sobrescritura no autorizada. Verificar longitud/SHA-256 antes de finalizar y limpiar temporales cancelados.
- No sustituir la función por ADB push/pull: LAN debe funcionar sin ADB.

### Monitor, presentación y audio

- Enumerar monitores reales y salidas SDL de audio; ofrecer dispositivo predeterminado, dispositivo concreto y audio desactivado.
- Ventana y pantalla completa en el monitor elegido; respetar escala y relación de aspecto. Cerrar ventana detiene VE y libera recursos.
- Aplicar dispositivos de salida al crear la sesión. Ante desaparición de un dispositivo, informar y ofrecer el predeterminado, sin ocultar la elección efectiva.
- La UI y el snapshot reflejan el audio real, eliminando el estado fijo Disabled cuando hay reproducción.

## Arquitectura y secuencia

1. Configuración/perfiles compartidos, persistencia y sincronización UI/protocolo.
2. Detección y selección efectiva del decoder, pruebas de forzado y software.
3. Enumeración de salidas y ciclo de vida de ventana/audio.
4. Canal de entrada con eventos SDL, geometría y cancelación.
5. Grabador con timestamps, audio y segmentación.
6. FILES V2 USB/LAN y UI de transferencia.
7. Integración de la pestaña, pruebas conjuntas y documentación de evidencia.

Crear componentes Linux pequeños para configuración, capacidades, entrada, grabación y archivos. Las sesiones USB/LAN consumen esos componentes y conservan TLS/pinning, tokens, exclusión mutua y revocación existentes. Detener o revocar cancela conjuntamente los canales y espera su limpieza con límites explícitos.

## Criterios de aceptación

| Función | Evidencia requerida |
|---|---|
| Ajustes/perfiles | Pruebas de IDs traducidos, validación atómica, presets, persistencia, revisiones y sincronización |
| Aceleración | Prueba software real; prueba del backend hardware disponible; backend forzado inválido falla; automática informa fallback |
| Entrada | Bytes contrastados con contrato Android; Unicode, teclas, escala/rotación, pérdida de foco y desconexión |
| Grabación | FFprobe comprueba tracks/timestamps; reproducción/decodificación del resultado; stop, cancelación y rotación |
| Archivos | Ida/vuelta V2 por loopback autenticado USB/LAN, SHA-256, cancelación, rutas hostiles y reconexión |
| Salidas | Enumeración y aplicación real en SDL; pruebas de dispositivo ausente y cierre de ventana |
| Integración | Build Release limpio, suites Linux, comprobación de UI y cero controles de VE que anuncien función implementada sin backend |
| Hardware | Teléfono LINK Control por USB y LAN: video/audio, entrada, archivos, grabación, rotación, desconexión y revocación |

Las pruebas simuladas y smoke tests no sustituyen hardware. Si falta teléfono, GPU o una segunda salida, registrar el caso como no verificado; no declarar cierre integral físico. Tampoco sustituir funciones faltantes por controles decorativos.

## Decisiones propuestas por el implementador

MKV como contenedor inicial; segmentación por cambio de geometría; cambios de decoder/dispositivo para la siguiente sesión; conservación de la arquitectura SDL/FFmpeg y contratos Android actuales. El usuario aprobó estas decisiones y el alcance de teclado/ratón, incluyendo touchpad.

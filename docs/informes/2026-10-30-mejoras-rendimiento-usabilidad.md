# Mejoras de rendimiento y usabilidad de NOVORA LINK

Fecha: 30 de octubre de 2026

## Objetivo

Mejorar el rendimiento real, la estabilidad y la facilidad de uso de NOVORA LINK sin añadir telemetría permanente ni competir con VisionEngine o LinkEngine. Las recomendaciones distinguen entre los flujos de producción, diagnóstico y experimentación.

## Prioridad alta

### Corregir la privacidad de Live RTMP

Al activar privacidad, la salida Live puede conservar paquetes que ya estaban pendientes de envío. La sesión debe cancelar la publicación o vaciar su cola antes de mostrar el estado de pausa. Esta mejora protege el contenido capturado y evita trabajo de red innecesario durante una pausa de privacidad.

Archivo relacionado: `src/NOVORA LINK/LinkEngine/Egress/LEEgressLiveSession.cs`.

### Corregir reinicios rápidos de captura VE LAN

Dos solicitudes consecutivas de inicio pueden dejar un reemplazo esperando a una captura intermedia. Debe existir una única solicitud de captura pendiente y cancelable; el último intento debe sustituir siempre al anterior.

Archivo relacionado: `src/LINK Control/AndroidService/NLAndroidServiceVeLanCapture.cs`.

### Mantener aislado el modo experimental de red

El modo experimental ejecuta un ciclo cada segundo que toma muestras, evalúa congestión y puede aplicar políticas al relay o cambiar bitrate y FPS de VisionEngine. Es útil para investigación, pero no debe participar en una sesión de producción ni en una medición base de latencia.

- Mantenerlo desactivado por defecto.
- Mostrar un indicador persistente cuando esté activo.
- Desactivarlo automáticamente al iniciar una prueba de latencia o rendimiento base.
- Registrar los cambios aplicados solamente durante la sesión activa, sin historial automático.

Archivo relacionado: `src/NOVORA LINK/UI/NLUIWindowMainExperimentalNetwork.cs`.

## Prioridad media

### Ajustar el watchdog ADB en sesiones LAN

Cuando no hay un dispositivo USB seleccionado, la interfaz consulta ADB cada cinco segundos como mecanismo de respaldo. En una sesión LAN autorizada con VisionEngine o LinkEngine activos, esa consulta no aporta valor y puede iniciar trabajo ADB periódico.

- Suspenderlo mientras exista control LAN autorizado, VisionEngine LAN o LinkEngine LAN activos.
- Reanudarlo solamente cuando no haya una ruta de conexión activa.
- Mantener la protección existente que limita la enumeración simultánea.

Archivo relacionado: `src/NOVORA LINK/UI/NLUIWindowMainDiscoveryLifecycle.cs`.

### Pausar comprobaciones de actualización durante una sesión activa

La consulta de actualización de LINK Control se ejecuta al abrir NOVORA LINK y después cada quince minutos. Su costo suele ser bajo, pero no aporta valor mientras están activos VisionEngine, LinkEngine o una transmisión Live.

- Pausarla durante sesiones activas.
- Reanudarla al finalizar la sesión o limitarla a la apertura de la aplicación.
- Mantener la comprobación manual desde Configuración.

Archivo relacionado: `src/NOVORA LINK/UI/NLUIWindowMainAndroidUpdateLINK.cs`.

### Consolidar el estado visual de Streaming

La pestaña Streaming concentra captura, grabación y Live. Sus cambios de estado deben usar un único canal de actualización de interfaz y limitar el cálculo de contadores a una vez por segundo. Esto evita actualizaciones duplicadas y mantiene los estados de grabación y Live coherentes.

Archivo relacionado: `src/NOVORA LINK/UI/NLUIWindowMainStreaming.cs`.

## Mejoras de usabilidad

### Convertir Inicio en un flujo guiado por estado

El orden recomendado para una sesión debe ser visible desde Inicio:

1. Conectar el teléfono.
2. Autorizar la captura.
3. Abrir VisionEngine.
4. Abrir Streaming para capturar, grabar o transmitir.
5. Abrir LinkEngine únicamente cuando se requiera Internet o datos.

Esto reduce acciones deshabilitadas sin explicación y evita iniciar motores que no son necesarios para la tarea actual.

### Diferenciar los tres modos de salida

La interfaz debe distinguir de forma consistente:

- **Ver pantalla**: VisionEngine sin archivo ni salida RTMP.
- **Grabar en PC**: salida a archivo local.
- **Transmitir en vivo**: publicación RTMP(S).

Cada modo debe explicar sus requisitos, estado actual, consumo esperado y el siguiente paso ante un error.

### Añadir un modo Sesión de rendimiento

Al activar este modo, NOVORA LINK debe:

- Cerrar la telemetría visual opcional.
- Pausar comprobaciones automáticas de actualización.
- Desactivar funciones experimentales de red.
- Suspender el watchdog ADB cuando LAN sea la ruta activa.
- Conservar únicamente captura, audio, control y renderizado esenciales.

## Validación requerida

Antes de declarar completas estas mejoras, realizar pruebas físicas y comparativas:

- Medir CPU, memoria, FPS, bitrate, cuadros perdidos y latencia con telemetría cerrada y abierta.
- Repetir la medición por USB, LAN y Live RTMP.
- Validar dos monitores, controlador, audio interno y funcionamiento a 60 Hz.
- Probar pérdida y reconexión de red durante Live y durante VE LAN.
- Confirmar que privacidad corta la emisión sin enviar cuadros pendientes.

## Estado de aplicación (30 de octubre de 2026)

Verificado solo por compilación y suites de consola (`scripts/Verify-NOVORA-Repository.ps1`: código 0). Nada de esto se ha probado en el teléfono ni tiene prueba automática propia.

| Punto | Estado |
| --- | --- |
| Privacidad de Live RTMP | Aplicado: vacía la cola, descarta lo ya leído y reemplaza el AAC. Un paquete que ya se está escribiendo en el socket no se puede retirar. |
| Reinicios rápidos VE LAN | Aplicado: solo queda pendiente la última solicitud. |
| Modo experimental | Aplicado: indicador persistente y desactivación al iniciar una prueba de velocidad o una sesión de rendimiento. Ya estaba desactivado por defecto y sin historial. |
| Watchdog ADB en LAN | Aplicado. |
| Actualizaciones durante sesión | Aplicado: se omite el ciclo de 15 minutos; se conserva el botón manual de Configuración. |
| Estado de Streaming | Aplicado: eventos agrupados a 1 Hz como máximo. |
| Inicio guiado | Aplicado como línea de siguiente paso. |
| Tres modos de salida | Aplicado: requisitos, consumo cualitativo y siguiente paso ante error en Streaming. El consumo no está medido. |
| Sesión de rendimiento | Aplicado y guardado entre reinicios; solo cierra las mediciones en vivo de VisionEngine. |
| Validación física | Pendiente. |

## Conclusión

La mayor mejora no proviene de añadir más instrumentación. Proviene de eliminar actividad opcional durante sesiones activas, corregir los flujos de concurrencia críticos y separar claramente los modos de producción, diagnóstico y experimentación.

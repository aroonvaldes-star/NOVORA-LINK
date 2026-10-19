# LinkEngine: información de la sesión actual

Fecha: 2026-10-18. Alcance aprobado por el usuario: pantalla y datos internos de LE; corregir etiquetas de transporte, mezcla de información y métricas sin medición. El usuario pidió ejecutar las correcciones y evitar acumulación de históricos.

## Comportamiento

- La pantalla distingue conexión, tráfico/pruebas y detalles técnicos. Preparación de conexión y ajustes experimentales compartidos LE/VE/Archivos quedan en desplegables.
- El resumen usa el transporte activo USB/LAN. Una conexión DATA no constituye una prueba de acceso a Internet.
- Tráfico en vivo, prueba del enlace y prueba de Internet son datos distintos. Subida y bajada siempre se expresan desde Android.
- Solo se presentan mediciones de la sesión actual. Una nueva conexión, transporte o cierre invalida las anteriores, incluidas respuestas HTTP y callbacks pendientes.
- Ausencia de medición se representa como desconocida, no cero. Las tasas sin contadores recientes se ocultan; cero sigue siendo válido cuando se ha medido.

## Estado y memoria

`LESessionPresentation` traduce los estados internos del runtime a un resumen consistente sin eliminar los estados necesarios para transporte y recuperación.

`LESessionMeasurements` conserva una muestra anterior de contadores, acumulados de bytes observados, dos tasas opcionales y como máximo el último resultado de cada una de las dos pruebas. No contiene listas ni históricos. `End` libera esos datos. Los bytes son observados desde la primera muestra aceptada; no se atribuyen bytes anteriores a la sesión.

USB identifica la sesión con dispositivo, inicio, conexión y transporte del runtime. LAN identifica el gateway, la autorización y su generación de conexión DATA; reconectar al mismo gateway también crea una sesión nueva. Un identificador de medición independiente impide reutilizar callbacks anteriores.

El monitor deja de crear JSONL; el servidor de pruebas deja de crear JSON. Un botón permite exportar únicamente el resumen actual, bajo acción explícita del usuario. No se cargan ni borran históricos existentes. El diagnóstico acotado del relay sigue usando su archivo sustituido, separado de las mediciones.

## Compatibilidad y límites

- Mantener túneles, políticas y protocolo DATA; no modificar el relay Rust ni la APK.
- Las pruebas por LAN se pueden solicitar desde AppControl sin ADB. La apertura del navegador desde PC continúa usando ADB cuando existe serial; en LAN nativa la pantalla indica abrir la medición en AppControl.
- Campos internos opcionales de latencia, caudal, sesiones TCP/UDP y errores DNS usan valores anulables. El tráfico conserva indicador explícito de disponibilidad.
- No se afirma funcionamiento físico, rendimiento de red ni evaluación visual a partir de compilaciones y pruebas locales.

## Verificación

Regresiones: sesión nueva, heartbeat sin reinicio, cambio USB/LAN, cierre, resultado tardío, reinicio del relay, muestra caducada, cero medido, métricas sin productor, configuración HTTP invalidada y ausencia de archivos automáticos. Compilar escritorio y ejecutar la suite de LinkEngine.

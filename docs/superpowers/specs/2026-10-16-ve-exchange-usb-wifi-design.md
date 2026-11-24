# Exchange de VisionEngine independiente del transporte

Fecha: 2026-10-16  
Estado: propuesta para revisión del usuario  
Alcance: hacer disponible cada función de Exchange cuando la sesión activa de VisionEngine se inicia por USB o por Wi-Fi/LAN.

## Objetivo y criterio rector

El teléfono puede iniciar VisionEngine por USB o por Wi-Fi/LAN. En cualquiera de esos casos, el conjunto de funciones de Exchange debe estar disponible por el transporte de esa sesión. Ninguna función puede requerir que el otro medio también esté conectado. Esta especificación no requiere migrar o reanudar una transferencia que ya estaba en curso al cambiar de USB a Wi-Fi o viceversa; cada operación nueva usa la sesión VE activa.

La ruta de transporte pertenece a la sesión, no a la función. La UI y las funciones de alto nivel solicitan una operación a Exchange; Exchange la entrega al adaptador asociado con la sesión VE activa. La identidad del dispositivo y el endpoint de transporte se mantienen como datos distintos: un host LAN nunca se entrega a una operación ADB como si fuera un serial.

## Funciones incluidas

| Función | USB | Wi-Fi/LAN | Resultado esperado |
|---|---|---|---|
| Texto y portapapeles | Canal de control de la sesión USB | Canal de control LAN autenticado | Texto sincronizado en la dirección que ya expone la función, con estado y errores visibles. |
| Envío de archivos | Adaptador Exchange USB | FILES sobre la sesión LAN autenticada | El archivo llega al área NOVORA del teléfono y conserva nombre/categoría admitidos. |
| Envío de carpetas | Adaptador USB con rutas relativas | FILES LAN con rutas relativas | Se conserva la estructura dentro del límite de seguridad admitido. |
| Recepción de archivos | Adaptador USB | FILES LAN bidireccional | Los archivos autorizados se guardan en `NOVORA-Files` en la PC y se informa el resultado. |
| Recepción de la carpeta NOVORA | Adaptador USB | FILES LAN bidireccional | Puede solicitarse y completarse sin ADB cuando VE está en Wi-Fi. |
| Imágenes y medios | Se enrutan como payloads por USB | Se enrutan como payloads por FILES LAN | Drag/drop, pegar o selección conserva el tratamiento por tipo y su categoría. |
| APK | Transferencia por USB; instalación, si se solicita, pasa por el instalador Android soportado | Transferencia por LAN; instalación solicitada mediante Android PackageInstaller | Copiar un APK no se reporta como instalado. Android muestra la confirmación/permisos que exija el sistema; el resultado final se informa aparte. |

Las funciones de imagen y medios son clases de contenido de las operaciones de archivos; no necesitan un transporte paralelo. Texto/portapapeles usa el canal de control disponible en ambos tipos de sesión.

## Diseño

1. **Contrato común de Exchange:** exponer operaciones de texto/portapapeles, envío y recepción de archivos, transferencia de carpetas, progreso, cancelación y estado. Cada operación consulta las capacidades de forma independiente; una capacidad de drag/drop no debe habilitar o bloquear toda la transferencia de archivos.
2. **Selección por sesión VE activa:** al iniciar VE, registrar el tipo de transporte y su endpoint autenticado/serial. Crear el adaptador Exchange correspondiente y asociarlo a la generación de sesión. Los consumidores de Exchange no seleccionan ADB o LAN mediante prefijos de texto ni inspeccionan rutas internas.
3. **Adaptadores por transporte:** USB conserva el transporte USB existente y completa las direcciones faltantes; Wi-Fi usa el canal LAN autenticado y el protocolo FILES bidireccional negociado para esa sesión. El adaptador LAN no dependerá de abrir manualmente la pantalla Archivos ni de seleccionar una carpeta como requisito previo para enviar.
4. **Protocolo y almacenamiento:** ambas rutas aplican las mismas reglas funcionales de nombres, categorías y directorios relativos. Se confina el acceso a los árboles seleccionados; se rechazan enlaces/reparse points y rutas que escapen del destino. Los archivos recibidos se confirman con tamaño y SHA-256 antes de hacerlos visibles como completos. Los parciales se eliminan al cancelar o fallar.
5. **APK:** separar explícitamente transferir de instalar. La transferencia termina cuando el APK quedó íntegro en Android. Si el usuario ejecuta instalar, la app invoca el flujo Android permitido y espera el resultado/confirmación del sistema; Exchange no declara éxito de instalación por el éxito de la copia.
6. **Eventos y errores:** ambas rutas ofrecen el mismo ciclo observable de encolado, progreso, éxito, cancelación y fallo. Si una capacidad no está disponible en una sesión, la operación muestra un error concreto y no intenta en silencio usar el otro transporte.
7. **Ciclo de vida:** las operaciones se atan a la generación de sesión VE. Al detenerse o invalidarse la sesión, cancelar el trabajo pendiente, cerrar sus canales, limpiar parciales y rechazar callbacks atrasados. Una sesión nueva crea adaptadores nuevos.

## Seguridad y privacidad

- LAN solo acepta operaciones dentro de la sesión autorizada/autenticada de AppControl y su canal de archivos negociado.
- Las reglas de privacidad actuales de VE continúan bloqueando operaciones protegidas, tanto en USB como en LAN.
- La recepción conserva la autorización explícita del teléfono para leer/compartir archivos donde Android la requiera. El destino PC es `NOVORA-Files`.
- No se usa ADB como fallback oculto de una sesión Wi-Fi ni se simula instalación de APK. Fallos de autenticación, permisos, protocolo o integridad se muestran al usuario y no dejan el elemento marcado como completado.

## Aceptación

1. Con una sesión VE iniciada solo por USB, cada función de la tabla opera y su resultado se ve en la UI.
2. Con una sesión VE iniciada solo por Wi-Fi/LAN, las mismas funciones operan sin cable USB, sin serial ADB válido y sin abrir manualmente Archivos para preparar el canal.
3. En ambas rutas, texto/portapapeles, archivos individuales, carpetas, imágenes, medios y APK llegan al mismo destino lógico y respetan categoría/ruta relativa.
4. La recepción de archivos y de la carpeta NOVORA funciona en ambas rutas, deja archivos íntegros en `NOVORA-Files` y presenta un resultado verificable.
5. El APK copiado se distingue de uno instalado; la instalación informa el resultado real del sistema Android.
6. La pérdida o cierre de la sesión cancela o falla operaciones de esa generación con estado visible, no completa una transferencia parcial y no afecta una sesión posterior.
7. Las capacidades de cada operación se anuncian/evalúan por separado; la ausencia de drag/drop no deshabilita portapapeles o transferencias que sí estén disponibles.

## Fuera de alcance

- Cambiar de transporte en mitad de una operación, continuarla automáticamente o unir USB y Wi-Fi en una ruta híbrida.
- Garantizar la calidad de video, latencia de pantalla, sincronización audiovisual o rendimiento general de VE; son problemas relacionados pero distintos.
- Instalar APK silenciosamente o eludir diálogos/permisos del sistema Android.
- Cambiar la organización global de archivos fuera de las ubicaciones NOVORA ya definidas.

## Contexto del código observado

El checkout actual contiene tanto rutas ADB como adaptadores FILES LAN v1/v2. La UI distingue NativeLan para enviar, pero `ReceiveNovoraFolder_Click` todavía llama al servicio ADB con la identidad activa; el identificador de una sesión LAN tiene forma `LAN:<host>`. FILES v1 depende de que Android abra Archivos/seleccione NOVORA, mientras que el código LAN v2 presente en el checkout ya negocia una transferencia con recepción y envío. El runtime de integración también construye directamente funciones ligadas a ADB. Estos son puntos que el plan deberá resolver; su presencia en el checkout no se considera evidencia de que el flujo completo funcione en un teléfono.

## Verificación futura

La implementación necesitará comprobaciones de contrato para USB y LAN por separado y una validación física de cada conexión sin el otro medio. Compilación o loopback no serán reportados como prueba de la sesión real PC-Android. La matriz de dispositivos, Android y resultados se definirá en el plan; ninguna evidencia física se anticipa en esta especificación.

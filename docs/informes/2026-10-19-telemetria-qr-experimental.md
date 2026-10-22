# Preparación experimental: LINK Control y launcher

## Cambios implementados

- Telemetría visual de VisionEngine cerrada por defecto. El muestreo de CPU/memoria y el texto de este panel se solicitan al abrirlo y se limitan a una vez por segundo. Los controles operativos de captura siguen activos.
- Notificaciones de LINK Control agrupadas por estado visible: se evita reconstruirlas si el contenido no cambia. Los resúmenes públicos describen conexión, motores o grabación sin nombres privados.
- Descarga Android mediante QR solicitado por el usuario. Se retiraron del launcher los controles de instalación y comprobación de APK. El QR usa una APK publicada en el repositorio público NOVORA-LINK, sin autenticación; admite versiones experimentales.
- El flujo de publicación incluye NLAndroidApp.apk, descriptor y hashes en la release pública. Actualmente falta publicar ese archivo canónico: el launcher informa que no hay descarga disponible hasta entonces.
- Símbolos vectoriales USB y Wi-Fi en las tarjetas de selección. Colores adaptados al tema.
- Casillas y entrada LAN con texto y acento explícitos para tema claro/oscuro. Los controles deshabilitados mantienen texto legible.
- Eliminado el desplazamiento heredado de hasta 180 dp en el inicio: detección, elección y motores parten del mismo margen superior, respetando los márgenes del sistema.

## Verificación

- 22 suites automatizadas completadas correctamente (incluyen descarga pública y telemetría/notificaciones).
- Compilación Release de escritorio correcta con salida de verificación separada, porque la aplicación principal estaba en uso.
- Compilación Android Release arm64, objetivo Compile: sin errores ni advertencias después de los ajustes visuales.
- Validación de workflows con actionlint correcta.
- No se ha generado, instalado ni publicado una APK nueva con estos cambios. No hay validación visual ni medición de latencia en dispositivo para esta revisión.

## Comparación física VE LAN pendiente

1. Usar el mismo teléfono, PC, red, escena y perfil. Registrar resolución, FPS y bitrate efectivos; descartar muestras si cambian.
2. Calentar la sesión durante un minuto. Ejecutar cuatro tramos iguales en orden cerrado/abierto/abierto/cerrado para el panel de telemetría, anotando inicio y fin.
3. Comparar fluidez, CPU y memoria con una herramienta externa constante. Para latencia extremo a extremo, filmar una acción visible y su respuesta con una referencia común; no equiparar tiempos del decodificador con latencia total.
4. Abrir la cortina de notificaciones con captura activa y parada. Verificar texto útil, estabilidad del contenido y privacidad del resumen público.
5. Revisar las tres pantallas y el formulario LAN en claro/oscuro, vertical/horizontal y con teclado visible. Confirmar que no se recortan controles ni se debilita la confirmación de huella.

La reducción real de latencia queda pendiente de estas mediciones; los resultados de compilación no la demuestran.

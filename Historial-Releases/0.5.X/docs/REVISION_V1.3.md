# Revisión V1.3

## Correcciones
- Se elimina la dependencia de scripts interactivos como núcleo del Launcher.
- Se elimina la ejecución arbitraria de comandos (`call !CMD_INPUT!`).
- Se centralizan las rutas en `NovoraPaths`.
- Se renombra el bundle de ejecución a `Tools/Runtime`.
- Se conserva el payload binario necesario de ADB, scrcpy y Gnirehtet.

## Rendimiento
- NOVORA consulta capacidades de pantalla mediante ADB.
- La captura se limita al máximo útil para el monitor seleccionado.
- FPS se calcula usando el límite de pantalla del dispositivo y la frecuencia real reportada por Windows para el monitor seleccionado.
- Se usa H.264 como perfil base por compatibilidad/latencia.
- Se desactiva audio por defecto para priorizar la latencia de video.
- No se añade `--video-buffer`, por lo que scrcpy mantiene su comportamiento de baja latencia.

## Gnirehtet
- Se valida ADB antes de iniciar.
- Se comprueba si el APK ya está instalado.
- Se instala automáticamente si falta.
- Para un solo dispositivo se usa `gnirehtet run`; con varios se usa `relay` + `start`.
- Se instala el APK automáticamente si falta.
- Se vuelve a comprobar ADB y se hace una prueba de conectividad IP cuando el dispositivo la permite.

## Limitación importante
No se puede garantizar cero lag ni 99% de funcionalidad universal. El rendimiento depende del USB, controlador ADB, codificador del teléfono, GPU/CPU del PC, monitor, temperatura, versión de Android y versión concreta de scrcpy/Gnirehtet. NOVORA debe medir y reportar esas condiciones en lugar de prometerlas.

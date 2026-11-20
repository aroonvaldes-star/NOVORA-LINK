# Audio de NOVORA

NOVORA ahora contempla audio como una función configurable del Launcher.

## Modos
- Audio activado: scrcpy se inicia con soporte de audio.
- Audio desactivado: se utiliza `--no-audio` para priorizar el flujo de vídeo.

## Importante
El soporte final depende de la versión de scrcpy incluida y de la versión de Android del dispositivo.

El Launcher no debe afirmar que el audio funciona solo porque el proceso inició. La fase de verificación deberá comprobar que la sesión de audio realmente está activa.

## Rendimiento
El usuario puede desactivar audio cuando busque la mínima carga y latencia posible en vídeo.

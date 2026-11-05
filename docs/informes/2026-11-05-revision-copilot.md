# Revisión de la integración de Copilot — 2026-11-05

## Alcance y conclusión

Revisión selectiva del código integrado en `99b2675` y del merge `00662bc`, frente
a `607843f`. La atribución a Copilot procede de las bitácoras: los commits tienen
autor humano. El merge incorpora un cambio de README y conserva el trabajo local.
No se modificó código de producto durante esta revisión.

Windows compila desde Linux y las suites ejecutadas pasan. Esto acredita la
compilación y los casos cubiertos; quedan problemas Linux de recuperación y
cobertura. La integración no constituye una certificación de funcionamiento
integral ni una release. No se validó Android físico ni la interfaz WPF en Windows.

## Hallazgos

### Media: se descarta el paquete que provoca la recreación del decoder

`src/NOVORA-LINUX/VisionEngine/LinuxVisionLanEngine.cs:154-162` y
`LinuxVisionEngine.cs:223-230` recrean FFmpeg al cambiar dimensiones o rotación,
reenvían la configuración y hacen `continue` sin enviar el paquete actual.
Android emite el cambio de sesión justo antes del keyframe solicitado al rotar
(`src/LINK Control/AndroidVideo/NLAndroidVideoService.cs`, RunVideoAsync).
Por tanto, ese keyframe puede perderse y la imagen esperar al siguiente.
La prueba actual inicia el decoder ya rotado; no cubre la transición en vivo.
Dirección de corrección: reenviar el paquete actual si contiene imagen, sin
duplicar el de configuración. Evidencia estática, sin reproducción física.

### Media: sigue sin supervisarse la tarea de presentación

Los bucles de USB y LAN supervisan audio, pero no `decoder.FrameTask`. Si
`LinuxSdlVideoRenderer.Present` falla, deja de drenarse stdout de FFmpeg; la sesión
puede conservar `Streaming` y bloquearse después al escribir en stdin.
El hallazgo anterior LIN-01 sigue pendiente. Supervisar la terminación del decoder
y cancelar conjuntamente los canales. Evidencia estática; las pruebas de smoke
consultan FrameTask, pero los motores de producción no lo hacen.

### Media: el smoke FFmpeg puede omitirse sin comunicarlo

`src/NOVORA-LINUX/tests/Program.cs:454-464` retorna sin error si no encuentra
FFmpeg o si falta `libopenh264`. La suite puede finalizar correctamente sin probar
decodificación ni rotación. El workflow Linux instala FFmpeg, pero no verifica
que incluya ese encoder. En esta revisión sí apareció explícitamente
`VisionEngine FFmpeg decoder smoke test passed.`; no se omitió en este host.
Exigir el encoder en CI o declarar la omisión de forma visible y comprobable.

### Automatización heredada pendiente

La CI principal conserva rutas `src/NOVORA` y `src/NOVORA.Android` y no se dispara
por push a `main`. El workflow Linux nuevo sí cubre Linux, pero construye el relay
sin ejecutar `cargo test`. La compilación cruzada de Windows y las pruebas Rust
se ejecutaron manualmente en esta revisión. No se repararon workflows bajo esta
solicitud de revisión/publicación.

## Verificación nueva

| Verificación | Resultado |
|---|---|
| Build Windows Release win-x64, SDK oficial 8.0.427, EnableWindowsTargeting | PASS, 0 advertencias y 0 errores; 11.04 s |
| Suite Linux Release (compilación incremental y ejecución) | PASS: protocolo, LAN, revocación, FFmpeg rotado y SDL |
| RelayStartup Release | PASS, 5 casos, incluido listener ajeno |
| RelayPolicy Release | PASS, 9 comprobaciones |
| cargo test --locked del relay Linux | PASS, 29 pruebas |
| git diff 607843f..HEAD --check | PASS |
| Tag SDL release-3.2.0 | Remoto coincide con el commit fijado 535d80badefc83c5c527ec5748f2a20d6a9310fe |

SDL emitió advertencias Mesa/Zink; la prueba terminó con backend OpenGL. No se
infiere aceleración GPU de ese resultado. No se añadieron pruebas nuevas.

## Bitácoras y publicación

Se contrastaron `NOVORA-LINK/.agents/bitacora.md` y `BITACORA_UNIVERSAL.md`.
La instalación de la revocación inmediata, el renderer y LAN coinciden con el
código y las pruebas. Las entradas anteriores “sin commit” quedan superadas por
la entrada del 5 de noviembre que registra `99b2675` y `00662bc`.
La última fila de Copilot tiene cuatro columnas en ambas bitácoras, frente a las
cinco/seis de sus cabeceras; es una anomalía de formato, no evidencia de pérdida
del contenido. Se conserva el historial y se agrega el resultado de esta revisión.

Los informes del 4 de noviembre son cortes históricos: las afirmaciones de Linux
sin seguimiento o LAN pendiente no describen todo el estado integrado actual.
El estado previo a publicar contiene únicamente `.claude/settings.local.json` y
`PES_STM/` sin seguimiento; ambos quedan fuera del commit y del push.
El push solicitado se realizará como avance normal de `main`, sin force-push.

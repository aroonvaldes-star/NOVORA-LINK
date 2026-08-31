# NOVORA-LINK 1.3 — base del repositorio

## Correcciones de estructura

- La solución sale de la carpeta histórica `VS/` y queda en la raíz con código en `src/NOVORA/`.
- Se excluyen `.vs/`, `bin/`, `obj/`, perfiles de usuario y artefactos del instalador.
- Las dependencias binarias de terceros se restauran de forma reproducible con SHA-256.
- La documentación técnica se concentra en `docs/`.

## Correcciones de release

- La versión base pasa a 1.3.
- `UpdateService` consulta `aroonvaldes-star/NOVORA-LINK`.
- El workflow genera `NOVORA-Setup-<version>.exe`, coincidiendo con el formato aceptado por el actualizador.
- El tag (`v1.3`, `v1.4`, etc.) es la fuente de verdad de la versión en CI.

## Auditoría runtime 1.3

- El caché de Red y Rendimiento queda separado por serial ADB para evitar mezclar datos entre dispositivos.
- ADB, Screen Mirroring y Gnirehtet validan sus propias dependencias de Tools de forma independiente.
- La ausencia o cuarentena de `scrcpy.exe` ya no bloquea el descubrimiento ADB, Red ni Rendimiento.
- Los monitores usan resolución real para perfiles de salida y área útil para posicionamiento de ventanas.
- La etiqueta del monitor principal ya no duplica `Principal` y la selección se persiste mediante `DeviceName` estable.
- Performance reconoce el formato de resumen de CPU utilizado por distintas implementaciones de `top` en Android.
- Red sólo muestra Internet USB activo cuando Gnirehtet corresponde al dispositivo seleccionado.
- PLAY/STOP e INTERNET USB sincronizan su texto con el proceso y dispositivo activos.
- Se añadieron pruebas de regresión para Tools, caché por dispositivo, etiquetas de monitor y CPU.

## Verificación

- GitHub Actions ejecuta la solución en Windows con .NET 8.
- El commit de código auditado `67073daee229cbf19a52640670f1f3a38122ee29` pasó Restore, Build y Test correctamente antes de promoverse a `release/1.3`.
- El workflow de release vuelve a validar Publish, Inno Setup y SHA-256 después de cada actualización de `release/1.3`.

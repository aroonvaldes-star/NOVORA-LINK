# NOVORA-LINK 1.4 PRERELEASE PRE FINAL

Esta carpeta es la candidata reestructurada para actualizar la rama principal.

No se declara Release estable por el solo hecho de compilar. La publicación estable requiere los gates definidos en `NLDocumentationProjectRules.md` y los P0 de `Release/NLDocumentationPreFinalFunctionalAnalysis.md`.

## Versionado

- Línea de producto: **1.4**
- Checkpoint: **PRERELEASE PRE FINAL**
- Escritorio: `1.4.1-prerelease-prefinal`
- Android fuente actual: `1.4.26` (`VersionCode` 26).
- APK firmado e integrado para instalación desde PC: `1.4.26` (`VersionCode` 26), SHA-256 `F95B77F7DE4B882FF6B25E75B595587F14471B31C5274C4E27AABA997FAF3EAC`.
- Cada nuevo empaquetado Android Release debe incrementar `ApplicationVersion` y `ApplicationDisplayVersion` antes de generar el APK; no se reutiliza una versión Release ya empaquetada.
- Cada compilación que incorpore uno o más cambios en Android recibe una revisión nueva: `VersionCode` aumenta en uno y `VersionName` usa `1.4.<VersionCode>`. Repetir la compilación de la misma revisión únicamente para validarla no vuelve a incrementar el contador.
- Próxima revisión Android: `1.4.27` (`VersionCode` 27) cuando exista el siguiente cambio de fuente Android; una recompilación de validación de `1.4.26` no consume otra revisión.

## Regla de publicación

Antes de publicar:
1. Release Gate automatizado.
2. Relay recompilado.
3. USB físico.
4. LAN/QR/trust.
5. VE físico.
6. Seguridad/rendimiento/calidad.
7. Installer Inno Setup.
8. Manual ES/EN verificado en PC, Android y distribución.

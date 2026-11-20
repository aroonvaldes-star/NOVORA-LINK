# NOVORA — Sistema de actualizaciones

## Estado del documento

Este procedimiento describe el actualizador histórico de NOVORA 1.0.1. Esa versión descarga por HTTPS, pero no valida SHA-256; no la uses como guía para publicar ni como canal recomendado de actualización. Las versiones actuales se distribuyen desde el [portal oficial](https://novora-link.aroonvaldes.workers.dev/) y las [releases vigentes](https://github.com/aroonvaldes-star/NOVORA-LINK/releases).

## Cómo funciona

NOVORA 1.0.1 comprueba manualmente las releases estables de GitHub en:

`https://github.com/aroonvaldes-star/NOVORA-LINK/releases/latest`

El botón **ACTUALIZAR NOVORA** compara la versión instalada con la última release estable. Si existe una versión mayor, descarga el instalador `.exe` de esa release y lo ejecuta con elevación para reemplazar la instalación.

No hay telemetría ni un proceso de actualización residente. La comprobación solo ocurre cuando el usuario pulsa el botón.

## Cómo publicar una actualización

1. Cambia la versión del proyecto en `src/NOVORA.Launcher/NOVORA.Launcher.csproj`.
2. Publica NOVORA como `Release / win-x64 / independiente del marco`.
3. Genera el instalador con Inno Setup.
4. El instalador debe terminar con este patrón:

   `NOVORA_<VERSION>_Complete_Setup.exe`

   Ejemplo:

   `NOVORA_1.0.2_Complete_Setup.exe`

5. Crea una **GitHub Release estable** con tag `v1.0.2`.
6. Adjunta el instalador `.exe` a esa release.
7. Los NOVORA 1.0.1 que pulsen **ACTUALIZAR NOVORA** detectarán `1.0.2`.

## Reglas importantes

- Usar tags de versión semántica: `v1.0.2`, `v1.1.0`, etc.
- No marcar la release como *Pre-release* si debe llegar a usuarios normales.
- El asset debe ser un instalador `.exe` terminado en `_Setup.exe`.
- Mantener HTTPS.
- El sistema no descarga archivos arbitrarios: selecciona exclusivamente un asset `.exe` con ese patrón dentro de la release estable de NOVORA.

# NOVORA-LINK 1.3.1

NOVORA-LINK 1.3.1 es una versión estable de mantenimiento de la línea 1.3 enfocada en asegurar la ruta de actualización hacia versiones futuras del proyecto.

## Cambios principales

- Versión de aplicación actualizada a **1.3.1**.
- El servicio de actualización apunta exclusivamente al repositorio oficial **aroonvaldes-star/NOVORA-LINK**.
- El actualizador ya no depende únicamente de la marca `latest` de GitHub.
- Se consultan hasta 100 releases y se selecciona la **versión estable semánticamente más alta** disponible.
- Se ignoran releases en estado draft y prerelease.
- Se aceptan futuras versiones como `1.4`, `1.4.1`, `1.5`, `2.0` y posteriores.
- La descarga del instalador sigue siendo únicamente por HTTPS.
- Se exige y verifica SHA-256 antes de ejecutar una actualización.
- El workflow de release soporta versiones de dos y tres componentes, incluyendo `v1.3.1`.

## Actualizaciones futuras

Esta versión está diseñada para actualizarse desde **1.3.1** hacia cualquier release estable superior publicada en el repositorio oficial que incluya un instalador con formato:

`NOVORA-Setup-<version>.exe`

Cuando exista una release estable superior, NOVORA comparará su versión instalada con las releases disponibles, elegirá la mayor versión válida y ofrecerá la actualización.

## Canal

- Versión: **1.3.1**
- Canal: **estable**
- Plataforma: **Windows x64**
- Runtime: **.NET 8 self-contained**

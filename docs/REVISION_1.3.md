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

## Verificación estática realizada

- Los XAML y XML del proyecto tienen sintaxis válida.
- No quedan referencias a `NOVORA-PROYECT` en el árbol preparado.
- No se incluyen carpetas de caché o compilación del ZIP original.

La compilación completa debe ejecutarse en Windows/.NET 8; el entorno de revisión no dispone del SDK `dotnet` instalado.

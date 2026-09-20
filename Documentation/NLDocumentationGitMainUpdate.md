# Actualización de la rama principal

Esta carpeta `NOVORA-LINK/` está preparada como candidata de contenido para la rama principal.

## Importante

No se incluye un directorio `.git` nuevo ni se reescribe el historial. La actualización debe realizarse sobre el repositorio Git real para conservar commits, tags, ramas y remotos.

## Flujo recomendado

1. Trabajar sobre una copia/working tree limpia del repositorio oficial.
2. Confirmar que no existan cambios locales que deban conservarse.
3. Sustituir el contenido de trabajo por esta carpeta, conservando `.git/`.
4. Ejecutar `Tool/NLToolReleaseGate.ps1`.
5. Revisar `git status` y `git diff`.
6. No subir `bin/`, `obj/`, `target/`, logs, resultados locales ni backups.
7. Ejecutar pruebas físicas obligatorias.
8. Sólo después crear commit/PR para `main`.

## Archivos Git incluidos

- `.gitignore`
- `.gitattributes`

No se crea `.github/workflows` de build automático en esta candidata porque el flujo Android requiere herramientas/workloads específicos y primero debe validarse el Release Gate local para no publicar un CI especulativo.

# Historial de versiones NOVORA-LINK

Este directorio conserva snapshots de código y recursos de versiones antiguas (`0.1.X`, `0.5.X`, `1.0.X`, `1.1.X`, `1.2.X`, `1.3.X` y `1.4.X`). Es un archivo de consulta, no el origen de los instaladores vigentes.

## Descargas y proyecto vigentes

- Portal oficial: https://novora-link.aroonvaldes.workers.dev/
- Código y releases: https://github.com/aroonvaldes-star/NOVORA-LINK
- Descargas estables: https://github.com/aroonvaldes-star/NOVORA-LINK/releases/latest
- Reporte de problemas: https://github.com/aroonvaldes-star/NOVORA-LINK/issues

## Inventario por serie

- `0.1.X`, `0.5.X`, `1.0.X`, `1.3.X`: snapshots parciales de fuente y recursos; no son artefactos instalables vigentes.
- `1.1.X` y `1.4.X`: solo tienen una nota de archivo porque la copia original no contenía archivos de producto.
- `1.2.X`: contiene documentación histórica, no una fuente completa ni binarios de release.

## Instalador actual

Los instaladores vigentes se compilan desde la raíz del repositorio con `scripts/Build-All-Installers.sh` (Linux/macOS) o `scripts/Build-All-Installers.ps1` (PowerShell), y se publican mediante los workflows actuales de GitHub Actions. Los scripts `.iss` dentro de snapshots antiguos son referencias históricas; no son entrada de la canalización actual.

Los instaladores Windows históricos conservan el mismo `AppId` y la carpeta `{autopf}\NOVORA` del instalador vigente. El instalador vigente limpia los ejecutables antiguos `NOVORA.exe` y `NOVORA.Launcher.exe`, reemplaza el acceso directo del menú Inicio y conserva el acceso directo de escritorio existente apuntándolo al ejecutable nuevo. No se reconstruyeron ni ejecutaron instaladores antiguos durante esta revisión.

## Alcance y límites

- Los archivos de código y recursos conservados no implican soporte para esas versiones. Para uso actual instala una release vigente.
- Algunos snapshots incluyen dependencias binarias históricas; revisa `NOTICE`/licencias antes de redistribuirlas fuera del repositorio.
- NOVORA 1.0.1 descarga instaladores por HTTPS sin validar el hash. Esa ruta es heredada y no se recomienda. El código de actualización 1.3 exige SHA-256, pero se ha corregido su destino de GitHub para apuntar al repositorio actual.
- Se omitieron cachés de IDE, directorios `bin`/`obj`, logs y preferencias locales al preparar el archivo para Git.

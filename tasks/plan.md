# Implementation Plan: NOVORA LINK — pendientes verificables de Dev_Local

## Overview

Este plan reúne los cambios pendientes que afectan la entrega actual de NOVORA LINK y que están respaldados por las auditorías de ExIn, LinkEngine y las rutas de compilación/prueba. No incorpora propuestas históricas de documentación como trabajo comprometido, ni modifica los dos cambios de retema visual que ya existen en el árbol sin una especificación y revisión de su autor.

## Architecture Decisions

- Corregir primero las rutas posteriores al renombre para recuperar una base de verificación antes de cambiar comportamiento.
- El relay sólo estará listo tras prueba criptográfica de pertenencia del proceso recién iniciado; que un puerto esté ocupado no es evidencia suficiente.
- Un perfil ExIn pertenece a una identidad de mando; nunca se migra ni se borra por coincidencia de modelo.
- La cadencia ExIn se mide como telemetría, no se infiere de FPS de vídeo ni de una compilación.
- Dark se corrige a través de la paleta y componentes compartidos de LINK Control, después de aprobar `SPEC-dark-accessibility.md`.

## Dependency Graph

```text
Rutas de proyectos y CI
    ├── Pruebas .NET recuperadas ──┬── Perfil ExIn seguro
    │                              └── Telemetría ExIn medible
    └── Verificación integrada

Identidad del relay ──> Arranque DATA seguro ──> Prueba PC–Android

SPEC Dark aprobada ──> Paleta/componentes Dark ──> Revisión visual Android
```

## Task List

### Phase 1: Recuperar verificación automatizada

- [x] Task 1: Corregir referencias de los proyectos de prueba críticos tras el renombre.
- [ ] Task 2: Migrar los restantes proyectos de prueba fuente-enlazada a `src/NOVORA LINK` y `src/LINK Control`.
- [ ] Task 3: Actualizar CI, análisis de seguridad y release para las rutas nuevas.

### Checkpoint: Rutas

- [ ] Las suites ExIn y LinkEngine arrancan desde sus `.csproj` reales.
- [ ] Los workflows no contienen rutas activas a los directorios renombrados.
- [ ] `dotnet build NOVORA.sln -c Release -p:UseSharedCompilation=false` concluye correctamente.

### Phase 2: LinkEngine seguro y recuperable

- [ ] Task 4: Verificar identidad del relay antes de declararlo listo.
- [ ] Task 5: Corregir la resolución de ejecutable de LinkEngine para el árbol renombrado.
- [ ] Task 6: Añadir regresiones de relay ocupado, relay ajeno y fallback de desarrollo.

### Checkpoint: LinkEngine

- [ ] Un proceso local ajeno en `127.0.0.1:27184` no hace que LinkEngine informe éxito.
- [ ] `cargo test --release --locked` pasa y la suite .NET de LinkEngine vuelve a compilar y pasar.
- [ ] Inicio, parada y recuperación no dejan relay ni ADB reverse huérfanos.

### Phase 3: ExIn correcto y medible

- [x] Task 7: Eliminar la adopción destructiva de perfiles de mando por modelo.
- [ ] Task 8: Instrumentar y probar orden/cadencia de telemetría de calibración.

### Checkpoint: ExIn

- [x] Desconectar un mando no mueve ni borra su perfil o sus paddles.
- [ ] Las muestras de calibración tienen secuencia ordenada y una cadencia observada verificable.
- [ ] La prueba física con mando y dos monitores queda registrada por separado.

### Phase 4: Accesibilidad Dark y cierre

- [ ] Task 9: Aprobar `SPEC-dark-accessibility.md` y definir tokens de contraste.
- [ ] Task 10: Aplicar y probar contraste Dark en botones, textos y estados deshabilitados de LINK Control.
- [ ] Task 11: Revisar por separado los cambios actuales de retema XAML antes de integrarlos.

### Checkpoint: Completo

- [ ] Pruebas pertinentes y build limpio.
- [ ] Validación manual PC–Android de USB/LAN, relay DATA, ExIn y Dark.
- [ ] No hay afirmaciones de rendimiento físico sin medición reproducible.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Rutas antiguas reparadas de forma incompleta | Alto | Buscar referencias activas y ejecutar cada suite afectada. |
| Un listener local suplanta el relay | Alto | Confirmación de identidad mediante nonce/token antes de exponer la sesión. |
| Migración de perfiles borra calibración válida | Alto | Eliminar migración automática; prueba de dos mandos del mismo modelo. |
| Pantalla o entrada no sostienen 60 Hz | Medio | Publicar métrica observada y documentar límite físico. |
| Cambios XAML simultáneos | Medio | Mantenerlos fuera de estas tareas hasta que se especifique su intención. |

## Open Questions

- ¿El usuario quiere preservar el formato de perfiles ya migrados o prefiere conservarlos como perfiles separados y exigir recalibración?
- ¿La métrica de Hz de ExIn será visible para el usuario final o sólo para diagnóstico?
- ¿Los cambios actuales en `NLResourceMainStyles.xaml` y `NLUIWindowMain.xaml` forman parte del rediseño Dark solicitado o de otro trabajo?

## Excluded Until Explicitly Authorized

- Propuestas NVIDIA, virtual-display nuevo, sustitución completa de Gnirehtet y otros documentos históricos sin evidencia de una incidencia actual.
- Instalación en teléfono, publicación, commit o push.

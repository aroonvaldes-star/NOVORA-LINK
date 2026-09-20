# NOVORA-LINK 1.4 PRERELEASE PRE FINAL — Análisis funcional

Fecha de preparación: 2026-09-17

## Alcance

Este informe se basa en el contexto maestro de la raíz activa `NL2\NOVORA-LINK`, no en los respaldos históricos. La captura indicó 1,278 archivos activos, cero bloqueados y cero cambios durante la recolección.

## Convención de estado

- ✅ **Conectado / presente en ruta activa**
- 🟢 **Verificado estáticamente**
- 🟡 **Integrado pero requiere build o prueba física**
- 🟠 **Parcial / backend pendiente**
- 🔴 **Bloquea Release**
- ⚪ **No verificado desde este entorno**

## Aplicación PC

| Área | Estado | Observación |
|---|---|---|
| Panel dinámico WPF | 🟢 | Inicio, Pantalla, Red, Rendimiento, Game Input, Integración, Privacidad y Configuración aparecen en la raíz activa. |
| MainWindow legacy conceptual | ✅ | El archivo se llama `NLUIWindowMain` por contrato WPF, pero la experiencia actual es el panel dinámico. |
| Manual ES/EN | ✅ | Botones añadidos en Configuración; los manuales se copian al output. |
| Actualización oficial | 🟢 | La UI declara consulta al abrir, no sondeo continuo. |
| Sintaxis `RefreshDevices_Click` | 🟢 | El bloque activo está corregido y no conserva la duplicación observada durante la auditoría. |

## Android

| Área | Estado | Observación |
|---|---|---|
| Package | ✅ | `com.novora.appcontrol`. |
| Conexión USB | 🟡 | Código y UI presentes; requiere prueba física final. |
| LAN / QR / confianza | 🟡 | Código presente; requiere prueba física final de pairing/reconexión. |
| Manual español | ✅ | Integrado. |
| Manual inglés | ✅ | Integrado en esta candidata. |
| VisionEngine / LinkEngine controls | 🟡 | Botones/rutas presentes; dependen de PC/backend físico. |

## LinkEngine

| Área | Estado | Observación |
|---|---|---|
| C# Core/Device/Transport/Network/Runtime | ✅ | Rutas activas presentes. |
| Recovery independiente | 🟢 | `LERecovery*` separado de VE. |
| TrafficEngine | ✅ | Fuente presente. |
| Regla 1 dispositivo | 🟢 fuente | `MaxConcurrentSessionsLE`, `MAX_ACTIVE_CLIENTS_LE` y `MAX_CLIENTS_LE` ajustados a 1. |
| Relay runtime `LENetworkRelay.exe` | 🔴 hasta rebuild | El binario incluido fue generado antes del cambio a 1 dispositivo; debe recompilarse en Windows y reemplazarse antes de publicar. |
| Gnirehtet runtime | ✅ retirado como app | RelayCore conserva código/atribución histórica de Genymobile; no se oculta su procedencia. |

## VisionEngine

| Área | Estado | Observación |
|---|---|---|
| Video / protocolo / transporte | ✅ código | Implementación activa presente. |
| Audio | ✅ código | Implementación activa presente. |
| Control mouse/teclado/touch | ✅ código | Implementación activa presente. |
| Gamepad SDL/UHID | ✅ código | Implementación activa presente. |
| PrivacyVE | ✅ código | Compuerta propia presente. |
| IntegrationVE | 🟡 | Infraestructura presente; varias capacidades dependen del backend real. |
| RecoveryVE | ✅ código | Recovery separado. |
| `scrcpy.exe` | 🟡 temporal | Aún existe como herramienta/console path; requiere atribución y futura sustitución si deja de ser necesario. |
| `scrcpy-server` | 🟡 temporal | VisionEngine todavía depende del servidor Android compatible. Se incluyó de nuevo en esta candidata y queda documentada su procedencia. |
| Prueba física de video/audio/control | ⚪ | No puede certificarse desde este entorno; obligatoria antes de estable. |

## STEngine

| Área | Estado | Observación |
|---|---|---|
| Motor bajo demanda | ✅ | `STCoreEngine` no crea timer/polling propio. |
| Análisis VisionEngine | ✅ | Puede analizar métricas VE cuando existen. |
| Análisis NOVORA | ✅ | Puede combinar snapshot VE + métricas LE. |
| Independencia total | 🟠 | Su utilidad actual consume snapshots de otros motores; puede existir en espera, pero todavía no ofrece un banco amplio de pruebas completamente autónomas. |

## NVIDIA

| Área | Estado | Observación |
|---|---|---|
| Código/namespace centralizado | ✅ | `NOVORA.NVIDIA`, prefijo `NLNVIDIA`. |
| NVDEC opcional/fallback | 🟡 | Código y documentación presentes; aceleración real requiere GPU y pruebas físicas. |
| Dependencia obligatoria | ✅ no | No debe impedir el funcionamiento general cuando NVIDIA no está disponible. |

## Zero Polling

La búsqueda automática produce candidatos, no culpables. Se observaron:

- `adb track-devices`: event-driven, válido.
- `WaitToReadAsync` / `TryRead`: espera/event queue, válido.
- `AcceptTcpClientAsync`/socket loops: event/readiness, válido.
- `Task.Delay` en deadlines/backoff/recovery: permitido si no se usa para descubrir cambios.
- Stress/benchmark loops: aceptables fuera del runtime normal.
- Los `Task.Delay` restantes en Control/Transport/Recovery deben revisarse durante el gate funcional para confirmar su uso exacto.

**Conclusión:** no es correcto eliminar todos los loops. El reglamento prohíbe sondeo, no event loops, read loops, deadlines ni pacing.

## Privacidad

✅ Existen PrivacyVE y políticas explícitas.  
✅ El reglamento de esta candidata documenta minimización de datos.  
🟡 Debe completarse una auditoría física/logs para demostrar que no se persisten contenidos sensibles durante sesiones reales.

## Terceros / autosustentabilidad

Todavía existen dependencias temporales:

- ADB / Android platform tools.
- scrcpy-server y `scrcpy.exe`.
- FFmpeg LGPL build.
- SDL3.
- paquetes NuGet, entre otros.

Se mantienen avisos/reconocimientos. La autosustentabilidad no justifica eliminarlos antes de que exista reemplazo funcional propio.

## Installer

✅ `.iss` actualizado a PRERELEASE PRE FINAL.  
✅ Instala por usuario.  
✅ Acceso de NOVORA en Escritorio.  
✅ Manual ES/EN copiado al Escritorio.  
⚪ Debe compilarse con Inno Setup 7 y probar instalación/desinstalación en Windows.

## Limpieza del repositorio

Se retiraron 32 árboles históricos/laboratorios de `tests/` de la candidata. Se conservaron:

- `tests/NOVORA.Tests`
- `tests/NOVORA.VisionEngine.BlockCHarness`
- `tests/NOVORA.VisionEngine.HeadlessHarness`
- `tests/NOVORA.VisionEngine.ProfileHarness`
- `tests/USB`

La lista de exclusiones está en `Documentation/Archive/NLDocumentationExcludedDevelopmentTrees.md`.

## P0 — Bloquean una Release estable

1. Recompilar `LENetworkRelay.exe` con el fuente de 1 dispositivo.
2. Ejecutar `Tool/NLToolReleaseGate.ps1` en Windows con SDK .NET/Rust.
3. Probar USB físico con `com.novora.appcontrol`.
4. Probar LAN/QR/trust/reconexión real.
5. Probar VisionEngine video/audio/input con el `scrcpy-server` distribuido.
6. Ejecutar pruebas de rendimiento, seguridad y calidad del reglamento.
7. Compilar/probar `NLInstallerSetup.iss`.

## P1 — Debe terminarse o validarse

- Determinar qué funciones de `IntegrationVE` tienen backend real y cuáles deben mostrarse como no disponibles.
- Ampliar STEngine si debe funcionar como banco de medición autónomo, no sólo como analizador de LE/VE.
- Completar revisión de persistencia/logs sensibles.
- Decidir si `scrcpy.exe` sigue siendo realmente necesario; VisionEngine afirma no ejecutarlo para el pipeline principal.

## P2 — Evolución posterior

- Sustituir gradualmente dependencias de terceros por implementaciones propias sólo después de investigación ejecutiva y pruebas.
- Automatizar más del Release Gate sin convertirlo en polling.
- Crear pruebas específicas de independencia/recovery cruzado entre motores.

## Resultado

**Esta carpeta es candidata a `main`, no Release estable todavía.**  
La estructura/documentación está preparada para rama principal, pero el reglamento exige cerrar los P0 antes de publicar estable.


## Verificación estática de esta carpeta

`NLToolVerify.py` y los gates estructurales/JSON/XML/XAML pasaron sobre la candidata. Esto no sustituye build ni pruebas físicas; consulte `NLDocumentationStaticVerification.md`.

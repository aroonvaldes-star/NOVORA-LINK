# LinkEngine Session Data Implementation Plan

**Goal:** Simplificar la pantalla y conservar únicamente datos de la sesión actual, sin históricos automáticos.

**Architecture:** Vista de conexión derivada de estados reales; almacén acotado de mediciones por sesión; servidor HTTP con contexto de prueba inmutable e invalidable. UI vinculada al runtime o gateway activo.

**Tech Stack:** C#/.NET 8, WPF, harness existente NOVORA.LinkEngine.Tests.

**Spec:** ../specs/2026-10-18-le-session-data-design.md

## Restricciones

Preservar los cambios preexistentes del checkout. Sin cambios de APK, relay Rust, publicación, instalación ni eliminación de archivos históricos. Guardado de mediciones solo por exportación manual.

## Ejecución local solicitada por el usuario

- [x] Reproducir el guardado automático mediante POST de resultado: la regresión falla porque aparece el JSON.
- [x] Agregar almacén de sesión y presentación; probar aislamiento, ceros frente a desconocidos, caducidad y reinicio de contadores.
- [x] Invalidar pruebas HTTP antiguas y retirar persistencia automática de ambas pruebas y del monitor.
- [x] Identificar conexiones DATA sucesivas del mismo gateway LAN con generaciones independientes.
- [x] Reorganizar la pantalla y añadir exportación manual del resumen actual.
- [x] Ejecutar regresiones y suite LinkEngine; comprobar compilación y diferencias de archivos.

## Revisión de riesgos

- Callback HTTP retrasado tras cambio de sesión: validar sesión y prueba vigente en el dispatcher.
- Reutilización del mismo gateway LAN: generación de conexión independiente.
- Contadores reiniciados o caducados: nueva base y tasas desconocidas, sin deltas negativos.
- Diálogo de exportación abierto durante desconexión: comprobar de nuevo la sesión antes de escribir.
- No cargar ni eliminar archivos antiguos; no confundir diagnóstico acotado del relay con histórico de mediciones.

## Evidencia

La prueba inicial de no persistencia falló con el comportamiento original. Tras la corrección pasan las regresiones de sesión y de HTTP sin persistencia en Debug y Release. La suite completa LinkEngine pasa en Debug y Release. La compilación Release de escritorio terminó con 0 advertencias y 0 errores. La comprobación de diferencias de los archivos modificados no presenta errores de espacios; una comprobación global detecta espacios finales ajenos a esta tarea en ExIn. La verificación final se registra también en E:/NOVORA/Bitacora.md. Queda pendiente comprobar la presentación y los flujos con un teléfono real.

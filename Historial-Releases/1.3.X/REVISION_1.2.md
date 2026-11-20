# NOVORA 1.2 — revisión estructural

## Problemas encontrados

1. La ventana principal tenía un `Grid.Row` de altura 0 para la barra de título.
2. La ventana estaba bloqueada con `ResizeMode="NoResize"` aunque tenía botón de maximizar.
3. El área central dependía de `Canvas` y de un sistema de widgets que no estaba realmente conectado a la UI.
4. `WidgetManager` y `MainViewModel` mantenían dos registros distintos de widgets.
5. `RebuildWidgets()` estaba vacío, por lo que las opciones de widgets no controlaban la ventana.
6. `WidgetBase.Initialize()` creaba asas de arrastre/redimensionamiento pero no las agregaba al árbol visual.
7. La configuración mostraba tipos de widgets que no estaban registrados.
8. `OutputProfileService` convertía de forma incorrecta algunas unidades de bitrate.
9. `UpdateService` todavía apuntaba al repositorio anterior.
10. La interfaz y documentación mezclaban conceptos de NOVORA 1.1 y 1.2.

## Correcciones

- La UI principal ahora usa únicamente controles WPF normales.
- Los cuatro paneles actuales se muestran/ocultan desde `MainViewModel`.
- Se eliminó el runtime de widgets duplicado de la aplicación.
- Se mantiene `WidgetConfig` para compatibilidad con configuraciones antiguas.
- Bitrate normalizado a formato Scrcpy `M`.
- `MB/s` explícito se interpreta como bytes/s y se convierte a Mbps ×8.
- El actualizador apunta a `NOVORA-LINK`.
- La actualización pide confirmación, descarga por HTTPS, verifica SHA-256 y ejecuta el instalador.
- La documentación fue alineada con el estado real de esta copia.
- Se eliminó `.vs`, `bin` y `obj` del paquete final.

## No afirmado

Esta revisión no demuestra por sí sola:

- compatibilidad universal con dispositivos Android;
- estabilidad de Wi-Fi;
- recuperación de Gnirehtet en todos los escenarios;
- funcionamiento completo del actualizador 1.2 → 1.3.

Eso requiere pruebas reales.

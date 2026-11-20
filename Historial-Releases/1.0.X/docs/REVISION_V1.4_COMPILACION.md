# Revisión V1.4 — compilación

Correcciones aplicadas tras la primera compilación en Visual Studio:

- Se evitó la ambigüedad entre `System.Windows.MessageBox` y `System.Windows.Forms.MessageBox`.
- `App` usa explícitamente `System.Windows.Application`.
- Se restauró `ScrcpyService` completo de V1.3 con optimización de monitor/FPS/resolución.
- Audio quedó integrado como opción real: activado usa el audio normal de scrcpy; desactivado agrega `--no-audio`.
- Se añadieron `using` explícitos de `System.IO`, `System.Diagnostics`, `System.Collections.Generic`, etc., para no depender del generador de `ImplicitUsings`.
- `MainWindow` pasa `AudioEnabled` al servicio de scrcpy.

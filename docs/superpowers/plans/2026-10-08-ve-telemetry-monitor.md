# Monitor de telemetría VisionEngine de 20 minutos

> **Para el equipo NOVORA:** al ejecutar este plan, mantener el monitor estrictamente de sólo lectura. No debe modificar el perfil de vídeo, bitrate, transporte ni enviar entradas al juego.

**Objetivo:** Mostrar en Windows y Android una sesión temporal de veinte minutos con la telemetría que VisionEngine ya produce: resolución, FPS, bitrate, cuadros decodificados/renderizados/perdidos y errores. Al terminar el período, el stream continúa funcionando y el estado deja claro que la observación terminó.

**Arquitectura:** Añadir una pequeña máquina de estado pura en VisionEngine que calcula inicio, vigencia y tiempo restante. Ampliar el contrato de telemetría con campos opcionales retrocompatibles. El cliente Windows alimenta el contrato desde `VECoreStatus` y `VEMetricsCollector`; ambos clientes sólo lo presentan desde su ruta de actualización existente.

**Tecnologías:** .NET/WPF, Android C#, `NLControlProtocol`, pruebas de consola `NOVORA.LinkEngine.Tests`.

---

### Tarea 1: Modelo temporal comprobable

**Archivos:**
- Crear: `src/NOVORA/VisionEngine/Metrics/VEObservationWindow.cs`
- Modificar: `tests/NOVORA.LinkEngine.Tests/Program.cs`

1. Escribir una prueba que pruebe inicio, tiempo restante y expiración exacta a 20 minutos.
2. Ejecutar la prueba para observar el fallo inicial.
3. Implementar una clase interna pura, sin temporizadores ni I/O, que reciba `DateTimeOffset` y exponga estado activo, segundos restantes y duración fija de 1200 segundos.
4. Reejecutar las pruebas.

### Tarea 2: Contrato y captura en Windows

**Archivos:**
- Modificar: `src/NOVORA/Control/NLControlProtocol.cs`
- Modificar: `src/NOVORA/UI/NLUIWindowMainAndroidEngines.cs`
- Modificar: `src/NOVORA/UI/NLUIWindowMain.xaml.cs` o el parcial que controle el ciclo VE

1. Añadir campos opcionales al final de `NLControlVisionTelemetry` para los contadores y estado de observación, conservando constructores/serialización existentes.
2. Iniciar la ventana de observación cuando haya vídeo VE activo; alimentar los contadores desde el estado VE actual y las tasas desde `VEMetricsCollector`.
3. Al cumplir 20 minutos, publicar observación terminada sin detener el stream ni alterar su configuración.
4. Mostrar en `VisionLiveTelemetryText14` un resumen legible con datos y tiempo restante/resultado.

### Tarea 3: Presentación Android

**Archivos:**
- Modificar: `src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs`

1. Consumir exclusivamente los nuevos campos recibidos por el contrato.
2. Añadir a los textos de resolución/FPS/bitrate el estado del monitor, cuadros perdidos y errores sin inventar datos cuando la muestra está vencida.
3. Mantener la indicación inequívoca de que el monitor terminó mientras el stream pueda seguir activo.

### Tarea 4: Verificación

1. Ejecutar `dotnet run --project tests/NOVORA.LinkEngine.Tests/NOVORA.LinkEngine.Tests.csproj --no-restore`.
2. Ejecutar `dotnet build src/NOVORA/NLProjectDesktop.csproj -t:Compile --no-restore`.
3. Ejecutar la compilación Android disponible sin empaquetar ni instalar en el dispositivo.
4. Revisar `git diff --check`; distinguir estas verificaciones de una validación física de streaming VE/Rainbow.

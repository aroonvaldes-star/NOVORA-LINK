# Spec: ExIn — telemetría de calibración fluida en VisionEngine

## Objective

Cuando una persona calibre un mando desde NOVORA PC mientras VisionEngine está activo o se mueve entre monitores, la visualización de entradas y de la calibración debe recibir y presentar muestras recientes con una cadencia objetivo de 60 Hz. La selección o el movimiento de monitor no puede cambiar de mando, reutilizar una calibración de otro mando ni degradar silenciosamente la telemetría de ExIn.

Este objetivo cubre la telemetría de ExIn y su presentación de estado; no afirma ni altera los FPS del vídeo capturado por VisionEngine. El rendimiento efectivo depende de la tasa de entrada del mando, de la frecuencia de actualización de la pantalla y de la ruta PC–Android, por lo que la aplicación debe conservar y exponer datos medibles, no prometer 60 fps donde el hardware no puede entregarlos.

## Tech Stack

- PC: C# / .NET 8, `ExInEngine` y `VisionEngine`.
- Android: C# para Android, `LINK Control` y sus vistas nativas.
- Contrato de estado: `NLControlExIn` y `NLControlExInTelemetry`.
- Pruebas: ejecutables .NET de `tests/NOVORA.ExInRuntime.Tests` y `tests/NOVORA.ExInSession.Tests`.

## Commands

Estado del árbol antes y después de trabajar:

```powershell
git status --short --branch
```

Prueba enfocada de ExIn, una vez que el proyecto de prueba referencia las rutas actuales:

```powershell
dotnet run --project tests/NOVORA.ExInRuntime.Tests/NOVORA.ExInRuntime.Tests.csproj -c Release -p:UseSharedCompilation=false
```

Compilación de la solución:

```powershell
dotnet build NOVORA.sln -c Release -p:UseSharedCompilation=false
```

Validación manual posterior: con un mando conectado, iniciar calibración, mover la ventana VE de un monitor a otro, observar secuencias de telemetría y comprobar que el mando activo y su perfil no cambian.

## Project Structure

```text
src/NOVORA LINK/ExInEngine/          -> Captura, perfiles, calibración y publicación ExIn en PC
src/NOVORA LINK/VisionEngine/        -> Presentación VE y selección de monitor
src/LINK Control/AndroidUI/           -> Consumo y representación nativa del estado ExIn
tests/NOVORA.ExInRuntime.Tests/       -> Pruebas fuente-enlazada de Manager, calibración y salida
tests/NOVORA.ExInSession.Tests/       -> Pruebas de sesión y perfil de ExIn
```

## Code Style

Mantener nombres con el sufijo `VE`, estado inmutable y sincronización explícita para datos compartidos. Una actualización sólo debe publicar una muestra nueva y comprobable; no debe reutilizar una muestra vieja para aparentar fluidez.

```csharp
if (next.Sequence <= previous.Sequence) return;
PublishLiveSnapshotVE(next);
```

Las pruebas deben expresar el comportamiento visible: secuencia creciente, identidad de mando intacta y perfil correcto; no deben depender de detalles de bloqueo internos.

## Testing Strategy

- Añadir primero pruebas de regresión que fallen con la pérdida de cadencia o con el cambio indebido de perfil.
- Usar una fuente de reloj o datos controlada para probar el límite de publicación de 60 Hz sin esperas temporales frágiles.
- Probar que un cambio de monitor de VE no modifica la identidad del mando ni la clave de perfil de ExIn.
- Ejecutar la suite enfocada y la compilación de solución después de cada incremento.
- Hacer una prueba física separada con el mando, dos monitores y VE: el resultado debe registrar frecuencia de pantalla, tasa de entrada y cadencia observada, sin convertir una compilación en evidencia de 60 fps físicos.

## Boundaries

- **Always:** conservar la calibración por `ProfileKey`, publicar sólo muestras ordenadas y proteger la salida neutral durante calibración.
- **Ask first:** cambiar el contrato PC–Android, el formato persistido de perfiles, el límite de FPS configurado de vídeo o dependencias/CI.
- **Never:** borrar, mover o adoptar automáticamente el perfil de otro mando del mismo modelo; atribuir 60 fps de vídeo a la telemetría ExIn; instalar software en el teléfono; hacer `commit` o `push` sin una orden explícita.

## Success Criteria

- Durante calibración, la telemetría de ExIn conserva una secuencia estrictamente creciente y se publica con un máximo objetivo de 60 Hz, sin bajar por el cambio de monitor de VE.
- La interfaz descarta muestras duplicadas o fuera de orden y programa el repintado con el sincronizador de fotogramas de la plataforma.
- Mover VE entre monitores no cambia el `InstanceId`, el `ProfileKey` ni los valores de calibración del mando activo.
- Ningún mando desconectado pierde su perfil ni transfiere automáticamente su calibración o sus mapeos a otro mando del mismo modelo.
- Las nuevas pruebas de regresión, las pruebas ExIn existentes que sean aplicables y la compilación de la solución concluyen correctamente; cualquier fallo previo por rutas antiguas se informa de forma separada.

## Open Questions

- ¿La pantalla que muestra los movimientos a 60 fps es la ventana VE en Windows, la vista de LINK Control en Android, o ambas? Esta especificación asume ambas rutas de visualización cuando estén activas.
- ¿Debe mostrarse una métrica diagnóstica de Hz observados al usuario, o basta con registrarla para soporte técnico?

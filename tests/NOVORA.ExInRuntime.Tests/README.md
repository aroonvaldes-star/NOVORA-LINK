# ExIn: runtime y recuperación

Esta suite ejecuta el código de producción de Manager, Core, router, generaciones, reportes, calibración y traductor de puntero mediante enlaces de fuente. Sustituye únicamente las fronteras nativa SDL y ubicación del almacén de perfiles. La salida controlada permite bloquear una escritura/creación, provocar un fallo y observar los reportes UHID realmente generados.

```powershell
dotnet run --project tests/NOVORA.ExInRuntime.Tests/NOVORA.ExInRuntime.Tests.csproj -c Release -p:UseSharedCompilation=false
```

Un argumento después de `--` filtra los nombres de casos, por ejemplo `-- attachment`. Sin filtro se ejecuta toda la suite. No referencia ni construye Desktop, no carga SDL3 y no ejecuta NOVORA. Los plazos de las aserciones son watchdogs; no son mediciones de latencia física.

La evidencia del 14 de octubre está en `Laboratorio/Control/Correccion-Optimizador-ExIn-20261014/ExIn`: RED contra las fuentes iniciales, regresiones de cierre y GREEN final. Las propiedades MSBuild `ExInManagerSource`, `ExInCoreSource` y `ExInGenerationSource` permiten ejecutar la misma suite sobre una copia de las fuentes anteriores sin revertir el producto.

La suite confirma estados y orden de reportes dentro del proceso. Las pruebas con PC, SDL físico, ADB, Android UHID, juego, USB/LAN y la concurrencia real con LE/VE siguen siendo una capa de validación distinta.

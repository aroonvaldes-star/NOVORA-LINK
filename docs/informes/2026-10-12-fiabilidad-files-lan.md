# Fiabilidad de FILES LAN y verificación de NOVORA LINK

Fecha: 2026-10-12. Checkout observado: `E:\NOVORA\NOVORA LINK\GITHUB\NL`, rama `NOVORA-LINK`, HEAD `5afbabc003fbbffac7aa50fee29a452eeb6a5e22`, con numerosos cambios preexistentes.

Resultado: las correcciones de esta tarea pasan la verificación local de código. La fiabilidad física PC-Android sigue sin validar: ADB no detectó ningún dispositivo durante esta sesión.

## Fallos reproducidos y correcciones

| Fallo observado antes de corregir | Cambio | Evidencia final |
| --- | --- | --- |
| `Verify-NOVORA-Repository.ps1` termina con `Falta archivo requerido: ...\src\NOVORA\NOVORA.csproj`. | Usa `NLProjectDesktop.csproj`, `NLProjectAndroid.csproj`, NovoraRelay y las tres suites actuales. Comprueba el enlace de Android al protocolo compartido. | El script completo termina con código 0. |
| Cancelar un envío FILES no interrumpe una espera de respuesta: la regresión termina con `Cancelar FILES LAN debe interrumpir la espera aunque Android no responda.` | Propaga el token a escritura, lectura y flush de las tramas. | Cancelación observada sobre una conexión TCP loopback real; el envío termina cancelado y el receptor observa cierre. |
| Cerrar el emisor deja una transferencia esperando una respuesta: `Cerrar FILES LAN debe interrumpir la transferencia antes de liberar sus recursos.` | El cierre cancela el envío activo y los envíos en cola, espera la liberación del semáforo y después libera sus recursos. | La regresión verifica el envío activo, otro envío en cola y la finalización del cierre. |
| Un receptor silencioso bloquea el envío sin cancelación explícita: `FILES LAN debe limitar la espera incluso sin cancelación del usuario.` | Cada solicitud tiene un plazo predeterminado de 30 segundos. El vencimiento cierra FILES y produce `TimeoutException`. | Prueba con un plazo de 200 ms y un receptor que recibe `file.begin` pero no contesta. |

Los cambios del emisor están en `src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileSender.cs`: constructores, `SendPathsAsync`, `SendFileAsync`, `SendRequestAsync` y `DisposeAsync`. Las cuatro pruebas nuevas y su utilidad de acceso al emisor interno están en `tests/NOVORA.LinkEngine.Tests/Program.cs`.

Cerrar FILES tras una cancelación o un timeout impide reutilizar una trama parcial o una respuesta atrasada. El cierre se aplica al stream FILES. Un envío posterior necesita un canal FILES nuevo, preparado al reiniciar VisionEngine LAN. La limpieza del archivo parcial en Android depende del cierre observado por su receptor; no se probó esa limpieza con almacenamiento SAF físico.

## Pruebas y compilación

Comando integrado ejecutado desde el checkout:

```powershell
.\scripts\Verify-NOVORA-Repository.ps1 -NoRestore -OutputRoot .\artifacts\reliability-20261012\integrated
```

`-NoRestore` usa los assets NuGet ya restaurados. Cargo usa `--offline --locked`, por lo que sus dependencias deben estar en la caché local. `OutputRoot` permite separar los resultados de las compilaciones existentes; si se omite, el script crea una ruta bajo `artifacts/verification` con fecha y hora.

| Comprobación ejecutada | Resultado | Alcance demostrado |
| --- | --- | --- |
| Parseo PowerShell del script | PASS | Sintaxis válida. |
| `cargo check --all-targets --offline --locked` | Código 0 | Compilación de los targets del relay actual. |
| `cargo test --release --offline --locked` | 3 pasadas, 0 fallidas, 0 ignoradas | Mapeo exclusivo de SpeedTest, sustitución de conexión DATA bloqueada y rechazo de IPv4 inválido sin detener el listener. |
| `cargo build --release --offline --locked` | Código 0 | Binario de NovoraRelay compilado en su directorio `target`. |
| Build Windows Release | Código 0; 0 errores, 0 advertencias | Código Windows compilado en la salida separada. |
| `NOVORA.LinkEngine.Tests` Release, ejecutado con `dotnet run` | PASS | Contratos existentes y cuatro pruebas nuevas de FILES. |
| `NOVORA.ExInHid.Tests` Release, ejecutado con `dotnet run` | PASS, 512 muestras | Serialización HID y límites existentes; no transferencia de entradas de un mando físico. |
| `NOVORA.DynamicAddress.Tests` Release, ejecutado con `dotnet run` | PASS | Identidad LAN y destino ADB variable mediante su harness existente. |
| Android Release, target `Compile` | Código 0; 0 errores, 0 advertencias | Código Android compilado; no empaquetado ni instalación. |

La prueba positiva FILES usa el emisor de producción y `NLControlFileReceiver` sobre TCP loopback. Envía dos archivos por el mismo canal: uno de 60 017 bytes, repartido en varios bloques, y otro vacío. Comprueba todos los bytes recibidos, ambos archivos confirmados y la ausencia de archivos `.partial` después del commit con SHA-256. Este backend es el receptor de archivos .NET; no sustituye la prueba del receptor Android SAF ni la de FILES sobre TLS en un teléfono.

El script ejecuta las suites de consola con `dotnet run`; `dotnet test` no ejecuta estos harnesses. Un fallo de una suite mantiene el código de salida fallido y detiene la verificación.

## Artefactos y conservación del trabajo

- `artifacts/reliability-20261012/verification.log`: salida de la verificación integrada final.
- `artifacts/reliability-20261012/baseline`: copias de los tres archivos antes de esta tarea y estado Git observado.
- `artifacts/reliability-20261012/LinkEngine`, `ExIn`, `DynamicAddress` e `integrated`: compilaciones de verificación local.
- El estado inicial incluía archivos modificados, eliminados y sin seguimiento. Los cambios de esta tarea se compararon con las copias iniciales, no con un diff global que atribuyera trabajo anterior a esta sesión.

## Límites y comprobaciones pendientes

La validación física requiere un teléfono conectado y pruebas independientes de USB y LAN: autorización, permisos, video, audio, Internet mediante LinkEngine, entrada ExIn, intercambio de archivos, desconexión y reconexión. También quedan pendientes dispositivos y versiones Android representativos, cargas prolongadas y mediciones reales de rendimiento.

La fuente Android observada declara `ApplicationVersion=99` y `ApplicationDisplayVersion=1.4.99`. Esa lectura no demuestra una versión instalada. Esta tarea modifica código Windows y su verificación; no prepara una entrega nueva ni demuestra funcionamiento de los motores en un dispositivo.

Revisión independiente terminada: no encontró defectos materiales en los tres cambios comparados con sus snapshots. Revisó también el propietario UI y el protocolo de archivos. Fue una revisión estática; las ejecuciones y compilaciones corresponden al registro integrado de esta tarea. No certifica cancelación de enumeraciones síncronas, cierres simultáneos desde distintos hilos, recuperación de FILES sin una nueva oferta ni una sesión física PC-Android.

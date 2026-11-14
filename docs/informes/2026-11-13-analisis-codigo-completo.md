# Análisis de código completo — 2026-11-13

## Alcance, método y límites

Revisión de `Dev_Local` (HEAD `8310fc1`, árbol con WIP sin confirmar en `src/LINUX`)
sobre el paquete de skills `addyosmani/agent-skills` instalado en
`~/.config/opencode/skills/`: eje principal `code-review-and-quality` (cinco ejes:
correctitud, legibilidad, arquitectura, seguridad, rendimiento), lente
`security-and-hardening` para las fronteras USB/LAN, `performance-optimization` y
`code-simplification` como criterio de remedios.

Cada módulo se revisó en una pasada independiente con instrucción de evidencia
estricta (`archivo:línea` leído, sin especulación sin etiquetar). Los hallazgos
marcados **«verificado en esta sesión»** los comprobé yo mismo leyendo el código o
ejecutando la comprobación; el resto procede de la revisión de área y conserva la
etiqueta «probable» cuando su autor no pudo confirmarlo.

**No se modificó código de producto.** Las afirmaciones de compilación y pruebas de
este informe son ejecuciones locales de esta sesión, no de Windows ni de Android
físico.

Áreas: 1) LinkEngine Windows · 2) ExIn/Control/Service Windows · 3) Relay nativo Rust
· 4) UI WPF + prototipo React · 5) VisionEngine (en curso) · 6) Android + Linux (en
curso) · 7) Transversal: build, CI, pruebas y dependencias (en curso).

---

## Resumen ejecutivo

| # | Severidad | Área | Hallazgo | Evidencia |
|---|---|---|---|---|
| 1 | Alta | Actualizador | El instalador se descarga en `%TEMP%\NOVORA\Updates`, se hashea y luego se ejecuta desde PowerShell **sin re-verificar** hash ni firma Authenticode (no existe ninguna comprobación Authenticode en el repo) | `NLServiceUpdate.cs:255-282, 484, 636-642` · verificado |
| 2 | Alta | ADB | `RunAsync` no tiene deadline propio y **no mata el hijo** al cancelar; un `adb shell dumpsys` colgado bloquea todo el layer bajo `_deviceGate` y deja `adb.exe` huérfano | `NLServiceADB.cs:1507-1544` vs `1480-1505` · verificado |
| 3 | Alta | ExIn | Bucle de recuperación infinito: `if (attempt < 3) attempt++` satura en 3 y `while (attempt < MaxRetriesVE)` nunca sale; el mensaje «Abandonado después de 10 intentos» es código inalcanzable | `ExInCoreEngine.cs:226-234, 268` · verificado |
| 4 | Alta | LinkEngine | El monitor de recuperación publica `Failed` y vuelve a esperar señal sin re-armarse: sin un evento posterior de transporte, la recuperación queda muerta | `LERecoveryMonitor.cs:257-264, 326-345` · verificado |
| 5 | Alta | LinkEngine | `SetSessionLE` invoca `StatusChangedLE` **dentro** de `lock (_sessionGate)` y hay suscriptores que ejecutan UI síncrona (escrituras WPF + captura de snapshot) | `LERuntimeManager.cs:1021-1045` → `NLUIWindowMainLinkEngine.cs:63-69, 171` · reportado |
| 6 | Alta | LinkEngine | Un `adb` lento/busy convierte cualquier fallo de verificación en `State = Failed` (terminal para los consumidores) aunque el listener siga sano; el accept loop puede volver a escribir `Connected` encima → dos escritores contradictorios | `LETransportManager.cs:595-641, 698-717, 1350-1366` · reportado |
| 7 | Alta | Relay Rust | Un solo paquete mal formado del peer aborta `serve()` completo (`enqueue(...)?`) y derriba todas las sesiones; un fallo transitorio de `accept` mata el proceso entero | `packet_queue.rs:34`, `main.rs:284-293`, `main.rs:150-152` · verificado |
| 8 | Alta | Relay Rust | Con EOF de stdin el relay **sigue enrutando** con la última política aceptada, indefinidamente, con el puerto y el token tomados | `main.rs:102-129` · verificado |
| 9 | Alta | WPF | Handlers de estado con `Dispatcher.Invoke` bloqueante y sin guarda `_closing`; en teardown lanzan en el hilo productor | `NLUIWindowMain.xaml.cs:1186-1245` · reportado |
| 10 | Alta | WPF | La secuencia de cierre no tiene timeout ni salida de fuerza; un paso colgado deja la ventana imposible de cerrar | `NLUIWindowMain.xaml.cs:150-194, 241` · reportado |
| 11 | Alta | React | `showToast()` (efecto secundario) se llama **dentro** de updaters de `setState`; con `StrictMode` cada toggle dispara doble toast y los timers quedan huérfanos y pisando toasts nuevos | `App.tsx:45-76`, `main.tsx:7` · reportado |
| 12 | Alta | Privacidad | La clave RTMP viaja en la línea de comandos de `ffmpeg` (legible por cualquier proceso local vía `Win32_Process.CommandLine`) mientras un comentario afirma que nunca sale del proceso | `NLStreamingService.cs:79-90` · reportado |

**Lectura de conjunto.** El diseño de protocolo es la parte fuerte: TLS fijado a
huella, tokens de sesión con comparación en tiempo constante, límites explícitos en
el framing, `ArgumentList` en lugar de concatenación de argv, y rechazo activo de
scrcpy/gnirehtet en los scripts de empaquetado. Los defectos se concentran en tres
patrones, no en 12 sitios sueltos:

1. **Verificación → ejecución sin cadena sellada** (actualizador, `adb.exe`, relay).
2. **Un fallo recuperable escala a caída total** (paquete mal formado, `accept`,
   excepción en la ruta de entrada de ExIn, `Failed` por `adb` lento).
3. **Estado duplicado y publicación bajo lock** (predicado «enlace sano» repetido 5
   veces, evento con `_sessionGate` tomado, dos copias idénticas del relay).

---

## 1. LinkEngine (Windows)

### Resumen

Capas: `LECoreEngine` compone `LEDeviceManager` (estado ADB), `LETransportManager`
(CONTROL), `LENetworkManager` (DATA + relay) y `LERecoveryManager`/`LERecoveryMonitor`
(reparación); publican registros inmutables `*Session` vía `ConcurrentDictionary` +
eventos y `LERuntimeManager` los proyecta en un solo `LERuntimeSession` para la UI.

CONTROL enlaza un `TcpListener` en loopback, crea `adb reverse tcp:27183`, verifica
desde `adb reverse --list`, autentica un HELLO con token de 64 hex (comparación en
tiempo constante) y corre HEARTBEAT/ACK cada 2 s con vencimiento a 5 s. DATA lanza
`LENetworkRelay.exe`, espera `LE_READY` por stdout y configura QoS por `LE_CONFIG` en
stdin con `LE_CONFIG_ACK`. La recuperación es dirigida por eventos y confirma fallos
con plazos de 3–5 s antes de reparar.

### Hallazgos (revisión de área + comprobaciones propias)

- **[Alta, correctitud] `LERecoveryMonitor.cs:257-264` — el monitor no se re-arma.**
  El único `catch (Exception)` publica `Failed` y vuelve al `await _signalLE.WaitAsync`;
  si no llega otro evento de transporte (el caso normal con el teléfono ausente) nada
  vuelve a señalizar. El mismo final en `318-345`: `WaitForDeviceReturnLEAsync` devuelve
  `false` → publica `Failed` y retorna sin re-armar. **Verificado:** el código no tiene
  temporizador de respaldo ni re-señal tras el fallo. Remedio: convertir el worker en
  bucle con backoff acotado (`await Task.Delay(backoff, ct)` + `SignalMonitorLE()`
  usando `LERecoveryPolicy.GetReconnectDelayLE`, hoy sin uso) y devolver `false` en vez
  de desenrollar.
- **[Alta, correctitud] `LERuntimeManager.cs:1021-1045` — evento bajo lock.** El
  comentario afirma que el suscriptor «no puede bloquear en la puerta», pero es una
  suposición sin verificar: `NLUIWindowMainLinkEngine.cs:63-69` aplica la sesión
  directamente cuando `Dispatcher.CheckAccess()` y encadena `AndroidEngineStateChanged()`
  → `NLUIWindowMainAndroidEngines.cs:146-161`. Remedio: dentro del lock solo intercambiar
  `_session` y sacar la instantánea; publicar fuera del lock por un canal acotado
  (`Channel<LERuntimeSession>` con `DropOldest`) consumido en el dispatcher.
- **[Alta, correctitud] `LETransportManager.cs:595-641` — `Failed` por `adb` lento.**
  Cualquier excepción de `adb reverse --list` (incluido el `catch` genérico `698-717`)
  marca `State = Failed` con el listener aún vivo; `LERuntimeManager.cs:1277-1282` aborta
  `WaitForHealthyLEAsync` y `LERecoveryMonitor.cs:352-356` lo consulta en cada
  evaluación. Mientras tanto el accept loop puede escribir `Connected` (`1350-1366`).
  Remedio: resultado explícito `Degraded/VerificationFailed` (solo `ReverseVerified=false`
  + `LastError`), reservar `Failed` a violaciones del protocolo.
- **[Media, seguridad] `LETransportHandshake.cs:102-110` — `ClientId` sin sanear.** Se
  acepta hasta ~1010 bytes UTF-8 arbitrarios (saltos de línea, controles, ANSI) y se
  renderiza en texto WPF/`LastError` (`NLUIWindowMainLinkEngine.cs:154-162`). El peer ya
  está autenticado por token, así que es suplantación/inyección de log, no RCE.
  Remedio: `^[A-Za-z0-9._-]{1,64}$` y errores por código, sin interpolar bytes del peer.
- **[Media, correctitud] `LETransportManager.cs:2037-2043` — semáforos liberados en
  `DisposeAsync`.** Mismo patrón en `LERecoveryManager.cs:750-756`, `LERuntimeManager.cs:1428`
  y `LECoreEngine.cs:496-498`: el perdedor de la carrera recibe `ObjectDisposedException`
  en `Release()`. Remedio: no liberar semáforos; señalar con `CancellationTokenSource` y
  dejar los primitivos al GC.
- **[Media, correctitud] `LEDeviceResolver.cs:229-259, 420-441` — sondeo degradado a
  caché permanente.** Un timeout de adb devuelve `""` → `h264/h265/opus = false` →
  `Partial`, y se persiste por modelo/compilación sin TTL (`LEDeviceHistory.cs:78-231`).
  Remedio: distinguir `Unavailable` de `Empty`, no guardar cuando el sondeo falló y añadir
  `CapturedAtUtc` + caducidad.
- **[Media, rendimiento] `LENetworkRelay.cs:700-729, 748-756` — sondeo de procesos y
  esperas bloqueantes dentro de `_gate`.** Recorre todos los procesos leyendo `MainModule`
  y bloquea `WaitForExit(1500/2000)` en la ruta de arranque (invocada desde la ventana):
  2–4 s de UI congelada por reinicio y tiempo comido al presupuesto de 5 s de `LE_READY`.
  Remedio: `WaitForExitAsync` con plazo, sacar el barrido de la ruta de arranque.
- **[Media, arquitectura] `LENetworkRelay.cs:18-19` — el puerto 27184 está redeclarado.**
  **Verificado:** el literal `27184` aparece además en `LETransportData.cs:14-18`,
  `LETransportPort.cs:56-57`, `LEFailoverADB.cs:81`, `NovoraRelay/src/main.rs:31`,
  `NLAndroidVpnService.cs:281`, `NLControlLanDataGateway.cs:36`, `LinuxLanLinkGateway.cs:51`,
  `LinuxLanLinkEngine.cs:15`, `LinuxLinkEngine.cs:13`, `LinkRelay/src/main.rs:31` y en
  textos de UI. El parseo de `adb reverse --list` está implementado tres veces y
  `LEFailoverADB.cs:448-460` usa un `Contains` que da por bueno un mapeo a otro puerto
  host. Remedio: una clase `LEPorts`/constantes generadas para el Rust y un único
  `LEReverseMapping.TryParse`.
- **[Media, arquitectura] `LERuntimeManager.cs:696-719` — predicado de salud repetido.**
  Cinco copias (`LERuntimeManager.cs:524-536/960-972`, `LERecoveryManager.cs:145-153/631-647`,
  `LERecoveryMonitor.cs:408-414`): añadir un campo a una sola deja las otras cuatro
  informando `Running` con el plano de datos muerto. Además `LECoreEngine.ConnectAsync`
  (`LECoreEngine.cs:189-330`) es un segundo ciclo de vida que nunca llama a `VerifyAsync`,
  y `_runtimeCts` (`LERuntimeManager.cs:191-192`, cancelado en `1132-1149`) no se pasa a
  ninguna operación. Remedio: un `LELinkHealth.Evaluate(device, transport, network, recovery)`
  puro; borrar `ConnectAsync/DisconnectAsync` o delegarlos; borrar `_runtimeCts`.
- **[Media, correctitud] `LENetworkRelay.cs:387-403` — `ExitedLE` sin `try/catch`.** Es el
  único publicador del módulo sin guarda; una excepción del suscriptor en un hilo del
  pool termina el proceso justo cuando la red ya está rota. Remedio: envolver siguiendo la
  convención del módulo y saltar a `Task.Run`.
- **[Baja, seguridad] `LETransportManager.cs:168-169, 1867-1903` — token CONTROL persistente.**
  `CloseInternalLEAsync` conserva `_controlTokens[serial]`, así que el token sobrevive al
  cierre y se reutiliza en el siguiente `OpenAsync`. Remedio: borrarlo al cerrar y emitir
  token nuevo por sesión.
- **[Baja/Nit, legibilidad] Clases por encima del umbral (~800 líneas) con
  responsabilidades mezcladas:** `LETransportManager` (2052), `LERuntimeManager` (1431),
  `LENetworkRelay` (887, formateado a un token por línea). **Verificado:** `LEFailoverADB`
  (562 líneas) no tiene llamadores fuera de sí mismo (`grep "new LEFailoverADB"` → solo su
  propio archivo) y `LERecoveryPolicy` (Windows) solo lo usa el árbol Linux: son peso muerto
  en el módulo. `InjectClientDisconnectLEAsync` (`802-860`) es API de inyección de fallos
  embarcada en producción. Remedio: partir en autenticador / accept loop / coordinador de
  reverse; borrar lo no usado y poner la inyección tras bandera de diagnóstico.

### Remedios estructurales

1. **Un dueño de estado por capa con un protocolo de escritura único.** Un gate
   serializador alrededor de `_sessions` (sustituir las escrituras directas `841-848`,
   `1872-1877`, `1940-1949` por el CAS `UpdateSessionLE`) y `Failed` reservado a
   violaciones de protocolo.
2. **Módulo puro de salud/proyección + canal de diagnóstico.** `LELinkHealth.Evaluate`
   elimina cinco predicados divergentes; publicar por `Channel` acotado elimina el evento
   bajo lock.
3. **Una sola fuente del contrato C#/Rust.** `LEPorts`/`LEProtocolConstants` generados
   hacia `NovoraRelay/src` + un único parser de `adb reverse`.

---

## 2. ExIn / Control / Service (Windows)

### Resumen

La capa Control es lo más sólido del árbol: TLS 1.2/1.3 con **pinning SHA-256** en lugar
de tokens portadores, `FixedTimeEquals`, IDs monotónicos (anti-replay), destinos de
archivo confinados con rechazo de puntos de reparse y validación explícita de crédito y
framing. La capa ADB es *argv-safe* (`ArgumentList`, sin concatenación en la línea de
comandos del host) y el escapado POSIX de shell en dispositivo (`'\''`) es correcto: **no
se encontró inyección de comandos** de host ni de dispositivo en el alcance.

El eslabón débil no es el protocolo sino la **entrega al instalador** y los **binarios de
herramientas**; y el ciclo de vida de procesos es donde la correctitud flaquea.

### Hallazgos

- **[Alta, seguridad] `NLServiceUpdate.cs:255-282, 484, 573-588, 636-642` — TOCTOU del
  instalador. Verificado:** el directorio es `Path.GetTempPath()/NOVORA/Updates` (`257`,
  `498`), se verifica SHA-256 (`854`) y después `Environment.Exit(0)` (`484`); el helper
  PowerShell espera hasta 60 s a que el PID desaparezca y hace `Start-Process -FilePath
  $installer` (`636-642`) **sin re-hashear**. **Verificado: no existe ninguna comprobación
  Authenticode en `src/`** (`grep Authenticode|WinVerifyTrust|GetAllCertificates` → 0
  resultados). Cualquier proceso del mismo usuario puede sustituir el `.exe` verificado
  durante la ventana y ejecutarlo con el privilegio de NOVORA. Remedio: (a) directorio con
  ACL restringida (p. ej. `%ProgramData%\NOVORA\Updates`), (b) mantener el archivo abierto
  con `FileShare.None` y re-hashear **dentro** del helper antes de `Start-Process`,
  (c) exigir firma Authenticode del publicador.
- **[Alta, correctitud] `NLServiceADB.cs:1507-1544` — `RunAsync` sin deadline ni kill.
  Verificado:** el vecino `ExecuteAndroidInstallerCommandAsync` (`1480-1505`) sí mata el
  hijo y acota el reaper a 5 s; `RunAsync` solo hace `await process.WaitForExitAsync(token)`.
  Como `GetDevicesAsync` mantiene `_deviceGate` durante todas las lecturas por dispositivo
  (`52-96`), un `dumpsys` colgado bloquea el layer completo indefinidamente y deja el
  cliente `adb.exe` huérfano. Remedio: `CancelAfter` por comando (p. ej. 20 s; 120 s para
  `push/pull/install`), un único `ExecuteAdbAsync` que mata el árbol y libera los gates
  antes de esperar la salida.
- **[Alta, correctitud] `ExInCoreEngine.cs:226-234, 268` — recuperación sin salida.
  Verificado:** `if (attempt < 3) attempt++;` dentro de `while (attempt < MaxRetriesVE)`
  deja `attempt` clavado en 3 → el bucle no termina, el backoff de 15 s se repite para
  siempre y `LogError("Abandonado después de 10 intentos")` (`268`) es inalcanzable. Cada
  iteración hace `StopAsync()` + `StartAsync()` del manager (re-init SDL y UHID): tormenta
  de reinicios ilimitada que compite por `_lifecycle` con las llamadas de UI. Remedio:
  incremento incondicional, tope de intentos **y** de tiempo total, estado `Failed`
  terminal visible, y borrar el comentario engañoso.
- **[Media, seguridad] `NLServiceNovoraPaths.cs:13-16, 54-65` con `NLServiceADB.cs:1438-1448`
  — `adb.exe` sin verificación de integridad.** Se lanza por ruta absoluta desde
  `<AppDir>\Tools` y `ValidateAdbTools` solo comprueba `File.Exists`; el propio actualizador
  documenta que el producto se instala en `%LOCALAPPDATA%\Programs\NOVORA`
  (`NLServiceUpdate.cs:689-694`). Si ese directorio es escribible por el usuario, un
  `adb.exe` troyanizado se ejecuta en la siguiente consulta (probable: depende del alcance
  real del instalador). Remedio: fijar SHA-256 de los binarios firmados y verificar antes
  del primer spawn; rechazar ejecutar desde directorios con escritura para no administradores.
- **[Media, correctitud] `ExInManager.cs:1216-1221` — back-pressure que derriba el motor.**
  Al llegar a `MaxPendingButtonEdgesVE` (256) se **lanza** `InvalidOperationException` desde
  la ruta de entrada; propaga a `RunEventsVE` (`970-990`) y `FailWorkerVE` cancela todo el
  motor: un mando con salida lenta tumba la entrada de *todos*. Remedio: descartar el borde
  más viejo o coalescer a un flag de último estado y exponer un contador; nunca lanzar desde
  la ruta de entrada.
- **[Media, correctitud] `NLServiceSettings.cs:139-172` — `Load()` que borra ajustes.**
  Cualquier fallo se traga y devuelve un objeto por defecto; el siguiente `Save()` (que se
  invoca en rutas de lectura-modificación-escritura como `SetDeviceName`, recargando el
  archivo cada vez) **sobrescribe el original** con los valores por defecto: pérdida
  permanente de nombres de dispositivo/monitor, serial elegido, tema y sensibilidad. Sin
  tope de tamaño en `File.ReadAllText`. El propio repo ya conoce el patrón correcto
  (`NLControlTrustStore.cs:30-39` rechaza rotar ante corrupción). Remedio: distinguir
  «ausente» de «ilegible/corrupto», una sola instancia cacheada con semáforo, tope de
  tamaño y `.bak` antes de la primera reescritura.
- **[Media, rendimiento] `NLServiceADB.cs:786-817, 875, 916, 931-941, 1125, 1320, 1362-1366`
  — ~8-10 procesos por refresco y comandos duplicados.** `dumpsys display` se ejecuta dos
  veces por refresco (`916` y `1125`), igual que `wm size` (`875` y `1320`) y
  `settings get system peak_refresh_rate` (`931` y `1362`), serializados bajo `_deviceGate`
  con caché de 2 s. Remedio: un `NLServiceAdbDeviceProbe` que capture una vez y entregue las
  cadenas crudas a los parsers existentes.
- **[Media, rendimiento] `ExInManager.cs:333-355, 362-381` — persistencia con `_gateVE`
  tomado.** `FinishCalibrationVE`/`ResetCalibrationVE` guardan/borran perfiles bajo el mismo
  lock del bucle de salida de 8 ms (`899-927`) y de `LiveSnapshotVE`: un disco lento o un
  escaneo de Defender congelan la entrada de todos los mandos. `TrySetPaddleMappingVE`
  (`222-238`) ya demuestra el patrón correcto (decidir bajo lock, escribir fuera).
- **[Media, correctitud] `NLControlFileTransferV2.cs:271-281` — lector huérfano.** En el
  timeout se cancela `wait` y se relanza **sin esperar ni observar `read`**, dejando un
  `ReadAsync` vivo sobre un canal declarado `SingleReader = true`: si la cancelación pierde
  la carrera, el lector huérfano consume y descarta un frame y la siguiente lectura espera
  hasta `ResponseTimeout`. Remedio: un único lector de larga vida por emisor, o esperar `read`
  con `catch (OperationCanceledException)` antes de relanzar.
- **[Media, legibilidad] `NLControlTrustServer.cs:154-158, 249-250` — `catch` vacío que
  mata sesiones autorizadas.** Se traga toda excepción no fatal con cuerpo vacío: un
  `NullReferenceException` del `_handler` (despacho de cualquier acción del peer) cierra la
  sesión sin log, sin `StatusChanged`, y el cliente solo ve «desconectado». `ex` se captura
  y se descarta; `registered` se fija en `true` (`218`) y nunca cambia. Remedio: registrar
  en Warning con nombre de acción e id, restringir el filtro a excepciones de transporte.
- **[Baja, legibilidad] `ExInProfileStore.cs:17-20` con `ExInManager.cs:1037-1045` — invariante
  prometida y no cumplida.** `LoadVE(profileKey, keysInUse)` empieza con `_ = keysInUse;`:
  el conjunto de exclusión calculado bajo lock existe solo para satisfacer el comentario
  («el perfil de un mando conectado nunca debe adoptarlo otro»), así que dos mandos que
  reporten la misma clave comparten calibración en silencio. Remedio: implementar la
  comprobación o borrar el parámetro y el comentario.
- **[Baja, arquitectura] `NLServiceConsoleCommand.cs:29-106` — passthrough de `adb shell`
  sin allowlist.** El comentario promete «consola limitada al ecosistema NOVORA/Android»;
  el archivo no tiene referencias fuera de sí mismo (código muerto hoy) pero es superficie
  de shell arbitraria en dispositivo en cuanto se conecte a UI o control. Remedio: borrarlo
  o ponerlo tras `--allow-raw-adb` con allowlist. Además `ExInCoreEngine.cs` y
  `ExInOutputGeneration.cs` conservan marcadores de edición (`// ⭐ NUEVO`, `// ⭐ FIJO`,
  banners `// ====`) que describen historia de edición en vez de comportamiento.

### Remedios estructurales

1. **Un kernel de ejecución ADB y una sonda de dispositivo.** Partir `NLServiceADB` (1579
   líneas) en `AdbProcessRunner` (único dueño del ciclo de vida del hijo y de la política de
   timeout/kill), `AdbServerControl` y `AdbDeviceProbe`, colapsando el fan-out de ~10
   llamadas por refresco.
2. **Convertir la verificación en una entrega sellada.** Un componente que posea el
   artefacto desde la verificación hasta el spawn: directorio con ACL restringida, handle
   con `FileShare.None`, re-hash, Authenticode y exec; el mismo componente dueño de la
   integridad de `Tools\`.
3. **Partir `ExInManager` (1727 líneas) en cuatro colaboradores** detrás de la superficie
   pública actual: `ExInSlotRegistry`, `ExInInputPipeline` (sin lanzar, con drop-oldest),
   `ExInOutputScheduler` (bucle de 8 ms, generaciones, rollback) y `ExInCalibrationService`
   (persistencia fuera de todo lock). Elimina de un movimiento el I/O bajo lock y la
   escalada de excepción de la ruta de entrada.

---

## 3. Relay nativo Rust (NovoraRelay / LinkRelay)

### Resumen

**Verificado en esta sesión:** `diff -r` entre
`src/WINDOWS/LinkEngine/Network/Native/NovoraRelay/src` y `src/LINUX/LinkRelay/src` no
produce salida (**idénticos byte a byte**, ~3.086 líneas duplicadas). **Cero `unsafe`** en
el crate (`grep unsafe` → 0): el riesgo no es memoria insegura sino **disponibilidad**.
`cargo test --offline` ejecutado por mí: **29 aprobadas, 0 fallidas** (0.31 s,
`CARGO_TARGET_DIR=/tmp/opencode/relay-target`). `cargo clippy` no está instalado en este
equipo (no se ejecutó).

El limitador de tasa es correcto (i128 saturante, reloj monotónico del llamador, crédito
ocioso acotado a `burst`); aviso: el shaping es no-op salvo `mode ∈ {Manual, Adaptive}`, así
que la configuración por defecto `Observe` es un proxy sin modelar — a confirmar si es
intencional para un helper privilegiado.

### Hallazgos

- **[Alta, correctitud/disponibilidad] Paquete mal formado = caída total. Verificado:**
  `packet_queue.rs:34` (`FlowKey::parse(&bytes)?`) y `main.rs:291-293` propagan el error con
  `?` desde `network_to_stack`, abortando el `select!` de `serve()`. Disparadores con peer
  autenticado: flag reservado IPv4, `data offset` TCP fuera de rango, `length` UDP
  inconsistente, o cualquier paquete legítimo con `total_length > 1500` (`main.rs:284`).
  Un paquete destruye **todas** las sesiones, no solo ese flujo. Remedio: `QueueError::InvalidPacket`
  contado como drop (`telemetry.record_drop_at`) y `continue`; reservar el teardown para
  desincronización de framing.
- **[Alta, correctitud] `main.rs:150-152` — `accept` transitorio mata el proceso.**
  `accepted?` y `set_nodelay(true)?` propagan fuera de `run_with_policy` y `main` devuelve
  ese `io::Result` (`84-86`): `EMFILE`/`ENFILE` (alcanzable: 256 flujos × socket upstream +
  1 UDP por flujo, más los descriptores del stack, contra un `RLIMIT_NOFILE` típico de 1024)
  o `ECONNABORTED` terminan el relay completo. Remedio: backoff y `continue` para errores
  transitorios; `set_nodelay` no fatal.
- **[Alta, correctitud] `main.rs:102-129` — el relay sobrevive a su padre. Verificado:** con
  EOF en stdin, `configuration_loop` simplemente `break` y el proceso sigue enrutando con la
  última política aceptada, conservando `127.0.0.1:27184` y su token (el comentario `// EOF
  never removes the last accepted manual policy` lo documenta como decisión). Una segunda
  instancia falla `bind` con `AddrInUse`. Añadido: `println!` entra en pánico con `EPIPE`
  (`main.rs:134, 512-518, 528-531`). Remedio: salir con error ante EOF de stdin,
  `PR_SET_PDEATHSIG` en Linux, y `writeln!` con error manejado.
- **[Media, correctitud] Mutex envenenados.** `policy.rs:219-432` y `packet_queue.rs:43-122`
  usan `lock().unwrap()` en todas partes; un pánico con el lock tomado lo envenena para
  siempre y cada paquete posterior entra en pánico, **mientras el proceso sigue anunciando
  `LE_READY`** (`main.rs:81`): el host ve un relay sano con agujero negro. Superficies de
  pánico bajo lock: `flows.get_mut(key).unwrap()` / `packets.front().unwrap()`
  (`flow_scheduler.rs:165-213`), `buckets.get_mut(key).unwrap()` (`policy.rs:559, 565`).
  Remedio: `unwrap_or_else(|e| e.into_inner())` o supervisión que reinicie; como mínimo
  salir con código distinto de cero para que la recuperación del host reaccione.
- **[Media, correctitud/contrato] ACK que nunca casa y `linkKey` que rompe la telemetría.**
  (a) `policy.rs:104-106, 377-383` echoa el snapshot **aplicado**; en rechazo previo es
  `revision = 0`, y `TryReadAcknowledgement` (`LERelayPolicyProtocol.cs:94, 118`) rechaza
  `revision <= 0`: `ApplyAsync` gasta siempre los 2 s de plazo y el `reason` del relay nunca
  se muestra. **Verificado por lectura** en ambos lados. (b) `Config::validate` acepta
  `link_key.len() <= 256` y el host permite 256, pero `TryReadQos` exige 128
  (`LERelayPolicySession.cs:42-43, 68-69`): claves de 129-256 caracteres hacen fallar **todas**
  las muestras `LE_QOS` y el presupuesto adaptativo corre a ciegas, sin error visible.
  Remedio: echoar `(revision, generation)` enviados con `accepted:false`; unificar el límite
  en una constante compartida.
- **[Media, seguridad] Token en el entorno y sesión que se auto-expulsa.** El control es un
  TCP loopback con token estático de 64 hex por entorno del hijo; en Linux
  `/proc/<pid>/environ` lo lee cualquier proceso del **mismo UID** (no es escalada, pero sí
  secuestro/DoS del túnel), y una segunda sesión **aborta la anterior** (`main.rs:178-181`).
  Remedio: `AF_UNIX` + `SO_PEERCRED` (o named pipe con DACL restrictiva) y rechazar la
  segunda sesión en vez de desalojar la viva.
- **[Media, rendimiento] Ruta caliente con dos asignaciones, dos copias y ~6 locks por
  paquete.** `main.rs:292, 494, 745-751`, `proxy_copy.rs:59`, `policy.rs:408-421`,
  `telemetry.rs:110-167`; a ~120 kpps son ~240 k asignaciones/s y `notify_waiters` despierta
  a los 256 flujos. En modo sin shaping se ejecuta además `dequeue_fifo`, un barrido O(flujos)
  por paquete (`flow_scheduler.rs:189-205`). Remedio: colas de `Bytes`/`BytesMut` de un pool
  acotado, decisión de shaping cacheada en un `AtomicU64` refrescado al aplicar, contadores
  por átomos y un `Notify` por dirección.
- **[Media, correctitud/DoS] Sin timeout de `connect` y un solo presupuesto de flujos.**
  `main.rs:268, 383, 617-634`: TCP y UDP comparten `MAX_FLOWS = 256` y los flujos UDP solo
  expiran dentro de `retain` al siguiente datagrama; un peer puede agotar las ranuras con
  destinos públicos en negro o rotando destinos UDP. Además `relay_udp` propaga errores de
  `recv_from_to`/`bind`/`connect` con `?` y un `EMFILE`/`ENETUNREACH` transitorio derriba todo
  el proxy UDP. Remedio: `timeout` en connect, presupuestos separados por protocolo,
  expiración por temporizador y errores locales de flujo.
- **[Media, seguridad] Salida sin allowlist de puertos ni auditoría, con excepción de
  loopback codificada.** `main.rs:770-842`: el predicado `is_public_ipv4` está bien
  construido, pero no hay allowlist de puertos, ni tope por destino, ni registro de destinos;
  y `781-788, 833-842` abren una excepción explícita `10.0.2.2:28085 → 127.0.0.1:28085` sin
  verificar que el listener sea el servidor de speedtest de NOVORA. Remedio: límites por
  destino /24, registro en `flow_limit`, y verificar el endpoint o usar socket dedicado.
- **[Baja, correctitud] Invariantes sin comprobar en la salida del stack.**
  `main.rs:487-492, 566-580`: se rechaza `sizes[index] > MTU` pero no `< 20`, y
  `record_ip_bytes` indexa `packet[0..9]` sin comprobar longitud (hoy no alcanzable desde la
  red, pero combinado con el envenenamiento de mutex sería caída permanente). Remedio:
  `continue` si `sizes[index] < 20` y `check_len` explícito.
- **[Baja, duplicación] Dos copias idénticas del crate (~3.086 líneas).** El coste ya es
  visible en los huéspedes, que parsean el mismo `LE_METRIC` con criterios distintos
  (`LENetworkRelay.cs:463-467` `long.TryParse` + `>= 0` frente a `LinuxLinkRelay.cs:219-227`
  `NumberStyles.None` sin signo). Remedio: un crate en `src/shared/NovoraRelay` con dos
  binarios finos por plataforma; `tcp_ip` fijado por revisión y la licencia Apache-2.0 en un
  solo sitio.
- **[Nit, legibilidad] `serve()` (220 líneas, 13 canales y un `select!` de 8 brazos) y
  `main.rs` (1041 líneas) mezclan framing de IPC, autenticación, clasificación IPv4,
  formato de métricas y plano de datos.** Remedio: futuros nombrados (`ingress`, `dispatch`,
  `egress`, `tcp_proxy`, `udp_proxy`, `metrics`, `watchdog`) y mover `is_public_ipv4` a su
  módulo.

### Remedios estructurales

1. **Un solo crate compartido, hoy.** Es un cambio de ~1 hora ahora y un generador de
   conflictos de merge cada semana después.
2. **Plano de datos que falla suave y observable:** ningún paquete, descriptor, error de
   socket o pánico debe tumbar el túnel ni el proceso; añadir contadores `LE_HEALTH` para que
   la recuperación del host sepa *por qué* degradó en vez de inferirlo del socket cerrado.
3. **Canal host↔relay sobre IPC local autenticado en vez de puerto loopback con secreto por
   entorno:** `AF_UNIX` + `SO_PEERCRED` / named pipe con DACL, y segundo cliente rechazado en
   lugar de desalojo.

---

## 4. UI WPF + prototipo React

### Resumen

- **Verificado en esta sesión:** la aplicación Linux compila en Release (0 advertencias, 0
  errores, 10.9 s) — el resto de esta sección no se compiló aquí porque WPF requiere Windows.
- **Qué es `src/*.tsx`:** un prototipo de UI navegable, **sin ninguna capacidad de red**
  (cero `fetch`/`WebSocket`/`postMessage` en `src/`, verificado por el revisor que lo revisó);
  todos los estados son literales o simulaciones de `setTimeout`/`setInterval`/
  `requestAnimationFrame`, y el «QR» de emparejamiento es un SVG decorativo. No hay
  `src/index.html`: la entrada es `/index.html` de la raíz.
- La WPF implementa fielmente la paleta Platinum Air de `DESIGN.md`
  (`Resource/NLResourceStyles.xaml:9-26`); el prototipo React implementa **otro** sistema
  (tinta monocroma, oscuro por defecto, Inter/JetBrains Mono): dos sistemas de diseño, una
  marca.
- Positivos a conservar: el canal de IPC de Android está auténticamente protegido
  (`NLControlTrustServer.cs:176-229`), cada petición se marshaliza y re-autoriza en el hilo
  de UI (`NLUIWindowMainAndroidDispatch.cs:9-14`) y los valores del peer se validan contra
  las listas de opciones anunciadas (`NLControlCommands.cs:105-119`).
- `NLUIWindowSettings.xaml.cs` **no existe** en este árbol (solo respaldos en `PES_STM/` y
  artefactos `obj/*.g.i.cs`): los ajustes viven en `SettingsPage14`
  (`NLUIWindowMain.xaml:1148-1226`).

### Hallazgos

- **[Alta, correctitud WPF] `NLUIWindowMain.xaml.cs:1186-1245` — `Dispatcher.Invoke`
  bloqueante sin guarda `_closing`.** `ShellGamepad_StatusChangedVE`,
  `ShellGamepad_BatteryAlertVE`, `ShellIntegration_StatusChangedVE` y
  `ShellNvidia_StatusChangedVE` bloquean al productor durante `ApplyGamepadShell14` (~50
  escrituras de UI + `SetResourceReference` + dos sliders) y, si el evento llega durante el
  teardown, lanzan en el hilo productor (excepción no controlada → fin del proceso). El
  propio archivo tiene el patrón correcto 30 líneas antes (`1196-1220`, coalescido con
  `BeginInvoke` a `DispatcherPriority.Render`). Remedio: `if (_closing) return;` +
  `BeginInvoke` coalescido, nunca `Invoke`.
- **[Alta, correctitud WPF] `NLUIWindowMain.xaml.cs:150-194, 241` — cierre sin timeout.**
  `e.Cancel = true` incondicional, `CleanupBeforeClose14Async` espera `_exInInitialization`
  y varias idas y vueltas de ADB sin plazo ni indicación de progreso, y un segundo clic se
  ignora (`161-164`): si un paso no termina, la ventana no se puede cerrar y hay que matar
  el proceso. Remedio: CTS enlazado (~5 s), estado «cerrando» y cierre forzado que omita los
  pasos restantes; no esperar nunca una tarea de inicialización sin límite.
- **[Alta, seguridad] `NLUIWindowMainStreaming.cs:50` → `NLStreamingService.cs:79-90` — clave
  RTMP en argv.** Verificado por el revisor de área: se concatena en una **cadena** y se pasa
  a `ProcessStartInfo("ffmpeg", args)`, quedando visible en `Win32_Process.CommandLine` para
  cualquier proceso local durante toda la emisión; el comentario «la clave nunca se reporta
  a la UI» es cierto pero engañoso. El repo ya tiene el patrón correcto
  (`NLServiceAppControlRepository.cs:77-79`, `ArgumentList`). Este hallazgo coincide con el
  ya registrado el 2026-11-10 en bitácora. Remedio: preferir la ruta OBS WebSocket
  (`ObsConnectAsync`, `139-163`, que sí hashea) para el caso por defecto; si RTMP se mantiene,
  documentar la exposición en `SECURITY.md` y dejar de afirmar lo contrario.
- **[Alta, correctitud React] `App.tsx:45-76` con `main.tsx:7` (`StrictMode`) — efectos
  secundarios dentro de updaters.** `showToast()` (`setToastMessage` + `setTimeout` +
  `setToastTimeout`) se invoca dentro de los updaters de `handleToggleTheme/Record/StEngine`:
  cada toggle dispara doble toast y dos timers, y el id vive en estado, así que las closures
  capturadas una vez con dependencias `[]` limpian siempre un id obsoleto — un timer huérfano
  puede apagar un toast nuevo antes de tiempo. Remedio: calcular `next`, llamar a `showToast`
  después del setter, id en `useRef` y limpieza en `useEffect`.
- **[Media, correctitud React] `ConectarScreen.tsx:31-42` (también `LiveTestScreen.tsx:115-145`,
  `EnginesScreen.tsx:21-35`) — intervalos que sobreviven a la navegación.** Los handles se
  guardan en variables locales y solo se limpian desde dentro del updater: salir a mitad del
  escaneo deja un `setInterval` de 400 ms vivo para siempre que llama a `onShowToast` →
  aparece «Detección completada» en una pantalla no relacionada. Remedio: hooks
  `useInterval`/`useTimeout` con limpieza al desmontar.
- **[Media, rendimiento + accesibilidad React] `LiveTestScreen.tsx:31-109, 325-341, 517-533`
  — bucle rAF permanente.** Muta estado cada fotograma (incluso sin mando y con la pestaña
  oculta) y re-renderiza un componente de 690 líneas a ~60 fps; las dos superficies de
  joystick son `<div>` con eventos de ratón: solo ratón (en una UI con forma móvil), sin
  teclado, sin etiquetas, y un `mouseup` fuera del elemento deja `isDraggingL/R.current` en
  `true`. Remedio: `setInterval` a 10-20 Hz condicionado a `document.visibilityState` y a la
  presencia de mando real, memoizar los subárboles, y `role="slider"` + `tabIndex` +
  `setPointerCapture`.
- **[Media, rendimiento WPF] `NLUIWindowMain.xaml.cs:1040-1061` — guardado por cada tick de
  slider.** `new NLServiceSettings()` + `Load()` + `Save()` en el hilo de UI por cada
  movimiento; `NLServiceSettings.Save` es sincrónico (`NLServiceSettings.cs:178-207`,
  `File.WriteAllText` + `File.Move`) → decenas de escrituras por segundo y tirón visible con
  AppData redirigido a OneDrive. Existe ya el patrón con debounce
  (`QueueVisionOutputSettingsSave14`, `691-702`). Remedio: unificar ambos handlers en el
  guardado con debounce.
- **[Media, correctitud WPF] `NLUIWindowMainDiscoveryLifecycle.cs:56-69` frente a `156-188`
  — watchdog que nunca se detiene.** `_adbWatchdogNV` se crea y `Start()`ea, pero
  `StopDiscoveryLifecycleNVAsync` no lo detiene ni desuscribe: el temporizador de 5 s sigue
  disparando `RefreshFromAdbDiscoveryNVAsync` → `RefreshDevicesAsync(force: true)` sin
  suscripción de descubrimiento detrás. Solo `_automaticConnectionTimer` recibe su `Stop()`
  (`NLUIWindowMainAndroidTrust.cs:103`). Remedio: detener, desuscribir y anular en el cierre.
- **[Media, correctitud React] `ArchivosMultimediaScreen.tsx:148-155` (input en `175-180`) —
  archivo repetido ignorado.** `handleFileUpload` no resetea `e.target.value`: volver a elegir
  el mismo archivo no dispara `change` y la UI no hace nada. Remedio: `e.target.value = ''` al
  final del handler.
- **[Media, accesibilidad React] `SettingsModal.tsx:29-37`, `ConectarScreen.tsx:373-380`,
  `index.html:5` — modales sin gestión de foco.** Declaran `role="dialog" aria-modal="true"`
  pero no atrapan el foco, no enfocan al abrir, no cierran con `Escape` ni con clic en el
  fondo; y `maximum-scale=1.0, user-scalable=no` bloquea el zoom (WCAG 1.4.4). Remedio: un
  `<Dialog>` compartido con captura/restauración de foco y cierre por `Escape`/fondo.
- **[Media, correctitud WPF] `NLUIWindowMain.xaml.cs:110-148` — inicialización a medias.**
  `_shellInitialized14 = true` se fija **después** de `LoadSettingsToViewModel14()`: si esa
  carga lanza, el catch solo escribe en `Debug` y deja la ventana con handlers que retornan
  temprano (`470, 484, 496, 552`) — la app deja de persistir ajustes en silencio y
  `_viewModel.PropertyChanged` / `Closed` nunca se suscriben. Remedio: suscribir primero,
  cargar al final, y ante fallo deshabilitar la superficie de ajustes con motivo visible.
- **[Media, arquitectura WPF] `NLUIWindowMainVisionEngine.cs` (2330 líneas, 10 conceptos) y
  `NLUIWindowMain.xaml` (1288 líneas con 8 páginas infladas al arranque).** Remedio: partir
  en `…VisionEngine.{Lifecycle,Presentation,Exchange,Status,Lan}.cs`, mover contadores de
  transferencia a un tipo de estadísticas de sesión, y hacer cada página un `UserControl`
  materializado en la primera navegación.
- **[Baja, tokens de diseño] `src/index.css:22, 104-117` frente a `DESIGN.md:48, 71-89`.** El
  prototipo usa `#0a0a0a` en vez de Cobalt `#304FFE`, Inter/JetBrains Mono en vez de
  Montserrat/Manrope (el propio `DESIGN.md:81` pide no sustituirlas en silencio), radios y
  sombras en cero, y `index.html:2` fija `class="dark"` mientras la especificación manda
  claro primero. No es preferencia: la WPF ya envía la paleta especificada, así que el
  prototipo enseña un producto que no existe. Remedio: re-derivar los tokens de `DESIGN.md`
  §2-3 o archivar el prototipo fuera de `src/`.
- **[Baja, reproducibilidad de build] `package.json:7, 14-21, 32`.** `"esbuild": "file:.25.12"`
  apunta a un directorio inexistente y `npm ci` limpio falla; `@google/genai`, `express`,
  `dotenv`, `lucide-react`, `motion` y `dotnet-sdk` no los usa ningún archivo de `src/`;
  el script dev enlaza `--host=0.0.0.0`; y `NovoraLogo.tsx:17-18` enlaza un banner externo
  opaco de `lh3.googleusercontent.com` (la primera pantalla se rompe sin red o cuando el
  activo caduque). `src/types/index.ts:3-11, 26-52` declara tipos que nada importa. Remedio:
  quitar la dependencia falsa y las no usadas, empaquetar el logo localmente, dev a
  `127.0.0.1`, borrar tipos muertos.

### Remedios estructurales

1. **Una sola disciplina de despacho en la ventana.** Un `PostUi(Action)` que compruebe
   `_closing`/`Dispatcher.HasShutdownStarted` y coalesza por canal; después borrar todo
   `Dispatcher.*` directo de los handlers — cierra de un golpe la clase «excepción al
   cerrar» y los bloqueos del hilo productor.
2. **Partir el parcial de 2330 líneas y el page host de 1288** por límites de concepto.
3. **Dar veredicto al prototipo React:** sin ruta de datos y con otro sistema de diseño;
   re-estilarlo desde `DESIGN.md` y conectarlo a una fuente real, o archivarlo fuera de `src/`.

---

## 5. VisionEngine (en curso)

Pendiente de la pasada de revisión en curso; se añadirá en la actualización de este informe.

## 6. Android + Linux (en curso)

Pendiente. Nota previa: los archivos `src/LINUX/MainWindow.VisionExchange.cs`,
`VisionEngine/LinuxFfmpegVideoDecoder.cs`, `VisionEngine/LinuxSdlVideoRenderer.cs`,
`tests/VisionExchangeChecks.cs`, `VisionEngine/LinuxDisplayWakeWatcher.cs` (nuevo) y
`src/APPLE/` (sin rastrear) son WIP del usuario; los defectos que se encuentren en ellos se
marcarán «(WIP)» y no se corregirán en esta revisión.

## 7. Transversal: build, CI, pruebas y dependencias (en curso)

Pendiente la auditoría de workflows, cobertura de pruebas y cadena de suministro.

---

## Verificación ejecutada en esta sesión

| Comprobación | Comando | Resultado literal |
|---|---|---|
| Build Linux Release | `DOTNET_ROLL_FORWARD=Major dotnet build src/LINUX/NOVORA.Linux.csproj -c Release` | Compilación correcta · **0 advertencias, 0 errores** · 10.9 s |
| Pruebas del relay Rust | `CARGO_TARGET_DIR=/tmp/opencode/relay-target cargo test --offline` (en `src/LINUX/LinkRelay`) | **29 passed; 0 failed** (0.31 s) |
| Suite Linux completa | `DOTNET_ROLL_FORWARD=Major dotnet run --project src/LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release` | **Todo PASS**: aceleración (cuda verificada; qsv/vulkan no disponibles), FILES, FILES V2 (transferencia/SHA/generación/**traversal, symlink y no-sobrescritura**), presupuesto Android, grabación MKV/H264/PCM, smoke FFmpeg H.264 y SDL (software 64×64), protocolo y trust-store |
| Copias del relay | `diff -r .../NovoraRelay/src .../LinkRelay/src` | Sin salida, exit 0 → **idénticas** |
| `unsafe` en el relay | `grep -rn unsafe src/LINUX/LinkRelay/src` | **0** |
| Auditoría npm | `npm audit --json` | **0 vulnerabilidades** (273 dependencias) |
| Secretos rastreados | `git grep` patrones de clave/token + `.env`/`.pem`/`.pfx`/clave privada | Solo 3 constantes de **prueba** con token ficticio repetido (`tests/…/Program.cs`, `src/LINUX/tests/Program.cs`); `.env.example` es plantilla; **0 claves privadas** |
| Herramientas retiradas | `git ls-files src/WINDOWS/Tools` + `git grep scrcpy/gnirehtet -- scripts/` | **No hay scrcpy/gnirehtet rastreados**; `Setup-Tools.ps1:23` y `Build-UnifiedInstaller.ps1:39` los **rechazan activamente** si aparecen (objetivo del proyecto en vigor) |
| Peso del repo | `git ls-files \| wc -l`, `du -sh .git` | 914 archivos rastreados, **148.3 MB** rastreados, `.git` 167 MB; mayor: `src/WINDOWS/Tools/avcodec-62.dll` **86 MB** |
| Cobertura por módulo crítico | `grep -rl <tipo> tests/ src/LINUX/tests/` | `NLServiceADB` 8 archivos · `ExInCoreEngine` 6 · `LENetworkRelay` 2 · **`LERecoveryMonitor` 0 · `LETransportManager` 0 · `LERuntimeManager` 0 · `NLServiceUpdate` 0** |
| Certificado de firmas | `grep -rn "Authenticode\|WinVerifyTrust\|GetAllCertificates" src/` | **0 resultados** (no hay verificación de firma en ninguna parte) |

No se compiló Windows (WPF no soportado en este entorno) ni Android (falta SDK/.NET 10),
no se ejecutó la suite Windows ni hubo dispositivo físico. Los recuentos de pruebas Rust y
Linux aquí registrados son de esta sesión; los de bitácoras anteriores corresponden a otras
ejecuciones.

## Orden de corrección sugerido

1. **Dinero y privilegios:** sellar la entrega del instalador (ACL + re-hash + Authenticode) y
   verificar la integridad de `Tools\adb.exe`; acotar los deadlines de ADB con kill del árbol.
2. **Caídas totales:** bucle de recuperación de ExIn, monitor de LinkEngine que no se re-arma,
   paquete mal formado y `accept` transitorio del relay, y EOF de stdin del relay.
3. **Estado y concurrencia:** publicar `LERuntimeSession` fuera del lock; `Failed` solo para
   violaciones de protocolo; back-pressure de ExIn sin lanzar; guardas `_closing` y cierre con
   timeout en la WPF.
4. **Deuda estructural:** un crate de relay compartido; `LEPorts`/parser de reverse únicos;
   `LELinkHealth` único; partir `NLServiceADB`, `ExInManager`, `LETransportManager` y el
   parcial de VE; retirar `LEFailoverADB`, `LERecoveryPolicy` (Windows) y
   `NLServiceConsoleCommand`.
5. **Producto:** veredicto sobre el prototipo React (re-estilar o archivar) y clave RTMP fuera
   de la línea de comandos.

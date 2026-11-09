# Conectividad dinámica Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Iniciar VisionEngine, LinkEngine y ExInEngine desde PC o Android mediante la conexión autorizada disponible del mismo teléfono, sin selección previa USB/LAN.

**Architecture:** Un coordinador por dispositivo conserva identidad, rutas, intenciones y generaciones. Los adaptadores reutilizan captura, VPN/relay y HID existentes; el control Android conserva sus permisos y su cliente activo único. El transporte es un detalle del canal, no la identidad del teléfono ni la intención del motor.

**Tech Stack:** C#/.NET 8 y Avalonia en Linux; .NET Android según `NLProjectAndroid.csproj`; ADB, TLS, Android Keystore, FFmpeg/SDL3 y relay Rust existentes.

**Spec:** `docs/superpowers/specs/2026-11-08-conectividad-dinamica-design.md`

## Global Constraints

- Alcance: NOVORA-LINUX y los cambios de LINK Control necesarios; conservar compatibilidad Windows.
- USB y LAN permanecen como transportes internos; Auto es el modo predeterminado.
- No iniciar los tres motores automáticamente al conectar; recuperar solo intenciones activas de esta sesión.
- No unir teléfonos por nombre, IP, número de serie ADB ni primer dispositivo enumerado.
- No omitir MediaProjection, VPN, confianza ni preparación shell/HID.
- ExIn por LAN sin cable es criterio obligatorio; TLS por sí solo no concede permiso shell.
- No introducir scrcpy ni sustituir UHID por accesibilidad; conservar avisos aplicables.
- No instalar APK, modificar firewall, emparejar depuración, desplegar, hacer commit ni push como efectos laterales de las pruebas locales.
- El árbol contiene trabajo concurrente: revisar estado/diff antes de cada edición y coordinar propiedad de host/server/UI con Claude.

## Review Focus

- Dos teléfonos con igual nombre o una IP reutilizada: nunca reenviar motores al teléfono incorrecto (Tareas 1, 3 y 6).
- PC y Android solicitan inicio mientras cae una ruta: una sola operación efectiva, sin respuestas tardías que resuciten motores (Tareas 3, 4 y 7).
- Cable conectado/desconectado rápidamente mientras LAN sigue sana: no oscilar ni secuestrar la sesión (Tareas 2, 3 y 7).
- Pérdida durante VPN, grabación, transferencia o mando pulsado: terminar/cancelar con integridad y neutralizar entrada (Tarea 7).
- APK antiguo, confianza migrada o shell HID no disponible: mantener compatibilidad y distinguir preparación pendiente de motor iniciado (Tareas 1, 5, 6 y 9).

## Entregas y dependencias

`1 identidad → 2 rutas → 3 coordinador → 4 arranque → 5 Android → 6 ExIn LAN → 7 recuperación → 8 interfaz → 9 aceptación`.

Cada entrega debe compilar y tener sus pruebas verdes antes de integrar la siguiente.
Tareas 1 y 5 tocan contratos compartidos Windows/Android. No dividir escritores
concurrentes sobre esos archivos. El plan se ejecuta sin cambiar el diseño visual
Windows ya aprobado.

Cada tarea que introduce un modo de pruebas modifica también
`src/NOVORA-LINUX/tests/Program.cs` para despacharlo antes de ejecutarlo en rojo.
El comando común es `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- <modo>`.
Comprobar que el modo invoca sus pruebas y que un flag desconocido falla: no
aceptar el PASS accidental de la suite anterior si faltó registrar el modo.

## Contratos nuevos propuestos

Ubicación Linux: `src/NOVORA-LINUX/Control/Dynamic/`. Mantener namespace
`NOVORA.Linux.Control.Dynamic` para no colisionar con `ControlEngines` existente.

```csharp
enum TransportKind { Usb, Lan }
enum EngineKind { Vision, Link, ExIn }
enum ConnectionPhase { Disconnected, Discovering, AwaitingTrust, Connecting, Connected, Recovering, Failed }
enum EnginePhase { Stopped, WaitingForConnection, WaitingForPermission, Starting, Running, Recovering, Stopping, Failed }
enum EngineAvailability { Ready, NeedsSetup, Unavailable }
record DeviceIdentity(Guid Id, string InstallKeyHash);
record RouteKey(Guid Value);
record RouteCandidate(RouteKey Key, DeviceIdentity Device, TransportKind Transport,
    bool Reachable, bool Authorized, long Revision);
record EngineRequest(Guid OperationId, DeviceIdentity Device, EngineKind Engine);
record EngineState(EngineKind Engine, EnginePhase Phase, long Epoch, string Message);
record ChannelLease(Guid Id, DeviceIdentity Device, RouteKey Route, long Epoch);
record EngineCapability(EngineAvailability Availability, string Reason);
record AuthorizedRoute(RouteCandidate Candidate, string InstallKeyHash, long ControlGeneration);
```

`RouteCandidate` no contiene credenciales, direcciones ni seriales en su
representación pública; el registro privado de endpoints los resuelve por clave.
Reachable no equivale a Authorized. Las rutas de reserva conocidas pueden
permanecer sin control autenticado hasta el failover: no se presupone un segundo
cliente activo en Android.

### Tarea 1: Identidad lógica y confianza de varias rutas

**Archivos:** crear `Control/Dynamic/DeviceIdentity.cs`, `LinuxDeviceRegistry.cs` y `LinuxRouteBinding.cs`; modificar `Control/LinuxTrustStore.cs`, `Control/ControlProtocol.cs`, `src/NOVORA LINK/Control/NLControlProtocol.cs`, `NLControlTrustedPc.cs`, `NLControlSession.cs`, `src/LINK Control/AndroidStorage/NLAndroidStorageTrustedPcs.cs`; crear `AndroidStorage/NLAndroidInstallationIdentity.cs` y `tests/DynamicIdentityChecks.cs`.

**Interfaces:** `LinuxDeviceRegistry.BindAsync(DeviceIdentity, RouteKey, RouteBindingProof, CancellationToken)` verifica la prueba antes de vincular; `RevokeAsync(DeviceIdentity, CancellationToken)` invalida todas las rutas. Definir `RouteBindingProof` con desafío, huella PC, época, hash de clave pública y firma. Incorporar campos opcionales de capacidades/identidad al protocolo actual sin cambiar la versión del framing.

- [ ] Escribir pruebas de dos rutas de una instalación, dos instalaciones con igual nombre/IP, nonce repetido, firma/huella equivocada y revocación en ambas rutas. Verificar que las uniones sin prueba sean rechazadas.
- [ ] Añadir una migración probada de Android: guardar USB y después LAN conserva ambas credenciales para una huella PC; el almacén antiguo se lee, el cifrado AES existente se conserva y un archivo corrupto no se reemplaza.
- [ ] Ejecutar el nuevo modo `--dynamic-identity`; observar fallo por los comportamientos ausentes antes de implementar. Registrar comando y resultado.
- [ ] Implementar identidad Android ECDSA P-256/SHA-256 y prueba de posesión con nonce de 256 bits de un solo uso, vencimiento de 120 segundos y máximo 32 desafíos pendientes por dispositivo; descartar al revocar/cerrar. Persistencia atómica con formato versionado y respaldo del original antes de migrar.
- [ ] Negociar `dynamicConnectionV1`. Clientes sin capacidad usan el flujo actual, sin vinculación automática entre rutas. Una ruta adicional exige autenticación o emparejamiento explícito y prueba de la misma instalación.
- [ ] Verificar Linux y pruebas puras compartidas. Compilar Android y Windows en los entornos soportados; no declarar esta entrega compatible si sus consumidores compartidos no compilan.

### Tarea 2: Inventario de rutas y preparación automática

**Archivos:** crear `LinuxRouteMonitor.cs`, `ILinuxRouteConnector.cs`, `LinuxUsbRouteConnector.cs`, `LinuxLanRouteConnector.cs`; modificar `Control/AdbBridge.cs`, `LinuxLanDiscovery.cs`, `LinuxControlHost.cs`; crear `tests/DynamicRouteChecks.cs`.

**Interfaces:** `Task<AuthorizedRoute> ILinuxRouteConnector.ConnectAsync(RouteCandidate, CancellationToken)` devuelve la referencia de sesión autenticada y su identidad comprobada; el conector conserva el recurso real por RouteKey. `Task StopAsync(RouteKey, CancellationToken)` libera solo recursos propios. `LinuxRouteMonitor.Changed` publica candidatos asociados al registro de Tarea 1.

- [ ] Crear pruebas de hotplug USB, dispositivo unauthorized/offline, cambio de interfaz/IP, desaparición LAN, múltiples NIC, reinicio del servidor ADB y recurso reverse ajeno. Verificar que inventario no arranque motores.
- [ ] Añadir `--dynamic-routes`, ejecutarlo y registrar el fallo esperado antes de implementar.
- [ ] Observar ADB con `track-devices`, drenar salida/error y reiniciar esa suscripción con backoff; usar eventos de cambio de red para reevaluar descubrimiento LAN e interfaces privadas.
- [ ] Extraer de `LinuxControlHost.Start` / `StartUsbAsync` la preparación de transporte para que no invoque `SelectTransport`. Preparar el bootstrap USB protegido y los listeners LAN existentes, conservando certificado y rutas reverse propias.
- [ ] Permitir solamente resume de confianza conocida como acción automática; ante teléfono nuevo publicar `AwaitingTrust`. No emparejar ni habilitar depuración silenciosamente.
- [ ] Verificar el modo y las suites existentes de pairing/resume/revocación. La prueba de LAN no debe invocar ADB para VE o LE.

### Tarea 3: Coordinador dinámico e intención de motores

**Archivos:** crear `DynamicConnectionContracts.cs`, `LinuxConnectionCoordinator.cs`, `LinuxEngineIntentStore.cs`; modificar `LinuxControlHost.cs`, `LinuxConnectionSelection.cs`; crear `tests/DynamicCoordinatorChecks.cs`.

**Interfaces:** `Task<EngineState> StartEngineAsync(EngineRequest, CancellationToken)`, `Task<EngineState> StopEngineAsync(EngineRequest, CancellationToken)`, `void ObserveRoute(RouteCandidate)`, eventos `Action<ConnectionPhase> ConnectionChanged` y `Action<EngineState> EngineChanged`. El coordinador es por dispositivo; una lista superior compone estados por DeviceIdentity. Consumir inventario/identidad de Tareas 1–2; producir estado y concesiones `ChannelLease` para adaptadores. El coordinador recibe `TimeProvider` y una fuente de jitter.

- [ ] Escribir la matriz de selección: USB solo, LAN solo, ambas sin sesión, LAN activa seguida de USB y dos teléfonos. Probar preferencia inicial USB sin esperar un USB no autorizado cuando LAN ya está lista.
- [ ] Escribir pruebas de inicio PC+Android simultáneo, parada durante conexión, pérdida durante arranque, repetición de OperationId, excepción de un adaptador y respuesta tardía de otra época.
- [ ] Ejecutar `--dynamic-coordinator` y observar fallos antes de implementar.
- [ ] Implementar una cola de eventos serializada por dispositivo, no un lock retenido durante red/ADB. Mantener ruta sana; elegir alternativas del mismo dispositivo y asignar época monotónica al cambiar.
- [ ] Conservar intenciones en memoria durante esta ejecución. Deduplicar por dispositivo/motor/OperationId: máximo 256 resultados terminales con retención de 120 segundos; no expulsar operaciones en curso, rechazar sobrecarga explícitamente. Una parada invalida todo arranque anterior del mismo motor.
- [ ] Aplicar plazos/reintentos de la spec. Cancelar conexión y recursos parciales antes del siguiente intento. Múltiples teléfonos requieren selección de dispositivo, nunca de cable/red.
- [ ] Verificar sin sleeps reales usando tiempo y jitter controlados. El modo manual anterior se conserva como adaptador avanzado durante la transición y deja de gobernar el flujo normal.

### Tarea 4: Adaptadores y arranque real de los tres motores

**Archivos:** crear `ILinuxDynamicEngineAdapter.cs`, `LinuxVisionEngineAdapter.cs`, `LinuxLinkEngineAdapter.cs`, `LinuxExInEngineAdapter.cs`; modificar `LinuxControlServer.cs`, `ILinuxLinkEngine.cs`, `LinuxLinkEngine.cs`, `LinuxLanLinkEngine.cs`, `MainWindow.ExIn.cs`; crear `tests/DynamicEngineChecks.cs`.

**Interfaces:** `EngineCapability GetAvailability(DeviceIdentity, RouteCandidate)`, `Task<EngineState> StartAsync(EngineRequest, ChannelLease, CancellationToken)` y `Task StopAsync(DeviceIdentity, long epoch, CancellationToken)`. Availability consulta capacidad conocida, sin bloquear en red/shell; la preparación asíncrona actualiza esa capacidad. La oferta interna de canales conserva los contratos USB/LAN actuales.

- [ ] Escribir contratos parametrizados por motor/ruta: autorización requerida, idempotencia, detener, error de preparación y arranque simultáneo sin dos propietarios de puertos/relay/sesión VE.
- [ ] Añadir `--dynamic-engines`, observar fallo y después implementar los adaptadores sobre los runtimes existentes. `Starting` no se convierte en `Running` solo porque se devolvió una oferta.
- [ ] Hacer que solicitudes nuevas PC/Android pasen por el coordinador. Mantener acciones antiguas `startLink`, `startAppVideo`, `startVideoLan` y `exin.*` como compatibilidad, sin crear una segunda intención paralela.
- [ ] Añadir solicitudes PC→Android con OperationId/Epoch y estado al snapshot para VE/LE; usar el polling de control ya existente. Una solicitud de motor no ejecuta ADB shell ni VPN directamente desde la UI.
- [ ] Separar el inicio local de lectura/calibración ExIn de la conexión HID al teléfono: ninguna selección independiente de «primer ADB» puede sustituir al DeviceIdentity elegido.
- [ ] Verificar permiso pendiente, timeout, rechazo y preparación antes de habilitar la acción; dejar ExIn LAN en `NeedsSetup` hasta completar Tarea 6, sin declarar esta ruta terminada.

### Tarea 5: LINK Control, permisos y compatibilidad

**Archivos:** modificar `src/LINK Control/AndroidService/NLAndroidServiceControl.cs`, `NLAndroidServiceControlAutomaticUsb.cs`, `NLAndroidServiceControlNetwork.cs`, `AndroidVideo/NLAndroidVideoProjectionRequest.cs`, `AndroidVpn/NLAndroidVpnService.cs`, `AndroidUI/NLAndroidUIActivity.cs` y `src/NOVORA LINK/Control/NLControlSession.cs`; crear `AndroidService/NLAndroidServiceDynamicConnection.cs` y pruebas puras en `tests/DynamicAndroidContractChecks.cs`.

**Interfaces:** consumidor de solicitudes `EngineRequest` negociadas por `dynamicConnectionV1`; resolver ofertas por su transporte real. Un único cliente activo `NLControlSession`, con época de dispositivo diferenciada de su generación local de socket.

- [ ] Probar respuestas perdidas/duplicadas, permiso solicitado una sola vez, rechazo de VPN/captura, rotación/recreación de Activity y desconexión mientras el usuario confirma. Compilar esos contratos puros dentro del ejecutable Linux y ejecutar `--dynamic-android` en rojo antes de implementar.
- [ ] Encaminar tanto los botones Android como solicitudes PC al mismo flujo de permisos e inicio. Evitar autorreactivar una intención que Android ya detuvo.
- [ ] Guardar ambos endpoints de confianza; reencontrar LAN por identidad/huella al cambiar IP. Autenticar la ruta nueva antes de publicar «Conectado».
- [ ] Coordinar el cierre del cliente antiguo con el cambio de época y recuperación; descartar snapshots y ofertas atrasados. No añadir dos clientes activos implícitamente.
- [ ] Verificar que un APK antiguo conserve el flujo anterior y que Windows sin la capacidad siga funcionando. Una extensión no negociada no debe alterar sus snapshot/acciones obligatorias.
- [ ] Compilar `NLProjectAndroid.csproj` y ejecutar pruebas de permisos en dispositivo/emulador del entorno Android disponible. Las pruebas puras no sustituyen MediaProjection/VPN reales.

### Tarea 6: ExInEngine por LAN sin cable

**Archivos:** crear `ExInEngine/LinuxExInRouteResolver.cs`, `LinuxWirelessAdbProvisioner.cs`, `Control/Dynamic/LinuxShellRouteProof.cs`; modificar `ExInEngine/LinuxExInPlatform.cs`, `MainWindow.ExIn.cs`, adaptador ExIn de Tarea 4 y `src/LINK Control/AndroidService/NLAndroidServiceUsbBootstrap.cs`; crear `tests/DynamicExInRouteChecks.cs`.

**Interfaces:** `EnsureShellRouteAsync(DeviceIdentity, TransportKind, CancellationToken)` devuelve una concesión con serial ADB verificado y estado `Ready/NeedsSetup/Unavailable`. `PairWirelessAsync(endpoint, pairingCode, CancellationToken)` es una operación explícita de preparación, no un retry automático. No registrar el código.

- [ ] Crear pruebas de LAN autorizada sin ADB, puertos mDNS cambiantes, IP reutilizada por otro teléfono, emparejamiento rechazado, comando HID ausente, USB de otro dispositivo y reinicio del teléfono que invalida el endpoint. Ejecutar `--dynamic-exin` en rojo.
- [ ] Descubrir endpoints de depuración inalámbrica y admitir emparejamiento por LAN cuando el dispositivo lo ofrece. Usar argumentos estructurados de proceso; no asumir puerto 5555, no enviar `tcpip` automáticamente.
- [ ] Vincular el serial ADB a la instalación esperada: enviar un desafío al bootstrap protegido mediante shell y recibir prueba firmada en el control autenticado de esa instalación. El nonce no es una credencial; no pasar tokens TLS por la línea de comandos.
- [ ] Obtener `Ready` solo tras verificar identidad y capacidad HID. Consumir `ExInControlSession` / `INLInputOutput` existentes sin modificar su funcionamiento en Windows. Conservar Game/UI, calibración, paddles, neutralización y salida de mando real.
- [ ] Verificar que una entrada LAN en el snapshot no se anuncie como mando operativo solo por detectar un gamepad en PC. Mostrar preparación necesaria y no elegir otro teléfono como fallback.
- [ ] Probar físicamente LAN sin cable y USB con el mismo mando/teléfono. Si shell/HID no está disponible, documentar el modelo y dejar el criterio abierto: no dar por cumplido «todos los motores por LAN».

### Tarea 7: Recuperación de motores y limpieza por generación

**Archivos:** crear `LinuxEngineRecovery.cs`; modificar coordinador/adaptadores, `VisionEngine/LinuxVisionEngine.cs`, `LinuxVisionLanEngine.cs`, `LinuxVisionRuntime.cs`, `Files/LinuxVisionFiles.cs`, `Control/LinuxLinkRelay.cs`, `LinuxLinkMeasurements.cs` y `MainWindow.ExIn.cs`; crear `tests/DynamicRecoveryChecks.cs`.

**Interfaces:** `RecoverAsync(DeviceIdentity, lostEpoch, CancellationToken)` consume intenciones válidas del coordinador; devuelve estados de la nueva época. Cada worker/callback conserva el ChannelLease que lo creó y no publica sobre otra época.

- [ ] Crear fallos controlados: cable perdido con LAN disponible, Wi-Fi perdido con USB disponible, ambas rutas perdidas, cable intermitente y revocación durante retry. Añadir `--dynamic-recovery` y observar rojo.
- [ ] Añadir los casos críticos: archivo a medio escribir, grabación activa, botón/stick presionado y VPN con flujo TCP activo. Afirmar neutralización, parcial/segmento correcto y que no se declare continuidad de conexiones TCP.
- [ ] Tras perder la ruta, cancelar workers, cerrar canales y liberar recursos propios antes de reconstruirlos. Aplicar backoff/cooldown sin dos motores propietarios del mismo puerto.
- [ ] Recuperar VE con oferta nueva y keyframe; esperar MediaProjection si fue invalidado. Finalizar el segmento de grabación anterior o conservar `.partial` y error visible. No reiniciar una grabación después de parada explícita.
- [ ] Recuperar LE con oferta/token nuevos y VPN según permiso válido; reiniciar mediciones de la sesión. Recuperar ExIn neutralizando y recreando HID, no repitiendo estados pulsados de la época vieja.
- [ ] Invalidar leases FILES V2; abortar transferencias según contrato y ofrecer reintento. No agregar reanudación por offsets a este incremento.
- [ ] Verificar stop/cierre/revocación como barreras: no queda ningún retry ni llega un motor a Running después. Drenar tareas y reportar errores de limpieza agregados.

### Tarea 8: Flujo automático en la interfaz

**Archivos:** modificar `MainWindow.Home.cs`, `MainWindow.Connection.cs`, `MainWindow.Link.cs`, `MainWindow.Vision.cs`, `MainWindow.ExIn.cs` y `MainWindow.cs`; ampliar `tests/WindowsParityChecks.cs` y crear `tests/DynamicUiChecks.cs`.

**Interfaces:** la UI consume `ConnectionChanged` / `EngineChanged` y envía `EngineRequest`. No elige un runtime directamente ni conserva una segunda selección propia de teléfono/ruta.

- [ ] Probar Inicio con USB solo y LAN solo: los mismos botones inician cada motor sin abrir un selector de transporte. Añadir `--dynamic-ui` y verificar rojo antes de implementar.
- [ ] Sustituir la acción principal de conexión por estado automático y selector de teléfono solo si es necesario. USB/LAN quedan en detalles/opciones avanzadas; conservar dimensiones, marca y estilos Windows.
- [ ] Mostrar «Conectando», «Conectado» y «Reconectando», con espera de permiso o preparación ExIn cuando corresponda. Habilitar acciones según capacidad efectiva, no un booleano global de autorización.
- [ ] Probar que dos teléfonos, espera de permiso, fallo agregado de cierre y cambio de época no dejan rótulos de una ruta y acciones sobre otra. Mantener el panel de preparación explícita para confianza/depuración.
- [ ] Renderizar páginas/estados Light/Dark a 1180×800 y 1000×610; revisar visualmente y ejecutar `--windows-ui` para preservar la composición aprobada.

### Tarea 9: Aceptación integrada y documentación

**Archivos:** ampliar `tests/Program.cs`, `ConnectionSelectionChecks.cs`, `ExInChecks.cs`, `LinkChecks.cs` y contratos dinámicos nuevos; actualizar `src/NOVORA-LINUX/README.md`; crear `docs/informes/2026-11-08-conectividad-dinamica-validacion.md` durante la implementación.

- [ ] Añadir los nuevos modos a `Program.cs` y ejecutar también las pruebas dinámicas sin flags en la suite principal. No ocultar los casos de recuperación detrás de modos que CI no ejecuta.
- [ ] Ejecutar `dotnet build src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release` y la suite principal mediante `dotnet run --project ... -c Release --no-build`; esperado: exit 0 y todas las regresiones nombradas aprobadas.
- [ ] Ejecutar `cargo test --manifest-path src/NOVORA-LINUX/LinkRelay/Cargo.toml --locked` y modos `--ve-runtime-native`, `--exin-native`, `--link-native`, `--ve-exchange-native`; comprobar las dependencias de cada fixture antes de atribuir un fallo al producto.
- [ ] Publicar a un directorio temporal y verificar relay ejecutable/licencias, `LE_READY` y autenticación DATA desde ese directorio; no es despliegue ni release.
- [ ] Compilar Android actualizado y consumidores Windows de contratos compartidos. Ejecutar APK antiguo contra Linux nuevo y APK nuevo contra Windows compatible. Un SDK Android ausente es bloqueo de verificación, no PASS.
- [ ] Ejecutar la matriz física siguiente y anexar evidencia por celda; controlar teléfono, versión APK, mando, transporte y permisos.
- [ ] Revisión independiente de identidad, recuperación y regresiones; resolver hallazgos materiales y registrar límites antes de declarar completado el objetivo.

| Escenario físico | VE | LE | ExIn | Comprobaciones adicionales |
|---|---|---|---|---|
| USB solo, inicio PC y Android | Debe iniciar | Debe iniciar | Debe iniciar | Permisos y parada |
| LAN solo, sin cable, inicio PC y Android | Debe iniciar | Debe iniciar | Debe iniciar tras preparación explícita si la necesita | Mando reconocido por Android, no solo por PC |
| Ambas rutas disponibles | Debe iniciar | Debe iniciar | Debe iniciar | Mismo teléfono, sin selección manual de transporte |
| LAN activa, conectar USB | Mantener sesión | Mantener sesión | Mantener sesión | Sin salto innecesario |
| USB perdido, LAN autorizada | Recuperar o esperar permiso | Reconstruir túnel | Neutralizar y recuperar | Grabación/FILES íntegros |
| LAN perdida, USB autorizado | Recuperar o esperar permiso | Reconstruir túnel | Neutralizar y recuperar | Sin reactivar intenciones detenidas |
| Dos teléfonos y una ruta caída | Mismo teléfono o error | Mismo teléfono o error | Mismo teléfono o error | Nunca fallback al otro teléfono |
| Revocación/cierre durante recuperación | Detenido | Detenido | Neutralizado y detenido | Sin reintentos posteriores |

## Autorrevisión del plan

- Todos los motores y ambos orígenes PC/Android están en Tareas 4–6 y en la matriz física.
- Identidad y migración preceden a cualquier fallback: no se acepta inferencia por IP/ADB.
- Los contratos aditivos se comprueban en Linux, Android y Windows; no basta compilar la app Linux.
- Los cinco riesgos de Review Focus tienen pruebas asignadas explícitamente.
- ExIn LAN, permisos y comparación de identidad son requisitos verificables; no se sustituyen por estados UI.
- No hay implementación de producto, migración de datos ni pruebas físicas ejecutadas al escribir este plan.

# NOVORA LINK — lista de trabajo

## Task 1: Reparar referencias de pruebas críticas — completada

**Description:** Actualizar las referencias de `NOVORA.ExInRuntime.Tests` y `NOVORA.LinkEngine.Tests` al árbol renombrado, sin cambiar comportamiento de producción.

**Acceptance criteria:**
- [x] Ambas suites resuelven los proyectos y fuentes existentes.
- [x] No quedan rutas `src/NOVORA` en estos dos proyectos.

**Verification:**
- [x] Tests pass: `dotnet run --project tests/NOVORA.ExInRuntime.Tests/NOVORA.ExInRuntime.Tests.csproj -c Release -p:UseSharedCompilation=false` (22/22)
- [x] Tests pass: `dotnet run --project tests/NOVORA.LinkEngine.Tests/NOVORA.LinkEngine.Tests.csproj -c Release -p:UseSharedCompilation=false`

**Dependencies:** None

**Files likely touched:**
- `tests/NOVORA.ExInRuntime.Tests/NOVORA.ExInRuntime.Tests.csproj`
- `tests/NOVORA.LinkEngine.Tests/NOVORA.LinkEngine.Tests.csproj`

**Estimated scope:** Small: 2 files

## Task 2: Reparar las restantes rutas de pruebas fuente-enlazada

**Description:** Migrar de forma agrupada las pruebas que incluyen fuentes de escritorio o Android mediante rutas anteriores al renombre.

**Acceptance criteria:**
- [ ] Cada `.csproj` afectado apunta a una fuente existente.
- [ ] Ninguna condición `Exists` oculta accidentalmente una fuente requerida.

**Verification:**
- [ ] Tests pass: ejecutar las suites afectadas individualmente.
- [ ] Build succeeds: `dotnet build NOVORA.sln -c Release -p:UseSharedCompilation=false`

**Progress (2026-10-27):**
- [x] `NOVORA.ExInOutput.Tests`: rutas migradas; 3/3 pruebas pasan al ejecutar la DLL con `dotnet`.
- [x] `NOVORA.ExInSession.Tests`: rutas migradas y compilación Release correcta; la ejecución está bloqueada por Control de aplicaciones local (`0x800711C7`).
- [x] `NOVORA.AndroidDownload.Tests`, `NOVORA.CaptureControl.Tests` y `NOVORA.CapturePolicy.Tests`: rutas migradas y compilación Release correcta.
- [x] `NOVORA.AndroidDownload.Tests`, `NOVORA.CaptureControl.Tests` y `NOVORA.CapturePolicy.Tests`: pruebas pasan; CapturePolicy 18/18.
- [x] `NOVORA.DynamicAddress.Tests`, `NOVORA.ExInHid.Tests` y `NOVORA.FileProtocol.Tests`: rutas migradas y pruebas pasan; FILES v2 completa con un caso omitido por falta de enlaces simbólicos de directorio.
- [x] `NOVORA.FileProtocol.Benchmark`: ruta migrada y compilación iniciada; su resultado final requiere una verificación aislada.

**Dependencies:** Task 1

**Files likely touched:**
- `tests/NOVORA.*.Tests/*.csproj` (lotes de hasta cinco por cambio)

**Estimated scope:** Medium: 3-5 files por incremento

## Task 3: Migrar rutas activas de CI y release

**Description:** Alinear los workflows con los proyectos renombrados para que CI pruebe los artefactos reales.

**Acceptance criteria:**
- [ ] CI, CodeQL y release apuntan a los `.csproj` y artefactos actuales.
- [ ] La configuración Dependabot del relay sigue resolviendo su manifiesto.

**Verification:**
- [ ] Manual check: buscar referencias activas a las rutas antiguas.
- [ ] Build succeeds: validar comandos de workflow localmente cuando sean aplicables.

**Dependencies:** Task 2

**Files likely touched:**
- `.github/workflows/ci.yml`
- `.github/workflows/codeql.yml`
- `.github/workflows/securitycodescan.yml`
- `.github/workflows/release.yml`
- `.github/dependabot.yml`

**Estimated scope:** Medium: 5 files

## Checkpoint: Rutas recuperadas

- [ ] Todas las pruebas prioritarias compilan.
- [ ] La solución compila en Release.
- [ ] Un revisor confirma que no se modificó lógica de producto al reparar rutas.

## Task 4: Probar que el relay iniciado es el relay propio

**Description:** Sustituir la detección basada sólo en puerto por una confirmación de preparación autenticada y ligada al proceso creado.

**Acceptance criteria:**
- [ ] Un listener externo en el puerto DATA no completa el arranque.
- [ ] El relay recién iniciado demuestra identidad antes de aceptar sesiones.
- [ ] Timeout y limpieza preservan el diagnóstico sin exponer el token.

**Verification:**
- [ ] Tests pass: prueba nueva de port squatting y arranque normal.
- [ ] Tests pass: `cargo test --release --locked --manifest-path "src/NOVORA LINK/LinkEngine/Network/Native/NovoraRelay/Cargo.toml"`

**Dependencies:** Task 1

**Files likely touched:**
- `src/NOVORA LINK/LinkEngine/Network/LENetworkRelay.cs`
- `src/NOVORA LINK/LinkEngine/Network/Native/NovoraRelay/src/main.rs`
- `tests/NOVORA.LinkEngine.Tests/Program.cs`

**Estimated scope:** Medium: 3 files

## Task 5: Reparar fallback del ejecutable LinkEngine

**Description:** Actualizar la ruta de desarrollo del relay para el directorio renombrado y cubrir su resolución.

**Acceptance criteria:**
- [ ] La resolución encuentra el relay desde artefactos y desde el checkout actual.
- [ ] Un error de ruta identifica el candidato intentado sin revelar secretos.

**Verification:**
- [ ] Tests pass: prueba de resolución con árbol temporal.
- [ ] Build succeeds: `dotnet build NOVORA.sln -c Release -p:UseSharedCompilation=false`

**Dependencies:** Task 4

**Files likely touched:**
- `src/NOVORA LINK/LinkEngine/Network/LENetworkRelay.cs`
- `tests/NOVORA.LinkEngine.Tests/Program.cs`

**Estimated scope:** Small: 2 files

## Task 6: Restaurar cobertura de recuperación LinkEngine

**Description:** Ejecutar y completar la suite recuperada para conexión, pairing, archivos, relay y recuperación.

**Acceptance criteria:**
- [ ] La suite .NET ejecuta todas sus pruebas sin omisiones.
- [ ] El relay Rust conserva sus 29 pruebas verdes.

**Verification:**
- [ ] Tests pass: suite LinkEngine .NET y `cargo test --release --locked`.
- [ ] Manual check: ciclo inicio–detención–reinicio sin procesos residuales.

**Dependencies:** Tasks 3-5

**Files likely touched:**
- `tests/NOVORA.LinkEngine.Tests/Program.cs`
- `tests/NOVORA.LinkEngine.Tests/LESessionMeasurementTests.cs`

**Estimated scope:** Small: 2 files

## Checkpoint: LinkEngine seguro

- [ ] Relay propio verificado antes de usar DATA.
- [ ] Tests .NET y Rust pasan.
- [ ] Revisión PC–Android pendiente de dispositivo físico queda documentada.

## Task 7: Preservar perfiles ExIn por identidad — completada

**Description:** Eliminar la adopción automática y destructiva de perfiles por modelo; conservar perfiles existentes sin reasignarlos.

**Acceptance criteria:**
- [x] Con dos mandos iguales, desconectar uno no transfiere ni borra sus archivos.
- [x] El mando sin perfil inicia sin calibración ajena.

**Verification:**
- [x] Tests pass: regresión de `ExInProfileStore` con dos perfiles del mismo modelo.
- [x] Tests pass: suite ExIn Runtime (23/23).

**Dependencies:** Task 1

**Files likely touched:**
- `src/NOVORA LINK/ExInEngine/ExInProfileStore.cs`
- `tests/NOVORA.ExInRuntime.Tests/Program.cs`

**Estimated scope:** Small: 2 files

## Task 8: Medir y ordenar telemetría de calibración ExIn

**Description:** Añadir pruebas e instrumentación mínima para secuencia, orden y cadencia observada durante calibración.

**Acceptance criteria:**
- [ ] Muestras duplicadas o fuera de orden no repintan la interfaz.
- [ ] La telemetría informa la cadencia observada sin presentarla como FPS de vídeo.
- [ ] Cambiar el monitor VE no altera identidad ni perfil del mando.

**Verification:**
- [ ] Tests pass: pruebas deterministas de secuencia y reloj controlado.
- [ ] Manual check: mando y dos monitores, con registro de Hz y dispositivo activo.

**Dependencies:** Tasks 1 and 7

**Files likely touched:**
- `src/NOVORA LINK/ExInEngine/ExInManager.cs`
- `src/LINK Control/AndroidUI/NLAndroidUIControllerLiveView.cs`
- `tests/NOVORA.ExInRuntime.Tests/Program.cs`

**Estimated scope:** Medium: 3 files

## Task 9: Especificar Dark accesible

**Description:** Documentar tokens, estados y objetivos de contraste para el tema oscuro de LINK Control.

**Acceptance criteria:**
- [ ] La especificación define azul, texto, botón primario/secundario y deshabilitado.
- [ ] Incluye estados normal, presionado y foco.

**Verification:**
- [ ] Manual check: revisión y aprobación humana de `SPEC-dark-accessibility.md`.

**Dependencies:** None

**Files likely touched:**
- `SPEC-dark-accessibility.md`

**Estimated scope:** Small: 1 file

## Task 10: Aplicar contraste Dark en LINK Control

**Description:** Implementar los tokens aprobados en paleta, botones y vistas de control/calibración.

**Acceptance criteria:**
- [ ] Textos y botones son legibles en tema Dark.
- [ ] El azul de interacción conserva contraste distinguible.
- [ ] Estados deshabilitados siguen siendo diferenciables.

**Verification:**
- [ ] Build succeeds: compilación Android Release.
- [ ] Manual check: revisión visual de pantallas principales y ExIn en Dark.

**Dependencies:** Task 9

**Files likely touched:**
- `src/LINK Control/AndroidUI/NLAndroidUITheme.cs`
- `src/LINK Control/AndroidUI/NLAndroidUIVisual.cs`
- `src/LINK Control/AndroidUI/NLAndroidUIControllerLiveView.cs`
- `src/LINK Control/AndroidUI/NLAndroidUITriggerCalibrationView.cs`

**Estimated scope:** Medium: 4 files

## Task 11: Aislar el retema XAML existente

**Description:** Revisar los cambios no confirmados de WPF, decidir su alcance y validarlos sin mezclarlos con Dark Android.

**Acceptance criteria:**
- [ ] El propietario confirma si pertenecen a este proyecto de cambio.
- [ ] Se conserva un diff independiente y verificable.

**Verification:**
- [ ] Build succeeds: proyecto de escritorio Release.
- [ ] Manual check: revisión visual WPF.

**Dependencies:** None

**Files likely touched:**
- `src/NOVORA LINK/Resource/NLResourceMainStyles.xaml`
- `src/NOVORA LINK/UI/NLUIWindowMain.xaml`

**Estimated scope:** Small: 2 files

## Checkpoint: Cierre

- [ ] Todas las pruebas afectadas pasan.
- [ ] Build de escritorio y Android pasa.
- [ ] Prueba física PC–Android distingue USB, LAN, vídeo, audio, relay e input.
- [ ] Revisión humana antes de publicación.

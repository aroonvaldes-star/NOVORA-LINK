# Compatibilidad de arranque, sesión y orden de motores

Fecha: 2026-11-24. Alcance: verificar que las adaptaciones de la rama
`linkcontrol-android-adaptaciones` (y su equivalente Linux
`linkcontrol-linux-adaptaciones`) no desactualizan ni revierten el comportamiento
ya existente, y que encajan con el código actual de `LINK-CONTROL`
(`novora-appcontrol`, HEAD `207f132`).

## 1. Orden real de arranque en el cliente Linux

| Paso | Acción | Evidencia |
| --- | --- | --- |
| 1 | `Program.Main` configura Avalonia y entra en el ciclo de escritorio. | `src/Linux/Program.cs:8-13` |
| 2 | `App.OnFrameworkInitializationCompleted` crea `MainWindow` y, solo en la aplicación real, llama a `StartAutomaticConnection()`. Las pruebas de UI construyen ventanas sin este paso. | `src/Linux/App.cs:20-28` |
| 3 | El constructor de `MainWindow` carga tema, inicializa textos de estado, suscribe eventos del host, arranca el temporizador, llama a `InitializeVisionExchangeVE()` y lanza la consulta ADB. | `src/Linux/MainWindow.cs:95-166` |
| 4 | `StartAutomaticConnection()` delega en `LinuxControlHost.StartAutomaticConnection()`: arranca `AdbDeviceTracker` y `LinuxAutoConnector`. El propio código indica que los motores siguen iniciándose solo bajo petición. | `src/Linux/MainWindow.cs:1146-1157`, `src/Linux/Control/LinuxControlHost.cs:82-96` |
| 5 | Ruta LAN: `StartLanCore` crea `LinuxLanLinkEngine`, `LinuxVisionLanEngine` y `LinuxControlServer`, les asigna `LinkMeasurements` y la configuración de VisionEngine, y levanta el servidor y el descubridor. Ruta USB: `StartUsbCoreAsync` crea `LinuxLinkEngine`, `LinuxVisionEngine`, el servidor local y prepara los mapeos inversos ADB. | `src/Linux/Control/LinuxControlHost.cs:195-223`, `:306-342` |
| 6 | Una vez autorizada la sesión, `LinuxVisionEngine.StartAsync` valida límites, prepara cuatro transportes, crea `LinuxVisionRuntime(Settings)` y lanza `RunSessionAsync`. | `src/Linux/VisionEngine/LinuxVisionEngine.cs:93-148` |
| 7 | El decodificador llama a `LinuxVisionAcceleration.SelectBackend(settings)` en el momento de construirse; ahí se resuelven las opciones del perfil y se aplica la degradación a software si el hardware no está disponible. | `src/Linux/VisionEngine/LinuxFfmpegVideoDecoder.cs:26-28`, `src/Linux/VisionEngine/LinuxVisionAcceleration.cs:53-81` |
| 8 | El monitor de streaming **no** arranca con la aplicación: `NLStreamingService` se crea como campo, pero `StartAsync` solo se ejecuta desde el botón de Streaming (RTMP u OBS). El constructor no abre proceso ni socket. | `src/Desktop/Linux/UI/MainWindow.Streaming.cs:16,305`, `src/Desktop/Windows/UI/NLUIWindowMainStreaming.cs:13,122`, `src/Desktop/Streaming/NLStreamingService.cs:124-176` |

La tarjeta de LinkEngine del Home es solo presentación/navegación: prepara y
muestra el estado de la ruta, mientras que la página LinkEngine conserva el
detalle técnico y las mediciones.`src/Desktop/Linux/UI/MainWindow.Home.cs:65,95-96`.

## 2. Orden real de arranque en Android (código actual contrastado)

| Paso | Acción | Evidencia |
| --- | --- | --- |
| 1 | `OnCreate` monta la shell, las páginas y la sección de ajustes, incluida la medición de LinkEngine. | `/tmp/opencode/LINK-CONTROL/src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs:122-187` |
| 2 | `OnStart` enlaza `NLAndroidServiceControl`, suscribe `Session.Changed`/`StatusChanged`, renderiza el estado y lanza resolución de ruta, consentido VPN y proyección de vídeo pendientes. | ibídem `:1120-1152` |
| 3 | `OnResume` vuelve a renderizar el monitor de LinkEngine y reanuda el bootstrap USB pendiente. | ibídem `:1105-1113` |
| 4 | `ApplyState` consume `snapshot.Profiles` y `snapshot.Acceleration` como texto de estado; los motores se inician por acción explícita del usuario (`ToggleLinkEngineAsync`, consentimiento VPN). | ibídem `:2012-2029`, `:2329`, `:2521-2558` |

El contrato de cableado no cambió: `NLControlAcceleration` y `NLControlSnapshot`
tienen la misma forma en la copia adaptada y en `LINK-CONTROL`
(`src/Shared/Control/NLControlProtocol.cs:16-17,56-59` frente a
`src/NOVORA/Control/NLControlProtocol.cs:21-22,57-62`). Por tanto, las etiquetas
nuevas (`Automático · QSV`, `Personalizado · ...`) son solo texto mostrado y no
rompen deserialización ni obligan a tocar la APK actual.

## 3. Compatibilidad de lo añadido

- **Aceleración.** `NLAccelerationProfile.OptionsFor` devuelve una o dos opciones
  por perfil; `Select` eligió hardware solo cuando el perfil lo permite y existe
  backend implementado. Se reforzó el caso `Custom` sin perfil válido: ahora
  devuelve software en lugar de reutilizar la opción automática. El modo
  eliminado `Forced` no aparece en el enum ni en las etiquetas. Las pruebas
  recorren 3 plataformas × 5 proveedores × 6 perfiles × 3 modos y comprueban que
  la selección siempre está dentro de las opciones ofrecidas.
- **Streaming/OBS.** El monitor usa cuatro Señales verificables (FPS, bitrate,
  duración y salud), `—` cuando no hay dato y `Sin señal del codificador` tras 4 s
  sin progreso. No sustituye el pipeline de OBS: usa WebSocket v5 para control y
  monitorización. No se inicia solo, por lo que no puede competir con la sesión
  USB/LAN ni con el arranque de VisionEngine.
- **LinkEngine en Home.** No cambia quién crea ni quién inicia el motor; solo
  mueve la tarjeta de estado y navegación. Las mediciones y el control siguen en
  la página LinkEngine y en `LinuxControlHost.LinkMeasurements`.
- **Sin revertir.** El contraste con `LINK-CONTROL` mostró divergencias reales
  (por ejemplo `NLControlCapturePolicy.cs`), por lo que no se hicieron
  cherry-picks ni se bajó la fuente a `1.4.113`. `main` permanece intacta en
  ambos repositorios.

## 4. Verificación ejecutada

- `dotnet build src/Linux/NOVORA.Linux.csproj -c Release`: **0 errores, 0
  advertencias**.
- `DOTNET_ROLL_FORWARD=Major dotnet run --project src/Linux/tests/NOVORA.Linux.ProtocolTests.csproj -c Release`:
  la parte de aceleración pasa (`VisionEngine acceleration selection checks
  passed.`). La suite continúa y falla después en `VisionExchangeChecks.cs:214`
  con `FileNotFoundException` para `Imagenes/a (1).png`. Ese fichero de prueba y
  `LinuxNovoraFolder` no fueron modificados por esta rama (`git diff` cubre solo
  el proyecto de pruebas, las comprobaciones de aceleración y dos copias del
  contrato compartido), así que se registra como fallo preexistente del entorno
  y no como regresión de las adaptaciones.
- Las rutas relativas del proyecto de pruebas se corrigieron de `../Android` y
  `../LinuxShared` a `../../Android` y `../../LinuxShared`, necesarias para que
  la suite compile desde `src/Linux/tests`.

## 5. Conclusión

El orden de arranque, el establecimiento de sesión y el inicio de motores se
mantienen: la conexión automática solo prepara rutas; VisionEngine decide el
backend al construir el decodificador dentro de una sesión autorizada; LinkEngine
sigue ligado a la ruta USB/LAN y a la medición de esa sesión; y el monitor de
streaming requiere una acción explícita. Lo añadido es compatible con el código
actual de `LINK-CONTROL` sin tocar su repositorio ni su versión. Queda pendiente
únicamente la validación física (GPU, RTMP/OBS real y teléfono) y la decisión de
publicar una APK oficial, que exige versión `1.4.129` y firma autorizada.

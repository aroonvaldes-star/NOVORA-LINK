# LinkEngine LAN en Linux — 2026-11-05

## Alcance

Primer incremento de paridad de LinkEngine (LE) en `src/NOVORA-LINUX`: habilitar
la ruta LAN/Wi-Fi usando el mismo contrato que Windows y LINK Control, sin tocar
`src/NOVORA LINK` ni `src/LINK Control`. No se instaló nada en el teléfono, no se
hizo commit ni push y no se modificaron reglas de confianza ni certificados.

## Contrato observado

- Android envía `startLink` por la sesión de control ya autorizada (TLS LAN o USB).
- Windows responde por USB con `{Transport:"USB",Token}` y Android abre CONTROL
  27183 y DATA 27184 mediante ADB reverse.
- Windows responde por LAN con `{Host,Port,Fingerprint,Token,Transport:"LAN"}`.
  Android abre un TLS fijado a `Fingerprint`, envía `LinkHello{Version,Token,Channel:"DATA"}`
  y el PC puentea ese canal al relay local 27184 tras autenticar `NOVORA-LINK|2|DATA|<token>`.
- El relay Linux siempre escucha en `127.0.0.1:27184` y solo reemplaza la sesión
  activa después de validar el token. Por tanto la ruta LAN no necesita ADB y
  reutiliza el mismo binario nativo.

## Cambios

- `Control/LinuxLinkProtocol.cs` (nuevo): framing CONTROL y autenticación DATA
  compartidos por USB y LAN.
- `Control/ILinuxLinkEngine.cs` (nuevo): superficie común de los runtimes LE.
- `Control/LinuxLinkRelay.cs` (nuevo): ciclo de vida del relay nativo, `LE_READY`,
  diagnóstico de stderr, detección de salida inesperada y exclusión de proceso
  para que USB y LAN no puedan poseer a la vez el puerto 27184; la segunda ruta
  falla con un mensaje explícito y el gate se libera al detener o al fallar el
  arranque.
- `Control/LinuxLanLinkGateway.cs` (nuevo): listener TLS efímero en la IP privada
  autorizada, certificado del trust store, token de un solo uso, límite de
  handshakes/conexiones por par, autenticación DATA contra 27184 y puente
  bidireccional. Una sesión activa; Android puede renovarla.
- `Control/LinuxLanLinkEngine.cs` (nuevo): arranca relay + gateway, publica
  `Waiting/Connected/Error/Stopped` y devuelve la oferta LAN.
- `Control/LinuxLinkEngine.cs`: implementa la interfaz común, reutiliza el relay
  y devuelve la oferta USB serializada.
- `Control/LinuxControlServer.cs`: `startLink`/`stopLink` aceptan la ruta de la
  sesión activa (USB o LAN), el snapshot publica `LinkCanStart/Stop/Running` en
  ambas y el cierre de la sesión de control detiene LE.
- `Control/LinuxControlHost.cs`: crea y libera el runtime LAN, elige el runtime
  activo para la UI y conserva el ciclo de vida USB existente.
- `MainWindow.cs`: textos de la página LE y de Integración reflejan USB + LAN;
  siguen pendientes telemetría y speedtest.
- `tests/Program.cs`: pruebas de gateway (token inválido, autenticación DATA,
  identidad del relay y puente bidireccional), de routing `startLink`/`stopLink`
  por LAN y modo `--le-lan-only`.
- `README.md`: documenta la ruta LAN y el comando de prueba focalizado.

## Verificación

- `dotnet build src/NOVORA-LINUX/NOVORA.Linux.csproj -c Release`: 0 advertencias,
  0 errores.
- `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release -- --le-lan-only`:
  PASS.
- Suite completa Linux vía `dotnet run`: PASS, incluido FFmpeg H.264 con rotación,
  SDL `opengl` 64×64 y las nuevas pruebas LE LAN.
- Una ejecución directa del DLL de pruebas sufrió el timeout intermitente ya
  documentado del smoke FFmpeg; las ejecuciones posteriores vía `dotnet run`
  pasaron. No se atribuye a este cambio.

## Limitaciones

- No hay validación física USB ni LAN: no se confirmó el flujo completo con LINK
  Control en un teléfono real.
- No se implementaron todavía telemetría de sesión, prueba de velocidad,
  recuperación automática ni diagnósticos equivalentes a Windows.
- La ruta LAN es IPv4 privada, sin IPv6, y depende de que el firewall permita el
  puerto TLS efímero. Solo hay un túnel LAN activo por sesión de control.
- El relay nativo sigue derivando de Gnirehtet; se conservan sus avisos de licencia.

## Coordinación

Mientras se trabajaba este incremento, Copilot CLI aplicó cambios concurrentes en
`MainWindow.cs` y VisionEngine. No se revirtieron ni se mezclaron con este alcance;
una compilación intermedia falló por un parche en vuelo y volvió a pasar al
completarse. La validación final se hizo sobre el árbol combinado.

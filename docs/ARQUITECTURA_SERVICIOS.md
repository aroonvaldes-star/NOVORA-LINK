# Arquitectura de servicios NOVORA-LINK 1.4 A3

## LinkEngine

```text
Android VpnService
      │
      ├─ CONTROL tcp:27183 ──► ManagerTransportLE
      │                         └─ SessionChangedLE ──► MonitorRecoveryLE
      │
      └─ DATA tcp:27184 ─────► RelayCore / TrafficEngine ──► Internet Windows
```

USB mantiene `27183/27184` sobre ADB reverse. En una sesión LAN autorizada,
Android recibe una oferta DATA efímera, fija el certificado TLS de la PC y
protege ese socket con `VpnService.Protect()`. La PC autentica el token de una
sola sesión y puentea únicamente DATA hacia `27184` en loopback; RelayCore no se
expone directamente a la red local y no se crea otra instancia de ADB.

`ManagerNetworkLE` administra lifecycle por acciones/eventos; ya no ejecuta mantenimiento periódico. `RelayNetworkLE` publica `ExitedLE` cuando el proceso termina. `MonitorRecoveryLE` confirma fallos mediante deadlines y sólo entra a Recovery después de persistencia real.

## VisionEngine

```text
scrcpy-server / Android
      │
      ▼
TransportVE
      ├─ Video ─► Decoder ─► Renderer D3D11
      ├─ Audio ─► Output Windows
      └─ Control adapter ─► comandos necesarios para la sesión visual
```

VE no posee archivos, portapapeles, Drag & Drop, apps, notificaciones ni gamepad. La grabación consume las salidas de video/audio de VE, pero no convierte esas funciones generales en parte de VE.

## ExInEngine e Integraciones

```text
ExInEngine ─► mouse / teclado / tactil / gamepad

NLIntegrationRuntime
      ├─ archivos / Share / Drag & Drop
      ├─ portapapeles por eventos
      ├─ apps / notificaciones / display
      └─ capacidades de camara y microfono cuando exista backend real
                         │
                         └─ politica de privacidad compartida
```

`NLIntegrationRuntime` vive a nivel de NOVORA y no dentro de `VECoreRuntime`. Archivos y Drag & Drop pueden operar con un teléfono conectado aunque VE esté detenido. El portapapeles y algunos comandos reutilizan por ahora el adaptador de control disponible, sin adquirir el lifecycle de video. Gamepad usa eventos SDL3 desde ExInEngine.

## RemoteNV

```text
Android MainActivity
      │  adb reverse tcp:27182
      ▼
ServerRemoteNV (Loopback)
      │
      └─ HELLO v2 + token efímero de sesión
```

RemoteNV puede solicitar Start/Stop/Status de VisionEngine y LinkEngine. El token se genera en Windows para el dispositivo seleccionado y se entrega al cliente Android mediante ADB; no se persiste.

## Respaldo local y cliente Android

Consultar [base local y APK 1.4.A4](BASE-LOCAL-Y-APK.md) para la política de respaldo, identidad verificada y diferencias entre compilaciones.

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

`ManagerNetworkLE` administra lifecycle por acciones/eventos; ya no ejecuta mantenimiento periódico. `RelayNetworkLE` publica `ExitedLE` cuando el proceso termina. `MonitorRecoveryLE` confirma fallos mediante deadlines y sólo entra a Recovery después de persistencia real.

## VisionEngine

```text
scrcpy-server / Android
      │
      ▼
TransportVE
      ├─ Video ─► Decoder ─► Renderer D3D11
      ├─ Audio ─► Output Windows
      └─ Control ─► mouse / teclado / gamepad / clipboard
                         │
                         ├─ PrivacyVE
                         └─ IntegrationVE
```

Gamepad usa eventos SDL3. PrivacyVE gobierna exposición/control/intercambio sensible. IntegrationVE gobierna las capacidades activadas desde Settings.

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

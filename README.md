# NOVORA-LINK 1.4 A3

NOVORA-LINK integra Android con Windows mediante dos motores separados:

- **LinkEngine (LE):** Internet PC→Android, VPN/reverse tethering, CONTROL/DATA, RelayCore, TrafficEngine y Recovery.
- **VisionEngine (VE):** video, audio, control, gamepad, clipboard, archivos, privacidad e integración Android↔Windows.

El cliente Android está en `NOVORA.linkEngine.Android/` y el escritorio WPF en `src/NOVORA/`.

## Estado de esta base

Esta carpeta corresponde a la base de fuentes reorganizada después de la integración 1.4 A3:

- LinkEngine sin los loops periódicos legacy de Network/Runtime/Recovery.
- Recovery reaccionando a eventos de `ManagerTransportLE`.
- Remote Android autenticado con token efímero por sesión (protocolo v2).
- Privacy/Integration/Gamepad/NVIDIA/Remote conectados a `SettingsWindow` y visibles desde `MainWindow`.
- Gamepad basado en eventos SDL3.
- VisionEngine conserva `scrcpy-server` como backend Android compatible mientras NOVORA controla transporte, decoder, renderer, audio y control.
- RelayCore conserva el origen/licencias correspondientes a Gnirehtet donde aplica y evoluciona hacia el Data Plane propio de LinkEngine.

## Estructura rápida

```text
NOVORA-LINK/
├─ NOVORA.sln
├─ src/NOVORA/
│  ├─ LinkEngine/
│  ├─ VisionEngine/
│  ├─ Remote/
│  ├─ Services/
│  ├─ ViewModels/
│  └─ Tools/                 # herramientas de ejecución incluidas en el respaldo
├─ NOVORA.linkEngine.Android/
├─ tests/
├─ scripts/
├─ docs/
├─ third_party/
└─ Installer/
```

## Preparación y compilación en Windows

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-Tools.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-NOVORA-Repository.ps1
```

`Verify-NOVORA-Repository.ps1` ejecuta Cargo sobre RelayCore y compila Windows + Android en Release.

## Reglas importantes

- **NO POLLING BY DEFAULT.** Eventos/callbacks/readiness antes que sondeos periódicos.
- Congestión, `WouldBlock` y backpressure pertenecen a TrafficEngine, no a Recovery.
- LinkEngine admite hasta **5 sesiones activas simultáneas dinámicas**.
- No persistir tokens de RemoteNV ni información personal del usuario.
- Código de motores: nomenclatura `Función + Carpeta + Motor` (`MonitorRecoveryLE`, `StatusCoreVE`, etc.).

Consulta `docs/ARCHITECTURE-RULES.md` y `docs/INTEGRATION-STATUS-1.4-A3.md`.

## Dependencias y licencias

Las herramientas de ejecución se incluyen en este respaldo local; los scripts permiten restaurar las dependencias externas.

---

NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.

## Respaldo local y cliente Android

Consultar [base local y APK 1.4.A4](docs/BASE-LOCAL-Y-APK.md) para la política de respaldo, identidad verificada y diferencias entre compilaciones.

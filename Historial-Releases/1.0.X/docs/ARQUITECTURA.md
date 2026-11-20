# Arquitectura NOVORA

La base del Launcher usa WPF + C# y mantiene las herramientas externas fuera de la UI.

```text
NOVORA/
├── src/NOVORA.Launcher/   # UI y lógica del Launcher
├── Tools/Runtime/         # binarios reales usados en ejecución
├── Scripts/               # automatizaciones auxiliares
├── Installer/             # instalador
└── docs/                  # documentación y respaldos
```

## Principios
- Las rutas se resuelven desde `AppContext.BaseDirectory`.
- ADB, scrcpy y Gnirehtet se ejecutan con rutas absolutas del bundle.
- No se depende del PATH de Windows.
- La UI no construye comandos shell concatenando texto del usuario.
- Las capacidades del dispositivo se consultan por ADB antes de elegir calidad.
- El monitor de salida se detecta desde Windows y puede seleccionarse.
- Si una capacidad no puede verificarse, NOVORA usa un fallback conservador en vez de inventar datos.

# Estructura actual de NOVORA-LINK

```text
NOVORA-LINK/
├── NOVORA.sln
├── src/NOVORA/
│   ├── LinkEngine/
│   │   ├── Core/
│   │   ├── Device/
│   │   ├── Failover/
│   │   ├── Metrics/
│   │   ├── Network/
│   │   │   └── Native/RelayCore/    # Data Plane Rust
│   │   ├── Protocol/
│   │   ├── Recovery/
│   │   ├── Runtime/
│   │   ├── Traffic/
│   │   └── Transport/
│   ├── VisionEngine/
│   │   ├── Audio/
│   │   ├── Control/
│   │   ├── Core/
│   │   ├── Device/
│   │   ├── Events/
│   │   ├── Exchange/
│   │   ├── Gamepad/
│   │   ├── Integration/
│   │   ├── Metrics/
│   │   ├── Nvidia/
│   │   ├── Performance/
│   │   ├── Privacy/
│   │   ├── Protocol/
│   │   ├── Recovery/
│   │   ├── Renderer/
│   │   ├── Server/
│   │   ├── Streaming/
│   │   ├── Stress/
│   │   ├── Transport/
│   │   └── Video/
│   ├── STEngine/
│   │   └── Core/
│   ├── Remote/
│   ├── Models/
│   ├── Services/
│   ├── ViewModels/
│   ├── MainWindow*.cs / MainWindow.xaml
│   └── SettingsWindow.xaml(.cs)
├── NOVORA.linkEngine.Android/
│   ├── LinkEngine/
│   │   ├── Network/
│   │   ├── Protocol/
│   │   └── Transport/
│   ├── Remote/
│   └── MainActivity.cs
├── scripts/
├── tests/
├── docs/
├── third_party/
└── Installer/
```

Los directorios `.vs`, `bin`, `obj`, `target`, backups y diagnósticos generados no forman parte del repositorio final.

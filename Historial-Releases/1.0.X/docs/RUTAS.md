# Rutas

El Launcher calcula su raíz desde `AppContext.BaseDirectory`.

```text
<Launcher>
└── Tools
    └── Runtime
        ├── adb.exe
        ├── AdbWinApi.dll
        ├── AdbWinUsbApi.dll
        ├── scrcpy.exe
        ├── scrcpy-server
        ├── SDL3.dll
        ├── avcodec-62.dll
        ├── avformat-62.dll
        ├── avutil-60.dll
        ├── swresample-6.dll
        ├── gnirehtet.exe
        ├── gnirehtet.apk
        └── libusb-1.0.dll
```

Los scripts interactivos antiguos no se distribuyen en Runtime; sus originales están archivados en `docs/original-scripts/`.

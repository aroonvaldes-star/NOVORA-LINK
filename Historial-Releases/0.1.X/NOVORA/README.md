# NOVORA V1

NOVORA es una herramienta Windows para controlar un teléfono Android por USB:

- `scrcpy` para duplicar la pantalla;
- `gnirehtet` para compartir la conexión del PC con el teléfono mediante una VPN local de Android (reverse tethering).

No es una VPN de privacidad, anonimato ni una protección adicional del tráfico.

## Inicio

En PowerShell, desde esta carpeta:

```powershell
.\NOVORA.bat status
.\NOVORA.bat devices
.\NOVORA.bat start -Profile low
.\NOVORA.bat mirror -Profile low
.\NOVORA.bat tether
.\NOVORA.bat stop
```

Con más de un teléfono, agrega `-Serial <id>` a `mirror` o `tether`.
El teléfono debe tener la depuración USB activa y debe aceptar la huella RSA de ADB.

## Perfiles de mirroring

| Perfil | Resolución máxima | FPS | Bitrate | Audio |
| --- | ---: | ---: | ---: | --- |
| `low` (predeterminado) | 1024 px | 30 | 4 Mbps | Desactivado |
| `balanced` | 1280 px | 45 | 8 Mbps | Desactivado |
| `quality` | 1920 px | 60 | 12 Mbps | Activado |

Usa `low` para teléfonos de baja gama. Sube de perfil sólo si las pruebas reales no muestran calentamiento, retraso ni cierres.

## Pruebas

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Smoke.Tests.ps1
```

CircleCI ejecuta esa misma comprobación en Windows. La conexión a un teléfono físico no puede validarse desde CI y es un requisito del checklist de lanzamiento.

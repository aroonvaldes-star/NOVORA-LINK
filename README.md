# NOVORA-LINK 1.3

NOVORA-LINK es una aplicación WPF para conectar, visualizar y controlar dispositivos Android desde Windows.

> **Versión estable actual:** NOVORA-LINK 1.3  
> **Próxima versión:** NOVORA-LINK 1.4  
> **Estado de 1.4:** En desarrollo  
> **Precio:** NOVORA-LINK 1.4 continuará siendo **GRATIS**.

## Versión estable

**NOVORA-LINK 1.3 es la versión pública y estable actual del repositorio.**

La rama pública se mantiene sobre 1.3 mientras NOVORA-LINK 1.4 continúa su desarrollo por separado. Esto evita publicar código experimental o incompleto antes de que la siguiente versión esté lista.

La solución estable principal está en:

- `NOVORA.sln`
- `src/NOVORA/NOVORA.csproj`

## NOVORA-LINK 1.3 vs 1.4

La versión 1.4 no es solamente un cambio visual. El trabajo actual busca trasladar funciones principales de NOVORA hacia motores desarrollados específicamente para el proyecto y reducir dependencias externas en el funcionamiento central.

| Área | NOVORA-LINK 1.3 | NOVORA-LINK 1.4 |
| --- | --- | --- |
| Estado | **Estable y disponible** | **En desarrollo** |
| Precio | Gratis | **Seguirá siendo gratis** |
| Conectividad Android ↔ Windows | ADB + integración de Gnirehtet | **LinkEngine propio en desarrollo** |
| Internet / reverse tethering | Gnirehtet 2.5.1 | Motor de red de LinkEngine en desarrollo |
| Visualización de pantalla | scrcpy 4.1 | **VisionEngine propio en desarrollo** |
| Video | Gestionado principalmente mediante scrcpy | Pipeline de video administrado por VisionEngine |
| Audio | Asociado al flujo de scrcpy | Audio integrado y administrado desde VisionEngine |
| Control | Mouse/teclado mediante la integración existente | Control integrado en VisionEngine; expansión a touch y gamepad dentro del desarrollo |
| Android | Depuración ADB y componentes de terceros según la función | **Componente/APK Android propio de NOVORA en desarrollo** |
| Recovery | Recuperación alrededor de procesos y sesiones existentes | Recovery separado por motor y por sesión |
| Métricas | Información básica de sesión y estado | Métricas específicas de conectividad, transporte y stream |
| Rendimiento | Arquitectura funcional de 1.3 | Trabajo orientado a eventos, procesamiento bajo demanda y **mínimo polling posible** |
| Arquitectura | NOVORA coordina herramientas y servicios | NOVORA evoluciona hacia LinkEngine + VisionEngine + componente Android |

### Importante sobre esta comparación

La columna **1.4** describe el trabajo que forma parte del desarrollo actual de la próxima versión. No significa que cada función esté finalizada, validada o lista para producción.

NOVORA-LINK 1.4 se publicará únicamente cuando sus componentes principales alcancen el nivel de estabilidad necesario para sustituir la base 1.3.

## Novedades de NOVORA-LINK 1.4

### LinkEngine

**En desarrollo.** Es el nuevo motor de conectividad de NOVORA-LINK.

El trabajo actual incluye:

- conectividad PC ↔ Android administrada por NOVORA;
- componente Android propio;
- transporte de control y datos;
- reverse tethering;
- gestión de tráfico TCP, UDP y DNS;
- sesiones independientes;
- estado real del túnel;
- recovery selectivo;
- métricas de tráfico y conectividad;
- reducción de llamadas y comprobaciones innecesarias;
- diseño preparado para ampliar el soporte de varios dispositivos sin convertir el motor en un ciclo constante de polling.

### VisionEngine

**En desarrollo.** Es el nuevo motor de visualización, audio y control.

El trabajo actual contempla:

- pipeline de video administrado por NOVORA;
- decoder y renderer de baja latencia;
- audio integrado;
- control mediante mouse y teclado;
- soporte de touch dentro de la evolución del motor;
- integración de gamepad;
- clipboard e intercambio entre Windows y Android;
- selección de monitor y salida de audio;
- modo ventana y pantalla completa;
- recovery independiente de LinkEngine;
- métricas del stream y control de rendimiento.

### Menos polling

Una regla central del desarrollo de 1.4 es:

> **Utilizar el mínimo polling posible.**

Cuando sea viable, NOVORA utilizará eventos, señales de estado y trabajo bajo demanda en lugar de consultar continuamente componentes que no han cambiado.

El objetivo no es afirmar que 1.4 tendrá "cero polling" en absolutamente todos los casos: heartbeats, timeouts o comprobaciones específicas pueden seguir siendo necesarios. La meta es evitar ciclos periódicos innecesarios y reducir trabajo redundante de CPU, ADB y red.

### Componente Android propio

NOVORA-LINK 1.4 está desarrollando su propio componente Android para integrarse directamente con los motores de NOVORA.

Su objetivo es concentrar las funciones Android necesarias para LinkEngine y la integración con Windows sin depender de aplicaciones externas como núcleo de la experiencia.

## Qué todavía NO estamos prometiendo

Para mantener una comunicación realista sobre 1.4:

- no existe una fecha pública de lanzamiento;
- las funciones que continúan en desarrollo pueden cambiar antes del release;
- no se publican cifras de rendimiento o latencia hasta tener pruebas repetibles;
- no afirmamos que toda dependencia externa haya desaparecido del ecosistema; ADB y bibliotecas necesarias pueden seguir formando parte de la solución según corresponda;
- la estabilidad de 1.3 sigue siendo la referencia pública hasta que 1.4 esté lista.

## NOVORA-LINK 1.4 seguirá siendo gratis

NOVORA-LINK nació como un proyecto gratuito y la actualización 1.4 continuará bajo esa misma decisión.

Las novedades principales anunciadas para 1.4 no se están desarrollando con la intención de convertir la actualización en una versión de pago.

## Estructura de NOVORA-LINK 1.3

- `src/NOVORA/` — aplicación WPF y servicios.
- `src/NOVORA/Tools/` — herramientas de ejecución restauradas por `scripts/Setup-Tools.ps1`.
- `Installer/` — instalador Inno Setup.
- `scripts/` — preparación reproducible de dependencias.
- `docs/` — arquitectura y estructura técnica.
- `.github/workflows/` — compilación y publicación de releases.

## Dependencias de ejecución de 1.3

Los binarios de terceros no se versionan dentro del repositorio. `scripts/Setup-Tools.ps1` descarga versiones fijadas y verifica SHA-256 antes de colocarlas en `src/NOVORA/Tools/`.

Versiones fijadas para 1.3:

- scrcpy 4.1 (Windows x64)
- Gnirehtet 2.5.1 (Rust, Windows x64)

## Compilar NOVORA-LINK 1.3

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Releases

La versión pública estable actual es **NOVORA-LINK 1.3**.

GitHub Actions publica el instalador correspondiente y su archivo SHA-256 para verificar integridad.

NOVORA-LINK 1.4 se anunciará como release cuando alcance la estabilidad requerida; hasta entonces, la información pública sobre 1.4 describe únicamente el desarrollo previsto y su progreso general, no código experimental.

## Créditos y licencias

NOVORA-LINK 1.3 utiliza proyectos de terceros como scrcpy, Gnirehtet y Android Debug Bridge de acuerdo con sus respectivas licencias.

Consulta `LICENSE`, `COPYRIGHT.md`, `ACKNOWLEDGEMENTS.md` y `THIRD-PARTY-NOTICES.md` para la información correspondiente a la versión pública.

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**

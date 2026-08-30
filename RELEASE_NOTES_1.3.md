# NOVORA-LINK 1.3

NOVORA-LINK 1.3 establece la nueva base estable del proyecto para conectar, visualizar y controlar dispositivos Android desde Windows con una arquitectura más limpia, menos rutas duplicadas y una distribución reproducible.

## Estado de la versión

- Versión: **1.3**
- Canal: **estable**
- Plataforma: **Windows x64**
- Aplicación: **WPF / .NET 8**
- Instalador: `NOVORA-Setup-1.3.exe`
- Integridad: `NOVORA-Setup-1.3.exe.sha256`

El instalador oficial se genera como publicación **self-contained**, por lo que incluye el runtime necesario de .NET para ejecutar esta compilación.

## Funciones principales

### Conexión Android por USB

NOVORA detecta dispositivos Android conectados mediante ADB por USB y permite seleccionarlos desde la interfaz principal. La identificación visible prioriza el nombre/modelo del dispositivo y el tipo de conexión, evitando mostrar seriales o direcciones IP como información principal.

### Conexión Android por Wi-Fi / ADB

NOVORA puede trabajar con dispositivos disponibles mediante ADB inalámbrico. La interfaz distingue entre conexiones **USB** y **Wi-Fi** para facilitar el diagnóstico del enlace activo.

### Visualización y control con scrcpy

Integra **scrcpy 4.1** para abrir una sesión de visualización y control del dispositivo Android desde Windows. NOVORA centraliza el arranque de scrcpy por dispositivo para reducir instancias duplicadas y estados ambiguos.

### Perfiles de salida

El sistema calcula perfiles de salida para adaptar la sesión a la pantalla y al dispositivo. La configuración contempla resolución, FPS y bitrate para buscar una salida equilibrada entre calidad, estabilidad y latencia.

### Control de bitrate

NOVORA incluye lógica dedicada para seleccionar y normalizar valores de bitrate de acuerdo con el perfil de salida. Esto permite ajustar el consumo de ancho de banda sin mezclar esa responsabilidad con la detección ADB o la interfaz.

### Detección de monitores

El servicio de monitores identifica pantallas disponibles en Windows y proporciona la información necesaria para adaptar la salida de NOVORA al entorno donde se está ejecutando.

### Reverse tethering con Gnirehtet

Integra **Gnirehtet 2.5.1** para proporcionar reverse tethering a dispositivos Android compatibles. La ejecución está centralizada para evitar múltiples caminos de inicio, recuperación y parada compitiendo entre sí.

### Recuperación y control de conexión

La arquitectura 1.3 reduce rutas de recuperación duplicadas y concentra el estado de conexión. El objetivo es evitar ciclos de reconexión contradictorios entre ADB, Gnirehtet y la interfaz.

### Panel RED

El panel **RED** concentra información relacionada con el enlace y el estado de conectividad del dispositivo. Forma parte de la ventana principal y reemplaza el framework de widgets modular anterior.

### Panel Rendimiento

El panel **Rendimiento** muestra métricas del dispositivo recopiladas por los servicios de NOVORA. Comparte el ciclo de actualización con el panel RED para evitar sondeos duplicados.

### Polling simplificado

La base 1.3 utiliza un ciclo de actualización compartido con intervalo de aproximadamente **30 segundos** para estados que no necesitan refresco continuo. Esto reduce consultas ADB repetidas y trabajo innecesario en la interfaz.

### Identidad y estado del dispositivo

La información del dispositivo está separada en servicios de identidad, estado y métricas. Esta división evita que una sola rutina ADB tenga que resolver nombre, conexión, rendimiento y estado al mismo tiempo.

### Configuración persistente

NOVORA conserva preferencias mediante un servicio dedicado de configuración. Esto permite mantener opciones de usuario sin acoplarlas directamente a `MainWindow`.

### Temas e interfaz

La versión 1.3 conserva soporte para temas y estilos centralizados en recursos WPF, separando presentación de lógica de conexión.

### Actualizaciones mediante GitHub Releases

`UpdateService` consulta las releases oficiales de **aroonvaldes-star/NOVORA-LINK** y utiliza instaladores `NOVORA-Setup-<versión>.exe` como formato de actualización. La release publica también un archivo SHA-256 para verificar la integridad del instalador.

## Arquitectura 1.3

La solución se reorganizó para que el código fuente real viva en:

- `NOVORA.sln`
- `src/NOVORA/`
- `src/NOVORA/Services/`
- `src/NOVORA/Models/`
- `src/NOVORA/ViewModels/`
- `Installer/`
- `scripts/`
- `docs/`

Se retiró la antigua carpeta `VS/` como raíz de desarrollo y se excluyen del repositorio `.vs/`, `bin/`, `obj/`, outputs de publicación y otros artefactos generados.

## Dependencias incluidas en la compilación

Las herramientas de terceros no se almacenan como binarios permanentes en Git. El proceso de build descarga versiones fijadas y verifica su integridad antes de publicar:

- **scrcpy 4.1 — Windows x64**
- **Gnirehtet 2.5.1 — Rust Windows x64**
- **ADB** incluido a través de las herramientas requeridas por la compilación

Los archivos restaurados dentro de `src/NOVORA/Tools/` se copian al output final de la aplicación antes de construir el instalador.

## Instalación

1. Descarga `NOVORA-Setup-1.3.exe` desde los Assets de esta Release.
2. Opcionalmente verifica el archivo con `NOVORA-Setup-1.3.exe.sha256`.
3. Ejecuta el instalador como administrador cuando Windows lo solicite.
4. El instalador crea NOVORA en `Program Files` y puede crear un acceso directo en el escritorio.
5. En Android activa **Opciones de desarrollador** y **Depuración USB** para conexiones ADB por cable.

## Cambios técnicos relevantes

- Nueva estructura de repositorio orientada a fuente limpia.
- Eliminación de código modular heredado que duplicaba responsabilidades.
- Eliminación del framework de widgets anterior; RED y Rendimiento quedan integrados directamente.
- Centralización de ADB, scrcpy y Gnirehtet.
- Menos polling y consultas duplicadas.
- Actualizador apuntando al repositorio correcto `NOVORA-LINK`.
- Pipeline CI para Windows/.NET 8.
- Pipeline de Release con instalador Inno Setup.
- Publicación reproducible de herramientas verificadas.
- Generación automática de SHA-256 para cada instalador.

## Validación

La base 1.3 fue compilada mediante GitHub Actions en Windows con .NET 8 después de restaurar sus herramientas verificadas. La validación incluye restore y build de la solución reorganizada.

## Créditos

NOVORA utiliza proyectos y componentes de terceros, entre ellos scrcpy, Gnirehtet y Android Debug Bridge. Consulta también:

- `LICENSE`
- `COPYRIGHT.md`
- `ACKNOWLEDGEMENTS.md`
- `THIRD-PARTY-NOTICES.md`

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**

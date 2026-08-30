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

## Instalación paso a paso

### 1. Descargar NOVORA

En la sección **Assets** de esta Release descarga:

- `NOVORA-Setup-1.3.exe`
- `NOVORA-Setup-1.3.exe.sha256` si deseas comprobar la integridad del instalador.

### 2. Verificar el SHA-256 opcionalmente

Desde PowerShell, dentro de la carpeta donde descargaste el instalador, puedes ejecutar:

```powershell
(Get-FileHash .\NOVORA-Setup-1.3.exe -Algorithm SHA256).Hash.ToLower()
```

El resultado debe coincidir con el contenido de `NOVORA-Setup-1.3.exe.sha256`.

### 3. Ejecutar el instalador

Abre `NOVORA-Setup-1.3.exe`.

Cuando Windows solicite permisos de administrador, acepta el aviso para continuar con la instalación.

NOVORA se instala de forma predeterminada en la carpeta de aplicaciones de Windows y el instalador permite crear opcionalmente un acceso directo en el escritorio.

### 4. Preparar el dispositivo Android

Antes de usar NOVORA con un teléfono o tablet Android:

1. Activa **Opciones de desarrollador** en Android.
2. Activa **Depuración USB**.
3. Conecta el dispositivo al PC mediante USB.
4. Si el dispositivo solicita autorización para la depuración ADB, autoriza el equipo.
5. Abre NOVORA.

### 5. Seleccionar el dispositivo

NOVORA detectará los dispositivos disponibles mediante ADB.

En **DISPOSITIVO ANDROID** selecciona el teléfono o tablet que deseas utilizar. NOVORA muestra el nombre/modelo del dispositivo y diferencia si la conexión está trabajando por **USB** o **Wi-Fi**.

El botón `↻` fuerza una nueva búsqueda de dispositivos.

### 6. Usar ADB por Wi-Fi opcionalmente

Para preparar una conexión inalámbrica:

1. Conecta primero el dispositivo por USB.
2. Selecciónalo en NOVORA.
3. Pulsa **Wi-Fi**.
4. Espera a que NOVORA confirme que ADB por Wi-Fi está conectado.
5. Después de la confirmación puedes retirar el cable USB si deseas continuar mediante ADB inalámbrico.

### 7. Seleccionar monitor y salida

En **MONITOR / SALIDA DE VIDEO** selecciona la pantalla donde deseas trabajar.

NOVORA calcula automáticamente el perfil de salida utilizando la configuración disponible de resolución, FPS, bitrate y tamaño máximo.

### 8. Iniciar o detener la visualización

Pulsa **▶ PLAY** para iniciar la sesión de scrcpy con el dispositivo seleccionado.

Cuando la sesión está activa, el botón cambia a **■ STOP** para detenerla de forma controlada.

## Uso de INTERNET USB — Gnirehtet

NOVORA integra **Gnirehtet 2.5.1** para proporcionar reverse tethering a un dispositivo Android mediante la conexión ADB disponible.

En la interfaz principal esta función aparece como **INTERNET USB**.

### Cómo iniciar Gnirehtet

1. Conecta y selecciona un dispositivo Android en NOVORA.
2. Comprueba que el dispositivo aparezca como conectado por USB o Wi-Fi.
3. Pulsa **INTERNET USB**.
4. NOVORA valida que `adb.exe`, `gnirehtet.exe` y `gnirehtet.apk` estén disponibles.
5. NOVORA inicia el relay de Gnirehtet.
6. Instala o prepara el cliente Gnirehtet en el dispositivo.
7. Inicia el cliente VPN/túnel para el dispositivo seleccionado.
8. El panel **RED** mostrará `Gnirehtet activo` cuando NOVORA mantenga el relay y el dispositivo asociado.

Cuando el proceso termina correctamente, NOVORA registra el estado como **Gnirehtet activo y túnel iniciado**.

### Cómo detener Gnirehtet

Con Gnirehtet activo, vuelve a pulsar **INTERNET USB**.

NOVORA enviará la orden de parada al dispositivo, cerrará el relay asociado y limpiará el estado de la sesión.

Al cerrar NOVORA, la aplicación también intenta detener Gnirehtet de forma controlada antes de finalizar.

## Recovery — recuperación de Gnirehtet

NOVORA-LINK 1.3 centraliza la recuperación dentro de `GnirehtetService` para evitar múltiples procesos de relay o ciclos de reconexión compitiendo entre sí.

### Si el panel RED muestra `Gnirehtet inactivo`

1. Confirma que el dispositivo sigue conectado en **DISPOSITIVO ANDROID**.
2. Si es necesario pulsa `↻` para refrescar la lista de dispositivos.
3. Selecciona nuevamente el dispositivo correcto.
4. Pulsa **INTERNET USB** para iniciar otra vez Gnirehtet.

Cuando NOVORA inicia Gnirehtet y detecta que el relay anterior ya no está ejecutándose, crea un nuevo relay antes de iniciar el cliente del dispositivo.

### Si Gnirehtet aparece activo pero el dispositivo perdió Internet

Utiliza un reinicio controlado de la sesión:

1. Pulsa **INTERNET USB** una vez para detener Gnirehtet.
2. Espera a que el panel RED deje de mostrar `Gnirehtet activo`.
3. Pulsa **INTERNET USB** nuevamente para iniciar una sesión limpia.
4. Comprueba el panel **RED** para verificar el estado de Internet, latencia y Gnirehtet.

Este procedimiento evita dejar un relay viejo compitiendo con una nueva sesión.

### Recuperación interna del túnel

La capa de servicio de NOVORA 1.3 incluye una operación interna de restablecimiento del túnel capaz de:

- reutilizar el dispositivo asociado a la sesión;
- volver a crear el relay si ya terminó;
- ejecutar nuevamente el túnel para el dispositivo seleccionado;
- actualizar el dispositivo activo cuando la recuperación termina correctamente.

**Importante:** en NOVORA-LINK 1.3 no existe un botón independiente llamado `Recovery` en la interfaz principal. La recuperación manual disponible para el usuario se realiza mediante **INTERNET USB**, deteniendo y volviendo a iniciar Gnirehtet cuando sea necesario.

## Panel RED

El panel **RED** concentra el estado de conectividad del dispositivo y permite revisar rápidamente:

- tipo de conexión: USB o Wi-Fi;
- disponibilidad de Internet;
- latencia medida;
- estado de Gnirehtet: activo o inactivo.

Este panel es la referencia principal para comprobar si el reverse tethering permanece operativo.

## Panel RENDIMIENTO

El panel **RENDIMIENTO** muestra métricas recopiladas del dispositivo, incluyendo información disponible de CPU, RAM, batería y temperatura.

RED y RENDIMIENTO comparten el mismo ciclo de actualización para evitar consultas ADB duplicadas.

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

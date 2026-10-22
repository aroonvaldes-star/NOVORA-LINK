<img width="1923" height="818" alt="Banner tecnológico Novora-Link en negro" src="https://github.com/user-attachments/assets/ca182ce8-2818-428d-bc34-f3757027ed62" />

# NOVORA-LINK 1.4

## Descripción técnica y acciones de la interfaz

NOVORA-LINK conecta un teléfono Android con una PC Windows para mostrar su pantalla, transportar datos y permitir la interacción autorizada. VisionEngine (VE) se ocupa de video y audio, LinkEngine (LE) del enlace y de la ruta de Internet por VPN, y Experience Input Engine (ExInEngine) de las entradas. Cada resultado depende del dispositivo, el transporte, los permisos y el estado de la sesión.

Esta descripción corresponde al árbol local de código revisado el 21 de octubre de 2026. LINK Control declara `versionCode 116` y `versionName 1.4.116`; el proyecto de escritorio declara la serie `1.4.0`. Estos datos de fuente no confirman la versión instalada en un teléfono, el contenido de un instalador ni los activos publicados.

En Android, App Control presenta dos pestañas: **Principal** y **Configuración**. Principal reúne la conexión USB/LAN, la elección de modo Juego, Video o Internet, el inicio de motores y los estados de VE, LE y ExIn. Configuración agrupa el estado de LE, los ajustes de VE y audio, y el estado de ExIn. Las páginas auxiliares que pueda conservar el código no son pestañas de navegación.

**Funciones que destacan en la versión de código revisada:**

- **Pantalla y audio del teléfono en Windows:** App Control captura con autorización de Android; VisionEngine recibe, decodifica y presenta la sesión por la ruta USB o LAN disponible.
- **Control desde la PC:** la ventana de VisionEngine envía acciones de puntero y teclado admitidas por el servicio de accesibilidad. ExInEngine administra por separado los mandos en Windows.
- **Portapapeles de texto en ambos sentidos:** permite copiar desde Android a Windows y enviar texto desde Windows al teléfono durante una sesión VisionEngine autorizada.
- **Archivos y carpetas:** la PC puede enviarlos mediante arrastrar y soltar o el portapapeles; también puede solicitar la carpeta NOVORA del teléfono cuando el canal de archivos esté disponible.
- **Capturas y grabación en la PC:** la interfaz habilita estas acciones según el estado del video, audio y formato de la sesión.
- **Privacidad y confianza:** el emparejamiento LAN verifica la identidad de la PC y PrivacyVE puede bloquear contenido y operaciones de intercambio sin cerrar necesariamente la conexión.

## 1. VisionEngine (VE)

VisionEngine recibe la captura autorizada de App Control y la presenta en Windows. La ruta Android usa MediaProjection y MediaCodec; el visor Windows decodifica y presenta el flujo. El inicio de la captura requiere permiso del teléfono y una sesión compatible. Una compilación o un estado de inicio no demuestran por sí solos video fluido en un dispositivo físico.

Los ajustes expuestos incluyen monitor de PC, perfil de VisionEngine, tasa de bits, resolución, límite de FPS, presentación de ventana y salida de audio. Se aplican según el estado y las capacidades que confirme Windows; algunos requieren iniciar o reiniciar el video. El FPS y la resolución efectivos pueden diferir de los valores elegidos.

En equipos compatibles se detectan capacidades NVIDIA. La actividad de NVDEC se informa a partir del decodificador que procesa fotogramas, no sólo de la presencia de una GPU o una API. La detección de NVENC o CUDA tampoco equivale a afirmar que codifiquen o aceleren la sesión actual.

**Acciones disponibles según estado y capacidad:**

- **Ver pantalla / Detener video (Windows):** inicia la visualización o detiene la sesión activa; Android debe autorizar la captura cuando se solicite.
- **Iniciar / Detener VisionEngine (Android):** cambia según el estado que publique la PC.
- **Configuración de VisionEngine (Android):** permite elegir los ajustes publicados y guardarlos para aplicarlos en la PC.
- **Captura de pantalla y Grabar en PC:** se habilitan sólo cuando la sesión y el formato cumplen sus requisitos. La grabación tiene restricciones adicionales de video y audio.

**Propiedades:** dispositivo y transporte, estado de captura y sesión, resolución, FPS, tasa de bits, perfil, monitor y decodificador en uso. Un dato ausente o caducado se muestra como **No disponible**.

La presentación de VisionEngine integra controles de entrada, portapapeles y arrastrar y soltar. Cada integración se condiciona por la sesión activa, la capacidad publicada y la política de privacidad; iniciar video no habilita automáticamente todas las acciones.

## 2. LinkEngine (LE)

LinkEngine administra la conexión Android-PC mediante USB o LAN y las funciones de transporte disponibles en esa sesión. La ruta de Internet mediante VPN es distinta del enlace Android-PC: una conexión o transferencia correcta no demuestra velocidad, continuidad ni baja latencia de Internet.

En Android, **Conectar USB** y **Conectar LAN** preparan la ruta correspondiente. El flujo LAN puede buscar una PC, introducir un código y recordar una PC autorizada. **Desconectar** cierra la conexión activa. La pantalla Principal muestra el transporte y el estado de los motores; Configuración muestra el estado de LE y las PC guardadas cuando están disponibles. Al iniciar LE, Android puede solicitar permiso VPN.

**Acciones visibles según estado:**

- **Conectar USB / Conectar LAN:** inicia el flujo de conexión y comunica el permiso o paso pendiente.
- **Buscar o vincular PC en LAN:** usa descubrimiento o código y confirma la identidad antes de guardar confianza.
- **Iniciar / Detener LinkEngine:** depende de la sesión y de las capacidades que publique la PC; puede requerir tomar una sesión activa.
- **Desconectar y consultar estado:** cierra la sesión o muestra transporte, PC vinculada y errores observados.

**Propiedades:** PC vinculada, transporte, confianza y estado de sesión. La latencia, pérdida de paquetes y tasas TX/RX sólo deben mostrarse si hay mediciones reales, recientes y con origen identificado. El protocolo define telemetría LE, pero en el flujo revisado no se encontró un productor efectivo para presentar un SpeedTest o monitor en vivo como función validada. Las mediciones del túnel Android-PC y de Internet por VPN deben permanecer separadas.

## 3. Experience Input Engine (ExInEngine)

ExInEngine coordina en la PC las entradas admitidas por la sesión Android. La PC gestiona la detección del mando, perfiles, asignación, sensibilidad, modos y calibración. La disponibilidad de un periférico no demuestra que sus entradas lleguen al teléfono; eso requiere una prueba durante una sesión activa.

App Control muestra **sólo el estado de ExInEngine**. No ofrece en sus dos pestañas controles de calibración, cambio de modo ni gestión del mando. Los controles correspondientes están en NOVORA PC. Que el código conserve páginas o métodos internos de ExIn no significa que formen parte de la navegación visible en Android.

**Acciones en Windows, sujetas a la capacidad del mando y la sesión:**

- **Probar entradas:** permite observar los reportes recibidos del mando conectado.
- **Modo Juego / UI:** cambia el modo cuando el motor confirma que puede hacerlo.
- **Calibrar mando:** guía la calibración en la PC y guarda el perfil cuando termina correctamente.
- **Reactivar control:** se habilita sólo si el motor permite la acción.

**Propiedades:** identidad y estado del mando, VID/PID si existen, perfil aplicado, batería si está disponible, reportes y diagnóstico. Android presenta información de estado recibida desde la PC; no debe inventar valores cuando falta telemetría.

## 4. Portapapeles, archivos y privacidad

**Portapapeles.** VisionEngine dispone de un canal de control para consultar o establecer texto en Android. Con la presentación activa, `Ctrl+C` solicita al teléfono copiar la selección enfocada y devuelve el texto a Windows; `Ctrl+V` lee el portapapeles de Windows y envía texto o URL para pegar en Android. Los cambios Android → Windows se procesan como eventos; el código revisado no consulta continuamente el portapapeles del teléfono. El contenido copiado no se muestra ni se registra en el mensaje de estado. La función necesita el control autorizado, la capacidad de portapapeles y que PrivacyVE permita usarla; la app que tenga el foco en Android también puede limitar el resultado de copiar o pegar.

**Archivos y carpetas.** En Windows se pueden soltar rutas sobre la zona Archivos / Share o la ventana de VisionEngine. `Ctrl+V` también reconoce archivos, carpetas y APK presentes en el portapapeles de Windows y los encamina como transferencias, en vez de tratarlos como texto. El destino Android es la carpeta NOVORA. La PC ofrece además una acción para recibir la carpeta NOVORA del teléfono. La transferencia exige una sesión y un canal de archivos disponibles; por LAN, Android debe conceder acceso a esa carpeta. La interfaz muestra el resultado de cada operación y los totales de archivos completados; esos totales no miden la velocidad del túnel ni de Internet.

**PrivacyVE.** El escudo de privacidad puede proteger el contenido de video y audio y bloquear control, mando, portapapeles o transferencias según su estado. Antes de copiar, pegar o transferir se vuelve a comprobar la política. La protección no demuestra que el contenido anterior se haya borrado de otros programas o del historial del sistema.

Estas rutas están presentes en el código revisado. Su comportamiento con aplicaciones Android concretas, permisos, archivos grandes y cambios entre USB y LAN requiere validación física por separado.

## 5. Criterios de interfaz y datos

Cada botón debe nombrar la acción que ejecutará en el estado actual, deshabilitarse mientras esa acción no esté disponible e informar el resultado. Si falta un permiso, un dispositivo o una capacidad, la interfaz debe indicar qué paso falta. Las acciones que interrumpen una sesión activa deben pedir confirmación cuando corresponda.

Los indicadores de estado deben distinguir dato confirmado, dato caducado y ausencia de datos. Los ajustes elegidos son solicitudes; la PC debe confirmar si se aplicaron. Las acciones rápidas no crean capacidades adicionales del motor.

## 6. Fiabilidad y verificación

La fiabilidad se evalúa por motor y por ruta: enlace USB/LAN, VPN e Internet, video, audio y entradas. Registrar versión realmente instalada, transporte, permisos, estado de cada motor y error observado permite reproducir fallos intermitentes. La calidad del video, la velocidad de Internet y la entrega de entradas requieren mediciones propias durante una sesión PC-Android.

La auditoría estática local del 10 de octubre identificó riesgos pendientes en el relay LE, el saludo DATA LAN y la recepción de archivos por VE LAN. También dejó sin validar el rendimiento y la fiabilidad de extremo a extremo en un teléfono físico. Por ello, las afirmaciones de baja latencia, estabilidad y recuperación automática siguen siendo objetivos sujetos a pruebas reproducibles, no resultados confirmados por este documento.

## 7. Licencia y avisos legales

NOVORA-LINK se distribuye bajo la **NOVORA-LINK PROPRIETARY LICENSE (NLPL) v1.1**. El código fuente puede descargarse, estudiarse y modificarse localmente; la redistribución de versiones modificadas y la explotación comercial requieren autorización escrita.

- [LICENSE](LICENSE) — licencia completa en español (versión prevalente).
- [LICENSE.en.md](LICENSE.en.md) — traducción de referencia al inglés.
- [NOTICE](NOTICE) — atribuciones y avisos de terceros.
- [PRIVACY.md](PRIVACY.md) — aviso de privacidad.
- [SECURITY.md](SECURITY.md) — reporte de vulnerabilidades.
- [TRADEMARKS.md](TRADEMARKS.md) — uso de marcas e identidad visual.
- [CONTRIBUTING.md](CONTRIBUTING.md) — contribuciones y licencia de aportes.
- [Legal/NLLegalThirdPartyNotices.md](Legal/NLLegalThirdPartyNotices.md) — inventario de terceros.
- [Legal/NLLegalEULA.txt](Legal/NLLegalEULA.txt) — contrato de usuario final.
- [Legal/LICENSES/](Legal/LICENSES/) — textos íntegros de licencias de terceros.

El instalador incluye el EULA, la licencia y la carpeta `Legal/` completa. Los componentes de terceros conservan sus propias licencias; consulte el inventario para el detalle de versiones y atribuciones.

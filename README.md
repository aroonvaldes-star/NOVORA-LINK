«[!WARNING]

⚠️ IMPORTANTE — NOVORA-LINK 1.4 PUEDE CONTENER ERRORES

NOVORA-LINK 1.4 es una Release pública en proceso de mejora continua.

Aunque esta versión ya está disponible para su uso, todavía pueden existir errores, incompatibilidades o comportamientos inesperados en determinados dispositivos, configuraciones o funciones.

Estos problemas se irán identificando y solucionando progresivamente mediante nuevas actualizaciones de NOVORA-LINK.

Los comentarios, reportes y experiencias de los usuarios son importantes para detectar problemas, mejorar la compatibilidad y aumentar la estabilidad del proyecto.

Si encuentras algún error, repórtalo indicando, cuando sea posible:

- Qué ocurrió.
- Qué dispositivo utilizaste.
- Qué versión de Android y Windows utilizaste.
- Qué motor estaba activo.
- Qué tipo de conexión estabas utilizando.
- Qué acción estabas realizando cuando apareció el problema.

Gracias por probar NOVORA-LINK y contribuir a su desarrollo.»

NOVORA-LINK

Versión actual: "1.4"
Release: "v1.4"
Estado: Release pública — mejora continua
Plataformas: Windows + Android

NOVORA-LINK conecta una computadora Windows con un dispositivo Android y organiza sus funciones mediante motores independientes, permitiendo utilizar únicamente los componentes necesarios para cada sesión.

La arquitectura está orientada a mantener una conexión clara entre Windows y Android, limitar la recuperación al componente afectado y minimizar el tratamiento innecesario de información personal.

---

Motores

NOVORA-LINK 1.4 organiza sus funciones principales en los siguientes motores:

LinkEngine — LE

Responsable de la conectividad, reverse tethering y transporte de red entre Windows y Android.

Puede compartir la conexión de Internet de la PC con el dispositivo Android mediante la infraestructura de NOVORA.

VisionEngine — VE

Responsable de las funciones relacionadas con:

- Pantalla.
- Video.
- Audio.
- Control.
- Teclado.
- Ratón.
- Integración Android ↔ Windows.
- Intercambio de información cuando la función correspondiente está disponible.

ExInEngine

Responsable de las entradas externas, principalmente gamepads y controles compatibles conectados a la PC y utilizados posteriormente en Android.

ExInEngine mantiene su propia sesión para evitar que una entrada externa dependa innecesariamente del estado de otros motores.

STEngine — ST

Responsable de la observación y medición técnica de NOVORA-LINK.

STEngine debe medir e informar, no controlar arbitrariamente los demás motores.

No debe modificar automáticamente bitrate, sesiones, colas o recuperación de otros componentes únicamente por detectar carga normal.

NL / común

Incluye los componentes compartidos de NOVORA-LINK:

- Aplicación.
- Interfaz.
- Servicios.
- Modelos.
- Control.
- Descubrimiento.
- Infraestructura común.

NLNVIDIA

Integración y aceleración NVIDIA opcional.

VisionEngine conserva su mecanismo alternativo cuando la aceleración correspondiente no está disponible.

---

Independencia de motores

Los motores de NOVORA-LINK están diseñados para poder iniciarse, detenerse o recuperarse de forma independiente siempre que la función utilizada no requiera explícitamente otra capa.

Un problema en un motor no debe provocar automáticamente el reinicio completo de NOVORA-LINK.

La recuperación completa debe utilizarse como último recurso.

---

Dispositivo activo

NOVORA-LINK 1.4 está diseñado actualmente para trabajar con:

1 dispositivo Android activo por computadora.

Esto permite concentrar los recursos de NOVORA en una sola sesión y simplificar el control de:

- ADB.
- Red.
- Video.
- Audio.
- Entrada.
- Transferencias.
- Recuperación.

Los límites históricos para múltiples clientes no representan la arquitectura actual de la Release 1.4.

---

Aplicaciones

NOVORA-LINK está formado por una aplicación para Windows y una aplicación complementaria para Android.

Ambas forman parte de la misma experiencia.

Windows

Proyecto principal:

"src/NOVORA/NLProjectDesktop.csproj"

Interfaz:

"src/NOVORA/UI/"

La aplicación Windows funciona como centro principal de la sesión.

Desde ella se realiza la detección del dispositivo y se administran los motores y funciones disponibles.

---

Android

Proyecto:

"src/NOVORA.Android/NLProjectAndroid.csproj"

Package ID:

"com.novora.appcontrol"

La aplicación Android complementa la aplicación de PC y presenta las funciones disponibles en el dispositivo.

Las aplicaciones Windows y Android deben corresponder a una versión compatible de NOVORA-LINK.

El proyecto Android requiere empaquetado explícito.

Para validaciones de código puede utilizarse:

"-t:Compile"

No debe generarse un APK innecesariamente cuando la validación requerida sea únicamente de compilación.

---

Conexión

USB es la ruta recomendada para:

- Primera configuración.
- Autorización del dispositivo.
- Instalación del APK.
- Depuración.
- Pruebas donde se necesita una conexión controlada.

Las funciones de red local, descubrimiento o QR pueden utilizarse cuando estén disponibles en la versión instalada.

La detección de un dispositivo no sustituye la autorización del usuario.

---

Nomenclatura

Prefijos principales utilizados en NOVORA-LINK:

Prefijo| Área
"NL"| NOVORA-LINK / común
"LE"| LinkEngine
"VE"| VisionEngine
"ST"| STEngine
"NLNVIDIA"| Integración NVIDIA

La autoridad para las migraciones de nombres se encuentra en:

"Documentation/NLDocumentationNamingMap.json"

No deben introducirse nuevamente nombres antiguos sin comprobar primero este mapa.

---

Zero Polling

NOVORA-LINK utiliza Zero Polling por defecto.

Siempre que sea posible se prefieren:

- Eventos.
- Callbacks.
- Push.
- Socket readiness.
- Notificaciones del sistema.
- Estado en memoria.
- Snapshots bajo demanda.

Los timers continúan permitidos cuando son necesarios para:

- Timeout.
- Deadline.
- Debounce.
- Pacing.
- Backoff.

Estos usos no representan polling por sí mismos.

---

Privacidad

NOVORA-LINK está diseñado bajo un enfoque de minimización de información personal.

El sistema no debe almacenar innecesariamente:

- Contraseñas.
- Cuentas.
- Correos electrónicos.
- Contactos.
- OTP.
- Tokens.
- Cookies.
- Historial privado del portapapeles.
- Contenido personal ajeno a la función solicitada.

Los permisos deben limitarse a los necesarios para ejecutar la función solicitada por el usuario.

Para más información:

"SECURITY.md"

"Documentation/NLDocumentationProjectRules.md"

---

Manual de usuario

NOVORA-LINK 1.4 incluye documentación de usuario en español e inglés.

Español

"Documentation/Manual/ES/NLDocumentationManualUsuarioES.md"

English

"Documentation/Manual/EN/NLDocumentationUserManualEN.md"

El manual español se mantiene como referencia prioritaria del proyecto.

Las aplicaciones pueden proporcionar acceso al manual correspondiente.

---

Compilación y validación

En Windows puede utilizarse el Release Gate:

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tool\NLToolReleaseGate.ps1

El Release Gate ejecuta las validaciones correspondientes por bloques.

Una compilación correcta no debe interpretarse automáticamente como una prueba física correcta.

NOVORA diferencia entre:

- Código implementado.
- Código conectado.
- Verificación estática.
- Build correcto.
- Integración funcional.
- Prueba física.
- Función completamente validada.

---

Instalador

Fuente de Inno Setup:

"Tool/NLInstallerSetup.iss"

La Release 1.4 proporciona el instalador de NOVORA-LINK para Windows junto con los componentes necesarios para la distribución correspondiente.

La instalación proporciona acceso a NOVORA-LINK y a la documentación incluida en la entrega.

---

Componentes de terceros

NOVORA-LINK puede utilizar componentes de terceros mientras no exista una sustitución propia completamente validada.

La presencia de estos componentes no implica autoría por parte de NOVORA-LINK.

Consulte:

"THIRD-PARTY-NOTICES.md"

"ACKNOWLEDGEMENTS.md"

"Legal/"

NOVORA-LINK no reclama autoría sobre componentes pertenecientes a terceros.

---

Estado funcional

La Release 1.4 contiene componentes con diferentes niveles de validación.

Una función puede encontrarse:

- Implementada.
- Conectada.
- Verificada estáticamente.
- Compilada.
- Integrada.
- Probada físicamente.
- Pendiente de validación adicional.

Por esta razón, una función visible en la interfaz no implica necesariamente que haya sido validada en todas las combinaciones de hardware, dispositivos Android o configuraciones posibles.

La documentación técnica correspondiente permite diferenciar estos estados.

---

Reportar errores

Los reportes de usuarios ayudan directamente al desarrollo de NOVORA-LINK.

Cuando encuentres un problema, intenta incluir:

- Versión de NOVORA-LINK.
- Modelo del dispositivo Android.
- Versión de Android.
- Versión de Windows.
- Motor afectado.
- Tipo de conexión.
- Primer error visible.
- Acción que estabas realizando.
- Si el problema puede reproducirse nuevamente.

Un reporte con contexto facilita considerablemente identificar y corregir el problema.

---

NOVORA-LINK 1.4

NOVORA-LINK 1.4 representa la primera Release pública de esta etapa del proyecto y reúne en una misma arquitectura:

- Aplicación principal para Windows.
- Aplicación Android.
- LinkEngine.
- VisionEngine.
- ExInEngine.
- STEngine.
- Integración Windows ↔ Android.
- Gestión independiente de motores.
- Herramientas de conectividad e interacción.
- Arquitectura Zero Polling.
- Controles orientados a privacidad.
- Recuperación limitada al componente afectado cuando sea posible.

«[!NOTE]
NOVORA-LINK continuará evolucionando después de la Release 1.4.

La publicación de "v1.4" no significa que el desarrollo haya terminado. Las siguientes actualizaciones corregirán problemas encontrados durante el uso real, ampliarán la compatibilidad y continuarán mejorando la estabilidad del sistema.»

---

NOVORA-LINK 1.4
Windows ↔ Android
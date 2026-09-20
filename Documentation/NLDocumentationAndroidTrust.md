# NOVORA Android: bloque 4 — confianza y reconexión LAN

Entrega de desarrollo para NOVORA PC 1.4. Android mantiene com.novora.appcontrol, versión 1.4.3 (código 4), protocolo de control 1. Incluye los bloques anteriores.

## Uso

1. En HOME de PC, crear una invitación LAN y escanear el QR desde Android. La invitación dura dos minutos y admite un uso.
2. Ya conectado, pulsar Recordar esta PC y confirmar. Detectar una PC o escanear un QR no guarda confianza automáticamente.
3. En siguientes sesiones, abrir PCs guardadas y conectar. No se necesita un nuevo QR mientras sigan válidos la dirección, identidad y permiso.
4. Si se interrumpe una sesión LAN recordada con el servicio Android activo, se intenta recuperar tras esperas de 1, 3 y 8 segundos. Se consulta el estado actual; no se repiten cambios de bitrate, perfil o audio.
5. Desconectar cancela la recuperación. Olvidar PC borra la credencial local y desconecta esa PC; no revoca por sí solo el permiso almacenado en una PC sin conexión.
6. En HOME de PC, Teléfonos autorizados permite revocar un teléfono. Se corta la sesión activa y ese permiso deja de autenticar. Si quedan otros teléfonos autorizados, se restaura la escucha que estaba habilitada.

## Reinicios y límites

Al abrir NOVORA PC se recupera la escucha LAN si estaba habilitada, hay teléfonos autorizados y la dirección guardada continúa disponible. DETENER CONTROL deshabilita esa restauración; ACTIVAR RECONEXIÓN LAN permite habilitarla otra vez. Si cambia la dirección de PC, generar un nuevo QR y guardar la conexión actualizada.

Android no inicia una conexión al arrancar el teléfono ni revive automáticamente tras terminar su proceso. La recuperación depende del servicio de sesión del bloque 3 y está limitada a tres intentos. Un rechazo de credenciales o identidad termina los intentos. USB conserva su flujo anterior y no utiliza esta confianza persistente.

Máximo 16 permisos por PC y 16 PCs guardadas en Android. Si se pierde la respuesta durante el registro, puede quedar un permiso en PC que Android no guardó: revocarlo desde HOME y vincular nuevamente. Guardar nuevamente una PC puede dejar un permiso anterior que debe revocarse en PC.

## Protección de credenciales

TLS comprueba la huella de PC antes de enviar el secreto de recuperación. PC guarda identidad y hashes de secretos protegidos con DPAPI del usuario Windows; Android cifra las credenciales con AES-GCM y una clave de AndroidKeyStore, fuera del respaldo automático. Un almacén dañado se rechaza y no se sustituye silenciosamente. El certificado de PC tiene vigencia de cinco años; su vencimiento requiere renovar identidad y vincular otra vez.

Referencias de implementación: [ProtectedData de Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata) y [Android Keystore](https://developer.android.com/privacy-and-security/keystore).

## Verificación y alcance

Las pruebas automatizadas cubren registro, persistencia, recuperación TLS, huella/secreto incorrectos, invitación cancelada y revocación. Consultar el informe de entrega para el conteo final. La compilación Android se limita al objetivo Compile: no equivale a generar o instalar un APK ni prueba AndroidKeyStore en un dispositivo.

La candidata PRERELEASE PRE FINAL normaliza el asset a `NLAssetBienvenido.png`. Los pendientes físicos de confianza/reconexión siguen requiriendo validación real.


Estado actual: [bloque 5 — control de motores e Internet USB](NLDocumentationAndroidEngines.md). Las referencias anteriores a ausencia de VPN describen aquellos bloques; el bloque 5 incorpora su cliente USB, pendiente de validación física. Cambiar control USB a LAN cierra ese túnel.


Distribución y primer inicio: [bloque 6 — instalación y actualización desde HOME](NLDocumentationAndroidInstall.md). La instalación es independiente de la vinculación y del consentimiento VPN; el APK firmado sigue pendiente de distribución.


## Actualización de interfaz, archivos y grabación (15 septiembre 2026)

Consultar [Android universal](NLDocumentationAndroidUniversal.md) para el comportamiento actual, rutas, comandos, APK 1.4.7 y límites comprobados. Las verificaciones históricas de esta sección conservan su alcance original.

## Corrección de arranque 1.4.8 — 15 septiembre 2026

Se reprodujo en Samsung SM-A566E con Android 16 el cierre de 1.4.7: UnsatisfiedLinkError en MainActivity.n_onCreate. Se deshabilita AndroidEnableMarshalMethods para usar registro JNI dinámico. Después apareció InvalidCastException al obtener IWindowManager; se corrige con JavaCast<IWindowManager>().

APK 1.4.8, versionCode 9, instalado mediante actualización conservando datos. Arranque en frío y pantalla de conexión comprobados por ADB y árbol de interfaz; proceso activo tras el arranque. Esto sustituye la ausencia de prueba física del arranque reportada antes; las demás pruebas completas PC↔Android siguen pendientes.

Windows, Android y los tres proyectos auxiliares de VE compilan. 230 pruebas .NET aprobadas. RelayCore Release compila con 21 advertencias existentes y aviso de dependencia net2. Los proyectos WPF se verificaron secuencialmente tras una colisión de archivos generados al intentar compilarlos en paralelo.

Referencia del problema de enlace en .NET: https://github.com/dotnet/maui/issues/35209

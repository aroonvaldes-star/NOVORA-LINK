# NOVORA Android — bloque 2: LAN, QR y detección

Actualización: el [bloque 3 de continuidad y notificaciones](NLDocumentationAndroidSession.md) cambia el cierre al salir de la Activity por una sesión propiedad del servicio. Los resultados y límites que siguen describen el bloque 2; el documento del bloque 3 indica el estado combinado y sus pruebas físicas pendientes.

Entrega de desarrollo del 14 de septiembre de 2026 sobre el bloque 1. Se incorpora control por red local a la misma aplicación Android com.novora.appcontrol. VersionCode 2, versión Android 1.4.1, protocolo de control 1 y destino de integración NOVORA PC 1.4. El número Android no exige que PC cambie de versión.

## Flujo de uso

1. En HOME de PC, pulsar PREPARAR LAN / QR y elegir la interfaz de la red compartida con Android. Se ofrecen direcciones IPv4 privadas de interfaces Ethernet o Wi-Fi activas.
2. PC muestra una invitación QR válida dos minutos. También permite copiar su texto de manera explícita. El QR contiene autorización temporal; no se debe publicar.
3. En Android, BUSCAR NOVORA PC realiza una consulta de tres segundos. Elegir un resultado solo selecciona su dirección: no autoriza control. Escanear el QR verifica el equipo. También se puede escanear directamente si el router bloquea la detección.
4. Android pide permiso de cámara al abrir el lector, lee el QR dentro de la app y solicita confirmar la conexión. Si se eligió una PC descubierta, la dirección del QR debe coincidir.
5. El cliente comprueba la huella del certificado y después envía el secreto por TLS. Una sesión autorizada habilita los mismos controles del bloque 1: bitrate, perfil, audio y reinicio de una captura activa.
6. DETENER CONTROL revoca el enlace. Cerrar una invitación QR pendiente también la revoca. Al salir de primer plano en Android se cierra la sesión y se necesita una invitación nueva.

La búsqueda se ejecuta a petición del usuario; no es una búsqueda continua al abrir la app. El estado del control se actualiza mediante eventos. USB conserva su botón y su código; preparar una nueva sesión sustituye la anterior, sin conmutación automática entre transportes.

## Seguridad y límites del transporte

- USB: TCP 27214 en loopback mediante ADB reverse, conservado del bloque 1.
- Control LAN: TCP 27215 en la dirección seleccionada, TLS 1.2/1.3, huella SHA-256 incluida en el QR, secreto aleatorio de 256 bits, cinco intentos y una sesión. No existe fallback LAN sin cifrado.
- Detección: UDP 27216, una consulta broadcast y respuestas correlacionadas por un valor aleatorio. Respuestas limitadas, nombre acotado, dirección del emisor comprobada y sin credenciales. El anuncio termina al cerrar, autorizar o vencer la invitación; tiene un límite autónomo de dos minutos.
- El certificado de sesión usa un contenedor temporal de clave de usuario compatible con Schannel de Windows, sin PersistKeySet. El buffer PKCS#12 se borra; el certificado se dispone al cerrar el servidor. No se distribuye un certificado privado ni una contraseña fija.
- El QR autoriza a quien lo obtiene. La validación depende de escanear el QR mostrado por la propia PC; los anuncios de red no son identidades verificadas. El copiado al portapapeles es explícito y no se borra automáticamente el portapapeles del usuario.
- Los mensajes están limitados a 64 KiB. No se ejecutan órdenes ni se publican ajustes antes del emparejamiento. Los cambios usan revisiones y no se reintentan automáticamente si se pierde su confirmación.
- Las rutas loopback de las pruebas de descubrimiento son internas; la API pública conserva las restricciones de IPv4 privada.

El alcance es red local IPv4 privada: no acceso remoto por Internet, satélite, VPN, IPv6 o redes de invitados aisladas. El firewall debe permitir NOVORA en la red privada; esta entrega no cambia sus reglas. Una PC puede usar Ethernet y el teléfono Wi-Fi si comparten una red que permite comunicación entre ambos.

LAN transporta órdenes de control: no sustituye el transporte de video ni el túnel de Internet. VisionEngine mantiene sus requisitos existentes para la captura del teléfono seleccionado. Bitrate y perfil requieren iniciar/reiniciar video; no se anuncian como ajustes de captura en caliente.

## Componentes y dependencias

Control/NLControlLanInvitation, NLControlLanServer y NLControlLanDiscovery implementan el enlace común de control. NLControlClient conserva USB e incorpora TLS. Las ventanas AndroidLan/AndroidControl de PC utilizan los servicios de NOVORA existentes. LinkEngine y RelayCore no fueron modificados.

AndroidUI incorpora el lector QR mediante cámara clásica de Android y ZXing.Net 0.16.11. La cámara es opcional en el manifiesto; no se guardan imágenes y solo se decodifica un cuadro a la vez. Esta API Camera está obsoleta; su funcionamiento físico en el Samsung sigue pendiente. No requiere Google Play Services. La misma dependencia genera el QR en PC.

Fuentes técnicas consultadas: [ZXing.Net en NuGet](https://www.nuget.org/packages/ZXing.Net/0.16.11), [callback de cámara de Android](https://learn.microsoft.com/en-us/dotnet/api/android.hardware.camera.setpreviewcallback), [SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream).

## Comprobaciones y pendientes

Se verificó compilación de PC y código administrado Android, TLS real sobre sockets loopback, rechazo de huella/clave incorrectas, rechazo de comandos previos al emparejamiento, lectura del QR generado y búsqueda/respuesta UDP local. No se ha certificado el recorrido físico cámara Samsung → Wi-Fi → NOVORA PC, el firewall o la estabilidad en distintas redes.

La suite histórica reportó 117 pruebas en ese bloque. En la candidata PRERELEASE PRE FINAL el asset fue normalizado a `NLAssetBienvenido.png`; las pruebas deben repetirse con el Release Gate actual.

```powershell
dotnet build .\src\NOVORA\NLProjectDesktop.csproj -c Release
dotnet test .\tests\NOVORA.Tests\NLProjectTests.csproj -c Release
dotnet build .\src\NOVORA.Android\NLProjectAndroid.csproj -c Debug -t:Compile
```

Para pruebas físicas falta un empaquetado explícito e instalación firmada. La aplicación mantiene su identidad, pero actualizar su código en el teléfono requiere instalar una nueva versión con la misma firma. No se generó APK/AAB en esta entrega. Persistencia en segundo plano, reconexión de confianza, VPN, burbuja, grabación, archivos y el resto del control total continúan pendientes.


Continuidad actualizada: [bloque 4 — PCs recordadas, recuperación LAN y revocación](NLDocumentationAndroidTrust.md). Las limitaciones de confianza descritas para bloques anteriores son históricas.


Estado actual: [bloque 5 — control de motores e Internet USB](NLDocumentationAndroidEngines.md). Las referencias anteriores a ausencia de VPN describen aquellos bloques; el bloque 5 incorpora su cliente USB, pendiente de validación física. Cambiar control USB a LAN cierra ese túnel.


Distribución y primer inicio: [bloque 6 — instalación y actualización desde HOME](NLDocumentationAndroidInstall.md). La instalación es independiente de la vinculación y del consentimiento VPN; el APK firmado sigue pendiente de distribución.


## Actualización de interfaz, archivos y grabación (15 septiembre 2026)

Consultar [Android universal](NLDocumentationAndroidUniversal.md) para el comportamiento actual, rutas, comandos, APK 1.4.7 y límites comprobados. Las verificaciones históricas de esta sección conservan su alcance original.

## Corrección de arranque 1.4.8 — 15 septiembre 2026

Se reprodujo en Samsung SM-A566E con Android 16 el cierre de 1.4.7: UnsatisfiedLinkError en MainActivity.n_onCreate. Se deshabilita AndroidEnableMarshalMethods para usar registro JNI dinámico. Después apareció InvalidCastException al obtener IWindowManager; se corrige con JavaCast<IWindowManager>().

APK 1.4.8, versionCode 9, instalado mediante actualización conservando datos. Arranque en frío y pantalla de conexión comprobados por ADB y árbol de interfaz; proceso activo tras el arranque. Esto sustituye la ausencia de prueba física del arranque reportada antes; las demás pruebas completas PC↔Android siguen pendientes.

Windows, Android y los tres proyectos auxiliares de VE compilan. 230 pruebas .NET aprobadas. RelayCore Release compila con 21 advertencias existentes y aviso de dependencia net2. Los proyectos WPF se verificaron secuencialmente tras una colisión de archivos generados al intentar compilarlos en paralelo.

Referencia del problema de enlace en .NET: https://github.com/dotnet/maui/issues/35209

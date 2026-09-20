# NOVORA Android: primer bloque de control USB

Continuidad actualizada: consultar el [bloque 3](NLDocumentationAndroidSession.md) para sesión independiente de la pantalla, notificaciones y pérdida del enlace. No modifica el contrato USB ni implementa la VPN.

Actualización: el [bloque 2 de LAN, QR y detección](NLDocumentationAndroidLan.md) amplía este bloque conservando USB. Las limitaciones USB y resultados descritos a continuación corresponden a la primera entrega; consultar el documento del bloque 2 para el estado combinado.

Entrega de desarrollo del 14 de septiembre de 2026. Código preparado contra NOVORA PC 1.4 en NL2. La compatibilidad física con un teléfono queda pendiente.

## Alcance entregado

HOME incorpora preparación y cierre del control USB. Android permite introducir el código de PC, consultar estado y cambiar bitrate, perfil y salida de audio. Usa los recursos NLAssetBienvenido y NLAssetNovoraLogo de PC y su paleta oscura/cian.

Bitrate y perfil se guardan para el siguiente inicio de video. El usuario puede confirmar un reinicio del video activo desde Android. La salida de audio se solicita al motor existente; si no hay reproducción activa, se informa que necesita iniciar o reiniciar video. Estado configurado y reproducción activa se muestran por separado.

## Enlace y límites

El canal nuevo escucha exclusivamente en loopback, puerto 27214, transportado por ADB reverse. No es un servidor LAN. Usa un código aleatorio de ocho dígitos, válido dos minutos, hasta cinco intentos y una sesión. No persiste credenciales. El usuario prepara el enlace en PC y escribe el código en Android. El protocolo de control versión 1 incluye revisiones de estado para rechazar cambios basados en valores obsoletos, mensajes limitados a 64 KiB y una cola limitada. Los cambios se notifican por eventos; no se consulta continuamente el estado.

El código autoriza a quien lo conoce dentro del transporte USB preparado. No sustituye TLS ni autorización para redes LAN. Descubrimiento y emparejamiento LAN deberán implementarse por separado. Al salir de primer plano, Android cierra esta sesión piloto y necesita una nueva preparación. Un tiempo de espera no reenvía automáticamente un cambio.

Este control utiliza los servicios de PC existentes. No implementa el túnel de LinkEngine ni reemplaza scrcpy/ADB. Tampoco inicia por primera vez VisionEngine desde Android: permite reiniciar una sesión de video ya activa.

## Proyecto y APK

Proyecto: src/NOVORA.Android/NLProjectAndroid.csproj. Requiere .NET 10 y workload Android; mínimo declarado Android 8/API 26. Identidad nueva: com.novora.appcontrol, versión 1.4.0. No actualiza la antigua com.novora.linkengine. La firma de distribución definitiva sigue pendiente.

Para comprobar código administrado sin producir APK:

```powershell
dotnet build .\src\NOVORA.Android\NLProjectAndroid.csproj -c Debug -t:Compile
```

El empaquetado exige habilitar NovoraPackage explícitamente. Una compilación normal que alcance empaquetado falla de forma intencional. Esta entrega no incluye un APK ni certifica empaquetado, firma, instalación o ejecución. Cambiar código nativo de Android sí requerirá actualizar la aplicación instalada; conservar una identidad estable evita aplicaciones duplicadas, no elimina esa necesidad.

## Verificación realizada

- PC compila en Release y las 100 pruebas .NET pasan, incluidas 15 comprobaciones del protocolo, autorización, secuencia, estado y desconexión.
- Android pasa el objetivo Compile sin advertencias ni errores. Esta comprobación no equivale a compilar e instalar un APK completo.
- La protección frente a empaquetado implícito rechaza la compilación normal como se esperaba. No se generaron APK/AAB.
- Segunda revisión independiente de código: corregidos limpieza de reverse tras cancelación, mensajes de audio y cierre de cola al perder el consumidor.
- En la candidata PRERELEASE PRE FINAL el asset fue normalizado a `NLAssetBienvenido.png` y la migración quedó registrada en `NLDocumentationNamingMap.json`.
- No se realizaron pruebas físicas PC–Android ni mediciones de rendimiento. Las pruebas de red usan loopback y un manejador de estado de prueba; no certifican el audio real de Windows.

## Funciones pendientes del concepto completo

LAN y QR; reconexión persistente; VPN/Internet; transferencia de archivos; inicio remoto completo de motores; todos los ajustes de PC; burbuja móvil; grabación y exclusión de la burbuja; accesos a redes sociales; notificación persistente; manual integrado; video vertical de bienvenida; instalación desde HOME y distribución firmada. No se muestran como disponibles en este bloque.


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

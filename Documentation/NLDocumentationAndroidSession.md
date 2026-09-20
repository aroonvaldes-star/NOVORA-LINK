# NOVORA Android — bloque 3: continuidad de sesión y notificaciones

Entrega de desarrollo sobre los bloques 1 y 2. Mantiene com.novora.appcontrol, incrementa versionCode a 3 y versión Android a 1.4.2; el contrato de control conserva compatibilidad de código con NOVORA PC 1.4. La firma e instalación física todavía no están verificadas.

## Comportamiento implementado

La sesión USB/LAN pertenece a un servicio Android y ya no a la Activity. Cambiar de app, abrir el lector QR o recrear la pantalla no llama a desconectar. La Activity se vincula por Binder, se suscribe mientras es visible y recupera el estado vigente al volver. La lectura del QR no sustituye una conexión existente hasta confirmar el nuevo enlace.

El servicio se inicia por una acción explícita de conexión con la pantalla visible. Publica una notificación de baja importancia con PC, USB/LAN, estado del video, bitrate configurado y acciones Abrir controles/Desconectar. La vista pública de pantalla bloqueada muestra contenido genérico. El servicio es no exportado, de tipo connectedDevice y NotSticky: no se relanza automáticamente una conexión al reiniciar el teléfono o morir el proceso.

Android 13 o posterior solicita permiso de notificaciones en el contexto de conectar. Rechazarlo puede ocultar el panel; el sistema todavía exige una notificación al iniciar el servicio y la desconexión sigue disponible dentro de NOVORA. Las preferencias del canal y del sistema pueden afectar la visibilidad real.

Al desconectar voluntariamente se invalidan el estado y órdenes futuras, se retira la notificación y se termina el servicio iniciado. Al perder el enlace se detiene foreground y, si hay permiso, puede permanecer una notificación ordinaria para abrir NOVORA. No se conserva una pantalla con controles habilitados tras perder la confirmación.

## Concurrencia, red y permisos

- NLControlSession protege la identidad de cada conexión, mantiene snapshots y estado de orden pendiente y descarta callbacks de clientes anteriores.
- Ninguna orden sin confirmación se reenvía automáticamente. Cambiar o cancelar una sesión pendiente no permite que su respuesta tardía la reactive.
- La sesión puede existir sin observadores de pantalla. Una recreación vuelve a leer el estado actual; las notificaciones también leen el estado vigente cuando procesan eventos.
- Eventos de cambio/pérdida de la red predeterminada cierran una sesión LAN activa o en preparación. No reconectan ni cambian de transporte automáticamente y no afectan USB. El seguimiento es de la red predeterminada; no se implementó selección avanzada de rutas en teléfonos con varias redes simultáneas.
- TCP keepalive solicita comprobaciones del sistema operativo: 30 s de inactividad, 10 s entre sondeos y tres reintentos cuando están soportados. En caso contrario se usan valores del sistema. No se añaden consultas periódicas de ajustes ni reenvíos de comandos. No se promete un tiempo exacto de detección bajo suspensión/ahorro de batería.
- Se declaran FOREGROUND_SERVICE, FOREGROUND_SERVICE_CONNECTED_DEVICE, ACCESS_NETWORK_STATE, POST_NOTIFICATIONS y CHANGE_NETWORK_STATE. Este último satisface un requisito declarativo de connectedDevice; el código no cambia la configuración de red.
- Los PendingIntent son explícitos e inmutables. No se guardan códigos, QR ni claves en preferencias, intents de conexión o almacenamiento persistente.

El servicio permite mantener la conexión al dejar la pantalla, pero su continuidad física bajo bloqueo, Doze y restricciones de Samsung aún requiere medición. No se solicita exclusión automática del ahorro de batería ni se fuerzan bloqueos de energía.

## Alcance que sigue pendiente

No se implementaron todavía PC de confianza persistentes, revocación de dispositivos guardados o reconexión automática. Una pérdida real exige un nuevo código/QR; mantener una sesión existente en segundo plano es distinto de recuperarla después de que termine. La expansión del control de PC, VPN/Internet, burbuja, grabación, archivos, video de bienvenida y el instalador HOME siguen en sus siguientes etapas.

## Evidencia de validación

La suite .NET integrada pasa 123 pruebas. Las seis nuevas usan sockets USB loopback y prueban quitar observadores sin perder la sesión, cierre voluntario, pérdida de PC, cancelación durante emparejamiento, no repetición de una mutación y reemplazo sin que una respuesta antigua borre la nueva sesión. No ejecutan Android ni su servicio real.

PC compila en Release. Android pasa el objetivo Compile con cero advertencias y errores. Segunda revisión independiente corrigió notificación obsoleta después de desconectar y la carrera entre callbacks y destrucción del Handler. Se revisó el manifiesto fuente; no se generaron APK/AAB ni se instaló la app.

La comprobación adicional _GenerateJavaStubs no se completó: faltaba el diseñador real de recursos. El SDK lo genera mediante una etapa que produce resources.apk temporal. No se sustituyó por un artefacto simulado ni se presenta como manifiesto final verificado. La compilación administrada es una capa parcial; empaquetado, stubs/manifiesto final y pruebas físicas permanecen pendientes.

La candidata PRERELEASE PRE FINAL normaliza el asset a `NLAssetBienvenido.png` y elimina esa excepción de nomenclatura.

```powershell
dotnet build .\src\NOVORA\NLProjectDesktop.csproj -c Release
dotnet test .\tests\NOVORA.Tests\NLProjectTests.csproj -c Release
dotnet build .\src\NOVORA.Android\NLProjectAndroid.csproj -c Debug -t:Compile
```

Fuentes: [servicios foreground y connectedDevice](https://developer.android.com/develop/background-work/services/fgs/service-types), [permiso de notificaciones](https://developer.android.com/develop/ui/views/notifications/notification-permission), [inicio de un servicio foreground](https://developer.android.com/develop/background-work/services/fgs/launch). Las reglas de Android justifican los permisos y ciclo de vida; no son evidencia de ejecución en el teléfono del usuario.


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

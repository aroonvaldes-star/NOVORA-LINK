# NOVORA Android — bloque 6: instalación desde HOME

Entrega de escritorio compatible con NOVORA PC 1.4. No modifica el cliente Android del bloque 5: mantiene com.novora.appcontrol, versionCode 5 y nombre 1.4.4. No requiere recompilar Android por el solo hecho de añadir el instalador PC.

## Experiencia del usuario

HOME muestra una tarjeta con la explicación de la app y un botón INSTALAR NOVORA POR USB. Con el paquete de distribución incluido, seleccionar un teléfono USB autorizado comprueba su versión mediante eventos y una consulta puntual. COMPROBAR APP permite repetir la consulta manualmente; no hay sondeo periódico del instalador.

- Primera instalación: muestra INSTALAR NOVORA POR USB.
- Versión instalada anterior: muestra ACTUALIZAR NOVORA ANDROID.
- Misma versión: muestra NOVORA ANDROID ACTUALIZADO y evita reinstalar.
- Versión instalada más nueva: bloquea la instalación para evitar bajar versión.
- Paquete ausente: explica que esa edición PC aún no incluye el instalador y mantiene deshabilitada la instalación. Esta entrega de código todavía no incluye el APK firmado.
- Firma incompatible: informa el rechazo de Android y no intenta desinstalar la app para eludirlo.

Antes de instalar, la confirmación muestra teléfono, versión actual y disponible; advierte que el control y la VPN se desconectarán. Se vuelve a comprobar el destino antes de escribir. Durante esa operación no se puede preparar otro enlace USB/LAN de NOVORA. Si cambia el dispositivo o se cierra PC, se cancela la espera; si Android ya estaba instalando, el resultado se informa como desconocido y se requiere comprobar de nuevo.

Se usa `adb install --user 0 -r`, nunca desinstalación, borrado de datos, downgrade ni concesión automática de permisos. `-r` solicita conservar los datos existentes, sujeto a que Android acepte una actualización compatible. No se promete recuperar datos ante fallos del sistema o migraciones de futuras versiones. Tras el éxito ADB se consulta nuevamente la versión instalada antes de confirmar.

## Requisitos de este bloque

Un solo teléfono USB conectado al servidor ADB y el usuario principal (0) activo. Windows no siempre muestra usb: en devices -l; por eso se compara el serial seleccionado con `adb -d get-serialno`. Si hay varios USB, se pide dejar sólo el destinatario. Cada comando sigue dirigido al serial explícito.

El usuario debe autorizar la depuración USB. La app no se instala silenciosamente sin esa autorización. No se instala a través de Wi-Fi/LAN; después de instalar, la vinculación por USB o QR conserva su flujo independiente. Instalar no concede automáticamente confianza a una PC ni activa la VPN.

## Paquete único de distribución

Dentro del proyecto PC: `src/NOVORA/Android/`. Junto al ejecutable final: `Android/`.

Dos nombres estables:

- `NLAndroidApp.apk`: único APK aprobado y firmado de la app nueva.
- `NLAndroidRelease.json`: descriptor vinculado exactamente a sus bytes.

El proyecto PC copia ambos archivos al compilar/publicar solamente cuando existen. No llama a compilación Android ni genera APK por cada prueba.

Formato del descriptor, con ejemplo de los valores de la app actual; la huella debe sustituirse por el SHA256 real del APK aprobado:

```json
{
  "SchemaVersion": 1,
  "PackageId": "com.novora.appcontrol",
  "VersionCode": 5,
  "VersionName": "1.4.4",
  "MinSdk": 26,
  "ControlProtocol": 1,
  "PcSeries": "1.4",
  "Sha256": "HUELLA_SHA256_REAL_DE_64_CARACTERES"
}
```

Este ejemplo es documentación, no un descriptor instalable. No agregarlo como si existiera un APK validado. Para preparar la distribución, primero completar la compilación Android, conservar la clave de firma elegida, verificar el APK firmado y sus metadatos, probarlo en el teléfono y generar el descriptor de ese artefacto exacto. Revisar compatibilidad antes de declarar ControlProtocol/PcSeries; esos campos son un compromiso de la publicación, no una detección automática del comportamiento de la app.

El lector PC comprueba límites, identidad del manifiesto binario real, versionCode/versionName/minSdk y SHA256. Una huella junto al archivo comprueba coherencia, no autoría: Android valida la firma al instalar, y para actualizar exige firma compatible. NOVORA PC no necesita tener Android SDK instalado para leer este paquete.

Se rechazan enlaces de filesystem, JSON o manifiestos ambiguos, campos desconocidos, archivos demasiado grandes y referencias de recursos en los campos de identidad. El APK se mantiene abierto contra escrituras durante hash/reinspección/instalación. No se crean copias temporales permanentes del APK para cada intento.

## Comprobaciones y límites

Las pruebas cubren Windows sin campo usb:, selección/versión modificada, usuario secundario, respuestas ambiguas, firma incompatible, cancelación, hash cambiado, actualización y comprobación posterior. El lector también se probó con un manifiesto binario real del SDK android-36/android.jar, sin generar un APK.

La consulta ADB real de esta sesión detectó el Samsung USB autorizado y usuario 0, sin com.novora.appcontrol instalada. Fue sólo lectura; no prueba instalación, actualización, firma o conservación de datos. Ver Resultado-verificacion.md para los resultados finales.

Pendiente: APK único firmado, paquete Android completo/manifiesto final, instalación física desde HOME, actualización conservando datos reales y pruebas de desconexión durante instalación. No afirmar compatibilidad universal.

Referencias: [Android Debug Bridge](https://developer.android.com/tools/adb), [manual ADB de AOSP](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/user/adb.1.md) y [formato binario de recursos Android](https://android.googlesource.com/platform/frameworks/base/+/master/libs/androidfw/include/androidfw/ResourceTypes.h).


## Actualización de interfaz, archivos y grabación (15 septiembre 2026)

Consultar [Android universal](NLDocumentationAndroidUniversal.md) para el comportamiento actual, rutas, comandos, APK 1.4.7 y límites comprobados. Las verificaciones históricas de esta sección conservan su alcance original.

## Corrección de arranque 1.4.8 — 15 septiembre 2026

Se reprodujo en Samsung SM-A566E con Android 16 el cierre de 1.4.7: UnsatisfiedLinkError en MainActivity.n_onCreate. Se deshabilita AndroidEnableMarshalMethods para usar registro JNI dinámico. Después apareció InvalidCastException al obtener IWindowManager; se corrige con JavaCast<IWindowManager>().

APK 1.4.8, versionCode 9, instalado mediante actualización conservando datos. Arranque en frío y pantalla de conexión comprobados por ADB y árbol de interfaz; proceso activo tras el arranque. Esto sustituye la ausencia de prueba física del arranque reportada antes; las demás pruebas completas PC↔Android siguen pendientes.

Windows, Android y los tres proyectos auxiliares de VE compilan. 230 pruebas .NET aprobadas. RelayCore Release compila con 21 advertencias existentes y aviso de dependencia net2. Los proyectos WPF se verificaron secuencialmente tras una colisión de archivos generados al intentar compilarlos en paralelo.

Referencia del problema de enlace en .NET: https://github.com/dotnet/maui/issues/35209


## Instalador Windows todo en uno — 15 septiembre 2026

La prerelease incluye instalador por usuario para Windows 11 24H2 o posterior x64, con .NET, herramientas, dependencia Visual C++ del relay y APK Android 1.4.8. [Descarga, instalacion y advertencias](NLDocumentationInstaller.md). [Pruebas del instalador y limites](NLDocumentationInstallerVerification.md). Los textos anteriores conservan su alcance historico; instalar no certifica todas las funciones PC-Android.


## Numeración y próxima release oficial

1.4 identifica la versión de NOVORA-LINK; .x es la revisión interna de compilaciones o ediciones. Consulta [numeración y aviso de 1.4 oficial](NLDocumentationReleaseVersion.md) y [pruebas de esta revisión](NLDocumentationReleaseVersionVerification.md). La revisión Windows 1.4.1-experimental comprueba una vez al abrir si existe una release oficial posterior.

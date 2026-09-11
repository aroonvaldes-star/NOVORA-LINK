# Diseño: gestión automática del cliente Android de NOVORA-LINK

Fecha: 2026-09-10

## Objetivo

NOVORA para Windows debe hacerse cargo del ciclo de vida del cliente Android necesario para LinkEngine. El usuario no debe abrir PowerShell, seleccionar un dispositivo en Visual Studio, instalar el APK manualmente ni conocer el nombre de la Activity.

Al iniciar una sesión LinkEngine con un Android ADB autorizado, NOVORA comprobará si el cliente Android está instalado y si corresponde actualizarlo. Cuando falte o el APK empaquetado sea más nuevo, NOVORA lo instalará con `adb install -r`, conservará los datos de la aplicación, resolverá la Activity launcher real del paquete instalado, la abrirá con el token de sesión y sólo después esperará el HELLO/ACK del protocolo.

## Contrato del paquete Android

- Package/ApplicationId esperado: `com.novora.linkengine`.
- APK distribuido por NOVORA: `Tools\Android\NOVORA.LinkEngine.Android.apk`.
- Manifiesto distribuido: `Tools\Android\NOVORA.LinkEngine.Android.manifest.json`.
- La Activity principal seguirá siendo exportada y `MainLauncher=true` en el proyecto Android.
- El nombre Java de la Activity puede seguir siendo completamente calificado para satisfacer .NET for Android; Windows no dependerá de ese nombre fijo para lanzarla.
- Extra de Intent requerido por el cliente Android: `novora_remote_token`.

El manifiesto tendrá este contrato lógico:

```json
{
  "packageName": "com.novora.linkengine",
  "versionCode": 1,
  "versionName": "1.0.0",
  "sha256": "..."
}
```

`versionCode` será la autoridad para actualizar. `sha256` permitirá comprobar que el APK empaquetado corresponde al artefacto esperado antes de instalarlo. El manifiesto se genera durante Release; NOVORA Runtime no necesita `aapt`, `apkanalyzer`, Visual Studio ni Android SDK.

## Empaquetado

El proyecto Windows ya copia `Tools\**\*` al directorio de salida con `CopyToOutputDirectory=PreserveNewest`. El APK y su manifiesto se ubicarán dentro de `src\NOVORA\Tools\Android\`, de forma que viajen con la salida de NOVORA.

El instalador de Inno Setup ya copia de forma recursiva el directorio publicado. Por ello, ambos artefactos llegarán a `{app}\Tools\Android\` sin requerir una regla independiente mientras el proceso de publicación preserve el contenido de `Tools`.

La compilación/release del proyecto Android debe producir el APK y generar su manifiesto antes de publicar Windows. Esta preparación pertenece al proceso de release, no al runtime de NOVORA.

## Nuevo componente: ManagerAndroidClientLE

Se añadirá `src/NOVORA/LinkEngine/Android/ManagerAndroidClientLE.cs`. Su namespace será `NOVORA.LinkEngine.Android` y evitará que `ManagerRuntimeLE` conozca detalles de paquetes Android, versionado e invocaciones `am`.

Responsabilidades:

1. Confirmar que el dispositivo continúa ADB ONLINE antes de tocar el cliente Android.
2. Confirmar que existen el APK y el manifiesto empaquetados.
3. Validar `packageName`, `versionCode` y `sha256` del manifiesto.
4. Leer la versión instalada de `com.novora.linkengine` mediante Package Manager.
5. Instalar cuando el paquete no exista.
6. Actualizar con `adb install -r` únicamente cuando el APK distribuido sea más nuevo.
7. No reinstalar cuando la misma versión o una versión más nueva ya esté instalada.
8. Resolver la Activity `MAIN` + `LAUNCHER` del paquete instalado.
9. Lanzar esa Activity con `novora_remote_token`.
10. Devolver un resultado estructurado y un mensaje útil a Runtime.

El componente reutilizará `AdbService`; no iniciará procesos `adb.exe` directamente.

## Comparación de versiones

La autoridad para decidir actualización será `versionCode`, no el texto `versionName`.

- `installedVersionCode`: leído del paquete instalado con Package Manager.
- `bundledVersionCode`: leído del manifiesto distribuido y validado contra el APK mediante SHA-256.

Reglas:

- Paquete ausente -> instalar.
- `bundledVersionCode > installedVersionCode` -> actualizar con `install -r`.
- `bundledVersionCode == installedVersionCode` -> no reinstalar.
- `bundledVersionCode < installedVersionCode` -> no hacer downgrade automáticamente.

Si el manifiesto es inválido, el hash no coincide o la versión instalada no puede determinarse de forma fiable, NOVORA no reemplazará silenciosamente una instalación existente. La etapa fallará con un diagnóstico concreto.

## Resolución y lanzamiento de Activity

NOVORA no construirá un component name fijo como `com.novora.linkengine/.MainActivity` ni `com.novora.linkengine/com.novora.linkengine.MainActivity`.

Después de garantizar la instalación correcta, `ManagerAndroidClientLE` consultará a Android por la Activity `MAIN/LAUNCHER` de `com.novora.linkengine`. Si Android devuelve un componente válido, NOVORA utilizará exactamente ese componente para abrir la aplicación y adjuntar `novora_remote_token`.

Si no existe una Activity launcher resoluble, la operación falla inmediatamente con `ANDROID CLIENT: LAUNCHER NOT FOUND`. No se espera el timeout de HELLO/ACK.

## Integración con ManagerRuntimeLE

`src/NOVORA/LinkEngine/Runtime/ManagerRuntimeLE.cs` integrará el componente con este orden:

1. Inicializar Engine.
2. Conectar ManagerDeviceLE.
3. Confirmar ADB ONLINE.
4. Registrar Recovery.
5. Abrir y verificar ManagerTransportLE.
6. Preparar y lanzar el cliente Android mediante `ManagerAndroidClientLE.EnsureReadyAndLaunchAsync(...)`.
7. Si el paso anterior falla, ejecutar el flujo de fallo controlado de Runtime y no esperar HELLO.
8. Cambiar el estado a `WaitingForAndroid`.
9. Esperar HELLO/ACK.
10. Iniciar data plane/Internet.
11. Esperar salud inicial.
12. Iniciar MonitorRecoveryLE.
13. Publicar Runtime HEALTHY.

La preparación/lanzamiento ocurre después de que el transporte esté listo, para que el Android pueda conectar inmediatamente al listener/reverse preparado, y antes de `WaitForHandshakeAsync`, para evitar el timeout actual cuando la app nunca llegó a abrirse.

## Extensiones de AdbService

`src/NOVORA/Services/AdbService.cs` seguirá siendo el único ejecutor de ADB. Sólo se añadirán helpers pequeños si no existen equivalentes:

- consultar `versionCode` de un paquete;
- resolver la Activity launcher de un paquete;
- lanzar un componente con extras sin registrar el valor del token;
- reutilizar `InstallAsync(..., -r)` para instalación/actualización.

No se creará un segundo wrapper de `adb.exe`.

## Estados y errores

Los mensajes distinguirán al menos estos casos:

- `ANDROID CLIENT: APK MISSING` — falta el APK distribuido.
- `ANDROID CLIENT: MANIFEST INVALID` — manifiesto ausente/inválido o hash no coincidente.
- `ANDROID CLIENT: INSTALLING` — primera instalación.
- `ANDROID CLIENT: UPDATING` — actualización por versionCode.
- `ANDROID CLIENT: READY` — versión instalada válida.
- `ANDROID CLIENT: PACKAGE QUERY FAILED` — Package Manager no respondió de forma válida.
- `ANDROID CLIENT: INSTALL FAILED` — ADB no pudo instalar/actualizar.
- `ANDROID CLIENT: LAUNCHER NOT FOUND` — el paquete no expone Activity launcher.
- `ANDROID CLIENT: LAUNCH FAILED` — Android rechazó el lanzamiento.

Los errores incluirán el detalle técnico útil de ADB en `LastError` o equivalente, pero el token de sesión se redactará y nunca aparecerá en logs o UI.

## Seguridad y privacidad

- El token de sesión nunca se persistirá en disco.
- El token no se incluirá en mensajes de error, logs de diagnóstico ni telemetría.
- No se añade telemetría.
- El APK se valida contra el SHA-256 declarado en el manifiesto antes de instalarse.
- No se desinstala el cliente Android como parte de la actualización normal.
- No se hacen downgrades automáticos.
- No se modifican ajustes permanentes del dispositivo fuera de lo que ya requiere LinkEngine.

## Recuperación

Si el dispositivo pasa a OFFLINE durante instalación o lanzamiento, la operación termina y Runtime entra en estado degradado/fallido según el flujo existente. Recovery podrá reintentar una nueva sesión cuando ADB vuelva a estar disponible, pero no ejecutará instalaciones repetidas en un bucle agresivo.

Una sesión recuperada volverá a validar la disponibilidad del paquete antes de relanzar el cliente cuando sea necesario reconstruir el handshake. Si la versión ya coincide, este paso no reinstala el APK.

## Archivos previstos

La implementación se concentrará en:

- `src/NOVORA/LinkEngine/Android/ManagerAndroidClientLE.cs` — nuevo componente de gestión del cliente Android.
- `src/NOVORA/LinkEngine/Runtime/ManagerRuntimeLE.cs` — integrar preparación/lanzamiento antes de HELLO/ACK.
- `src/NOVORA/Services/AdbService.cs` — helpers ADB reutilizables que falten.
- `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.apk` — APK distribuido.
- `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.manifest.json` — metadata/hash de release.
- proceso de release/build — copiar el APK Release Android y generar el manifiesto antes de publicar Windows.
- `Installer/NOVORA.Installer.iss` — sólo se modificará si una prueba de publish demuestra que `Tools\Android` no llega al instalador; la configuración actual debería incluirlo recursivamente.
- pruebas de regresión existentes o nuevas para instalación, actualización, no-downgrade, hash, resolución y lanzamiento.

`MainActivity.cs` no necesita otro cambio para resolver `Error type 3`; la corrección se realiza en la gestión y lanzamiento desde Windows.

## Pruebas

Se validarán como mínimo los siguientes escenarios:

1. Paquete no instalado -> NOVORA instala el APK y lo lanza.
2. Paquete con versionCode menor -> NOVORA ejecuta actualización `-r` y lo lanza.
3. Mismo versionCode -> no reinstala; lanza directamente.
4. VersionCode instalado mayor -> no hace downgrade; lanza la versión existente.
5. APK distribuido ausente -> error inmediato y descriptivo.
6. Manifiesto ausente, inválido o SHA-256 incorrecto -> no instala y devuelve error inmediato.
7. Dispositivo OFFLINE -> no intenta instalar ni lanzar.
8. Package Manager no puede resolver launcher -> error inmediato, sin esperar 30 segundos.
9. Lanzamiento exitoso -> Android recibe `novora_remote_token` y se completa HELLO/ACK.
10. Token nunca aparece en logs ni mensajes de error.
11. Publicación/instalador Windows contiene el APK y su manifiesto bajo `Tools\Android`.
12. Compilación Release de Windows y Android permanece sin errores.

## Criterio de éxito

Con un Android autorizado por ADB, el usuario podrá pulsar el flujo normal de conexión en NOVORA y obtener una sesión LinkEngine sana sin usar Visual Studio, PowerShell ni ADB manual. Si el cliente Android falta o está desactualizado, NOVORA lo corrige automáticamente antes del handshake. Si no puede hacerlo, informa la causa real inmediatamente en vez de terminar únicamente con `Error type 3` o `No llegó HELLO Android`.

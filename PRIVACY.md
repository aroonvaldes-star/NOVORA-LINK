# Política de Privacidad de NOVORA-LINK

**Última actualización: 4 de septiembre de 2026**

Esta política describe el comportamiento de privacidad observable en NOVORA-LINK 1.3 y el tratamiento esperado para futuras versiones oficiales del proyecto.

## Resumen

NOVORA-LINK está diseñado para operar principalmente de forma local entre Windows y los dispositivos Android autorizados por el usuario. La revisión del código de la versión 1.3 no identifica un sistema propio de telemetría, analítica publicitaria, perfiles de usuario ni un backend de NOVORA al que se envíen métricas de uso.

NOVORA-LINK sí almacena determinadas preferencias localmente y puede comunicarse con servicios de terceros cuando una función lo requiere, por ejemplo GitHub para comprobar o descargar actualizaciones.

## Datos almacenados localmente

NOVORA-LINK guarda configuración en:

`%LocalAppData%\NOVORA\settings.json`

La configuración actual puede incluir:

- estado de audio;
- monitor seleccionado y nombre del dispositivo de pantalla;
- identificador/serial del dispositivo Android seleccionado;
- bitrate, FPS y resolución máxima;
- tema visual;
- nombres personalizados asignados por el usuario a dispositivos y monitores.

Estos datos se usan para recordar preferencias y no deben enviarse a un servicio de NOVORA salvo que una función futura lo indique expresamente y esta política se actualice antes de hacerlo.

## Datos temporales

El actualizador puede usar la carpeta temporal de Windows para descargar y verificar instaladores, por ejemplo bajo:

`%TEMP%\NOVORA\Updates\`

También puede generarse un registro local de error de actualización bajo `%TEMP%\NOVORA\` si una actualización falla.

## Comunicación con Android

Cuando el usuario conecta o autoriza un dispositivo, NOVORA-LINK puede utilizar ADB, scrcpy, Gnirehtet y, en versiones futuras, LinkEngine/ScreenEngine para realizar funciones solicitadas por el usuario, entre ellas:

- descubrir y consultar el estado del dispositivo;
- mostrar o controlar la pantalla;
- transportar audio o eventos de entrada;
- instalar o ejecutar componentes Android necesarios;
- proporcionar conectividad de red mediante reverse tethering/VPN.

Los identificadores y datos técnicos necesarios para estas operaciones pueden circular entre la PC y el dispositivo autorizado. La versión 1.3 no está diseñada para enviar esos datos a un servidor propio de NOVORA.

## Pantalla, audio, portapapeles y archivos

Las funciones de control remoto pueden procesar contenido visible o accesible en el dispositivo cuando el usuario activa esas funciones. Ese contenido debe permanecer en la sesión local PC ↔ Android salvo que el propio usuario lo envíe mediante otra aplicación o servicio.

NOVORA-LINK no debe conservar grabaciones, audio, contenido del portapapeles o archivos de forma permanente por defecto sin una acción explícita del usuario.

## Reverse tethering y tráfico de red

Cuando se activa Internet USB/reverse tethering, el tráfico del dispositivo puede salir a Internet a través de la conexión de Windows. Los servicios y sitios a los que acceda el dispositivo podrán observar la dirección IP y demás información normal de esa conexión.

NOVORA-LINK no afirma controlar las prácticas de privacidad de los sitios, aplicaciones, proveedores de Internet ni servicios de terceros utilizados por el usuario.

## GitHub y actualizaciones

El actualizador de NOVORA-LINK puede consultar la API de GitHub por HTTPS para conocer releases y descargar instaladores. En consecuencia, GitHub puede recibir información normal de una petición de red, como la dirección IP y encabezados HTTP, conforme a sus propias políticas.

NOVORA-LINK debe verificar la integridad criptográfica SHA-256 del instalador antes de ejecutarlo cuando ese mecanismo esté disponible en la release.

## Telemetría y publicidad

En la revisión actual de NOVORA-LINK 1.3 no se identificó código de telemetría, analítica publicitaria ni venta de datos personales.

Si en una versión futura se introduce telemetría remota, cuentas, sincronización en la nube o cualquier recolección adicional, la política deberá actualizarse antes de la distribución de esa versión y la interfaz deberá explicar qué datos se recaban y con qué finalidad.

## Cómo borrar datos locales

El usuario puede cerrar NOVORA-LINK y eliminar `%LocalAppData%\NOVORA\settings.json` para borrar las preferencias locales de la aplicación. También puede eliminar los archivos temporales de `%TEMP%\NOVORA\` cuando no haya una actualización en curso.

La desinstalación de la aplicación puede no borrar automáticamente todos los archivos de configuración o temporales de Windows.

## Terceros

NOVORA-LINK integra software de terceros sujeto a sus propias licencias y políticas. Consulta `THIRD-PARTY-NOTICES.md` y `ACKNOWLEDGEMENTS.md` para identificar los principales componentes.

## Cambios en esta política

Los cambios materiales de privacidad deben reflejarse en este archivo y, cuando afecten al comportamiento de una release pública, en sus notas de versión.

## Alcance

Esta política describe el comportamiento del software y del repositorio oficial. No sustituye las políticas de GitHub, Microsoft, Google/Android, fabricantes de dispositivos, operadores de red ni aplicaciones utilizadas desde Android.

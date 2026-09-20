# NOVORA-LINK 1.4 — Experimental 1
## Descarga, instalación y advertencias

Esta prerelease es una versión de prueba. El instalador reúne NOVORA PC 1.4.1-experimental, .NET, ADB, herramientas de pantalla y audio, LinkEngine con su dependencia de Microsoft Visual C++ y NOVORA Android 1.4.8. No necesitas instalar .NET, Java ni Android Studio para utilizar el paquete Windows. Los controladores USB específicos del fabricante, cuando sean necesarios, se obtienen de su fabricante.

## 1. Requisitos de esta edición

- Windows 11 24H2 o posterior, x64 (compilación 26100 o posterior). El instalador respeta el mínimo declarado por este paquete; no certifica todos los equipos. Esta edición no admite Windows de 32 bits ni ARM64.
- Android 8.0 o posterior (API 26), según el mínimo del APK. La compatibilidad con cada fabricante y versión debe comprobarse.
- Cable USB de datos, teléfono desbloqueado y depuración USB autorizada para conexión por cable.
- Misma red local para control LAN. La PC necesita Internet para compartirlo por USB.
- Espacio suficiente en disco: el asistente calcula el espacio del programa. Reserva espacio adicional para archivos y grabaciones.
- NVIDIA es opcional; el paquete conserva los decodificadores por software. No se promete el mismo rendimiento en todos los equipos.

## 2. Cómo descargar

1. Abre https://github.com/aroonvaldes-star/NOVORA-LINK/releases/tag/v1.4.0-experimental.1
2. Busca la sección Assets (archivos adjuntos).
3. Descarga NOVORA-LINK-1.4.1-experimental-Setup-x64.exe para instalar en Windows.
4. No elijas Source code (zip/tar.gz): esos archivos contienen el código, no el instalador.
5. El ZIP Windows es la alternativa portátil: extrae TODO su contenido y abre NOVORA.exe. No necesitas instalar ambos formatos.
6. La app Android ya está incluida en el instalador Windows. El APK independiente es opcional si deseas instalarlo manualmente.

Comprobación opcional de integridad: descarga SHA256SUMS-Aviso-1.4.txt desde la misma prerelease. En PowerShell, situado en tu carpeta de descargas, ejecuta:

Get-FileHash -LiteralPath '.\NOVORA-LINK-1.4.1-experimental-Setup-x64.exe' -Algorithm SHA256

Compara todos los caracteres con el archivo de huellas. Una huella comprueba integridad; no sustituye una firma digital ni certifica la ausencia de errores.

## 3. Cómo instalar en Windows

1. Finaliza las grabaciones y transferencias y cierra NOVORA antes de instalar o actualizar.
2. Ejecuta NOVORA-LINK-1.4.1-experimental-Setup-x64.exe.
3. Lee las advertencias del asistente y elige la carpeta. La ubicación predeterminada es %LOCALAPPDATA%\Programs\NOVORA-LINK Experimental.
4. Si lo deseas, marca el acceso directo del escritorio y pulsa Instalar.
5. Abre NOVORA-LINK Experimental desde Inicio de Windows. También encontrarás la guía y el desinstalador en ese grupo.

La instalación es para tu usuario y no solicita elevación de administrador. Al abrir la aplicación, NOVORA puede registrar su inicio con Windows (preferencia activada por defecto); puedes deshabilitar NOVORA-LINK en Configuración de Windows > Aplicaciones > Inicio. El paquete se instala sin descargar componentes adicionales. No concede permisos al teléfono, no instala controladores USB y no abre reglas de firewall automáticamente.

## 4. Instalar NOVORA en Android desde la PC

1. Conecta un solo teléfono mediante un cable de datos, usando el usuario principal de Android.
2. Activa la depuración USB en las opciones de desarrollador y acepta en el teléfono la autorización de tu computadora.
3. Abre NOVORA PC, entra en Inicio y selecciona el dispositivo.
4. Pulsa COMPROBAR APP y después INSTALAR NOVORA POR USB o ACTUALIZAR NOVORA ANDROID, según corresponda.
5. Revisa el teléfono y las versiones indicadas, confirma y espera el resultado.
6. La instalación puede desconectar el control y la VPN. Vuelve a preparar la conexión cuando termine.

Si Android rechaza una firma incompatible, detén el intento y revisa la versión. No desinstales la app solo para eludir el rechazo: podrías perder sus datos. Si la instalación se interrumpe, pulsa COMPROBAR APP antes de repetirla.

Alternativa manual: descarga el APK independiente de esta misma prerelease. Android puede pedir permiso al navegador o administrador de archivos para instalar aplicaciones de esa fuente. Concédelo únicamente si reconoces el archivo; puedes retirarlo al terminar. No desactives Play Protect ni otras protecciones para forzar una instalación bloqueada.

## 5. Primera conexión y uso

USB: en Inicio de PC pulsa PREPARAR CONTROL USB. En Android selecciona Conectar por USB e introduce el código de ocho dígitos. Caduca en dos minutos. Instalar la app no vincula el control automáticamente.

Pantalla: selecciona teléfono, monitor y salida de audio; guarda las opciones y pulsa INICIAR en PC. VisionEngine también puede iniciarse desde Control en Android cuando PC anuncia la capacidad. Los cambios de video se aplican al iniciar o reiniciar la transmisión.

LAN: pulsa PREPARAR LAN / QR en PC, elige la red compartida y escanea su QR desde Android. Encontrar una PC en la búsqueda no la autoriza. No compartas el QR ni los códigos. La conexión de control LAN no prepara por sí sola el enlace ADB necesario para capturar la pantalla. Usa redes de confianza; no abras puertos del router a Internet.

Internet USB: requiere control USB autorizado para el mismo teléfono. Inicia LinkEngine desde Android, lee el aviso y acepta el permiso VPN si deseas continuar. Espera el estado y comprueba navegación real. Aceptar la solicitud no demuestra que ya haya Internet. Cambiar a control LAN o perder el USB cierra este túnel. Al detenerse, el teléfono puede volver a Wi-Fi o datos móviles. No es una VPN comercial ni garantiza anonimato.

Archivos, capturas y grabaciones: se guardan en el Escritorio real de Windows, dentro de NOVORA-Files, organizados por tipo. En Android se utiliza la carpeta compartida NOVORA. Espera la confirmación antes de desconectar.

## 6. Advertencias de la prerelease

- Versión experimental: puede fallar, desconectarse o no completar una transferencia/grabación. Conserva copias de archivos importantes y comprueba el resultado antes de borrar el original.
- El instalador NOVORA no tiene firma digital de editor. Windows o el navegador pueden mostrar avisos de archivo poco conocido o editor desconocido. Comprueba el origen y la huella. Si no confías en el archivo, cancela. No desactives Defender ni agregues exclusiones generales; una alerta no debe suponerse falsa.
- El APK utiliza una firma de desarrollo para pruebas. No es una distribución estable de tienda; no equivale a una aplicación certificada para todos los teléfonos.
- El instalador no concede permisos de cámara, carpeta, superposición o VPN. Android los solicita según la función. Otórgalos solo cuando quieras utilizarla.
- Pantalla, control remoto, portapapeles y archivos pueden mostrar o transmitir datos personales. Vincula únicamente tus equipos, no publiques códigos/QR ni incluyas secretos en capturas o reportes.
- Al usar LAN, Windows puede pedir permiso de red. Revisa que se trate de NOVORA y de tu red privada. No desactives el firewall completo.
- Esta revisión comprueba una vez al abrir si existe una release oficial posterior y muestra un enlace para verla. La descarga e instalación requieren tu acción. Las revisiones experimentales se descargan manualmente; no se anuncian como versiones oficiales.
- La carpeta de instalación experimental es independiente, pero la aplicación conserva su ubicación de preferencias %LOCALAPPDATA%\NOVORA. No se promete aislamiento de ajustes respecto de otras copias de NOVORA; evita ejecutarlas simultáneamente.
- La grabación requiere H.264 y audio del teléfono activos. H.265/AV1 no están habilitados para grabación. Ocultar la burbuja en capturas es experimental. Compartir admite hasta 100 archivos por selección y 2 GiB por archivo.

## 7. Qué está comprobado y qué sigue pendiente

Los informes previos de esta versión registran compilaciones Windows, Android y RelayCore, 230 pruebas automatizadas .NET y arranque de Android 1.4.8 en Samsung SM-A566E con Android 16. Son resultados de la versión existente, no una nueva prueba física realizada por el instalador.

Las pruebas de esta revisión se publican por separado en Verificacion-aviso-1.4.md; el informe Verificacion-instalador.md corresponde al instalador experimental anterior. Instalar o abrir correctamente no certifica Internet USB real, latencia de juegos, todas las funciones PC-Android, sesiones largas, permisos en todos los fabricantes ni compatibilidad universal. La revisión de archivos tampoco equivale a una auditoría de seguridad completa.

## 8. Actualizar o desinstalar

Para actualizar, cierra NOVORA y ejecuta un instalador posterior compatible del mismo canal experimental. Revisa siempre sus notas. Esta primera edición no incluye una prueba de actualización desde versiones futuras. No reemplaza automáticamente una copia portátil ubicada en otra carpeta.

Para desinstalar: Configuración de Windows > Aplicaciones > Aplicaciones instaladas > NOVORA-LINK Experimental > Desinstalar. También puedes utilizar el acceso del menú Inicio. Cierra las sesiones antes de hacerlo.

El desinstalador retira los archivos que instaló y sus accesos directos. Retira también la entrada de inicio automático solo si apunta exactamente a la copia que se está desinstalando; conserva la de otra copia. Conserva los archivos personales de NOVORA-Files y las preferencias/confianza guardadas fuera de la carpeta del programa. No desinstala la app del teléfono ni revoca automáticamente los permisos guardados en Android.

## 9. Reportar problemas

https://github.com/aroonvaldes-star/NOVORA-LINK/issues/new

Incluye versión, Windows, modelo y versión de Android, conexión USB/LAN, pasos, resultado esperado y mensaje de error. Oculta información personal. Los comentarios y sugerencias ayudan a mejorar NOVORA.

# Numeración y paso a NOVORA-LINK 1.4 oficial

## Qué significa el número

**1.4 es la versión de NOVORA-LINK.** El número adicional **.x** identifica una revisión interna de trabajo: las compilaciones o ediciones que se fueron preparando para llegar a ese punto. No representa por sí solo una nueva versión principal ni una función adicional.

Ejemplo: **1.4.8 sigue perteneciendo a NOVORA-LINK 1.4**; el 8 es una referencia interna de desarrollo. Para utilizar la app puedes ignorar ese contador y fijarte en **1.4** y en si la publicación está marcada como **experimental** u **oficial**. Conserva el número completo únicamente si vas a reportar un problema.

PC y Android pueden tener contadores distintos dentro de la misma versión 1.4. El contador identifica las revisiones entregadas; no se utiliza como una certificación de calidad ni como prueba de que haya más funciones.

## Próxima publicación prevista

La próxima versión pública está prevista como **NOVORA-LINK 1.4 oficial**. Esta publicación sigue siendo una prerelease y no se convierte automáticamente en estable. La fecha y publicación oficial quedan pendientes de las comprobaciones y autorización correspondientes.

## Aviso desde esta prerelease

La revisión Windows **1.4.1-experimental** incorpora el aviso. Consulta una sola vez al abrir NOVORA el canal oficial de GitHub. Cuando exista una release oficial posterior válida, muestra **VER 1.4 OFICIAL** para abrir su publicación, instrucciones y descarga. No descarga ni instala automáticamente; no hay sondeo periódico. Si falla la consulta, se vuelve a intentar al abrir la aplicación de nuevo.

La transición tiene en cuenta el canal: **1.4 oficial sustituye a una 1.4.x experimental aunque el contador interno experimental sea mayor**. No anuncia versiones anteriores ni otras prereleases.

Quienes descargaron el primer instalador o ZIP experimental deben instalar esta revisión corregida una vez para obtener el nuevo aviso. Modificar las notas de GitHub no cambia las aplicaciones que ya están instaladas.

## Desde la 1.3 y la 1.3.1

En las fuentes históricas revisadas, se consulta al pulsar **Actualizar**. Cuando se publique la 1.4 oficial con el instalador compatible, podrán detectarla por esa opción. No se promete un aviso automático al iniciar versiones antiguas.

Algunas instalaciones históricas consultan NOVORA-PROYECT y necesitan pasar primero por el **puente 1.3.1** publicado allí, que dirige sus consultas al repositorio oficial. La revisión fue de fuente y API; no certifica el contenido ni la instalación de todos los binarios históricos.

La detección y la instalación son comprobaciones distintas: la migración desde la instalación antigua de 1.3, que utilizaba otra carpeta e instalación con administrador, debe probarse antes de declarar compatible la release oficial.

## Contrato para preparar la release oficial (mantenimiento)

1. Compilar la app como estable, sin sufijo experimental. Ejemplo de versión: **1.4.0**. Verificar también AssemblyInformationalVersion; no debe conservar el sufijo experimental.
2. Preparar el tag **v1.4.0** sobre el código realmente verificado. No mover ni convertir el tag experimental.
3. Preparar un instalador de la release oficial con el nombre **NOVORA-Setup-1.4.0.exe**, para que lo reconozcan los actualizadores históricos 1.3/1.3.1.
4. Comprobar instalación desde cero y migración desde 1.3, puente 1.3.1 y esta prerelease, incluidos carpetas, accesos, preferencias, permisos y reinicio de la copia correcta. No basta con renombrar el instalador experimental.
5. Subir primero el instalador validado y comprobar que el API de GitHub informa su **digest sha256** de 64 caracteres. El archivo lateral SHA256SUMS no sustituye ese campo para los clientes históricos.
6. Publicar como **release estable**, sin draft ni prerelease, y marcarla **Latest**. Hasta entonces los clientes que consultan /latest siguen viendo la última estable anterior; no se debe anunciar la experimental como oficial para forzar el aviso.
7. Verificar desde la nueva prerelease el aviso de la 1.4 oficial y desde 1.3/1.3.1 su opción Actualizar. Publicar el estado real de esas pruebas.

Esta preparación no publica anticipadamente una 1.4 oficial ni programa una publicación automática.

## Referencias de compatibilidad histórica

- https://github.com/aroonvaldes-star/NOVORA-LINK/blob/v1.3/src/NOVORA/Services/UpdateService.cs
- https://github.com/aroonvaldes-star/NOVORA-LINK/blob/v1.3.1/src/NOVORA/Services/UpdateService.cs
- https://github.com/aroonvaldes-star/NOVORA-PROYECT/releases/tag/v1.3.1

## Verificación de esta revisión

Ver el informe Verificacion-aviso-1.4.md adjunto a la prerelease para los resultados nuevos. Las pruebas con respuestas de una futura release son simulaciones del contrato; no significan que 1.4 oficial ya se haya publicado.
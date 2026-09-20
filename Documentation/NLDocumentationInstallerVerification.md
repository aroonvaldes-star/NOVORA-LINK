# Verificación del instalador NOVORA-LINK 1.4 Experimental 1

Fecha: 15 de septiembre de 2026. Sistema de prueba: Windows 11, compilación 26200, x64.

## Resultado del empaquetado: PASS local

- Compilación del instalador con Inno Setup 7: exit code 0. Instalador por usuario, mínimo Windows build 26100 y arquitectura x64.
- Origen: ZIP Windows de la prerelease v1.4.0-experimental.1, SHA-256 631be68de1c53cb5eca334ea8f89bd101cbb9144758b6be9e03d56d9dd4b8157.
- Se conservaron los binarios originales Windows, Android y herramientas del ZIP; se añadieron guía de instalación y VCRUNTIME140.dll de Microsoft junto al relay, con aviso de procedencia/licencia. No se recompilaron los motores ni el APK para este instalador.
- APK Android 1.4.8: SHA-256 e8de4485731bb35f0939bbadfecd442636f1d4076f252ae933d338cc41fe2157; coincide con el descriptor y la descarga publicada.
- Dependencia VCRUNTIME140.dll x64: firma Microsoft válida, versión 14.51.36247.0, SHA-256 d1f4225df2cd877dbf130d5668a021dce3f94118455ff5ec952061c30afc9ce7. Procede de VC/Redist/MSVC/14.51.36231/x64/Microsoft.VC145.CRT de Visual Studio 2026; no se extrajo de System32.
- Instalación real en una carpeta temporal con espacios: exit code 0. Se compararon los 493 archivos instalados con el manifiesto SHA-256 del payload.
- Se comprobaron tres accesos del menú Inicio: aplicación, guía y desinstalador.
- Arranque de NOVORA instalado: proceso activo tras 15 segundos, ventana titulada NOVORA-LINK 1.4 y coreclr.dll cargado desde la carpeta instalada. Esta es una prueba de arranque, no de interacción completa con la interfaz.
- Arranque de LENetworkRelay.exe instalado: VCRUNTIME140.dll cargado desde la copia local del paquete. Conexión localhost a tcp:27184 y respuesta de cuatro bytes 00000000. No equivale a Internet Android ni a prueba de rendimiento.
- Dos ciclos reales de instalación/desinstalación: exit code 0. El programa y el registro de desinstalación se retiraron. Los accesos directos de la primera desinstalación se retiraron.
- Se conservó un archivo ajeno al instalador colocado en su carpeta.
- Se conservó una entrada de inicio con Windows cuyo comando apuntaba a otra copia; se retiró la entrada cuando apuntaba exactamente a la copia desinstalada.
- La prueba restauró la entrada de inicio y el contenido previo de settings.json. No instaló ni cambió la app del teléfono.
- Firma del compilador Inno Setup: válida, Pyrsys B.V. El instalador NOVORA generado está SIN firma digital de editor (NotSigned).

## Segunda revisión

Una revisión independiente detectó dos problemas antes de la publicación: dependencia Visual C++ ausente para el relay y entrada de inicio automático sin retirar al desinstalar. Ambos se corrigieron; la nueva revisión estática y las pruebas instaladas comprobaron las correcciones.

La primera ejecución de la prueba esperaba un grupo personalizado del menú Inicio. El instalador creó correctamente su grupo fijo NOVORA-LINK Experimental. Se corrigió la expectativa de la prueba, se retiró esa instalación temporal y se repitió el ciclo completo con resultado PASS. No se presenta el intento inicial como exitoso.

## Límites

- Prueba en el Windows disponible, no en una máquina virtual limpia ni en varias marcas. Se comprobó la carga de .NET y Visual C++ desde la copia instalada, pero no se desinstalaron los runtimes globales del equipo.
- No se probaron Windows 24H2, Windows ARM, Windows 10, equipos de 32 bits ni todos los fabricantes Android. El instalador excluye plataformas inferiores al mínimo declarado y arquitecturas distintas de x64.
- La prueba usó instalación y desinstalación silenciosas. No se recorrieron manualmente todas las pantallas del asistente ni se probaron descargas con SmartScreen en otro equipo.
- Se finalizó únicamente el proceso NOVORA creado para la prueba; no se considera una prueba de cierre normal de la app. Tampoco se probaron actualización con la app abierta ni migraciones futuras.
- No se probó Internet USB real, VPN en Android, permisos, transferencia PC-teléfono, grabaciones largas, privacidad sobre otras apps ni mandos durante este empaquetado.
- Las 230 pruebas .NET y el arranque previo en Samsung corresponden a los informes de la versión anterior al empaquetado; no se repitieron aquí.
- No se ejecutó un escaneo completo de Codex Security, CircleCI ni pruebas Test Android Apps. La revisión de dependencias y hashes no equivale a una auditoría completa de vulnerabilidades o licencias de todas las dependencias anteriores.

## Descargas anteriores

El ZIP y APK originales y su SHA256SUMS.txt se conservan sin cambios. El nuevo SHA256SUMS-Instalador.txt incluye la huella del instalador y los adjuntos nuevos. El tag v1.4.0-experimental.1 y su commit permanecen iguales; las fuentes del empaquetado y su entrega PowerShell se adjuntan por separado.
## Instalador publicado

Nombre: NOVORA-LINK-1.4-experimental.1-Setup-x64.exe

Tamaño: 143537637 bytes.

SHA-256: cdf14a27f839b722b595164041d40753f1756a51bbbf4d954497651c352263b4
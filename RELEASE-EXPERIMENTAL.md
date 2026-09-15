# NOVORA-LINK 1.4 — Experimental 1

**Esta pre-release todavía está en fase experimental.** Puede presentar errores, comportamientos inesperados y funciones que necesiten ajustes. La publicamos para que puedan probar los avances y ayudarnos a mejorar NOVORA.

**Con mucho gusto recibimos sus comentarios, sugerencias y reportes de errores de la app. Su ayuda es muy valiosa: nos permite encontrar problemas, entender qué necesitan y mejorar la experiencia para todos. ¡Gracias por acompañarnos en el desarrollo de NOVORA!**

## Descargas

- **Windows x64:** `NOVORA-LINK-1.4-experimental.1-Windows-x64.zip`. Extrae toda la carpeta y abre `NOVORA.exe`. Incluye el runtime .NET; no ejecutes el programa directamente dentro del ZIP. Aplicación de PC: 1.4.0. Plataforma de compilación: Windows 10 SDK 26100; la compatibilidad con distintas versiones de Windows y equipos sigue en pruebas.
- **Android:** `NOVORA-Android-1.4.8-experimental.apk`. Android 8.0 o posterior (API 26). Actualiza la app existente cuando la firma coincida. APK con firma de desarrollo para pruebas; no es una distribución estable de tienda.
- `SHA256SUMS.txt` permite comprobar la integridad de las descargas.

## Novedades incluidas para probar

- Interfaz Android de conexión, control y configuraciones.
- Control flotante de VisionEngine y accesos opcionales a aplicaciones elegidas por cada usuario.
- Envío de archivos desde Android hacia PC, incluido Compartir desde otras apps.
- Archivos organizados por tipo en `Escritorio/NOVORA-Files` y `Almacenamiento interno/NOVORA`.
- Capturas PNG y grabación MKV en PC con video H.264 y audio del teléfono, sin micrófono.
- Correcciones de arranque de Android 1.4.8 y actualización del APK incluido en PC.

## Estado de las pruebas y límites conocidos

Se compilaron Windows, Android, RelayCore y los tres proyectos auxiliares de VisionEngine. Pasaron 230 pruebas automatizadas .NET. Se comprobó el arranque de Android 1.4.8 en un Samsung SM-A566E con Android 16.

Esto no garantiza todavía el funcionamiento completo en todos los equipos. La conexión PC↔Android, las transferencias, el control flotante y las grabaciones prolongadas requieren más pruebas reales. RelayCore compila con advertencias existentes. La grabación requiere H.264 y audio del teléfono activo; H.265/AV1 no están habilitados para grabación. Ocultar la burbuja en capturas sigue siendo experimental. Compartir admite hasta 100 archivos por selección y 2 GiB por archivo.

## Cómo ayudarnos

- [Reportar un error en Issues](https://github.com/aroonvaldes-star/NOVORA-LINK/issues/new).
- [Compartir ideas y comentarios en Discussions](https://github.com/aroonvaldes-star/NOVORA-LINK/discussions).

Para investigar un problema, nos ayuda conocer la versión de NOVORA, el modelo y sistema del teléfono, la versión de Windows, si usabas USB o LAN, los pasos para reproducirlo y qué esperabas que ocurriera. Puedes adjuntar una captura sin información personal.

**Esta publicación es una pre-release experimental y no sustituye la versión estable.**


## Código de esta publicación

Esta rama contiene una instantánea del código local usado para la pre-release. Los binarios de herramientas, DLL nativas y APK se distribuyen en los adjuntos de la release, no en este árbol de fuentes. No se incluyen cachés, claves de firma ni configuración personal. Para recompilar, se necesitan los SDK .NET 8 y .NET 10 Android, además de las herramientas de ejecución correspondientes.

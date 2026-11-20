# NOVORA 1.2

NOVORA es una aplicación WPF para conectar Android con Windows mediante ADB y Scrcpy.

## Esta revisión

Esta copia corrige la estructura de la interfaz de NOVORA 1.2:

- Se eliminó el `Canvas` de la ventana principal.
- Se eliminó el sistema duplicado de widgets que no estaba conectado correctamente a la UI.
- La configuración ahora controla los paneles que realmente existen.
- Se corrigió la gestión del bitrate y la migración de valores antiguos en `MB/s`.
- El actualizador apunta a `aroonvaldes-star/NOVORA-LINK`.
- La comprobación de actualización puede descargar, verificar SHA-256 e iniciar la instalación oficial con confirmación del usuario.
- Se conserva la base WPF + C# + .NET 8 y las herramientas de `Tools/`.

## Estado

Esta copia está preparada para ser compilada y probada en Visual Studio.

No se declara una release final hasta completar las pruebas de hardware y el ciclo real de actualización 1.2 → 1.3.

## Abrir

Abrir:

`NOVORA.sln`

Proyecto:

`src/NOVORA/NOVORA.csproj`

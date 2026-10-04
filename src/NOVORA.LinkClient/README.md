# NOVORA-LINK — cliente Android independiente

Conversión nativa de las tres interfaces HTML entregadas: Inicio, Engines y Archivos. Es un APK separado de AppControl y se conecta directamente al protocolo TLS fijado de NOVORA PC mediante un código LAN temporal.

## Abrir en Visual Studio

1. Abre `NovoraLink.AndroidXml.csproj`.
2. Selecciona un emulador o dispositivo Android.
3. Ejecuta el proyecto.
4. Edita las pantallas en `Resources/layout` y vuelve a ejecutar para ver los cambios.

El proyecto usa .NET 10 para Android y API mínima 26. No requiere paquetes NuGet externos.

## Archivos principales

- `Resources/layout/activity_home.xml`
- `Resources/layout/activity_engines.xml`
- `Resources/layout/activity_files.xml`
- `Resources/values`: colores, textos, dimensiones y estilos compartidos.
- `Resources/drawable`: tarjetas, botones, estados y barras de progreso.
- `HomeActivity.cs`, `EnginesActivity.cs`, `FilesActivity.cs`: navegación y controles conectados a NOVORA.
- `NovoraConnection.cs`: sesión independiente, emparejamiento LAN y envío de comandos.

## Alcance conectado

- Código LAN temporal y estado vivo de NOVORA PC.
- Consulta y reinicio de VisionEngine.
- Captura de pantalla cuando NOVORA la habilita.
- Estado real de resolución y FPS.

USB y transferencia P2P permanecen aislados en AppControl por ahora; este cliente no reutiliza su servicio ni su identidad de paquete.

Consulta `ASSET_MANIFEST.md` para los recursos visuales que requieren archivos originales o implementación nativa adicional.

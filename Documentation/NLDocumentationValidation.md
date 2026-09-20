# Validacion de la entrega

Actualización NVIDIA/VE: [NVDEC opcional, recuperación y mediciones](../src/NOVORA/NVIDIA/NLNVIDIANvdec.md).

## Perfiles y NVIDIA: revisión posterior

[Informe de correcciones, 79 pruebas y mediciones físicas](../src/NOVORA/NVIDIA/NLNVIDIAValidacion.md).
Ocho muestras de video aislado completadas. El fallo SDL/gamepad observado en la
ejecución anterior queda pendiente; no se confunde con el éxito del video aislado.

## Reorganización NVIDIA — 2026-09-14

- Código específico centralizado en [NVIDIA](../src/NOVORA/NVIDIA/NLNVIDIAArquitectura.md): siete archivos trasladados y renombrados, más el contrato de rutas nativas. Namespace NOVORA.NVIDIA.
- Referencias actualizadas en VisionEngine, UI y mapa histórico de nombres. No quedan referencias activas C# al namespace anterior. Las rutas antiguas del mapa son claves históricas intencionales.
- Segunda revisión: configuración NvidiaProfile y valores del enum conservados; detección bajo demanda y alternativa FFmpeg conservadas; no se agregaron timers. No se encontraron DLL NVIDIA distribuidas para trasladar.
- `dotnet build NLProjectSolution.sln -c Release --no-restore`: correcto, cero errores y advertencias.
- `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore`: 71 aprobadas, cero fallidas y omitidas. Incluye cinco casos nuevos de NVIDIA: evento/estado Disabled, fallback sin puente nativo para tres perfiles y nueva ubicación del componente nativo.
- HeadlessHarness y BlockCHarness: compilaciones Release --no-restore correctas, cero errores y advertencias; no se ejecutaron sesiones físicas de captura.
- `python Tool/NLToolVerify.py`: PASS, 327 archivos propios en la comprobación posterior a los cambios de código.
- Sin backups, instalación de SDK/skills o publicación. La carpeta no contiene repositorio Git; no hay diff Git disponible.
- Pendiente: decodificador nativo, aceleración real, pruebas con teléfono, latencia completa y validación en GPU de distintos fabricantes. La existencia de una DLL o una política UseNvdec no demuestra aceleración activa.

## Evidencia anterior a esta reorganización

- Aplicacion Windows: compilacion Release realizada con cero errores y cero advertencias.
- Pruebas Windows: 66 aprobadas. Incluyen construccion de MainWindow en STA, carga de recursos y comprobacion del boton deshabilitado.
- Harness de VisionEngine Headless y BlockC: compilados con cero errores y cero advertencias. No se ejecutaron pruebas fisicas de captura o rendimiento.
- RelayCore: 34 pruebas aprobadas y una de rendimiento ignorada. Compilacion nativa Release completada; binario resultante incorporado a Tools/LinkEngine/LENetworkRelay.exe.
- Rust conserva advertencias del codigo/dependencias existentes y un aviso de incompatibilidad futura de net2 0.2.39.
- La revision adicional detecto y corrigio rutas WPF, recursos, un nombre de propiedad heredada y la firma del harness BlockC.
- No se ha probado con un telefono conectado ni se afirma compatibilidad con todos los fabricantes.

Tool/NLToolVerify.py revisa nomenclatura del codigo propio, declaraciones de tipos, ausencia de paquetes de la app retirada y presencia de las herramientas ADB/scrcpy. Las claves antiguas de configuracion pueden aparecer en pruebas que verifican compatibilidad con configuraciones existentes; no reactivan la app eliminada.

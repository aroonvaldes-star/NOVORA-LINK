# NOVORA-LINUX: paridad visual Windows y conectividad USB/LAN

Estado: aprobada por el usuario («Va»); cambios locales implementados y probados.
La certificación visual del 100% y la aceptación física siguen pendientes.
Evidencia: `docs/informes/2026-11-07-linux-paridad-windows-usb-lan.md`.

## Solicitud y referencia

El usuario solicita que NOVORA-LINUX tenga el diseño idéntico al de NOVORA-LINK
Windows e integre conectividad USB y LAN. Claude colaborará por separado; su
asignación está pendiente de conocer. No se presume exclusividad de archivos.

La referencia es el código Windows actual: `src/NOVORA LINK/UI/NLUIWindowMain.xaml`,
`Resource/NLResourceMainStyles.xaml`, `Service/NLServiceTheme.cs` y
`Service/NLServiceBrandTheme.cs`. Los renders de `Assets/Diseno` son aproximaciones
y no constituyen criterios de aceptación. La implementación queda en Linux;
no se modifica el cliente Windows para hacerlo coincidir con Linux.
Baseline de código inspeccionado: HEAD `011be06a07164770d8c6bc6de7650cb9921314df`;
antes de implementar se comprobarán cambios concurrentes y el diff Windows.

## Enfoque

Trasladar la estructura WPF a Avalonia y conectar sus acciones a los servicios
Linux existentes. Mantener Avalonia es una decisión técnica propuesta: permite
conservar los motores Linux y usar la referencia Windows como especificación.
Una reinterpretación visual incumpliría la igualdad solicitada. Ejecutar WPF
mediante compatibilidad Windows no constituye una implementación nativa Linux.

## Diferencias verificadas

- La ventana coincide en 1180×800, mínimo 1000×610; ello no prueba igualdad interior.
- Windows tiene barras de 54/56/46 unidades y margen de contenido 22/18/22/22.
- Emblema Windows de 58×56 y marca de 156×42; Linux usa 38×38 y 170×36.
- Windows usa fondo degradado, tarjetas translúcidas, borde degradado y sombra.
  Linux usa colores sólidos y una tabla de sustitución Light/Dark.
- Títulos Windows de 27, navegación y captions de 15; Linux introduce otros tamaños.
- Inicio Windows usa tarjeta de conexión en dos columnas, acciones a la derecha,
  estado y nombre del dispositivo. Linux añade selección ADB dentro de esa tarjeta,
  acciones debajo y un resumen de sesión adicional.
- Las tarjetas de motores Linux añaden badges y cambian textos, botones y tamaños.
- Windows VER PANTALLA ejecuta el flujo de VisionEngine; Linux solo navega a su página.
- VisionEngine Linux tiene una composición propia; Configuración añade una tarjeta
  LAN que no corresponde a la distribución Windows.
- Montserrat Regular/SemiBold y los logos/mandos comparados tienen SHA-256 idéntico.

## Diseño propuesto

Reproducir shell, Inicio, VisionEngine, LinkEngine, ExInEngine, Integración,
Configuración y Sistema Android, con la misma jerarquía, orden, alineación,
dimensiones, recursos, paleta, tipografía y estados visuales de Windows.
Trasladar plantillas de botones, navegación, selectores, switches, tarjetas,
indicadores y barras de progreso; evitar que los estilos Fluent predeterminados
introduzcan diferencias. Conservar los originales de marca y fuentes.

Inicio reproduce la tarjeta y las tres tarjetas de motores de Windows. Los
selectores y diagnósticos USB/LAN se presentan mediante el flujo de tipo de
conexión, conservando su accesibilidad sin alterar la composición de Inicio.
Las capacidades ausentes deben comunicar su estado real: igualdad visual no
autoriza presentar funciones inexistentes como operativas.

El tema reproduce los recursos Light/Dark Windows, incluidos degradados,
transparencias, colores de texto y acento. El comportamiento de inicio se
contrastará con la inicialización Windows antes de cambiar la persistencia Linux.
Los iconos Windows basados en fuentes requieren equivalentes de geometría fieles;
no se presupone permiso para redistribuir fuentes del sistema Windows.

## Conectividad real

Reutilizar `LinuxControlHost`, `AdbBridge`, `LinuxControlServer`, el trust store y
los motores USB/LAN existentes. La integración visual no sustituye sus protocolos.

- USB: enumerar dispositivos físicos autorizados, seleccionar uno, preparar el
  bootstrap protegido de LINK Control y sus rutas ADB reverse, esperar autorización
  y reflejar conectado/desconectado/error. Liberar solo rutas propias.
- LAN: seleccionar dirección IPv4 privada, iniciar descubrimiento y control TLS,
  mostrar invitación, huella y expiración, renovar y revocar acceso.
- VER PANTALLA: solicitar captura mediante el control autorizado; Android conserva
  el consentimiento MediaProjection. Reflejar espera, transmisión, parada y error.
- LinkEngine: conservar inicio autorizado desde AppControl y las ofertas específicas
  USB/LAN; verificar arranque, parada y presencia del relay en el paquete publicado.
- Evitar estados engañosos al coexistir USB y LAN; las acciones y sus indicadores
  deben corresponder al transporte y sesión utilizados.
- Propuesta de política: conservar la ruta de la sesión activa y cambiarla solo
  por selección explícita. Como Windows, impedir cambios mientras VE/LE esté
  activo, arrancando o esperando consentimiento. Si no hay sesión ni selección
  previa y ambas rutas están autorizadas, pedir elección en el menú existente.
  Captura, parada y estado siempre deben usar la misma sesión seleccionada.
  No inferir autorización USB a partir de `IsAuthorized` global: actualmente
  `UpdateDeviceStatus` mezcla ese indicador con `IsUsbRunning`.
- Conservar video, audio, entrada, grabación y FILES V2 ya implementados.

## Límites

No ampliar a implementación de ExInEngine/UHID, actualizaciones, cámara, micrófono,
telemetría o speedtest que hoy no existen en Linux. No introducir scrcpy ni retirar
avisos de terceros sin comprobar la procedencia del código restante. No instalar
APK, cambiar firewall, publicar, hacer commit o push como parte de este diseño.

## Secuencia y coordinación

1. Acordar propiedad de archivos con Claude antes de editar código compartido.
2. Extraer recursos y plantillas visuales, después shell e Inicio.
3. Integrar el flujo de conexión USB/LAN y las acciones reales de Inicio.
4. Trasladar las restantes páginas conservando servicios Linux.
5. Verificar publicación del relay, regresiones y comparación visual.

## Aceptación y evidencia

- Build Release sin errores; suite Linux y 29 pruebas Rust como base actual.
- Pruebas de acciones mediante los servicios, incluyendo rechazo, cancelación,
  desconexión y limpieza. Comprobar el artefacto publicado, no solo el build.
- Comparar capturas de ambas aplicaciones con mismo tamaño, tema, escala y estado;
  revisar todas las páginas y controles, incluidos hover/foco/disabled.
- Estados iniciales de comparación: sin teléfono, USB esperando autorización,
  USB autorizado, LAN esperando emparejamiento, LAN autorizado y captura activa.
  Base visual: 1180×800 y escala 100%, Light y Dark; después mínimo 1000×610
  y escalado 125/150%. Los estados de capacidades no implementadas conservan
  ubicación y estilo, pero explican la disponibilidad real de Linux.
- Registrar diferencias de renderizado entre WPF/Avalonia y corregirlas. No declarar
  identidad visual del 100% únicamente por comparación de código o compilación.
- Validación física: USB y LAN con teléfono autorizado, captura/audio/entrada,
  archivos y LinkEngine. Actualmente no hay dispositivo conectado.
- GPU, multimonitor y escalado fraccionario requieren evidencia específica.

La referencia ejecutada Windows equivalente y el reparto con Claude siguen siendo
insumos pendientes para certificar el resultado; la comparación fuente a fuente
sí puede continuar sin ellos.

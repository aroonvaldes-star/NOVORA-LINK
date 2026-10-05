# Complemento observador externo de UX para NOVORA-LINK

## Objetivo

Crear un complemento local para Codex que observe NOVORA-LINK en Windows y Android sin modificar, instrumentar ni inyectar código en las aplicaciones. El complemento debe actuar como los ojos de Codex durante una sesión de uso, conservar evidencia visual y técnica, contrastar las acciones observadas con el comportamiento descrito por el repositorio completo y generar un informe ampliado de problemas que afecten la experiencia de usuario.

El complemento no confundirá una inferencia con un hecho. Cada hallazgo incluirá evidencia, límites de observación y un nivel de confianza explícito.

## Resultado observable

El usuario podrá iniciar una sesión conjunta de observación, utilizar NOVORA normalmente en PC y Android, consultar la vista actual desde Codex y finalizar la sesión para obtener:

- una cronología sincronizada de acciones y resultados;
- defectos visuales y de interacción detectados;
- diferencias entre la acción realizada, la conducta esperada según el código y el resultado observable;
- referencias al código probablemente relacionado;
- pasos de reproducción, severidad, impacto UX y nivel de confianza;
- evidencia original suficiente para reanalizar la sesión sin repetirla.

## Límites y garantías de veracidad

- El complemento es externo y de solo lectura respecto al repositorio y los procesos de NOVORA-LINK.
- No modifica archivos, memoria, paquetes, configuración ni tráfico de NOVORA.
- No introduce hooks, DLL injection, bytecode instrumentation ni cambios en los proyectos Windows o Android.
- Una observación externa no puede demostrar siempre qué método interno se ejecutó. En esos casos el informe distinguirá el defecto observado de su causa probable.
- Ninguna ausencia de evidencia se presentará automáticamente como evidencia de ausencia.
- Los resultados usarán tres niveles: `CONFIRMADO`, `PROBABLE` e `INDETERMINADO`.
- El sistema favorecerá `INDETERMINADO` antes que emitir una conclusión no sustentada.

## Alcance funcional

### Superficies observadas

- Aplicación WPF de Windows en `src/NOVORA/UI/`.
- Aplicación Android vigente en `src/NOVORA.Android/` y, mientras siga formando parte del checkout, la superficie Android XML en `src/NOVORA.LinkClient/`.
- LinkEngine, VisionEngine, ExInEngine, integraciones NOVORA, RemoteNV y servicios comunes, únicamente mediante sus efectos externamente visibles.
- Procesos, ventanas, árbol de accesibilidad, logs, estado ADB, actividades, servicios, sockets y archivos de evidencia producidos por los mecanismos normales de diagnóstico.

### Conocimiento del código

Antes de analizar una sesión, el complemento creará un índice de solo lectura del checkout seleccionado. El índice cubrirá código fuente, XAML, layouts Android, recursos, manifiestos, proyectos, protocolos, documentación técnica y pruebas. Excluirá binarios, cachés y resultados generados salvo que sean evidencia de ejecución.

El índice representará:

- controles visibles y sus identificadores;
- textos y recursos localizados;
- handlers, comandos, callbacks y navegación;
- llamadas desde UI hacia servicios y motores;
- máquinas y transiciones de estado;
- códigos y mensajes de error;
- contratos PC-Android y eventos de protocolo;
- expectativas expresadas por pruebas y documentación;
- relaciones archivo, símbolo y línea.

El índice estará identificado por commit, estado de archivos modificados y hash de los archivos analizados. Si el código cambia, la sesión conservará el índice exacto contra el que fue evaluada.

## Arquitectura

El paquete se llamará `novora-ux-observer` y seguirá Agent Plugins 1.0. Será un complemento local con servidor MCP por `stdio` y una aplicación auxiliar independiente.

```text
novora-ux-observer/
  plugin.json
  mcp.json
  skills/observe-novora/SKILL.md
  server/
  observer/
    Windows/
    Android/
    Vision/
    Correlation/
    Reporting/
  schemas/
  rules/
  assets/
  tests/
```

### Servidor MCP

Expone a Codex operaciones acotadas para seleccionar el checkout, construir o actualizar el índice, iniciar y detener sesiones, inspeccionar la vista actual, marcar momentos, recuperar evidencia y generar o reanalizar informes. El protocolo MCP se mantendrá separado de stdout de diagnóstico; los logs del servidor irán a stderr.

### Observador Windows

Usará APIs públicas del sistema para capturar la ventana objetivo y consultar Microsoft UI Automation. Observará foco, controles, bounds, estado habilitado, nombre accesible, cambios de estructura, creación o cierre de ventanas y vida del proceso. No enviará entradas por defecto; la interacción seguirá perteneciendo al usuario.

### Observador Android

Usará ADB autorizado para obtener capturas, jerarquía accesible, actividad enfocada, servicios visibles, estado del dispositivo y `logcat`. No instalará una aplicación auxiliar en el teléfono durante la primera versión y no modificará el APK de NOVORA.

### Analizador visual

Comparará fotogramas y árboles de UI para detectar geometría, movimiento y legibilidad. La imagen establece qué se vio; la accesibilidad ayuda a identificar el control, pero ninguna fuente reemplaza a la otra.

### Correlacionador código-ejecución

Relacionará una interacción con candidatos del índice usando identificadores de accesibilidad, texto, jerarquía, pantalla, ventana, ruta de navegación, tiempos y mensajes de logs. La relación se almacenará como conjunto de candidatos ponderados, no como una única causa inventada.

### Generador de informes

Producirá JSON como fuente canónica y HTML/Markdown como vistas derivadas. Todo hallazgo conservará enlaces a evidencia y al snapshot del código usado.

## Herramientas MCP previstas

- `novora_select_workspace`: valida y selecciona el checkout de NOVORA-LINK sin escribir en él.
- `novora_build_code_index`: construye un índice versionado de código y recursos.
- `novora_start_observation`: inicia una sesión para Windows, Android o ambos.
- `novora_get_live_view`: devuelve las capturas actuales, jerarquía y anomalías preliminares.
- `novora_mark_moment`: agrega una nota del usuario a la cronología.
- `novora_get_timeline`: consulta eventos correlacionados por intervalo.
- `novora_stop_observation`: finaliza la captura y sella la evidencia.
- `novora_generate_report`: aplica reglas y genera el informe ampliado.
- `novora_reanalyze_session`: ejecuta reglas nuevas sobre evidencia conservada.

Las herramientas que requieran ADB o captura de pantalla informarán claramente cuando falte permiso o cuando una superficie protegida no pueda verse.

## Flujo de una sesión

1. Validar checkout, versión, cambios locales, aplicaciones objetivo y dispositivo Android.
2. Construir o seleccionar el índice compatible con ese estado del código.
3. Sincronizar relojes mediante pares de timestamps monotónicos y UTC; registrar la incertidumbre de alineación.
4. Capturar una línea base de ventanas, pantallas, procesos, actividades, servicios y conectividad.
5. Suscribirse a eventos disponibles y realizar capturas bajo demanda o provocadas por cambios.
6. Registrar cada acción observable con fotograma anterior y posterior, control, bounds y contexto.
7. Agrupar señales pertenecientes a la misma acción mediante ventanas temporales configurables.
8. Correlacionar la acción con rutas candidatas del código y resultados externos.
9. Sellar la sesión con hashes de evidencia al detenerla.
10. Aplicar reglas, deduplicar incidentes y generar el informe.

La aplicación auxiliar no aplicará sondeo continuo indiscriminado. Preferirá eventos de UI Automation, cambios de ventana, señales ADB y captura activada por eventos. Los intervalos se reservarán para deadlines, muestreo visual durante movimiento y mecanismos que no ofrezcan eventos, respetando el principio Zero Polling de NOVORA.

## Detecciones

### Legibilidad y disposición

- texto cortado, elidido sin alternativa accesible o fuera de su contenedor;
- superposición entre texto, iconos, tarjetas y controles accionables;
- controles total o parcialmente fuera del viewport;
- contraste insuficiente con evaluación WCAG cuando fondo y primer plano sean medibles;
- tamaños visuales o táctiles inferiores a los mínimos configurados;
- alineación, espaciado o jerarquía inconsistentes entre estados comparables;
- contenido importante oculto por escalado, DPI, orientación o fuente ampliada.

### Movimiento inesperado

- layout shift sin acción o transición que lo justifique;
- controles que cambian de posición durante el intento de pulsarlos;
- parpadeo, desaparición y reaparición repetida;
- scroll involuntario o retorno inesperado de posición;
- apertura, cierre o reposicionamiento inesperado de ventanas y diálogos;
- animación que no finaliza, se reinicia o contradice el estado resultante.

### Interacción

- botón habilitado sin efecto verificable dentro de su deadline;
- acción duplicada a partir de una única interacción;
- múltiples interacciones del usuario provocadas por falta de feedback;
- navegación circular, regreso inesperado o destino equivocado;
- foco perdido, atrapado o dirigido a un control distinto;
- indicador de carga ausente, detenido o persistente después del resultado;
- mensaje visual que contradice el estado externo observado.

### Motores y errores

- excepción, crash, ANR, hang o cierre inesperado asociado temporalmente a una acción;
- estado visible de un motor incompatible con procesos, logs, sockets o servicio Android;
- transición que excede el plazo esperado sin feedback suficiente;
- fallo de un motor que afecta innecesariamente a otro en la interfaz;
- acción presentada como completada cuando la evidencia solo confirma aceptación o inicio;
- mensajes de error sin recuperación, causa útil o próxima acción.

## Reglas de confianza

### CONFIRMADO

Requiere evidencia directa reproducible del defecto. Ejemplos: píxeles de dos controles se superponen; una excepción aparece en `logcat` inmediatamente después de la acción; un único evento de entrada produce dos navegaciones observables.

### PROBABLE

Requiere dos o más señales independientes coherentes, pero la causa o el resultado interno no puede demostrarse externamente. Ejemplo: el clic es confirmado por accesibilidad, la pantalla no cambia y no aparece actividad, log ni proceso compatible dentro del deadline.

### INDETERMINADO

Se usa cuando faltan señales, existe desincronización, la superficie está protegida, un control no expone accesibilidad, el log no está disponible o hay varias explicaciones equivalentes. El informe indicará la prueba adicional necesaria.

Cada regla declarará señales obligatorias, señales de apoyo, deadline, condiciones de exclusión y explicación del nivel asignado. Las reglas visuales conservarán la región exacta y los fotogramas usados.

## Informe ampliado

El informe incluirá:

- identidad de sesión, versiones, dispositivo, resolución, DPI, orientación y estado del checkout;
- resumen ejecutivo con incidencias por severidad, confianza, plataforma y motor;
- cronología completa de acciones, efectos y cambios de estado;
- agrupación de incidentes repetidos sin perder ocurrencias individuales;
- anexos de evidencia y limitaciones de captura.

Cada incidencia contendrá:

- hora y superficie;
- acción del usuario y contexto anterior;
- resultado esperado derivado del código, prueba o documentación citada;
- resultado realmente observado;
- diferencia e impacto UX;
- capturas anterior y posterior con región anotada;
- evidencia de accesibilidad, proceso, ADB, log o red;
- archivos, símbolos y líneas candidatos con explicación de la relación;
- severidad y nivel de confianza;
- pasos exactos de reproducción;
- recomendación de corrección sin aplicar cambios;
- hipótesis alternativas y forma de descartarlas.

## Evidencia y almacenamiento

Las sesiones se almacenarán fuera del checkout de NOVORA-LINK, en un directorio configurable del usuario. Cada sesión tendrá manifiesto, eventos JSONL, capturas, árboles de UI, logs seleccionados, índice o referencia inmutable al índice e informe.

Aunque el complemento sea de uso personal y permita captura completa, la recogida de contenido sensible será explícita en la configuración de sesión. El modo completo podrá incluir texto visible, capturas y rutas de archivo; nunca extraerá deliberadamente contraseñas, OTP, tokens ni cookies. La exportación para compartir generará una copia sanitizada y no sustituirá la evidencia local original.

La retención será configurable por número de días o tamaño. El borrado será una acción explícita y separada de detener una sesión.

## Interfaz auxiliar

El panel local mostrará:

- estado de conexión con Windows, Android y Codex;
- selector de superficies;
- previsualización Windows/Android;
- estado de grabación y espacio estimado;
- cronología reciente;
- anomalías preliminares claramente marcadas como no finales;
- controles `Iniciar sesión`, `Marcar momento`, `Finalizar` y `Generar informe`.

El panel no se superpondrá a NOVORA durante capturas destinadas a evaluar layout, salvo que el usuario active una vista lateral. Sus propios cambios visuales no se contabilizarán como eventos de NOVORA.

## Manejo de fallos

- Si Windows UI Automation falla, conservar captura y marcar la identidad del control como indeterminada.
- Si ADB se desconecta, cerrar el intervalo de evidencia Android y no atribuir acciones durante el hueco.
- Si `logcat` rota o carece de permisos, registrar el rango perdido.
- Si las marcas de tiempo se desalinean, ampliar el margen de correlación y reducir la confianza.
- Si la ventana queda oculta o minimizada, no evaluar legibilidad durante ese intervalo.
- Si contenido protegido produce una captura negra, registrarlo como limitación, no como defecto de NOVORA.
- Si el índice no corresponde al ejecutable observado, impedir conclusiones sobre rutas de código hasta resolver la versión.

## Pruebas y aceptación

### Pruebas automáticas

- indexación de soluciones C#, XAML, recursos Android y pruebas;
- resolución control-visible a handler con fixtures conocidos;
- sincronización y agrupación temporal determinista;
- clasificación de confianza y condiciones de exclusión;
- detección visual con imágenes sintéticas de corte, solapamiento y desplazamiento;
- deduplicación de eventos y acciones repetidas;
- generación y validación de informes JSON, HTML y Markdown;
- verificación de que el observador no escribe dentro del checkout seleccionado;
- validación de manifiestos `plugin.json` y `mcp.json`.

### Pruebas de integración

- aplicación Windows de prueba con UI Automation y movimientos controlados;
- aplicación Android de prueba/emulador con layout y ANR reproducibles;
- desconexión y reconexión ADB durante una sesión;
- rotación, cambio de DPI, fuente al 200 % y distintas resoluciones;
- sesión combinada con tiempos deliberadamente desalineados;
- lectura de un checkout sucio sin modificar archivos ni índice Git.

### Prueba física requerida

Una entrega completa requiere ejecutar el complemento con NOVORA Windows y el dispositivo Android real, reproducir al menos un defecto visual, una acción sin resultado y un evento de motor, y comprobar que el informe separa correctamente observación, expectativa y causa probable.

### Criterios de aceptación

- Cero modificaciones en el checkout de NOVORA-LINK durante una sesión normal.
- Codex puede solicitar y recibir la vista actual de ambas superficies.
- Toda conclusión del informe incluye evidencia y nivel de confianza.
- El complemento nunca presenta una ruta probable del código como ejecución confirmada sin evidencia directa.
- Las sesiones pueden reanalizarse sin volver a ejecutar NOVORA.
- Un fallo de una fuente de observación degrada la confianza sin invalidar evidencia independiente.
- El paquete local inicia, descubre sus herramientas MCP y completa una llamada inocua de diagnóstico.

## Decisiones de compatibilidad

- Windows es el host del servidor y del observador.
- El complemento respetará la arquitectura de un dispositivo Android activo por PC.
- No se instalarán dependencias o certificados globales.
- El índice admitirá cambios locales y los identificará por hash.
- La primera versión será privada y local; no se diseñará para publicación pública ni ejecución web/móvil.
- El código de NOVORA-LINK será una fuente de expectativas y candidatos, no una fuente infalible de lo ocurrido en runtime.

## Fuera de alcance

- corregir automáticamente NOVORA-LINK;
- ejecutar acciones o pruebas destructivas sin solicitud expresa;
- afirmar cobertura total de rutas que no producen señales externas;
- evadir superficies protegidas o controles de seguridad del sistema;
- sustituir pruebas físicas de motores, red, video, audio o entrada;
- publicar sesiones, capturas o informes en servicios externos;
- certificar ausencia absoluta de errores o falsos negativos.

## Entrega prevista

La implementación se realizará como un directorio nuevo y autocontenido, separado del código de NOVORA-LINK. Incluirá manifiestos, servidor MCP local, skill de operación, observadores, reglas, esquemas, pruebas y documentación de instalación. La conexión o instalación local se realizará únicamente cuando el usuario la solicite después de validar el paquete.

# Design System: NOVORA PROTOTYPE APPCONTROL

Documento maestro para diseñar en Stitch y después integrar con Codex sobre la
aplicación Android funcional `com.novora.appcontrol`, ubicada en
`src/NOVORA.Android`.

El diseño puede mejorar composición, jerarquía, navegación visual y copy, pero
debe conservar las capacidades, estados, permisos y límites del código actual.
No convertir AppControl en NOVORA LinkClient y no incorporar funciones
imaginarias.

## 1. Dirección visual aprobada — Platinum Air

Platinum Air es una interfaz clara, sencilla y cercana al logotipo de NOVORA.
Debe sentirse como una superficie de control precisa y ligera, no como un
dashboard oscuro ni como una colección de tarjetas decorativas.

- Modo claro como experiencia principal.
- Fondo blanco frío o gris azulado muy claro.
- Cabecera de plata suave inspirada en el volumen metálico del logotipo.
- Azul cobalto eléctrico para acciones, selección y foco.
- Superficies blancas, bordes finos y sombras muy suaves.
- Espacio visual generoso y geometría limpia, técnica y moderadamente
  redondeada.
- El logotipo NOVORA tiene presencia en la cabecera, pero el estado funcional
  conserva la prioridad.
- Evitar vidrio borroso, neón excesivo, gradientes fuertes, paneles oscuros
  dominantes y tarjetas anidadas sin propósito.
- Conservar el control de tema existente; una futura variante oscura debe
  mantener los mismos roles semánticos, no alterar la jerarquía ni introducir
  nuevos comportamientos.

## 2. Paleta y roles de color

### Fundación Platinum Air

- **Canvas (`#F4F6FB`)**: fondo raíz de páginas.
- **Surface (`#FFFFFF`)**: tarjetas, navegación, campos y diálogos.
- **Raised Surface (`#EDF1F8`)**: selección tonal, controles secundarios y
  estados presionados.
- **Cool Border (`#D7DDE8`)**: bordes de `1dp`, separadores y estructura.
- **Primary Text (`#121722`)**: títulos, valores y etiquetas principales.
- **Secondary Text (`#657087`)**: descripciones, metadatos y navegación
  inactiva.

### Marca e interacción

- **NOVORA Cobalt (`#304FFE`)**: acción primaria, navegación activa, foco y
  progreso.
- **Cobalt Light (`#5671FF`)**: hover, apoyo gráfico y estados secundarios de
  marca.
- **On Cobalt (`#FFFFFF`)**: texto e iconos sobre el acento.

### Estados funcionales

- **Success (`#16845B`)**: conectado, activo, detectado, completado y
  verificado.
- **Warning (`#976C00`)**: degradado, permiso pendiente, confirmación necesaria
  o advertencia recuperable.
- **Error (`#B3262E`)**: fallo, bloqueo, desconexión, revocación o acción
  destructiva.

Color nunca es la única señal: acompañarlo con texto, icono, borde, forma o
patrón. No asignar color de éxito a una capacidad disponible si el motor no está
realmente activo.

## 3. Tipografía e iconografía

La tipografía de marca del prototipo es obligatoria:

- **Headline — Montserrat**: títulos principales, títulos de página y títulos
  de sección.
- **Body — Montserrat**: párrafos, descripciones, valores, ajustes y mensajes.
- **Label — Manrope**: botones, campos, chips, pestañas, navegación y rótulos
  breves.
- **Telemetry — plataforma monospace**: sólo coordenadas, porcentajes, tasas,
  valores de transporte y datos cuyos dígitos cambian.

Usar `sans-serif` como fallback técnico si una fuente todavía no ha cargado. En
la integración Android, Codex debe incluir los assets tipográficos necesarios;
el diseño no debe sustituir silenciosamente Montserrat o Manrope por otra
familia.

- Título principal: Montserrat `24sp`, bold, altura aproximada `30sp`.
- Título de página: Montserrat `20sp`, bold, altura aproximada `26sp`.
- Título de sección: Montserrat `15sp`, semibold, altura aproximada `20sp`.
- Cuerpo y fila de ajuste: Montserrat `13sp`, regular o medium, altura
  aproximada `18sp`.
- Botón y etiqueta de campo: Manrope `14sp`, semibold, altura aproximada `20sp`.
- Estado, caption y navegación: Manrope `12sp`, medium, altura aproximada
  `16sp`.
- Telemetría: plataforma monospace `13sp`, altura aproximada `18sp`.

Mantener etiquetas en español y sentence case. Mayúsculas sólo en rótulos
técnicos breves con tracking moderado. Usar iconos simples, consistentes y con
descripción accesible; no depender de iconos ambiguos sin texto.

## 4. Cuadrícula, superficies y componentes

### Layout base

- Cuadrícula de `8dp` con subdivisiones de `4dp`.
- Padding de página de `16dp`, respetando barras del sistema, cutouts y teclado.
- Espacios estándar: `4dp`, `8dp`, `16dp` y `24dp`.
- Una columna principal en teléfono; ancho de lectura máximo cercano a `560dp`
  en pantallas amplias.
- Objetivo táctil mínimo de `48x48dp`.
- Navegación inferior persistente fuera del contenido desplazable.
- Acciones primarias al alcance del pulgar y feedback visible sin cubrir el
  siguiente paso.

### Cabecera

Cabecera compacta de plata suave con el símbolo o wordmark NOVORA, `NOVORA
LINK`, `APPCONTROL`, versión, estado de sesión y control de tema. La plata puede
usar una transición tonal sutil, pero no un gradiente decorativo dominante.

### Botones

- Altura mínima `48dp`, radio `10dp` aproximado.
- Primario: fondo Cobalt, contenido On Cobalt, sin más de una acción dominante
  por bloque de decisión.
- Secundario: Surface o Raised Surface, borde Cool Border, texto Primary.
- Peligro: estilo contenido o tonal Error según el riesgo.
- Bloquear la acción asíncrona mientras se ejecuta y mostrar progreso o estado.
- Las etiquetas pueden ocupar dos líneas cuando sea necesario.

### Tarjetas y secciones

- Surface, borde `1dp` Cool Border, radio `12dp` y sombra muy suave.
- Padding habitual de `16dp` y separación vertical de `8dp` a `16dp`.
- Preferir secciones claras y filas agrupadas a una pantalla llena de tarjetas.
- Separar visualmente estado actual, acción disponible y resultado reciente.

### Chips y estados

- Altura mínima `32dp`, radio `10dp`, borde y texto semánticos.
- Mostrar siempre una etiqueta explícita: por ejemplo `Activo`, `Degradado`,
  `Sin conexión` o `No disponible`.
- Nunca mostrar métricas de ejemplo para llenar el espacio.

### Controles segmentados y campos

- Altura mínima `48dp` y segmentos del mismo ancho cuando sean opciones pares.
- Segmento seleccionado con Cobalt y On Cobalt; no seleccionado con Surface o
  Raised Surface.
- Campos con etiqueta persistente, instrucciones claras y borde visible al
  recibir foco.

### Componentes del dominio

- **Engine State Row**: nombre, propósito corto, estado confirmado y sólo las
  acciones habilitadas por la capacidad publicada.
- **Connection Panel**: PC, transporte, confianza, permiso y próxima acción sin
  mezclar USB con LAN.
- **ExIn Live Panel**: visualización física más valores crudos y corregidos en
  monospace. Xbox y Sony conservan geometrías distintas.
- **Calibration Panel**: distingue prueba en vivo, progreso visual, resultado y
  acciones de PC. Android no aparece como propietario de la calibración.
- **Transfer Row**: nombre, bytes enviados, progreso, resultado y cancelación
  cuando corresponda.

## 5. Arquitectura de navegación

La barra inferior contiene exactamente tres destinos:

1. **Inicio**
2. **Engines**
3. **Archivos**

El destino seleccionado se reconoce por icono, texto, fondo o indicador, no
sólo por color. Son páginas subordinadas y regresan a su destino de origen:

- Opciones y diagnóstico de conexión.
- Búsqueda, código y confianza LAN.
- PCs guardadas.
- Detalle de ExInEngine.
- Ajustes de VisionEngine.
- Selector y envío de archivos.
- Permisos, confirmaciones y recuperación.

Debe existir una región global de mensajes accesible para informar operaciones,
errores y cambios de sesión sin tapar la acción siguiente.

## 6. Contrato funcional de pantallas

### Inicio

Inicio presenta una jerarquía sencilla, adaptable al estado real, con:

- Resumen de PC vinculada, teléfono local y transporte actual.
- Estados explícitos para desconectado, conectando, USB activo, LAN cifrada,
  ocupado, degradado y error.
- Acciones existentes: detectar o preparar USB, desconectar, buscar PCs NOVORA
  en LAN, introducir un código LAN de seis dígitos, consultar PCs guardadas,
  recordar la PC actual y detectar Engines.
- Resumen de LinkEngine, VisionEngine y ExInEngine con cantidad activa.
- Selector de intención: **Jugar**, **Video** o **Internet**.
- Preflight de **Conexión**, **Motor** y **Control**.
- Una única acción dominante cuyo texto se deriva del estado: `Preparar
  conexión`, `Iniciar sesión de juego`, `Iniciar video`, `Iniciar Internet`,
  `Ver sesión activa` o `Abrir control de juego`.
- Acceso secundario a diagnóstico de motores.

Juego necesita VisionEngine y puede incluir ExInEngine si la PC detecta un
control. Video necesita VisionEngine. Internet usa LinkEngine y puede requerir
autorización VPN. Ocultar los pasos irrelevantes para el modo seleccionado.

### Conexión, emparejamiento y confianza

- USB puede comenzar desde una invitación segura preparada por la PC o desde la
  acción de detección disponible.
- LAN permite buscar una PC o escribir un código de exactamente seis dígitos.
- Antes de confirmar LAN, el usuario compara y acepta la huella completa
  mostrada por NOVORA PC.
- Después de una conexión válida, se puede recordar la PC.
- PCs guardadas muestran nombre seguro y transporte, con reconectar y olvidar.
- Un cambio de PC, generación de sesión o revisión durante una operación cancela
  la acción y pide revisar el estado nuevo.
- No mostrar ni inventar secretos, invitaciones, tokens o huellas incompletas.

### Engines

Mostrar el estado del bus de control y tres módulos diferenciados:

- **LinkEngine**: transporte USB/LAN, Internet, VPN Android y túnel de datos.
  Iniciar o detener sólo cuando la PC publique la capacidad. Telemetría opcional:
  latencia, pérdida y TX/RX.
- **VisionEngine**: pantalla, video, audio, monitor, perfil, bitrate, resolución y
  FPS. Iniciar o detener, y abrir Ajustes VE. Puede requerir MediaProjection,
  audio y accesibilidad. Distinguir video LAN nativo de USB y LAN mediante
  AppControl; no tratar las rutas como equivalentes.
- **ExInEngine**: control físico, modo Juego/UI, prueba, diagnóstico y
  calibración. Mostrar identidad, familia, VID/PID, conexión, batería y
  capacidades sólo cuando existan. Telemetría opcional: polling, procesamiento
  en PC y jitter.

La telemetría sólo se presenta cuando existe y es reciente. El código actual
considera reciente una muestra dentro de aproximadamente dos segundos.

### Detalle de ExInEngine

- Estado `Detectado` o `Sin detectar`.
- Nombre, familia, conexión, VID/PID, perfil y capacidades publicadas.
- Selector **Juego / UI** y acción **Reactivar control** cuando se permitan.
- Secciones **Estado**, **Prueba** y **Calibración**.
- Estado: salud, diagnóstico y batería.
- Prueba: mando físico, botones, sticks y gatillos crudos/corregidos en monospace.
- Calibración: progreso e instrucciones para centro, recorrido de sticks y
  gatillos.

Xbox y Sony DualSense/DualShock mantienen composiciones físicas distintas. La
PC ejecuta y guarda la calibración; Android sólo la representa y explica.

### Ajustes de VisionEngine

Usar únicamente opciones publicadas por la PC:

- Monitor.
- Modo o perfil de VisionEngine.
- Estado de aceleración y evidencia disponible.
- Bitrate, resolución y límite de FPS.
- Salida de audio en PC.

La acción principal es **Aplicar cambios y reiniciar VE** y aplica todo junto.
Deshabilitarla con una razón explícita si la PC no admite aplicación conjunta,
la sesión cambió o existe una grabación activa.

### Archivos y multimedia

- Archivos recibidos, transferencias y completados recientes.
- Acción **Enviar a PC**.
- Acciones de VisionEngine: grabar en PC y captura de pantalla.
- Estado de audio del teléfono y ruta confirmada por la PC.
- Acceso mediante Android Storage Access Framework, sin permiso de acceso total.
- Carpeta pública exactamente `NOVORA` en almacenamiento interno.
- Categorías: Imágenes, Videos, Audio, Documentos, Comprimidos, Instaladores y
  Otros.

Si no existe una carpeta vinculada, pedir elegir o crear exactamente `NOVORA`.

### Enviar archivos

- Selección manual o contenido compartido desde otras aplicaciones Android.
- Máximo 100 archivos por selección y 2 GiB por archivo.
- Mostrar PC y transporte de destino.
- Deshabilitar Enviar sin conexión o sin selección.
- Mostrar nombre, bytes enviados, progreso, cantidad completada y resultado
  verificado.
- Permitir cancelar sin perder el recuento completado.
- Cancelar automáticamente si la sesión cambia o se pierde.

## 7. Permisos, confirmaciones y recuperación

Diseñar estados completos para:

- MediaProjection para compartir pantalla.
- Audio cuando VisionEngine lo requiera.
- Servicio de accesibilidad para control remoto.
- Consentimiento VPN para LinkEngine.
- Notificaciones cuando corresponda.
- Confirmación antes de detener un motor activo.
- Confirmación antes de aplicar cambios y reiniciar VisionEngine.
- Código LAN inválido, huella no confirmada, PC no encontrada, sesión cambiada,
  servicio desconectado, permiso cancelado, motor bloqueado y versión de PC sin
  la capacidad requerida.

Cada error explica qué ocurrió, qué se conservó y la próxima acción segura.

## 8. Reglas de estado y datos

- La UI se actualiza desde snapshots y eventos reales; no representar un polling
  visual o una recarga manual permanente.
- Bloquear la acción correspondiente durante operaciones asíncronas y evitar
  dobles taps.
- Distinguir `Activo`, `Detectado`, `Listo`, `Degradado`, `Crítico`, `Bloqueado`,
  `Error`, `No detectado`, `Sin conexión` y `No disponible`.
- No inventar PC, IP, mando, batería, transferencia, motor, aceleración ni
  telemetría.
- Ocultar un dato no publicado o mostrar ausencia explícita si es relevante.
- No habilitar una acción si `CanStart`, `CanStop`, `CanSetMode`,
  `CanReactivate` o su capacidad equivalente no está confirmada.
- Conservar el resultado de una acción el tiempo suficiente para comprender
  éxito, cancelación o fallo.
- La PC es la autoridad sobre motores, telemetría, configuración y calibración;
  Android es la superficie de control y visualización.

## 9. Accesibilidad y control físico

- Contraste WCAG AA como mínimo.
- Objetivos táctiles mínimos `48x48dp` y texto legible.
- Descripciones de contenido para iconos y controles.
- Región de estado con anuncios educados y no interruptivos.
- Foco visible con borde Cobalt y otra señal además del color.
- Navegación completa con D-pad, hombros y teclado.
- A/X/Enter activa; B/Atrás vuelve; Start regresa a Inicio.
- Share + Options puede alternar el modo de ExInEngine cuando esté permitido.
- La selección de pestaña, segmento y destino se reconoce por forma, fondo,
  borde y texto.

## 10. Entregables para Stitch

Generar pantallas Android verticales de referencia aproximada `390x844dp`, con
copy realista en español y un único sistema Platinum Air:

1. Inicio desconectado.
2. Inicio conectado por USB con sesión Juego lista.
3. Búsqueda y emparejamiento LAN con verificación de huella.
4. PCs guardadas.
5. Engines con estados y telemetría disponible.
6. ExInEngine — Estado.
7. ExInEngine — Prueba en vivo.
8. ExInEngine — Calibración visual controlada desde PC.
9. Ajustes de VisionEngine.
10. Archivos y multimedia.
11. Enviar archivos con progreso y cancelación.
12. Permisos, confirmación, vacío, degradado y error.

Mantener los nombres LinkEngine, VisionEngine, ExInEngine, NOVORA-LINK y
AppControl. Antes de cerrar cada pantalla, verificar:

- Cada control corresponde a una acción existente.
- Cada dato tiene una fuente real.
- La pantalla funciona sin telemetría opcional.
- Las acciones disruptivas solicitan confirmación.
- La jerarquía es clara para uso con una mano.
- La apariencia sigue siendo Platinum Air y cercana al logotipo NOVORA.

## 11. Límites de integración para Codex

- Objetivo: `src/NOVORA.Android` (`com.novora.appcontrol`).
- Tema: `AndroidUI/NLAndroidUITheme.cs` y `Resources/values*/colors.xml`.
- Primitivas: `AndroidUI/NLAndroidUIVisual.cs`,
  `AndroidUI/NLAndroidUIComponents.cs` y `Resources/values/styles.xml`.
- Shell y flujos: `AndroidUI/NLAndroidUIActivity.cs`,
  `AndroidUI/NLAndroidUIMainShell.cs` y `Resources/layout/*.xml`.
- ExIn: `AndroidUI/NLAndroidUIExInPage.cs`,
  `AndroidUI/NLAndroidUIControllerLiveView.cs` y
  `AndroidUI/NLAndroidUITriggerCalibrationView.cs`.

El prototipo puede cambiar composición y copy, pero no contratos de transporte,
autoridad de datos ni responsabilidades entre LinkEngine, VisionEngine y
ExInEngine. La integración final debe mapear cada pantalla de Stitch a estos
puntos reales del código antes de reemplazar la interfaz existente.

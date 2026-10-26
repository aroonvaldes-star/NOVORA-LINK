# Prompt maestro para Stitch — NOVORA PROTOTYPE APPCONTROL

Usa el contenido siguiente como prompt principal de Stitch. El objetivo es
rediseñar la aplicación Android existente sin alterar ni inventar su
funcionalidad.

---

Diseña un prototipo móvil Android completo llamado **NOVORA PROTOTYPE
APPCONTROL**, basado en la aplicación funcional existente
`com.novora.appcontrol` de NOVORA-LINK.

El prototipo debe coincidir con las capacidades, estados, restricciones y
flujos que ya existen en el código. No diseñes una aplicación conceptual
distinta, no añadas funciones imaginarias y no conviertas AppControl en la
aplicación separada NOVORA LinkClient.

## 1. Objetivo del producto

AppControl es el acompañante Android de NOVORA-LINK para enlazar un teléfono
con una PC confiable y controlar funciones que la PC confirma mediante su
sesión real:

- Conectar el teléfono por USB físico o LAN cifrada.
- Recordar, consultar, reconectar u olvidar PCs confiables.
- Consultar y controlar LinkEngine, VisionEngine y ExInEngine.
- Preparar sesiones de Juego, Video o Internet.
- Autorizar captura de pantalla, audio, accesibilidad y VPN cuando el flujo lo
  requiera.
- Consultar ajustes de VisionEngine y aplicarlos juntos mediante la PC.
- Enviar, recibir, explorar y organizar archivos NOVORA.
- Mostrar telemetría real y reciente cuando la PC la publique.

Android funciona como superficie de control y visualización. La PC sigue
siendo la autoridad sobre motores, telemetría, configuración y calibración.

## 2. Dirección visual aprobada: Platinum Air

Usa una interfaz **clara, sencilla y cercana al logotipo de NOVORA**:

- Fondo principal blanco frío o gris azulado muy claro.
- Cabecera de plata suave inspirada en el volumen metálico del logotipo.
- Azul cobalto eléctrico como acento principal y para acciones importantes.
- Tarjetas claras, bordes finos, sombras muy suaves y amplio espacio visual.
- Geometría limpia y precisa; evita vidrio borroso, neón excesivo, gradientes
  decorativos fuertes, paneles oscuros dominantes y tarjetas innecesarias.
- El logotipo NOVORA debe aparecer con presencia, pero el contenido funcional
  conserva prioridad.
- Modo claro como experiencia principal. Conserva un control de tema porque la
  aplicación actual también admite modo oscuro.
- Usa **Montserrat** para Headline y Body: títulos, párrafos, descripciones,
  valores, ajustes y mensajes.
- Usa **Manrope** para Label: botones, campos, chips, pestañas, navegación y
  rótulos breves.
- Reserva la fuente monoespaciada de la plataforma para telemetría numérica.
- Usa `sans-serif` sólo como fallback técnico; no sustituyas silenciosamente
  Montserrat o Manrope por otra familia.
- Cuadrícula base de `8dp`, subdivisiones de `4dp`, margen de página de `16dp`,
  tarjetas de `16dp` aproximados y controles con radio moderado.
- Todo objetivo táctil debe medir al menos `48x48dp`.
- Usa icono, texto, borde y fondo para comunicar selección o estado; nunca
  dependas únicamente del color.

Paleta de referencia para esta propuesta:

- Canvas Platinum Air: `#F4F6FB`.
- Superficie: `#FFFFFF`.
- Superficie elevada: `#EDF1F8`.
- Borde frío: `#D7DDE8`.
- Texto principal: `#121722`.
- Texto secundario: `#657087`.
- Azul cobalto NOVORA: `#304FFE`.
- Azul cobalto claro: `#5671FF`.
- Éxito: `#16845B`.
- Advertencia: `#976C00`.
- Error: `#B3262E`.

## 3. Arquitectura de navegación obligatoria

Usa una barra de navegación inferior persistente con exactamente tres destinos
principales:

1. **Inicio**
2. **Engines**
3. **Archivos**

Mantén como páginas subordinadas, sin crear más destinos inferiores:

- Opciones y diagnóstico de conexión.
- Detalle de ExInEngine.
- Ajustes de VisionEngine.
- Selector y envío de archivos.
- Diálogos de permisos, confianza, confirmación y recuperación.

La cabecera estándar debe mostrar el logotipo, `NOVORA LINK`, `APPCONTROL`, la
versión, el estado de sesión y el control para cambiar el tema. Debe existir una
zona global de mensajes accesible para informar operaciones, errores y cambios
de sesión sin cubrir la acción siguiente.

## 4. Pantalla Inicio

Diseña Inicio con jerarquía sencilla y estado real:

- Resumen de la PC vinculada, teléfono local y transporte actual.
- Estados explícitos: sin conexión, conectando, USB activo, LAN cifrada activa,
  ocupado, degradado o error.
- Acceso claro a las opciones de conexión:
  - Detectar o preparar USB.
  - Desconectar.
  - Buscar PCs NOVORA en LAN.
  - Introducir un código LAN de exactamente seis dígitos.
  - Consultar PCs guardadas.
  - Recordar la PC actual.
  - Detectar Engines.
- Resumen de LinkEngine, VisionEngine y ExInEngine con número de motores activos.
- Selector de intención de sesión: **Jugar**, **Video** o **Internet**.
- Lista previa de Conexión, Motor y Control.
- Un único botón dominante cuyo texto cambia según el estado real:
  `Preparar conexión`, `Iniciar sesión de juego`, `Iniciar video`,
  `Iniciar Internet`, `Ver sesión activa` o `Abrir control de juego`.
- Acceso secundario al diagnóstico de motores.

El modo Juego necesita VisionEngine y puede mostrar ExInEngine cuando la PC
detecte un control. El modo Video necesita VisionEngine. El modo Internet usa
LinkEngine y puede requerir autorización VPN. No muestres pasos irrelevantes
para el modo seleccionado.

## 5. Conexión, emparejamiento y confianza

Representa los flujos existentes sin simplificar su seguridad:

- USB puede iniciarse desde una invitación segura preparada por la PC o desde
  la acción de detección disponible.
- LAN permite buscar una PC o introducir un código de seis dígitos.
- Antes de confirmar LAN, el usuario debe comparar y aceptar la huella completa
  mostrada por NOVORA PC.
- Permite recordar la PC después de una conexión válida.
- La lista de PCs guardadas muestra nombre seguro y transporte, con acciones
  para reconectar u olvidar.
- Si cambia la PC, la generación de sesión o la revisión del estado durante una
  operación, cancela la acción y pide revisar el nuevo estado.
- Nunca muestres ni inventes secretos, huellas incompletas, invitaciones,
  tokens o datos de confianza.

## 6. Pantalla Engines

Incluye un resumen del bus de control y tres módulos claramente separados:

### LinkEngine

- Propósito: transporte USB/LAN, Internet, VPN Android y túnel de datos.
- Estado y mensaje confirmados por la PC.
- Acción para iniciar o detener LinkEngine sólo cuando la capacidad publicada
  lo permita.
- Telemetría opcional: latencia, pérdida y TX/RX.

### VisionEngine

- Propósito: pantalla, video, audio, monitor, perfil, bitrate, resolución y FPS.
- Acción para iniciar o detener VisionEngine.
- Acceso a Ajustes VE.
- Telemetría opcional: resolución activa, FPS y bitrate.
- El inicio puede requerir MediaProjection, permiso de audio y servicio de
  accesibilidad.
- Distingue rutas funcionales: video LAN nativo, USB y LAN mediante AppControl.
  No presentes todos los transportes como equivalentes.

### ExInEngine

- Propósito: mando físico, modo Juego/UI, prueba, diagnóstico y calibración.
- Identidad del control, familia, VID/PID, conexión, batería y capacidades.
- Acceso al detalle de ExInEngine.
- Telemetría opcional: polling, procesamiento en PC y jitter.

Oculta las métricas que no existan o estén desactualizadas. La implementación
actual sólo considera reciente una muestra de telemetría dentro de
aproximadamente dos segundos. No rellenes huecos con valores de ejemplo.

## 7. Detalle de ExInEngine

Diseña una página subordinada con:

- Cabecera con estado Detectado o Sin detectar.
- Datos del dispositivo: nombre, familia, conexión, VID/PID, identidad del
  perfil y capacidades publicadas.
- Selector segmentado **Juego / UI**.
- Acción **Reactivar control** cuando esté permitida.
- Tres secciones: **Estado**, **Prueba** y **Calibración**.
- Estado: salud, diagnóstico y batería.
- Prueba: representación física del mando, valores crudos y corregidos de
  sticks, gatillos y botones con tipografía monoespaciada.
- Calibración: progreso e instrucciones visuales para centro, recorrido de
  sticks y gatillos.

Xbox y Sony DualSense/DualShock deben conservar composiciones físicas
distintas. La calibración se ejecuta y guarda desde NOVORA PC; Android la
visualiza y explica, pero no debe fingir que es propietario del proceso.

## 8. Ajustes de VisionEngine

Incluye selectores para valores publicados por la PC:

- Monitor del PC.
- Modo o perfil de VisionEngine.
- Estado de aceleración y evidencia disponible.
- Bitrate.
- Resolución.
- Límite de FPS.
- Salida de audio en PC.

La acción principal es **Aplicar cambios y reiniciar VE**. Todos los valores se
aplican juntos. Si la PC no soporta aplicación conjunta, si la sesión cambió o
si existe una grabación activa, muestra la razón y deshabilita la acción.

## 9. Pantalla Archivos

Incluye:

- Archivos recibidos y acción Enviar a PC.
- Estado de transferencia y completados recientes.
- Acciones de la sesión VisionEngine: Grabar en PC y Captura de pantalla.
- Estado del audio del teléfono y ruta de audio confirmada por la PC.
- Acceso a la carpeta pública `NOVORA` mediante Android Storage Access
  Framework, sin permiso de acceso total a archivos.
- Categorías: Imágenes, Videos, Audio, Documentos, Comprimidos, Instaladores y
  Otros.
- Si no existe una carpeta vinculada, pide elegir o crear exactamente la carpeta
  `NOVORA` en el almacenamiento interno.

## 10. Flujo Enviar archivos

Diseña un flujo compatible con compartir desde otras aplicaciones Android y
con selección manual:

- Elegir uno o varios archivos.
- Máximo 100 archivos por selección.
- Máximo 2 GiB por archivo.
- Mostrar la PC y el transporte de destino.
- Deshabilitar Enviar mientras no exista conexión o selección.
- Mostrar nombre, bytes enviados, progreso, cantidad completada y resultado
  verificado.
- Permitir cancelar; conservar el recuento de archivos ya completados.
- Cancelar automáticamente si cambia o se pierde la sesión.

## 11. Permisos, confirmaciones y estados de error

Diseña estados y diálogos para:

- Autorización de MediaProjection para compartir pantalla.
- Permiso de audio cuando VisionEngine lo necesite.
- Acceso al servicio de accesibilidad para control remoto.
- Consentimiento VPN para LinkEngine.
- Permiso de notificaciones cuando corresponda.
- Confirmación antes de detener un motor activo.
- Confirmación antes de aplicar cambios y reiniciar VisionEngine.
- Código LAN inválido, huella no confirmada, PC no encontrada, sesión cambiada,
  servicio desconectado, permiso cancelado, motor bloqueado y versión de PC sin
  capacidad requerida.

Cada error debe explicar qué ocurrió, qué se conservó y cuál es la siguiente
acción segura. Evita mensajes genéricos cuando el estado real permita una
explicación más concreta.

## 12. Reglas de estado y datos

- La interfaz se actualiza desde snapshots y eventos reales de la sesión; no
  diseñes refresco manual permanente ni polling visual.
- Durante una operación, bloquea la acción correspondiente y evita dobles taps.
- Distingue `Activo`, `Detectado`, `Listo`, `Degradado`, `Crítico`, `Bloqueado`,
  `Error`, `No detectado`, `Sin conexión` y `No disponible`.
- Nunca inventes identidad de PC, IP, control, batería, transferencia, motor,
  aceleración o telemetría.
- Cuando un dato no esté publicado, ocúltalo o muestra una ausencia explícita.
- No muestres botones habilitados si la capacidad `CanStart`, `CanStop`,
  `CanSetMode`, `CanReactivate` o equivalente no está confirmada.
- Mantén visibles los resultados de una acción el tiempo suficiente para que el
  usuario comprenda éxito, cancelación o fallo.

## 13. Accesibilidad y control físico

- Contraste WCAG AA como mínimo.
- Texto legible sin depender de mayúsculas pequeñas.
- Descripciones de contenido para iconos y controles.
- Región de estado accesible con anuncios educados, no interruptivos.
- Foco visible mediante borde azul, no sólo cambio de color de texto.
- Navegación completa con D-pad, hombros y teclado.
- A/X/Enter activa el control enfocado; B/Atrás vuelve; Start regresa a Inicio.
- El atajo Share + Options puede alternar el modo de ExInEngine cuando esté
  permitido.
- Mantén la selección de pestaña, segmento y destino visible por forma, fondo,
  borde y texto.

## 14. Entregables solicitados a Stitch

Genera un conjunto coherente de pantallas móviles Android en orientación
vertical, aproximadamente `390x844dp`, usando el mismo sistema Platinum Air:

1. Inicio desconectado.
2. Inicio conectado por USB con sesión Juego lista.
3. Búsqueda y emparejamiento LAN con verificación de huella.
4. Lista de PCs guardadas.
5. Engines con estados y telemetría disponible.
6. ExInEngine: Estado.
7. ExInEngine: Prueba en vivo.
8. ExInEngine: Calibración visual controlada desde PC.
9. Ajustes de VisionEngine.
10. Archivos y multimedia.
11. Enviar archivos con progreso y cancelación.
12. Estados de permisos, confirmación, vacío, degradado y error.

Usa copy realista en español. Mantén los nombres LinkEngine, VisionEngine,
ExInEngine, NOVORA-LINK y AppControl. No reemplaces esos nombres por categorías
genéricas.

Antes de terminar, revisa cada pantalla contra estas reglas:

- ¿Cada control corresponde a una acción que ya existe?
- ¿Cada dato mostrado tiene una fuente real?
- ¿La pantalla funciona también sin telemetría opcional?
- ¿Las acciones peligrosas o disruptivas piden confirmación?
- ¿La jerarquía sigue siendo sencilla y comprensible en una sola mano?
- ¿La apariencia sigue siendo Platinum Air y cercana al logotipo NOVORA?

---

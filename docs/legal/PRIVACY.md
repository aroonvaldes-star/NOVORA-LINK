# Aviso de Privacidad de NOVORA-LINK y LINK Control

**Versión:** 1.1 (borrador integral)

**Fecha de actualización:** 27 de octubre de 2026

**Responsable identificado en esta versión:** Aaron Yair Galarza Valdes

**Contacto para privacidad:** [aroonvaldes@gmail.com](mailto:aroonvaldes@gmail.com)

> **Pendiente antes de distribuir este aviso:** la ley mexicana exige que el aviso indique la identidad y el domicilio del responsable. El domicilio físico no consta en este repositorio y no se inventa ni se sustituye por un correo. Hay que completarlo y revisar la versión final antes de publicar o distribuir el software. El estado y los demás pendientes están en [`Legal/REVISION-LEGAL-2026-10.md`](Legal/REVISION-LEGAL-2026-10.md).

Este aviso describe los tratamientos observados en el código fuente de NOVORA-LINK y LINK Control al 27 de octubre de 2026. Se prepara con referencia a la Ley Federal de Protección de Datos Personales en Posesión de los Particulares (LFPDPPP) vigente en México. No afirma cumplimiento universal de todas las leyes internacionales: las normas aplicables dependen del lugar, las personas y las actividades de tratamiento.

## 1. Responsable y alcance

El responsable que se identifica para este producto es **Aaron Yair Galarza Valdes**. Su domicilio para efectos del aviso debe completarse antes de la distribución pública. Para solicitudes de privacidad se puede escribir a **aroonvaldes@gmail.com** con el asunto «Privacidad NOVORA-LINK».

El aviso cubre la aplicación NOVORA-LINK para Windows y LINK Control para Android, incluidos los datos tratados en las conexiones directas entre dispositivos. El software no requiere una cuenta NOVORA y el código revisado no configura un servidor propio de telemetría o una base de datos remota del titular. Esto no significa que no haya comunicaciones con servicios de terceros: se explican en la sección 4.

## 2. Datos tratados y dónde se usan

Las categorías dependen de las funciones que se activen. El contenido que el usuario ve, copia, graba o transfiere puede incluir datos personales de otras personas.

| Categoría | Ejemplos | Uso y ubicación observados |
|---|---|---|
| Dispositivo y conexión | Nombre o modelo Android, versiones de software, transporte USB/LAN, direcciones IP locales, estado y duración de sesión, identificadores y huellas de confianza de dispositivos | Se utilizan para conectar y autenticar los dispositivos. La configuración y los datos de confianza se guardan en los dispositivos participantes. |
| Contenido de pantalla y audio | Imagen de pantalla, audio capturado y comandos de control | Se procesa durante la sesión iniciada por el usuario y autorizada por Android; se transmite a la PC emparejada. No se envía al titular mediante un servicio NOVORA. La grabación que el usuario inicie se guarda en la ubicación local que elija. |
| Portapapeles y archivos | Texto o URL copiados, archivos, carpetas, nombres y rutas que el usuario seleccione | Se procesa cuando el usuario ejecuta copiar, pegar o transferir. El contenido viaja entre sus dispositivos y puede quedar en la carpeta de destino que eligió. |
| Configuración y confianza | Preferencias de motores, perfiles ExIn y dispositivos o PC autorizados | Se guarda localmente para permitir que las funciones y las conexiones recordadas operen. |
| Diagnóstico y mediciones locales | Errores, estados técnicos, salida de audio elegida, identificador de dispositivo de audio, tamaños de cola, tiempos y mediciones de una sesión | Se utiliza para resolver fallos o presentar diagnósticos. El código revisado puede escribir `NOVORA_error.log` en la carpeta temporal de Windows y `NOVORA-AUDIO-BT-VS-DIRECT.csv` en el Escritorio. Un error podría contener rutas locales u otros datos técnicos. Algunas pruebas conservan resultados sólo en memoria durante la sesión. |
| Solicitudes a servicios externos | Dirección IP pública visible para el servicio, fecha y hora de solicitud y metadatos técnicos HTTP; durante una prueba de velocidad, datos de prueba | Se envían directamente desde el equipo que ejecuta la función a GitHub, Google o Cloudflare, según la acción elegida. Esos proveedores aplican sus propios avisos y controles. |

La aplicación no pide una cuenta, contactos, agenda, ubicación precisa, identificador publicitario ni datos biométricos para las funciones descritas. No solicita intencionalmente categorías sensibles. Una pantalla, audio, archivo o texto que el usuario decida compartir podría contener información sensible o datos de terceros.

## 3. Finalidades y activación

Los datos se usan para:

1. Descubrir, emparejar, autenticar y mantener la conexión autorizada entre el teléfono y la PC.
2. Capturar y mostrar pantalla o audio, controlar el dispositivo y mover el contenido que el usuario seleccione.
3. Ejecutar las funciones de LinkEngine, incluida la ruta VPN que el usuario active.
4. Comprobar disponibilidad de versiones y descargar una actualización solicitada.
5. Ejecutar pruebas de conectividad, estabilidad o velocidad cuando el usuario las inicia.
6. Mostrar diagnósticos locales, proteger la sesión y corregir errores.
7. Atender solicitudes de privacidad o soporte que el usuario envíe.

La instalación por sí sola no se presenta como autorización general para capturar contenido. La captura requiere la autorización del sistema Android; accesibilidad, VPN y acceso a archivos requieren sus permisos o acciones correspondientes. El usuario puede detener la sesión, dejar de usar una función o revocar permisos desde Android. Cuando una ley exija consentimiento u otra base jurídica específica, el aviso y la interfaz del producto deberán ofrecer el mecanismo adecuado; un permiso técnico del sistema no sustituye automáticamente ese deber.

## 4. Comunicaciones con terceros y con el otro dispositivo

| Destino | Cuándo ocurre | Información que puede recibir |
|---|---|---|
| GitHub (`api.github.com`, `github.com`) | Al consultar releases o descargar una actualización o APK | IP pública, solicitud HTTP y metadatos técnicos necesarios para responder. El usuario puede revisar el [Aviso general de privacidad de GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). |
| Google (`www.google.com/generate_204`) | Cuando el usuario inicia la calibración experimental de estabilidad de Internet desde la PC | IP pública y metadatos técnicos de las solicitudes HTTPS. La sonda se repite aproximadamente cada 500 ms mientras el monitor experimental permanece activo, hasta que el usuario lo detiene o cierra. Los 30 segundos son el intervalo para calcular la referencia inicial, no el final automático de la sonda. No se envía el contenido de pantalla, archivos ni portapapeles a Google. Consulte la [Política de Privacidad de Google](https://policies.google.com/privacy). |
| Cloudflare (`speed.cloudflare.com`) | Cuando el usuario inicia la prueba de velocidad de Internet por LinkEngine | IP pública, solicitudes HTTPS y datos de prueba: hasta 8 MiB de descarga y 8 MiB de subida por prueba completa, además de solicitudes pequeñas de latencia. La prueba de túnel PC↔Android es local y no equivale a esta prueba de Internet. Consulte la [Política de Privacidad de Cloudflare](https://www.cloudflare.com/privacypolicy/). |
| Teléfono y PC emparejados | Mientras el usuario mantiene una sesión autorizada | Según la función: estado de conexión, pantalla, audio, control, portapapeles, archivos, mediciones y datos de la VPN. El contenido se intercambia entre los dispositivos de la sesión; no atraviesa un servidor NOVORA propio en el código revisado. |

El operador de cada servicio externo decide sus propios fines, periodos de conservación y transferencias posteriores. Este repositorio no permite asegurar en qué país se procesa cada solicitud ni sustituye los avisos de esos proveedores. El titular no recibe una copia de las solicitudes de actualización o pruebas de Internet en un servidor propio.

Si el usuario activa la VPN de LinkEngine, el tráfico de Internet del teléfono se enruta mediante la PC. La PC, sus redes, el proveedor de Internet y los sitios o servicios de destino pueden tratar metadatos o contenido conforme a sus propias condiciones. La aplicación sirve de ruta y no debe entenderse como una promesa de anonimato ni de que todo el recorrido esté fuera del alcance de terceros.

## 5. Conservación y eliminación

El código revisado mantiene preferencias, perfiles y datos de confianza en almacenamiento local; también puede crear los dos archivos de diagnóstico de Windows nombrados en la sección 2. El usuario controla los archivos de contenido grabados o transferidos en los dispositivos de destino. Los resultados de algunas pruebas viven sólo en memoria durante la sesión y se descartan al terminarla.

El titular no recibe esos archivos locales de manera automática. Desinstalar la aplicación no garantiza que Windows elimine registros, perfiles o archivos guardados fuera de la carpeta de instalación. El usuario puede cerrar la aplicación y eliminar los archivos locales que ya no necesite. Cuando se envía una solicitud de soporte, el usuario decide qué datos adjunta; debe omitir contenido, identificadores y registros que no sean necesarios.

Los datos que un proveedor externo conserve por sus propias operaciones deben solicitarse directamente a ese proveedor y están sujetos a su aviso. La eliminación local en NOVORA no elimina copias que el usuario haya creado o que un tercero haya recibido legítimamente.

## 6. Derechos y solicitudes

La persona titular puede solicitar acceso, rectificación, cancelación u oposición (ARCO), así como revocar su consentimiento y pedir que se limite el uso o divulgación, en los términos y excepciones de la ley aplicable. Envíe la solicitud a **aroonvaldes@gmail.com**. Indique el nombre, un medio para recibir respuesta, el derecho solicitado y una descripción suficiente para localizar los datos. La identidad o representación podrá verificarse conforme a la ley; no adjunte documentos de identidad sin que sean necesarios. Si se requiere verificación adicional, el responsable indicará cómo proporcionarla de forma razonablemente segura.

La LFPDPPP establece, como regla, que el responsable comunica su determinación en un máximo de 20 días desde la recepción y, si procede, hace efectivo el derecho dentro de los 15 días siguientes a la comunicación; cada plazo puede ampliarse una vez por un periodo igual cuando las circunstancias lo justifiquen. Los plazos y excepciones aplicables se determinan conforme al texto legal vigente.

Si la respuesta no es satisfactoria o no se recibe dentro del plazo aplicable, la persona titular puede acudir al procedimiento de protección de derechos previsto por la LFPDPPP ante la **Secretaría Anticorrupción y Buen Gobierno**, autoridad nombrada en la ley vigente. La referencia anterior al INAI que aparecía en una versión previa de este aviso queda retirada.

Las funciones locales pueden detenerse desconectando la sesión o revocando sus permisos. Eso no sustituye una solicitud ARCO respecto de los datos que el responsable conserve ni elimina registros que el usuario ya guardó en sus dispositivos.

## 7. Seguridad e incidentes

El producto usa controles técnicos y del sistema operativo para establecer sesiones autorizadas, administrar secretos y restringir operaciones a las capacidades disponibles. Ninguna medida elimina todo riesgo, en particular en una PC o teléfono compartido, infectado o administrado por otra persona. Mantenga actualizados los dispositivos, proteja las cuentas del sistema y empareje sólo equipos de confianza.

Si una vulneración de seguridad afecta de forma significativa los derechos patrimoniales o morales de titulares, la LFPDPPP prevé que el responsable les informe de inmediato para que puedan tomar medidas. El canal de contacto es **aroonvaldes@gmail.com**.

## 8. Niñas, niños y adolescentes

NOVORA-LINK no está diseñado para solicitar edad ni información de menores. El contenido que el usuario vea, grabe o transfiera puede incluir datos de niñas, niños o adolescentes; antes de usar esas funciones, el usuario debe tener facultades y atender las reglas aplicables a ese contenido. Este aviso no fija un umbral de edad ni sustituye las salvaguardas que exija la jurisdicción pertinente.

## 9. Alcance internacional

Las reglas mexicanas se aplican conforme a sus propios ámbitos. Otras normas pueden resultar aplicables cuando se cumplan sus criterios territoriales. Por ejemplo, el RGPD de la Unión Europea tiene los supuestos territoriales descritos en su artículo 3, incluida la oferta de bienes o servicios a personas en la Unión y ciertos casos de monitoreo. El proyecto no afirma que esta publicación, por sí sola, pruebe o descarte esos supuestos. Antes de dirigir el producto a una región o activar un tratamiento sujeto a normas adicionales, deben revisarse las obligaciones locales, las bases jurídicas, los avisos, los mecanismos de derechos y las transferencias internacionales.

## 10. Cambios al aviso

La versión vigente se publicará en el repositorio oficial y se actualizará su fecha. Si un cambio requiere nuevo aviso o consentimiento, se proporcionará el mecanismo exigido por la ley aplicable; la mera continuación del uso no se considera por sí misma consentimiento para finalidades nuevas.

Repositorio: <https://github.com/aroonvaldes-star/NOVORA-LINK>

## 11. Contacto

- **Responsable:** Aaron Yair Galarza Valdes.
- **Correo:** [aroonvaldes@gmail.com](mailto:aroonvaldes@gmail.com).
- **Asunto sugerido:** «Privacidad NOVORA-LINK».
- **Domicilio del responsable:** pendiente de incorporar antes de publicar la versión final.

### Fuentes legales principales

- [LFPDPPP, texto vigente de la Cámara de Diputados](https://www.diputados.gob.mx/LeyesBiblio/pdf/LFPDPPP.pdf).
- [Reglamento (UE) 2016/679, RGPD, EUR-Lex](https://eur-lex.europa.eu/legal-content/ES/TXT/?uri=CELEX:32016R0679).

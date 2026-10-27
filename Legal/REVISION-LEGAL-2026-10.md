# Revisión documental legal — NOVORA-LINK

**Revisado:** 27 de octubre de 2026

**Alcance:** documentos legales del repositorio y descripción de privacidad comparada con rutas visibles en el código fuente.

**Estado:** borradores actualizados; hay datos y controles de publicación pendientes.

Este documento registra fuentes y decisiones editoriales para mantener los avisos coherentes. No es dictamen jurídico ni certificación de cumplimiento. La revisión no sustituye asesoría de una persona abogada que conozca la titularidad, las operaciones, la distribución y los mercados reales del proyecto. Los textos del ZIP recibido se trataron como material de referencia; sus afirmaciones no se asumieron ciertas ni como instrucciones.

## 1. Fuentes normativas consultadas

| Materia | Fuente oficial consultada | Uso en los documentos |
|---|---|---|
| Datos personales en México | [LFPDPPP, Cámara de Diputados](https://www.diputados.gob.mx/LeyesBiblio/pdf/LFPDPPP.pdf), texto con última reforma DOF 14-11-2025 | Identidad y domicilio del responsable; categorías, finalidades, limitación, ARCO, seguridad e incidentes; autoridad competente. |
| Derecho de autor en México | [Ley Federal del Derecho de Autor, Cámara de Diputados](https://www.diputados.gob.mx/LeyesBiblio/pdf/LFDA.pdf), texto con última reforma DOF 14-05-2026 | Protección de software, derechos morales, licencias escritas, derechos patrimoniales y copia de respaldo de software. |
| Propiedad industrial en México | [Ley Federal de Protección a la Propiedad Industrial, Cámara de Diputados](https://www.diputados.gob.mx/LeyesBiblio/pdf/LFPPI.pdf), texto con última reforma DOF 15-01-2026 | Terminología actual para signos distintivos; los documentos no afirman que NOVORA tenga marcas registradas. |
| Protección internacional de obras | [Convenio de Berna, texto y estado en WIPO](https://www.wipo.int/en/web/treaties/ip/berne/index) y [artículo 5, WIPO Lex](https://www.wipo.int/wipolex/en/text/283698) | Referencia a protección por los países miembros según el Convenio, sin afirmar que una obra o signo concreto esté registrado. |
| Protección de datos en la UE | [Reglamento (UE) 2016/679, EUR-Lex](https://eur-lex.europa.eu/legal-content/ES/TXT/?uri=CELEX:32016R0679), en particular artículos 3 y 13 | Aclarar que el RGPD depende de los supuestos territoriales y exige información adicional cuando aplica; no declararlo aplicable universalmente. |

La expresión «leyes internacionales» no designa un régimen único que sustituya las leyes nacionales. Cada norma debe analizarse por su ámbito territorial, las personas afectadas, el producto ofrecido y las operaciones efectivamente realizadas.

## 2. Cambios documentales aplicados

- Se retiraron del aviso de privacidad la cita a la ley mexicana abrogada de 2010 y la referencia al INAI como autoridad actual para nuevas solicitudes. La ley vigente identifica a la Secretaría Anticorrupción y Buen Gobierno.
- Se dejó de decir que el domicilio del responsable puede solicitarse por correo: la LFPDPPP exige que el aviso incluya identidad y domicilio. El repositorio no contiene ese domicilio; el aviso lo señala como dato pendiente y no publica uno inventado.
- Se describieron los destinos y el momento de las conexiones externas observadas: GitHub para releases/actualizaciones, Google desde que se inicia la calibración experimental y mientras el monitor siga activo (sonda cada ~500 ms), y Cloudflare en la prueba de Internet. Se distingue la prueba local del túnel de la prueba externa; esta última puede intercambiar 8 MiB de descarga y 8 MiB de subida.
- Se aclaró que registros, perfiles y archivos de contenido pueden permanecer en los dispositivos locales después de desinstalar el producto; no se afirma borrado automático.
- Se separó el aviso informativo de privacidad de la aceptación contractual. Usar el software o continuar usándolo no se trata como consentimiento general para finalidades nuevas.
- El EULA conserva las protecciones obligatorias de consumidores y añade una reserva expresa de derechos inderogables y excepciones legales.
- La guía de contribuciones ya no pretende adquirir automáticamente, por enviar un pull request, derechos patrimoniales gratuitos, perpetuos e irrevocables ni derechos morales. Las contribuciones futuras requieren un acuerdo escrito separado, con alcance y condiciones por determinar.
- El aviso de copyright usa los nombres vigentes de las leyes mexicanas, distingue obras originales de componentes y aportes ajenos, y no afirma un registro ante INDAUTOR o IMPI.
- Se repararon rutas de inventario de componentes de terceros que seguían apuntando al árbol previo a `src/NOVORA LINK`.

## 3. Pendientes antes de distribución o declaración de cumplimiento

1. **Domicilio del responsable:** incorporar un domicilio físico válido en `PRIVACY.md` y en los avisos simplificados que se usen. Sin ese dato no debe presentarse el aviso como versión final conforme al artículo 15 de la LFPDPPP.
2. **Aviso simplificado en producto:** confirmar y, si falta, implementar su presentación antes o al recabar datos por medios electrónicos, con vínculo al aviso integral, conforme al artículo 16 de la LFPDPPP. Este cambio documental no verifica la interfaz instalada.
3. **Proveedores externos:** confirmar qué solicitudes llegan a GitHub, Google y Cloudflare en cada plataforma y versión, sus roles, ubicaciones, condiciones, conservación y mecanismos de transferencia internacional. Los enlaces de privacidad no prueban por sí solos una base legal o un acuerdo entre partes.
4. **Retención local:** documentar y verificar cada ubicación, dato registrado, plazo y procedimiento de borrado para PC y Android, incluidos logs, perfiles, confianza, grabaciones y archivos transferidos.
5. **Alcance internacional:** antes de promocionar o distribuir a regiones específicas, determinar si el RGPD, UK GDPR, leyes estatales de EE. UU. u otras normas se activan; documentar bases jurídicas, representación, transferencias, solicitudes de derechos e información local que correspondan.
6. **Términos de usuario y licencias:** revisión por especialista mexicano en derechos de autor, software y protección al consumidor. La LFDA vigente establece en sus artículos 30–33 reglas sobre forma escrita, onerosidad, temporalidad y remuneración de transmisiones/licencias; la NLPL y el EULA actuales no detallan contraprestación y plazo contractual específicos. No se concluyó que esos términos satisfagan dichas reglas ni se cambió el modelo económico o la licencia sin conocer la decisión del Titular. Confirmar por escrito ese modelo y revisar NLPL/EULA, la aceptación del instalador y las condiciones del producto Android antes de distribuir.
7. **Contribuciones existentes:** identificar aportes previos y acuerdos efectivamente firmados; esta actualización prospectiva de `CONTRIBUTING.md` no acredita ni cambia por sí sola derechos ya incorporados.
8. **Marcas y titularidad:** comprobar autoría, cadena de titularidad, permisos de activos y registros reales antes de hacer afirmaciones de propiedad o registro. No se encontró evidencia en el repositorio que permita declarar registros de copyright o marcas.

## 4. Mantenimiento

Revisar esta nota y los documentos vinculados cuando cambien código que accede a red, captura, registros o almacenamiento; las dependencias o destinos; la entidad responsable; los mercados; los términos de licencia; o una norma oficial. Registrar en cada revisión la fecha, la fuente normativa vigente y las verificaciones de código realizadas.

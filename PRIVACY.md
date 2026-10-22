# Aviso de Privacidad de NOVORA-LINK

**Responsable:** Aaron Yair Galarza Valdes  
**Producto:** NOVORA-LINK (escritorio Windows) y LINK Control (Android)  
**Versión del aviso:** 1.0  
**Vigente desde:** 21 de octubre de 2026

Este aviso se emite en términos de la Ley Federal de Protección de Datos
Personales en Posesión de los Particulares (LFPDPPP) de México y su
Reglamento.

## 1. Identidad y domicilio del responsable

El responsable del tratamiento es Aaron Yair Galarza Valdes, con domicilio
en los Estados Unidos Mexicanos. Para cualquier asunto relacionado con
este aviso, el correo de contacto es **aroonvaldes@gmail.com**.

NOVORA-LINK es una aplicación local de conexión entre un equipo Windows y
un teléfono Android. **No requiere cuenta, no opera un servicio en la nube
propio y no vende datos personales.**

## 2. Datos que se tratan

NOVORA-LINK trata únicamente los datos necesarios para establecer la
conexión y ejecutar las funciones que el usuario solicita:

### 2.1 Datos de dispositivo y conexión

- Nombre y modelo del dispositivo Android.
- Versión de Android, versión de LINK Control y versión de NOVORA-LINK.
- Tipo de transporte (USB o LAN), dirección IP local, puertos efímeros y
  estado de la sesión.
- Identificadores de sesión y tokens de emparejamiento, almacenados de
  forma protegida.
- Huellas de certificados y de PCs de confianza.

### 2.2 Contenido generado por el usuario

- **Pantalla y audio del dispositivo:** se capturan únicamente después de
  que el usuario concede el permiso de captura en Android y mientras la
  sesión de VisionEngine está activa.
- **Portapapeles:** se consulta o establece solo cuando el usuario ejecuta
  una acción de copiar o pegar.
- **Archivos y carpetas:** se transfieren solo cuando el usuario los
  arrastra, los pega o solicita la carpeta NOVORA del teléfono.
- **Entradas de mando y teclado/ratón:** se procesan para controlar el
  dispositivo durante una sesión autorizada.

### 2.3 Diagnóstico

- Registros locales de errores, estado de motores, versiones y eventos de
  conexión.
- Los mensajes de estado no incluyen el contenido del portapapeles.

**NOVORA-LINK no recopila** nombre, correo, teléfono, contactos, ubicación,
identificador de publicidad, datos biométricos ni el contenido de otras
aplicaciones.

## 3. Finalidades del tratamiento

### 3.1 Finalidades primarias

1. Establecer, autenticar y mantener la conexión entre el teléfono y la PC.
2. Transmitir video, audio y entradas durante una sesión autorizada.
3. Ejecutar transferencias de archivos y operaciones de portapapeles
   solicitadas por el usuario.
4. Proporcionar la ruta de Internet por VPN cuando el usuario la active.
5. Verificar actualizaciones y compatibilidad de versiones.
6. Prevenir accesos no autorizados y diagnosticar fallos.
7. Atender solicitudes de soporte cuando el usuario las envíe.

### 3.2 Finalidades secundarias

NOVORA-LINK **no realiza** mercadotecnia, perfilamiento publicitario,
venta de datos ni cesión a terceros con fines comerciales.

## 4. Consentimiento

El tratamiento se basa en:

- El consentimiento del usuario al instalar y utilizar la aplicación.
- La ejecución de las funciones solicitadas por el propio usuario.
- El cumplimiento de obligaciones legales aplicables.
- El interés legítimo en la seguridad del Software, cuando resulte
  aplicable.

La captura de pantalla y audio requiere una autorización específica del
sistema Android. El permiso de accesibilidad, el permiso VPN y el acceso a
archivos se solicitan por separado y pueden revocarse en cualquier
momento desde los ajustes del teléfono.

## 5. Transferencias y encargados

NOVORA-LINK no vende ni renta datos personales. Para funcionar realiza
comunicaciones limitadas con:

| Destino | Momento | Datos | Finalidad |
|---|---|---|---|
| GitHub (`api.github.com`, `github.com`) | Al buscar o descargar actualizaciones | IP, agente de usuario, versión instalada | Entrega de releases y metadatos |
| Google (`www.google.com/generate_204`) | Solo al ejecutar la prueba de Internet | IP, agente de usuario | Comprobar conectividad |
| Cloudflare (`speed.cloudflare.com`) | Solo al ejecutar el speed test | IP, agente de usuario, 8 MB de datos de prueba | Medir velocidad de Internet |
| PC y teléfono del usuario | Durante la sesión | Video, audio, entradas, archivos, portapapeles | Funciones solicitadas |

Estos servicios se rigen por sus propias políticas de privacidad. NOVORA-LINK
no controla sus prácticas.

Cuando la VPN de LinkEngine está activa, el tráfico de Internet del
teléfono se encamina a través de la PC. El operador de esa PC puede
observar metadatos de red. NOVORA-LINK no está diseñado para registrar ni
inspeccionar el contenido del tráfico más allá de lo necesario para
reenviar paquetes.

## 6. Limitación del uso y divulgación

Los datos se utilizan exclusivamente para las finalidades descritas. No se
divulgan a terceros salvo:

- Por obligación legal o requerimiento de autoridad competente.
- Para proteger derechos del Titular o de terceros.
- Con el consentimiento expreso del usuario.

## 7. Derechos ARCO y otros derechos

El usuario puede ejercer sus derechos de **Acceso, Rectificación,
Cancelación y Oposición (ARCO)**, así como revocar su consentimiento,
enviando una solicitud a **aroonvaldes@gmail.com** con:

- Nombre y medio de contacto.
- Descripción clara de la solicitud.
- Documentos que acrediten su identidad o representación.

El Titular responderá en un plazo máximo de **20 días hábiles** conforme a
la LFPDPPP. Si la solicitud resulta procedente, se hará efectiva dentro de
los **15 días hábiles** siguientes.

El usuario también puede presentar una queja ante el Instituto Nacional de
Transparencia, Acceso a la Información y Protección de Datos Personales
(INAI).

## 8. Conservación

Los datos se conservan localmente en el equipo o teléfono del usuario:

- Ajustes y preferencias: hasta que el usuario los elimine o desinstale la
  aplicación.
- PCs y dispositivos de confianza: hasta que el usuario los elimine.
- Registros de diagnóstico: hasta que el usuario los elimine o desinstale.
- Contenido de sesión (video, audio, portapapeles, archivos temporales): no
  se conserva por NOVORA-LINK; los archivos transferidos permanecen en la
  carpeta NOVORA del dispositivo de destino hasta que el usuario los
  elimine.

## 9. Medidas de seguridad

NOVORA-LINK aplica medidas técnicas razonables, entre ellas:

- TLS con validación de huella (pinning) en las conexiones LAN.
- Almacenamiento de credenciales de Windows mediante DPAPI.
- Almacenamiento de secretos en Android mediante AndroidKeyStore con
  AES‑GCM.
- Tokens de emparejamiento y límites de sesión.
- Ausencia de telemetría remota y de recopilación centralizada.

Ninguna medida es infalible. El usuario debe mantener actualizados el
sistema operativo, la aplicación y sus dispositivos.

## 10. Menores de edad

NOVORA-LINK no está dirigido a menores de 14 años. Si el Titular detecta
datos de un menor obtenidos sin el consentimiento de quien ejerce la
patria potestad, procederá a su eliminación.

## 11. Cambios al aviso

Cualquier cambio se publicará en el repositorio oficial con la fecha de
actualización. El uso continuado después de la publicación implica la
aceptación del aviso vigente.

Repositorio: https://github.com/aroonvaldes-star/NOVORA-LINK

## 12. Cookies y tecnologías similares

NOVORA-LINK es una aplicación de escritorio y móvil; no utiliza cookies de
seguimiento ni tecnologías publicitarias. El scaffold web presente en el
repositorio no forma parte del producto distribuido.

## 13. Contacto

- Responsable: Aaron Yair Galarza Valdes
- Correo: aroonvaldes@gmail.com
- Asunto sugerido: "Privacidad NOVORA-LINK"

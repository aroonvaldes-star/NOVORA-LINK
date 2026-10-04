# Transporte nativo VE LAN

Fecha: 2026-10-04

## Objetivo

Permitir que VisionEngine transmita la pantalla y el audio del teléfono hacia NOVORA PC mediante dos transportes seleccionables manualmente:

- USB/ADB, conservando el flujo existente.
- LAN nativo, sin cable, ADB ni depuración inalámbrica.

El nuevo transporte no reemplaza USB. NOVORA nunca cambiará automáticamente de un transporte al otro.

## Experiencia de vinculación

La primera conexión LAN siempre comienza con el código temporal de seis dígitos mostrado por NOVORA PC. El código autoriza una sesión de control TLS y permite guardar credenciales protegidas para recordar la PC.

Después de la primera vinculación, NOVORA-LINK puede reconectar por LAN sin pedir otro código. La confianza puede revocarse desde Android o desde la PC. Si se revoca, la siguiente conexión vuelve a requerir un código.

## Selección de transporte

La pestaña Motores de NOVORA-LINK incluirá un selector persistente `VE USB | VE LAN`.

- En USB, VisionEngine usa sin cambios el transporte ADB reverse/forward existente.
- En LAN, VisionEngine usa el transporte directo descrito en esta especificación.
- El selector sólo cambia por acción explícita del usuario.
- Un fallo LAN detiene VE y muestra el diagnóstico; no activa USB silenciosamente.
- LE y la sesión de control permanecen independientes del estado de VE.

## Arquitectura

### Sesión de control

La sesión LAN autenticada existente coordina el inicio y la detención de VE. No transporta los paquetes multimedia. Su responsabilidad es validar la confianza, negociar la oferta temporal y publicar estados.

### Oferta VE LAN

Al iniciar VE LAN, NOVORA PC crea una oferta temporal que contiene:

- identidad y revisión del protocolo;
- dirección IPv4 privada de la interfaz LAN elegida;
- puertos de video, audio y control;
- huella del certificado TLS;
- tokens aleatorios independientes y de un solo uso para cada canal;
- opciones de captura negociadas;
- fecha de caducidad corta y un identificador de sesión.

La oferta sólo se entrega dentro de la sesión de control TLS autorizada. No se almacena en preferencias, archivos ni registros.

### Servidor de PC

NOVORA PC agrega un transporte `VELanTransport` junto al transporte ADB existente. Este componente:

- abre listeners TLS temporales únicamente en la interfaz LAN seleccionada;
- valida la huella esperada, el token, el canal, la sesión y la caducidad;
- admite una conexión por canal y consume cada token al primer intento válido;
- entrega streams compatibles con el lector actual de VisionEngine;
- cierra listeners y sockets al detener VE, perder control LAN o agotar el tiempo;
- no afecta los listeners ni rutas de LinkEngine.

El pipeline posterior conserva el protocolo, decodificador, presentación y métricas actuales de VisionEngine.

### Cliente Android

NOVORA-LINK incorpora un servicio foreground de captura VE independiente de AppControl. El servicio:

- solicita autorización de MediaProjection antes de capturar;
- usa MediaCodec por hardware para producir H.264;
- captura audio cuando Android y la configuración lo permiten;
- conecta video, audio y control a los endpoints TLS de la oferta;
- valida la huella del certificado de NOVORA antes de enviar datos;
- se detiene si termina MediaProjection, vence la oferta, cambia la sesión o se pierde el canal de control;
- muestra una notificación persistente mientras exista captura.

Se reutilizarán los componentes Android de encoder, audio, composición y protocolo que sean neutrales al paquete. El servicio, sus acciones y su almacenamiento pertenecerán exclusivamente a `com.novora.linkclient`.

## Flujo de inicio

1. El usuario vincula NOVORA-LINK mediante el código de seis dígitos, o reconecta usando una confianza guardada.
2. El usuario selecciona `VE LAN` y pulsa Iniciar VE.
3. Android envía `startVideoLan` por la sesión de control autorizada.
4. NOVORA valida capacidad, interfaz y estado; crea listeners y una oferta de un solo uso.
5. Android solicita el permiso de captura del sistema si todavía no está concedido para esa sesión.
6. El servicio VE valida la oferta y abre los canales TLS.
7. NOVORA autentica los canales y los conecta al pipeline actual de VisionEngine.
8. Cuando llega la cabecera válida y el primer frame, ambos lados publican el estado `Transmitiendo por LAN`.

El motor no se considera iniciado antes de recibir y validar el primer frame.

## Detención y errores

La detención puede originarse en Android, PC, MediaProjection, pérdida de red o pérdida de confianza. En todos los casos se liberan:

- MediaProjection, VirtualDisplay, encoder y captura de audio;
- listeners, sockets y tokens temporales;
- decodificador y presentación de VisionEngine;
- estado de operación pendiente.

Los errores se clasifican sin incluir secretos:

- vinculación requerida;
- permiso de captura rechazado;
- oferta vencida o inválida;
- certificado no reconocido;
- canal rechazado;
- red inaccesible;
- tiempo de inicio agotado;
- stream interrumpido;
- codec no compatible.

No habrá fallback automático a USB. La interfaz conserva la selección LAN y permite reintentar.

## Estados de interfaz

NOVORA-LINK y NOVORA PC mostrarán estados equivalentes:

- Desconectado;
- Vinculando por código;
- Listo por LAN;
- Esperando permiso de captura;
- Preparando VE LAN;
- Conectando canales;
- Transmitiendo por LAN;
- Degradado, cuando video continúa pero un canal opcional falla;
- Error, con causa accionable;
- Deteniendo.

Los datos mostrados provienen del estado real del motor y del transporte, no de temporizadores visuales.

## Seguridad

- Todo canal LAN usa TLS con pinning de certificado.
- La oferta sólo viaja por el canal de control TLS ya autorizado.
- Los tokens son criptográficamente aleatorios, separados por canal, caducan rápidamente y se consumen una vez.
- Sólo se aceptan IPv4 privadas de la interfaz LAN seleccionada.
- Los listeners existen únicamente durante la preparación y transmisión.
- Los registros omiten tokens, ofertas completas y datos de captura.
- Perder o revocar la confianza cierra de inmediato el transporte multimedia.

## Compatibilidad

- USB/ADB mantiene su comportamiento y protocolo actuales.
- AppControl no se modifica ni comparte servicio, almacenamiento o identidad con NOVORA-LINK.
- LinkEngine y ExInEngine no dependen del transporte elegido para VE.
- El protocolo negociará versión y capacidades para rechazar limpiamente clientes incompatibles.

## Pruebas y criterios de aceptación

La implementación se considera terminada cuando:

1. Una instalación nueva exige código de seis dígitos antes de ofrecer VE LAN.
2. Una PC recordada reconecta sin otro código y una confianza revocada no reconecta.
3. VE LAN inicia sin dispositivo ADB y sin cable USB conectado.
4. Video H.264 llega, se decodifica y se presenta con estados y métricas reales.
5. Audio y control funcionan cuando están habilitados y degradan de forma explícita si fallan.
6. Desconectar Wi-Fi detiene y limpia todos los recursos sin activar USB.
7. Elegir USB mantiene el flujo ADB anterior sin regresiones.
8. Cambiar el selector durante una sesión requiere detener el transporte activo antes de iniciar el otro.
9. Tokens repetidos, vencidos o enviados al canal incorrecto son rechazados.
10. Las pruebas automatizadas cubren negociación, seguridad, estado, limpieza y selección manual; las pruebas físicas cubren inicio, transmisión, detención y reconexión en Android real.

## Fuera de alcance

- Selección automática o fallback automático entre USB y LAN.
- Transmisión mediante servicios externos, WebRTC o nube.
- Acceso desde Internet o redes públicas.
- Cambios visuales o funcionales en AppControl.
- Reemplazo del transporte USB/ADB existente.

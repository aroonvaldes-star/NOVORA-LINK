# Conectividad dinámica de NOVORA-LINUX y LINK Control

Estado: diseño propuesto para el plan solicitado; no implementado.

## Objetivo solicitado

Permitir iniciar VisionEngine, LinkEngine y ExInEngine sin que el usuario tenga
que escoger USB o LAN antes de usar un motor. USB y LAN permanecen como
transportes internos. La conexión pertenece a un teléfono identificado y
autorizado; el motor se inicia sobre una ruta disponible de ese teléfono.

La solicitud permite iniciar los tres motores por cualquiera de las rutas;
no implica iniciar los tres automáticamente al conectar el teléfono.
El alcance es NOVORA-LINUX y los cambios de LINK Control necesarios para ello.
Se conserva compatibilidad con Windows, sin ampliar este trabajo a convertir
su interfaz de escritorio al nuevo coordinador.

## Evidencia del código actual, 2026-11-08

| Área | Estado observado | Consecuencia |
|---|---|---|
| Host Linux | `LinuxControlHost` conserva `_selectedTransport`, servidores separados y selección manual | Falta un coordinador de rutas e intención de motores |
| Captura | USB y LAN usan `LinuxVisionEngine` / `LinuxVisionLanEngine` | Se pueden reutilizar ambos runtimes; no basta cambiar una etiqueta |
| LinkEngine | `ILinuxLinkEngine` tiene implementaciones USB y LAN | Hay una superficie común, pero el arranque depende de la sesión autorizada |
| ExIn | `MainWindow.ExIn.cs` busca un serial ADB y usa LAN como fallback ADB | Control LAN autorizado no demuestra capacidad HID |
| Salida HID actual | `ExInHidOutput` ejecuta `adb -s <serial> shell hid -`; UI usa comandos shell | Se necesita permiso shell, no accesibilidad como sustituto del mando |
| Confianza PC | Linux genera `DeviceId` por inscripción y conserva hashes de tokens | No existe identidad lógica demostrada entre dos inscripciones independientes |
| Confianza Android | `NLAndroidStorageTrustedPcs.Save` sustituye por huella de PC; cada registro tiene un transporte | Una segunda ruta puede reemplazar la primera credencial |
| Control Android | `NLControlSession` posee un solo cliente y generación, cierra el anterior al conectar | La recuperación debe coordinar el cambio; no asumir dos controles activos |
| Cierre | El servidor cancela la sesión y detiene motores al perder el control | El failover necesita guardar intención fuera del socket perdido |

El contexto AGENTS exige preservar UHID y no introducir scrcpy. El código
inspeccionado de ExIn ya usa el comando HID del sistema a través de ADB; no se
propondrá volver a introducir el servidor legado. Se conservarán avisos para
dependencias o código derivado que permanezcan.

## Enfoque propuesto y alternativas

**Recomendado: coordinador de dispositivo y rutas + adaptadores de motor.**
Mantener los runtimes probados USB/LAN, unificar identidad, elección, intención y
recuperación. Android sigue resolviendo permisos y abriendo sus canales.
Reduce el riesgo sobre video, VPN y archivos, y permite migración compatible.

Alternativa: sustituir todos los canales por un único protocolo nuevo. Exige
reescribir captura, VPN, HID y transferencia antes de entregar el objetivo.
No se recomienda para este incremento.

Un fallback que solo pruebe USB y luego una IP no satisface la solicitud:
carece de identidad entre rutas, recuperación de motores y capacidad HID real.
La recomendación y los valores de política siguientes son decisiones propuestas
del plan, no preferencias adicionales confirmadas por el usuario.

## Identidad, confianza y compatibilidad

- Identidad lógica del teléfono vinculada a una clave de instalación Android
  no exportable, separada de la clave AES que cifra las PC guardadas.
- Prueba de posesión ECDSA P-256/SHA-256 con desafío del PC, nonce de un solo uso
  y vinculación a huella de PC y generación. El identificador público no autoriza.
- Cada ruta mantiene su credencial y endpoint; la relación entre rutas se acepta
  solo dentro de sesiones autenticadas con prueba de la misma instalación.
- Nunca unir teléfonos por nombre, IP, número de serie ADB ni primer dispositivo
  enumerado. El serial ADB puede servir como localizador después de verificarlo.
- Android conserva una PC por huella y una colección de credenciales USB/LAN;
  actualizar un endpoint no elimina la credencial de otra ruta.
- Revocar un dispositivo revoca todas sus rutas; olvidar una PC en Android
  detiene sus motores y recuperaciones pendientes.
- Negociar la capacidad aditiva `dynamicConnectionV1`; un cliente antiguo mantiene
  sus acciones actuales y no recibe extensiones que cambien sus respuestas.
- Migrar almacenes mediante escritura atómica; conservar respaldo y no regenerar
  silenciosamente una identidad que no pueda leerse. Una identidad nueva tras
  reinstalación Android requiere autorización nueva.
- Los archivos compartidos de `src/NOVORA LINK/Control/` se consumen también por
  Android y Windows: sus cambios se someten a regresión de ambos consumidores.

## Política de conexión propuesta

1. Una sola selección de teléfono. Si hay varios y no existe elección previa,
   pedir cuál; nunca activar un motor sobre otro teléfono automáticamente.
2. Auto es el modo predeterminado. Conservar una ruta autorizada y saludable.
3. Al iniciar una conexión sin ruta activa, preferir USB autorizado; si no está
   listo, usar LAN autorizada del mismo teléfono sin esperar indefinidamente USB.
4. Conectar USB mientras LAN funciona no interrumpe los motores ni provoca salto.
5. Preparar y autenticar una ruta antes de anunciarla como disponible. Detección
   física, ADB autorizado, TLS autorizado y capacidad del motor son estados distintos.
6. Al perder la ruta, invalidar su generación, neutralizar entrada y recuperar
   una alternativa autorizada del mismo teléfono. No reutilizar ofertas o leases.
7. Reintentos por pérdida transitoria: 0, 1, 2, 4 y 8 segundos; después como máximo
   cada 15 segundos, con jitter de hasta 20%. Reiniciar el intento ante un evento
   nuevo de ruta. Inyección de `TimeProvider` y aleatoriedad para pruebas.
8. Cada intento de conexión tiene plazo de 8 segundos; el arranque espera como
   máximo 30 segundos por una ruta y después informa error recuperable.
9. La falta de confianza o un permiso denegado detienen el intento hasta una
   acción explícita. Una huella distinta no es una caída transitoria.
10. Tras dos fallos de canal consecutivos de una ruta, enfriarla 15 segundos;
    probar una alternativa válida sin desconectar una sesión sana.

Los temporizadores de protocolo que ya existen no se sustituyen por estos
valores; son límites del coordinador. Descubrimiento ADB mediante `track-devices`
y cambios de red mediante eventos, con un refresco acotado para recuperar
eventos perdidos. No usar un bucle de sondeo rápido permanente.

## Arranque de motores

Una intención identifica `DeviceId`, `EngineKind`, `OperationId` y `Epoch`.
El coordinador deduplica PC/Android concurrentes y devuelve estado observable:
`WaitingForConnection`, `WaitingForPermission`, `Starting`, `Running`,
`Recovering`, `Stopping`, `Stopped` o `Failed`.

Una conexión lógica transporta el control; cada motor obtiene una concesión de
canal con la misma identidad. Preferir la ruta activa; un adaptador puede usar
una segunda ruta autorizada del mismo teléfono si la capacidad lo exige. Esto
no permite dirigir control, video o mando a teléfonos distintos.

| Motor | USB | LAN | Confirmación de arranque |
|---|---|---|---|
| VisionEngine | AppControl + ADB reverse | AppControl + canales TLS | Canales autorizados y recepción de frames; audio según disponibilidad |
| LinkEngine | Relay + CONTROL/DATA reverse | Relay + gateway DATA TLS | Android autoriza VPN y se autentica DATA; no basta `LE_READY` |
| ExInEngine | ADB autorizado + HID del sistema | ADB inalámbrico emparejado + HID del sistema | Salida HID lista y reporte reconocido por Android |

Para poder iniciar desde PC, `startLink` necesita una solicitud PC→Android en el
snapshot, análoga a la de autorización de video. Android inicia la operación y
resuelve el permiso VPN; el PC no puede omitir ese diálogo. Las acciones ya
existentes de Android continúan sirviendo a clientes antiguos.

## ExIn LAN: trabajo obligatorio, no fallback supuesto

La autorización TLS de LINK Control no concede a una app Android ordinaria la
capacidad shell que exige `hid`. El plan incluye preparar ADB inalámbrico cuando
sea necesario, con consentimiento, emparejamiento y puertos descubiertos; no
suponer `5555` ni activar `adb tcpip` silenciosamente.

- En dispositivos que ofrecen depuración inalámbrica con emparejamiento, permitir
  configuración inicial por LAN sin cable. Asociar el endpoint al mismo teléfono
  mediante la prueba de instalación, no por coincidencia de IP.
- Un teléfono previamente preparado por USB también puede proporcionar una ruta
  ADB inalámbrica válida; no se presupone que sobreviva a un reinicio.
- Exponer `NeedsSetup`, `Ready`, `Unavailable` y el motivo por motor. No presentar
  ExIn como iniciado hasta tener salida HID real.
- Si el dispositivo no permite obtener shell HID por LAN con los mecanismos
  admitidos, ese caso bloquea la aceptación universal en ese dispositivo. No se
  sustituye por accesibilidad ni se declara cumplido ocultando la condición.

Eliminar también ADB de ExIn exigiría un agente Android con privilegios y otro
proyecto de distribución/activación. No es una consecuencia automática de
unificar USB/LAN y no se añade como dependencia implícita en este plan.

## Recuperación

- Mantener solo las intenciones que el usuario dejó activas en esta sesión.
  Una parada explícita, revocación o cierre cancela recuperación y arranques.
- VisionEngine: cerrar canales viejos, solicitar una oferta nueva y reactivar
  captura solo si Android conserva permiso válido; si no, esperar consentimiento.
  Terminar el segmento de grabación anterior o conservar `.partial` con diagnóstico.
- LinkEngine: reconstruir gateway/VPN/DATA según corresponda. Los flujos TCP/UDP
  pueden perderse; no prometer migración transparente de conexiones de terceros.
- ExIn: neutralizar antes de reenganchar, destruir dispositivos de la generación
  anterior y recrear HID; no dejar botones, sticks ni teclas pulsados.
- FILES V2: cancelar leases de la generación anterior, limpiar parciales según su
  contrato y ofrecer reintento. No añadir reanudación por offsets sin otro contrato.
- Una respuesta tardía no puede publicar un motor de una generación anterior.

## Interfaz y criterios de aceptación

Conservar el diseño Windows aprobado. Inicio muestra el teléfono y «Conectando»,
«Conectado», «Reconectando» o un bloqueo concreto; USB/LAN son detalle técnico.
Las acciones son iniciar/detener motor. La elección manual de ruta queda en
opciones avanzadas para diagnóstico y nunca es requisito del flujo normal.

La entrega se acepta solo después de probar cada motor desde PC y Android en
USB solo, LAN solo y ambas rutas, más pérdida/cambio de ruta, dos teléfonos,
permisos, revocación, grabación y archivos. LAN solo de ExIn debe demostrar mando
real, documentar su preparación y comprobarse sin cable conectado.

No modificar firewall, instalar APK, emparejar depuración ni publicar una versión
como efecto lateral de elaborar o ejecutar pruebas locales del plan. La
aceptación física requiere un entorno y dispositivos expresamente disponibles.

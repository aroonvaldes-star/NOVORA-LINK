# Diseño: envío de reportes ExIn por LAN con UHID en AppControl

Fecha: 2026-10-19. Estado: propuesta pendiente de decisión del usuario. Sin código de producto.

## Problema

ExIn convierte el mando conectado a la PC en un gamepad virtual dentro del teléfono. Hoy la salida es
`ExInHidOutput` (`src/NOVORA/Integration/ExInHidOutput.cs`), que ejecuta `adb -s <serie> shell hid -` y
escribe por stdin los comandos de `ExInHidProtocol` (`Register`, `Report`, `Destroy`). Por eso los
reportes solo salen con USB o con depuración Wi-Fi de ADB. Con la sesión LAN de control activa, el
indicador "Reportes enviados" sigue en 0.

## Restricción técnica verificada

- El código fuente de AppControl (`src/NOVORA.Android`) no contiene ninguna ruta UHID, `InputManager` ni
  `BluetoothHidDevice`. AGENTS.md indica que el servicio de accesibilidad tampoco implementa UHID.
- El comando `shell hid` es una herramienta de shell con acceso a `/dev/uhid`. Una app normal de Android no
  puede abrir `/dev/uhid` (SELinux lo impide sin privilegios de shell o root).
- `InputManager.injectInputEvent` requiere el permiso `INJECT_EVENTS`, reservado a apps del sistema.
- El servicio de accesibilidad puede enviar gestos y teclas, pero no crear un dispositivo gamepad con
  reportes HID completos.

Conclusión: **AppControl, tal como está instalada, no puede crear el gamepad virtual por LAN sin un
privilegio que no tiene.** Un diseño "solo LAN" completo requiere uno de los caminos siguientes.

## Opciones

### A. Mantener ADB para la salida HID (sin cambios en el teléfono)
- Transporte: USB o depuración Wi-Fi de ADB (ya soportado).
- LAN se usa para control, telemetría y estado, que ya funcionan.
- Coste: ninguno. Riesgo: ninguno nuevo.
- Limitación: ExIn no envía reportes sin ADB.

### B. Ayudante privilegiado iniciado una vez por ADB, con reportes por LAN (recomendado si LAN es obligatorio)
- Un proceso `novora-hid` se lanza una vez con `adb shell` (el mismo mecanismo que `shell hid`). Escucha en
  `127.0.0.1` y recibe reportes por socket local.
- AppControl, que sí tiene conexión LAN con la PC, recibe los reportes por el canal de control y los
  reenvía al ayudante por el socket local.
- Requiere ADB una vez por arranque del teléfono. Después, el envío de reportes va por LAN.
- Riesgos: el ayudante se detiene al reiniciar el teléfono; hay que relanzarlo; hay que validar que el
  canal local no sea accesible desde otras apps (puerto en loopback con token).

### C. Root
- Permite UHID directo desde AppControl. Requiere root del teléfono; no recomendado para un equipo de
  pruebas de usuario final.

## Propuesta de protocolo LAN (solo para la opción B)

Nuevas acciones en el canal de control (`NLControlProtocol`), con el mismo formato de `ExInHidProtocol`:

| Acción | Dirección | Contenido |
|---|---|---|
| `exin.hid.register` | PC → Android | id, nombre, vendor, product, descriptor |
| `exin.hid.report` | PC → Android | id, reporte (bytes) |
| `exin.hid.destroy` | PC → Android | id |
| `exin.hid.status` | Android → PC | ayudante listo / caído, id activos |

Reglas:
- Un reporte solo se acepta si el ayudante confirmó el `register` del mismo id.
- Los reportes se descartan si el canal de control se cae; no se encolan.
- `exin.hid.status` alimenta el estado "Reportes enviados" y "Teléfono destino" en la PC.

## Criterios de aceptación (opción B)

1. Con el A56 en LAN y sin ADB activo tras el arranque inicial del ayudante, un mando físico conectado a
   la PC mueve un gamepad virtual en el teléfono.
2. "Reportes enviados" sube mientras hay entrada y se detiene al desconectar el mando.
3. Al cerrar la sesión LAN, ExIn deja de enviar y no deja teclas o botones pulsados (reporte de liberación).
4. Con la opción A (ADB) sigue funcionando igual, sin regresión.

## Pruebas previstas

- Unitarias: el codec de `exin.hid.*` (límites de tamaño, ids desconocidos, reporte antes de register).
- Integración: `NOVORA.ExInOutput.Tests` y `NOVORA.ExInRuntime.Tests` deben seguir pasando.
- Física: A56 con mando Xbox One Elite 2, comprobando en la app del teléfono qué gamepad recibe las entradas.

## Decisiones pendientes del usuario

1. ¿Es obligatorio que ExIn funcione sin ADB? Si no, la opción A basta.
2. Si es obligatorio: ¿aceptas un paso de ADB por arranque del teléfono (opción B)?
3. ¿Prefieres que el ayudante se empaquete dentro de la APK de AppControl o como archivo aparte?

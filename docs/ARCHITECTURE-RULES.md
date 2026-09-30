# NOVORA-LINK — Reglas de arquitectura

## Motores

- **LinkEngine (LE):** conectividad, VPN/reverse tethering, CONTROL/DATA, RelayCore, tráfico y recuperación.
- **VisionEngine (VE):** captura, video, audio, decodificación, render, grabación y su Recovery.
- **ExInEngine:** mouse, teclado, táctil y gamepad, con lifecycle independiente de VE.
- **Integraciones NOVORA:** archivos, Share, Drag & Drop, portapapeles, apps, notificaciones y capacidades Android↔Windows. No requieren una sesión de video activa salvo cuando el contrato Android específico usa temporalmente el canal de control de VE.
- **Privacidad:** política transversal compartida por VE, ExIn e Integraciones; no pertenece al lifecycle de un solo motor.
- **RemoteNV:** canal de control Android→NOVORA. No forma parte del Data Plane de LinkEngine.

## Event-driven por defecto

1. Polling está prohibido por defecto.
2. Preferencia: evento → callback → push → socket readiness → notificación del SO → contador en memoria → snapshot bajo demanda.
3. Timers sólo para deadlines, timeouts, debounce, pacing o backoff después de un fallo real.
4. UI invisible no justifica consultas informativas.
5. Congestión, `WouldBlock`, high-watermark o backpressure pertenecen a TrafficEngine; no disparan Recovery.
6. ADB shell periódico es último recurso.

## Recovery

`MonitorRecoveryLE` reacciona a `ManagerTransportLE.SessionChangedLE`. Los retrasos de confirmación de 3 s/5 s son deadlines one-shot para evitar recuperar ante fallos transitorios. Si el dispositivo desaparece, se usa `adb wait-for-device` y una validación posterior única.

## RemoteNV

RemoteNV usa protocolo v2. Cada configuración de dispositivo genera un token criptográfico efímero de 256 bits. El token se entrega al APK mediante el `Intent` iniciado por ADB, no se persiste y es obligatorio en el `HELLO` remoto.

## Capacidad

NOVORA puede recordar varios teléfonos conocidos, pero mantiene **una sola sesión activa por PC**. El teléfono activo concentra VE, LE, ExIn, Integraciones y control; cada subsistema conserva su lifecycle y cambiar de teléfono es una transición explícita. Una futura capacidad simultánea requiere ADR, aislamiento por sesión y benchmarks de recursos.

## Nomenclatura

Código de motores: `Función + Carpeta + Motor`, por ejemplo `MonitorRecoveryLE`, `StatusCoreVE`.

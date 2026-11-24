# Estabilidad de VisionEngine/LinkEngine/ExIn por LAN — Plan de implementación

**Estado:** plan; no se ha modificado código de producto.
**Proyecto:** NOVORA-LINK, `GITHUB/NL`.
**Fecha:** 2026-10-17.

## Objetivo

1. Estabilidad durante toda la sesión LAN, sin depender de que la red esté libre.
2. Protección frente a descargas y tráfico de otros dispositivos en la misma red.
3. VE sin subidas de latencia en juegos o video. Si hace falta bajar carga, se adapta primero la resolución, después la calidad, y nunca los fps por debajo de 30 mientras se pueda.
4. Quitar de AppControl (teléfono) la configuración de VE, ExIn y LE. Esa configuración pasa al PC.

## Hechos del código que motivan el plan

- `NLAndroidVideoPacketQueue`: `MaximumPackets = 8` y `MaximumQueueAge = 60 ms`. Cualquier pico de red mayor a 60 ms descarta fotogramas y pide un IDR.
- `NLAndroidVideoLanClient`: buffer de envío de video de 128 KiB. Con latencias de 100 a 200 ms limita el throughput a unos 5 a 10 Mbps.
- `NLAndroidVideoEncoderConfiguration`: CBR cuando el codec lo soporta, IDR cada 2 s.
- `NLUIWindowMainExperimentalNetwork`: adaptación de tasa (mínimo 250 kbps, pasos de fps 30/15/10). Está detrás de una casilla desactivada por defecto.
- `NLNetworkSharedLinkBudget`: reparte el enlace entre VE y LE cuando comparten ruta, solo tras confirmación del usuario.
- La telemetría de captura viaja en las respuestas del canal de control y solo se lee en la UI experimental.

## Arquitectura propuesta

- **Control de calidad en el PC:** el PC decide la tasa, resolución y fps. El teléfono solo ejecuta la orden. Así el PC conoce la red y el teléfono no decide por sí solo.
- **Medición continua de la sesión:** el PC mide latencia de control, pérdidas, reintentos y retardo de cola en ventanas de 1 s, sin sondeos extra fuera de la sesión.
- **Política de adaptación con histéresis:** bajar rápido al detectar congestión y subir lento tras varios segundos estables.
- **Prioridad de tráfico:** marcar el tráfico de NOVORA con DSCP en el PC (`NetQosPolicy`) y dejar en el router la prioridad para el teléfono y el PC.
- **Separación de canales:** video, control, LE y Exchange con límites propios; Exchange nunca compite con video en reproducción.

## Fases

### Fase 1 — Política de adaptación en el PC (sin cambiar el teléfono)
- Crear `src/NOVORA/VisionEngine/Adaptation/VEAdaptivePolicy.cs` con la lógica de tasa, resolución y fps.
- Escalones de resolución: 1280, 960, 720 px de lado largo. Escalones de fps: 60, 45, 30. Orden de degradación: primero resolución, después tasa, al final fps.
- Histéresis: bajar tras 2 s de congestión; subir tras 8 s estables y un paso a la vez.
- Pruebas: harness nuevo `tests/NOVORA.VisionEngine.Adaptation.Tests` con simulaciones de latencia (20 ms, 100 ms, 250 ms), pérdidas y colas. Criterio: nunca baja de 30 fps mientras haya escalón de resolución disponible.

### Fase 2 — Cola y buffer de envío en el teléfono (cambio de APK, n+1 → 1.4.106)
- `MaximumQueueAge`: de 60 ms a 200 ms, con descarte solo cuando la cola supera ese tiempo.
- `VideoSendBufferBytes`: de 128 KiB a 512 KiB.
- Mantener IDR cada 2 s; no pedir IDR por picos menores a 500 ms.
- Pruebas: harness de cola `tests/NOVORA.FileProtocol.Tests` o uno nuevo, con picos sintéticos.

### Fase 3 — Tráfico de otros dispositivos y prioridad
- PC: `NetQosPolicy` con DSCP para el proceso NOVORA (puertos 27215, 27216, 27184).
- PC: detectar saturación del adaptador Wi-Fi (bytes/s frente al enlace negociado) y avisar en la UI.
- Router: documentar la prioridad para el teléfono y el PC; no se puede cambiar desde la app.

### Fase 4 — Quitar configuración de VE, ExIn y LE de AppControl
- Inventario: pantallas de configuración del teléfono que muestran VE, ExIn o LE (`NLAndroidUIActivity` y `layout/nl_android_engines.xml`, `nl_android_home.xml`).
- Eliminar o dejar solo lectura de estado. Los ajustes pasan a `NLUIWindowMain` en el PC.
- Verificar que el teléfono no guarda ajustes de motor que el PC ya no envía.
- Pruebas: compilar Android en Release; revisar manualmente la pantalla de configuración tras instalar.

### Fase 5 — Telemetría bajo demanda
- El PC activa la telemetría de captura solo cuando la pantalla de telemetría está abierta (capacidad `StabilityTelemetry` negociada).
- Con la pantalla cerrada, el teléfono no adjunta telemetría a las respuestas.

## Pruebas y criterios de cierre

- **Harnesses:** adaptación, cola de video, FILES v2 y Exchange sin regresiones (los harnesses existentes deben seguir en PASS).
- **Builds:** `NLProjectDesktop` y `NLProjectAndroid` en Release, 0 errores y 0 advertencias nuevas.
- **Físicas (cuando se haga la validación):** sesión LAN de 20 minutos con video de juego y video de YouTube; registrar pérdidas, latencia y fps presentados; sin bajar de 30 fps mientras la resolución pueda bajar.
- **Independencia:** la sesión LAN no usa ADB ni `adb reverse` en ningún paso.

## Restricciones

- No cambiar la versión del PC ni el instalador (la regla n+1 solo aplica a la APK Android).
- Cada APK nueva sube a n+1 y se sincroniza con la copia de Windows y el descriptor.
- No desactivar seguridad de la sesión (TLS, pairing, confianza).
- No añadir dependencias nuevas de terceros.

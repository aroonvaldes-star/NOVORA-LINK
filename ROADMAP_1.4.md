# NOVORA-LINK 1.4 — ROADMAP PÚBLICO

**Estado:** En desarrollo  
**Canal actual:** Experimental / Próximamente  
**Precio:** **GRATIS**

NOVORA-LINK 1.4 será la siguiente gran actualización del proyecto. El desarrollo continúa sobre una base separada de la rama estable 1.3 para evitar publicar código incompleto o romper la versión disponible actualmente.

## Principio de la versión 1.4

La prioridad de NOVORA-LINK 1.4 es evolucionar de una aplicación que coordina herramientas hacia una plataforma con motores propios, mejor observabilidad, recuperación selectiva y menor trabajo innecesario.

Regla de rendimiento:

> **Reducir el polling al mínimo posible y utilizar eventos, señales de estado y trabajo bajo demanda siempre que sea viable.**

---

## LinkEngine

LinkEngine es el motor de conectividad en desarrollo para NOVORA-LINK.

### En desarrollo

- Cliente/APK Android propio de NOVORA.
- Provisioning automático del componente Android.
- Canales CONTROL y DATA.
- Transporte PC ↔ Android.
- Motor de reverse tethering.
- Gestión de tráfico TCP.
- Gestión de tráfico UDP.
- DNS.
- Sesiones por dispositivo.
- Health y estado real del túnel.
- Recovery selectivo.
- Métricas de tráfico y conectividad.
- Optimización de colas y prioridades.
- Reducción del polling y de llamadas ADB redundantes.

### Arquitectura objetivo

```text
LinkEngine/
├── Core/
├── Device/
├── Transport/
├── Network/
├── Tunnel/
├── Recovery/
├── Metrics/
├── Performance/
└── Protocol/
```

El objetivo es que NOVORA conozca el estado real de la conexión en lugar de limitarse a observar si un proceso externo sigue abierto.

---

## VisionEngine

VisionEngine es el motor de visualización, audio y control de Android desde Windows.

### Base actual

VisionEngine ya se organiza en subsistemas independientes para video, audio, control, renderer, transporte, exchange, recovery, métricas y rendimiento.

### En desarrollo

- Pipeline de video administrado por NOVORA.
- Decoder de video.
- Renderer de baja latencia.
- Colas pequeñas y descarte de frames antiguos cuando sea necesario para priorizar el frame más reciente.
- Audio integrado.
- Mouse.
- Teclado.
- Touch.
- Scroll.
- Gamepad/UHID.
- Clipboard.
- Transferencia de archivos e imágenes.
- Ventana dedicada de VisionEngine.
- Modo Ventana.
- Modo Pantalla completa.
- Selección del monitor de salida.
- Selección de salida de audio.
- Recovery independiente de LinkEngine.
- Métricas del stream.
- Adaptación futura de bitrate, FPS, resolución y codec según capacidades y estado de la sesión.

### Arquitectura objetivo

```text
VisionEngine/
├── Core/
├── Device/
├── Server/
├── Transport/
├── Video/
├── Audio/
├── Control/
├── Exchange/
├── Gamepad/
├── Renderer/
├── Recovery/
├── Metrics/
├── Performance/
└── Protocol/
```

---

## Modo de presentación y consumo de recursos

En pantalla completa, NOVORA priorizará la sesión activa.

Se mantendrán:

- video;
- audio;
- control;
- transporte;
- heartbeat esencial;
- recovery;
- LinkEngine si está activo.

Las consultas informativas de la interfaz que no sean necesarias en ese momento podrán suspenderse hasta regresar a la ventana principal.

El objetivo es evitar gastar CPU, ADB y tráfico actualizando indicadores que el usuario no está viendo.

---

## Android Component

NOVORA-LINK 1.4 desarrolla un componente Android propio para trabajar directamente con los motores de NOVORA.

Objetivos:

- instalación/provisioning desde NOVORA;
- autorización VPN cuando LinkEngine la necesite;
- comunicación con el runtime de LinkEngine;
- canales de control y datos;
- evolución hacia una integración Android ↔ Windows más directa.

---

## Nomenclatura interna

Los nuevos componentes de LinkEngine y VisionEngine siguen la convención:

```text
<Función><Carpeta><Motor>

LE = LinkEngine
VE = VisionEngine
```

Ejemplos:

```text
StatusCoreLE.cs
MonitorRecoveryLE.cs
StatusCoreVE.cs
MonitorRecoveryVE.cs
```

---

## Privacidad y seguridad

Las funciones futuras de integración deberán mantener el principio de que NOVORA no necesita recopilar información personal para operar.

El diseño de VisionEngine contempla una capa de privacidad para poder limitar video, control, clipboard o intercambio cuando el contexto lo requiera y respetar las protecciones de contenido de Android.

---

## Dependencias externas

NOVORA-LINK 1.3 permanece como la versión estable existente y conserva sus dependencias de acuerdo con sus respectivas licencias.

La dirección técnica de 1.4 es trasladar cada vez más responsabilidades a **LinkEngine, VisionEngine y el componente Android propio**, reduciendo la dependencia de herramientas externas en las funciones principales.

Cualquier componente de terceros que permanezca durante el desarrollo seguirá identificado y sujeto a su licencia correspondiente.

---

## Disponibilidad

**NOVORA-LINK 1.4 todavía no tiene una fecha pública de lanzamiento.**

Se publicará cuando LinkEngine, VisionEngine y las funciones principales alcancen el nivel de estabilidad requerido para sustituir la base experimental.

### NOVORA-LINK 1.4 será GRATIS

NOVORA-LINK nació como un proyecto gratuito y la versión 1.4 continuará con esa misma decisión.

No se anunciará una función de pago para convertir las novedades principales de 1.4 en una actualización bloqueada.

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**

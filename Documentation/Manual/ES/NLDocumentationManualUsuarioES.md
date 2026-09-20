# Manual de Usuario — NOVORA-LINK 1.4 PRERELEASE PRE FINAL

## Español — idioma principal

### 1. Qué es NOVORA-LINK
NOVORA-LINK conecta una computadora Windows con un dispositivo Android. La interfaz de PC usa un panel dinámico. Android usa la app `com.novora.appcontrol`.

### 2. Conexión
**USB:** conecta el teléfono con depuración USB autorizada. NOVORA detecta el dispositivo y prepara el enlace sin requerir sondeo constante.  
**LAN:** usa la búsqueda de PC o la invitación/QR cuando estén disponibles. Verifica siempre que estás conectando la PC correcta.

NOVORA está diseñada para **un dispositivo Android activo por computadora**.

### 3. Motores
- **LinkEngine:** comparte Internet desde la PC hacia Android mediante el túnel de NOVORA.
- **VisionEngine:** pantalla, audio, control, gamepad, intercambio e integración Android↔Windows.
- **STEngine:** medición técnica de NOVORA. Puede estar disponible sin obligar a detener otros motores.

Cada motor tiene su propio ámbito de recuperación. Si un motor falla, NOVORA intenta evitar detener los demás.

### 4. Pantalla
Desde **Pantalla** puedes preparar el perfil de video, resolución, FPS, bitrate, monitor y audio. Inicia VisionEngine sólo cuando quieras compartir/controlar la pantalla.

### 5. Red
Desde **Red** controla LinkEngine. La congestión o backpressure no deben confundirse con Recovery.

### 6. Game Input
Los mandos compatibles pasan por la ruta SDL/UHID de VisionEngine cuando el backend está disponible.

### 7. Integración
NOVORA incluye infraestructura para portapapeles, archivos, Drag & Drop, compartir, notificaciones y otras capacidades Android↔Windows. Algunas funciones dependen de que el backend correspondiente esté disponible.

### 8. Privacidad
NOVORA intenta minimizar datos personales. No debe guardar innecesariamente contraseñas, cuentas, correos, contactos, OTP, tokens, cookies, historial del portapapeles ni contenido privado.

**Privacy Shield** puede bloquear exposición de video, audio nuevo, control, gamepad, portapapeles y transferencias en contextos sensibles.

### 9. App Android
En Android:
- **Conectar:** prepara USB/LAN.
- **Control:** inicia/detiene motores y usa herramientas.
- **Ajustes:** modifica opciones confirmadas por la PC.
- **Archivos NOVORA:** acceso a funciones de archivos disponibles.
- **Manual:** disponible en español e inglés.

### 10. Actualizaciones
NOVORA puede comprobar el canal oficial al abrir. No debe mantener un sondeo periódico sólo para buscar una actualización.

### 11. Si algo falla
1. Revisa que el teléfono siga conectado/autorizado.
2. Revisa el estado del motor afectado.
3. Detén/inicia sólo ese motor cuando sea posible.
4. No desactives antivirus/seguridad de Windows como solución general.
5. Reporta el primer error concreto y la versión completa de NOVORA.

### 12. Estado de esta prerelease
Esta carpeta es **PRERELEASE PRE FINAL**. Antes de una publicación estable deben completarse las pruebas físicas, seguridad, rendimiento, calidad y funcionamiento exigidas por el reglamento del proyecto.

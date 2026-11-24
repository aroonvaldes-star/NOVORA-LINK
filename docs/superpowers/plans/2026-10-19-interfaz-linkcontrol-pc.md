# Integración de identidad LINK Control en PC

> Ejecución en esta sesión con superpowers:executing-plans. El usuario aprobó integrar los renders Light/Dark y sus correcciones.

**Objetivo:** reorganizar la ventana WPF existente conservando conexiones, motores, calibración y permisos del sistema.
**Diseño:** Asset/Diseno/ASSETS-SELECCIONADOS.md y renders Light r3 / Dark. Montserrat; inicio siempre Light; Dark seleccionable; marca original contrastada; Inicio sin resumen; descarga Android y actualizaciones separadas en Configuración.

## Tareas
- [x] Respaldar los archivos afectados y separar estilos/presentación de la ventana.
- [x] Aplicar paletas, fuente empaquetada, marcas originales y recursos de iconos.
- [x] Redistribuir Inicio, VisionEngine, LinkEngine, ExIn e Integración; mover descarga a Configuración y habilitar comprobaciones manuales independientes.
- [x] Retirar pestaña y escudo manual PrivacyVE, incluida persistencia y activación desde PC. Conservar las restricciones de contenido seguro nativas y la protección de calibración.
- [ ] Compilar Release sin interrumpir el ejecutable del usuario; verificar temas, navegación, descarga y suites afectadas; registrar resultados y límites.

### Avance 2026-10-19

- Copias previas de los archivos afectados en `.superpowers/sdd/2026-10-19-interfaz-linkcontrol-pc/backups/`.
- Integradas las comprobaciones manuales independientes de NOVORA-LINK y LINK Control, la navegación desde Inicio y la paleta Light/Dark Platinum Air/carbón.
- Retiradas las referencias restantes de persistencia y la llamada de control manual PrivacyVE desde PC; se conservan las restricciones nativas de contenido protegido.
- El icono Win32 válido `Asset/NOVORA_Win32.ico` reemplaza como icono de aplicación al archivo `NLAssetNOVORA11.ico`, cuyo contenido actual era PNG y hacía fallar CSC (CS7065).
- Build Release aislado correcto: 0 advertencias y 0 errores. `NOVORA.AndroidDownload.Tests` pasó con QR PNG real y rechazo de destinos inseguros. Pendiente revisión visual en ejecución y navegación/temas manuales.

No se genera APK ni se publica una release: el alcance actual es PC. No se alteran perfiles, bitrate, FPS, transportes ni datos de emparejamiento. No se incorporan mediciones simuladas del render al producto. Los comandos de cada motor mantienen su autoridad y disponibilidad reales.

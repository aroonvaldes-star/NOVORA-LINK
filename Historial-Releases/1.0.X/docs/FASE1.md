# NOVORA — Fase 1

## Estado
Los controles inferiores del Launcher dejaron de ser placeholders.

### Configuración
Abre una ventana WPF y permite:
- activar/desactivar audio;
- activar/desactivar el inicio automático de Gnirehtet.
Los valores se guardan en `%LOCALAPPDATA%\NOVORA\settings.json`.

### Iniciar NOVORA
Con un dispositivo ADB autorizado:
1. comprueba la configuración;
2. inicia Gnirehtet si está habilitado;
3. inicia scrcpy con el monitor seleccionado y el modo de audio seleccionado.

### Configuraciones rápidas
**Optimizar sistema**
- inicia/actualiza el servidor ADB;
- vuelve a detectar el dispositivo y sus capacidades.

**Limpiar caché**
- solicita `cmd package trim-caches 512M`;
- no ejecuta `pm clear` ni borra datos de aplicaciones.

### Otras acciones
**Respaldar dispositivo**
- crea un informe local en `Documentos\NOVORA\Backups`;
- incluye modelo, Android, serial y paquetes instalados;
- no pretende ser un backup completo de datos personales.

**Actualizar Novora**
- ejecuta `NOVORA.Update.exe` si está presente junto al Launcher;
- si no existe, informa que el actualizador todavía no está integrado.

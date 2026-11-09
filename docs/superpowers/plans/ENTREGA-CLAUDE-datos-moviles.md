# Entrega para Claude — conexión dinámica y ahorro móvil

El usuario solicita que Claude implemente el plan complementario:

**`2026-11-08-datos-moviles-ahorro-claude.md`**, en esta misma carpeta.

Léelo junto con tu plan/diseño de conexión dinámica vigentes y `AGENTS.md`/`CLAUDE.md` antes de editar. Codex no ha modificado código Android/Linux ni tus documentos de conexión dinámica.

Prioridades: ruta remota autenticada PC↔LINK Control, perfil móvil Extremo 120 kbps/5 fps/480 px como propuesta inicial, Solo control, audio apagado, medición bidireccional, presupuesto que sobreviva reconexiones y compatibilidad USB/LAN/Windows. Los mínimos actuales de bitrate/FPS y el techo del encoder requieren negociación, no solo un selector UI.

La primera prueba recomienda una VPN de superposición existente, sin sustituir TLS NOVORA. No hay infraestructura pública/VPN de ensayo identificada aún: registrar ese requisito y no declarar WAN funcionando hasta probarla. VPN no conserva tráfico si el operador corta Internet. Evitar conflicto con VpnService de LinkEngine y no exponer ADB.

Conservar tu trabajo local; adaptar nombres propuestos al coordinador real sin duplicarlo. Aplicar N+1 únicamente a la futura APK de entrega, verificando artefactos vigentes y firma. Sin commit, push, instalación ni publicación automática.

Estado de entrega: archivos disponibles en el checkout compartido; no hay confirmación de recepción/lectura por tu sesión. El usuario puede indicarte que leas este archivo para reanudar.

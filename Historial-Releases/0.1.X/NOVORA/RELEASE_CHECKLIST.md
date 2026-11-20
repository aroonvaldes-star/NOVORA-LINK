# Checklist de lanzamiento V1 — viernes 14

No publiques hasta que todos los puntos estén marcados con evidencia (fecha, modelo, versión Android y resultado).

## Bloqueantes

- [ ] CI de CircleCI verde en el commit de lanzamiento.
- [ ] `tests\Smoke.Tests.ps1` verde en Windows limpio.
- [ ] Sin antivirus o SmartScreen bloqueando los binarios incluidos.
- [ ] Se verificó el origen e integridad de `adb`, `scrcpy` y `gnirehtet`; no se sustituyeron binarios sin revisión.

## Teléfono de baja gama

- [ ] Android 10+ y 2 GB de RAM: `NOVORA.bat mirror -Profile low` durante 15 minutos.
- [ ] Sin cierre de scrcpy, sin calentamiento excesivo y controles utilizables durante la prueba.
- [ ] Desconectar y reconectar USB; `NOVORA.bat devices` vuelve a mostrar el teléfono.
- [ ] Con la sesión anterior detenida, `NOVORA.bat tether` muestra la notificación VPN de gnirehtet y navega durante 10 minutos.
- [ ] `NOVORA.bat stop` cierra sólo la sesión de NOVORA y elimina la VPN local del teléfono.

## Regresión

- [ ] Probar un segundo modelo Android y, si se soporta, conexión inalámbrica.
- [ ] Confirmar que varios dispositivos obligan a indicar `-Serial`.
- [ ] Guardar logs de `%~dp0logs\novora-v1.log` y capturas de cada fallo antes de cambiar el perfil.

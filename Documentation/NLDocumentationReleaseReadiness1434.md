# NOVORA-LINK 1.4 - Estado de candidata 1.4.34

Fecha: 23 de septiembre de 2026.

## Resultado actual

La candidata queda preparada como Release Candidate, no como Release estable. El gate automatizado, el empaquetado Android, la instalacion fisica del APK y el ciclo real del instalador Windows han pasado. La promocion a estable permanece bloqueada por pruebas fisicas que requieren el telefono desbloqueado y observacion directa.

## Evidencia cerrada

- Escritorio Release: 0 advertencias, 0 errores.
- Suite .NET: 405/405.
- Android Release: paquete `com.novora.appcontrol` 1.4.34, code 34, minSdk 26, targetSdk 36.
- APK canonico e instalado: SHA-256 `8F2E6E81FD70B692FD0E370A75A4BC482D598B468D70A0B4A12A61236A4BE405`.
- Samsung SM-A566E `R5CY3118MEW`: instalacion incremental y arranque de MainActivity aprobados.
- RelayCore: 34/34, un benchmark ignorado; binario Release reconstruido e integrado con SHA-256 `7ECF309236DFEEE03882CD810F1E36A5A9AFC8303547ABF2CE52583EF58FF051`.
- Verificador del repositorio: 860 archivos aprobados.
- Instalador RC: compilacion, instalacion aislada, arranque y desinstalacion aprobados. SHA-256 `BA4B523C098FDBFFD68D5A8DA33D554E3A9236F4A787D278FAEE1C47ABF75AF3`.

## Gates abiertos

- Inspeccion visual Android con el telefono desbloqueado; la captura actual es negra por bloqueo seguro y foco de NotificationShade.
- USB y LAN/QR/trust/reconexion sobre el APK 1.4.34 exacto.
- VisionEngine inicio, parada, video, audio, control y pantalla completa sobre la candidata actual.
- ExIn con movimiento completo de sticks, triggers y botones, incluida convivencia con VisionEngine a pantalla completa.
- Archivos, captura, grabacion y reconexion sobre la candidata actual.
- Medicion comparable de latencia, jitter, perdida y colas; comparacion NVIDIA frente a software cuando exista hardware aplicable.
- Revision de seguridad y privacidad de runtime sobre la candidata final.
- Promocion de etiquetas PRERELEASE a 1.4 estable solamente despues de cerrar los gates anteriores.

## Limites conocidos no bloqueantes de compilacion

- RelayCore conserva la dependencia futura-incompatible `net2 0.2.39` a traves de `mio 0.6`; requiere migracion separada con paridad fisica.
- VisionEngine conserva `scrcpy-server 4.1` como backend activo; no puede retirarse hasta cumplir sus gates de sustitucion.

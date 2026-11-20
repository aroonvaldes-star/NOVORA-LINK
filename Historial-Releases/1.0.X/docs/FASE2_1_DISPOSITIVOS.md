# NOVORA Fase 2.1 — Selección de dispositivos

NOVORA separa la detección de monitores de Windows de los dispositivos Android expuestos por ADB.

## Comportamiento

- Se enumeran todos los dispositivos de `adb devices`.
- Se muestran estados `device`, `unauthorized` y `offline`.
- El usuario selecciona el dispositivo Android que quiere utilizar.
- La selección se identifica por serial.
- No se cambia silenciosamente a otro dispositivo si el seleccionado se desconecta.
- ADB/Scrcpy/Gnirehtet deben usar el serial seleccionado.

## Objetivo

Permitir varios Samsung, tablets u otros dispositivos conectados simultáneamente sin que NOVORA elija uno arbitrariamente.

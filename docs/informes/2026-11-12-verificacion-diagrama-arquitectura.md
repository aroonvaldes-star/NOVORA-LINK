# Verificación del diagrama «NOVORA-LINK 1.4 · arquitectura»

Fuente comprobada: rama `main` de `NOVORA-LINK/Dev_Local` (commit `f148947`).
Diagrama: artefacto de claude.ai, no versionado en el repositorio.

## Coincide con el código

| Elemento del diagrama | Evidencia en el código |
|---|---|
| LINK Control UI, Control service (USB o código LAN) | `src/ANDROID/AndroidService/NLAndroidServiceControl.cs` (`USB_BOOTSTRAP`, invitación LAN) |
| Captura y encoder (MediaProjection) | `src/ANDROID/AndroidVideo/NLAndroidVideoService.cs`, `NLAndroidVideoEncoder.cs` |
| Accessibility input (puntero, teclas, portapapeles) | `src/ANDROID/AndroidVideo/NLAndroidControlAccessibilityService.cs` |
| VisionEngine decodifica y presenta el stream | `src/WINDOWS/VisionEngine/Video/VEVideoDecoder.cs` |
| ExInEngine (SDL in, HID out) hacia el teléfono | `src/WINDOWS/ExInEngine/ExInManager.cs`: «Windows gamepad → SDL3 → UHID → Android» |
| LinkEngine gestiona relay y red | `src/WINDOWS/LinkEngine/` (`NovoraRelay`) |
| Service layer: ajustes, ADB, actualizaciones | `src/WINDOWS/Service/NLServiceADB.cs` |
| Confianza: PCs fiables guardados en el teléfono | `src/ANDROID/AndroidStorage/NLAndroidStorageTrustedPcs.cs`; en el PC, `src/WINDOWS/Control/NLControlTrustStore.cs` |
| PrivacyVE en el exchange | `src/WINDOWS/VisionEngine/Exchange/` |
| Dirección de las flechas de control, video y entrada | Consistente con el código |

No se encontró uso de scrcpy en el código, salvo una mención en `NovoraRelay/README.md`.

## Errores o huecos

1. **El túnel VPN no va solo por Internet.** El diagrama dibuja `VPN service → tunel VPN → Internet → relay`. En `src/ANDROID/AndroidVpn/NLAndroidVpnService.cs` el túnel usa el transporte USB o LAN (`_activeTransport` vale `"USB"` o `"LAN"`). La ruta por Internet existe, pero es un caso aparte.
2. **Falta el extremo Android del mando.** El diagrama termina en «HID de mando» sobre el transporte, pero no muestra quién lo aplica en el teléfono. En el código es un UHID creado desde el servidor Android heredado. `AGENTS.md` indica que el servicio de accesibilidad no implementa UHID.
3. **El portapapeles es de doble sentido.** El diagrama solo muestra «portapapeles» hacia el teléfono. El código tiene lectura de portapapeles en Android (`ReadClipboardOnMainAsync`) y `VEExchangeClipboard` en el PC.

## Orden del proceso

El diagrama muestra direcciones de datos, no una secuencia temporal. No indica el orden de arranque real (transporte por ADB o invitación LAN, confianza, sesión de control y luego video, entrada y mando).
No se comprobó ese orden de arranque en el código, así que no se puede confirmar ni contradecir.

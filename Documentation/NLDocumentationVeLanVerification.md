# Verificación de transporte nativo VE LAN

Fecha: 2026-10-04

Dispositivo: Samsung SM-A566E, Android 16

Paquete: `com.novora.linkclient`

Rama: `NOVORA-LINK`

## Evidencia automatizada

- Protocolo, TLS fijado, tokens de un solo uso, ciclo de sesión, cliente, coordinador y estados LAN: 96/96 pruebas enfocadas superadas.
- Estados de ciclo LAN, incluida la selección bloqueada durante `AwaitingPermission`: 14/14 superadas.
- Regresión combinada con USB: 121/123 superadas mientras NOVORA estaba abierto. Las dos restantes intentan reservar el puerto fijo 27214 ya ocupado por esa instancia; no son fallos funcionales.
- NOVORA PC Release compiló en una salida aislada: 0 errores, 0 advertencias.
- NOVORA-LINK Debug compiló: 0 errores, 0 advertencias.

## Evidencia del dispositivo

- APK firmado final instalado correctamente mediante el dispositivo ADB Wi-Fi `10.0.0.28:5555`, sin cable USB conectado.
- La actividad principal resolvió como `com.novora.linkclient/...HomeActivity` y arrancó sin crash.
- El manifiesto empaquetado contiene `com.novora.linkclient.VeLanCaptureService` privado con tipo foreground `mediaProjection`.
- El manifiesto empaquetado contiene `com.novora.linkclient.VeAccessibilityService`, protegido por `BIND_ACCESSIBILITY_SERVICE`.
- No se registran secretos, tokens ni huellas de certificado en esta evidencia.

## Verificación interactiva pendiente

La instancia NOVORA abierta para uso del usuario fue iniciada antes de compilar estos cambios y mantiene el puerto 27214. Para no interrumpirla, todavía falta reiniciar NOVORA con el binario nuevo y comprobar de forma interactiva:

1. emparejamiento por código de seis dígitos sin USB;
2. aprobación de MediaProjection y cuadro en movimiento con estado `Streaming`;
3. audio o degradación visible sin audio;
4. control por el servicio de accesibilidad propio de NOVORA-LINK;
5. corte/reconexión Wi-Fi sin fallback a USB;
6. ruta USB manual después de volver a conectar el cable.

Esta sección debe actualizarse con el resultado observado después del reinicio; no se declara aprobada anticipadamente.

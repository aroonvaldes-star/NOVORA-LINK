# Validación de VisionEngine Exchange por USB y Wi-Fi (2026-10-16)

Plan: `docs/superpowers/plans/2026-10-16-ve-exchange-usb-wifi.md`, tarea 6.
Spec: `docs/superpowers/specs/2026-10-16-ve-exchange-usb-wifi-design.md`.

## Alcance de esta validación

Este informe separa tres niveles de evidencia:

- **Compilado y probado en el PC:** harnesses y builds Release ejecutados en esta sesión.
- **Probado con teléfono:** no se hizo. El teléfono no estaba conectado.
- **Instalado y probado físicamente:** no se hizo.

## Pruebas locales (sin teléfono)

| Harness / build | Comando | Resultado |
|---|---|---|
| Exchange (contrato, USB, LAN, sesión, capacidades, APK) | `dotnet run --project tests/NOVORA.VisionEngine.Exchange.Tests -c Release` | 24 PASS / 0 FAIL |
| FILES v2 (protocolo) | `dotnet run --project tests/NOVORA.FileProtocol.Tests -c Release` | ALL FILES v2 CHECKS PASSED |
| LinkEngine | `dotnet run --project tests/NOVORA.LinkEngine.Tests -c Release` | Sin fallos (salida PASS) |
| Startup VisionEngine | `dotnet run --project tests/NOVORA.VisionEngine.Startup.Tests -c Release` | PASS en ruta ADB, LAN nativa y consentimiento |
| Build Windows | `dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release` | 0 advertencias, 0 errores |
| Build Android | `dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release` | 0 advertencias, 0 errores |

## Comprobación previa del teléfono

Según la bitácora, antes de cualquier prueba física hay que verificar ADB por Wi-Fi.

- `adb devices -l` no devolvió ningún dispositivo (ni por USB ni por Wi-Fi).
- `adb shell settings get global adb_wifi_enabled` no pudo ejecutarse: no hay dispositivo.

Sin conexión no se puede diagnosticar USB ni Wi-Fi. Hay que activar la depuración inalámbrica en el teléfono y emparejarlo, o conectar el cable.

## Matriz física pendiente

Ninguno de estos casos se ejecutó. Quedan para cuando haya teléfono.

**USB (solo USB):**
- [ ] Portapapeles en ambas direcciones.
- [ ] Archivos y carpetas PC → Android y Android → PC.
- [ ] Imágenes, medios y APK transferido.
- [ ] Instalación de APK con confirmación del usuario en Android.

**Wi-Fi/LAN (solo LAN):**
- [ ] Sesión AppControl LAN autorizada, sin cable.
- [ ] Verificar ADB por Wi-Fi como preparación y luego desconectar la ruta ADB en el PC; el Exchange de LAN debe seguir funcionando sin serial ADB.
- [ ] Envío PC → Android y recepción Android → PC confirmada por tamaño y SHA-256.

**Cierres y cancelación (por cada ruta):**
- [ ] Cancelar un envío deja cero parciales.
- [ ] Reintentar en una sesión nueva.
- [ ] Rechazo o denegación de instalación no se muestra como éxito.

## Limitaciones conocidas en el código

- **Instalación de APK:** el adaptador USB y el LAN devuelven `Failed` sin tocar el teléfono. Falta el lado Android (`PackageInstaller`) y el canal de solicitud y respuesta.
- **Portapapeles de la UI:** los atajos Ctrl+C/Ctrl+V y `VisionClipboard_ChangedVE` no pasan todavía por `RequireControlVE`.
- **Gates de arrastre:** `IntegrationFiles_Drop` exige `FileTransfer` y `DragDrop` a la vez.
- **Cableado de la UI:** las llamadas de enlace de la tarea 4 en `NLUIWindowMainAndroidVideoSource.cs`, `NLUIWindowMainAndroidVideoLan.cs` y `NLUIWindowMainVisionEngine.cs` no están comiteadas.
- **LAN sin FILES v2:** sigue el camino antiguo que pide abrir Archivos en Android.
- **Progreso USB:** el envío USB por el adaptador no emite el progreso de `VEExchangeTransfer`.

## Estado

Las pruebas y builds locales pasan. La validación física por USB y Wi-Fi queda pendiente; no se afirma éxito en teléfono.

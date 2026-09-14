# Base local, respaldo y APK Android

Actualizado: 2026-09-13.

## Fuente principal

La carpeta C:\Users\Aroon\Desktop\NOVORA-LINK es la base autoritativa. GitHub aroonvaldes-star/NOVORA-LINK, rama NOVORA-LINK, sirve como respaldo. No sustituir motores locales por una rama remota anterior. Comparar documentación antes de incorporarla; no confundir planes históricos con funciones implementadas.

En esta revisión git fetch origin confirmó que la rama remota y el commit local inicial coincidían en 39bf82c. La rama NOVORA-LINK-1.4 conservaba un estado anterior, 2063a4e. Se recuperaron nueve documentos Markdown ausentes desde origin/NOVORA-LINK, sin reemplazar fuentes locales.

El respaldo incluye motores, fuentes Android/Windows, pruebas, scripts, documentación, recursos y herramientas de ejecución en src/NOVORA/Tools. Se excluyen bin, obj, cachés .vs, target de Rust, artifacts de compilación y almacenes privados de firma. La disponibilidad de fuentes y binarios no garantiza reconstrucción reproducible ni compatibilidad universal.

## APK 1.4.A4

Identidad inspeccionada con aapt: com.novora.linkengine; versionName 1.4.A4; versionCode 3; SDK objetivo 36. Proyecto Android: NOVORA.linkEngine.Android/NOVORA.linkEngine.Android.csproj.

APK canónica: src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.apk.
SHA-256: 1FA98BA5D18085174BB9C3219A3DC2579EFD5581AB239513727C88085318F72E.

Coincide exactamente con el archivo Android Release terminado en -Signed.apk y con la copia de Windows Release. Directory.Build.targets copia el resultado firmado a la ruta canónica. La instalación/provisión debe partir de esa ruta de distribución.

Se encontraron otras compilaciones con la misma etiqueta 1.4.A4 y código 3:
- Windows Debug y artifacts/discovery-check: SHA-256 99B0B89DAE204A42035E214A646E0840F2F5AEFF6E23B209EF955623AD4E7815.
- artifacts/verify-lan-discovery: SHA-256 3E88FE9D8CE670F19F042B5AB170A07C8E715062E560672B2EAF31447A0FDF7B.
- Copias de pruebas Release: SHA-256 0F7A4B7A061EE4F9F26C9F63298C7E4B3AC950C6754E0F0F706B2DF4D0E6C1B3.

La etiqueta no identifica de forma única el contenido. En futuras distribuciones modificadas se debe incrementar versionCode y registrar el hash publicado. Esta tarea no cambia la versión ni reinstala el teléfono.

ADB confirmó en R5CY3118MEW versionName 1.4.A4, versionCode 3 y lastUpdateTime 2026-09-12 23:54:02. No se comparó el hash instalado con el archivo local ni se realizó prueba funcional PC-Android.

La configuración Release actual utiliza el almacén de firma de desarrollo del usuario. La verificación con apksigner no pudo ejecutarse porque Java no estaba en PATH/JAVA_HOME. No se afirma aquí validación criptográfica de firma ni preparación para tienda.

## Alcance de esta revisión

Cambios de documentación y respaldo; sin cambios en lógica de motores, polling o versiones. Se revisan nombres, exclusiones, identidad APK y diferencias Git. No se realizó compilación nueva ni prueba funcional; las APK inspeccionadas son artefactos existentes.

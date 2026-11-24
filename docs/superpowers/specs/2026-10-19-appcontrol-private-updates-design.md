# AppControl: actualizaciones desde releases privadas

Estado: propuesta concreta pendiente de revisión del diseño; implementación no iniciada.

## Solicitud y resultado esperado

El usuario solicita retirar el apartado de descarga/instalación de AppControl del launcher Windows y trasladar la detección, descarga e instalación a AppControl Android. Usar el repositorio privado `aroonvaldes-star/NOVORA-AppControl`, conservar cada versión desde 1.4.110 como release independiente y preservar los datos guardados al actualizar.

Repositorio verificado privado el 2026-10-19. APK original localizada: 1.4.111/code111, ARM64, minSdk30, targetSdk36. APK original 1.4.110 no localizada. No reconstruir un histórico con fuentes posteriores. La próxima APK nueva debe aplicar N+1 y sincronizar los artefactos de entrega.

## Diseño propuesto

1. Android muestra una sección Actualizaciones con versión instalada, estado de consulta/descarga e instalación pendiente. Comprueba al abrir, con caducidad de la consulta y botón manual; no agrega sondeo frecuente ni bloquea el inicio o los motores.
2. El canal es privado: una GitHub App con device flow, acceso de lectura Contents solo para este repositorio y autorización individual del usuario. El clientId es público; no distribuir clientSecret, claves privadas ni PAT compartido dentro de la APK. Registrar/instalar esa GitHub App requiere una acción externa de configuración y autorización; no usar la credencial del conector de Codex en el producto.
3. La app consulta releases publicadas no borrador/no prerelease y selecciona por versionCode compatible. Cada release incluye una sola APK de la arquitectura soportada y descriptor con packageId, versionCode, versionName, minSdk y SHA-256. Rechaza versiones iguales/inferiores, metadatos ambiguos, tamaño excesivo y URLs de descarga fuera de la API oficial esperada. El token se envía solo a GitHub API y nunca se reenvía indiscriminadamente a redirects.
4. Descarga a caché privada con límite de espacio/tamaño, cancelación y un único archivo temporal. Valida SHA-256, manifiesto real, arquitectura y certificado contra la app instalada antes de ofrecer la instalación. No interpreta un hash como sustituto de comprobar el firmante. Un cambio de firma bloquea la actualización y conserva el paquete instalado.
5. Usa PackageInstaller para reemplazar el mismo paquete. Nunca desinstala ni limpia datos, preferencias, PCs de confianza o autorizaciones. No solicita eliminación para resolver una firma incompatible. Si Android requiere permiso de instalación de esta fuente o confirmación, abre su interfaz y espera la acción del usuario. No promete instalación silenciosa en un teléfono normal.
6. Puede descargar automáticamente la actualización compatible con una preferencia explícita. Si hay motores activos, aplaza la instalación y presenta el estado; una actualización puede cerrar procesos y requerir reactivar captura/VPN. Al siguiente inicio verifica la versión instalada y limpia únicamente temporales propios. Cancelación o instalación fallida no equivale a éxito y no inicia downgrade automático.
7. Retira el apartado y sus comprobaciones automáticas de instalación Android de la UI Windows al disponer del reemplazo Android verificado. La actualización de Windows permanece independiente. No borra inicialmente el instalador ADB interno ni APKs históricas; son una vía de recuperación hasta validar el nuevo flujo.

## Archivos y límites

- Windows: `UI/NLUIWindowMain.xaml`, ciclo de inicialización/cierre del apartado y `NLUIWindowMainAndroidInstall.cs`; retirar referencias de controles de forma coherente.
- Android: nueva unidad de consulta/descarga/validación; adaptador de identidad GitHub; integración con `AndroidUI/NLAndroidUIActivity.cs`; receptor persistente de resultado de PackageInstaller y permiso REQUEST_INSTALL_PACKAGES en manifiesto.
- Código de selección de releases y validación de metadatos separado de Android para pruebas deterministas.
- Descriptores de entrega sincronizados con la APK real; una APK nueva usa versionCode superior a 111. Publicar APK no implica que una versión previa sin actualizador pueda actualizarse sola: requiere instalación inicial del actualizador por un medio existente.
- No cambiar VE/LE/ExIn, datos existentes, firma o canales de captura como parte de esta función.

## Verificación requerida

- Pruebas de versiones, duplicados, descriptor inválido, respuestas 401/403/404/rate-limit, redirects, pérdida de red, hash erróneo, firma distinta, versión inferior, descarga cancelada y espacio insuficiente.
- Pruebas del ciclo de instalación: permiso rechazado, STATUS_PENDING_USER_ACTION, cancelación, proceso terminado, resultado desconocido y verificación tras reinicio.
- Compilación Windows y Android arm64 sin empaquetar durante desarrollo; APK de entrega N+1 con firma verificada y descriptor/hash sincronizados.
- Prueba física de actualización sobre una instalación con PCs recordadas y preferencias conocidas; comparar antes/después y confirmar versión, transporte y datos. Un build o loopback no demuestra conservación física.

## Dependencias externas pendientes

- Registrar GitHub App de lectura y autorizar únicamente NOVORA-AppControl; usuario y app necesitan acceso al repositorio privado.
- Habilitar carga de archivos en la extensión Edge para terminar de publicar la APK ya preparada mediante el navegador; CLI local sin sesión GitHub.
- Localizar la APK 1.4.110 si se conserva en otro lugar.

## Fuentes técnicas verificadas

- https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app
- https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/differences-between-github-apps-and-oauth-apps
- https://developer.android.com/reference/android/content/pm/PackageInstaller

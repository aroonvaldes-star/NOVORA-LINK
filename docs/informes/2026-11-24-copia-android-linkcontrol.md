# Copia del cliente Android de LINK Control — 2026-11-24

## Alcance

Se creó una copia local y autocontenida del módulo Android de LINK Control a
partir del repositorio público
<https://github.com/aroonvaldes-star/NOVORA-LINK>, rama `main`, commit
`5c2a2ecea670e4b15880be3d91ffc34a7c6d27ec`. No se realizó ingeniería inversa
de un APK ni se modificó la lógica de la aplicación.

## Trabajo realizado

1. Se copió `src/ANDROID` a `src/Android` dentro de
   `/home/aroon61/LINKCONTROL-Android`.
2. Se copiaron a `src/Shared` los archivos que `NLProjectAndroid.csproj`
   enlazaba desde `src/WINDOWS`: control, red, protocolos de VisionEngine y
   assets de marca. Se reescribieron las rutas relativas del `.csproj` para
   que el proyecto sea independiente del árbol completo.
3. Se añadieron `LINKCONTROL-Android.sln`, `.gitignore` y `README.md`.
4. Se copiaron las bitácoras y documentación técnica del repositorio de origen
   a `docs/` (incluye `docs/informes/` y `docs/superpowers/`), además de los
   avisos legales en `docs/legal/`.
5. Se descargó la APK pública `NOVORA-Android-1.4.8-experimental.apk` desde el
   pre-release `v1.4.0-experimental.1` y se guardó en `apk/`. El SHA-256
   verificado es `e8de4485731bb35f0939bbadfecd442636f1d4076f252ae933d338cc41fe2157`,
   coincidente con `SHA256SUMS.txt` del publicador.

## Estado del proyecto copiado

- Paquete: `com.linkcontrol.novora`.
- Versión declarada en la fuente copiada: `1.4.128` (`ApplicationVersion` 128).
- Versión de la APK pública descargada: `1.4.8` experimental. **No coinciden**:
  la APK es un artefacto publicado anteriormente, no un build de esta copia.
- Framework: `net10.0-android`, API mínima 30, Release `android-arm64`.

## Verificación y límites

- Se comprobó que los 31 `Include` del `.csproj` apuntan a archivos existentes.
- No se compiló ni se generó una APK nueva: este entorno no tiene .NET 10 SDK,
  workload de Android ni Android SDK. Tampoco hay material de firma privado.
- No se hicieron pruebas en dispositivo físico. Las afirmaciones de conexión,
  captura o rendimiento de las bitácoras originales no se revalidaron aquí.
- La licencia del código sigue siendo NLPL v1.1; la redistribución modificada
  requiere autorización escrita del titular.

## Próximo paso para un build propio

Instalar .NET 10 SDK, `dotnet workload install android`, Android SDK 30+ y JDK,
y ejecutar:

```bash
dotnet build src/Android/NLProjectAndroid.csproj -c Release
```

La configuración de firma debe proporcionarse por separado antes de publicar.

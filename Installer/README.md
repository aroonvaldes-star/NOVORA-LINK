# Instalador de NOVORA-LINK 1.4

El instalador toma una publicación ya preparada desde `artifacts/publish/win-x64`.

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet publish .\src\NOVORA\NLProjectDesktop.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64
```

Antes de compilar el instalador, ejecuta `scripts/Prepare-ThirdPartyCompliance.ps1` para incluir las licencias en la publicación y adjuntar las fuentes redistribuibles de FFmpeg. Después compila `Installer/NOVORA.Installer.iss` con Inno Setup 6 o 7. En CI, `AppVersion` y `PublishDir` se pasan como defines para que el tag sea la fuente de verdad de la versión.

Para generar un solo ejecutable con Windows autónomo, APK, herramientas, licencias y fuentes de FFmpeg, usa `scripts/Build-UnifiedInstaller.ps1 -PublishDir <directorio-publicado> -ComplianceDir <directorio-compliance> -Version <versión>`. El script exige `Android/NLAndroidApp.apk` y `Android/NLAndroidRelease.json` concordantes y verifica la fuente LGPL antes de compilar. La fuente se instala en `Tools/Legal/Sources`; el instalador y su SHA-256 quedan en `Installer/output`. El flujo de GitHub publica únicamente estos dos archivos y falla si falta algún componente.

La edición experimental 1.4/1.4.66 corresponde a la [pre-release 1.4.66-pre.1](https://github.com/aroonvaldes-star/NOVORA-LINK/releases/tag/1.4.66-pre.1). El instalador único local es `Installer/output/NOVORA-LINK-1.4.66-UNIFIED-Setup.exe` (SHA-256 `00a4c29566563157a291f2d16806afcbce2d813fc419f522c1f62476f99b37c6`); incluye Windows autónomo y la APK, sin requerir .NET 8 Desktop Runtime x64 por separado. Las instrucciones están en `docs/releases/1.4.66-pre.1.md`. La APK integrada se valida con `Android/NLAndroidRelease.json`; cada nueva compilación de AppControl debe aumentar `ApplicationVersion` N+1 y actualizar APK y descriptor juntos. El tag experimental no debe empezar con `v`: el flujo `release.yml` interpreta los tags `v*.*` como releases normales.

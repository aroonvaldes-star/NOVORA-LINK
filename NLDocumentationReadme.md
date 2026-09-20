# NOVORA-LINK — documentación de la raíz activa

La documentación principal del repositorio se encuentra en `README.md`.

Esta candidata corresponde a **NOVORA-LINK 1.4 PRERELEASE PRE FINAL** y usa:

- PC: `src/NOVORA/NLProjectDesktop.csproj`
- Android: `src/NOVORA.Android/NLProjectAndroid.csproj`
- Tests: `tests/NOVORA.Tests/NLProjectTests.csproj`
- Naming map: `Documentation/NLDocumentationNamingMap.json`
- Release gate: `Tool/NLToolReleaseGate.ps1`
- Installer: `Tool/NLInstallerSetup.iss`
- Informe funcional: `Documentation/Release/NLDocumentationPreFinalFunctionalAnalysis.md`

La interfaz PC vigente es el panel dinámico. Android usa `com.novora.appcontrol`.

No se debe interpretar documentación histórica de bloques Android como estado actual si contradice el código activo o el informe funcional prerelease.

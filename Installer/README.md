# Instalador de NOVORA-LINK 1.3

El instalador toma una publicación ya preparada desde `artifacts/publish/win-x64`.

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet publish .\src\NOVORA\NOVORA.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\publish\win-x64
```

Después compila `Installer/NOVORA.Installer.iss` con Inno Setup 6 o 7. En CI, `AppVersion` y `PublishDir` se pasan como defines para que el tag sea la fuente de verdad de la versión.

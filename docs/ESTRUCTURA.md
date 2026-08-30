# NOVORA-LINK — estructura del repositorio

```text
NOVORA-LINK/
├─ .github/workflows/release.yml
├─ Installer/
├─ docs/
├─ scripts/
├─ src/NOVORA/
├─ .gitignore
├─ NOVORA.sln
└─ README.md
```

`src/NOVORA/Tools/` es la única ruta de ejecución para ADB, scrcpy y Gnirehtet. Los binarios se obtienen con `scripts/Setup-Tools.ps1` y no se guardan en Git.

No versionar: `.vs/`, `bin/`, `obj/`, `Installer/output/`, `artifacts/` ni `*.user`.

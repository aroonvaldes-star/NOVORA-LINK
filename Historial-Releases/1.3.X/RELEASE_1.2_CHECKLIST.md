# NOVORA 1.2 — Release checklist

## Automatic updater

- NOVORA checks the latest non-draft, non-prerelease GitHub release at startup.
- A newer setup asset is accepted only over HTTPS and only when GitHub exposes a SHA-256 digest.
- The downloaded installer is hashed locally before execution.
- A temporary PowerShell helper waits for the current NOVORA process to exit.
- Inno Setup runs silently and starts the installed NOVORA after installation.
- Update failures do not prevent NOVORA from starting normally.

## Scope protection

- `MainWindow.xaml` / `MainWindow.xaml.cs`: verification only; no changes made for this release-prep pass.
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`: verification only; no changes made for this release-prep pass.
- The ComboBox data source issue was fixed in `MainViewModel`: bitrate, FPS and resolution options are now populated by default.

## Release packaging

- Version: `1.2.0`
- Runtime tools: `src/NOVORA/Tools`
- Installer source: `Installer/NOVORA.Installer.iss`
- CI builds `win-x64`, publishes the runtime, builds the Inno Setup installer and emits a SHA-256 file.

## Known bootstrap limitation

The already-published NOVORA 1.1 installer contains the old updater implementation, which only checks for updates. Therefore the existing 1.1 binary cannot magically acquire the new automatic-install behavior before receiving 1.2. The automatic updater is active once the user installs this 1.2 build and will handle later releases without repository access.

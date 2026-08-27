# NOVORA-LINK

Base repository for NOVORA 1.2 and subsequent releases.

- WPF / C# / .NET 8
- Benchmark mode
- Gnirehtet recovery
- Bitrate normalization
- Automatic updates from GitHub Releases

## Base 1.2

NOVORA 1.2 is the baseline version. Its updater checks:

`https://api.github.com/repos/aroonvaldes-star/NOVORA-LINK/releases/latest`

If a newer semantic version is available and the release contains a `NOVORA-*.zip` asset, NOVORA offers the user the update and installs it through `Tools/UpdateLauncher.ps1`.

Future update assets must be **compiled binary ZIPs containing `NOVORA.exe`**, not source-code archives.

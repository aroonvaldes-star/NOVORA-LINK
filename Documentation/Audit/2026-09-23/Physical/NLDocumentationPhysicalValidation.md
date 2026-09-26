# Physical Validation - 2026-09-23

## Device And Package

- Authorized USB device: Samsung SM-A566E, serial `R5CY3118MEW`.
- Package: `com.novora.appcontrol`, version 1.4.28, code 28.
- Installed base APK and canonical `src/NOVORA/Android/NLAndroidApp.apk` share SHA-256 `CDD5F0518B9DD07338C583A527AF965FAD00BEA2BE7ED64D7D327494B2C8AFF5`.
- The package was not reinstalled because the installed artifact was already exact.

## Runtime Evidence

- NOVORA PC ran from this checkout and remained responsive.
- Final Android state showed LinkEngine, VisionEngine and STEngine active together; ExInEngine was detected.
- LinkEngine launched `Tools/LinkEngine/LENetworkRelay.exe` from this checkout and kept USB reverses for `27214`, `27183` and `27184`.
- Windows showed established localhost sessions on all three ports.
- Android netstats identified `tun0` as the default VPN transport. TCP connection probes to `1.1.1.1:443` and `example.com:80` succeeded. ICMP probes did not receive replies.
- VisionEngine remote start created an independent scrcpy reverse and an Android server configured for 8 Mbps, 2340 maximum size and 120 FPS.
- ExInEngine identified an Xbox One Elite 2 Controller (`045E:028E`) and delivered live neutral telemetry. No valid calibration profile was created.

## VE And ExIn Coexistence Correction

- The first visual check was insufficient: Android still showed the physical controller as detected after VE started, but the standalone ExIn reverse channel had disappeared.
- Root cause: every scrcpy-compatible session uploaded and executed the same remote path, `/data/local/tmp/novora-vision-server.jar`. Starting VE overwrote the artifact used by the already-running ExIn control-only session.
- Fix: the remote server artifact is now isolated by SCID, for example `novora-vision-server-1234abcd.jar`. The per-session socket and JAR therefore share the same ownership boundary.
- Physical regression `Starting_vision_engine_preserves_the_exin_control_session` passed on `R5CY3118MEW`: ExIn remained ready after a full VE start, and Android simultaneously reported the control-only and video server processes using distinct session paths.
- A second runtime cause was reproduced after the user needed to restart VE: two NOVORA processes from the same checkout were open simultaneously, while only one owned ports `27214`, `27183`, `27184` and the ExIn reverse. The idle duplicate was closed and NOVORA now acquires a named single-instance handle before creating windows or engines.
- Runtime single-instance verification launched NOVORA twice: the second process exited normally and the running process count remained one.
- Automated suite after both corrections: 406 passed, 0 failed. The physical ExIn-to-VE regression passed again on `R5CY3118MEW`.

## Visual Evidence

- `android-final-state.png`: final three-engine coexistence and ExIn detection.
- `android-ve-running.png`: VE configuration retrieved from the running PC session.
- `android-returned-after-ve.png`: active VE state after returning to AppControl.
- `android-engines-final.png`: LE-only intermediate state demonstrating independent VE stop/restart behavior.
- `android-ve-le-running.png`: phone surface while VE and LE were running concurrently.

## Remaining Physical Gates

- Inspect Windows VE fullscreen geometry, focus and recovery behavior directly.
- Exercise every ExIn stick, trigger and button and confirm Android-side translation.
- Run comparable idle/loaded latency, jitter, loss and queue measurements.
- Run NVIDIA versus software decode A/B with decoded-frame and benefit evidence.

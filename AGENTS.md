# NOVORA project context

- App Control (`src/NOVORA.Android`) is NOVORA's Android companion app. It handles the authorized PC connection, screen and audio capture, remote control, and the Android side of LinkEngine and VisionEngine.
- App Control is the intended Android capture source for VisionEngine over USB and LAN. Prefer its existing `MediaProjection`/`MediaCodec` services when replacing the legacy scrcpy server path.
- The project goal is to remove scrcpy executables and runtime dependencies from NOVORA and use NOVORA's own Android capture service. Preserve working video, audio, and control over USB and LAN while doing so.
- ExInEngine's standalone gamepad currently uses a shell-side UHID control path through the legacy Android server. Preserve gamepad behavior when replacing that server; App Control's accessibility service does not currently implement UHID.
- LinkEngine's native relay includes source derived from Gnirehtet. Removing Gnirehtet fully requires replacing that relay implementation, not just renaming files or removing attribution.
- Keep required third-party license notices for any code or binary that remains; remove a notice only after its corresponding dependency or derived code is gone.

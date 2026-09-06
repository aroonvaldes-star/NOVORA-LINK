# VisionEngine / scrcpy 4.1 attribution

VisionEngine Block A uses `scrcpy-server` 4.1 as a temporary Android backend and implements a NOVORA-managed Windows client around the documented/source protocol behavior of scrcpy 4.1.

The implementation intentionally keeps the third-party identity visible. It is not a renamed `scrcpy.exe` and does not claim ownership of scrcpy.

Relevant upstream components studied for Block A:

- `app/src/server.c`
- `app/src/server.h`
- `app/src/adb/adb_tunnel.c`
- `app/src/demuxer.c`
- `app/src/packet_merger.c`
- `app/src/decoder.c`

Upstream: https://github.com/Genymobile/scrcpy

License: Apache License 2.0. The distribution already includes the Apache 2.0 text in `src/NOVORA/Tools/LICENSE.txt`, and the repository-level `THIRD-PARTY-NOTICES.md` identifies scrcpy explicitly.

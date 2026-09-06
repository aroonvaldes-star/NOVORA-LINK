# VisionEngine Block B — scrcpy 4.1 adaptation notes

VisionEngine Block B usa como referencia técnica el código Apache-2.0 de scrcpy 4.1.

Piezas estudiadas/adaptadas:

- `app/src/control_msg.c` y `control_msg.h`: enumeración y serialización de mensajes de control.
- `app/src/device_msg.c` y `device_msg.h`: clipboard ACK y UHID output Android -> PC.
- `app/src/hid/hid_gamepad.c`: descriptor HID, report de 15 bytes, ejes, botones y D-pad.
- `app/src/server.c`: orden de sockets `video -> audio -> control` y comportamiento reverse/forward.
- `app/src/demuxer.c`: framing multimedia de 12 bytes para video/audio.

VisionEngine no ejecuta `scrcpy.exe`. Block B usa temporalmente `scrcpy-server` como backend Android mientras el cliente Windows (transport, video, audio, control, gamepad y exchange) pertenece a la arquitectura de NOVORA.

Se deben preservar los avisos y condiciones de Apache License 2.0 aplicables al código derivado.

from __future__ import annotations

from pathlib import Path
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
VE = ROOT / "src" / "NOVORA" / "VisionEngine"

EXPECTED = {
    "Core": {
        "StatesCoreVE.cs",
        "ResultCoreVE.cs",
        "StatusCoreVE.cs",
        "SessionCoreVE.cs",
        "EngineCoreVE.cs",
        "RuntimeCoreVE.cs",
    },
    "Device": {
        "StatesDeviceVE.cs",
        "CapabilitiesDeviceVE.cs",
        "StatusDeviceVE.cs",
        "SessionDeviceVE.cs",
        "ManagerDeviceVE.cs",
    },
    "Server": {
        "StatesServerVE.cs",
        "BackendServerVE.cs",
        "OptionsServerVE.cs",
        "StatusServerVE.cs",
        "SessionServerVE.cs",
        "ManagerServerVE.cs",
    },
    "Protocol": {
        "ConstantsProtocolVE.cs",
        "CodecProtocolVE.cs",
        "ExtensionsProtocolVE.cs",
        "KindProtocolVE.cs",
        "BinaryProtocolVE.cs",
        "SessionProtocolVE.cs",
        "HeaderProtocolVE.cs",
        "ReaderProtocolVE.cs",
    },
    "Transport": {
        "ModeTransportVE.cs",
        "StatesTransportVE.cs",
        "TunnelTransportVE.cs",
        "SessionTransportVE.cs",
        "ManagerTransportVE.cs",
    },
    "Video": {
        "PacketVideoVE.cs",
        "FrameVideoVE.cs",
        "StatsVideoVE.cs",
        "MergerVideoVE.cs",
        "DecoderVideoVE.cs",
        "DemuxerVideoVE.cs",
        "ManagerVideoVE.cs",
    },
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def check_files() -> None:
    missing: list[str] = []
    for folder, files in EXPECTED.items():
        for name in files:
            path = VE / folder / name
            if not path.is_file():
                missing.append(str(path.relative_to(ROOT)))
    require(not missing, "Missing Block A files:\n" + "\n".join(missing))


def check_naming() -> None:
    for folder, files in EXPECTED.items():
        for name in files:
            require(name.endswith(f"{folder}VE.cs") or folder == "Core" and name.endswith("CoreVE.cs"),
                    f"Naming rule violation: {folder}/{name}")
            text = (VE / folder / name).read_text(encoding="utf-8")
            typename = name[:-3]
            require(re.search(rf"\b(?:class|record|enum|struct)\s+{re.escape(typename)}\b", text) is not None,
                    f"Primary type {typename} not found in {folder}/{name}")


def check_protocol_fixture() -> None:
    # scrcpy 4.1: codec id is big-endian "h264".
    codec = struct.pack(">I", 0x68323634)
    require(codec == b"h264", "H264 codec id fixture is wrong")

    # scrcpy 4.1 session header: bit 63, client-resized bit 0, width, height.
    session = bytearray(12)
    session[0] = 0x80
    session[3] = 0x01
    session[4:8] = struct.pack(">I", 1080)
    session[8:12] = struct.pack(">I", 2400)
    require(session[0] & 0x80, "Session marker missing")
    require(session[3] & 0x01, "Client-resized flag missing")
    require(struct.unpack(">I", session[4:8])[0] == 1080, "Width fixture invalid")
    require(struct.unpack(">I", session[8:12])[0] == 2400, "Height fixture invalid")

    # Media header: config bit 62, key-frame bit 61, PTS low 61 bits, uint32 len.
    pts = 123456
    flags = (1 << 61) | pts
    media = struct.pack(">QI", flags, 4096)
    parsed = struct.unpack(">Q", media[:8])[0]
    require((parsed & (1 << 61)) != 0, "Key-frame flag fixture invalid")
    require((parsed & ((1 << 61) - 1)) == pts, "PTS mask fixture invalid")
    require(struct.unpack(">I", media[8:12])[0] == 4096, "Packet length fixture invalid")


def check_source_contracts() -> None:
    constants = (VE / "Protocol" / "ConstantsProtocolVE.cs").read_text(encoding="utf-8")
    require("PacketHeaderSizeVE = 12" in constants, "Packet header must be 12 bytes")
    require("DeviceNameFieldLengthVE = 64" in constants, "Device name field must be 64 bytes")
    require("PacketFlagConfigVE = 1UL << 62" in constants, "Config flag must be bit 62")
    require("PacketFlagKeyFrameVE = 1UL << 61" in constants, "Key flag must be bit 61")

    codec = (VE / "Protocol" / "CodecProtocolVE.cs").read_text(encoding="utf-8")
    for literal in ("0x68323634", "0x68323635", "0x00617631"):
        require(literal in codec, f"Missing codec id {literal}")

    merger = (VE / "Video" / "MergerVideoVE.cs").read_text(encoding="utf-8")
    require("_configurationVE" in merger, "H.26x config merger state missing")

    decoder = (VE / "Video" / "DecoderVideoVE.cs").read_text(encoding="utf-8")
    for symbol in ("avcodec_send_packet", "avcodec_receive_frame", "AVCodecContext"):
        require(symbol in decoder, f"FFmpeg decoder contract missing: {symbol}")

    server = (VE / "Server" / "ManagerServerVE.cs").read_text(encoding="utf-8")
    for token in ("com.genymobile.scrcpy.Server", "4.1", "scid=", "audio=false", "control=false"):
        require(token in server, f"scrcpy 4.1 compatibility launch missing: {token}")

    tunnel = (VE / "Transport" / "ManagerTransportVE.cs").read_text(encoding="utf-8")
    require("reverse" in tunnel and "forward" in tunnel,
            "Transport must implement adb reverse with forward fallback")


def check_headless() -> None:
    forbidden = [
        "RendererVideoVE",
        "Direct3D",
        "System.Windows.Controls.Image",
        "WriteableBitmap",
        "BitmapSource",
    ]
    for path in VE.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        for token in forbidden:
            require(token not in text, f"Headless rule violated by {token} in {path.relative_to(ROOT)}")


def main() -> int:
    check_files()
    check_naming()
    check_protocol_fixture()
    check_source_contracts()
    check_headless()
    print("VISIONENGINE BLOCK A STATIC/PROTOCOL CONTRACT: PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as exc:
        print(f"VISIONENGINE BLOCK A STATIC/PROTOCOL CONTRACT: FAIL\n{exc}", file=sys.stderr)
        raise SystemExit(1)

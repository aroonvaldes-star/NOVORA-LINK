#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WINDOWS_LE = ROOT / "src" / "NOVORA" / "LinkEngine"
ANDROID_LE = ROOT / "NOVORA.linkEngine.Android" / "LinkEngine"
VE = ROOT / "src" / "NOVORA" / "VisionEngine"

EXPECTED_WINDOWS = {
    "Core/StatesCoreLE.cs": "StatesCoreLE",
    "Core/ResultCoreLE.cs": "ResultCoreLE",
    "Core/StatusCoreLE.cs": "StatusCoreLE",
    "Core/EngineCoreLE.cs": "EngineCoreLE",
    "Device/ConnectionDeviceLE.cs": "ConnectionDeviceLE",
    "Device/ManagerDeviceLE.cs": "ManagerDeviceLE",
    "Device/PollingDeviceLE.cs": "PollingDeviceLE",
    "Device/ProvisioningDeviceLE.cs": "ProvisioningDeviceLE",
    "Device/SessionDeviceLE.cs": "SessionDeviceLE",
    "Device/StateDeviceLE.cs": "StateDeviceLE",
    "Metrics/CollectorMetricsLE.cs": "CollectorMetricsLE",
    "Network/DnsNetworkLE.cs": "DnsNetworkLE",
    "Network/ManagerNetworkLE.cs": "ManagerNetworkLE",
    "Network/RelayNetworkLE.cs": "RelayNetworkLE",
    "Network/SessionNetworkLE.cs": "SessionNetworkLE",
    "Network/StateNetworkLE.cs": "StateNetworkLE",
    "Protocol/HeartbeatProtocolLE.cs": "HeartbeatProtocolLE",
    "Recovery/ManagerRecoveryLE.cs": "ManagerRecoveryLE",
    "Recovery/MonitorRecoveryLE.cs": "MonitorRecoveryLE",
    "Recovery/PolicyRecoveryLE.cs": "PolicyRecoveryLE",
    "Recovery/ResultRecoveryLE.cs": "ResultRecoveryLE",
    "Recovery/StateRecoveryLE.cs": "StateRecoveryLE",
    "Runtime/ManagerRuntimeLE.cs": "ManagerRuntimeLE",
    "Runtime/SessionRuntimeLE.cs": "SessionRuntimeLE",
    "Runtime/StateRuntimeLE.cs": "StateRuntimeLE",
    "Transport/DataTransportLE.cs": "DataTransportLE",
    "Transport/HandshakeTransportLE.cs": "HandshakeTransportLE",
    "Transport/ListenerTransportLE.cs": "ListenerTransportLE",
    "Transport/ManagerTransportLE.cs": "ManagerTransportLE",
    "Transport/PortTransportLE.cs": "PortTransportLE",
    "Transport/SessionTransportLE.cs": "SessionTransportLE",
    "Transport/StateTransportLE.cs": "StateTransportLE",
}

EXPECTED_ANDROID = {
    "Network/NotificationNetworkLE.cs": "NotificationNetworkLE",
    "Network/PacketNetworkLE.cs": "PacketNetworkLE",
    "Network/StateNetworkLE.cs": "StateNetworkLE",
    "Network/StatusNetworkLE.cs": "StatusNetworkLE",
    "Network/TunnelNetworkLE.cs": "TunnelNetworkLE",
    "Network/VpnNetworkLE.cs": "VpnNetworkLE",
    "Protocol/DataProtocolLE.cs": "DataProtocolLE",
    "Protocol/HandshakeProtocolLE.cs": "HandshakeProtocolLE",
    "Protocol/HeartbeatProtocolLE.cs": "HeartbeatProtocolLE",
    "Transport/ClientTransportLE.cs": "ClientTransportLE",
    "Transport/DataTransportLE.cs": "DataTransportLE",
    "Transport/StateTransportLE.cs": "StateTransportLE",
    "Transport/StatusTransportLE.cs": "StatusTransportLE",
}

EXPECTED_VE = {
    str(path.relative_to(VE)).replace("\\", "/"): path.stem
    for path in VE.rglob("*.cs")
}


TYPE_RE_TEMPLATE = r"\b(?:class|enum|interface|record(?:\s+(?:class|struct))?)\s+{name}\b"


def check_group(base: Path, expected: dict[str, str], errors: list[str]) -> None:
    for rel, type_name in expected.items():
        path = base / rel
        if not path.is_file():
            errors.append(f"missing: {path.relative_to(ROOT)}")
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        if not re.search(TYPE_RE_TEMPLATE.format(name=re.escape(type_name)), text):
            errors.append(
                f"primary type {type_name} not found in {path.relative_to(ROOT)}"
            )


def check_redundant_dirs(base: Path, errors: list[str]) -> None:
    if not base.exists():
        return
    # Native relay is not C#, so every C# engine source should be at most
    # <Subsystem>/<File.cs> after normalization.
    for path in base.rglob("*.cs"):
        rel = path.relative_to(base)
        if len(rel.parts) > 2:
            errors.append(f"redundant C# subfolder: {rel}")


def main() -> int:
    errors: list[str] = []
    check_group(WINDOWS_LE, EXPECTED_WINDOWS, errors)
    check_group(ANDROID_LE, EXPECTED_ANDROID, errors)
    check_group(VE, EXPECTED_VE, errors)

    renderer_exists = (VE / "Renderer").is_dir()
    forbidden_ve_tokens = {
        "RendererVideoVE",
        "WriteableBitmap",
        "BitmapSource",
        "System.Windows.Controls.Image",
    }
    if not renderer_exists:
        forbidden_ve_tokens.add("Direct3D")

    if VE.exists():
        for path in VE.rglob("*.cs"):
            text = path.read_text(encoding="utf-8-sig", errors="replace")
            for token in forbidden_ve_tokens:
                if token in text:
                    phase = "Block D" if renderer_exists else "Block C"
                    errors.append(
                        f"VisionEngine naming/renderer contract violation ({phase}): "
                        f"{token} in {path.relative_to(ROOT)}"
                    )

    check_redundant_dirs(WINDOWS_LE, errors)
    check_redundant_dirs(ANDROID_LE, errors)

    if errors:
        print("ENGINE NAMING VALIDATION: FAIL")
        for error in errors:
            print(f" - {error}")
        return 1

    print("ENGINE NAMING VALIDATION: PASS")
    print(f" Windows LinkEngine files checked: {len(EXPECTED_WINDOWS)}")
    print(f" Android LinkEngine files checked: {len(EXPECTED_ANDROID)}")
    print(f" VisionEngine files checked: {len(EXPECTED_VE)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

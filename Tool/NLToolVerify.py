from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
MAP_PATH = ROOT / "Documentation" / "NLDocumentationNamingMap.json"

SKIP_DIRS = {
    "bin", "obj", "target", ".git", ".vs", "artifacts",
    "__pycache__", "TestResults",
}

STANDARD_ROOT = {
    "README.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md",
    "LICENSE", "THIRD-PARTY-NOTICES.md", "ACKNOWLEDGEMENTS.md",
    ".gitignore", ".gitattributes", ".gitmodules", "NOVORA.sln",
}

REQUIRED = [
    "src/NOVORA/NLProjectDesktop.csproj",
    "src/NOVORA.Android/NLProjectAndroid.csproj",
    "tests/NOVORA.Tests/NLProjectTests.csproj",
    "Documentation/NLDocumentationNamingMap.json",
    "Documentation/Manual/ES/NLDocumentationManualUsuarioES.md",
    "Documentation/Manual/EN/NLDocumentationUserManualEN.md",
    "Tool/NLInstallerSetup.iss",
    "Tool/NLToolReleaseGate.ps1",
    "src/NOVORA/Tools/adb.exe",
    "src/NOVORA/Tools/scrcpy.exe",
    "src/NOVORA/Tools/scrcpy-server",
    "src/NOVORA/Tools/LinkEngine/LENetworkRelay.exe",
    "src/NOVORA/Android/NLAndroidApp.apk",
]

issues = []
checked = 0

def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()

def skipped(path: Path) -> bool:
    return any(part in SKIP_DIRS for part in path.relative_to(ROOT).parts)

if not MAP_PATH.is_file():
    issues.append("Missing naming map: Documentation/NLDocumentationNamingMap.json")
    mapping = {}
else:
    try:
        payload = json.loads(MAP_PATH.read_text(encoding="utf-8-sig"))
        mapping = payload.get("files", {})
        if not isinstance(mapping, dict):
            issues.append("Naming map 'files' must be an object.")
            mapping = {}
    except Exception as ex:
        issues.append(f"Naming map invalid JSON: {ex}")
        mapping = {}

# The current map is authoritative for migrations: old names may appear as
# historical keys, but an old mapped source file must not coexist with the
# migrated target in the active tree.
for old, new in mapping.items():
    old_path = ROOT / Path(old)
    new_path = ROOT / Path(new)
    if old == new:
        continue
    if old_path.exists() and new_path.exists():
        issues.append(f"Old and new mapped paths coexist: {old} -> {new}")

# Prefix checks are intentionally aligned with the current regulation:
# prefix identifies area/engine. Folder name is NOT forced into every type.
for path in ROOT.rglob("*"):
    if not path.is_file() or skipped(path):
        continue

    rel = path.relative_to(ROOT)
    rels = rel.as_posix()
    name = path.name

    if len(rel.parts) == 1 and name in STANDARD_ROOT:
        checked += 1
        continue

    if rels.startswith("Documentation/"):
        if name.endswith((".md", ".json", ".html", ".txt")) and not name.startswith("NLDocumentation"):
            issues.append(f"Documentation file must use NLDocumentation prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("Tool/"):
        if name.endswith((".py", ".ps1")) and not name.startswith("NLTool"):
            issues.append(f"Tool script must use NLTool prefix: {rels}")
        if name.endswith(".iss") and not name.startswith("NLInstaller"):
            issues.append(f"Installer must use NLInstaller prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA/LinkEngine/"):
        if path.suffix.lower() == ".rs":
            expected = "LE"
        elif path.suffix.lower() == ".cs":
            expected = "LE"
        else:
            checked += 1
            continue
        if not name.startswith(expected):
            issues.append(f"LinkEngine owned file must use LE prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA/VisionEngine/"):
        if path.suffix.lower() == ".cs" and not name.startswith("VE"):
            issues.append(f"VisionEngine owned file must use VE prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA/STEngine/"):
        if path.suffix.lower() == ".cs" and not name.startswith("ST"):
            issues.append(f"STEngine owned file must use ST prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA/NVIDIA/"):
        if path.suffix.lower() in {".cs", ".md"} and not name.startswith("NLNVIDIA"):
            issues.append(f"NVIDIA owned file must use NLNVIDIA prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA.Android/"):
        if path.suffix.lower() in {".cs", ".xml"} and not name.startswith("NL"):
            issues.append(f"Android NOVORA-owned file must use NL prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("src/NOVORA/"):
        if path.suffix.lower() in {".cs", ".xaml", ".csproj"}:
            # Engine directories handled above. Everything else is common NL.
            if not name.startswith("NL") and name not in {"Cargo.toml", "Cargo.lock"}:
                issues.append(f"NOVORA common owned file must use NL prefix: {rels}")
        checked += 1
        continue

    if rels.startswith("tests/NOVORA.Tests/"):
        if path.suffix.lower() == ".cs" and not (name.startswith("NLTest") or name.startswith("NLConfiguration")):
            issues.append(f"NOVORA.Tests source must use NLTest/NLConfiguration prefix: {rels}")
        checked += 1
        continue

    checked += 1

for expected in REQUIRED:
    if not (ROOT / expected).is_file():
        issues.append(f"Missing required file: {expected}")

# Retired Android package identifiers must not return to active C#.
retired = [
    "com.novora.linkengine",
    "NOVORA.LinkEngine.Android",
]
for path in list((ROOT / "src" / "NOVORA").rglob("*.cs")) + list((ROOT / "src" / "NOVORA.Android").rglob("*.cs")):
    if skipped(path):
        continue
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    for token in retired:
        if token.lower() in text.lower():
            issues.append(f"Retired integration token {token}: {relative(path)}")

# Current package and single-device contract.
android_project = (ROOT / "src/NOVORA.Android/NLProjectAndroid.csproj").read_text(encoding="utf-8-sig")
if "<ApplicationId>com.novora.appcontrol</ApplicationId>" not in android_project:
    issues.append("Android ApplicationId is not com.novora.appcontrol.")

traffic_cs = (ROOT / "src/NOVORA/LinkEngine/Traffic/LETrafficEngine.cs").read_text(encoding="utf-8-sig")
if not re.search(r"MaxConcurrentSessionsLE\s*=\s*1\s*;", traffic_cs):
    issues.append("Managed LinkEngine session limit is not 1.")

traffic_rs = (ROOT / "src/NOVORA/LinkEngine/Network/Native/RelayCore/src/relay/LERelayTrafficEngine.rs").read_text(encoding="utf-8-sig")
if not re.search(r"MAX_ACTIVE_CLIENTS_LE:\s*usize\s*=\s*1\s*;", traffic_rs):
    issues.append("Native TrafficEngine active-client limit is not 1.")

tunnel_rs = (ROOT / "src/NOVORA/LinkEngine/Network/Native/RelayCore/src/relay/LERelayTunnelServer.rs").read_text(encoding="utf-8-sig")
if not re.search(r"MAX_CLIENTS_LE:\s*usize\s*=\s*1\s*;", tunnel_rs):
    issues.append("Native tunnel client limit is not 1.")

if issues:
    print("NOVORA VERIFY: FAIL")
    print("\n".join(f"- {item}" for item in issues))
    sys.exit(1)

print(f"NOVORA VERIFY: PASS ({checked} files checked)")
print("NamingMap loaded and current prefixes validated.")
print("Android package: com.novora.appcontrol")
print("LinkEngine active-device limit: 1")

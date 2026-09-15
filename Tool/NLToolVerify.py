from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SKIP = {"bin", "obj", "target", ".git", ".vs", "artifacts", "__pycache__"}
ENGINES = {"LinkEngine": "LE", "VisionEngine": "VE", "STEngine": "ST"}
OWN = {".cs", ".xaml", ".rs", ".csproj", ".sln", ".py", ".md", ".json", ".xml"}
issues = []
checked = 0
for path in ROOT.rglob("*"):
    if not path.is_file() or any(part in SKIP for part in path.relative_to(ROOT).parts):
        continue
    relative = path.relative_to(ROOT)
    if "Tools" in relative.parts:
        if path.name.endswith((".apk", ".apk.idsig", ".aab")):
            issues.append(f"Retired package: {relative}")
        continue
    if path.name in {"Cargo.toml", "Cargo.lock"}:
        continue
    if path.suffix not in OWN | {".png", ".ico", ".mp4", ".txt"}:
        issues.append(f"Unclassified file: {relative}")
        continue
    motor = next((ENGINES[part] for part in relative.parts if part in ENGINES), "NL")
    folder = path.parent.name
    if path.suffix in {".csproj", ".sln"}:
        motor, folder = "NL", "Project"
    elif path.suffix == ".rs":
        motor, folder = "LE", "Relay" if folder == "relay" else "Native"
    elif path.parent == ROOT:
        motor, folder = "NL", "Documentation"
    expected = motor + folder
    if not re.fullmatch(re.escape(expected) + r"[A-Z][A-Za-z0-9]*(?:\.xaml)?\.[A-Za-z0-9]+", path.name):
        issues.append(f"Name must start with {expected}: {relative}")
    if path.suffix == ".cs":
        text = path.read_text(encoding="utf-8-sig")
        clean = re.sub(r"/\*.*?\*/|//[^\n]*", "", text, flags=re.S)
        for name in re.findall(r"\b(?:class|record(?:\s+(?:class|struct))?|enum|struct|interface)\s+([A-Za-z_]\w*)", clean):
            if not name.startswith(motor + folder):
                issues.append(f"Type {name} must start with {motor + folder}: {relative}")
        forbidden = ["com.novora." + "linkengine", "NOVORA.LinkEngine." + "Android", "SESSION_" + "READY", "NOVORA-" + "REMOTE"]
        for value in forbidden:
            if value.lower() in text.lower():
                issues.append(f"Retired integration {value}: {relative}")
    checked += 1
for expected in ["src/NOVORA/Tools/adb.exe", "src/NOVORA/Tools/scrcpy.exe", "src/NOVORA/Tools/scrcpy-server"]:
    if not (ROOT / expected).is_file():
        issues.append(f"Missing preserved tool: {expected}")
if issues:
    print("NOMENCLATURE: FAIL")
    print("\n".join(issues))
    sys.exit(1)
print(f"NOMENCLATURE: PASS ({checked} owned files)")
print("Retired app integration: absent in active C# sources")
#!/usr/bin/env bash
# Ejecuta NOVORA LINK Linux; compila Debug si aún no existe el ensamblado.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Linux/NOVORA.Linux.csproj"
ASSEMBLY="$ROOT/src/Linux/bin/Debug/net8.0/NOVORA-LINK.Linux.dll"

export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if [[ ! -f "$ASSEMBLY" ]]; then
    dotnet build "$PROJECT" -c Debug
fi

cd "$ROOT/src/Linux"
exec dotnet "$ASSEMBLY"

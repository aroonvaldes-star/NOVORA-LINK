#!/usr/bin/env bash
# Compila y recompila el proyecto NOVORA LINK Linux incluido en esta copia.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Linux/NOVORA.Linux.csproj"

export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$ROOT"

build()
{
    local config="$1"
    echo
    echo "============================================================"
    echo " NOVORA LINK Linux · Rebuild $config"
    echo "============================================================"
    dotnet build "$PROJECT" -c "$config" -t:Rebuild
}

build Debug
debug_status=$?
build Release
release_status=$?

echo
echo "Debug:   $debug_status"
echo "Release: $release_status"
echo
read -r -p "Pulsa Enter para cerrar..." _
exit $(( debug_status != 0 ? debug_status : release_status ))

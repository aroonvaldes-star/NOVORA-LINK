#!/usr/bin/env bash
# Compila y recompila el proyecto Android de LINKCONTROL.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Android/NLProjectAndroid.csproj"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$ROOT"

build()
{
    local config="$1"
    echo
    echo "============================================================"
    echo " LINKCONTROL Android · Rebuild $config"
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

#!/usr/bin/env bash
# Compila y recompila el proyecto Android de LINKCONTROL.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Android/NLProjectAndroid.csproj"
ANDROID_SDK="${ANDROID_SDK:-$HOME/.local/lib/android-sdk}"
JAVA_HOME="${JAVA_HOME:-$HOME/.local/lib/jvm/jdk-21.0.13.1+2}"
AAPT2_DIR="${AAPT2_DIR:-$HOME/.local/bin}"
AAPT2_ARG=()
if [[ -x "$AAPT2_DIR/aapt2" ]]; then
    AAPT2_ARG=("-p:Aapt2ToolPath=$AAPT2_DIR")
fi

export PATH="$HOME/.dotnet:$PATH"
export JAVA_HOME
export PATH="$JAVA_HOME/bin:$PATH"
export ANDROID_HOME="$ANDROID_SDK"
export ANDROID_SDK_ROOT="$ANDROID_SDK"
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
    dotnet build "$PROJECT" -c "$config" -t:Rebuild \
        -p:AndroidSdkDirectory="$ANDROID_SDK" \
        -p:AndroidSdkPath="$ANDROID_SDK" \
        -p:JavaSdkDirectory="$JAVA_HOME" \
        "${AAPT2_ARG[@]}"
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

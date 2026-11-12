#!/usr/bin/env bash
# Install an isolated official .NET 8 SDK and cross-build the Windows desktop.
# WPF execution and visual checks still require Windows.
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
bootstrap_dir="$repo_dir/artifacts/dotnet-linux-bootstrap"
sdk_dir="${NOVORA_DOTNET_ROOT:-$bootstrap_dir/sdk}"
mkdir -p -- "$bootstrap_dir"

if [[ ! -x "$sdk_dir/dotnet" ]]; then
    if [[ -n "${NOVORA_DOTNET_ROOT:-}" ]]; then
        echo "No executable dotnet found in NOVORA_DOTNET_ROOT: $sdk_dir" >&2
        exit 1
    fi
    command -v curl >/dev/null || { echo 'curl is required.' >&2; exit 1; }
    installer="$bootstrap_dir/dotnet-install.sh"
    curl --fail --location --retry 2 --connect-timeout 15 --max-time 120 \
        https://dot.net/v1/dotnet-install.sh --output "$installer"
    bash "$installer" --channel 8.0 --install-dir "$sdk_dir" --no-path
fi

# These settings apply to this script only; system .NET and shell config stay intact.
export DOTNET_ROOT="$sdk_dir"
export PATH="$sdk_dir:$PATH"
export DOTNET_CLI_HOME="$bootstrap_dir/cli-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export NUGET_PACKAGES="$bootstrap_dir/nuget-packages"
# NTFS mounts reject ':' in NuGet HTTP-cache names. Use a native Linux cache.
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-${XDG_CACHE_HOME:-$HOME/.cache}/novora-link/nuget-http}"

desktop_targets="$sdk_dir/sdk"
if ! compgen -G "$desktop_targets/*/Sdks/Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets" >/dev/null; then
    echo 'The installed SDK lacks WindowsDesktop build targets; cross-build cannot continue.' >&2
    exit 1
fi

cd -- "$repo_dir"
"$sdk_dir/dotnet" --info
# Restore fresh assets rather than using NuGet paths generated on Windows.
# An empty fallback-folder property avoids inherited Windows-only fallback paths.
"$sdk_dir/dotnet" build 'src/WINDOWS/NLProjectDesktop.csproj' \
    --configuration Release \
    --runtime win-x64 --self-contained false \
    -p:EnableWindowsTargeting=true \
    -p:RestoreFallbackFolders= \
    -p:NuGetAudit=false \
    -p:BaseOutputPath="$repo_dir/artifacts/windows-from-linux/bin/" \
    "$@"

#!/usr/bin/env bash
# Recompila el relay Rust de LinkEngine para NOVORA LINK Linux.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MANIFEST="$ROOT/src/Linux/LinkRelay/Cargo.toml"
PREFIX="${RELAY_TOOLCHAIN:-$HOME/.local/toolchain}"
TCP_IP_SRC="${TCP_IP_SRC:-$HOME/.local/src/tcp_ip}"
TCP_IP_REV="6ebaaad5b7379dba266fd36b3726798f70e57810"
export CARGO_HOME="${CARGO_HOME:-$HOME/.cargo-relay}"
export PATH="$HOME/.cargo/bin:$PATH"

# La dependencia tcp_ip se fija por revisión. El caché Git de Cargo puede
# conservar referencias temporales inválidas, así que se usa un clón local.
if [[ ! -d "$TCP_IP_SRC/.git" ]]; then
    mkdir -p "$(dirname "$TCP_IP_SRC")"
    git clone -q https://github.com/rustp2p/tcp_ip.git "$TCP_IP_SRC"
    git -C "$TCP_IP_SRC" checkout -q "$TCP_IP_REV"
fi

if command -v cc >/dev/null 2>&1 && [[ ! -x "$PREFIX/usr/bin/gcc" ]]; then
    echo "Usando compilador C del sistema"
else
    echo "Usando cadena de herramientas aislada en $PREFIX"
    export PATH="$PREFIX/usr/bin:$PATH"
    export LD_LIBRARY_PATH="$PREFIX/usr/lib/aarch64-linux-gnu:$PREFIX/lib/aarch64-linux-gnu:/usr/lib/aarch64-linux-gnu:/lib/aarch64-linux-gnu"
    export CC="$PREFIX/usr/bin/gcc"
    export CFLAGS="--sysroot=$PREFIX"
    export CARGO_TARGET_AARCH64_UNKNOWN_LINUX_GNU_LINKER="$PREFIX/usr/bin/gcc"
    export RUSTFLAGS="-C link-arg=--sysroot=$PREFIX"
fi

cd "$ROOT"
cargo build --release --manifest-path "$MANIFEST" \
    --config "patch.\"https://github.com/rustp2p/tcp_ip.git\".tcp_ip.path=\"$TCP_IP_SRC\""

ls -l "$ROOT/src/Linux/LinkRelay/target/release/novora-linkengine-relay"

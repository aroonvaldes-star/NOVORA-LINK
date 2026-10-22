# NOVORA LinkEngine relay

This is NOVORA's replacement DATA server for AppControl. It listens on
`127.0.0.1:27184`, writes a four-byte relay ID, and exchanges contiguous
IPv4 packets with the Android VPN over the existing USB reverse or authenticated
LAN gateway. TCP and UDP sockets are forwarded through the PC's network.

The packet stack is the independently maintained Apache-2.0 licensed
[`tcp_ip`](https://github.com/rustp2p/tcp_ip) crate, pinned in `Cargo.lock`.
The server and its DATA integration are NOVORA code. It uses no Zig, scrcpy,
Gnirehtet, VPN adapter or DLL. The crate's license must be included with a
distributed relay binary.

Build: `cargo build --release --offline` after dependencies are cached.
The build script copies `target/release/novora-linkengine-relay.exe` to
`Tools/LinkEngine/LENetworkRelay.exe`. Physical Android VPN tests passed over
LAN for TCP and UDP forwarding through the PC on 2026-10-10.

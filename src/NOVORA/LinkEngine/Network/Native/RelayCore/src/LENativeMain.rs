/*
 * NOVORA LinkEngine Relay
 *
 * IPv4 TCP/UDP relay core derived from:
 * Genymobile/gnirehtet
 *
 * Original work Copyright (C) 2017 Genymobile.
 * Apache License 2.0.
 *
 * NOVORA does NOT use:
 * - Gnirehtet APK
 * - Gnirehtet command-line lifecycle
 * - Gnirehtet ADB control
 * - gnirehtet.exe
 *
 * LinkEngine owns Android VPN, ADB lifecycle, control,
 * recovery and session state.
 */

extern crate relaylib;

use std::process;

const LINKENGINE_DATA_PORT: u16 = 27184;

fn main() {
    println!(
        "NOVORA LinkEngine Relay DATA tcp:{}",
        LINKENGINE_DATA_PORT
    );

    if let Err(error) =
        relaylib::relay(LINKENGINE_DATA_PORT)
    {
        eprintln!(
            "LinkEngine Relay failed: {}",
            error
        );

        process::exit(1);
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Failover;

public enum AdbTransportTypeLE
{
    Unknown = 0,
    Usb = 1,
    Wifi = 2
}

public sealed record AdbDeviceTransportLE(
    string Serial,
    AdbTransportTypeLE Transport,
    string RawLine)
{
    public bool IsUsb =>
        Transport == AdbTransportTypeLE.Usb;

    public bool IsWifi =>
        Transport == AdbTransportTypeLE.Wifi;
}

public sealed record AdbFailoverResultLE(
    bool Success,
    string? Serial,
    AdbTransportTypeLE Transport,
    bool TransportChanged,
    string Message)
{
    public static AdbFailoverResultLE Ok(
        string serial,
        AdbTransportTypeLE transport,
        bool changed,
        string message)
    {
        return new AdbFailoverResultLE(
            true,
            serial,
            transport,
            changed,
            message);
    }

    public static AdbFailoverResultLE Fail(
        string message)
    {
        return new AdbFailoverResultLE(
            false,
            null,
            AdbTransportTypeLE.Unknown,
            false,
            message);
    }
}

/// <summary>
/// Gestiona recuperación del transporte ADB.
///
/// Prioridad:
///
/// 1. Mantener el transporte actual si sigue disponible.
/// 2. USB.
/// 3. Wi-Fi.
/// 4. Sin transporte.
///
/// No realiza polling permanente.
/// Está pensado para ejecutarse cuando una operación ADB
/// falle o durante ManagerRecoveryLE.
/// </summary>
public sealed class AdbFailoverLE
{
    public const int ControlPortLE = 27183;

    public const int DataPortLE = 27184;

    private readonly string _adbPath;

    public AdbFailoverLE(
        string adbPath)
    {
        if (string.IsNullOrWhiteSpace(
                adbPath))
        {
            throw new ArgumentException(
                "La ruta de adb.exe es obligatoria.",
                nameof(adbPath));
        }

        _adbPath =
            Path.GetFullPath(
                adbPath);

        if (!File.Exists(
                _adbPath))
        {
            throw new FileNotFoundException(
                "No se encontró adb.exe.",
                _adbPath);
        }
    }

    /// <summary>
    /// Obtiene todos los dispositivos ADB actualmente
    /// en estado "device".
    /// </summary>
    public async Task<IReadOnlyList<AdbDeviceTransportLE>>
        GetAvailableDevicesAsync(
            CancellationToken cancellationToken = default)
    {
        ProcessResultLE result =
            await RunAdbAsync(
                    new[]
                    {
                        "devices",
                        "-l"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return Array.Empty<AdbDeviceTransportLE>();
        }

        List<AdbDeviceTransportLE> devices =
            new();

        string[] lines =
            result.StandardOutput.Split(
                new[]
                {
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string raw in lines)
        {
            string line =
                raw.Trim();

            if (line.StartsWith(
                    "List of devices",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts =
                line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                continue;
            }

            string serial =
                parts[0];

            string state =
                parts[1];

            if (!string.Equals(
                    state,
                    "device",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AdbTransportTypeLE transport =
                DetectTransportLE(
                    serial);

            devices.Add(
                new AdbDeviceTransportLE(
                    serial,
                    transport,
                    line));
        }

        return devices;
    }

    /// <summary>
    /// Selecciona el mejor transporte disponible.
    ///
    /// Si currentSerial sigue vivo, lo conserva.
    ///
    /// Si desapareció:
    ///
    /// USB > Wi-Fi.
    /// </summary>
    public async Task<AdbFailoverResultLE>
        RecoverTransportAsync(
            string? currentSerial,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdbDeviceTransportLE> devices =
            await GetAvailableDevicesAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        if (devices.Count == 0)
        {
            return AdbFailoverResultLE.Fail(
                "No existe ningún transporte ADB disponible.");
        }

        /*
         * ====================================================
         * 1. CONSERVAR TRANSPORTE ACTUAL
         * ====================================================
         */

        if (!string.IsNullOrWhiteSpace(
                currentSerial))
        {
            AdbDeviceTransportLE? current =
                devices.FirstOrDefault(
                    device =>
                        string.Equals(
                            device.Serial,
                            currentSerial,
                            StringComparison.OrdinalIgnoreCase));

            if (current is not null)
            {
                bool reverseOk =
                    await RebuildLinkEngineReverseAsync(
                            current.Serial,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (!reverseOk)
                {
                    return AdbFailoverResultLE.Fail(
                        $"ADB sigue presente en {current.Serial}, " +
                        "pero no fue posible reconstruir los reverse " +
                        "de LinkEngine.");
                }

                return AdbFailoverResultLE.Ok(
                    current.Serial,
                    current.Transport,
                    changed: false,
                    message:
                        $"ADB continúa activo mediante {current.Transport}.");
            }
        }

        /*
         * ====================================================
         * 2. PREFERIR USB
         * ====================================================
         */

        AdbDeviceTransportLE? candidate =
            devices
                .Where(
                    device =>
                        device.Transport ==
                        AdbTransportTypeLE.Usb)
                .OrderBy(
                    device =>
                        device.Serial,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        /*
         * ====================================================
         * 3. SI NO HAY USB, TOMAR WIFI
         * ====================================================
         */

        candidate ??=
            devices
                .Where(
                    device =>
                        device.Transport ==
                        AdbTransportTypeLE.Wifi)
                .OrderBy(
                    device =>
                        device.Serial,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        /*
         * ====================================================
         * 4. ÚLTIMO RECURSO
         * ====================================================
         */

        candidate ??=
            devices[0];

        /*
         * ====================================================
         * 5. RECONSTRUIR LINKENGINE
         * ====================================================
         */

        bool restored =
            await RebuildLinkEngineReverseAsync(
                    candidate.Serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!restored)
        {
            return AdbFailoverResultLE.Fail(
                $"Encontramos ADB mediante {candidate.Transport}, " +
                "pero falló la reconstrucción de CONTROL/DATA.");
        }

        return AdbFailoverResultLE.Ok(
            candidate.Serial,
            candidate.Transport,
            changed: true,
            message:
                $"ADB recuperado mediante {candidate.Transport}. " +
                $"Serial: {candidate.Serial}");
    }

    /// <summary>
    /// Reconstruye los dos canales ADB reverse utilizados
    /// actualmente por LinkEngine.
    /// </summary>
    public async Task<bool> RebuildLinkEngineReverseAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                serial))
        {
            return false;
        }

        serial =
            serial.Trim();

        /*
         * Eliminamos solamente los reverse de NOVORA.
         * No ejecutamos reverse --remove-all porque podríamos
         * destruir forwards pertenecientes a otras herramientas.
         */

        await RunAdbAsync(
                new[]
                {
                    "-s",
                    serial,
                    "reverse",
                    "--remove",
                    $"tcp:{ControlPortLE}"
                },
                cancellationToken,
                throwOnCancellation: true)
            .ConfigureAwait(false);

        await RunAdbAsync(
                new[]
                {
                    "-s",
                    serial,
                    "reverse",
                    "--remove",
                    $"tcp:{DataPortLE}"
                },
                cancellationToken,
                throwOnCancellation: true)
            .ConfigureAwait(false);

        /*
         * CONTROL
         */

        ProcessResultLE control =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{ControlPortLE}",
                        $"tcp:{ControlPortLE}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (control.ExitCode != 0)
        {
            return false;
        }

        /*
         * DATA
         */

        ProcessResultLE data =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{DataPortLE}",
                        $"tcp:{DataPortLE}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (data.ExitCode != 0)
        {
            return false;
        }

        /*
         * Verificación.
         */

        ProcessResultLE list =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        "--list"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        if (list.ExitCode != 0)
        {
            return false;
        }

        bool controlPresent =
            list.StandardOutput.Contains(
                $"tcp:{ControlPortLE}",
                StringComparison.OrdinalIgnoreCase);

        bool dataPresent =
            list.StandardOutput.Contains(
                $"tcp:{DataPortLE}",
                StringComparison.OrdinalIgnoreCase);

        return
            controlPresent &&
            dataPresent;
    }

    /// <summary>
    /// Solicita al dispositivo volver a ADB USB.
    ///
    /// Sólo funciona si todavía existe un transporte ADB
    /// desde el cual podamos enviar la orden.
    /// </summary>
    public async Task<bool> RequestUsbModeAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                serial))
        {
            return false;
        }

        ProcessResultLE result =
            await RunAdbAsync(
                    new[]
                    {
                        "-s",
                        serial.Trim(),
                        "usb"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

        /*
         * adb usb normalmente provoca que el transporte actual
         * se desconecte mientras adbd cambia de modo.
         *
         * ExitCode 0 significa que la orden fue aceptada,
         * no necesariamente que USB ya esté enumerado.
         */

        return result.ExitCode == 0;
    }

    private static AdbTransportTypeLE DetectTransportLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(
                serial))
        {
            return AdbTransportTypeLE.Unknown;
        }

        /*
         * Los transports TCP tradicionales aparecen normalmente:
         *
         * 192.168.x.x:5555
         *
         * Wireless Debugging también utiliza endpoints TCP.
         */

        if (serial.Contains(
                ':',
                StringComparison.Ordinal))
        {
            return AdbTransportTypeLE.Wifi;
        }

        return AdbTransportTypeLE.Usb;
    }

    private async Task<ProcessResultLE> RunAdbAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnCancellation = true)
    {
        ProcessStartInfo startInfo =
            new()
            {
                FileName = _adbPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using Process process =
            new()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

        process.Start();

        Task<string> stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> stderrTask =
            process.StandardError.ReadToEndAsync();

        try
        {
            await process
                .WaitForExitAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(
                        entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort.
            }

            if (throwOnCancellation)
            {
                throw;
            }

            return new ProcessResultLE(
                -1,
                string.Empty,
                "ADB cancelado.");
        }

        string stdout =
            await stdoutTask
                .ConfigureAwait(false);

        string stderr =
            await stderrTask
                .ConfigureAwait(false);

        return new ProcessResultLE(
            process.ExitCode,
            stdout,
            stderr);
    }

    private sealed record ProcessResultLE(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
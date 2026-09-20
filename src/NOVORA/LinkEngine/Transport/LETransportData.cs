using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NOVORA.LinkEngine.Core;
using NOVORA.Service;

namespace NOVORA.LinkEngine.Transport;

public sealed class LETransportData :
    IAsyncDisposable
{
    public const int DevicePortLE =
        27184;

    public const int HostPortLE =
        27184;

    private readonly NLServiceADB _adb;

    private readonly ConcurrentDictionary<string, byte> _configured =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public LETransportData(
        NLServiceADB adb)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    public async Task<LECoreResult> StartAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return LECoreResult.Fail(
                "Serial DATA inválido.");
        }

        serial =
            serial.Trim();

        try
        {
            await _adb
                .StartServerAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            await _adb
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        $"tcp:{DevicePortLE}",
                        $"tcp:{HostPortLE}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            bool verified =
                await VerifyAsync(
                        serial,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (!verified)
            {
                return LECoreResult.Fail(
                    $"DATA adb reverse tcp:{DevicePortLE} no pudo verificarse.");
            }

            _configured[serial] =
                0;

            return LECoreResult.Ok(
                $"DATA adb reverse tcp:{DevicePortLE} -> tcp:{HostPortLE} READY.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _configured.TryRemove(
                serial,
                out _);

            return LECoreResult.Fail(
                $"DATA transport falló: {ex.Message}");
        }
    }

    public async Task EnsureAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (await VerifyAsync(
                serial,
                cancellationToken)
            .ConfigureAwait(false))
        {
            _configured[serial] =
                0;

            return;
        }

        LECoreResult result =
            await StartAsync(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                result.Message);
        }
    }

    public async Task<bool> VerifyAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        try
        {
            string output =
                await _adb
                    .ExecuteRawAsync(
                        new[]
                        {
                            "-s",
                            serial.Trim(),
                            "reverse",
                            "--list"
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

            return ContainsMappingLE(
                output,
                DevicePortLE,
                HostPortLE);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task StopAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        serial =
            serial.Trim();

        _configured.TryRemove(
            serial,
            out _);

        try
        {
            await _adb
                .ExecuteRawAsync(
                    new[]
                    {
                        "-s",
                        serial,
                        "reverse",
                        "--remove",
                        $"tcp:{DevicePortLE}"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Device may already be offline.
        }
    }

    public bool IsConfiguredLE(
        string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        return _configured.ContainsKey(
            serial.Trim());
    }

    private static bool ContainsMappingLE(
        string? output,
        int devicePort,
        int hostPort)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        string device =
            $"tcp:{devicePort}";

        string host =
            $"tcp:{hostPort}";

        string[] lines =
            output.Split(
                new[]
                {
                    "\r\n",
                    "\n",
                    "\r"
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            string[] columns =
                line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            if (columns.Length >= 3)
            {
                if (string.Equals(
                        columns[^2],
                        device,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        columns[^1],
                        host,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (columns.Length == 2)
            {
                if (string.Equals(
                        columns[0],
                        device,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        columns[1],
                        host,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        string[] serials =
            _configured.Keys.ToArray();

        foreach (string serial in serials)
        {
            try
            {
                await StopAsync(
                        serial,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _configured.Clear();

        _disposed =
            true;
    }
}
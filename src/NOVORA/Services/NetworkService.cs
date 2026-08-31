using NOVORA.Models;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public sealed record NetworkStatus(
    string IpAddress,
    string InterfaceName,
    bool InternetAvailable,
    long LatencyMs);

public sealed class NetworkService
{
    private readonly AdbService _adb;

    public NetworkService(AdbService adb)
    {
        _adb = adb;
    }

    public async Task<NetworkStatus> GetAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device is null || !device.Connected)
        {
            throw new InvalidOperationException(
                "No hay un dispositivo Android conectado.");
        }

        var route =
            await _adb.ShellAsync(
                device.Serial,
                "ip -4 route get 1.1.1.1",
                cancellationToken);

        var ipMatch =
            Regex.Match(
                route,
                @"\bsrc\s+(\d{1,3}(?:\.\d{1,3}){3})");

        var ifaceMatch =
            Regex.Match(
                route,
                @"\bdev\s+(\S+)");

        var ip =
            ipMatch.Success
                ? ipMatch.Groups[1].Value
                : string.Empty;

        var iface =
            ifaceMatch.Success
                ? ifaceMatch.Groups[1].Value
                : string.Empty;

        string ping;

        try
        {
            ping =
                await _adb.ShellAsync(
                    device.Serial,
                    "ping -c 1 -W 1 1.1.1.1",
                    cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Android puede devolver ExitCode 1 cuando
            // el host no responde. Eso representa falta
            // de conectividad, no un fallo de ADB.
            return new NetworkStatus(
                string.IsNullOrWhiteSpace(ip)
                    ? "—"
                    : ip,
                string.IsNullOrWhiteSpace(iface)
                    ? "—"
                    : iface,
                false,
                -1);
        }

        var latencyMatch =
            Regex.Match(
                ping,
                @"time[=<]([0-9.]+)\s*ms",
                RegexOptions.IgnoreCase);

        var latency =
            latencyMatch.Success &&
            double.TryParse(
                latencyMatch.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedLatency)
                ? (long)Math.Round(parsedLatency)
                : -1;

        var internet =
            ping.Contains(
                "bytes from",
                StringComparison.OrdinalIgnoreCase)
            ||
            ping.Contains(
                "time=",
                StringComparison.OrdinalIgnoreCase);

        return new NetworkStatus(
            string.IsNullOrWhiteSpace(ip)
                ? "—"
                : ip,
            string.IsNullOrWhiteSpace(iface)
                ? "—"
                : iface,
            internet,
            latency);
    }
}
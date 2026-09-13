using NOVORA.Models;
using System.Globalization;

namespace NOVORA.Services;

/// <summary>
/// Resultado completo de una ejecución de DeviceFastPerformance.
/// </summary>
public sealed record DeviceFastPerformanceResult(
    DeviceMetrics Before,
    DeviceMetrics After,
    long MemoryDeltaKb,
    long StorageDeltaKb,
    double LoadDelta,
    int ActionsAttempted,
    int ActionsCompleted)
{
    public bool MeasurableImprovement =>
        MemoryDeltaKb >= 8 * 1024 ||
        StorageDeltaKb > 0 ||
        LoadDelta >= 0.10;
}

/// <summary>
/// Optimizador universal y conservador para dispositivos Android.
///
/// IMPORTANTE:
/// Este servicio NO mata aplicaciones de terceros,
/// NO borra datos de aplicaciones,
/// NO toca almacenamiento personal,
/// NO usa pm clear,
/// NO usa force-stop global,
/// NO usa kill-all.
///
/// La filosofía es:
/// medir -> solicitar mantenimiento seguro -> medir nuevamente.
/// </summary>
public sealed class DeviceFastPerformanceService
{
    private readonly AdbService _adb;
    private readonly DeviceMetricsService _metrics;

    public DeviceFastPerformanceService(
        AdbService adb,
        DeviceMetricsService metrics)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(nameof(adb));

        _metrics =
            metrics ??
            throw new ArgumentNullException(nameof(metrics));
    }

    public async Task<DeviceFastPerformanceResult> OptimizeAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ValidateDevice(device);

        DeviceMetrics before =
            await _metrics.GetAsync(
                device,
                cancellationToken);

        SystemLoadSnapshot loadBefore =
            await GetSystemLoadAsync(
                device.Serial,
                cancellationToken);

        StorageSnapshot storageBefore =
            await GetStorageAsync(
                device.Serial,
                cancellationToken);

        IReadOnlyList<string> commands =
            BuildSafeUniversalCommands();

        int completed = 0;

        foreach (string command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await TryExecuteShellAsync(
                device.Serial,
                command,
                cancellationToken))
            {
                completed++;
            }
        }

        await Task.Delay(
            1500,
            cancellationToken);

        DeviceMetrics after =
            await _metrics.GetAsync(
                device,
                cancellationToken);

        SystemLoadSnapshot loadAfter =
            await GetSystemLoadAsync(
                device.Serial,
                cancellationToken);

        StorageSnapshot storageAfter =
            await GetStorageAsync(
                device.Serial,
                cancellationToken);

        long memoryDeltaKb =
            Math.Max(
                0,
                before.UsedMemoryKb -
                after.UsedMemoryKb);

        long storageDeltaKb =
            Math.Max(
                0,
                storageAfter.AvailableKb -
                storageBefore.AvailableKb);

        double loadDelta =
            Math.Max(
                0,
                loadBefore.Load1 -
                loadAfter.Load1);

        return new DeviceFastPerformanceResult(
            before,
            after,
            memoryDeltaKb,
            storageDeltaKb,
            loadDelta,
            commands.Count,
            completed);
    }

    public static IReadOnlyList<string> BuildSafeUniversalCommands()
    {
        return new[]
        {
            "sync",
            "am idle-maintenance"
        };
    }

    private static void ValidateDevice(
        DeviceInfo device)
    {
        if (device is null)
        {
            throw new ArgumentNullException(
                nameof(device));
        }

        if (!device.Connected)
        {
            throw new InvalidOperationException(
                "El dispositivo Android no está conectado.");
        }

        if (string.IsNullOrWhiteSpace(
            device.Serial))
        {
            throw new InvalidOperationException(
                "El dispositivo no tiene un serial ADB válido.");
        }
    }

    private async Task<bool> TryExecuteShellAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            string output =
                await _adb.ShellAsync(
                    serial,
                    command,
                    cancellationToken);

            if (output.Contains(
                    "Unknown command",
                    StringComparison.OrdinalIgnoreCase) ||
                output.Contains(
                    "Exception",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
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

    private async Task<SystemLoadSnapshot> GetSystemLoadAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            string output =
                await _adb.ShellAsync(
                    serial,
                    "cat /proc/loadavg",
                    cancellationToken);

            string[] parts =
                output
                    .Trim()
                    .Split(
                        new[]
                        {
                            ' ',
                            '\t',
                            '\r',
                            '\n'
                        },
                        StringSplitOptions.RemoveEmptyEntries);

            double load1 =
                ParseDouble(
                    parts,
                    0);

            double load5 =
                ParseDouble(
                    parts,
                    1);

            double load15 =
                ParseDouble(
                    parts,
                    2);

            return new SystemLoadSnapshot(
                load1,
                load5,
                load15);
        }
        catch
        {
            return new SystemLoadSnapshot(
                0,
                0,
                0);
        }
    }

    private async Task<StorageSnapshot> GetStorageAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            string output =
                await _adb.ShellAsync(
                    serial,
                    "df -k /data",
                    cancellationToken);

            string[] lines =
                output
                    .Split(
                        new[]
                        {
                            '\r',
                            '\n'
                        },
                        StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines.Reverse())
            {
                string[] columns =
                    line
                        .Split(
                            new[]
                            {
                                ' ',
                                '\t'
                            },
                            StringSplitOptions.RemoveEmptyEntries);

                if (columns.Length < 4)
                {
                    continue;
                }

                if (!long.TryParse(
                    columns[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long total))
                {
                    continue;
                }

                if (!long.TryParse(
                    columns[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long used))
                {
                    continue;
                }

                if (!long.TryParse(
                    columns[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long available))
                {
                    continue;
                }

                return new StorageSnapshot(
                    total,
                    used,
                    available);
            }
        }
        catch
        {
        }

        return new StorageSnapshot(
            0,
            0,
            0);
    }

    private static double ParseDouble(
        string[] parts,
        int index)
    {
        if (index >= parts.Length)
        {
            return 0;
        }

        return double.TryParse(
            parts[index],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0;
    }

    private sealed record SystemLoadSnapshot(
        double Load1,
        double Load5,
        double Load15);

    private sealed record StorageSnapshot(
        long TotalKb,
        long UsedKb,
        long AvailableKb);
}

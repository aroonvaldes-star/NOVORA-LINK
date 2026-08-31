using NOVORA.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public sealed record DeviceMetrics(
    double CpuPercent,
    long UsedMemoryKb,
    long TotalMemoryKb,
    int BatteryPercent,
    double BatteryTemperatureC);

public sealed class DeviceMetricsService
{
    private readonly AdbService _adb;

    public DeviceMetricsService(
        AdbService adb)
    {
        _adb =
            adb ??
            throw new ArgumentNullException(
                nameof(adb));
    }

    public async Task<DeviceMetrics> GetAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device is null ||
            !device.Connected ||
            string.IsNullOrWhiteSpace(device.Serial))
        {
            throw new InvalidOperationException(
                "No hay un dispositivo Android conectado.");
        }

        /*
         * Conservamos la lectura de RAM que ya funcionaba.
         *
         * CPU se obtiene con varias líneas de top porque Android/toybox
         * no utiliza exactamente el mismo formato en todos los equipos.
         */
        var output =
            await _adb.ShellAsync(
                device.Serial,
                "printf '%s\\n' " +
                "\"$(dumpsys meminfo | grep -m1 'Total RAM')\" " +
                "\"$(dumpsys meminfo | grep -m1 'Used RAM')\" " +
                "\"$(dumpsys battery | grep -m1 'level:')\" " +
                "\"$(dumpsys battery | grep -m1 'temperature:')\"; " +
                "echo '__NOVORA_CPU__'; " +
                "top -n 1 -b 2>/dev/null | head -n 12",
                cancellationToken);

        var totalMemoryKb =
            ParseMemory(
                output,
                "Total RAM");

        var usedMemoryKb =
            ParseMemory(
                output,
                "Used RAM");

        var batteryPercent =
            ParseInt(
                output,
                @"level:\s*(\d+)");

        var temperatureRaw =
            ParseDouble(
                output,
                @"temperature:\s*(\d+(?:\.\d+)?)");

        var batteryTemperatureC =
            temperatureRaw / 10d;

        var cpuPercent =
            ParseCpu(output);

        return new DeviceMetrics(
            cpuPercent,
            usedMemoryKb,
            totalMemoryKb,
            batteryPercent,
            batteryTemperatureC);
    }

    /*
     * Devuelve SIEMPRE KB.
     *
     * Android puede devolver:
     *
     * Total RAM: 11984936K
     * Total RAM: 11704 MB
     * Total RAM: 11 GB
     *
     * El modelo DeviceMetrics permanece en KB.
     */
    private static long ParseMemory(
        string text,
        string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match =
            Regex.Match(
                text,
                $@"{Regex.Escape(label)}\s*[:=]?\s*([\d,]+(?:\.\d+)?)\s*(KB|K|MB|M|GB|G)?",
                RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return 0;
        }

        var numberText =
            match.Groups[1]
                .Value
                .Replace(
                    ",",
                    string.Empty);

        if (!double.TryParse(
                numberText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return 0;
        }

        var unit =
            match.Groups[2]
                .Value
                .Trim()
                .ToUpperInvariant();

        var valueKb =
            unit switch
            {
                "GB" or "G" =>
                    value * 1024d * 1024d,

                "MB" or "M" =>
                    value * 1024d,

                "KB" or "K" =>
                    value,

                /*
                 * dumpsys meminfo históricamente reporta
                 * estos valores como KB aunque algunos
                 * fabricantes omitan la K.
                 */
                _ =>
                    value
            };

        if (valueKb <= 0)
        {
            return 0;
        }

        if (valueKb >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return
            (long)Math.Round(
                valueKb,
                MidpointRounding.AwayFromZero);
    }

    private static int ParseInt(
        string text,
        string pattern)
    {
        var match =
            Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return 0;
        }

        return int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static double ParseDouble(
        string text,
        string pattern)
    {
        var match =
            Regex.Match(
                text,
                pattern,
                RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return 0;
        }

        return double.TryParse(
            match.Groups[1].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    private static double ParseCpu(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var cpuSectionIndex =
            text.IndexOf(
                "__NOVORA_CPU__",
                StringComparison.OrdinalIgnoreCase);

        var cpuText =
            cpuSectionIndex >= 0
                ? text[
                    (cpuSectionIndex +
                     "__NOVORA_CPU__".Length)..]
                : text;

        /*
         * Formato frecuente de Android/toybox:
         *
         * 800%cpu  35%user  0%nice  40%sys
         * 725%idle ...
         *
         * En un equipo de 8 núcleos el total puede ser 800.
         *
         * Convertimos eso a escala 0-100:
         *
         * (total - idle) / total * 100
         */
        var totalMatch =
            Regex.Match(
                cpuText,
                @"(?<total>\d+(?:\.\d+)?)\s*%\s*cpu",
                RegexOptions.IgnoreCase);

        var idleMatch =
            Regex.Match(
                cpuText,
                @"(?<idle>\d+(?:\.\d+)?)\s*%\s*idle",
                RegexOptions.IgnoreCase);

        if (totalMatch.Success &&
            idleMatch.Success &&
            TryParseInvariant(
                totalMatch.Groups["total"].Value,
                out var total) &&
            TryParseInvariant(
                idleMatch.Groups["idle"].Value,
                out var idle) &&
            total > 0)
        {
            var used =
                ((total - idle) / total) *
                100d;

            return ClampCpu(used);
        }

        /*
         * Otros Android usan escala normal de 100:
         *
         * CPU: 12% usr + 7% sys + 81% idle
         *
         * Con idle podemos obtener directamente el utilizado.
         */
        if (idleMatch.Success &&
            TryParseInvariant(
                idleMatch.Groups["idle"].Value,
                out var normalizedIdle) &&
            normalizedIdle >= 0 &&
            normalizedIdle <= 100)
        {
            return ClampCpu(
                100d - normalizedIdle);
        }

        /*
         * Otra variante:
         *
         * 15%user
         * 20%sys
         */
        var userMatch =
            Regex.Match(
                cpuText,
                @"(?<value>\d+(?:\.\d+)?)\s*%\s*(?:user|usr)",
                RegexOptions.IgnoreCase);

        var systemMatch =
            Regex.Match(
                cpuText,
                @"(?<value>\d+(?:\.\d+)?)\s*%\s*(?:sys|system)",
                RegexOptions.IgnoreCase);

        if (userMatch.Success ||
            systemMatch.Success)
        {
            var user =
                TryGetCpuValue(
                    userMatch);

            var system =
                TryGetCpuValue(
                    systemMatch);

            var combined =
                user + system;

            /*
             * Si top usa escala multicore y no pudimos leer
             * el total, evitamos mostrar números absurdos.
             */
            return ClampCpu(
                combined);
        }

        /*
         * Último fallback:
         * tabla de procesos de top.
         *
         * Buscamos la columna %CPU y sumamos los procesos
         * visibles en las líneas capturadas.
         */
        var lines =
            cpuText.Split(
                new[]
                {
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries);

        var cpuColumnIndex =
            -1;

        foreach (var line in lines)
        {
            var columns =
                SplitColumns(line);

            if (columns.Length == 0)
            {
                continue;
            }

            if (cpuColumnIndex < 0)
            {
                cpuColumnIndex =
                    Array.FindIndex(
                        columns,
                        column =>
                            string.Equals(
                                column,
                                "%CPU",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                column,
                                "CPU%",
                                StringComparison.OrdinalIgnoreCase));

                if (cpuColumnIndex >= 0)
                {
                    continue;
                }
            }

            if (cpuColumnIndex < 0 ||
                columns.Length <= cpuColumnIndex)
            {
                continue;
            }

            var cpuValueText =
                columns[cpuColumnIndex]
                    .Trim()
                    .TrimEnd('%');

            if (!TryParseInvariant(
                    cpuValueText,
                    out var processCpu))
            {
                continue;
            }

            if (processCpu < 0)
            {
                continue;
            }

            /*
             * Vamos acumulando la carga observada.
             */
            var current =
                processCpu;

            if (current > 0)
            {
                /*
                 * Reutilizamos una variable fuera del foreach
                 * mediante el método auxiliar inferior.
                 */
                return ParseProcessCpuTable(
                    lines,
                    cpuColumnIndex);
            }
        }

        return 0;
    }

    private static double ParseProcessCpuTable(
        IEnumerable<string> lines,
        int cpuColumnIndex)
    {
        double total =
            0;

        var headerPassed =
            false;

        foreach (var line in lines)
        {
            var columns =
                SplitColumns(line);

            if (columns.Length == 0)
            {
                continue;
            }

            if (!headerPassed)
            {
                var headerIndex =
                    Array.FindIndex(
                        columns,
                        column =>
                            string.Equals(
                                column,
                                "%CPU",
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                column,
                                "CPU%",
                                StringComparison.OrdinalIgnoreCase));

                if (headerIndex >= 0)
                {
                    cpuColumnIndex =
                        headerIndex;

                    headerPassed =
                        true;
                }

                continue;
            }

            if (columns.Length <= cpuColumnIndex)
            {
                continue;
            }

            var raw =
                columns[cpuColumnIndex]
                    .Trim()
                    .TrimEnd('%');

            if (!TryParseInvariant(
                    raw,
                    out var value))
            {
                continue;
            }

            if (value < 0)
            {
                continue;
            }

            total += value;
        }

        return ClampCpu(total);
    }

    private static string[] SplitColumns(
        string line)
    {
        return Regex.Split(
                line.Trim(),
                @"\s+")
            .Where(
                column =>
                    !string.IsNullOrWhiteSpace(
                        column))
            .ToArray();
    }

    private static double TryGetCpuValue(
        Match match)
    {
        if (!match.Success)
        {
            return 0;
        }

        return TryParseInvariant(
            match.Groups["value"].Value,
            out var value)
            ? value
            : 0;
    }

    private static bool TryParseInvariant(
        string value,
        out double result)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static double ClampCpu(
        double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(
            value,
            0d,
            100d);
    }
}
using System.Globalization;
using System.Text.RegularExpressions;

namespace NOVORA.Services;

public static class BitrateService
{
    private static readonly Regex NumberWithUnit = new(
        @"^\s*(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>Mbps|Mb/s|Mbit/s|MBPS|M|bps|bit/s|MB/s|MiB/s)\s*$",
        RegexOptions.CultureInvariant);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "10M";

        var input = value.Trim();

        // Bytes per second are not the same as bits per second.
        // Explicit legacy byte units are migrated by x8.
        if (TryParse(input, out var mbps, out var isBytes) && isBytes)
            mbps *= 8d;

        else if (!TryParse(input, out mbps, out _))
            return "10M";

        if (mbps <= 0 || double.IsNaN(mbps) || double.IsInfinity(mbps))
            return "10M";

        return FormatScrcpy(mbps);
    }

    public static string FormatMbps(double mbps)
    {
        if (mbps <= 0 || double.IsNaN(mbps) || double.IsInfinity(mbps))
            return "10 Mbps";

        return mbps.ToString("0.###", CultureInfo.InvariantCulture) + " Mbps";
    }

    private static bool TryParse(string input, out double mbps, out bool isBytes)
    {
        mbps = 0;
        isBytes = false;

        var match = NumberWithUnit.Match(input);
        if (!match.Success)
            return false;

        var number = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numeric))
            return false;

        var unit = match.Groups["unit"].Value;

        switch (unit)
        {
            case "bps":
            case "bit/s":
                mbps = numeric / 1_000_000d;
                break;
            case "Mbps":
            case "Mb/s":
            case "Mbit/s":
            case "MBPS":
            case "M":
                mbps = numeric;
                break;
            case "MB/s":
                mbps = numeric;
                isBytes = true;
                break;
            case "MiB/s":
                mbps = numeric * 1_048_576d / 1_000_000d;
                isBytes = true;
                break;
            default:
                return false;
        }

        return true;
    }

    private static string FormatScrcpy(double mbps)
        => mbps.ToString("0.###", CultureInfo.InvariantCulture) + "M";
}
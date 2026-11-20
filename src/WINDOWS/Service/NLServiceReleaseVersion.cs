using System.Reflection;

namespace NOVORA.Service;

// Major/minor identify the public version; build/revision identify internal work.
internal static class NLServiceReleaseVersion
{
    internal static string Installed
    {
        get
        {
            var assembly = typeof(NLServiceUpdate).Assembly;
            var info = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info) &&
                TryParse(info, out var infoVersion, out var experimental))
            {
                if (!experimental &&
                    assembly.GetName().Version is { } asmVersion &&
                    asmVersion > infoVersion)
                {
                    return asmVersion.ToString();
                }

                return info;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0";
        }
    }

    internal static bool TryParse(string text, out Version version, out bool experimental)
    {
        version = new Version(0, 0, 0, 0);
        experimental = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var clean = text.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = clean.Split('-', 2);
        experimental = parts.Length == 2;
        if (experimental && string.IsNullOrWhiteSpace(parts[1])) return false;
        var numericPart = parts[0].TrimEnd();
        if (!experimental && numericPart.EndsWith(" Release", StringComparison.OrdinalIgnoreCase))
            numericPart = numericPart[..^" Release".Length].TrimEnd();
        if (!Version.TryParse(numericPart, out var parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    internal static bool ShouldOffer(string installed, string candidate)
    {
        if (!TryParse(installed, out var current, out var currentExperimental) ||
            !TryParse(candidate, out var next, out var nextExperimental) || nextExperimental) return false;
        var currentSeries = new Version(current.Major, current.Minor);
        var nextSeries = new Version(next.Major, next.Minor);
        if (nextSeries != currentSeries) return nextSeries > currentSeries;
        // An official 1.4 supersedes every experimental 1.4.x, even if x is larger.
        return currentExperimental || next > current;
    }

    internal static string PublicLabel(Version version) => $"{version.Major}.{version.Minor}";
}
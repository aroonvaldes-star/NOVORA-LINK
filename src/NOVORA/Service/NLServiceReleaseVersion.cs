using System.Reflection;

namespace NOVORA.Service;

// Major/minor identify the public version; build/revision identify internal work.
internal static class NLServiceReleaseVersion
{
    internal static string Installed => typeof(NLServiceUpdate).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0";

    internal static bool TryParse(string text, out Version version, out bool experimental)
    {
        version = new Version(0, 0, 0, 0);
        experimental = false;
        var clean = text.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = clean.Split('-', 2);
        experimental = parts.Length == 2;
        if (experimental && string.IsNullOrWhiteSpace(parts[1])) return false;
        if (!Version.TryParse(parts[0], out var parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
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
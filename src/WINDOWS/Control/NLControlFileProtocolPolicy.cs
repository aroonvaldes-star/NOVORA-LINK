using System.Threading;

namespace NOVORA.Control;

/// <summary>
/// Single policy for FILES wire versions. FILES v1 is frozen, not extended:
/// it remains available only for peers that do not negotiate v2 and is removed
/// on <see cref="LegacyRemovalOn"/>. Every legacy fallback is counted so the
/// shutdown can be verified before the deadline.
/// </summary>
public static class NLFileProtocolPolicy
{
    public const int LegacyVersion = NLControlFileProtocolV1.Version;
    public const int CurrentVersion = NLControlFileProtocolV2.Version;

    /// <summary>First NOVORA release that negotiates FILES v2.</summary>
    public const string MinimumV2PeerVersion = "1.4.118";

    /// <summary>Last release family allowed to rely on the frozen v1 path.</summary>
    public const string LastLegacyPeerVersion = "1.4.117";

    public static readonly DateOnly LegacyDeprecatedOn = new(2026, 10, 23);
    public static readonly DateOnly LegacyRemovalOn = new(2027, 10, 23);

    public static bool IsLegacyEnabled(DateTimeOffset? now = null)
    {
        DateTime utc = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        return DateOnly.FromDateTime(utc) < LegacyRemovalOn;
    }

    /// <summary>Versions advertised in the authenticated capabilities exchange.</summary>
    public static int[] AdvertisedVersions(DateTimeOffset? now = null) =>
        IsLegacyEnabled(now)
            ? [LegacyVersion, CurrentVersion]
            : [CurrentVersion];

    public static bool IsSupported(int version, DateTimeOffset? now = null) =>
        version == CurrentVersion ||
        (version == LegacyVersion && IsLegacyEnabled(now));

    public static string LegacyDeadlineText =>
        $"FILES v1 está obsoleto desde {LegacyDeprecatedOn:yyyy-MM-dd} y se " +
        $"retirará el {LegacyRemovalOn:yyyy-MM-dd}; v2 requiere " +
        $"NOVORA {MinimumV2PeerVersion} o posterior.";
}

public sealed record NLFileProtocolTelemetrySnapshot(
    long CurrentTransfers,
    long LegacyTransfers,
    long LegacyFallbacks,
    string? LastLegacyReason,
    DateTimeOffset? LastLegacyAtUtc);

/// <summary>Process-wide counters used by diagnostics and tests.</summary>
public static class NLFileProtocolTelemetry
{
    private static long _currentTransfers;
    private static long _legacyTransfers;
    private static long _legacyFallbacks;
    private static readonly object _reasonGate = new();
    private static string? _lastLegacyReason;
    private static DateTimeOffset? _lastLegacyAtUtc;

    public static void RecordCurrentTransfer() =>
        Interlocked.Increment(ref _currentTransfers);

    public static void RecordLegacyTransfer() =>
        Interlocked.Increment(ref _legacyTransfers);

    public static void RecordLegacyFallback(string reason)
    {
        Interlocked.Increment(ref _legacyFallbacks);
        lock (_reasonGate)
        {
            _lastLegacyReason = string.IsNullOrWhiteSpace(reason)
                ? "unspecified"
                : reason;
            _lastLegacyAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public static NLFileProtocolTelemetrySnapshot Snapshot()
    {
        lock (_reasonGate)
        {
            return new(
                CurrentTransfers: Interlocked.Read(ref _currentTransfers),
                LegacyTransfers: Interlocked.Read(ref _legacyTransfers),
                LegacyFallbacks: Interlocked.Read(ref _legacyFallbacks),
                LastLegacyReason: _lastLegacyReason,
                LastLegacyAtUtc: _lastLegacyAtUtc);
        }
    }
}

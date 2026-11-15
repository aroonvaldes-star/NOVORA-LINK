namespace NOVORA.Control;

/// <summary>Mobile-data saving profiles negotiated through the additive capability «mobileDataV1».</summary>
public enum NLControlMobileProfile { Extreme, Economy, ControlOnly }

/// <summary>
/// Requested mobile-data policy for one logical session. <paramref name="SessionLimitBytes"/> null means
/// no budget; a positive value is a bidirectional media budget (not a carrier billing guarantee).
/// </summary>
public sealed record NLControlMobilePolicy(long Generation, long Revision, NLControlMobileProfile Profile,
    long? SessionLimitBytes);

/// <summary>Effective capture limits for a profile. ControlOnly has no capture at all.</summary>
public sealed record NLControlMobileLimits(bool Capture, int Bitrate, int Fps, int MaxSize, bool Audio);

/// <summary>Acknowledgement: requested and effective profile, and why it could not be applied.</summary>
public sealed record NLControlMobilePolicyAck(long Generation, long Revision, bool Accepted,
    NLControlMobileProfile Requested, NLControlMobileProfile? Effective, string? Reason = null);

/// <summary>NOVORA traffic counters for one logical session (before TLS; not operator billing).</summary>
public sealed record NLControlMobileUsage(long Generation, long SentBytes, long ReceivedBytes,
    long VideoSentBytes, long AudioSentBytes, long FileBytes, bool LimitReached);

public static class NLControlMobileData
{
    public const string Capability = "mobileDataV1";

    /// <summary>Lowest values accepted only when «mobileDataV1» was negotiated.</summary>
    public const int MobileMinimumBitrate = 100_000;
    public const int MobileMinimumFps = 1;
    /// <summary>Legacy limits kept for peers without the capability.</summary>
    public const int LegacyMinimumBitrate = 250_000;
    public const int LegacyMinimumFps = 10;

    public static NLControlMobileLimits Limits(NLControlMobileProfile profile) => profile switch
    {
        // 120 kbit/s ≈ 54 MB/h of nominal video (bit/s × 3600 / 8 / 1e6), excluding overhead.
        NLControlMobileProfile.Extreme => new(true, 120_000, 5, 480, false),
        // 30 fps by user request (2026-11-08): same data cost, smoother motion, less detail per frame.
        NLControlMobileProfile.Economy => new(true, 250_000, 30, 720, false),
        NLControlMobileProfile.ControlOnly => new(false, 0, 0, 0, false),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    /// <summary>Nominal decimal megabytes per hour of video for a bitrate.</summary>
    public static double NominalVideoMegabytesPerHour(int bitrate) => bitrate * 3600.0 / 8 / 1_000_000;

    public static int MinimumBitrate(bool mobileNegotiated) =>
        mobileNegotiated ? MobileMinimumBitrate : LegacyMinimumBitrate;

    public static int MinimumFps(bool mobileNegotiated) =>
        mobileNegotiated ? MobileMinimumFps : LegacyMinimumFps;

    public static void Validate(NLControlMobilePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Generation <= 0 || policy.Revision <= 0 || !Enum.IsDefined(policy.Profile) ||
            policy.SessionLimitBytes is <= 0)
            throw new InvalidDataException("Política de datos móviles no válida.");
    }
}

/// <summary>
/// Owns the applied policy and the session budget. Revisions are monotonic per generation; a new
/// socket keeps the same logical session (generation), so reconnecting never resets the budget.
/// Thread-safe; counters are 64-bit and snapshots are coherent.
/// </summary>
public sealed class NLControlMobileSession
{
    private readonly object _gate = new();
    private readonly long _generation;
    private long _revision;
    private NLControlMobileProfile? _profile;
    private long? _limit;
    private long _sent, _received, _video, _audio, _files;
    private bool _limitReached, _closed;

    public NLControlMobileSession(long generation)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation;
    }

    public event Action<NLControlMobileUsage>? LimitReached;

    public NLControlMobileProfile? Profile { get { lock (_gate) return _profile; } }

    /// <summary>
    /// Applies a policy. <paramref name="canCapture"/> reports whether the active capture pipeline
    /// accepts the profile's limits; when it does not, the profile is not announced as applied.
    /// </summary>
    public NLControlMobilePolicyAck Apply(NLControlMobilePolicy policy, Func<NLControlMobileLimits, bool> canCapture)
    {
        ArgumentNullException.ThrowIfNull(canCapture);
        lock (_gate)
        {
            NLControlMobilePolicyAck Reject(string reason) =>
                new(policy.Generation, policy.Revision, false, policy.Profile, _profile, reason);
            try { NLControlMobileData.Validate(policy); }
            catch (InvalidDataException ex) { return Reject(ex.Message); }
            if (_closed || policy.Generation != _generation) return Reject("La sesión cambió.");
            if (policy.Revision <= _revision) return Reject("Política antigua o repetida.");
            _revision = policy.Revision;
            NLControlMobileLimits limits = NLControlMobileData.Limits(policy.Profile);
            // Judge against the requested budget: raising or clearing it may allow media again,
            // but the bytes already used are never forgotten.
            bool reached = policy.SessionLimitBytes is long requested && _sent + _received >= requested;
            if (limits.Capture && reached)
                return Reject("Se alcanzó el tope de datos de la sesión; solo queda control.");
            if (!canCapture(limits))
                return Reject("El codificador no admite este perfil; se mantiene el anterior.");
            _profile = policy.Profile;
            _limit = policy.SessionLimitBytes;
            _limitReached = reached;
            return new(policy.Generation, policy.Revision, true, policy.Profile, _profile);
        }
    }

    /// <summary>Whether new media or file transfers may start.</summary>
    public bool MediaAllowed
    {
        get { lock (_gate) return !_closed && !_limitReached && _profile != NLControlMobileProfile.ControlOnly; }
    }

    public void RecordSent(long bytes, NLControlMobileChannel channel) => Record(bytes, sent: true, channel);
    public void RecordReceived(long bytes, NLControlMobileChannel channel) => Record(bytes, sent: false, channel);

    private void Record(long bytes, bool sent, NLControlMobileChannel channel)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        NLControlMobileUsage? reached = null;
        lock (_gate)
        {
            if (_closed || bytes == 0) return;
            if (sent) _sent = checked(_sent + bytes); else _received = checked(_received + bytes);
            if (sent && channel == NLControlMobileChannel.Video) _video += bytes;
            if (sent && channel == NLControlMobileChannel.Audio) _audio += bytes;
            if (channel == NLControlMobileChannel.Files) _files += bytes;
            if (!_limitReached && _limit is long limit && _sent + _received >= limit)
            {
                _limitReached = true;
                reached = SnapshotCore();
            }
        }
        // Raised outside the lock: the handler stops capture/audio and cancels transfers.
        if (reached is not null) LimitReached?.Invoke(reached);
    }

    public NLControlMobileUsage Snapshot() { lock (_gate) return SnapshotCore(); }

    private NLControlMobileUsage SnapshotCore() =>
        new(_generation, _sent, _received, _video, _audio, _files, _limitReached);

    /// <summary>An explicit stop ends the logical session; later records and policies are ignored.</summary>
    public void Close() { lock (_gate) _closed = true; }
}

public enum NLControlMobileChannel { Control, Video, Audio, Files }

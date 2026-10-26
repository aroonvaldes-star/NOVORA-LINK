using NOVORA.Control;

namespace NOVORA.Network;

/// <summary>Correlated control-channel RTT; all timestamps belong to the PC monotonic clock.</summary>
public sealed class NLNetworkControlProbe
{
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly long _generation;
    private readonly string _key;
    private readonly NLNetworkSampleSource _source;
    private readonly Queue<NLNetworkSample> _samples = new();
    private long? _lastAttempt;
    private (ulong Sequence, long Started)? _pending;
    private readonly Queue<(long ObservedAt, double RttMs, long OffsetUs)> _clockSamples = new();

    public NLNetworkControlProbe(long generation, string key, NLNetworkSampleSource source, TimeProvider? clock = null)
    {
        if (generation <= 0 || string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl))
            throw new ArgumentException("Ruta de eco de control no válida.");
        if (source is not (NLNetworkSampleSource.UsbControl or NLNetworkSampleSource.LanControl))
            throw new ArgumentException("La fuente del eco debe ser el canal de control USB o LAN.");
        _generation = generation; _key = key; _source = source; _clock = clock ?? TimeProvider.System;
    }

    public long Generation => _generation;
    public string LinkKey => _key;
    public TimeProvider Clock => _clock;
    public IReadOnlyList<NLNetworkSample> Samples
    {
        get { lock (_sync) { Trim(_clock.GetTimestamp()); return _samples.ToArray(); } }
    }

    public bool TryBegin(ulong sequence)
    {
        lock (_sync)
        {
            long now = _clock.GetTimestamp();
            ExpireAt(now);
            if (sequence == 0 || _pending is not null ||
                _lastAttempt is { } last && _clock.GetElapsedTime(last, now) < TimeSpan.FromSeconds(1)) return false;
            _lastAttempt = now; _pending = (sequence, now); return true;
        }
    }

    public void Expire() { lock (_sync) ExpireAt(_clock.GetTimestamp()); }

    /// <summary>
    /// Device monotonic clock minus PC monotonic clock, in microseconds, taken from the
    /// lowest-RTT echo of the last 10 seconds. Null until the device reports its clock.
    /// </summary>
    public long? DeviceClockOffsetUs
    {
        get
        {
            lock (_sync)
            {
                Trim(_clock.GetTimestamp());
                if (_clockSamples.Count == 0) return null;
                var best = _clockSamples.MinBy(sample => sample.RttMs);
                return best.OffsetUs;
            }
        }
    }

    public bool ObserveEcho(ulong sequence, long deviceClockUs = 0)
    {
        lock (_sync)
        {
            long now = _clock.GetTimestamp();
            ExpireAt(now);
            if (_pending is not { } pending || sequence != pending.Sequence) return false;
            double elapsed = _clock.GetElapsedTime(pending.Started, now).TotalMilliseconds;
            _pending = null;
            if (!double.IsFinite(elapsed) || elapsed < 0 || elapsed >= 2000) return false;
            Record(now, elapsed, true);
            if (deviceClockUs > 0)
            {
                // The device read its clock somewhere inside the round trip; the midpoint
                // bounds the error to RTT/2, so the lowest-RTT sample is the one used.
                long midpointUs = (long)((pending.Started + (now - pending.Started) / 2.0) * 1_000_000d / _clock.TimestampFrequency);
                if (_clockSamples.Count >= 16) _clockSamples.Dequeue();
                _clockSamples.Enqueue((now, elapsed, deviceClockUs - midpointUs));
            }
            return true;
        }
    }

    private void ExpireAt(long now)
    {
        if (_pending is { } pending && _clock.GetElapsedTime(pending.Started, now) >= TimeSpan.FromSeconds(2))
        { _pending = null; Record(now, null, false); }
        Trim(now);
    }

    private void Record(long now, double? rtt, bool valid)
    {
        // RTT is round-trip evidence for the route, never RTT/2 or directional one-way latency.
        foreach (var direction in Enum.GetValues<NLNetworkDirection>())
        {
            if (_samples.Count >= 32) _samples.Dequeue();
            _samples.Enqueue(new(_generation, _key, NLNetworkDomain.DeviceLink, direction, _source,
                now, 1, rtt, null, null, 0, 0, 0, 0, valid));
        }
    }

    private void Trim(long now)
    {
        while (_samples.Count > 0 && _clock.GetElapsedTime(_samples.Peek().ObservedAtTicks, now) > TimeSpan.FromSeconds(10))
            _samples.Dequeue();
        while (_clockSamples.Count > 0 && _clock.GetElapsedTime(_clockSamples.Peek().ObservedAt, now) > TimeSpan.FromSeconds(10))
            _clockSamples.Dequeue();
    }
}

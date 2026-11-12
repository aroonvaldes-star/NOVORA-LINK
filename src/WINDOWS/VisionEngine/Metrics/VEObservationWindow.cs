namespace NOVORA.VisionEngine.Metrics;

/// <summary>
/// Tracks one read-only observation window for a live VE stream. It does not
/// schedule work, inspect pixels, or alter the video pipeline.
/// </summary>
internal sealed class VEObservationWindow
{
    internal const int DurationSeconds = 20 * 60;

    private DateTimeOffset? _startedAtUtc;
    private bool _completed;

    internal VEObservationWindowSnapshot Observe(bool isStreaming, DateTimeOffset observedAtUtc)
    {
        if (!isStreaming)
        {
            _startedAtUtc = null;
            _completed = false;
            return VEObservationWindowSnapshot.Inactive;
        }

        if (_completed)
        {
            return new(false, true, 0, _startedAtUtc);
        }

        _startedAtUtc ??= observedAtUtc;
        TimeSpan elapsed = observedAtUtc - _startedAtUtc.Value;
        if (elapsed >= TimeSpan.FromSeconds(DurationSeconds))
        {
            _completed = true;
            return new(false, true, 0, _startedAtUtc);
        }

        int elapsedSeconds = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        return new(true, false, DurationSeconds - elapsedSeconds, _startedAtUtc);
    }
}

internal readonly record struct VEObservationWindowSnapshot(
    bool IsActive,
    bool IsCompleted,
    int RemainingSeconds,
    DateTimeOffset? StartedAtUtc)
{
    internal static VEObservationWindowSnapshot Inactive { get; } = new(false, false, 0, null);
}

namespace NOVORA.VisionEngine.Renderer;

/// <summary>
/// Admission policy for the renderer's existing latest-frame queue. It owns no
/// frames, timers or encoder settings. Composition follows WPF RenderingTime;
/// Stopwatch timestamps govern immediate pictures and the stalled-tick fallback.
/// These two independent time domains are never compared as absolute times.
/// </summary>
public sealed class VERendererPresentationTiming
{
    private readonly object _gateVE = new();
    private readonly long _timestampFrequencyVE;
    private bool _enabledVE;
    private double _refreshRateHzVE;
    private double _periodTicksVE;
    private double _earlyToleranceTicksVE;
    private double _compositionPeriodTicksVE;
    private double _compositionToleranceTicksVE;
    private long _compositionStallTimeoutTicksVE;
    private long _resetTimestampVE;
    private bool _hasPresentedVE;
    private long _lastPresentationTimestampVE;
    private double _nextPresentationTimestampVE;
    private bool _hasCompositionVE;
    private long _lastRenderingTimeTicksVE;
    private long _lastCompositionTimestampVE;
    private bool _hasCompositionPhaseVE;
    private double _nextCompositionRenderingTimeVE;
    private bool _hasPendingCompositionTickVE;
    private long _pendingCompositionRenderingTimeVE;

    public VERendererPresentationTiming(long timestampFrequency)
    {
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));

        _timestampFrequencyVE = timestampFrequency;
        ConfigureVE(false, 60, 0);
    }

    public bool EnabledVE { get { lock (_gateVE) return _enabledVE; } }
    public double RefreshRateHzVE { get { lock (_gateVE) return _refreshRateHzVE; } }
    public long CompositionStallTimeoutTicksVE
    {
        get { lock (_gateVE) return _compositionStallTimeoutTicksVE; }
    }

    public void ConfigureVE(bool enabled, double refreshRateHz, long nowTimestamp)
    {
        if (!double.IsFinite(refreshRateHz) || refreshRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(refreshRateHz));

        double period = Math.Max(1d, _timestampFrequencyVE / refreshRateHz);
        double stallTimeout = Math.Ceiling(Math.Max(3d, _timestampFrequencyVE * 3d / refreshRateHz));
        if (!double.IsFinite(period) || !double.IsFinite(stallTimeout) || stallTimeout >= long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(refreshRateHz), "Refresh interval exceeds the clock range.");

        lock (_gateVE)
        {
            _enabledVE = enabled;
            _refreshRateHzVE = refreshRateHz;
            _periodTicksVE = period;
            // A slightly early composition tick must not turn 60 Hz into 30 FPS.
            // Keep admission tolerance below 1 ms and 5% of the display period.
            _earlyToleranceTicksVE = Math.Min(_timestampFrequencyVE / 1000d, period * 0.05);
            _compositionPeriodTicksVE = Math.Max(1d, TimeSpan.TicksPerSecond / refreshRateHz);
            _compositionToleranceTicksVE = Math.Min(TimeSpan.TicksPerMillisecond,
                _compositionPeriodTicksVE * 0.05);
            _compositionStallTimeoutTicksVE = (long)stallTimeout;
            ResetStateVE(nowTimestamp);
        }
    }

    public bool ShouldPresentOnArrivalVE(long nowTimestamp)
    {
        lock (_gateVE)
        {
            if (!_enabledVE) return true;
            if (nowTimestamp < _resetTimestampVE) return false;
            if (!_hasPresentedVE) return true;
            if (nowTimestamp < _lastPresentationTimestampVE) return false;

            if (_hasCompositionVE &&
                (double)nowTimestamp - _lastCompositionTimestampVE < _compositionStallTimeoutTicksVE)
                return false;

            // Also used by a UI watchdog when composition stops and no new
            // frame arrives. The caller alone decides whether the queue is empty.
            return IsPresentationDueVE(nowTimestamp);
        }
    }

    public bool ShouldPresentOnCompositionVE(long renderingTimeTicks, long nowTimestamp)
    {
        lock (_gateVE)
        {
            if (!_enabledVE || nowTimestamp < _resetTimestampVE ||
                _hasPresentedVE && nowTimestamp < _lastPresentationTimestampVE)
                return false;

            if (_hasCompositionVE && renderingTimeTicks <= _lastRenderingTimeTicksVE)
                return false;

            _hasCompositionVE = true;
            _lastRenderingTimeTicksVE = renderingTimeTicks;
            _lastCompositionTimestampVE = nowTimestamp;

            // A callback delayed almost one display period can be followed by
            // the next on-time WPF tick only milliseconds later. Reject that
            // burst using half a real period, rather than gating every tick on
            // a full Stopwatch period (which would turn UI jitter into 30 FPS).
            if (_hasPresentedVE && nowTimestamp + _earlyToleranceTicksVE -
                _lastPresentationTimestampVE < _periodTicksVE / 2d)
                return false;

            bool due = !_hasPresentedVE || (_hasCompositionPhaseVE
                ? renderingTimeTicks + _compositionToleranceTicksVE >= _nextCompositionRenderingTimeVE
                : IsPresentationDueVE(nowTimestamp));
            if (due)
            {
                _hasPendingCompositionTickVE = true;
                _pendingCompositionRenderingTimeVE = renderingTimeTicks;
            }
            return due;
        }
    }

    /// <summary>
    /// After successful native presentation, pass its start timestamp and true
    /// only when admitted by ShouldPresentOnCompositionVE. Eligibility checks
    /// do not reserve a presentation; the renderer serializes native calls.
    /// </summary>
    public void RecordPresentationVE(long nowTimestamp, bool compositionDrivenVE = false)
    {
        lock (_gateVE)
        {
            if (nowTimestamp < _resetTimestampVE ||
                _hasPresentedVE && nowTimestamp <= _lastPresentationTimestampVE)
                return;

            if (compositionDrivenVE && _hasPendingCompositionTickVE)
            {
                _nextPresentationTimestampVE = nowTimestamp + _periodTicksVE;
                AdvanceCompositionPhaseVE(_pendingCompositionRenderingTimeVE);
            }
            else
            {
                if (!_hasPresentedVE)
                    _nextPresentationTimestampVE = nowTimestamp + _periodTicksVE;
                else
                {
                    double periods = Math.Max(1d, Math.Floor(
                        (nowTimestamp + _earlyToleranceTicksVE - _nextPresentationTimestampVE) /
                        _periodTicksVE) + 1d);
                    _nextPresentationTimestampVE += periods * _periodTicksVE;
                    if (_nextPresentationTimestampVE - nowTimestamp <
                        _periodTicksVE - 2d * _earlyToleranceTicksVE)
                        _nextPresentationTimestampVE = nowTimestamp + _periodTicksVE;
                }
                // An immediate/fallback picture starts a new composition phase;
                // use its monotonic age once to prevent a double first picture.
                _hasCompositionPhaseVE = false;
            }

            _hasPresentedVE = true;
            _hasPendingCompositionTickVE = false;
            _lastPresentationTimestampVE = nowTimestamp;
        }
    }

    public void ResetVE(long nowTimestamp)
    {
        lock (_gateVE) ResetStateVE(nowTimestamp);
    }

    private bool IsPresentationDueVE(long nowTimestamp)
        => nowTimestamp + _earlyToleranceTicksVE >= _nextPresentationTimestampVE;

    private void AdvanceCompositionPhaseVE(long renderingTimeTicks)
    {
        if (!_hasCompositionPhaseVE)
            _nextCompositionRenderingTimeVE = renderingTimeTicks + _compositionPeriodTicksVE;
        else
        {
            // Callback latency does not alter the compositor's phase. Skip
            // expired display periods and rebase an off-phase pause rather than
            // presenting a burst immediately before the old next deadline.
            double periods = Math.Max(1d, Math.Floor(
                (renderingTimeTicks + _compositionToleranceTicksVE - _nextCompositionRenderingTimeVE) /
                _compositionPeriodTicksVE) + 1d);
            _nextCompositionRenderingTimeVE += periods * _compositionPeriodTicksVE;
            if (_nextCompositionRenderingTimeVE - renderingTimeTicks <
                _compositionPeriodTicksVE - 2d * _compositionToleranceTicksVE)
                _nextCompositionRenderingTimeVE = renderingTimeTicks + _compositionPeriodTicksVE;
        }
        _hasCompositionPhaseVE = true;
    }

    private void ResetStateVE(long nowTimestamp)
    {
        _resetTimestampVE = nowTimestamp;
        _hasPresentedVE = false;
        _lastPresentationTimestampVE = nowTimestamp;
        _nextPresentationTimestampVE = nowTimestamp;
        _hasCompositionVE = false;
        _lastRenderingTimeTicksVE = 0;
        _lastCompositionTimestampVE = nowTimestamp;
        _hasCompositionPhaseVE = false;
        _nextCompositionRenderingTimeVE = 0;
        _hasPendingCompositionTickVE = false;
        _pendingCompositionRenderingTimeVE = 0;
    }
}

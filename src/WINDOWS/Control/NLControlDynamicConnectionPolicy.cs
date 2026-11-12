namespace NOVORA.Control;

/// <summary>Bounded ADB bootstrap recovery. Called on the Windows dispatcher; no I/O or polling.</summary>
public sealed class NLControlUsbRecovery(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string _connection = "";
    private int _attempts;
    private bool _paused, _wasAuthorized;
    private DateTimeOffset? _started, _lost;

    public bool AuthorizationExpired(bool authorized) => !authorized && !_wasAuthorized &&
        _started is { } started && _clock.GetUtcNow() - started >= TimeSpan.FromSeconds(20);
    public bool Exhausted => _attempts >= 3;

    public void Observe(string connection)
    {
        if (_connection == connection) return;
        _connection = connection;
        Reset();
    }

    public bool TryBegin()
    {
        if (_paused || _connection.Length == 0 || _attempts >= 3 || _started is not null) return false;
        _attempts++;
        _started = _clock.GetUtcNow();
        _lost = null;
        _wasAuthorized = false;
        return true;
    }

    public bool ShouldRetry(bool authorized, bool sessionEnded)
    {
        if (_paused || _started is null || _attempts >= 3) return false;
        if (authorized) { _wasAuthorized = true; _lost = null; return false; }
        DateTimeOffset now = _clock.GetUtcNow();
        if (_wasAuthorized || sessionEnded)
        {
            _lost ??= now;
            if (now - _lost.Value < TimeSpan.FromSeconds(2)) return false;
        }
        else if (now - _started.Value < TimeSpan.FromSeconds(20)) return false;
        _started = null;
        return true;
    }

    public void Pause() => _paused = true;
    public void Reset() { _paused = false; _attempts = 0; _started = null; _lost = null; _wasAuthorized = false; }
}

public static class NLControlLanAutoPolicy
{
    public static TimeSpan RetryDelay(int failures) => TimeSpan.FromSeconds(failures switch
    {
        <= 0 => 0, 1 => 1, 2 => 2, 3 => 4, 4 => 8, _ => 15
    });
    /// <summary>Input is ranked by Windows adapter/gateway preference; retain a healthy listener.</summary>
    public static string? ChooseAddress(IEnumerable<string> rankedAddresses, string? current)
    {
        string[] addresses = rankedAddresses.Distinct(StringComparer.Ordinal).ToArray();
        return current is not null && addresses.Contains(current, StringComparer.Ordinal)
            ? current : addresses.FirstOrDefault();
    }
}

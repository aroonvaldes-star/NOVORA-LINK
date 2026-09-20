using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NOVORA.LinkEngine.Core;

namespace NOVORA.LinkEngine.Metrics;

public sealed class LEMetricsCollector
{
    private readonly ConcurrentDictionary<string, LEMetricsDevice> _devices =
        new(StringComparer.OrdinalIgnoreCase);

    public LEMetricsDevice GetOrCreate(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return _devices.GetOrAdd(serial, static value => new LEMetricsDevice(value));
    }

    public LEMetricsDeviceMetricsSnapshot GetSnapshot(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return GetOrCreate(serial).Snapshot();
    }

    public IReadOnlyList<LEMetricsDeviceMetricsSnapshot> GetAllSnapshots()
    {
        return _devices.Values
            .Select(static value => value.Snapshot())
            .OrderBy(static value => value.Serial)
            .ToArray();
    }

    public void Remove(string serial)
    {
        if (!string.IsNullOrWhiteSpace(serial))
            _devices.TryRemove(serial, out _);
    }

    public void Clear() => _devices.Clear();
}

public sealed class LEMetricsDevice
{
    private readonly object _sync = new();

    private LECoreStates _state = LECoreStates.Disconnected;
    private bool _internetActive;
    private long _bytesSent;
    private long _bytesReceived;
    private double _latencyMs;
    private double _throughputMbps;
    private int _tcpSessions;
    private int _udpSessions;
    private int _dnsFailures;
    private int _recoveryAttempts;
    private int _successfulRecoveries;
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;

    public string Serial { get; }

    public LEMetricsDevice(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        Serial = serial;
    }

    public void SetState(LECoreStates state)
    {
        lock (_sync)
        {
            _state = state;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetInternetActive(bool active)
    {
        lock (_sync)
        {
            _internetActive = active;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void AddTraffic(long bytesSent, long bytesReceived)
    {
        lock (_sync)
        {
            _bytesSent += Math.Max(0, bytesSent);
            _bytesReceived += Math.Max(0, bytesReceived);
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateNetwork(double latencyMs, double throughputMbps)
    {
        lock (_sync)
        {
            _latencyMs = Math.Max(0, latencyMs);
            _throughputMbps = Math.Max(0, throughputMbps);
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateSessions(int tcpSessions, int udpSessions)
    {
        lock (_sync)
        {
            _tcpSessions = Math.Max(0, tcpSessions);
            _udpSessions = Math.Max(0, udpSessions);
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RegisterDnsFailure()
    {
        lock (_sync)
        {
            _dnsFailures++;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void RegisterRecoveryAttempt(bool success)
    {
        lock (_sync)
        {
            _recoveryAttempts++;

            if (success)
                _successfulRecoveries++;

            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Touch()
    {
        lock (_sync)
            _lastActivityUtc = DateTimeOffset.UtcNow;
    }

    public LEMetricsDeviceMetricsSnapshot Snapshot()
    {
        lock (_sync)
        {
            bool healthy =
                _state != LECoreStates.Failed &&
                _state != LECoreStates.Degraded &&
                _latencyMs < 250;

            return new LEMetricsDeviceMetricsSnapshot(
                Serial,
                _state,
                _internetActive,
                _bytesSent,
                _bytesReceived,
                _latencyMs,
                _throughputMbps,
                _tcpSessions,
                _udpSessions,
                _dnsFailures,
                _recoveryAttempts,
                _successfulRecoveries,
                healthy,
                _lastActivityUtc);
        }
    }
}

public sealed record LEMetricsDeviceMetricsSnapshot(
    string Serial,
    LECoreStates State,
    bool InternetActive,
    long BytesSent,
    long BytesReceived,
    double LatencyMs,
    double ThroughputMbps,
    int TcpSessions,
    int UdpSessions,
    int DnsFailures,
    int RecoveryAttempts,
    int SuccessfulRecoveries,
    bool Healthy,
    DateTimeOffset LastActivityUtc);

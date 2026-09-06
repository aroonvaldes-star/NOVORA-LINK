using System;
using System.Collections.Concurrent;
using System.Linq;

namespace NOVORA.LinkEngine.Transport;

public sealed class PortTransportLE
{
    public const int DefaultStartPortLE = 27183;
    public const int DefaultEndPortLE = 27283;

    private readonly ConcurrentDictionary<int, string> _reservations =
        new();

    private readonly int _startPort;
    private readonly int _endPort;

    public PortTransportLE(
        int startPort = DefaultStartPortLE,
        int endPort = DefaultEndPortLE)
    {
        if (startPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(startPort));

        if (endPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(endPort));

        if (endPort < startPort)
        {
            throw new ArgumentException(
                "El puerto final debe ser mayor o igual al puerto inicial.");
        }

        _startPort = startPort;
        _endPort = endPort;
    }

    public int Reserve(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var existing =
            _reservations.FirstOrDefault(
                item => string.Equals(
                    item.Value,
                    serial,
                    StringComparison.OrdinalIgnoreCase));

        if (existing.Key != 0)
            return existing.Key;

        for (int port = _startPort;
             port <= _endPort;
             port++)
        {
            if (_reservations.TryAdd(port, serial))
                return port;
        }

        throw new InvalidOperationException(
            $"PortTransportLE no encontró puertos libres entre {_startPort} y {_endPort}.");
    }

    public void Release(int port)
    {
        if (port > 0)
            _reservations.TryRemove(port, out _);
    }

    public void ReleaseBySerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return;

        foreach (var item in _reservations.ToArray())
        {
            if (string.Equals(
                    item.Value,
                    serial,
                    StringComparison.OrdinalIgnoreCase))
            {
                _reservations.TryRemove(item.Key, out _);
            }
        }
    }

    public void Clear()
    {
        _reservations.Clear();
    }
}

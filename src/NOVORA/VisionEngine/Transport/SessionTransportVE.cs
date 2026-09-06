using System.Net.Sockets;

namespace NOVORA.VisionEngine.Transport;

/// <summary>
/// Conjunto de sockets de una sesión VisionEngine.
/// El orden sigue scrcpy 4.1: video, audio y control; el device-name se lee
/// únicamente del primer socket habilitado.
/// </summary>
public sealed class SessionTransportVE : IAsyncDisposable
{
    private bool _disposed;

    internal SessionTransportVE(
        TunnelTransportVE tunnel,
        TcpClient? videoClient,
        TcpClient? audioClient,
        TcpClient? controlClient,
        string deviceName)
    {
        Tunnel = tunnel;
        VideoClientVE = videoClient;
        AudioClientVE = audioClient;
        ControlClientVE = controlClient;
        DeviceName = deviceName;
        ConnectedAtUtc = DateTimeOffset.UtcNow;

        VideoStream = videoClient?.GetStream();
        AudioStream = audioClient?.GetStream();
        ControlStream = controlClient?.GetStream();
    }

    public TunnelTransportVE Tunnel { get; }

    public string DeviceName { get; }

    public DateTimeOffset ConnectedAtUtc { get; }

    public NetworkStream? VideoStream { get; }

    public NetworkStream? AudioStream { get; }

    public NetworkStream? ControlStream { get; }

    public bool HasVideoVE => VideoStream is not null;

    public bool HasAudioVE => AudioStream is not null;

    public bool HasControlVE => ControlStream is not null;

    internal TcpClient? VideoClientVE { get; }

    internal TcpClient? AudioClientVE { get; }

    internal TcpClient? ControlClientVE { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        DisposeClientVE(ControlStream, ControlClientVE);
        DisposeClientVE(AudioStream, AudioClientVE);
        DisposeClientVE(VideoStream, VideoClientVE);

        return ValueTask.CompletedTask;
    }

    private static void DisposeClientVE(
        NetworkStream? stream,
        TcpClient? client)
    {
        try
        {
            stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            client?.Dispose();
        }
        catch
        {
        }
    }
}

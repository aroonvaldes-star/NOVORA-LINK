using System.Net.Sockets;

namespace NOVORA.VisionEngine.Transport;

/// <summary>
/// Reserva del túnel ADB para una sesión de VisionEngine.
/// En Reverse conserva el listener abierto antes de iniciar el servidor Android,
/// eliminando la carrera que existiría si se abriera después.
/// </summary>
public sealed class VETransportTunnel : IAsyncDisposable
{
    private bool _disposed;

    internal VETransportTunnel(
        string serial,
        int scid,
        string socketName,
        int localPort,
        VETransportMode mode,
        TcpListener? reverseListener)
    {
        Serial = serial;
        Scid = scid;
        SocketName = socketName;
        LocalPort = localPort;
        Mode = mode;
        ReverseListenerVE = reverseListener;
    }

    public string Serial { get; }

    public int Scid { get; }

    public string SocketName { get; }

    public int LocalPort { get; }

    public VETransportMode Mode { get; }

    internal TcpListener? ReverseListenerVE { get; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            ReverseListenerVE?.Stop();
        }
        catch
        {
            // El cierre del listener no debe ocultar el resultado de Stop de VE.
        }

        return ValueTask.CompletedTask;
    }
}

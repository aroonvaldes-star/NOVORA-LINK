using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Recovery;
using NOVORA.LinkEngine.Runtime;

namespace NOVORA.LinkEngine.Metrics;

/// <summary>One translation from internal session facts to the connection summary.</summary>
public sealed record LESessionPresentation(string State, string Phone, string Link, string Recovery)
{
    public static LESessionPresentation FromRuntime(LERuntimeSession session)
    {
        string transport = session.ConnectionType switch
        {
            LEDeviceConnection.Usb => "USB",
            LEDeviceConnection.Wifi => "LAN",
            _ => "Sin transporte"
        };
        bool ready = session.State == LERuntimeState.Running && session.SessionHealthy && session.ClientConnected && session.HandshakeVerified;
        string state = ready ? "Conectada" : session.State switch
        {
            LERuntimeState.Stopped => "Detenida",
            LERuntimeState.Stopping => "Deteniendo…",
            LERuntimeState.Failed => "Necesita atención",
            LERuntimeState.Degraded => "Reconectando…",
            _ => "Conectando…"
        };
        string recovery = session.RecoveryState switch
        {
            LERecoveryMonitorState.RecoveringSocket or LERecoveryMonitorState.RecoveringInfrastructure => "Reconectando…",
            LERecoveryMonitorState.WaitingForDevice => "Esperando teléfono",
            LERecoveryMonitorState.Failed => "Necesita atención",
            LERecoveryMonitorState.Degraded => "Revisando…",
            _ => ready ? "Sin incidencias" : "En espera"
        };
        return new(state, session.DeviceOnline ? "Conectado" : "Sin comunicación",
            ready ? transport + " activo" : transport + " · " + state, recovery);
    }
}

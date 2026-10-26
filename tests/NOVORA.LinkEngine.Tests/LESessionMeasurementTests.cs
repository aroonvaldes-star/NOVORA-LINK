using NOVORA.LinkEngine.Core;
using NOVORA.LinkEngine.Device;
using NOVORA.LinkEngine.Metrics;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Runtime;
using NOVORA;

internal static class LESessionMeasurementTests
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var now = DateTimeOffset.UtcNow;
        var data = new LESessionMeasurements();
        data.Begin("USB-A", "USB", now);
        Check(data.UploadMbps is null && data.Internet is null, "Una sesión nueva debe empezar sin mediciones.");
        Check(!data.Observe("USB-A", new(now.AddSeconds(-1), 500, 500, 1, 1), now), "No se aceptan contadores anteriores a la sesión.");
        data.Observe("USB-A", new(now, 1000, 2000, 1, 1), now);
        Check(!data.Observe("USB-A", new(now.AddMilliseconds(50), 126000, 252000, 2, 2), now.AddMilliseconds(50)),
            "Los contadores demasiado seguidos deben conservar la línea base y no publicar una tasa inestable.");
        data.Observe("USB-A", new(now.AddSeconds(1), 126000, 252000, 2, 2), now.AddSeconds(1));
        Check(data.UploadMbps == 1 && data.DownloadMbps == 2 && data.UploadedBytes == 125000 && data.DownloadedBytes == 250000,
            "Las tasas deben calcularse con deltas de bytes y tiempo de la misma sesión.");
        data.SetTunnel("USB-A", new(now, "USB", 10, 5, 20, 1, "test-A"));
        Check(!data.Begin("USB-A", "USB", now.AddSeconds(1)) && data.Tunnel is not null, "Un heartbeat no debe reiniciar la sesión.");
        data.Observe("USB-A", new(now.AddSeconds(2), 1, 1, 0, 0), now.AddSeconds(2));
        Check(data.UploadMbps is null && data.UploadedBytes == 125000, "Reiniciar el relay no debe crear tráfico negativo ni perder el total observado.");
        data.Begin("LAN-B", "LAN", now.AddSeconds(3));
        Check(data.Tunnel is null && data.UploadedBytes == 0 && data.DownloadMbps is null, "Cambiar de transporte debe liberar los resultados y contadores anteriores.");
        Check(!data.SetTunnel("USB-A", new(now.AddSeconds(4), "USB", 10, 5, 20, 1, "late")), "Un resultado tardío no pertenece a la sesión nueva.");
        data.Observe("LAN-B", new(now.AddSeconds(3), 0, 0, 0, 0), now.AddSeconds(3));
        data.Observe("LAN-B", new(now.AddSeconds(4), 0, 0, 0, 0), now.AddSeconds(4));
        Check(data.UploadMbps == 0, "Cero medido es distinto de sin medir.");
        data.Observe("LAN-B", new(now.AddSeconds(4), 0, 0, 0, 0), now.AddSeconds(8));
        Check(data.UploadMbps is null, "No se deben mostrar tasas antiguas si deja de llegar telemetría.");
        data.End();
        Check(data.SessionKey is null && data.Internet is null && data.UploadedBytes == 0, "Cerrar la sesión debe liberar todos sus datos.");
        Check(!data.SetInternet("LAN-B", new(now, "LAN", 1, 1, 1, 1, "test", "late")), "No se aceptan resultados tras cerrar la sesión.");

        Check(!NLScrollWheelRouter.TryGetNextVerticalOffset(0, 100, 120, out _),
            "En el inicio, el scroll hacia arriba debe propagarse y no quedarse atrapado.");
        Check(NLScrollWheelRouter.TryGetNextVerticalOffset(50, 100, -120, out double nextOffset) && nextOffset > 50,
            "Dentro de la página, el touchpad debe desplazar el panel verticalmente.");
        Check(!NLScrollWheelRouter.TryGetNextVerticalOffset(100, 100, -120, out _),
            "Al final, el scroll hacia abajo debe propagarse y no quedar bloqueado.");

        var metrics = new LEMetricsDevice("device");
        var initial = metrics.Snapshot();
        Check(initial.LatencyMs is null && initial.TcpSessions is null && initial.DnsFailures is null && !initial.TrafficMeasured && !initial.Healthy,
            "Las métricas sin productor no pueden aparentar ceros medidos ni buena salud.");
        metrics.SetState(LECoreStates.Online);
        metrics.SetInternetActive(true);
        metrics.UpdateNetwork(0, 0);
        Check(metrics.Snapshot().Healthy && metrics.Snapshot().LatencyMs == 0, "Una medición cero válida debe conservarse.");
        bool invalidRejected = false;
        try { metrics.UpdateNetwork(double.NaN, 0); } catch (ArgumentOutOfRangeException) { invalidRejected = true; }
        Check(invalidRejected, "No se deben aceptar mediciones NaN.");

        var runtime = LERuntimeSession.CreateInitialLE("device") with { ConnectionType = LEDeviceConnection.Wifi };
        var summary = LESessionPresentation.FromRuntime(runtime);
        Check(summary.State == "Detenida" && summary.Link.Contains("LAN") && !summary.Link.Contains("USB"), "La presentación debe respetar el transporte y estado real.");
        summary = LESessionPresentation.FromRuntime(runtime with { State = LERuntimeState.Running, SessionHealthy = true, ClientConnected = true, HandshakeVerified = true });
        Check(summary.State == "Conectada" && summary.Link == "LAN activo", "Una sesión LAN lista debe mostrarse conectada.");
        Console.WriteLine("PASS: LE session isolation, bounded retention, counter reset, missing measurements and connection presentation.");
    }
}

using System.Windows.Threading;

namespace NOVORA.VisionEngine.Renderer;

public sealed record SurfaceRendererVE(
    IntPtr Handle,
    Dispatcher Dispatcher)
{
    public static SurfaceRendererVE FromHostVE(HostRendererVE host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new SurfaceRendererVE(host.HandleVE, host.DispatcherVE);
    }
}

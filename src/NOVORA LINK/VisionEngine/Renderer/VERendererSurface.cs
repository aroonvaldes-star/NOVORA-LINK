using System.Windows.Threading;

namespace NOVORA.VisionEngine.Renderer;

public sealed record VERendererSurface(
    IntPtr Handle,
    Dispatcher Dispatcher)
{
    public static VERendererSurface FromHostVE(VERendererHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new VERendererSurface(host.HandleVE, host.DispatcherVE);
    }
}

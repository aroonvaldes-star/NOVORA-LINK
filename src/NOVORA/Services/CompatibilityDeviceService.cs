using NOVORA.LinkEngine.Device;
using NOVORA.Models;

namespace NOVORA.Services;

/// <summary>
/// Entrada pública de la detección de compatibilidad.
///
/// ResolverDeviceLE decide automáticamente entre:
///
/// - escaneo completo;
/// - perfil técnico reutilizado por modelo/build.
///
/// No crea polling permanente.
/// </summary>
public sealed class CompatibilityDeviceService
{
    private readonly ResolverDeviceLE _resolver;

    public CompatibilityDeviceService(
        AdbService adb)
    {
        ArgumentNullException.ThrowIfNull(
            adb);

        _resolver =
            new ResolverDeviceLE(
                adb);
    }

    public async Task<CompatibilityProfileDevice> DetectAsync(
        string serial,
        DeviceCapabilities displayCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        displayCapabilities ??=
            DeviceCapabilities.Unknown;

        ResultResolverDeviceLE result =
            await _resolver.ResolveAsync(
                    serial,
                    displayCapabilities,
                    cancellationToken)
                .ConfigureAwait(false);

        return result.Profile;
    }
}

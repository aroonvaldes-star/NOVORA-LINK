using NOVORA.LinkEngine.Device;
using NOVORA.Model;

namespace NOVORA.Service;

/// <summary>
/// Entrada pública de la detección de compatibilidad.
///
/// LEDeviceResolver decide automáticamente entre:
///
/// - escaneo completo;
/// - perfil técnico reutilizado por modelo/build.
///
/// No crea polling permanente.
/// </summary>
public sealed class NLServiceCompatibilityDevice
{
    private readonly LEDeviceResolver _resolver;

    public NLServiceCompatibilityDevice(
        NLServiceADB adb)
    {
        ArgumentNullException.ThrowIfNull(
            adb);

        _resolver =
            new LEDeviceResolver(
                adb);
    }

    public async Task<NLModelCompatibilityProfileDevice> DetectAsync(
        string serial,
        NLModelDeviceCapabilities displayCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        displayCapabilities ??=
            NLModelDeviceCapabilities.Unknown;

        LEDeviceResultResolver result =
            await _resolver.ResolveAsync(
                    serial,
                    displayCapabilities,
                    cancellationToken)
                .ConfigureAwait(false);

        return result.Profile;
    }
}

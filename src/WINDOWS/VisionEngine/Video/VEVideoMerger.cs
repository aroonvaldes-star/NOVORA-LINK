namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Conserva el último paquete de configuración H.264/H.265 y lo antepone
/// al siguiente paquete multimedia del flujo de App Control.
/// </summary>
public sealed class VEVideoMerger
{
    private byte[]? _configurationVE;
    private byte[]? _latestConfigurationVE;

    public bool HasConfigurationVE
        => _configurationVE is not null;

    public VEVideoPacket? MergeVE(VEVideoPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.IsConfiguration)
        {
            _configurationVE = packet.PayloadVE.ToArray();
            _latestConfigurationVE = _configurationVE;
            return null;
        }

        if (_configurationVE is null)
        {
            return packet;
        }

        byte[] configuration = _configurationVE;
        _configurationVE = null;

        byte[] merged =
            new byte[configuration.Length + packet.LengthVE];

        Buffer.BlockCopy(
            configuration,
            0,
            merged,
            0,
            configuration.Length);

        Buffer.BlockCopy(
            packet.Data,
            0,
            merged,
            configuration.Length,
            packet.LengthVE);

        // A merged packet owns an exact array, not the pooled lease. Creating
        // a new packet instead of `with` prevents copying the lease reference
        // and returning the same buffer twice.
        return new VEVideoPacket(
            PresentationTimeUs: packet.PresentationTimeUs,
            IsConfiguration: false,
            IsKeyFrame: packet.IsKeyFrame,
            Data: merged,
            ReceivedTimestamp: packet.ReceivedTimestamp);
    }

    public void ReplayConfigurationVE() => _configurationVE = _latestConfigurationVE;

    public void ResetVE()
    {
        _configurationVE = null;
        _latestConfigurationVE = null;
    }
}

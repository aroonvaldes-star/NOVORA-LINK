namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Conserva el último paquete de configuración H.264/H.265 y lo antepone
/// al siguiente paquete multimedia, igual que packet_merger de scrcpy 4.1.
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
            _configurationVE = packet.Data.ToArray();
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
            new byte[configuration.Length + packet.Data.Length];

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
            packet.Data.Length);

        return packet with
        {
            Data = merged,
            IsConfiguration = false
        };
    }

    public void ReplayConfigurationVE() => _configurationVE = _latestConfigurationVE;

    public void ResetVE()
    {
        _configurationVE = null;
        _latestConfigurationVE = null;
    }
}

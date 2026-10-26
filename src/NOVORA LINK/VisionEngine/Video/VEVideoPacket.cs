using NOVORA.VisionEngine.Protocol;

namespace NOVORA.VisionEngine.Video;

/// <summary>
/// Paquete codificado recibido desde MediaCodec a través del protocolo VE.
/// When the packet owns a pooled buffer, dispose it after all consumers have
/// copied or decoded <see cref="PayloadVE"/>.
/// </summary>
public sealed record VEVideoPacket(
    long? PresentationTimeUs,
    bool IsConfiguration,
    bool IsKeyFrame,
    byte[] Data,
    long ReceivedTimestamp = 0) : IDisposable
{
    private VEPooledPayload? _leaseVE;
    private int _lengthVE = -1;

    /// <summary>Number of valid bytes at the start of <see cref="Data"/>.</summary>
    public int LengthVE => _lengthVE >= 0 ? _lengthVE : Data.Length;

    /// <summary>The valid payload slice, excluding pooled-buffer slack.</summary>
    public ReadOnlyMemory<byte> PayloadVE => Data.AsMemory(0, LengthVE);

    internal static VEVideoPacket CreatePooledVE(
        long? presentationTimeUs,
        bool isConfiguration,
        bool isKeyFrame,
        VEPooledPayload lease,
        long receivedTimestamp = 0)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new VEVideoPacket(
            presentationTimeUs,
            isConfiguration,
            isKeyFrame,
            lease.Buffer,
            receivedTimestamp)
        {
            _leaseVE = lease,
            _lengthVE = lease.Length
        };
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _leaseVE, null)?.Dispose();
    }
}

using System.Buffers;

namespace NOVORA.VisionEngine.Protocol;

/// <summary>
/// One rented media buffer. Ownership is explicit: disposing the lease returns
/// the array to the pool exactly once, even if the frame path fails midway.
/// </summary>
public sealed class VEPooledPayload : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private byte[]? _buffer;

    internal VEPooledPayload(byte[] buffer, int length, ArrayPool<byte> pool)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        if (length <= 0 || length > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
    }

    public int Length { get; }

    public byte[] Buffer =>
        _buffer ?? throw new ObjectDisposedException(nameof(VEPooledPayload));

    public ReadOnlyMemory<byte> Memory =>
        Buffer.AsMemory(0, Length);

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null) _pool.Return(buffer);
    }
}

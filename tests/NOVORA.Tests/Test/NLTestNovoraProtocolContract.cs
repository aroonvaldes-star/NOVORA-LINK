using System.Buffers.Binary;
using NOVORA.Control;
using NOVORA.VisionEngine.Protocol;
using NOVORA.VisionEngine.Server;
using Xunit;

namespace NOVORA.Tests.Test;

public sealed class NLTestNovoraProtocolContract
{
    [Fact]
    public void Vision_server_artifacts_are_isolated_per_session()
    {
        string first = VEProtocolConstants.BuildRemoteServerPathVE(0x1234abcd);
        string second = VEProtocolConstants.BuildRemoteServerPathVE(0x4567cdef);

        Assert.Equal("/data/local/tmp/novora-vision-server-1234abcd.jar", first);
        Assert.Equal("/data/local/tmp/novora-vision-server-4567cdef.jar", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Current_vision_backend_reports_nonzero_scrcpy_runtime_dependency()
    {
        VEServerBackendDescriptor backend = VEServerBackendCatalog.Describe(
            VEServerBackend.Scrcpy41Compatibility,
            available: true,
            selected: true,
            active: false);

        Assert.True(backend.Selected);
        Assert.False(backend.Active);
        Assert.Equal("scrcpy-server", backend.RuntimeDependency);
        Assert.Equal("4.1", backend.ProtocolVersion);
    }

    [Fact]
    public async Task Control_protocol_rejects_oversize_and_truncated_frames()
    {
        byte[] oversize = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(oversize, NLControlProtocol.MaxFrame + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NLControlProtocol.ReadAsync<NLControlRequest>(new MemoryStream(oversize), default));

        byte[] truncated = new byte[6];
        BinaryPrimitives.WriteInt32BigEndian(truncated, 10);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            NLControlProtocol.ReadAsync<NLControlRequest>(new MemoryStream(truncated), default));
    }

    [Fact]
    public async Task Control_protocol_cancellation_is_the_read_timeout_boundary()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NLControlProtocol.ReadAsync<NLControlRequest>(new BlockingReadStream(), timeout.Token));
    }

    [Fact]
    public void Control_commands_reject_version_mismatch_before_dispatch()
    {
        NLControlSnapshot state = new(
            1, "PC", "1.4", "4M", "Gaming", "default", "default", false,
            [], [], []);
        string? error = NLControlCommands.Validate(new NLControlRequest(
            NLControlProtocol.Version + 1, 1, "state"), state);

        Assert.NotNull(error);
        Assert.Contains("Versión", error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

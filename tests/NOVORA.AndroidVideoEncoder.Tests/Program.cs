using Android.Media;
using NOVORA.AndroidVideo;

int passed = 0, failed = 0;
await Test("IDR larger than 512KiB reaches an empty queue", async () =>
{
    await using var encoder = Create();
    MediaCodec.LastCreated.Emit(Nal(700_000, 5), key: true);
    Require(encoder.Packets.TryRead(out var packet) && packet.KeyFrame && packet.Payload.Length == 700_000,
        "A valid 700000-byte IDR was discarded.");
});
await Test("Full backlog is replaced atomically with cached configuration and fresh IDR", async () =>
{
    await using var encoder = Create(); var codec = MediaCodec.LastCreated;
    byte[] config = Nal(40, 7); codec.Emit(config, config: true);
    Require(encoder.Packets.TryRead(out var consumedConfig) && consumedConfig.Configuration, "Initial config missing.");
    encoder.PacketDequeued(consumedConfig!);
    for (int i = 0; i < 8; i++) codec.Emit(Nal(20_000, 1));
    codec.Emit(Nal(21_000, 1));
    codec.Emit(Nal(700_000, 5), key: true);
    var packets = Drain(encoder);
    Require(packets.Count == 2 && packets[0].Configuration && packets[0].Payload.SequenceEqual(config) &&
        packets[1].KeyFrame && packets[1].Payload.Length == 700_000,
        "Recovery must contain only cached config followed by fresh IDR.");
});
await Test("A flagged non-IDR I slice cannot recover a broken H264 chain", async () =>
{
    await using var encoder = Create(); var codec = MediaCodec.LastCreated;
    for (int i = 0; i < 9; i++) codec.Emit(Nal(20_000, 1));
    Drain(encoder); codec.Emit(Nal(10_000, 1), key: true); codec.Emit(Nal(10_000, 1));
    Require(!encoder.Packets.TryRead(out _), "Dependent slices escaped while awaiting IDR.");
    codec.Emit(Nal(12_000, 5), key: true);
    Require(encoder.Packets.TryRead(out var packet) && packet.KeyFrame, "IDR did not recover.");
});
await Test("Unsupported CBR is omitted from encoder options", async () =>
{
    MediaCodec.SupportsCbr = false;
    await using var encoder = Create();
    Require(!MediaCodec.LastCreated.ConfiguredFormats[0].ContainsKey("bitrate-mode"),
        "CBR is configured although capability denies it.");
});
await Test("Rejected optional settings retry with the base format", async () =>
{
    MediaCodec.ConfigureFailure = f => f.ContainsKey("bitrate-mode")
        ? new Java.Lang.IllegalArgumentException("CBR rejected") : null;
    await using var encoder = Create();
    var codec = MediaCodec.LastCreated;
    Require(codec.ConfiguredFormats.Count == 2 && codec.ResetCount == 1 &&
        !codec.ConfiguredFormats[1].ContainsKey("bitrate-mode") &&
        !codec.ConfiguredFormats[1].ContainsKey("max-bframes"), "Fallback retained rejected optional settings.");
});
await Test("Recoverable codec configuration errors use one base retry", async () =>
{
    MediaCodec.ConfigureFailure = f => f.ContainsKey("latency")
        ? new MediaCodec.CodecException("optional config", recoverable: true) : null;
    await using var encoder = Create();
    Require(MediaCodec.LastCreated.ResetCount == 1 &&
        !MediaCodec.LastCreated.ConfiguredFormats[1].ContainsKey("latency"), "Recoverable config did not fall back.");
});
await Test("Fatal and resource codec errors propagate and release construction", () =>
{
    foreach (var failure in new[]
    {
        new MediaCodec.CodecException("fatal"),
        new MediaCodec.CodecException("resources", recoverable: true, errorCode: 1100),
        new MediaCodec.CodecException("reclaimed", recoverable: true, errorCode: 1101),
        new MediaCodec.CodecException("transient", recoverable: true, transient: true)
    })
    {
        MediaCodec.ConfigureFailure = _ => failure;
        try { Create(); throw new Exception("Expected codec failure."); }
        catch (MediaCodec.CodecException observed) { Require(ReferenceEquals(observed, failure), "Codec cause was hidden."); }
        Require(MediaCodec.LastCreated.ResetCount == 0 && MediaCodec.LastCreated.Released &&
            MediaCodec.LastCreated.Disposed, "Fatal codec was retried or leaked.");
    }
    return Task.CompletedTask;
});
await Test("Failure of the base retry propagates the cause and releases codec", () =>
{
    var failure = new Java.Lang.IllegalArgumentException("invalid base geometry");
    MediaCodec.ConfigureFailure = _ => failure;
    try { Create(); throw new Exception("Expected base-format failure."); }
    catch (Java.Lang.IllegalArgumentException observed) { Require(ReferenceEquals(observed, failure), "Base cause lost."); }
    Require(MediaCodec.LastCreated.ConfiguredFormats.Count == 2 && MediaCodec.LastCreated.Released &&
        MediaCodec.LastCreated.Disposed, "Base format failed with a codec leak.");
    return Task.CompletedTask;
});
await Test("Input surface construction failure releases codec", () =>
{
    var failure = new InvalidOperationException("no surface"); MediaCodec.SurfaceFailure = failure;
    try { Create(); throw new Exception("Expected surface failure."); }
    catch (InvalidOperationException observed) { Require(ReferenceEquals(observed, failure), "Surface cause lost."); }
    Require(MediaCodec.LastCreated.Released && MediaCodec.LastCreated.Disposed, "Surface failure leaked codec.");
    return Task.CompletedTask;
});
await Test("Cleanup failure cannot mask the original construction error", () =>
{
    var original = new MediaCodec.CodecException("original configure failure");
    MediaCodec.ConfigureFailure = _ => original; MediaCodec.DisposeFailure = new Exception("cleanup failed");
    try { Create(); throw new Exception("Expected construction failure."); }
    catch (MediaCodec.CodecException observed) { Require(ReferenceEquals(observed, original), "Original failure was hidden."); }
    return Task.CompletedTask;
});
await Test("Delayed PacketDequeued cannot decrement newer queued payloads twice", async () =>
{
    await using var encoder = Create(); var codec = MediaCodec.LastCreated;
    codec.Emit(Nal(240_000, 1)); Require(encoder.Packets.TryRead(out var old), "Initial frame missing.");
    codec.Emit(Nal(200_000, 1)); codec.Emit(Nal(200_000, 1));
    encoder.PacketDequeued(old!); encoder.PacketDequeued(old!);
    codec.Emit(Nal(140_000, 1));
    Require(!encoder.Packets.TryRead(out _), "Old dequeue notifications undercounted new backlog.");
    codec.Emit(Nal(14_000, 5), key: true);
    Require(encoder.Packets.TryRead(out var recovered) && recovered.KeyFrame, "Recovery failed after delayed notification.");
});
await Test("Codec output buffers are released on oversized and stopped callbacks", async () =>
{
    var encoder = Create(); var codec = MediaCodec.LastCreated;
    codec.Emit(Nal(16 * 1024 * 1024 + 1, 5), key: true);
    Require(!encoder.Packets.TryRead(out _) && codec.ReleasedBuffers == 1, "Oversized output was retained.");
    await encoder.DisposeAsync(); codec.Emit(Nal(20, 1));
    Require(codec.ReleasedBuffers == 2 && !encoder.Packets.TryRead(out _), "Stopped callback leaked a buffer.");
});
await Test("Recovery callback bursts are coalesced by the existing keyframe gate", async () =>
{
    await using var encoder = Create(); var codec = MediaCodec.LastCreated;
    for (int i = 0; i < 50; i++) codec.Emit(Nal(100_000, 1));
    Require(codec.SyncRequests == 1 && codec.ReleasedBuffers == 50, "Recovery burst spammed codec sync requests.");
    codec.Emit(Nal(700_000, 5), key: true); codec.Emit(Nal(5_000, 1));
    Require(codec.SyncRequests == 1, "Observing IDR bypassed the one-second request gate.");
});
foreach (var portable in PortableQueueTests.Cases()) await Test(portable.Name, portable.Run);
Console.WriteLine($"RESULT {passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;

NLAndroidVideoEncoder Create() => new(2560, 1440, 20_000_000, 60, ex => throw new InvalidOperationException("Callback failed", ex));
async Task Test(string name, Func<Task> body)
{
    // Preserve per-test setup that deliberately changes this JNI boundary.
    try { await body(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; }
    finally { MediaCodec.ResetBoundary(); }
}
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static byte[] Nal(int bytes, byte type)
{
    var result = new byte[bytes]; result[2] = 1; result[3] = (byte)(0x60 | type); return result;
}
static List<NLAndroidVideoPacket> Drain(NLAndroidVideoEncoder encoder)
{
    List<NLAndroidVideoPacket> result = [];
    while (encoder.Packets.TryRead(out var packet)) { result.Add(packet); encoder.PacketDequeued(packet); }
    return result;
}

using NOVORA.Control;

var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
var frame = new NLFileFrameV2(NLFileFrameKindV2.Data, id, 0x01020304, new byte[] { 9, 8, 7 });
Check(Convert.ToHexString(NLControlFileProtocolV2.EncodeHeader(frame)) ==
    "4E4C4632020000240000000300112233445566778899AABBCCDDEEFF0000000001020304", "Header36BigEndianRfc4122");
using (var wire = new MemoryStream())
{
    await NLControlFileProtocolV2.WriteAsync(wire, frame);
    wire.Position = 0;
    var read = await NLControlFileProtocolV2.ReadAsync(new FragmentedReadStream(wire));
    Check(read?.TransferId == id && read.Offset == 0x01020304 && read.Payload.SequenceEqual(new byte[] { 9, 8, 7 }), "FragmentedHeaderPayload");
}
for (int size = 1; size < 39; size++)
{
    byte[] header = NLControlFileProtocolV2.EncodeHeader(frame);
    byte[] bytes = header.Concat(frame.Payload).Take(size).ToArray();
    if (size < 39) await Reject(async () => { _ = await NLControlFileProtocolV2.ReadAsync(new MemoryStream(bytes)); }, "TruncatedFrame" + size);
}
foreach (int pos in new[] { 0, 4, 5, 6, 8, 28 })
{
    byte[] bad = NLControlFileProtocolV2.EncodeHeader(frame); bad[pos] = 255;
    await Reject(async () => { _ = await NLControlFileProtocolV2.ReadAsync(new MemoryStream(bad)); }, "InvalidHeaderBeforeAllocation" + pos);
}
NLControlFileProtocolV2.ValidateMetadata(new("ok.bin", 2L * 1024 * 1024 * 1024, "Documents/safe"));
await Reject(() => { NLControlFileProtocolV2.ValidateMetadata(new("ok.bin", 2L * 1024 * 1024 * 1024 + 1)); return Task.CompletedTask; }, "TwoGiBInclusiveBoundary");
foreach (var path in new[] { "../escape", "x/../escape", "/absolute", "C:\\root", "x//y", "x/" })
    await Reject(() => { NLControlFileProtocolV2.EncodeMetadata(new("ok.bin", 0, path)); return Task.CompletedTask; }, "RejectUnsafeRelativeDirectory");
foreach (var kind in new[] { NLFileFrameKindV2.Begin, NLFileFrameKindV2.Data, NLFileFrameKindV2.Error })
    await Reject(() => { NLControlFileProtocolV2.EncodeHeader(new(kind, id, 0, new byte[65537])); return Task.CompletedTask; }, "PayloadCaps" + kind);
Check(NLControlFileProtocolV2.ReadCredit(NLControlFileProtocolV2.Ack(id, 0, 1048576)) == 1048576, "BoundedCredit");
if (typeof(Program).Assembly.GetType("NOVORA.Control.NLControlFileTransferV2") is null)
    throw new Exception("FAIL FilesV2TransferEngineExists: no stream transfer engine is available.");
if (typeof(Program).Assembly.GetType("NOVORA.Control.NLControlFileTransferLease") is null)
    throw new Exception("FAIL FilesV2LeaseExists: no FIFO transfer lease is available.");
await CompletionRaceTests.RunAsync(Check);
await CancelSealRaceTests.RunAsync(Check);
await TransferTests.RunAsync(Check);
await DeadlineTests.RunAsync(Check);
if (typeof(Program).Assembly.GetType("NOVORA.Control.NLControlFileStorageV2") is null)
    throw new Exception("FAIL FilesV2StorageExists: no safe Windows temporary-target provider is available.");
if (typeof(Program).Assembly.GetType("NOVORA.Control.NLControlFileLeaseCoordinatorV2") is null)
    throw new Exception("FAIL FilesV2LeaseCoordinatorExists: remote/local requests are not coordinated.");
await IntegrationTests.RunAsync(Check);
Console.WriteLine("ALL FILES v2 CHECKS PASSED");

static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
static async Task Reject(Func<Task> action, string name)
{
    try { await action(); } catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { Check(true, name); return; }
    throw new Exception("FAIL expected rejection: " + name);
}

sealed class FragmentedReadStream(Stream inner) : Stream
{
    public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 2));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], ct);
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

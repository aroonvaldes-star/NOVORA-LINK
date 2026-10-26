using NOVORA.Control;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

if (args.Length != 1) throw new ArgumentException("Supply the existing experimental report directory.");
string output = Path.GetFullPath(args[0]);
if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "benchmark-fixtures"));
string root = Path.Combine(fixtures, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var results = new List<Measurement>();
byte[] payload = new byte[2 * 1024 * 1024]; new Random(20261013).NextBytes(payload);
byte[] expected = SHA256.HashData(payload);
string sourcePath = Path.Combine(root, "payload.bin"); await File.WriteAllBytesAsync(sourcePath, payload);
try
{
    foreach (int rtt in new[] { 0, 20, 50 })
    foreach (string direction in new[] { "PcToAndroid", "AndroidToPc" })
    foreach (int repetition in new[] { 1, 2, 3 })
    foreach (int wire in new[] { 1, 2 })
    {
        string target = Path.Combine(root, $"rtt{rtt}-{direction}-{repetition}-v{wire}"); Directory.CreateDirectory(target);
        Console.WriteLine($"RUN v{wire} {direction} ACK_RTT={rtt}ms rep={repetition}");
        Measurement measurement = await Measure(wire, rtt, direction, repetition, sourcePath, target, expected);
        results.Add(measurement);
        Console.WriteLine($"PASS v{wire} {direction} {rtt}ms rep={repetition}: {measurement.GoodputMiBPerSecond:F3} MiB/s, wire={measurement.TotalWireBytes}, SHA={measurement.IntegrityVerified}");
    }
    await File.WriteAllTextAsync(Path.Combine(output, "files-benchmark.json"), JsonSerializer.Serialize(new
    {
        Description = "Real production FILES v1/v2 senders and disk providers over TCP loopback. ACK response RTT is simulated by ordered per-frame scheduled delivery. Both roles run on Windows; no physical Android or WAN measurement.",
        PayloadBytes = payload.Length, ExpectedSha256 = Convert.ToHexString(expected), WindowV2Bytes = 262144,
        Budget = "Unrestricted in both modes; no pacing", Results = results
    }, new JsonSerializerOptions { WriteIndented = true }));
    var md = new StringBuilder("# FILES v1/v2: benchmark TCP loopback\n\nACK RTT simulado por entrega ordenada de respuestas; 2 MiB por archivo, 3 repeticiones por grupo, mismos bytes y proveedor de disco Windows. Ambos sentidos son roles invertidos en PC; Android/SAF y WAN físicos no fueron medidos. Ambos modos sin límite/pacer. SHA verificado al releer cada archivo publicado.\n\n| Sentido | ACK RTT ms | v1 MiB/s mediana | v2 MiB/s mediana | Cociente v2/v1 | v1 wire B | v2 wire B | v2 vuelo/cola pico B |\n|---|---:|---:|---:|---:|---:|---:|---:|\n");
    foreach (int rtt in new[] { 0, 20, 50 })
    foreach (string direction in new[] { "PcToAndroid", "AndroidToPc" })
    {
        var v1 = results.Where(m => m.WireVersion == 1 && m.AckRttMilliseconds == rtt && m.Direction == direction).ToArray();
        var v2 = results.Where(m => m.WireVersion == 2 && m.AckRttMilliseconds == rtt && m.Direction == direction).ToArray();
        double a = Median(v1.Select(m => m.GoodputMiBPerSecond)), b = Median(v2.Select(m => m.GoodputMiBPerSecond));
        md.AppendLine($"| {direction} | {rtt} | {a:F3} | {b:F3} | {b / a:F2} | {Median(v1.Select(m => (double)m.TotalWireBytes)):F0} | {Median(v2.Select(m => (double)m.TotalWireBytes)):F0} | {v2.Max(m => m.PeakInFlightBytes)}/{v2.Max(m => m.PeakQueuedBytes)} |");
    }
    md.Append("\nEl JSON conserva cada repetición: tiempo, wire/protocolo, allocations y CPU del proceso, SHA esperado/observado y cotas del core. Allocations/CPU son del proceso completo cliente+receptor+simulador durante la transferencia; no son memoria máxima residente ni coste de un dispositivo Android. El retardo configurado se añade a cada respuesta de v1; v2 programa las respuestas independientes en FIFO, permitiendo datos en vuelo. El reloj/planificador del host puede añadir demora. Este escenario comprueba el coste de stop-and-wait frente a ventana bajo ACK retrasado; no acredita menor latencia física, p95 de control bajo carga ni mejora garantizada en una red real.\n");
    await File.WriteAllTextAsync(Path.Combine(output, "files-benchmark.md"), md.ToString());
    Console.WriteLine($"ALL BENCHMARKS PASSED: {results.Count} disk SHA-verified transfers");
}
finally
{
    string resolved = Path.GetFullPath(root);
    if (!resolved.StartsWith(fixtures + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture cleanup escaped benchmark root.");
    Directory.Delete(resolved, recursive: true);
}

static double Median(IEnumerable<double> values) { double[] sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
static async Task<Measurement> Measure(int version, int rtt, string direction, int repetition, string sourcePath, string target, byte[] expected)
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    using var senderClient = new TcpClient { NoDelay = true };
    Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
    await senderClient.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
    using TcpClient receiverClient = await accepted; receiverClient.NoDelay = true; listener.Stop();
    // The accepted and connecting ends keep their PC/Android role across trials; reverse the actual byte flow.
    var pcWire = new CountingStream(senderClient.GetStream());
    var androidWire = new CountingStream(receiverClient.GetStream());
    var senderWire = direction == "PcToAndroid" ? pcWire : androidWire;
    var receiverWire = direction == "PcToAndroid" ? androidWire : pcWire;
    await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
    using var stop = new CancellationTokenSource();
    NLControlFileTransferV2? v2Sender = null, v2Receiver = null;
    AckDelayStream? delayed = null;
    Task? server = null;
    NLControlFileReceiver? v1Receiver = null;
    if (version == 1)
    {
        v1Receiver = new(target);
        server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var request = await NLControlProtocol.ReadAsync<NLControlRequest>(receiverWire, stop.Token);
                string message = await v1Receiver.HandleAsync(request.Action, request.Value, stop.Token);
                if (rtt > 0) await Task.Delay(rtt, stop.Token);
                await NLControlProtocol.WriteAsync(receiverWire, new NLControlReply(1, request.Id, true, message), stop.Token);
            }
        });
    }
    else
    {
        delayed = new(receiverWire, TimeSpan.FromMilliseconds(rtt));
        v2Receiver = new(delayed, new NLControlFileStorageV2(target), 1);
        v2Sender = new(senderWire, null, 1);
        await v2Receiver.StartAsync(); await v2Sender.StartAsync();
    }
    using var process = Process.GetCurrentProcess(); process.Refresh();
    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    TimeSpan cpuBefore = process.TotalProcessorTime;
    var elapsed = Stopwatch.StartNew();
    try
    {
        if (version == 1)
        {
            long requestId = 0;
            await NLControlFileSender.SendAsync(source, "bench.bin", source.Length, async (action, value) =>
            {
                await NLControlProtocol.WriteAsync(senderWire, new NLControlRequest(1, ++requestId, action, value), stop.Token);
                return await NLControlProtocol.ReadAsync<NLControlReply>(senderWire, stop.Token);
            }, ct: stop.Token, relativePath: "Otros/bench.bin");
        }
        else await v2Sender!.SendAsync(Guid.NewGuid(), new("bench.bin", source.Length, "Otros/bench.bin"), source, ct: stop.Token);
        elapsed.Stop(); process.Refresh();
        TimeSpan cpu = process.TotalProcessorTime - cpuBefore;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        string published = Path.Combine(target, "Otros", "bench.bin");
        byte[] observed = SHA256.HashData(await File.ReadAllBytesAsync(published));
        if (!observed.AsSpan().SequenceEqual(expected) || new FileInfo(published).Length != source.Length)
            throw new InvalidDataException("Post-commit disk SHA verification failed.");
        long totalWire = senderWire.WrittenBytes + receiverWire.WrittenBytes;
        return new(version, direction, rtt, repetition, source.Length, totalWire, totalWire - source.Length,
            elapsed.Elapsed.TotalMilliseconds, source.Length / 1048576d / elapsed.Elapsed.TotalSeconds,
            allocated, cpu.TotalMilliseconds, v2Receiver?.PeakQueuedBytes ?? 0, v2Sender?.PeakInFlightBytes ?? 0,
            Convert.ToHexString(expected), Convert.ToHexString(observed), true);
    }
    finally
    {
        stop.Cancel();
        if (v2Sender is not null) await v2Sender.DisposeAsync();
        if (v2Receiver is not null) await v2Receiver.DisposeAsync();
        v1Receiver?.Dispose();
        receiverClient.Dispose(); senderClient.Dispose();
        if (server is not null) { try { await server; } catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { } }
        if (delayed is not null) await delayed.DisposeAsync();
    }
}

sealed record Measurement(int WireVersion, string Direction, int AckRttMilliseconds, int Repetition,
    long UsefulBytes, long TotalWireBytes, long ProtocolBytes, double ElapsedMilliseconds, double GoodputMiBPerSecond,
    long AllocatedBytes, double CpuMilliseconds, long PeakQueuedBytes, long PeakInFlightBytes,
    string ExpectedSha256, string ActualFileSha256, bool IntegrityVerified);

sealed class CountingStream(Stream inner) : Stream
{
    private long _written;
    public long WrittenBytes => Interlocked.Read(ref _written);
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { await inner.WriteAsync(buffer, ct); Interlocked.Add(ref _written, buffer.Length); }
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); Interlocked.Add(ref _written, count); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

sealed class AckDelayStream : Stream
{
    private readonly Stream _inner;
    private readonly TimeSpan _delay;
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly Channel<(byte[] Bytes, long Due)> _queue = Channel.CreateBounded<(byte[], long)>(128);
    private readonly CancellationTokenSource _stop = new();
    private readonly MemoryStream _frame = new();
    private readonly Task _writer;
    public AckDelayStream(Stream inner, TimeSpan delay) { _inner = inner; _delay = delay; _writer = Task.Run(Transmit); }
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _frame.Write(buffer.Span); return ValueTask.CompletedTask; }
    public override Task FlushAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[] bytes = _frame.ToArray(); _frame.SetLength(0);
        long due = _clock.GetTimestamp() + (long)(_delay.TotalSeconds * _clock.TimestampFrequency);
        if (!_queue.Writer.TryWrite((bytes, due))) throw new IOException("Simulated network queue bound exceeded.");
        return Task.CompletedTask;
    }
    private async Task Transmit()
    {
        try
        {
            await foreach (var frame in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                TimeSpan remaining = TimeSpan.FromSeconds((frame.Due - _clock.GetTimestamp()) / (double)_clock.TimestampFrequency);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _stop.Token);
                await _inner.WriteAsync(frame.Bytes, _stop.Token); await _inner.FlushAsync(_stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => _frame.Write(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    { if (disposing) { _stop.Cancel(); _queue.Writer.TryComplete(); _inner.Dispose(); } base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { Dispose(); await _writer; _frame.Dispose(); GC.SuppressFinalize(this); }
}

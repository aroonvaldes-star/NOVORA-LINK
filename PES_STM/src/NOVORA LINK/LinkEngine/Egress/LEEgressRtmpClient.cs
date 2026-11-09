using System.Buffers.Binary;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace NOVORA.LinkEngine.Egress;

/// <summary>
/// Publicador RTMP mínimo (handshake simple, AMF0, chunking). Solo publica:
/// el lector descarta medios entrantes y responde ack/ping.
/// </summary>
internal sealed class LEEgressRtmpClient : IAsyncDisposable
{
    private const int OutChunkLE = 4096;
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _life = new();
    private readonly Dictionary<double, TaskCompletionSource<object?[]>> _pending = new();
    private readonly object _gate = new();
    private TaskCompletionSource<string> _publishStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _reader;
    private uint _streamId;
    private int _inChunk = 128;
    private long _bytesRead, _lastAck;
    private uint _windowAck = 2_500_000;
    private double _transaction;

    public long BytesWritten { get; private set; }
    public Exception? Failure { get; private set; }

    private LEEgressRtmpClient(TcpClient tcp, Stream stream) { _tcp = tcp; _stream = stream; }

    public static async Task<LEEgressRtmpClient> ConnectAsync(LEEgressTarget target, CancellationToken token)
    {
        Uri uri = target.ServerUri;
        bool secure = uri.Scheme == "rtmps";
        int port = uri.IsDefaultPort || uri.Port <= 0 ? secure ? 443 : 1935 : uri.Port;
        var tcp = new TcpClient { NoDelay = true, SendBufferSize = 1 << 20 };
        try
        {
            await tcp.ConnectAsync(uri.Host, port, token).ConfigureAwait(false);
            Stream stream = tcp.GetStream();
            if (secure)
            {
                var ssl = new SslStream(stream, false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.Host }, token).ConfigureAwait(false);
                stream = ssl;
            }
            var client = new LEEgressRtmpClient(tcp, stream);
            await client.HandshakeAsync(token).ConfigureAwait(false);
            client._reader = Task.Run(() => client.ReadLoopAsync(client._life.Token));
            await client.PublishAsync(uri, target.StreamKey.Trim(), token).ConfigureAwait(false);
            return client;
        }
        catch { tcp.Dispose(); throw; }
    }

    private async Task HandshakeAsync(CancellationToken token)
    {
        byte[] c0c1 = new byte[1537];
        c0c1[0] = 3;
        Random.Shared.NextBytes(c0c1.AsSpan(9));
        await _stream.WriteAsync(c0c1, token).ConfigureAwait(false);
        byte[] s0s1 = new byte[1537];
        await _stream.ReadExactlyAsync(s0s1, token).ConfigureAwait(false);
        if (s0s1[0] != 3) throw new IOException("El servidor RTMP respondió una versión no soportada.");
        await _stream.WriteAsync(s0s1.AsMemory(1), token).ConfigureAwait(false);
        byte[] s2 = new byte[1536];
        await _stream.ReadExactlyAsync(s2, token).ConfigureAwait(false);
    }

    private async Task PublishAsync(Uri uri, string key, CancellationToken token)
    {
        string app = uri.AbsolutePath.Trim('/');
        string tcUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        byte[] chunk = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(chunk, OutChunkLE);
        await WriteMessageAsync(2, 1, 0, 0, chunk, token).ConfigureAwait(false);

        await CallAsync("connect", token, new Dictionary<string, object?>
        {
            ["app"] = app, ["type"] = "nonprivate", ["flashVer"] = "FMLE/3.0 (compatible; NOVORA LINK)", ["tcUrl"] = tcUrl
        }).ConfigureAwait(false);
        await SendCommandAsync("releaseStream", NextTransaction(), token, 0u, null, key).ConfigureAwait(false);
        await SendCommandAsync("FCPublish", NextTransaction(), token, 0u, null, key).ConfigureAwait(false);
        var created = await CallAsync("createStream", token, (object?)null).ConfigureAwait(false);
        _streamId = created.Length > 1 && created[1] is double id ? (uint)id : 1;
        lock (_gate) _publishStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendCommandAsync("publish", 0, token, _streamId, null, key, "live").ConfigureAwait(false);
        string code = await _publishStatus.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        if (code != "NetStream.Publish.Start") throw new IOException("El servidor rechazó la publicación: " + code);
    }

    public Task SendMetadataAsync(int width, int height, bool audio, CancellationToken token)
    {
        var meta = new Dictionary<string, object?>
        {
            ["width"] = (double)width, ["height"] = (double)height, ["videocodecid"] = 7d, ["encoder"] = "NOVORA LINK"
        };
        if (audio) { meta["audiocodecid"] = 10d; meta["audiosamplerate"] = 48000d; meta["audiochannels"] = 2d; meta["stereo"] = true; }
        var body = new MemoryStream();
        LEEgressAmf0.Write(body, "@setDataFrame"); LEEgressAmf0.Write(body, "onMetaData"); LEEgressAmf0.WriteEcma(body, meta);
        return WriteMessageAsync(4, 18, _streamId, 0, body.ToArray(), token);
    }

    public Task SendVideoAsync(uint timestampMs, ReadOnlyMemory<byte> flvBody, CancellationToken token) => WriteMessageAsync(6, 9, _streamId, timestampMs, flvBody, token);
    public Task SendAudioAsync(uint timestampMs, ReadOnlyMemory<byte> flvBody, CancellationToken token) => WriteMessageAsync(5, 8, _streamId, timestampMs, flvBody, token);

    private double NextTransaction() { lock (_gate) return ++_transaction; }

    private async Task<object?[]> CallAsync(string name, CancellationToken token, params object?[] args)
    {
        double id = NextTransaction();
        var reply = new TaskCompletionSource<object?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pending[id] = reply;
        await SendCommandAsync(name, id, token, 0, args).ConfigureAwait(false);
        return await reply.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
    }

    private Task SendCommandAsync(string name, double transaction, CancellationToken token, uint? streamId, params object?[] args)
    {
        var body = new MemoryStream();
        LEEgressAmf0.Write(body, name); LEEgressAmf0.Write(body, transaction);
        foreach (var arg in args) LEEgressAmf0.Write(body, arg);
        return WriteMessageAsync(streamId is > 0 ? 4 : 3, 20, streamId ?? 0, 0, body.ToArray(), token);
    }

    private async Task WriteMessageAsync(int csid, byte type, uint streamId, uint timestamp, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        int at = BuildMessage(csid, type, streamId, timestamp, payload.Span, out byte[] frame);
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Failure is not null) throw new IOException("La conexión RTMP se cerró.", Failure);
            await _stream.WriteAsync(frame.AsMemory(0, at), token).ConfigureAwait(false);
            BytesWritten += at;
        }
        finally { _writeGate.Release(); }
    }

    private static int BuildMessage(int csid, byte type, uint streamId, uint timestamp, ReadOnlySpan<byte> payload, out byte[] frame)
    {
        bool extended = timestamp >= 0xFFFFFF;
        int chunks = Math.Max(1, (payload.Length + OutChunkLE - 1) / OutChunkLE);
        frame = new byte[12 + (extended ? 4 : 0) + payload.Length + (chunks - 1) * (1 + (extended ? 4 : 0))];
        var span = frame.AsSpan();
        span[0] = (byte)csid;
        WriteUInt24(span[1..], extended ? 0xFFFFFF : timestamp);
        WriteUInt24(span[4..], (uint)payload.Length);
        span[7] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], streamId);
        int at = 12;
        if (extended) { BinaryPrimitives.WriteUInt32BigEndian(span[at..], timestamp); at += 4; }
        for (int offset = 0; offset < payload.Length; offset += OutChunkLE)
        {
            if (offset > 0)
            {
                span[at++] = (byte)(0xC0 | csid);
                if (extended) { BinaryPrimitives.WriteUInt32BigEndian(span[at..], timestamp); at += 4; }
            }
            int length = Math.Min(OutChunkLE, payload.Length - offset);
            payload.Slice(offset, length).CopyTo(span[at..]); at += length;
        }
        return at;
    }

    private static void WriteUInt24(Span<byte> target, uint value) { target[0] = (byte)(value >> 16); target[1] = (byte)(value >> 8); target[2] = (byte)value; }
    private static uint ReadUInt24(ReadOnlySpan<byte> source) => (uint)(source[0] << 16 | source[1] << 8 | source[2]);

    private sealed class InboundLE { public uint Timestamp, Delta, Length, StreamId; public byte Type; public MemoryStream? Body; }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var states = new Dictionary<int, InboundLE>();
        byte[] header = new byte[18];
        try
        {
            while (!token.IsCancellationRequested)
            {
                await ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
                int fmt = header[0] >> 6, csid = header[0] & 0x3F;
                if (csid == 0) { await ReadAsync(header.AsMemory(1, 1), token).ConfigureAwait(false); csid = 64 + header[1]; }
                else if (csid == 1) { await ReadAsync(header.AsMemory(1, 2), token).ConfigureAwait(false); csid = 64 + header[1] + header[2] * 256; }
                if (!states.TryGetValue(csid, out var state)) states[csid] = state = new();
                int size = fmt switch { 0 => 11, 1 => 7, 2 => 3, _ => 0 };
                if (size > 0)
                {
                    await ReadAsync(header.AsMemory(0, size), token).ConfigureAwait(false);
                    uint ts = ReadUInt24(header);
                    if (size >= 7) { state.Length = ReadUInt24(header.AsSpan(3)); state.Type = header[6]; }
                    if (size == 11) state.StreamId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(7));
                    if (ts == 0xFFFFFF) { await ReadAsync(header.AsMemory(0, 4), token).ConfigureAwait(false); ts = BinaryPrimitives.ReadUInt32BigEndian(header); }
                    if (fmt == 0) state.Timestamp = ts; else { state.Delta = ts; state.Timestamp += ts; }
                }
                if (state.Length > 16 * 1024 * 1024) throw new InvalidDataException("Mensaje RTMP entrante demasiado grande.");
                state.Body ??= new MemoryStream((int)state.Length);
                int take = (int)Math.Min(_inChunk, state.Length - state.Body.Length);
                byte[] part = new byte[take];
                await ReadAsync(part, token).ConfigureAwait(false);
                state.Body.Write(part);
                if (state.Body.Length < state.Length) continue;
                byte[] message = state.Body.ToArray(); state.Body = null;
                await HandleMessageAsync(state.Type, message, token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Failure = ex;
            lock (_gate)
            {
                foreach (var pending in _pending.Values) pending.TrySetException(ex);
                _pending.Clear();
                _publishStatus.TrySetException(ex);
            }
        }
        catch { }
    }

    private async Task ReadAsync(Memory<byte> buffer, CancellationToken token)
    {
        await _stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        _bytesRead += buffer.Length;
        if (_bytesRead - _lastAck >= _windowAck)
        {
            _lastAck = _bytesRead;
            byte[] ack = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(ack, (uint)_bytesRead);
            _ = WriteMessageAsync(2, 3, 0, 0, ack, token);
        }
    }

    private async Task HandleMessageAsync(byte type, byte[] message, CancellationToken token)
    {
        switch (type)
        {
            case 1 when message.Length >= 4: _inChunk = Math.Clamp(BinaryPrimitives.ReadInt32BigEndian(message) & 0x7FFFFFFF, 1, 16 * 1024 * 1024); break;
            case 5 when message.Length >= 4: _windowAck = Math.Max(4096, BinaryPrimitives.ReadUInt32BigEndian(message)); break;
            case 4 when message.Length >= 6 && BinaryPrimitives.ReadUInt16BigEndian(message) == 6:
                byte[] pong = new byte[6]; pong[1] = 7; message.AsSpan(2, 4).CopyTo(pong.AsSpan(2));
                await WriteMessageAsync(2, 4, 0, 0, pong, token).ConfigureAwait(false); break;
            case 20:
                var values = LEEgressAmf0.ReadAll(message);
                if (values.Length < 2 || values[0] is not string name) break;
                if (name is "_result" or "_error" && values[1] is double id)
                {
                    TaskCompletionSource<object?[]>? pending;
                    lock (_gate) { _pending.Remove(id, out pending); }
                    if (name == "_error") pending?.TrySetException(new IOException("El servidor RTMP devolvió un error: " + Describe(values)));
                    else pending?.TrySetResult(values.Skip(2).ToArray());
                }
                else if (name == "onStatus")
                {
                    string code = values.OfType<Dictionary<string, object?>>().Select(o => o.GetValueOrDefault("code") as string).FirstOrDefault(c => c is not null) ?? "desconocido";
                    lock (_gate) _publishStatus.TrySetResult(code);
                    if (code.Contains("Failed") || code.Contains("Unpublish") || code.Contains("BadName"))
                        Failure ??= new IOException("El servidor RTMP cerró la publicación: " + code);
                }
                break;
        }
    }

    private static string Describe(object?[] values) =>
        values.OfType<Dictionary<string, object?>>().Select(o => (o.GetValueOrDefault("description") ?? o.GetValueOrDefault("code")) as string).FirstOrDefault(s => s is not null) ?? "sin detalle";

    public async ValueTask DisposeAsync()
    {
        _life.Cancel();
        try { _stream.Dispose(); } catch { }
        _tcp.Dispose();
        if (_reader is not null) { try { await _reader.ConfigureAwait(false); } catch { } }
        _life.Dispose();
    }
}

/// <summary>Codificador/decodificador AMF0 con los tipos que usa RTMP publish.</summary>
internal static class LEEgressAmf0
{
    public static void Write(Stream output, object? value)
    {
        switch (value)
        {
            case null: output.WriteByte(5); break;
            case double number: output.WriteByte(0); Span<byte> d = stackalloc byte[8]; BinaryPrimitives.WriteDoubleBigEndian(d, number); output.Write(d); break;
            case int number: Write(output, (double)number); break;
            case bool flag: output.WriteByte(1); output.WriteByte(flag ? (byte)1 : (byte)0); break;
            case string text: output.WriteByte(2); WriteKey(output, text); break;
            case Dictionary<string, object?> map: output.WriteByte(3); WriteProperties(output, map); break;
            default: throw new NotSupportedException("Tipo AMF0 no soportado.");
        }
    }

    public static void WriteEcma(Stream output, Dictionary<string, object?> map)
    {
        output.WriteByte(8);
        Span<byte> count = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(count, (uint)map.Count); output.Write(count);
        WriteProperties(output, map);
    }

    private static void WriteProperties(Stream output, Dictionary<string, object?> map)
    {
        foreach (var (key, item) in map) { WriteKey(output, key); Write(output, item); }
        output.Write([0, 0, 9]);
    }

    private static void WriteKey(Stream output, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        output.Write(length); output.Write(bytes);
    }

    public static object?[] ReadAll(byte[] data)
    {
        var values = new List<object?>(); int at = 0;
        try { while (at < data.Length) values.Add(Read(data, ref at)); } catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException) { }
        return values.ToArray();
    }

    private static object? Read(byte[] data, ref int at)
    {
        byte marker = data[at++];
        switch (marker)
        {
            case 0: double number = BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(at)); at += 8; return number;
            case 1: return data[at++] != 0;
            case 2: return ReadKey(data, ref at);
            case 3: return ReadProperties(data, ref at);
            case 5 or 6: return null;
            case 8: at += 4; return ReadProperties(data, ref at);
            case 10:
                int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)); at += 4;
                var items = new object?[Math.Min(count, 1024)];
                for (int i = 0; i < items.Length; i++) items[i] = Read(data, ref at);
                return items;
            case 12: int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)); at += 4; string text = Encoding.UTF8.GetString(data, at, length); at += length; return text;
            default: throw new InvalidDataException("Marcador AMF0 no soportado.");
        }
    }

    private static Dictionary<string, object?> ReadProperties(byte[] data, ref int at)
    {
        var map = new Dictionary<string, object?>();
        while (true)
        {
            string key = ReadKey(data, ref at);
            if (key.Length == 0 && data[at] == 9) { at++; return map; }
            map[key] = Read(data, ref at);
        }
    }

    private static string ReadKey(byte[] data, ref int at)
    {
        int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at)); at += 2;
        string text = Encoding.UTF8.GetString(data, at, length); at += length; return text;
    }
}

/// <summary>Prueba de conexión: handshake + publish y cierre inmediato.</summary>
public static class LEEgressLiveProbe
{
    public static async Task ProbeAsync(LEEgressTarget target, CancellationToken token)
    {
        await using var client = await LEEgressRtmpClient.ConnectAsync(target, token).ConfigureAwait(false);
    }
}

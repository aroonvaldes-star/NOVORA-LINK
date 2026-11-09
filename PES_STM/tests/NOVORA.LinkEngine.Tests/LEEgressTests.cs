using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NOVORA.LinkEngine.Egress;

/// <summary>Publica contra un servidor RTMP mínimo en proceso y codifica AAC con el FFmpeg real.</summary>
internal static class LEEgressTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task RunAsync()
    {
        TestTargetValidation();
        TestAacEncoder();
        await TestRtmpPublishAsync();
    }

    private static void TestTargetValidation()
    {
        Check(new LEEgressTarget("rtmp://live.twitch.tv/app", "k").ServerUri.Port is 1935 or -1, "rtmp:// debe aceptarse.");
        bool rejected = false;
        try { _ = new LEEgressTarget("http://x/app", "k").ServerUri; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Solo se aceptan rtmp:// y rtmps://.");
        rejected = false;
        try { _ = new LEEgressTarget("rtmp://x", "k").ServerUri; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "El servidor necesita aplicación.");
    }

    private static void TestAacEncoder()
    {
        using var encoder = new LEEgressAacEncoder(ToolsDirectory(), 128000);
        var pcm = new byte[48000 * 4];
        for (int i = 0; i < 48000; i++)
        {
            short sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 12000);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4), sample);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4 + 2), sample);
        }
        int packets = 0, bytes = 0;
        for (int offset = 0; offset < pcm.Length; offset += 3840) // 20 ms como VE
            foreach (var packet in encoder.Encode(pcm.AsSpan(offset, Math.Min(3840, pcm.Length - offset))))
            { packets++; bytes += packet.Length; }
        Check(packets >= 44, $"1 s de PCM debe dar ~46 frames AAC (dio {packets}).");
        Check(bytes is > 8_000 and < 40_000, $"Tamaño AAC fuera de rango para 128 kbps ({bytes} B).");
    }

    private static async Task TestRtmpPublishAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(() => FakeServerAsync(listener));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var target = new LEEgressTarget($"rtmp://127.0.0.1:{port}/live", "test-key");
        await LEEgressLiveProbe.ProbeAsync(target, deadline.Token);
        var seen = await server.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();
        Check(seen.Contains("connect") && seen.Contains("createStream") && seen.Contains("publish:test-key"),
            "El cliente debe hacer connect, createStream y publish con la clave: " + string.Join(",", seen));
    }

    private static async Task<List<string>> FakeServerAsync(TcpListener listener)
    {
        var seen = new List<string>();
        using var tcp = await listener.AcceptTcpClientAsync();
        var stream = tcp.GetStream();
        var c0c1 = new byte[1537]; await stream.ReadExactlyAsync(c0c1);
        var s0s1s2 = new byte[1 + 1536 * 2]; s0s1s2[0] = 3; c0c1.AsSpan(1).CopyTo(s0s1s2.AsSpan(1537));
        await stream.WriteAsync(s0s1s2);
        await stream.ReadExactlyAsync(new byte[1536]);
        int chunk = 128;
        var bodies = new Dictionary<int, (byte Type, int Length, uint Stream, MemoryStream Body)>();
        var one = new byte[11];
        try
        {
            while (true)
            {
                await stream.ReadExactlyAsync(one.AsMemory(0, 1));
                int fmt = one[0] >> 6, csid = one[0] & 0x3F;
                if (fmt == 0)
                {
                    await stream.ReadExactlyAsync(one);
                    int length = one[3] << 16 | one[4] << 8 | one[5];
                    bodies[csid] = (one[6], length, BinaryPrimitives.ReadUInt32LittleEndian(one.AsSpan(7)), new MemoryStream());
                }
                else if (fmt != 3) throw new InvalidDataException("El cliente solo debe usar fmt0/fmt3.");
                var state = bodies[csid];
                var part = new byte[Math.Min(chunk, state.Length - (int)state.Body.Length)];
                await stream.ReadExactlyAsync(part);
                state.Body.Write(part);
                if (state.Body.Length < state.Length) continue;
                byte[] message = state.Body.ToArray();
                if (state.Type == 1) { chunk = BinaryPrimitives.ReadInt32BigEndian(message); continue; }
                if (state.Type != 20) continue;
                var values = AmfReadAll(message);
                string name = (string)values[0]!;
                double txn = (double)values[1]!;
                switch (name)
                {
                    case "connect":
                        seen.Add(name);
                        await SendCommandAsync(stream, 0, "_result", txn, Obj(("fmsVer", "FMS/3,0,1,123")), Obj(("code", "NetConnection.Connect.Success")));
                        break;
                    case "createStream":
                        seen.Add(name);
                        await SendCommandAsync(stream, 0, "_result", txn, null, 1d);
                        break;
                    case "publish":
                        seen.Add("publish:" + values[3]);
                        await SendCommandAsync(stream, 1, "onStatus", 0d, null, Obj(("level", "status"), ("code", "NetStream.Publish.Start")));
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException) { }
        return seen;
    }

    private static Dictionary<string, object?> Obj(params (string Key, object? Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    private static async Task SendCommandAsync(Stream stream, uint streamId, string name, double txn, params object?[] args)
    {
        var body = new MemoryStream();
        AmfWrite(body, name); AmfWrite(body, txn);
        foreach (var arg in args) AmfWrite(body, arg);
        byte[] payload = body.ToArray();
        if (payload.Length > 128) throw new InvalidOperationException("El servidor de prueba no fragmenta.");
        var header = new byte[12];
        header[0] = 3;
        header[4] = (byte)(payload.Length >> 16); header[5] = (byte)(payload.Length >> 8); header[6] = (byte)payload.Length;
        header[7] = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), streamId);
        await stream.WriteAsync(header); await stream.WriteAsync(payload);
    }

    private static void AmfWrite(Stream output, object? value)
    {
        switch (value)
        {
            case null: output.WriteByte(5); break;
            case double d: output.WriteByte(0); var b = new byte[8]; BinaryPrimitives.WriteDoubleBigEndian(b, d); output.Write(b); break;
            case string s: output.WriteByte(2); WriteKey(output, s); break;
            case Dictionary<string, object?> map:
                output.WriteByte(3);
                foreach (var (k, v) in map) { WriteKey(output, k); AmfWrite(output, v); }
                output.Write([0, 0, 9]); break;
        }
    }

    private static void WriteKey(Stream output, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        output.WriteByte((byte)(bytes.Length >> 8)); output.WriteByte((byte)bytes.Length); output.Write(bytes);
    }

    private static List<object?> AmfReadAll(byte[] data)
    {
        var values = new List<object?>(); int at = 0;
        while (at < data.Length)
        {
            byte marker = data[at++];
            switch (marker)
            {
                case 0: values.Add(BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(at))); at += 8; break;
                case 2: int n = data[at] << 8 | data[at + 1]; values.Add(System.Text.Encoding.UTF8.GetString(data, at + 2, n)); at += 2 + n; break;
                case 5: values.Add(null); break;
                case 3:
                    while (!(data[at] == 0 && data[at + 1] == 0 && data[at + 2] == 9))
                    {
                        int k = data[at] << 8 | data[at + 1]; at += 2 + k;
                        byte m = data[at++];
                        if (m == 2) { int l = data[at] << 8 | data[at + 1]; at += 2 + l; }
                        else if (m == 0) at += 8; else if (m == 1) at += 1;
                    }
                    at += 3; values.Add(null); break;
                default: return values;
            }
        }
        return values;
    }

    private static string ToolsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "NOVORA LINK", "Tools");
            if (File.Exists(Path.Combine(candidate, "avcodec-62.dll"))) return candidate;
        }
        throw new DirectoryNotFoundException("No se encontró Tools con FFmpeg.");
    }
}

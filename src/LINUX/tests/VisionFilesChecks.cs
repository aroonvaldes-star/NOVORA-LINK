using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using NOVORA.Linux.Files;

internal static class VisionFilesChecks
{
    // The engine wraps the real cause ("FILES channel failed." → inner). The log and UI must show the inner cause.
    private static void DiagnosticsChecks()
    {
        var outer = new IOException("FILES channel failed.", new InvalidDataException("FILES response has a stale id or wrong direction."));
        string text = LinuxFileDiagnostics.Describe(outer);
        if (text != "FILES channel failed. → FILES response has a stale id or wrong direction.")
            throw new Exception("Cause chain not described: " + text);
        if (LinuxFileDiagnostics.Describe(new IOException("same", new IOException("same"))) != "same")
            throw new Exception("Duplicate causes must be collapsed.");
        Console.WriteLine("FILES failure cause chain checks passed.");
    }

    // A receiver without a storage grant must refuse each Begin with a diagnostic. The sender sees the refusal
    // and the channel stays open for the next file (the phone used to close the channel, leaving only EOF on the PC).
    private static async Task RefusedBeginKeepsChannelAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient();
        Task connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient peer = await listener.AcceptTcpClientAsync(ct); await connect; listener.Stop();
        await using var sender = new NLControlFileTransferV2(client.GetStream(), null, 19);
        await using var receiver = new NLControlFileTransferV2(peer.GetStream(), null, 19);
        await sender.StartAsync();
        await receiver.StartAsync();
        byte[] payload = RandomNumberGenerator.GetBytes(4096);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await sender.SendAsync(Guid.NewGuid(), new("refused.bin", payload.Length), new MemoryStream(payload), ct: ct);
                throw new Exception("Begin without a storage grant was accepted");
            }
            catch (NLFileTransferRejectedV2 ex) when (ex.Message.Contains("autorizada")) { }
            if (sender.IsClosed || receiver.IsClosed) throw new Exception("A refused Begin closed the FILES channel");
        }
        Console.WriteLine("FILES refused Begin keeps the channel open checks passed.");
    }

    public static async Task RunAsync()
    {
        DiagnosticsChecks();
        string directory = Path.Combine(Path.GetTempPath(), "novora-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            try { NLControlFileProtocolV2.EncodeMetadata(new("../escape", 1)); throw new Exception("Traversal accepted"); }
            catch (InvalidDataException) { }
            var storage = new NLControlFileStorageV2(directory);
            string outside = Path.Combine(directory, "outside"); Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(directory, "Otros"), outside);
            try { await storage.OpenAsync(Guid.NewGuid(), 1, new("x.bin", 1), timeout.Token); throw new Exception("Symlink accepted"); }
            catch (IOException) { }
            Directory.Delete(Path.Combine(directory, "Otros"));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient();
            Task connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using TcpClient peer = await listener.AcceptTcpClientAsync(timeout.Token); await connect; listener.Stop();
            await using var pc = new LinuxVisionFiles(directory);
            await pc.AttachAsync(client.GetStream(), 17, timeout.Token);
            await using var android = new NLControlFileTransferV2(peer.GetStream(), null, 17);
            await android.StartAsync();
            byte[] payload = RandomNumberGenerator.GetBytes(128_000);
            Guid id = Guid.NewGuid();
            var lease = new NLFileLeaseRequestV2(id,17,NLFileTransferDirectionV2.AndroidToPc);
            var grant = JsonSerializer.Deserialize<NLFileLeaseGrantV2>(pc.Acquire(JsonSerializer.Serialize(lease)))!;
            if (!grant.Granted) throw new Exception("Lease denied");
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.Changed += () => { if (pc.Status.StartsWith("Recibido:")) received.TrySetResult(); };
            await android.SendAsync(id, new("test.bin",payload.Length), new MemoryStream(payload), ct:timeout.Token);
            await received.Task.WaitAsync(timeout.Token);
            byte[] disk = await File.ReadAllBytesAsync(Path.Combine(directory,"Otros","test.bin"), timeout.Token);
            if (!payload.SequenceEqual(disk)) throw new Exception("FILES content mismatch");
            try { pc.Acquire(JsonSerializer.Serialize(lease with { Generation = 16 })); throw new Exception("Stale lease accepted"); }
            catch (InvalidDataException) { }
            // Send a second file of the same name; the first must remain intact.
            id = Guid.NewGuid(); lease = lease with { TransferId = id };
            pc.Acquire(JsonSerializer.Serialize(lease));
            await android.SendAsync(id,new("test.bin",payload.Length),new MemoryStream(payload),ct:timeout.Token);
            if (!File.Exists(Path.Combine(directory,"Otros","test (1).bin"))) throw new Exception("Existing file was overwritten");
            Console.WriteLine("VisionEngine FILES V2 transfer, SHA, generation, traversal, symlink and no-overwrite checks passed.");
            await RefusedBeginKeepsChannelAsync(timeout.Token);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

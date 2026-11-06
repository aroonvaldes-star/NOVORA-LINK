using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using NOVORA.Linux.Files;

internal static class VisionFilesChecks
{
    public static async Task RunAsync()
    {
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
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

using NOVORA.Control;
using NOVORA.VisionEngine.Exchange;
using System.Security.Cryptography;

static class IntegrationTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await DiskPublicationAndCleanup(check);
        await PcWrapperAndRemoteArbitration(check);
        await CommitRechecksReceiveAuthorization(check);
        await PrivacyDeniesRemoteAcquireWithoutClosingChannel(check);
        await CoordinatorExpiryAndIdentity(check);
    }
    private static async Task DiskPublicationAndCleanup(Action<bool, string> check)
    {
        string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "storage-fixtures"));
        string root = Path.Combine(fixtures, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var pair = await TcpPair.CreateAsync();
            await using var receiver = new NLControlFileTransferV2(pair.Right.GetStream(), new NLControlFileStorageV2(root), 5);
            await using var sender = new NLControlFileTransferV2(pair.Left.GetStream(), null, 5);
            await receiver.StartAsync();
            byte[] bytes = Enumerable.Range(0, 786433).Select(i => (byte)(i * 11)).ToArray();
            for (int attempt = 0; attempt < 2; attempt++)
                await sender.SendAsync(Guid.NewGuid(), new("disk.bin", bytes.Length, "Otros/Folder/disk.bin"), new MemoryStream(bytes)).WaitAsync(TimeSpan.FromSeconds(5));
            string[] saved = Directory.GetFiles(Path.Combine(root, "Otros", "Folder"));
            check(saved.Length == 2 && saved.All(path => SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(bytes))), "PostCommitDiskReadbackShaAndNonOverwrite");
            check(!Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "AtomicPublicationLeavesNoPartial");
            var provider = new NLControlFileStorageV2(root);
            var target = await provider.OpenAsync(Guid.NewGuid(), 5, new("abort.bin", 1), default);
            await target.WriteAsync(0, new byte[] { 4 }, default); await target.AbortAsync(default);
            bool rejected = false;
            try { await target.CommitAsync(1, SHA256.HashData(new byte[] { 4 }), default); } catch (IOException) { rejected = true; }
            await target.DisposeAsync();
            check(rejected && !Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any() &&
                !Directory.EnumerateFiles(root, "abort.bin", SearchOption.AllDirectories).Any(), "FilesystemAbortInvalidatesCommitAndCleansPartial");
            foreach (var metadata in new[] { new NLFileMetadataV2("a.bin", 0, "Documentos/a.bin"), new("a.bin", 0, "Otros/not-a.bin"), new("CON.bin", 0) })
            {
                bool unsafeRejected = false;
                try { await provider.OpenAsync(Guid.NewGuid(), 5, metadata, default); } catch (InvalidDataException) { unsafeRejected = true; }
                check(unsafeRejected, "ProviderEnforcesCategoryFilenameAndReservedNames");
            }
        }
        finally { DeleteFixture(root, fixtures); }
    }
    private static async Task PcWrapperAndRemoteArbitration(Action<bool, string> check)
    {
        string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "storage-fixtures"));
        string root = Path.Combine(fixtures, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string selected = Path.Combine(root, "source.bin"); byte[] bytes = new byte[65537]; Random.Shared.NextBytes(bytes); await File.WriteAllBytesAsync(selected, bytes);
            await using var pair = await TcpPair.CreateAsync(); using var coordinator = new NLControlFileLeaseCoordinatorV2(77);
            var androidStore = new MemoryStorage();
            await using var pc = new VEExchangeLanFileV2(pair.Left.GetStream(), 77, coordinator, () => true, receiveRoot: Path.Combine(root, "received"));
            await using var android = new NLControlFileTransferV2(pair.Right.GetStream(), androidStore, 77);
            await android.StartAsync();
            check(await pc.SendPathsAsync(new[] { selected }) == 1 && androidStore.Published.Values.Single().SequenceEqual(bytes), "PcWrapperStreamsSelectedPathWithCentralLease");
            Guid id = Guid.NewGuid(); var request = new NLFileLeaseRequestV2(id, 77, NLFileTransferDirectionV2.AndroidToPc);
            var grant = coordinator.PollRemote(request); check(grant.Granted, "RemoteAcquireGrantedOnEmptyChannel");
            var localQueued = coordinator.AcquireLocalAsync(Guid.NewGuid());
            var result = await android.SendAsync(id, new("remote.bin", bytes.Length), new MemoryStream(bytes)).WaitAsync(TimeSpan.FromSeconds(5));
            check(result.Sha256.SequenceEqual(SHA256.HashData(bytes)) && File.ReadAllBytes(Path.Combine(root, "received", "Otros", "remote.bin")).SequenceEqual(bytes), "AndroidToPcUsesAuthorizedSharedEndpointAndDiskCommit");
            check(localQueued.IsCompletedSuccessfully, "TerminalSentReleasesRemoteLeaseForNextLocalRequest");
            coordinator.CancelLocalBeforeBegin((await localQueued).TransferId);
            Guid wrong = Guid.NewGuid();
            var invalid = android.SendAsync(wrong, new("ungranted.bin", 0), new MemoryStream());
            try { await invalid.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Unleased Begin was accepted."); }
            catch (IOException) { check(true, "IncomingBeginRequiresCurrentPcLease"); }
        }
        finally { DeleteFixture(root, fixtures); }
    }
    private static async Task CoordinatorExpiryAndIdentity(Action<bool, string> check)
    {
        var clock = new ManualTime(); using var coordinator = new NLControlFileLeaseCoordinatorV2(23, clock);
        var local = await coordinator.AcquireLocalAsync(Guid.NewGuid()); coordinator.ConfirmLocalBegin(local.TransferId);
        var remote = new NLFileLeaseRequestV2(Guid.NewGuid(), 23, NLFileTransferDirectionV2.AndroidToPc);
        check(!coordinator.PollRemote(remote).Granted && !coordinator.PollRemote(remote).Granted, "RemoteRepeatedPollRetainsSingleFifoPosition");
        var next = coordinator.AcquireLocalAsync(Guid.NewGuid());
        clock.Advance(TimeSpan.FromSeconds(31));
        check(coordinator.Active == local && !next.IsCompleted, "AbandonedRemotePollDoesNotPreemptActive");
        coordinator.NotifyTerminal(local.TransferId, NLFileFrameKindV2.Complete);
        check(next.IsCompletedSuccessfully && (await next).Direction == NLFileTransferDirectionV2.PcToAndroid, "AbandonedRemoteQueueExpiresBeforeGrant");
        check(!coordinator.ConfirmIncomingBegin(remote.TransferId, 22), "StaleCaptureGenerationCannotBegin");
        bool wrongDirection = false;
        try { coordinator.PollRemote(remote with { Direction = NLFileTransferDirectionV2.PcToAndroid }); } catch (InvalidDataException) { wrongDirection = true; }
        check(wrongDirection, "RemoteCannotAcquirePcSendingDirection");
    }
    private static async Task CommitRechecksReceiveAuthorization(Action<bool, string> check)
    {
        string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "storage-fixtures"));
        string root = Path.Combine(fixtures, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int authorizationChecks = 0;
            await using var pair = await TcpPair.CreateAsync();
            using var coordinator = new NLControlFileLeaseCoordinatorV2(88);
            var androidStore = new MemoryStorage();
            await using var pc = new VEExchangeLanFileV2(pair.Left.GetStream(), 88, coordinator,
                () => Interlocked.Increment(ref authorizationChecks) == 1, receiveRoot: Path.Combine(root, "received"));
            await using var android = new NLControlFileTransferV2(pair.Right.GetStream(), androidStore, 88);
            await android.StartAsync();
            Guid id = Guid.NewGuid();
            check(coordinator.PollRemote(new(id, 88, NLFileTransferDirectionV2.AndroidToPc)).Granted,
                "ReceiveLeaseGrantedBeforePolicyChange");
            bool rejected = false;
            try
            {
                await android.SendAsync(id, new("policy-change.bin", 3), new MemoryStream([1, 2, 3]))
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (IOException) { rejected = true; }
            check(rejected && !File.Exists(Path.Combine(root, "received", "Otros", "policy-change.bin")),
                "ReceivePolicyRecheckedBeforeCommitPublication");
        }
        finally { DeleteFixture(root, fixtures); }
    }
    private static async Task PrivacyDeniesRemoteAcquireWithoutClosingChannel(Action<bool, string> check)
    {
        string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "storage-fixtures"));
        string root = Path.Combine(fixtures, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool receiveAllowed = false;
            int allowedChecks = int.MaxValue;
            await using var pair = await TcpPair.CreateAsync();
            using var coordinator = new NLControlFileLeaseCoordinatorV2(89);
            var androidStore = new MemoryStorage();
            await using var pc = new VEExchangeLanFileV2(pair.Left.GetStream(), 89, coordinator,
                () => Volatile.Read(ref receiveAllowed) && Interlocked.Decrement(ref allowedChecks) >= 0,
                receiveRoot: Path.Combine(root, "received"));
            await using var android = new NLControlFileTransferV2(pair.Right.GetStream(), androidStore, 89);
            await android.StartAsync();
            var blocked = new NLFileLeaseRequestV2(Guid.NewGuid(), 89, NLFileTransferDirectionV2.AndroidToPc);
            check(pc.PollRemote(blocked) is null, "PrivacyDeniesRemoteAcquire");
            string selected = Path.Combine(root, "while-blocked.bin");
            await File.WriteAllBytesAsync(selected, [7, 8, 9]);
            check(await pc.SendPathsAsync(new[] { selected }).WaitAsync(TimeSpan.FromSeconds(5)) == 1 && !pc.Completion.IsCompleted,
                "DeniedAcquireLeavesChannelAndLeaseFree");
            Volatile.Write(ref receiveAllowed, true);
            Guid id = Guid.NewGuid();
            check(pc.PollRemote(new(id, 89, NLFileTransferDirectionV2.AndroidToPc)) is { Granted: true },
                "RemoteAcquireGrantedAfterPrivacyAllows");
            await android.SendAsync(id, new("allowed.bin", 3), new MemoryStream([1, 2, 3])).WaitAsync(TimeSpan.FromSeconds(5));
            check(File.Exists(Path.Combine(root, "received", "Otros", "allowed.bin")), "ReceiveAllowedAfterPrivacyLifted");
            Guid midId = Guid.NewGuid();
            check(pc.PollRemote(new(midId, 89, NLFileTransferDirectionV2.AndroidToPc)) is { Granted: true }, "MidTransferLeaseGranted");
            // Begin and the first Data block pass; PrivacyVE turns on before the second block.
            Volatile.Write(ref allowedChecks, 2);
            byte[] large = new byte[NLControlFileProtocolV2.ChunkBytes * 4];
            bool stopped = false;
            try { await android.SendAsync(midId, new("mid-transfer.bin", large.Length), new MemoryStream(large)).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (IOException) { stopped = true; }
            // The refusal closes the channel; wait for its receiver cleanup before inspecting the disk.
            if (stopped) await pc.Completion.ContinueWith(_ => { }).WaitAsync(TimeSpan.FromSeconds(5));
            check(stopped && !File.Exists(Path.Combine(root, "received", "Otros", "mid-transfer.bin")) &&
                !Directory.EnumerateFiles(root, "*.partial", SearchOption.AllDirectories).Any(), "PrivacyStopsReceiveMidTransferAndCleansPartial");
        }
        finally { DeleteFixture(root, fixtures); }
    }
    private static void DeleteFixture(string root, string fixtures)
    {
        string resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(fixtures + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture cleanup escaped test root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}

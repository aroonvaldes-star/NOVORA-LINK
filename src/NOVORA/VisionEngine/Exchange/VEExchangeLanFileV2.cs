using NOVORA.Control;
using System.IO;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>Windows FILES v2 endpoint: local paths and authorized Android receives share one PC-arbitrated owner.</summary>
internal sealed class VEExchangeLanFileV2 : IAsyncDisposable
{
    private readonly NLControlFileTransferV2 _engine;
    private readonly NLControlFileLeaseCoordinatorV2 _leases;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;
    internal long Generation => _leases.Generation;
    internal Task Completion => _engine.Completion;
    internal long PeakQueuedBytes => _engine.PeakQueuedBytes;
    internal long PeakInFlightBytes => _engine.PeakInFlightBytes;
    internal event Action<NLFileTransferResultV2>? Received;

    internal VEExchangeLanFileV2(Stream stream, long generation, NLControlFileLeaseCoordinatorV2 coordinator,
        Func<int, CancellationToken, ValueTask>? pacer = null, string? receiveRoot = null)
    {
        if (coordinator.Generation != generation) throw new InvalidDataException("FILES coordinator generation mismatch.");
        _leases = coordinator;
        _engine = new(stream, new NLControlFileStorageV2(receiveRoot ?? NLControlFileStorage.PcRoot), generation,
            pacer: pacer, receiveAuthorization: coordinator.ConfirmIncomingBegin);
        _engine.BeginSending += coordinator.ConfirmLocalBegin;
        _engine.TerminalReceived += Terminal;
        _engine.TerminalSent += Terminal;
        _engine.Received += result => Received?.Invoke(result);
        _ = _engine.StartAsync();
        _ = _engine.Completion.ContinueWith(task => coordinator.CloseGeneration(generation,
            task.Exception?.GetBaseException() ?? new IOException("FILES generation closed.")), TaskScheduler.Default);
    }

    private void Terminal(Guid id, NLFileFrameKindV2 kind) => _leases.NotifyTerminal(id, kind);

    internal async Task<int> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _sendGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int sent = 0;
            foreach (string selected in paths)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(selected);
                RejectLink(path);
                if (File.Exists(path))
                {
                    await SendFileAsync(path, NLControlFileStorage.Category(Path.GetFileName(path)) + "/" + Path.GetFileName(path), lifetime.Token).ConfigureAwait(false);
                    sent++;
                }
                else if (Directory.Exists(path))
                {
                    string rootName = NLControlFileStorage.SafeName(new DirectoryInfo(path).Name);
                    foreach (string file in EnumerateConfinedFiles(path))
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        string relative = Path.GetRelativePath(path, file).Replace('\\', '/');
                        string[] parts = relative.Split('/').Select(NLControlFileStorage.SafeName).ToArray();
                        await SendFileAsync(file, "Otros/" + rootName + "/" + string.Join('/', parts), lifetime.Token).ConfigureAwait(false);
                        sent++;
                    }
                }
                else throw new FileNotFoundException("El archivo seleccionado ya no existe.");
            }
            return sent;
        }
        finally { _sendGate.Release(); }
    }

    private async Task SendFileAsync(string path, string relativePath, CancellationToken ct)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            NLControlFileProtocolV2.ChunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var metadata = new NLFileMetadataV2(NLControlFileStorage.SafeName(Path.GetFileName(path)), source.Length, relativePath);
        NLControlFileStorageV2.GetDirectories(metadata);
        Guid id = Guid.NewGuid();
        await _leases.AcquireLocalAsync(id, ct).ConfigureAwait(false);
        try { await _engine.SendAsync(id, metadata, source, ct: ct).ConfigureAwait(false); }
        catch
        {
            if (_engine.IsClosed) _leases.CloseGeneration(Generation, new IOException("FILES send failed and channel closed."));
            else _leases.CancelLocalBeforeBegin(id);
            throw;
        }
    }

    private static IEnumerable<string> EnumerateConfinedFiles(string root)
    {
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current.Depth > 30) throw new IOException("La carpeta supera la profundidad permitida.");
            foreach (string item in Directory.EnumerateFileSystemEntries(current.Path))
            {
                RejectLink(item);
                if (Directory.Exists(item)) pending.Push((item, current.Depth + 1));
                else yield return item;
            }
        }
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se envían enlaces de archivos o carpetas."); }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _stop.Cancel(); _leases.CloseGeneration(Generation);
        await _engine.DisposeAsync().ConfigureAwait(false);
    }
}

using NOVORA.Control;
using System.IO;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>Streams PC files through the authenticated VE LAN FILES channel.</summary>
internal sealed class VEExchangeLanFileSender : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private long _nextRequestId;
    private bool _disposed;
    private readonly Func<int,CancellationToken,ValueTask>? _pacer;

    internal VEExchangeLanFileSender(Stream stream) : this(stream, TimeSpan.FromSeconds(30)) { }

    internal VEExchangeLanFileSender(Stream stream, TimeSpan requestTimeout) : this(stream,requestTimeout,null) { }
    internal VEExchangeLanFileSender(Stream stream, TimeSpan requestTimeout, Func<int,CancellationToken,ValueTask>? pacer)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead || !stream.CanWrite)
            throw new ArgumentException("El canal FILES no es bidireccional.", nameof(stream));
        if (requestTimeout <= TimeSpan.Zero || requestTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _requestTimeout = requestTimeout;
        _pacer=pacer;
    }

    internal async Task<int> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        cancellationToken = lifetime.Token;
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            int sent = 0;
            foreach (string path in paths)
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(path); }
                catch { continue; }

                if (File.Exists(fullPath))
                {
                    await SendFileAsync(fullPath,
                        NLControlFileStorage.Category(Path.GetFileName(fullPath)) + "/" + Path.GetFileName(fullPath),
                        cancellationToken).ConfigureAwait(false);
                    sent++;
                }
                else if (Directory.Exists(fullPath))
                {
                    string rootName = NLControlFileStorage.SafeName(new DirectoryInfo(fullPath).Name);
                    foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(fullPath, file).Replace('\\', '/');
                        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
                            .Select(NLControlFileStorage.SafeName).ToArray();
                        await SendFileAsync(file, "Otros/" + rootName + "/" + string.Join('/', parts), cancellationToken)
                            .ConfigureAwait(false);
                        sent++;
                    }
                }
            }
            return sent;
        }
        finally { _sendGate.Release(); }
    }

    private async Task SendFileAsync(string path, string relativePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        await using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await NLControlFileSender.SendAsync(source, NLControlFileStorage.SafeName(info.Name), info.Length,
            (action, value) => SendRequestAsync(action, value, cancellationToken),
            ct: cancellationToken, relativePath: relativePath).ConfigureAwait(false);
    }

    private async Task<NLControlReply> SendRequestAsync(string action, string value,
        CancellationToken cancellationToken)
    {
        long id = Interlocked.Increment(ref _nextRequestId);
        var request=new NLControlRequest(NLControlProtocol.Version,id,action,value);
        if(action=="file.chunk" && _pacer is not null)
            await _pacer(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request).Length+4,cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        try
        {
            await NLControlProtocol.WriteAsync(_stream,
                request, deadline.Token)
                .ConfigureAwait(false);
            NLControlReply reply = await NLControlProtocol.ReadAsync<NLControlReply>(_stream, deadline.Token)
                .ConfigureAwait(false);
            if (reply.Version != NLControlProtocol.Version || reply.Id != id)
                throw new InvalidDataException("Respuesta FILES LAN fuera de secuencia.");
            return reply;
        }
        catch (OperationCanceledException ex)
        {
            // A cancelled frame may be partial or have an unread reply. Reusing FILES
            // would confuse the next transfer; closing it also clears Android's partial file.
            _stream.Dispose();
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Android no respondió al canal FILES LAN en el tiempo permitido.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        await _sendGate.WaitAsync().ConfigureAwait(false);
        _sendGate.Dispose();
        _stop.Dispose();
    }
}

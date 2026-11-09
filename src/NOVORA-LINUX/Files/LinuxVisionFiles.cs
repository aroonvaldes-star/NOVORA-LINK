using System.Text.Json;

namespace NOVORA.Linux.Files;

internal sealed class LinuxVisionFiles(string receiveDirectory) : IAsyncDisposable
{
    private NLControlFileTransferV2? _engine;
    private NLControlFileLeaseCoordinatorV2? _leases;
    private CancellationTokenRegistration _cancellation;
    private readonly SemaphoreSlim _send = new(1, 1);
    public long Generation { get; private set; }
    public bool IsConnected => _engine is { IsClosed: false };
    public string Status { get; private set; } = "Archivos: esperando canal autenticado.";
    public event Action? Changed;
    /// <summary>A file or folder finished sending; SHA-256 confirmed by Android for every file.</summary>
    public event Action<string>? Sent;
    /// <summary>Android delivered one verified file (destination, bytes).</summary>
    public event Action<string, long>? ReceivedFile;
    private void Report(string text) { Status = text; Changed?.Invoke(); }

    public Task AttachAsync(Stream stream, long generation, CancellationToken token)
    {
        if (_engine is not null || generation <= 0) throw new InvalidOperationException("Canal FILES inválido o duplicado.");
        Generation = generation;
        var leases = new NLControlFileLeaseCoordinatorV2(generation);
        var engine = new NLControlFileTransferV2(stream, new NLControlFileStorageV2(receiveDirectory), generation,
            receiveAuthorization: leases.ConfirmIncomingBegin);
        _leases = leases; _engine = engine;
        engine.BeginConfirmed += leases.ConfirmLocalBegin;
        engine.TerminalReceived += (id, kind) => leases.NotifyTerminal(id, kind);
        engine.TerminalSent += (id, kind) => leases.NotifyTerminal(id, kind);
        engine.Received += result =>
        {
            Report($"Recibido: {result.Destination} ({result.Length:N0} bytes, SHA-256 verificado).");
            if (result.Destination is { } destination) ReceivedFile?.Invoke(destination, result.Length);
        };
        _cancellation = token.Register(() => { leases.CloseGeneration(generation); stream.Dispose(); });
        _ = ObserveAsync(engine);
        Report("Archivos: canal FILES V2 conectado.");
        return engine.StartAsync();
    }
    private async Task ObserveAsync(NLControlFileTransferV2 engine)
    {
        try { await engine.Completion.ConfigureAwait(false); }
        catch (Exception ex) { Report("Archivos: " + ex.Message); }
    }
    public string Acquire(string json)
    {
        if (!IsConnected) throw new IOException("El canal FILES V2 no está conectado.");
        var request = JsonSerializer.Deserialize<NLFileLeaseRequestV2>(json) ?? throw new InvalidDataException("Solicitud FILES vacía.");
        return JsonSerializer.Serialize(_leases!.PollRemote(request));
    }
    public string CancelLease(string json)
    {
        var request = JsonSerializer.Deserialize<NLFileLeaseRequestV2>(json) ?? throw new InvalidDataException("Solicitud FILES vacía.");
        return JsonSerializer.Serialize(_leases?.CancelRemote(request) == true);
    }
    public async Task SendAsync(string path, CancellationToken token)
    {
        await _send.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!IsConnected) throw new IOException("Inicia VE y espera el canal FILES V2 antes de enviar.");
            if (Directory.Exists(path))
            {
                string full = Path.GetFullPath(path);
                var pending = new Stack<string>(); pending.Push(full);
                while (pending.TryPop(out string? dir))
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se envían enlaces de carpetas.");
                    foreach (string file in Directory.EnumerateFiles(dir))
                        await SendFileAsync(file, "Otros/" + Path.GetFileName(full) + "/" + Path.GetRelativePath(full, file).Replace('\\','/'), token).ConfigureAwait(false);
                    foreach (string child in Directory.EnumerateDirectories(dir)) pending.Push(child);
                }
            }
            else await SendFileAsync(path, null, token).ConfigureAwait(false);
            Sent?.Invoke(path);
        }
        catch(OperationCanceledException) { Report("Envío cancelado."); throw; }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or InvalidDataException) { Report("No se pudo enviar: "+ex.Message); throw; }
        finally { _send.Release(); }
    }
    private async Task SendFileAsync(string path, string? relativePath, CancellationToken token)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se envían enlaces de archivos.");
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        long length=stream.Length;
        Guid id = Guid.NewGuid();
        NLControlFileTransferV2 engine = _engine ?? throw new IOException("FILES cerrado.");
        NLControlFileLeaseCoordinatorV2 leases = _leases ?? throw new IOException("FILES cerrado.");
        await leases.AcquireLocalAsync(id, token).ConfigureAwait(false);
        try
        {
            Report($"Enviando {Path.GetFileName(path)} ({length:N0} bytes)…");
            var progress = new Progress<long>(bytes => Report($"Enviando {Path.GetFileName(path)}: {bytes:N0}/{length:N0} bytes"));
            await engine.SendAsync(id, new NLFileMetadataV2(Path.GetFileName(path), length, relativePath), stream, progress, token).ConfigureAwait(false);
            Report($"Enviado: {Path.GetFileName(path)}; SHA-256 confirmado por Android.");
        }
        finally { leases.CancelLocalBeforeBegin(id); }
    }
    public async ValueTask DisposeAsync()
    {
        _cancellation.Unregister();
        _leases?.CloseGeneration(Generation);
        if (_engine is not null) await _engine.DisposeAsync().ConfigureAwait(false);
        _leases?.Dispose();
        _engine = null;
    }
}

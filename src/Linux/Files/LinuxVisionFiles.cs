using System.Text.Json;
using NOVORA.Linux.VisionEngine;

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
    /// <summary>Full cause of the last channel failure (null while healthy); shown in Integración and the VE title.</summary>
    public string? LastError { get; private set; }
    /// <summary>True once the channel existed and then closed: the only way back is a new capture session.</summary>
    public bool ClosedAfterFailure => _engine is not null && !IsConnected;
    public event Action? Changed;
    /// <summary>A file or folder finished sending; SHA-256 confirmed by Android for every file.</summary>
    public event Action<string>? Sent;
    /// <summary>Android delivered one verified file (destination, bytes).</summary>
    public event Action<string, long>? ReceivedFile;
    private void Report(string text) { Status = text; Changed?.Invoke(); }

    /// <summary>Records the real cause (inner chain) in the log and in the status shown to the user.</summary>
    private void Fail(string context, Exception ex)
    {
        LinuxFileDiagnostics.Record(context, ex);
        LastError = LinuxFileDiagnostics.Describe(ex);
        Report($"Archivos: {context}. Causa: {LastError}. Pulsa REINICIAR CAPTURA para reconectar.");
    }

    public Task AttachAsync(Stream stream, long generation, CancellationToken token)
    {
        if (_engine is not null || generation <= 0) throw new InvalidOperationException("Canal FILES inválido o duplicado.");
        Generation = generation;
        LastError = null;
        var leases = new NLControlFileLeaseCoordinatorV2(generation);
        var engine = new NLControlFileTransferV2(stream, new NLControlFileStorageV2(receiveDirectory), generation,
            receiveAuthorization: leases.ConfirmIncomingBegin);
        _leases = leases; _engine = engine;
        engine.BeginConfirmed += leases.ConfirmLocalBegin;
        engine.BeginSending += leases.ConfirmLocalBegin;
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
        catch (Exception ex) { Fail("El canal de archivos se cerró", ex); }
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
            string normalized = LinuxSdlVideoRenderer.NormalizeDropPath(path);
            if (Directory.Exists(normalized))
            {
                string full = Path.GetFullPath(normalized);
                string rootName = global::NOVORA.Control.NLControlFileStorage.SafeName(new DirectoryInfo(full).Name);
                var pending = new Stack<(string Path, int Depth)>(); pending.Push((full, 0));
                while (pending.TryPop(out var current))
                {
                    if (current.Depth > 30) throw new IOException("La carpeta supera la profundidad permitida.");
                    if ((File.GetAttributes(current.Path) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se envían enlaces de carpetas.");
                    foreach (string file in Directory.EnumerateFiles(current.Path))
                    {
                        string relative = Path.GetRelativePath(full, file).Replace('\\', '/');
                        string[] parts = relative.Split('/').Select(global::NOVORA.Control.NLControlFileStorage.SafeName).ToArray();
                        await SendFileAsync(file, "Otros/" + rootName + "/" + string.Join('/', parts), token).ConfigureAwait(false);
                    }
                    foreach (string child in Directory.EnumerateDirectories(current.Path)) pending.Push((child, current.Depth + 1));
                }
            }
            else if (File.Exists(normalized))
            {
                string full = Path.GetFullPath(normalized);
                string safeName = global::NOVORA.Control.NLControlFileStorage.SafeName(Path.GetFileName(full));
                string relativePath = global::NOVORA.Control.NLControlFileStorage.Category(safeName) + "/" + safeName;
                await SendFileAsync(full, relativePath, token).ConfigureAwait(false);
            }
            else throw new FileNotFoundException("El archivo o carpeta seleccionado no existe: " + normalized);
            Sent?.Invoke(normalized);
        }
        catch(OperationCanceledException) { Report("Envío cancelado."); throw; }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or InvalidDataException)
        {
            LinuxFileDiagnostics.Record("envío de " + Path.GetFileName(path), ex);
            Report("No se pudo enviar: " + LinuxFileDiagnostics.Describe(ex));
            throw;
        }
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
            string safeName = global::NOVORA.Control.NLControlFileStorage.SafeName(Path.GetFileName(path));
            Report($"Enviando {safeName} ({length:N0} bytes)…");
            var progress = new Progress<long>(bytes => Report($"Enviando {safeName}: {bytes:N0}/{length:N0} bytes"));
            await engine.SendAsync(id, new NLFileMetadataV2(safeName, length, relativePath), stream, progress, token).ConfigureAwait(false);
            Report($"Enviado: {safeName}; SHA-256 confirmado por Android.");
        }
        catch
        {
            if (engine.IsClosed) leases.CloseGeneration(Generation, new IOException("FILES send failed and channel closed."));
            else leases.CancelLocalBeforeBegin(id);
            throw;
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

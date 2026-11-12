using NOVORA.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Exchange de una sesión iniciada por USB. Delega en un <see cref="IVEExchangeUsbBackend"/>
/// y guarda lo recibido en la carpeta NOVORA indicada; solo confirma archivos ya completos.
/// </summary>
public sealed class VEExchangeUsbTransport : IVEExchangeTransport
{
    private readonly IVEExchangeUsbBackend _backend;
    private readonly string _serial;
    private readonly string _receiveRoot;
    private readonly Func<bool>? _canExchangeFiles;

    public VEExchangeUsbTransport(
        IVEExchangeUsbBackend backend,
        string serial,
        VEExchangeCapabilities capabilities,
        string receiveRoot,
        Func<bool>? canExchangeFiles = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _serial = VEExchangeUsbSerialGuard.Validate(serial);
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        ArgumentException.ThrowIfNullOrWhiteSpace(receiveRoot);
        _receiveRoot = Path.GetFullPath(receiveRoot);
        _canExchangeFiles = canExchangeFiles;
    }

    public VEExchangeRoute Route => VEExchangeRoute.UsbAppControl;

    public VEExchangeCapabilities Capabilities { get; }

    public async Task<VEExchangeResult> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!PrivacyAllowsFiles()) return Blocked();
        if (!Capabilities.SendFiles) return VEExchangeResult.FailVE("El envío de archivos no está habilitado.");
        if (paths.Count == 0) return VEExchangeResult.FailVE("No hay elementos para enviar.");

        long items = 0;
        long bytes = 0;
        string? destination = null;
        try
        {
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full))
                    bytes += new FileInfo(full).Length;
                else if (!Directory.Exists(full))
                    return VEExchangeResult.FailVE("El elemento local no existe.", full);

                destination = await _backend.PushAsync(_serial, full, cancellationToken).ConfigureAwait(false);
                items++;
            }

            return VEExchangeResult.OkVE(
                items == 1 ? "Elemento enviado a Android." : $"{items} elementos enviados a Android.",
                destination: destination,
                itemCount: items,
                bytes: bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible enviar a Android.", destination: destination, detail: ex.Message);
        }
    }

    public async Task<VEExchangeResult> PullFileAsync(string remotePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        if (!PrivacyAllowsFiles()) return Blocked();
        if (!Capabilities.ReceiveFiles) return VEExchangeResult.FailVE("La recepción de archivos no está habilitada.");

        Directory.CreateDirectory(_receiveRoot);
        string temporary = Path.Combine(_receiveRoot, ".novora-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await _backend.PullAsync(_serial, remotePath.Trim(), temporary, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(temporary))
                return VEExchangeResult.FailVE("La copia terminó pero el archivo local no apareció.", remotePath, temporary);

            string name = NLControlFileStorage.SafeName(VEExchangePath.GetFileNameVE(remotePath));
            string committed = NLControlFileStorage.Commit(temporary, _receiveRoot, name);
            return VEExchangeResult.OkVE(
                "Archivo recibido desde Android.",
                remotePath,
                committed,
                itemCount: 1,
                bytes: new FileInfo(committed).Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible recibir el archivo desde Android.", remotePath, temporary, ex.Message);
        }
        finally { DeleteQuietly(temporary); }
    }

    public async Task<VEExchangeResult> PullNovoraFolderAsync(CancellationToken cancellationToken)
    {
        if (!PrivacyAllowsFiles()) return Blocked();
        if (!Capabilities.ReceiveFolder) return VEExchangeResult.FailVE("La recepción de carpetas no está habilitada.");

        Directory.CreateDirectory(_receiveRoot);
        string temporary = Path.Combine(_receiveRoot, ".novora-recepcion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await _backend.PullFolderAsync(_serial, VEExchangePath.RootAndroidVE, temporary, cancellationToken).ConfigureAwait(false);

            string received = Directory.Exists(Path.Combine(temporary, "NOVORA")) ? Path.Combine(temporary, "NOVORA") : temporary;
            (long files, long bytes) = MeasureVE(received);
            int count = NLControlFileStorage.ImportDirectory(received, _receiveRoot);
            return count > 0
                ? VEExchangeResult.OkVE(
                    $"{count} archivo(s)/carpeta(s) recibidos y organizados por tipo.",
                    VEExchangePath.RootAndroidVE,
                    _receiveRoot,
                    itemCount: files,
                    bytes: bytes)
                : VEExchangeResult.FailVE("La copia terminó pero no aparecieron archivos locales.", VEExchangePath.RootAndroidVE, _receiveRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible recibir la carpeta NOVORA desde Android.", VEExchangePath.RootAndroidVE, temporary, ex.Message);
        }
        finally { DeleteDirectoryQuietly(temporary); }
    }

    public Task<VEExchangeApkInstallResult> InstallApkAsync(string androidPath, CancellationToken cancellationToken)
        => Task.FromResult(new VEExchangeApkInstallResult(
            VEExchangeApkInstallState.Failed,
            "La instalación de APK por USB todavía no está conectada."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool PrivacyAllowsFiles() => _canExchangeFiles?.Invoke() ?? true;

    private static VEExchangeResult Blocked()
        => VEExchangeResult.FailVE("PrivacyVE bloqueó la transferencia de archivos.");

    private static (long Files, long Bytes) MeasureVE(string directory)
    {
        long files = 0;
        long bytes = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files++;
            bytes += new FileInfo(path).Length;
        }

        return (files, bytes);
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Rechaza endpoints LAN como serial ADB en el adaptador USB.</summary>
internal static class VEExchangeUsbSerialGuard
{
    public static string Validate(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string trimmed = serial.Trim();
        if (trimmed.StartsWith("LAN:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Un endpoint LAN no es un serial ADB.", nameof(serial));
        return trimmed;
    }
}

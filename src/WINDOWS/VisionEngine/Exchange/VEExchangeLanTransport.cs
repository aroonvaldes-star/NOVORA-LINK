using NOVORA.Control;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Exchange de una sesión iniciada por Wi-Fi/LAN. Usa el canal FILES v2 ya autenticado
/// de esa sesión; nunca toma un serial ADB ni un endpoint LAN como identificador.
/// Las recepciones las inicia el teléfono: el PC no solicita archivos concretos por LAN.
/// </summary>
public sealed class VEExchangeLanTransport : IVEExchangeTransport
{
    private readonly IVEExchangeLanFiles? _files;
    private readonly Func<bool>? _canExchangeFiles;
    private int _disposed;

    public VEExchangeLanTransport(
        IVEExchangeLanFiles? files,
        VEExchangeCapabilities capabilities,
        Func<bool>? canExchangeFiles = null)
    {
        _files = files;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _canExchangeFiles = canExchangeFiles;
        if (_files is not null) _files.Received += OnReceived;
    }

    /// <summary>Recepciones confirmadas del teléfono, expresadas como resultado Exchange.</summary>
    public event Action<VEExchangeResult>? ReceivedVE;

    public VEExchangeRoute Route => VEExchangeRoute.NativeLan;

    public VEExchangeCapabilities Capabilities { get; }

    public async Task<VEExchangeResult> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!PrivacyAllowsFiles()) return Blocked();
        if (!Capabilities.SendFiles) return VEExchangeResult.FailVE("El envío de archivos no está habilitado.");
        if (_files is null) return VEExchangeResult.FailVE("El canal FILES LAN no está disponible.");
        if (paths.Count == 0) return VEExchangeResult.FailVE("No hay elementos para enviar.");

        try
        {
            int sent = await _files.SendPathsAsync(paths, cancellationToken).ConfigureAwait(false);
            return sent > 0
                ? VEExchangeResult.OkVE(
                    sent == 1 ? "Elemento enviado a Android." : $"{sent} elementos enviados a Android.",
                    itemCount: sent)
                : VEExchangeResult.FailVE("No se envió ningún archivo.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VEExchangeResult.FailVE("No fue posible enviar a Android por LAN.", detail: ex.Message);
        }
    }

    public Task<VEExchangeResult> PullFileAsync(string remotePath, CancellationToken cancellationToken)
        => Task.FromResult(VEExchangeResult.FailVE(
            "Por LAN el teléfono inicia la recepción. Envía el archivo desde Android.",
            remotePath));

    public Task<VEExchangeResult> PullNovoraFolderAsync(CancellationToken cancellationToken)
        => Task.FromResult(VEExchangeResult.FailVE(
            "Por LAN el teléfono inicia la recepción. Envía la carpeta desde Android."));

    public Task<VEExchangeApkInstallResult> InstallApkAsync(string androidPath, CancellationToken cancellationToken)
        => Task.FromResult(new VEExchangeApkInstallResult(
            VEExchangeApkInstallState.Failed,
            "La instalación de APK por LAN todavía no está conectada."));

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        if (_files is not null) _files.Received -= OnReceived;
        return ValueTask.CompletedTask;
    }

    private void OnReceived(NLFileTransferResultV2 result)
    {
        if (!Capabilities.ReceiveFiles || !PrivacyAllowsFiles() || Volatile.Read(ref _disposed) != 0) return;
        ReceivedVE?.Invoke(VEExchangeResult.OkVE(
            "Archivo recibido desde Android.",
            destination: result.Destination,
            itemCount: 1,
            bytes: result.Length));
    }

    private bool PrivacyAllowsFiles() => _canExchangeFiles?.Invoke() ?? true;

    private static VEExchangeResult Blocked()
        => VEExchangeResult.FailVE("PrivacyVE bloqueó la transferencia de archivos.");
}

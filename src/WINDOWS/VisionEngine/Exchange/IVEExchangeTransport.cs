namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Ruta por la que una sesión VisionEngine alcanza al teléfono.
/// Es un valor tipado: nunca se representa como serial ADB ni como texto de host.
/// </summary>
public enum VEExchangeRoute
{
    UsbAppControl = 1,
    NativeLan = 2
}

/// <summary>
/// Capacidades independientes. Habilitar una no implica ninguna otra.
/// </summary>
public sealed record VEExchangeCapabilities(
    bool Clipboard,
    bool SendFiles,
    bool ReceiveFiles,
    bool ReceiveFolder,
    bool DragDrop,
    bool InstallApk);

public enum VEExchangeApkInstallState
{
    Confirmed = 1,
    Denied = 2,
    Pending = 3,
    Failed = 4
}

/// <summary>
/// Resultado de instalar un APK ya transferido. Transferir y confirmar la instalación son operaciones distintas.
/// </summary>
public sealed record VEExchangeApkInstallResult(
    VEExchangeApkInstallState State,
    string Message)
{
    public bool InstalledVE => State == VEExchangeApkInstallState.Confirmed;
}

/// <summary>
/// Operaciones de Exchange expuestas por un transporte concreto (USB o NativeLan).
/// </summary>
public interface IVEExchangeTransport : IAsyncDisposable
{
    VEExchangeRoute Route { get; }

    VEExchangeCapabilities Capabilities { get; }

    Task<VEExchangeResult> SendPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);

    Task<VEExchangeResult> PullFileAsync(
        string remotePath,
        CancellationToken cancellationToken);

    Task<VEExchangeResult> PullNovoraFolderAsync(
        CancellationToken cancellationToken);

    Task<VEExchangeApkInstallResult> InstallApkAsync(
        string androidPath,
        CancellationToken cancellationToken);
}

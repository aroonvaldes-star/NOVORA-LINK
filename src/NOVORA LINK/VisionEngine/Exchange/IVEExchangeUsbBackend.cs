namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Puerto de operaciones ADB usado por el adaptador USB.
/// Permite probar el adaptador sin teléfono ni ADB real.
/// </summary>
public interface IVEExchangeUsbBackend
{
    /// <summary>Envía un archivo o carpeta local a Android y devuelve la ruta final en el teléfono.</summary>
    Task<string> PushAsync(string serial, string localPath, CancellationToken cancellationToken);

    /// <summary>Copia un archivo remoto a una ruta temporal local.</summary>
    Task PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken);

    /// <summary>Copia una carpeta remota a una carpeta temporal local.</summary>
    Task PullFolderAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken);
}

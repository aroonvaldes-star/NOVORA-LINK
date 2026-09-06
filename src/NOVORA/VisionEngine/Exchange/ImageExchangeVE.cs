namespace NOVORA.VisionEngine.Exchange;

public sealed class ImageExchangeVE
{
    private static readonly HashSet<string> ExtensionsVE = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".heic", ".heif" };
    private readonly FileExchangeVE _filesVE;
    public ImageExchangeVE(FileExchangeVE files) => _filesVE = files ?? throw new ArgumentNullException(nameof(files));

    public Task<ResultExchangeVE> PushAsync(string serial, string localPath, CancellationToken cancellationToken = default)
    {
        ValidateVE(localPath);
        return _filesVE.PushToDownloadsAsync(serial, localPath, scanMedia: true, cancellationToken: cancellationToken);
    }

    public Task<ResultExchangeVE> PullAsync(string serial, string remotePath, string localDestination, CancellationToken cancellationToken = default)
    {
        ValidateVE(remotePath);
        return _filesVE.PullFromAndroidAsync(serial, remotePath, localDestination, cancellationToken);
    }

    private static void ValidateVE(string path)
    {
        if (!ExtensionsVE.Contains(Path.GetExtension(path)))
            throw new NotSupportedException($"Formato de imagen VisionEngine no soportado: {Path.GetExtension(path)}.");
    }
}

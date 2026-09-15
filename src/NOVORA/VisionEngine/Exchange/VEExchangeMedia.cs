namespace NOVORA.VisionEngine.Exchange;

public sealed class VEExchangeMedia
{
    private static readonly HashSet<string> ExtensionsVE = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus" };
    private readonly VEExchangeFile _filesVE;
    public VEExchangeMedia(VEExchangeFile files) => _filesVE = files ?? throw new ArgumentNullException(nameof(files));

    public Task<VEExchangeResult> PushAsync(string serial, string localPath, CancellationToken cancellationToken = default)
    {
        ValidateVE(localPath);
        return _filesVE.PushToDownloadsAsync(serial, localPath, scanMedia: true, cancellationToken: cancellationToken);
    }

    public Task<VEExchangeResult> PullAsync(string serial, string remotePath, string localDestination, CancellationToken cancellationToken = default)
    {
        ValidateVE(remotePath);
        return _filesVE.PullFromAndroidAsync(serial, remotePath, localDestination, cancellationToken);
    }

    private static void ValidateVE(string path)
    {
        if (!ExtensionsVE.Contains(Path.GetExtension(path)))
            throw new NotSupportedException($"Formato multimedia VisionEngine no soportado: {Path.GetExtension(path)}.");
    }
}

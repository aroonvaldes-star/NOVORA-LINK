using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NOVORA.VisionEngine.Exchange;

/// <summary>
/// Cola de transferencia PC -> Android de VisionEngine.
///
/// Archivos/carpetas:
///
///     /sdcard/NOVORA/
///
/// APK:
///
///     instalación directa
///
/// Un solo worker evita lanzar múltiples adb push simultáneos
/// y reducir la estabilidad del stream de VisionEngine.
/// </summary>
internal sealed class TransferExchangeVE : IAsyncDisposable
{
    private const int QueueCapacityVE =
        16;

    private readonly Channel<TransferRequestVE> _queueVE;

    private readonly CancellationTokenSource _lifetimeCtsVE =
        new();

    private readonly Task _workerVE;
    private readonly Func<bool>? _canExchangeFilesVE;

    private bool _disposedVE;

    public TransferExchangeVE(Func<bool>? canExchangeFiles = null)
    {
        _canExchangeFilesVE = canExchangeFiles;
        _queueVE =
            Channel.CreateBounded<TransferRequestVE>(
                new BoundedChannelOptions(
                    QueueCapacityVE)
                {
                    SingleReader =
                        true,

                    SingleWriter =
                        false,

                    FullMode =
                        BoundedChannelFullMode.Wait,

                    AllowSynchronousContinuations =
                        false
                });

        _workerVE =
            Task.Run(
                WorkerLoopVE);
    }

    public event EventHandler<TransferStartedEventArgsVE>?
        TransferStartedVE;

    public event EventHandler<TransferCompletedEventArgsVE>?
        TransferCompletedVE;

    public event EventHandler<TransferFailedEventArgsVE>?
        TransferFailedVE;

    public async ValueTask QueueAsync(
        string serial,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedVE();

        if (!CanExchangeFilesVE())
            throw new InvalidOperationException("PrivacyVE bloqueó la transferencia de archivos.");

        ArgumentException.ThrowIfNullOrWhiteSpace(
            serial);

        ArgumentNullException.ThrowIfNull(
            paths);

        if (paths.Count == 0)
        {
            return;
        }

        List<string> validPaths =
            new(
                paths.Count);

        HashSet<string> uniquePaths =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        path);
            }
            catch
            {
                continue;
            }

            if (
                !File.Exists(fullPath) &&
                !Directory.Exists(fullPath))
            {
                continue;
            }

            if (!uniquePaths.Add(fullPath))
            {
                continue;
            }

            validPaths.Add(
                fullPath);
        }

        if (validPaths.Count == 0)
        {
            return;
        }

        TransferRequestVE request =
            new(
                serial,
                validPaths.ToArray());

        await _queueVE
            .Writer
            .WriteAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WorkerLoopVE()
    {
        CancellationToken cancellationToken =
            _lifetimeCtsVE.Token;

        try
        {
            await foreach (
                TransferRequestVE request
                in _queueVE.Reader.ReadAllAsync(
                    cancellationToken))
            {
                await ProcessRequestVE(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessRequestVE(
        TransferRequestVE request,
        CancellationToken cancellationToken)
    {
        if (!CanExchangeFilesVE())
        {
            foreach (string path in request.PathsVE)
            {
                TransferFailedVE?.Invoke(
                    this,
                    new TransferFailedEventArgsVE(
                        path,
                        "PRIVACY",
                        "PrivacyVE bloqueó la transferencia.",
                        TimeSpan.Zero));
            }
            return;
        }

        foreach (string path in request.PathsVE)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            await ProcessSinglePathVE(
                    request.SerialVE,
                    path,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ProcessSinglePathVE(
        string serial,
        string localPath,
        CancellationToken cancellationToken)
    {
        RequestExchangeVE request =
            RequestExchangeVE.FromPathVE(
                localPath);

        if (request.KindVE == KindExchangeVE.Apk)
        {
            await InstallApkVE(
                    serial,
                    request.ValueVE,
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        if (
            request.KindVE != KindExchangeVE.File &&
            request.KindVE != KindExchangeVE.Directory)
        {
            return;
        }

        await PushVE(
                serial,
                request.ValueVE,
                request.KindVE ==
                    KindExchangeVE.Directory,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InstallApkVE(
        string serial,
        string apkPath,
        CancellationToken cancellationToken)
    {
        string name =
            Path.GetFileName(
                apkPath);

        TransferStartedVE?.Invoke(
            this,
            new TransferStartedEventArgsVE(
                apkPath,
                "APK INSTALL",
                false));

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        try
        {
            await ApkExchangeVE
                .InstallAsync(
                    serial,
                    apkPath,
                    cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            TransferCompletedVE?.Invoke(
                this,
                new TransferCompletedEventArgsVE(
                    apkPath,
                    $"INSTALLED:{name}",
                    stopwatch.Elapsed));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            TransferFailedVE?.Invoke(
                this,
                new TransferFailedEventArgsVE(
                    apkPath,
                    "APK INSTALL",
                    ex.Message,
                    stopwatch.Elapsed));
        }
    }

    private async Task PushVE(
        string serial,
        string localPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        string safeName =
            PathExchangeVE.GetSafeNameVE(
                localPath);

        string androidDestination =
            isDirectory
                ? PathExchangeVE.BuildDestinationFromNameVE(
                    safeName)
                : PathExchangeVE.BuildCategorizedDestinationFromNameVE(
                    safeName);

        TransferStartedVE?.Invoke(
            this,
            new TransferStartedEventArgsVE(
                localPath,
                androidDestination,
                isDirectory));

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        try
        {
            await EnsureRootVE(
                    serial,
                    cancellationToken)
                .ConfigureAwait(false);

            ResultAdbExchangeVE result =
                await AdbExchangeVE
                    .RunAsync(
                        serial,
                        cancellationToken,
                        "push",
                        localPath,
                        androidDestination)
                    .ConfigureAwait(false);

            stopwatch.Stop();

            if (!result.SuccessVE)
            {
                string error =
                    string.IsNullOrWhiteSpace(
                        result.ErrorVE)
                        ? result.OutputVE
                        : result.ErrorVE;

                TransferFailedVE?.Invoke(
                    this,
                    new TransferFailedEventArgsVE(
                        localPath,
                        androidDestination,
                        error.Trim(),
                        stopwatch.Elapsed));

                return;
            }

            if (!isDirectory)
            {
                await ScanMediaVE(
                        serial,
                        androidDestination,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            TransferCompletedVE?.Invoke(
                this,
                new TransferCompletedEventArgsVE(
                    localPath,
                    androidDestination,
                    stopwatch.Elapsed));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            TransferFailedVE?.Invoke(
                this,
                new TransferFailedEventArgsVE(
                    localPath,
                    androidDestination,
                    ex.Message,
                    stopwatch.Elapsed));
        }
    }

    private static async Task EnsureRootVE(
        string serial,
        CancellationToken cancellationToken)
    {
        ResultAdbExchangeVE result =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    PathExchangeVE.BuildEnsureRootCommandVE())
                .ConfigureAwait(false);

        if (!result.SuccessVE)
        {
            throw new InvalidOperationException(
                $"No se pudo crear {PathExchangeVE.RootAndroidVE}.");
        }
    }

    private static async Task ScanMediaVE(
        string serial,
        string androidPath,
        CancellationToken cancellationToken)
    {
        string fileUri =
            $"file://{androidPath}";

        string rootUri =
            $"file://{PathExchangeVE.RootAndroidVE}";

        string folder =
            PathExchangeVE.NormalizeAndroidPathVE(
                Path.GetDirectoryName(androidPath)
                    ?.Replace('\\', '/') ??
                PathExchangeVE.RootAndroidVE);

        string folderUri =
            $"file://{folder}";

        _ =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    "am",
                    "broadcast",
                    "-a",
                    "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
                    "-d",
                    fileUri)
                .ConfigureAwait(false);

        _ =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    "am",
                    "broadcast",
                    "-a",
                    "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
                    "-d",
                    folderUri)
                .ConfigureAwait(false);

        _ =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    "am",
                    "broadcast",
                    "-a",
                    "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
                    "-d",
                    rootUri)
                .ConfigureAwait(false);

        _ =
            await AdbExchangeVE
                .RunAsync(
                    serial,
                    cancellationToken,
                    "shell",
                    "cmd",
                    "media",
                    "scan-file",
                    androidPath)
                .ConfigureAwait(false);
    }

    private bool CanExchangeFilesVE()
        => _canExchangeFilesVE?.Invoke() ?? true;

    private void ThrowIfDisposedVE()
    {
        ObjectDisposedException.ThrowIf(
            _disposedVE,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedVE)
        {
            return;
        }

        _disposedVE =
            true;

        _queueVE
            .Writer
            .TryComplete();

        _lifetimeCtsVE
            .Cancel();

        try
        {
            await _workerVE
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _lifetimeCtsVE
            .Dispose();
    }
}

internal sealed class TransferRequestVE
{
    public TransferRequestVE(
        string serial,
        string[] paths)
    {
        SerialVE =
            serial;

        PathsVE =
            paths;
    }

    public string SerialVE
    {
        get;
    }

    public string[] PathsVE
    {
        get;
    }
}

public sealed class TransferStartedEventArgsVE : EventArgs
{
    public TransferStartedEventArgsVE(
        string localPath,
        string androidPath,
        bool isDirectory)
    {
        LocalPathVE =
            localPath;

        AndroidPathVE =
            androidPath;

        IsDirectoryVE =
            isDirectory;
    }

    public string LocalPathVE
    {
        get;
    }

    public string AndroidPathVE
    {
        get;
    }

    public bool IsDirectoryVE
    {
        get;
    }
}

public sealed class TransferCompletedEventArgsVE : EventArgs
{
    public TransferCompletedEventArgsVE(
        string localPath,
        string androidPath,
        TimeSpan elapsed)
    {
        LocalPathVE =
            localPath;

        AndroidPathVE =
            androidPath;

        ElapsedVE =
            elapsed;
    }

    public string LocalPathVE
    {
        get;
    }

    public string AndroidPathVE
    {
        get;
    }

    public TimeSpan ElapsedVE
    {
        get;
    }
}

public sealed class TransferFailedEventArgsVE : EventArgs
{
    public TransferFailedEventArgsVE(
        string localPath,
        string androidPath,
        string error,
        TimeSpan elapsed)
    {
        LocalPathVE =
            localPath;

        AndroidPathVE =
            androidPath;

        ErrorVE =
            error;

        ElapsedVE =
            elapsed;
    }

    public string LocalPathVE
    {
        get;
    }

    public string AndroidPathVE
    {
        get;
    }

    public string ErrorVE
    {
        get;
    }

    public TimeSpan ElapsedVE
    {
        get;
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NOVORA.Discovery;

public sealed class NLDiscoveryADBTrackDevices :
    IAsyncDisposable
{
    private readonly string _adbPathNV;

    private CancellationTokenSource? _lifetimeCtsNV;
    private Task? _workerNV;
    private Process? _processNV;
    private string? _lastSnapshotNV;
    private bool _disposedNV;

    public event Action<object?, string>? DevicesChangedNV;

    public event Action<object?, string>? RecoveryNV;

    public NLDiscoveryADBTrackDevices(
        string adbPathNV)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            adbPathNV);

        _adbPathNV =
            Path.GetFullPath(
                adbPathNV);
    }

    public bool IsRunningNV =>
        _workerNV is
        {
            IsCompleted: false
        };

    public Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedNV();

        if (IsRunningNV)
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(
                _adbPathNV))
        {
            throw new FileNotFoundException(
                "ADB no existe para DiscoveryEngine.",
                _adbPathNV);
        }

        _lifetimeCtsNV?.Dispose();
        _lifetimeCtsNV =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        _workerNV =
            RunLoopNVAsync(
                _lifetimeCtsNV.Token);

        return Task.CompletedTask;
    }

    private async Task RunLoopNVAsync(
        CancellationToken cancellationToken)
    {
        TimeSpan recoveryDelayNV =
            TimeSpan.FromMilliseconds(
                500);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunProcessOnceNVAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                recoveryDelayNV =
                    TimeSpan.FromMilliseconds(
                        500);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RecoveryNV?.Invoke(
                    this,
                    ex.Message);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(
                        recoveryDelayNV,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            double nextMilliseconds =
                Math.Min(
                    recoveryDelayNV.TotalMilliseconds * 2d,
                    8000d);

            recoveryDelayNV =
                TimeSpan.FromMilliseconds(
                    nextMilliseconds);
        }
    }

    private async Task RunProcessOnceNVAsync(
        CancellationToken cancellationToken)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    _adbPathNV,
                UseShellExecute =
                    false,
                CreateNoWindow =
                    true,
                RedirectStandardOutput =
                    true,
                RedirectStandardError =
                    true
            };

        startInfo.ArgumentList.Add(
            "track-devices");

        using var process =
            new Process
            {
                StartInfo =
                    startInfo,
                EnableRaisingEvents =
                    true
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "ADB track-devices no pudo iniciar.");
        }

        _processNV =
            process;

        Task stderrDrainNV =
            process.StandardError
                .ReadToEndAsync(
                    cancellationToken);

        Stream outputNV =
            process.StandardOutput.BaseStream;

        byte[] lengthBytesNV =
            new byte[4];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await ReadFramePartNVAsync(
                        outputNV,
                        lengthBytesNV,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    break;
                }

                if (!int.TryParse(
                        Encoding.ASCII.GetString(lengthBytesNV),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out int payloadLengthNV))
                {
                    throw new InvalidDataException(
                        "ADB track-devices entregó una longitud de mensaje inválida.");
                }

                byte[] payloadNV =
                    new byte[payloadLengthNV];

                if (!await ReadFramePartNVAsync(
                        outputNV,
                        payloadNV,
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new EndOfStreamException(
                        "ADB track-devices terminó antes del mensaje anunciado.");
                }

                PublishSnapshotNV(
                    Encoding.UTF8.GetString(payloadNV));
            }

            await process
                .WaitForExitAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            await stderrDrainNV
                .ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"ADB track-devices terminó inesperadamente con código {process.ExitCode}.");
            }
        }
        finally
        {
            if (ReferenceEquals(
                    _processNV,
                    process))
            {
                _processNV =
                    null;
            }
        }
    }

    private static async Task<bool> ReadFramePartNVAsync(
        Stream inputNV,
        byte[] destinationNV,
        CancellationToken cancellationToken)
    {
        int offsetNV = 0;

        while (offsetNV < destinationNV.Length)
        {
            int countNV =
                await inputNV.ReadAsync(
                        destinationNV.AsMemory(offsetNV),
                        cancellationToken)
                    .ConfigureAwait(false);

            if (countNV == 0)
            {
                if (offsetNV == 0)
                {
                    return false;
                }

                throw new EndOfStreamException(
                    "ADB track-devices terminó en medio de un mensaje.");
            }

            offsetNV += countNV;
        }

        return true;
    }

    private void PublishSnapshotNV(
        string snapshotNV)
    {
        string normalizedNV =
            snapshotNV
                .Replace(
                    "\r",
                    string.Empty,
                    StringComparison.Ordinal)
                .Trim();

        if (string.Equals(
                normalizedNV,
                _lastSnapshotNV,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastSnapshotNV =
            normalizedNV;

        DevicesChangedNV?.Invoke(
            this,
            normalizedNV);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? ctsNV =
            _lifetimeCtsNV;

        Task? workerNV =
            _workerNV;

        _lifetimeCtsNV =
            null;

        _workerNV =
            null;

        ctsNV?.Cancel();

        try
        {
            if (_processNV is
                {
                    HasExited: false
                } processNV)
            {
                processNV.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
        }

        if (workerNV is not null)
        {
            try
            {
                await workerNV
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        ctsNV?.Dispose();
    }

    private void ThrowIfDisposedNV()
    {
        ObjectDisposedException.ThrowIf(
            _disposedNV,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposedNV)
        {
            return;
        }

        _disposedNV =
            true;

        await StopAsync()
            .ConfigureAwait(false);
    }
}

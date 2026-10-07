using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NOVORA.Discovery;

public sealed class NLDiscoveryADBTrackDevices :
    IAsyncDisposable
{
    private readonly string _adbPathNV;
    private readonly object _lifecycleGateNV = new();
    private readonly NLDiscoveryProcessJobNV? _processJobNV;

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

        _processJobNV =
            NLDiscoveryProcessJobNV.TryCreateNV();
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

        lock (_lifecycleGateNV)
        {
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
        }

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

        _processJobNV?.AssignNV(
            process);

        lock (_lifecycleGateNV)
        {
            _processNV =
                process;
        }

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
            await StopOwnedProcessNVAsync(
                    process)
                .ConfigureAwait(false);

            lock (_lifecycleGateNV)
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
    }

    private static async Task StopOwnedProcessNVAsync(
        Process processNV)
    {
        try
        {
            if (!processNV.HasExited)
            {
                processNV.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            await processNV
                .WaitForExitAsync()
                .WaitAsync(
                    TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch
        {
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
        CancellationTokenSource? ctsNV;
        Task? workerNV;
        Process? processNV;

        lock (_lifecycleGateNV)
        {
            ctsNV =
                _lifetimeCtsNV;

            workerNV =
                _workerNV;

            processNV =
                _processNV;

            _lifetimeCtsNV =
                null;

            _workerNV =
                null;
        }

        ctsNV?.Cancel();

        try
        {
            if (processNV is
                {
                    HasExited: false
                })
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

        _processJobNV?.Dispose();
    }

    private sealed class NLDiscoveryProcessJobNV : IDisposable
    {
        private const uint KillOnJobCloseNV = 0x00002000;
        private const int ExtendedLimitInformationNV = 9;

        private readonly SafeFileHandle _handleNV;

        private NLDiscoveryProcessJobNV(
            SafeFileHandle handleNV)
        {
            _handleNV =
                handleNV;
        }

        public static NLDiscoveryProcessJobNV? TryCreateNV()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            SafeFileHandle handleNV =
                CreateJobObjectNV(
                    IntPtr.Zero,
                    null);

            if (handleNV.IsInvalid)
            {
                handleNV.Dispose();
                return null;
            }

            var informationNV =
                new JobObjectExtendedLimitInformationNV
                {
                    BasicLimitInformation =
                        new JobObjectBasicLimitInformationNV
                        {
                            LimitFlags =
                                KillOnJobCloseNV
                        }
                };

            int lengthNV =
                Marshal.SizeOf<JobObjectExtendedLimitInformationNV>();

            IntPtr bufferNV =
                Marshal.AllocHGlobal(
                    lengthNV);

            try
            {
                Marshal.StructureToPtr(
                    informationNV,
                    bufferNV,
                    fDeleteOld: false);

                if (!SetInformationJobObjectNV(
                        handleNV,
                        ExtendedLimitInformationNV,
                        bufferNV,
                        (uint)lengthNV))
                {
                    handleNV.Dispose();
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(
                    bufferNV);
            }

            return new NLDiscoveryProcessJobNV(
                handleNV);
        }

        public void AssignNV(
            Process processNV)
        {
            if (_handleNV.IsInvalid ||
                _handleNV.IsClosed)
            {
                return;
            }

            _ = AssignProcessToJobObjectNV(
                _handleNV,
                processNV.Handle);
        }

        public void Dispose()
        {
            _handleNV.Dispose();
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateJobObjectW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateJobObjectNV(
            IntPtr jobAttributesNV,
            string? nameNV);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "SetInformationJobObject",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObjectNV(
            SafeFileHandle jobNV,
            int informationClassNV,
            IntPtr informationNV,
            uint informationLengthNV);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "AssignProcessToJobObject",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObjectNV(
            SafeFileHandle jobNV,
            IntPtr processNV);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformationNV
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCountersNV
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformationNV
        {
            public JobObjectBasicLimitInformationNV BasicLimitInformation;
            public IoCountersNV IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}

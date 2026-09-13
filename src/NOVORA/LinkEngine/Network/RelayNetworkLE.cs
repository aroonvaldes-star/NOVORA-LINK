using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVORA.LinkEngine.Network;

public sealed class RelayNetworkLE :
    IAsyncDisposable
{
    public const int PortLE =
        27184;

    public const string ExecutableLE =
        "NOVORA.LinkEngine.Relay.exe";

    private const int MaximumStoredWarningsLE =
        250;

    private readonly SemaphoreSlim _gate =
        new(1, 1);

    private readonly ConcurrentQueue<string> _warningLines =
        new();

    private Process? _process;

    private long _totalLogLines;
    private long _warningCount;
    private long _errorCount;

    private long _clientBufferFullCount;
    private long _udpDropCount;

    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastWarningAtUtc;

    private string? _lastWarning;

    private bool _disposed;

    public event EventHandler? ExitedLE;

    public string DiagnosticsLogPathLE =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NOVORA",
            "Logs",
            "LinkEngine-Relay-Diagnostics.log");

    public bool IsRunningLE
    {
        get
        {
            try
            {
                return
                    _process is not null &&
                    !_process.HasExited &&
                    IsPortListeningLE();
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedLE();

        await _gate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (IsRunningLE)
            {
                return;
            }

            StopProcessLE();

            KillStaleProcessesLE();

            ResetDiagnosticsLE();

            string executable =
                ResolveExecutableLE();

            if (!File.Exists(executable))
            {
                throw new FileNotFoundException(
                    "NOVORA.LinkEngine.Relay.exe no existe.",
                    executable);
            }

            var info =
                new ProcessStartInfo
                {
                    FileName =
                        executable,

                    WorkingDirectory =
                        Path.GetDirectoryName(
                            executable) ??
                        AppContext.BaseDirectory,

                    UseShellExecute =
                        false,

                    CreateNoWindow =
                        true,

                    RedirectStandardOutput =
                        true,

                    RedirectStandardError =
                        true,

                    StandardOutputEncoding =
                        Encoding.UTF8,

                    StandardErrorEncoding =
                        Encoding.UTF8
                };

            Process process =
                new()
                {
                    StartInfo =
                        info,

                    EnableRaisingEvents =
                        true
                };

            process.OutputDataReceived +=
                RelayOutputReceivedLE;

            process.ErrorDataReceived +=
                RelayErrorReceivedLE;

            process.Exited +=
                RelayProcessExitedLE;

            if (!process.Start())
            {
                process.Dispose();

                throw new InvalidOperationException(
                    "No fue posible iniciar LinkEngine Relay.");
            }

            _process =
                process;

            _startedAtUtc =
                DateTimeOffset.UtcNow;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DateTimeOffset deadline =
                DateTimeOffset.UtcNow +
                TimeSpan.FromSeconds(5);

            while (DateTimeOffset.UtcNow <
                   deadline)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (process.HasExited)
                {
                    int code =
                        process.ExitCode;

                    StopProcessLE();

                    PersistDiagnosticsLE();

                    throw new InvalidOperationException(
                        $"LinkEngine Relay terminó con código {code}.");
                }

                if (IsPortListeningLE())
                {
                    return;
                }

                await Task.Delay(
                        100,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            StopProcessLE();

            PersistDiagnosticsLE();

            throw new TimeoutException(
                $"LinkEngine Relay no abrió 127.0.0.1:{PortLE}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsRunningLE)
        {
            return Task.CompletedTask;
        }

        return StartAsync(
            cancellationToken);
    }

    public RelayDiagnosticsSnapshotLE
        GetDiagnosticsSnapshotLE()
    {
        return new RelayDiagnosticsSnapshotLE(
            IsRunningLE,
            Interlocked.Read(
                ref _totalLogLines),
            Interlocked.Read(
                ref _warningCount),
            Interlocked.Read(
                ref _errorCount),
            Interlocked.Read(
                ref _clientBufferFullCount),
            Interlocked.Read(
                ref _udpDropCount),
            _startedAtUtc,
            _lastWarningAtUtc,
            _lastWarning);
    }

    public IReadOnlyList<string>
        GetRecentWarningsLE()
    {
        return _warningLines
            .ToArray();
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _gate
            .WaitAsync(
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            StopProcessLE();

            PersistDiagnosticsLE();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RelayProcessExitedLE(
        object? sender,
        EventArgs e)
    {
        if (_disposed ||
            sender is not Process process ||
            !ReferenceEquals(
                _process,
                process))
        {
            return;
        }

        ExitedLE?.Invoke(
            this,
            EventArgs.Empty);
    }

    private void RelayOutputReceivedLE(
        object sender,
        DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(
                args.Data))
        {
            return;
        }

        RegisterRelayLineLE(
            args.Data,
            forceError:
                false);
    }

    private void RelayErrorReceivedLE(
        object sender,
        DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(
                args.Data))
        {
            return;
        }

        RegisterRelayLineLE(
            args.Data,
            forceError:
                true);
    }

    private void RegisterRelayLineLE(
        string line,
        bool forceError)
    {
        Interlocked.Increment(
            ref _totalLogLines);

        bool clientBufferFull =
            line.Contains(
                "Client buffer full",
                StringComparison.OrdinalIgnoreCase);

        bool udpDrop =
            line.Contains(
                "Cannot send to client, drop packet",
                StringComparison.OrdinalIgnoreCase);

        bool warning =
            clientBufferFull ||
            udpDrop ||
            line.Contains(
                " WARN ",
                StringComparison.OrdinalIgnoreCase);

        bool error =
            forceError ||
            line.Contains(
                " ERROR ",
                StringComparison.OrdinalIgnoreCase) ||
            line.Contains(
                "failed",
                StringComparison.OrdinalIgnoreCase);

        if (clientBufferFull)
        {
            Interlocked.Increment(
                ref _clientBufferFullCount);
        }

        if (udpDrop)
        {
            Interlocked.Increment(
                ref _udpDropCount);
        }

        if (warning)
        {
            Interlocked.Increment(
                ref _warningCount);
        }

        if (error)
        {
            Interlocked.Increment(
                ref _errorCount);
        }

        if (!warning &&
            !error)
        {
            return;
        }

        string diagnosticLine =
            $"{DateTimeOffset.Now:O} {line}";

        _warningLines.Enqueue(
            diagnosticLine);

        while (_warningLines.Count >
               MaximumStoredWarningsLE)
        {
            _warningLines.TryDequeue(
                out _);
        }

        _lastWarning =
            line;

        _lastWarningAtUtc =
            DateTimeOffset.UtcNow;
    }

    private void ResetDiagnosticsLE()
    {
        while (_warningLines.TryDequeue(
                   out _))
        {
        }

        Interlocked.Exchange(
            ref _totalLogLines,
            0);

        Interlocked.Exchange(
            ref _warningCount,
            0);

        Interlocked.Exchange(
            ref _errorCount,
            0);

        Interlocked.Exchange(
            ref _clientBufferFullCount,
            0);

        Interlocked.Exchange(
            ref _udpDropCount,
            0);

        _startedAtUtc =
            null;

        _lastWarningAtUtc =
            null;

        _lastWarning =
            null;

        try
        {
            if (File.Exists(
                    DiagnosticsLogPathLE))
            {
                File.Delete(
                    DiagnosticsLogPathLE);
            }
        }
        catch
        {
        }
    }

    private void PersistDiagnosticsLE()
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(
                    DiagnosticsLogPathLE);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            RelayDiagnosticsSnapshotLE snapshot =
                GetDiagnosticsSnapshotLE();

            var builder =
                new StringBuilder();

            builder.AppendLine(
                "============================================================");

            builder.AppendLine(
                " NOVORA LINKENGINE - RELAY DIAGNOSTICS");

            builder.AppendLine(
                "============================================================");

            builder.AppendLine();

            builder.AppendLine(
                $"Generated ................. {DateTimeOffset.Now:O}");

            builder.AppendLine(
                $"Started UTC ............... {snapshot.StartedAtUtc?.ToString("O") ?? "-"}");

            builder.AppendLine(
                $"Relay running ............. {snapshot.IsRunning}");

            builder.AppendLine();

            builder.AppendLine(
                $"Log lines ................. {snapshot.TotalLogLines}");

            builder.AppendLine(
                $"Warnings .................. {snapshot.WarningCount}");

            builder.AppendLine(
                $"Errors .................... {snapshot.ErrorCount}");

            builder.AppendLine();

            builder.AppendLine(
                $"CLIENT BUFFER FULL ........ {snapshot.ClientBufferFullCount}");

            builder.AppendLine(
                $"UDP DROP PACKET ........... {snapshot.UdpDropCount}");

            builder.AppendLine();

            builder.AppendLine(
                $"Last warning UTC .......... {snapshot.LastWarningAtUtc?.ToString("O") ?? "-"}");

            builder.AppendLine(
                $"Last warning .............. {snapshot.LastWarning ?? "-"}");

            builder.AppendLine();

            builder.AppendLine(
                "================ RECENT WARNINGS / ERRORS =================");

            foreach (string warning in
                     _warningLines.ToArray())
            {
                builder.AppendLine(
                    warning);
            }

            builder.AppendLine();

            File.WriteAllText(
                DiagnosticsLogPathLE,
                builder.ToString(),
                Encoding.UTF8);
        }
        catch
        {
            /*
             * Las métricas nunca deben tumbar
             * el Data Plane.
             */
        }
    }

    private static bool IsPortListeningLE()
    {
        try
        {
            return
                IPGlobalProperties
                    .GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(
                        endpoint =>
                            endpoint.Address
                                .Equals(
                                    System.Net.IPAddress.Loopback) &&
                            endpoint.Port ==
                                PortLE);
        }
        catch
        {
            return false;
        }
    }

    private static void KillStaleProcessesLE()
    {
        Process[] processes =
            Process.GetProcessesByName(
                "NOVORA.LinkEngine.Relay");

        foreach (Process process in
                 processes)
        {
            try
            {
                process.Kill(
                    entireProcessTree:
                        true);

                process.WaitForExit(
                    1500);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void StopProcessLE()
    {
        Process? process =
            _process;

        _process =
            null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree:
                        true);

                process.WaitForExit(
                    2000);
            }
        }
        catch
        {
        }

        try
        {
            process.CancelOutputRead();
        }
        catch
        {
        }

        try
        {
            process.CancelErrorRead();
        }
        catch
        {
        }

        process.OutputDataReceived -=
            RelayOutputReceivedLE;

        process.ErrorDataReceived -=
            RelayErrorReceivedLE;

        process.Exited -=
            RelayProcessExitedLE;

        process.Dispose();
    }

    private static string ResolveExecutableLE()
    {
        string outputCandidate =
            Path.Combine(
                AppContext.BaseDirectory,
                "Tools",
                "LinkEngine",
                ExecutableLE);

        if (File.Exists(
                outputCandidate))
        {
            return outputCandidate;
        }

        DirectoryInfo? current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        for (
            int level = 0;
            level < 10 &&
            current is not null;
            level++)
        {
            string direct =
                Path.Combine(
                    current.FullName,
                    "Tools",
                    "LinkEngine",
                    ExecutableLE);

            if (File.Exists(
                    direct))
            {
                return direct;
            }

            string source =
                Path.Combine(
                    current.FullName,
                    "src",
                    "NOVORA",
                    "Tools",
                    "LinkEngine",
                    ExecutableLE);

            if (File.Exists(
                    source))
            {
                return source;
            }

            current =
                current.Parent;
        }

        return outputCandidate;
    }

    private void ThrowIfDisposedLE()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(
                CancellationToken.None)
            .ConfigureAwait(false);

        _disposed =
            true;

        _gate.Dispose();
    }
}

public sealed record RelayDiagnosticsSnapshotLE(
    bool IsRunning,
    long TotalLogLines,
    long WarningCount,
    long ErrorCount,
    long ClientBufferFullCount,
    long UdpDropCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastWarningAtUtc,
    string? LastWarning);
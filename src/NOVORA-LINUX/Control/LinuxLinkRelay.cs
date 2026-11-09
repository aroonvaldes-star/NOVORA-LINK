using System.Diagnostics;
using System.Globalization;
using System.Text;
using NOVORA.LinkEngine.Network;
using NOVORA.LinkEngine.Recovery;

namespace NOVORA.Linux.Control;

/// <summary>
/// Owns the native Linux relay process on loopback DATA port 27184. The relay
/// authenticates every local session with its own token; USB reuses the
/// offered control token and LAN shares it only with the TLS gateway.
/// Like Windows LERecoveryManager, an unexpected exit restarts the relay with the
/// same token (backoff 1→8 s, 8 consecutive failures); Android's DATA channel then
/// reconnects to the same port without a new offer. The relay's one-second
/// <c>LE_METRIC</c> counters are kept as the session traffic snapshot.
/// </summary>
internal sealed class LinuxLinkRelay : IAsyncDisposable
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private readonly LERecoveryPolicy _policy;
    private readonly Func<string, CancellationToken, Process>? _startOverride;
    private readonly object _processGate = new();
    private Process? _process;
    private string? _token;
    private CancellationTokenSource? _recovery;
    private Task _recoveryTask = Task.CompletedTask;
    private LERelayTrafficSnapshot? _traffic;
    private volatile bool _stopping;
    private bool _gateHeld;
    private int _restarts;

    public LinuxLinkRelay() : this(LERecoveryPolicy.DefaultLE) { }

    internal LinuxLinkRelay(LERecoveryPolicy policy, Func<string, CancellationToken, Process>? startOverride = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _startOverride = startOverride;
    }

    /// <summary>Raised only after recovery gave up; the session is then in error.</summary>
    public event EventHandler<string>? ExitedUnexpectedly;
    /// <summary>Raised when the relay died and a restart is scheduled (attempt number).</summary>
    public event EventHandler<int>? Recovering;
    /// <summary>Raised when a restarted relay reported LE_READY again.</summary>
    public event EventHandler? Recovered;

    public bool IsRunning { get { lock (_processGate) return _process is { HasExited: false }; } }
    public int Restarts => Volatile.Read(ref _restarts);
    public LERelayTrafficSnapshot? TrafficSnapshot => Volatile.Read(ref _traffic);

    public async Task StartAsync(string token, CancellationToken cancellationToken)
    {
        if (_token is not null)
            throw new InvalidOperationException("El relay nativo de LinkEngine ya está iniciado.");
        if (!LinuxLinkProtocol.IsToken(token))
            throw new ArgumentException("Token del relay inválido.", nameof(token));
        if (!ProcessGate.Wait(0))
            throw new InvalidOperationException(
                "Otra sesión LinkEngine ya está activa en esta PC; deténla antes de cambiar de ruta.");

        _gateHeld = true;
        _stopping = false;
        try
        {
            Process process = await LaunchAsync(token, cancellationToken);
            _token = token;
            _recovery = new CancellationTokenSource();
            Attach(process);
        }
        catch
        {
            ReleaseGate();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        CancellationTokenSource? recovery = _recovery;
        _recovery = null;
        recovery?.Cancel();
        try { await _recoveryTask; } catch (OperationCanceledException) { }
        recovery?.Dispose();
        Process? process;
        lock (_processGate)
        {
            process = _process;
            _process = null;
        }
        _token = null;
        Volatile.Write(ref _traffic, null);
        try
        {
            if (process is not null)
                await StopProcessAsync(process);
        }
        finally
        {
            ReleaseGate();
        }
    }

    private void Attach(Process process)
    {
        lock (_processGate) _process = process;
        process.Exited += (_, _) => OnProcessExited(process);
        // An exit that raced the subscription must still be recovered.
        if (process.HasExited) OnProcessExited(process);
    }

    private void OnProcessExited(Process process)
    {
        lock (_processGate)
        {
            if (_stopping || !ReferenceEquals(_process, process)) return;
            _process = null;
            CancellationToken token = _recovery?.Token ?? new CancellationToken(true);
            // Chained, never dropped: a restarted relay may die while the previous recovery returns.
            _recoveryTask = _recoveryTask.ContinueWith(_ => RecoverAsync(process, token),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task RecoverAsync(Process exited, CancellationToken cancellationToken)
    {
        DateTime startedAt;
        try { startedAt = exited.StartTime.ToUniversalTime(); }
        catch (InvalidOperationException) { startedAt = DateTime.UtcNow; }
        exited.Dispose();
        Volatile.Write(ref _traffic, null);
        // A relay that ran past the recovery window failed fresh: restart the backoff.
        int attempt = DateTime.UtcNow - startedAt > _policy.RecoveryTimeout ? 1 : Volatile.Read(ref _restarts) + 1;
        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            if (attempt > _policy.MaximumConsecutiveFailures)
            {
                ExitedUnexpectedly?.Invoke(this,
                    $"El proceso nativo de LinkEngine terminó {_policy.MaximumConsecutiveFailures} veces seguidas; " +
                    "detén e inicia LinkEngine desde AppControl.");
                return;
            }
            Volatile.Write(ref _restarts, attempt);
            Recovering?.Invoke(this, attempt);
            try
            {
                await Task.Delay(_policy.GetReconnectDelayLE(attempt), cancellationToken);
                string token = _token ?? throw new OperationCanceledException();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_policy.RecoveryTimeout);
                Process process = await LaunchAsync(token, timeout.Token);
                if (cancellationToken.IsCancellationRequested || _stopping)
                {
                    await StopProcessAsync(process);
                    return;
                }
                Recovered?.Invoke(this, EventArgs.Empty);
                Attach(process);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stopping) { return; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or
                                       System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                Debug.WriteLine("LinkEngine relay restart failed: " + ex.Message);
                attempt++;
            }
        }
    }

    private void ReleaseGate()
    {
        if (!_gateHeld)
            return;
        _gateHeld = false;
        ProcessGate.Release();
    }

    private async Task<Process> LaunchAsync(string token, CancellationToken cancellationToken)
    {
        Process process = _startOverride?.Invoke(token, cancellationToken) ?? StartProcess(token);
        try
        {
            await WaitForReadyAsync(process, cancellationToken);
            return process;
        }
        catch
        {
            await StopProcessAsync(process);
            throw;
        }
    }

    private static Process StartProcess(string token)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Tools", "LinkEngine", "novora-linkengine-relay");
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el relay nativo Linux de LinkEngine.", path);

        var start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.Environment["NOVORA_RELAY_TOKEN"] = token;
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("No se pudo iniciar el relay nativo de LinkEngine.");
        return process;
    }

    /// <summary>Same format and validation as Windows LENetworkRelay.RegisterRelayLineLE.</summary>
    internal static bool TryParseMetric(string line, DateTimeOffset now, out LERelayTrafficSnapshot? snapshot)
    {
        snapshot = null;
        if (!line.StartsWith("LE_METRIC ", StringComparison.Ordinal)) return false;
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 5 &&
            fields[1].StartsWith("tx=", StringComparison.Ordinal) &&
            fields[2].StartsWith("rx=", StringComparison.Ordinal) &&
            fields[3].StartsWith("txp=", StringComparison.Ordinal) &&
            fields[4].StartsWith("rxp=", StringComparison.Ordinal) &&
            long.TryParse(fields[1][3..], NumberStyles.None, CultureInfo.InvariantCulture, out long tx) &&
            long.TryParse(fields[2][3..], NumberStyles.None, CultureInfo.InvariantCulture, out long rx) &&
            long.TryParse(fields[3][4..], NumberStyles.None, CultureInfo.InvariantCulture, out long txPackets) &&
            long.TryParse(fields[4][4..], NumberStyles.None, CultureInfo.InvariantCulture, out long rxPackets))
            snapshot = new LERelayTrafficSnapshot(now, tx, rx, txPackets, rxPackets);
        return true;
    }

    private async Task WaitForReadyAsync(Process relay, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new StringBuilder();
        object gate = new();
        relay.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (e.Data.StartsWith("LE_READY ", StringComparison.Ordinal))
                ready.TrySetResult();
            else if (TryParseMetric(e.Data, DateTimeOffset.UtcNow, out LERelayTrafficSnapshot? snapshot))
            {
                bool current;
                lock (_processGate) current = ReferenceEquals(_process, relay);
                if (snapshot is not null && current) Volatile.Write(ref _traffic, snapshot);
            }
        };
        relay.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lock (gate)
            {
                if (diagnostics.Length < 512)
                    diagnostics.Append(e.Data.AsSpan(0, Math.Min(e.Data.Length, 512 - diagnostics.Length)));
            }
        };
        relay.BeginOutputReadLine();
        relay.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Task exited = relay.WaitForExitAsync(timeout.Token);
        Task completed = await Task.WhenAny(ready.Task, exited);
        if (completed == exited)
        {
            await exited;
            lock (gate)
                throw new InvalidOperationException($"El relay de LinkEngine terminó al iniciar (código {relay.ExitCode}). {diagnostics}");
        }
        await ready.Task.WaitAsync(timeout.Token);
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

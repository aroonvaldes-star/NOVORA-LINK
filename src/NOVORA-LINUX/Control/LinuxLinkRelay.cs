using System.Diagnostics;
using System.Text;

namespace NOVORA.Linux.Control;

/// <summary>
/// Owns the native Linux relay process on loopback DATA port 27184. The relay
/// authenticates every local session with its own token; USB reuses the
/// offered control token and LAN shares it only with the TLS gateway.
/// </summary>
internal sealed class LinuxLinkRelay : IAsyncDisposable
{
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private Process? _process;
    private bool _stopping;
    private bool _gateHeld;

    public event EventHandler<string>? ExitedUnexpectedly;

    public async Task StartAsync(string token, CancellationToken cancellationToken)
    {
        if (_process is not null)
            throw new InvalidOperationException("El relay nativo de LinkEngine ya está iniciado.");
        if (!LinuxLinkProtocol.IsToken(token))
            throw new ArgumentException("Token del relay inválido.", nameof(token));
        if (!ProcessGate.Wait(0))
            throw new InvalidOperationException(
                "Otra sesión LinkEngine ya está activa en esta PC; deténla antes de cambiar de ruta.");

        _gateHeld = true;
        _stopping = false;
        Process? process = null;
        try
        {
            process = StartProcess(token);
            await WaitForReadyAsync(process, cancellationToken);
            process.Exited += (_, _) =>
            {
                if (!_stopping)
                    ExitedUnexpectedly?.Invoke(this, "El proceso nativo de LinkEngine terminó inesperadamente.");
            };
            _process = process;
        }
        catch
        {
            try
            {
                if (process is not null)
                    await StopProcessAsync(process);
            }
            finally
            {
                ReleaseGate();
            }
            throw;
        }
    }

    public async Task StopAsync()
    {
        Process? process = _process;
        if (process is null)
        {
            ReleaseGate();
            return;
        }
        _stopping = true;
        _process = null;
        try
        {
            await StopProcessAsync(process);
        }
        finally
        {
            ReleaseGate();
        }
    }

    private void ReleaseGate()
    {
        if (!_gateHeld)
            return;
        _gateHeld = false;
        ProcessGate.Release();
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

    private static async Task WaitForReadyAsync(Process relay, CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new StringBuilder();
        object gate = new();
        relay.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("LE_READY ", StringComparison.Ordinal) == true)
                ready.TrySetResult();
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
